using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    private const uint ProtocolMagic = 0x55424E58;
    private const ushort ProtocolVersion = 2;
    private const ulong ManifestMagic = 0x31464E4D504E4558;
    private const uint ManifestFormat = 1;
    private const uint ManifestStateValid = 0x56414C44;
    private const uint ApplicationImageId = 0x41505031;
    private const uint LoaderAbiVersion = 4;
    private const uint AppBase = 0x08020000;
    private const uint AppCapacity = 0x0015E000;
    private const int HeaderBytes = 32;
    private const int ManifestBytes = 128;
    private const int PayloadBytes = 480;
    private const byte EnterBootloaderRequest = 0x46;
    private const string AppHardwareId = "vid_1209&pid_b386";
    private const string BootHardwareId = "vid_1209&pid_b387";
    private static string? selectedBootSerial;
    private static string? selectedUid;
    private static readonly Guid DeviceInterfaceGuid =
        new("8F5A4A7B-0AA6-4F8E-A14F-A2F7B3C6AA4E");

    private enum Opcode : ushort
    {
        Info = 1,
        Begin = 2,
        Data = 3,
        Commit = 4,
        Abort = 5,
        Boot = 6,
        UsbService = 7,
        MscReadOnly = 8,
        MscWritable = 9,
        Download = 10,
        RomDfu = 11,
    }

    private sealed record Package(byte[] Image, uint ImageCrc, byte[] BuildId);
    private readonly record struct Response(uint Status, uint Arg0, uint Arg1);

    public static int Main(string[] args)
    {
        try
        {
            // --boot-serial is update-only; --uid also pins diagnostics/mode
            // control. Obtain the UID from the intended physical target;
            // never fall back to another B386/B387 device if it disappears.
            while (args.Length >= 3 && (args[^2] == "--boot-serial" || args[^2] == "--uid"))
            {
                var selector = args[^2];
                var value = args[^1];
                if (value.Length != 24 || !value.All(char.IsAsciiHexDigit))
                    throw new ArgumentException("UID must be the exact 24-digit MCU UID.");
                if (selector == "--boot-serial")
                {
                    if (selectedBootSerial is not null || selectedUid is not null)
                        throw new ArgumentException("Specify exactly one of --uid or --boot-serial.");
                    selectedBootSerial = value;
                }
                else
                {
                    if (selectedUid is not null || selectedBootSerial is not null)
                        throw new ArgumentException("Specify exactly one of --uid or --boot-serial.");
                    selectedUid = value;
                }
                args = args[..^2];
                if (selector == "--boot-serial" && args[0].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--boot-serial is supported only with a firmware package update.");
            }
            if (args.Length == 1 && args[0] == "--sensor-self-test") { SensorControl.SelfTest(); return 0; }
            if (args.Length == 1 && args[0] == "--selector-self-test") { SelectorSelfTest(); return 0; }
            if (args.Length == 1 && (args[0] == "--stlink-check" || args[0] == "--stlink-recover-pipes"))
                return StLinkProbe.Check(args[0] == "--stlink-recover-pipes");
            if (args.Length == 1 && args[0] == "--self-test") { MscControl.SelfTest(); return 0; }
            if (args.Length == 1 && args[0].Equals("--list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var path in WinUsbDevice.ListPaths(DeviceInterfaceGuid))
                    Console.WriteLine(path);
                foreach (var path in MscControl.ListPaths()) Console.WriteLine(path);
                return 0;
            }
            var attached = WinUsbDevice.ListPaths(DeviceInterfaceGuid).Count(p =>
                p.Contains(AppHardwareId, StringComparison.OrdinalIgnoreCase) || p.Contains(BootHardwareId, StringComparison.OrdinalIgnoreCase)) + MscControl.ListPaths().Count;
            if (attached > 1 && selectedBootSerial is null && selectedUid is null) throw new IOException("More than one WILDX USB device is connected; supply --uid <24-digit MCU UID> before mode control or updating.");
            if (args.Length == 1 && args[0] == "--sensors")
            {
                SensorControl.Read(FindPath(AppHardwareId) ?? throw new IOException("GET_SENSORS requires the B386 WinUSB service."));
                return 0;
            }
            if (args.Length == 1 && args[0] == "--mode-info")
            {
                PrintUnifiedMode(FindPath(AppHardwareId) ?? throw new IOException("Mode information requires the B386 WinUSB service."));
                return 0;
            }
            if (args.Length == 1 && args[0] == "--boot-info")
            {
                using var core = WinUsbDevice.Open(FindPath(BootHardwareId) ??
                    throw new IOException("Read-only boot information requires the selected B387 recovery device."));
                core.SetTimeouts(3000);
                var bootInfo = Transfer(core, Opcode.Info, 1, 0, 0, []);
                EnsureOk(bootInfo, "INFO");
                Console.WriteLine($"Loader ABI={bootInfo.Arg0 >> 16}, app_valid={(bootInfo.Arg0 & 1) != 0}, update_active={(bootInfo.Arg0 & 2) != 0}, usb_service_valid={(bootInfo.Arg0 & 4) != 0}, app_capacity={bootInfo.Arg1}");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--msc-status")
            {
                if (selectedUid is not null)
                    throw new NotSupportedException("MSC status cannot be UID-pinned; use the ABI-4 WinUSB status path instead.");
                using var msc = MscControl.Open(); msc.PrintStatus(); return 0;
            }
            if (args.Length == 1 && args[0] == "--dfu")
            {
                RequestRomDfu();
                return 0;
            }
            if (args.Length == 1 && (args[0] == "--usb" || args[0] == "--stream" || args[0] == "--download" || args[0] == "--sd" || args[0] == "--msc" || args[0] == "--msc-rw" || args[0] == "--recovery"))
            {
                if (MscControl.ListPaths().Count != 0)
                {
                    if (selectedUid is not null)
                        throw new NotSupportedException("MSC mode control cannot be UID-pinned and is not used for ABI-4 unified mode management.");
                    using var msc = MscControl.Open();
                    msc.Switch(args[0] switch { "--usb" => 1, "--sd" => 2, "--recovery" => 3, "--msc-rw" => 4, _ => (byte)5 });
                    Console.WriteLine("MSC acknowledged mode switch."); return 0;
                }
                var service = FindPath(AppHardwareId);
                if (service is not null)
                {
                    if (args[0] is "--usb" or "--stream" or "--download" or "--sd" or "--recovery")
                    {
                        RequestUnifiedMode(service, UnifiedMode(args[0]));
                    }
                    else
                    {
                        throw new NotSupportedException("ABI-4 unified service does not expose MSC mode control.");
                    }
                    Console.WriteLine("Mode switch requested.");
                    return 0;
                }
                using var core = WinUsbDevice.Open(FindPath(BootHardwareId) ??
                    throw new InvalidOperationException("No USB mode found. An SD app requires an explicit idle-mode request via firmware control or SWD."));
                if (args[0] == "--recovery") { Console.WriteLine("Recovery is already active."); return 0; }
                var modeOpcode = args[0] switch { "--usb" or "--stream" => Opcode.UsbService, "--download" => Opcode.Download, "--sd" => Opcode.Boot, "--msc" => Opcode.MscReadOnly, _ => Opcode.MscWritable };
                EnsureOk(Transfer(core, modeOpcode, 1, 0, 0, []), "MODE");
                Console.WriteLine("Mode switch requested.");
                return 0;
            }
            if (args.Length is < 1 or > 2)
            {
                Console.Error.WriteLine("Usage: XeNpFirmwareUpdater <firmware.xenp> [--stay] [--uid <24-digit MCU UID>]");
                Console.Error.WriteLine("       XeNpFirmwareUpdater <firmware.xenp> [--stay] --boot-serial <24-digit MCU UID>");
                Console.Error.WriteLine("       XeNpFirmwareUpdater --list");
                Console.Error.WriteLine("       XeNpFirmwareUpdater --boot-info [--uid <24-digit MCU UID>] (read-only)");
                Console.Error.WriteLine("       XeNpFirmwareUpdater --sensors | --mode-info | --sensor-self-test");
                Console.Error.WriteLine("       XeNpFirmwareUpdater --usb | --stream | --download | --sd | --recovery [--uid <24-digit MCU UID>]");
                Console.Error.WriteLine("       XeNpFirmwareUpdater --dfu --uid <24-digit MCU UID> (mode change only; confirm STM32 BOOTLOADER in STM32CubeProgrammer)");
                return 2;
            }
            var stay = args.Length == 2 && args[1].Equals("--stay", StringComparison.OrdinalIgnoreCase);
            if (args.Length == 2 && !stay)
                throw new ArgumentException($"Unknown option: {args[1]}");

            var package = LoadPackage(args[0]);
            var bootPath = FindPath(BootHardwareId);
            if (bootPath is null && selectedBootSerial is not null)
                bootPath = WaitForPath(BootHardwareId, TimeSpan.FromSeconds(12));
            if (bootPath is null && selectedBootSerial is not null)
                throw new IOException("The explicitly selected recovery device is not present; no other device was touched.");
            if (bootPath is null)
            {
                Console.WriteLine("Requesting USB recovery mode...");
                if (selectedUid is null && MscControl.ListPaths().Count != 0)
                {
                    using var msc = MscControl.Open(); msc.Switch(3);
                }
                else
                {
                    var appPath = FindPath(AppHardwareId) ?? throw new InvalidOperationException("No WILDX USB service or bootloader was found.");
                    RequestUnifiedMode(appPath, 4);
                }
                bootPath = WaitForPath(BootHardwareId, TimeSpan.FromSeconds(12)) ??
                    throw new TimeoutException("The USB recovery device did not enumerate.");
            }

            using var device = WinUsbDevice.Open(bootPath);
            device.SetTimeouts(30_000);
            var sequence = 1U;
            var info = Transfer(device, Opcode.Info, sequence++, 0, 0, []);
            EnsureOk(info, "INFO");
            if ((info.Arg0 >> 16) < LoaderAbiVersion || info.Arg1 != AppCapacity)
                throw new InvalidOperationException("Install the split-image loader via SWD first; the connected loader does not protect the resident USB partition.");
            Console.WriteLine($"Updating {package.Image.Length:N0} bytes...");
            EnsureOk(
                Transfer(device, Opcode.Begin, sequence++, (uint)package.Image.Length, package.ImageCrc, package.BuildId),
                "BEGIN");

            var offset = 0;
            while (offset < package.Image.Length)
            {
                var count = Math.Min(PayloadBytes, package.Image.Length - offset);
                var response = Transfer(
                    device,
                    Opcode.Data,
                    sequence++,
                    (uint)offset,
                    0,
                    package.Image.AsSpan(offset, count));
                EnsureOk(response, $"DATA at 0x{offset:X}");
                if (response.Arg0 != (uint)(offset + count))
                    throw new IOException($"Device acknowledged {response.Arg0} bytes, expected {offset + count}.");
                offset += count;
                if (offset == package.Image.Length || offset % (48 * 1024) == 0)
                    Console.WriteLine($"  {offset * 100.0 / package.Image.Length,6:F1}%  {offset:N0}/{package.Image.Length:N0}");
            }

            EnsureOk(Transfer(device, Opcode.Commit, sequence++, 0, 0, []), "COMMIT");
            Console.WriteLine($"Committed CRC32 0x{package.ImageCrc:X8}.");
            if (!stay)
            {
                EnsureOk(Transfer(device, Opcode.Boot, sequence, 0, 0, []), "BOOT");
                Console.WriteLine("Application boot requested.");
            }
            else
            {
                Console.WriteLine("Recovery mode left active (--stay).");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Update failed: {exception.Message}");
            return 1;
        }
    }

    private static Package LoadPackage(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length <= ManifestBytes)
            throw new InvalidDataException("Update package is truncated.");
        var manifest = bytes.AsSpan(0, ManifestBytes);
        if (BinaryPrimitives.ReadUInt64LittleEndian(manifest) != ManifestMagic)
            throw new InvalidDataException("Update manifest magic is invalid.");
        if (Crc32(manifest[..124]) != BinaryPrimitives.ReadUInt32LittleEndian(manifest[124..]))
            throw new InvalidDataException("Update manifest CRC is invalid.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(manifest[8..]) != ManifestFormat ||
            BinaryPrimitives.ReadUInt32LittleEndian(manifest[12..]) != ManifestStateValid ||
            BinaryPrimitives.ReadUInt32LittleEndian(manifest[16..]) != ApplicationImageId ||
            BinaryPrimitives.ReadUInt32LittleEndian(manifest[80..]) > LoaderAbiVersion)
            throw new InvalidDataException("Update manifest is not compatible with this loader ABI.");
        var imageBase = BinaryPrimitives.ReadUInt32LittleEndian(manifest[24..]);
        var imageSize = BinaryPrimitives.ReadUInt32LittleEndian(manifest[28..]);
        var imageCrc = BinaryPrimitives.ReadUInt32LittleEndian(manifest[32..]);
        if (imageBase != AppBase || imageSize is < 8 or > AppCapacity || bytes.Length != ManifestBytes + imageSize)
            throw new InvalidDataException("Update image violates the XE_NP flash contract.");
        var image = bytes.AsSpan(ManifestBytes, checked((int)imageSize)).ToArray();
        if (Crc32(image) != imageCrc)
            throw new InvalidDataException("Update image CRC does not match its manifest.");
        var vectorSp = BinaryPrimitives.ReadUInt32LittleEndian(image);
        var vectorReset = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(4));
        var resetAddress = vectorReset & ~1U;
        var stackInMainSram = vectorSp is > 0x20000000 and <= 0x20270000;
        var stackInSram4 = vectorSp is > 0x28000000 and <= 0x28004000;
        if ((!stackInMainSram && !stackInSram4) || (vectorSp & 7) != 0 ||
            (vectorReset & 1) == 0 || resetAddress < AppBase || resetAddress >= AppBase + imageSize)
            throw new InvalidDataException("Update image has invalid Cortex-M33 vectors.");
        return new Package(image, imageCrc, manifest.Slice(44, 32).ToArray());
    }

    private static void RequestRecovery(string path, byte request = EnterBootloaderRequest)
    {
        using var device = WinUsbDevice.Open(path);
        var setup = new Native.WinUsbSetupPacket
        {
            RequestType = 0x40,
            Request = request,
            Value = 0,
            Index = 0,
            Length = 0,
        };
        if (!Native.WinUsb_ControlTransfer(device.Interface, setup, [], 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Bootloader control request failed");
    }

    private static void RequestRomDfu()
    {
        if (selectedUid is null)
            throw new ArgumentException("ROM DFU requires --uid <24-digit MCU UID>; no target substitution is allowed.");
        var bootPath = FindPath(BootHardwareId);
        if (bootPath is null)
        {
            var service = FindPath(AppHardwareId) ??
                throw new IOException("The selected MCU UID has no B386 USB service or B387 recovery interface.");
            RequestUnifiedMode(service, UnifiedMode("--recovery"));
            bootPath = WaitForPath(BootHardwareId, TimeSpan.FromSeconds(12)) ??
                throw new TimeoutException("The selected MCU UID did not enumerate its B387 recovery loader. Outcome unknown; no retry or other target was used.");
        }
        using var recovery = WinUsbDevice.Open(bootPath);
        recovery.SetTimeouts(3000);
        var info = Transfer(recovery, Opcode.Info, 1, 0, 0, []);
        EnsureOk(info, "INFO");
        RequireDfuRecovery(info.Arg0);
        EnsureOk(Transfer(recovery, Opcode.RomDfu, 2, 0, 0, []), "ROM DFU");
        // Keep the acknowledged handle alive across the loader's deferred reset.
        Thread.Sleep(150);
        Console.WriteLine("ROM DFU mode request acknowledged for MCU UID " + selectedUid + ". Confirm STM32 BOOTLOADER in STM32CubeProgrammer; USB disconnect alone does not confirm ROM DFU. No firmware was written.");
    }

    private static void RequireDfuRecovery(uint info)
    {
        if ((info >> 16) != LoaderAbiVersion || (info & 2U) != 0 || (info & 8U) == 0)
            throw new IOException("Idle ABI-4 recovery loader with ROM DFU capability required; active updates are not interrupted and no ROM DFU request was sent.");
    }

    private static void RequestUnifiedMode(string path, ushort mode)
    {
        using var device = WinUsbDevice.Open(path);
        var query = new Native.WinUsbSetupPacket { RequestType=0xC0, Request=0x6E, Length=32 };
        var state = new byte[32];
        if (!Native.WinUsb_ControlTransfer(device.Interface, query, state, 32, out var received, IntPtr.Zero) || received != 32 ||
            BinaryPrimitives.ReadUInt32LittleEndian(state) != 0x3253504EU || BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(4)) != LoaderAbiVersion ||
            BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(16)) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(20)) != 0)
            throw new IOException("Idle V2 ABI-4 USB service with no pending transition required; no mode change sent.");
        RequireV2Identity(state,path);
        var setup = new Native.WinUsbSetupPacket
        {
            RequestType = 0x40,
            Request = 0x6F,
            Value = mode,
            Index = 0,
            Length = 0,
        };
        if (!Native.WinUsb_ControlTransfer(device.Interface, setup, [], 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unified USB mode request failed (requires ABI-4 unified resident service).");
        /* The resident service arms the reset only after it observes the EP0
         * status ACK, then deliberately holds the interface for 50 ms before
         * reset.  Do not dispose the WinUSB handle immediately: Windows can
         * issue a new SETUP/reset while tearing it down, which correctly
         * cancels a not-yet-acknowledged transition on the device. */
        Thread.Sleep(150);
    }

    private static void PrintUnifiedMode(string path)
    {
        using var device = WinUsbDevice.Open(path);
        var setup = new Native.WinUsbSetupPacket
        {
            RequestType = 0xC0,
            Request = 0x6E,
            Value = 0,
            Index = 0,
            Length = 32,
        };
        var bytes = new byte[32];
        var ok = Native.WinUsb_ControlTransfer(device.Interface, setup, bytes, (uint)bytes.Length, out var received, IntPtr.Zero);
        if (!ok || received != bytes.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw new IOException($"Unified USB mode-info request failed: ok={ok}, bytes={received}, win32={error} (requires ABI-4 unified resident service).");
        }
        var words = Enumerable.Range(0, 8).Select(index => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4, 4))).ToArray();
        if (words[0] != 0x3253504EU)
            throw new InvalidDataException($"Unexpected unified-mode signature 0x{words[0]:X8}.");
        RequireV2Identity(bytes,path);
        Console.WriteLine($"USB mode: personality={words[2]}, capability_mask=0x{words[3]:X}, busy={words[4]}, pending={words[5]}, loader_abi={words[1]}");
        Console.WriteLine($"UID words: 0x{words[6]:X8}, folded=0x{words[7]:X8}");
    }

    private static ushort UnifiedMode(string option) => option switch
    {
        "--usb" or "--stream" => 2,
        "--download" => 3,
        "--sd" => 1,
        "--recovery" => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

    private static void SelectorSelfTest()
    {
        const string uid = "001B0023434250062039314E";
        if (!HasRealMcuUid("\\\\?\\usb#vid_1209&pid_b386#" + uid + "#{guid}") ||
            !HasRealMcuUid("\\\\?\\usb#vid_1209&pid_b386#npv2-" + uid + "#{guid}") ||
            HasRealMcuUid("\\\\?\\usb#vid_1209&pid_b386#npv1-" + uid + "#{guid}") ||
            HasRealMcuUid("\\\\?\\usb#vid_1209&pid_b386#" + new string('0',24) + "#{guid}") ||
            HasRealMcuUid("\\\\?\\usb#vid_1209&pid_b386#XENP0004#{guid}") ||
            UnifiedMode("--sd") != 1 || UnifiedMode("--stream") != 2 ||
            UnifiedMode("--download") != 3 || UnifiedMode("--recovery") != 4)
            throw new InvalidOperationException("UID selector or unified mode map self-test failed.");
        Console.WriteLine("PASS: exact MCU UID selector and ABI-4 unified mode mapping.");
        var state=new byte[32];BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(4),LoaderAbiVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(24),Convert.ToUInt32(uid[..8],16));
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(28),Convert.ToUInt32(uid[8..16],16)^Convert.ToUInt32(uid[16..24],16));
        string path="\\\\?\\usb#vid_1209&pid_b386#npv2-"+uid+"#{guid}";
        RequireV2Identity(state,path);state[28]^=1;bool refused=false;
        try{RequireV2Identity(state,path);}catch(IOException){refused=true;}
        if(!refused)throw new InvalidOperationException("Mismatched V2 MCU UID admitted.");
        Console.WriteLine("PASS: V2 mode/status replies are bound to the selected MCU UID.");
    }

    private static Response Transfer(
        WinUsbDevice device,
        Opcode opcode,
        uint sequence,
        uint arg0,
        uint arg1,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length > PayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload));
        var request = new byte[HeaderBytes + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(request, ProtocolMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6), (ushort)opcode);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), arg0);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16), arg1);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(20), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(24), Crc32(request.AsSpan(0, 24)));
        payload.CopyTo(request.AsSpan(HeaderBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(28), Crc32(payload));
        device.Write(request);
        var response = device.Read(HeaderBytes);
        if (response.Length != HeaderBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(response) != ProtocolMagic ||
            BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(4)) != ProtocolVersion ||
            BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(6)) != ((ushort)opcode | 0x8000) ||
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(8)) != sequence ||
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(24)) != Crc32(response.AsSpan(0, 24)) ||
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(28)) != Crc32(response.AsSpan(0, 28)))
            throw new InvalidDataException("Bootloader returned an invalid response header.");
        return new Response(
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(12)),
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(16)),
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(20)));
    }

    private static void EnsureOk(Response response, string operation)
    {
        if (response.Status != 0)
            throw new IOException($"{operation} returned boot status {response.Status}.");
    }

    private static string? McuUid(string path)
    {
        // USB hardware IDs use the device serial between the second and third
        // '#'. Refuse the historical XENP0004 placeholder: mode/update control
        // must identify a physical MCU, never a generic fallback personality.
        var parts = path.Split('#');
        if (parts.Length < 3) return null;
        var serial = parts[2];
        if (serial.StartsWith("npv2-", StringComparison.OrdinalIgnoreCase)) serial = serial[5..];
        return serial.Length == 24 && serial.All(char.IsAsciiHexDigit) && serial != new string('0',24) &&
            !serial.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase) ? serial : null;
    }

    private static bool HasRealMcuUid(string path) => McuUid(path) is not null;

    private static void RequireV2Identity(byte[] state,string path)
    {
        string uid=McuUid(path)??throw new IOException("Exact V2 MCU UID required.");
        if(BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(4))!=LoaderAbiVersion ||
            BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(24))!=Convert.ToUInt32(uid[..8],16) ||
            BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(28))!=(Convert.ToUInt32(uid[8..16],16)^Convert.ToUInt32(uid[16..24],16)))
            throw new IOException("V2 ABI/MCU UID response does not match the selected physical target.");
    }

    private static string? FindPath(string hardwareId) =>
        WinUsbDevice.ListPaths(DeviceInterfaceGuid)
            .SingleOrDefault(path => path.Contains(hardwareId, StringComparison.OrdinalIgnoreCase) &&
                HasRealMcuUid(path) &&
                (selectedUid is null || string.Equals(McuUid(path),selectedUid,StringComparison.OrdinalIgnoreCase)) &&
                (selectedBootSerial is null || (hardwareId == BootHardwareId &&
                    string.Equals(McuUid(path),selectedBootSerial,StringComparison.OrdinalIgnoreCase))));

    private static string? WaitForPath(string hardwareId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var path = FindPath(hardwareId);
            if (path is not null)
                return path;
            Thread.Sleep(200);
        } while (DateTime.UtcNow < deadline);
        return null;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFU;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((uint)-(int)(crc & 1) & 0xEDB88320U);
        }
        return crc ^ 0xFFFFFFFFU;
    }
}

internal sealed class WinUsbDevice : IDisposable
{
    private readonly SafeFileHandle file;
    public IntPtr Interface { get; private set; }
    private readonly byte inPipe;
    private readonly byte outPipe;

    private WinUsbDevice(SafeFileHandle file, IntPtr usb, byte inPipe, byte outPipe)
    {
        this.file = file;
        Interface = usb;
        this.inPipe = inPipe;
        this.outPipe = outPipe;
    }

    public static WinUsbDevice Open(string path)
    {
        var file = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite, Native.ShareRead | Native.ShareWrite,
            IntPtr.Zero, Native.OpenExisting, Native.FileAttributeNormal | Native.FileFlagOverlapped, IntPtr.Zero);
        if (file.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile failed");
        if (!Native.WinUsb_Initialize(file, out var usb))
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw new Win32Exception(error, $"WinUsb_Initialize failed ({error})");
        }
        byte inPipe = 0, outPipe = 0;
        try
        {
            if (!Native.WinUsb_QueryInterfaceSettings(usb, 0, out var descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings failed");
            for (byte index = 0; index < descriptor.NumEndpoints; index++)
            {
                if (!Native.WinUsb_QueryPipe(usb, 0, index, out var pipe))
                    continue;
                if (pipe.PipeType == 2 && (pipe.PipeId & 0x80) != 0) inPipe = pipe.PipeId;
                if (pipe.PipeType == 2 && (pipe.PipeId & 0x80) == 0) outPipe = pipe.PipeId;
            }
            if (inPipe == 0 || outPipe == 0)
                throw new InvalidOperationException("Bootloader bulk endpoints were not found.");
            return new WinUsbDevice(file, usb, inPipe, outPipe);
        }
        catch
        {
            Native.WinUsb_Free(usb);
            file.Dispose();
            throw;
        }
    }

    public void SetTimeouts(uint milliseconds)
    {
        Native.WinUsb_SetPipePolicy(Interface, inPipe, Native.PipeTransferTimeout, sizeof(uint), ref milliseconds);
        Native.WinUsb_SetPipePolicy(Interface, outPipe, Native.PipeTransferTimeout, sizeof(uint), ref milliseconds);
    }

    public void Write(byte[] bytes)
    {
        if (!Native.WinUsb_WritePipe(Interface, outPipe, bytes, (uint)bytes.Length, out var written, IntPtr.Zero) || written != bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_WritePipe failed");
    }

    public byte[] Read(int count)
    {
        var bytes = new byte[count];
        if (!Native.WinUsb_ReadPipe(Interface, inPipe, bytes, (uint)bytes.Length, out var read, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_ReadPipe failed");
        return read == bytes.Length ? bytes : bytes.AsSpan(0, checked((int)read)).ToArray();
    }

    public static IReadOnlyList<string> ListPaths(Guid interfaceGuid)
    {
        var paths = new List<string>();
        var info = Native.SetupDiGetClassDevs(ref interfaceGuid, null, IntPtr.Zero, Native.Present | Native.DeviceInterface);
        if (info == Native.InvalidHandle) return paths;
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new Native.DeviceInterfaceData { Size = Marshal.SizeOf<Native.DeviceInterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref interfaceGuid, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == Native.NoMoreItems) break;
                    continue;
                }
                // A mode switch can leave an inactive B386 interface beside
                // the live B387 interface on one present device. Enumerating
                // it as another board falsely blocks the multi-device guard.
                // SPINT_ACTIVE is 0x1; removed/inactive interfaces are not I/O targets.
                if ((data.Flags & 1U) == 0U) continue;
                Native.SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (Native.SetupDiGetDeviceInterfaceDetail(info, ref data, detail, required, out _, IntPtr.Zero))
                        paths.Add(Marshal.PtrToStringUni(detail + 4) ?? string.Empty);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(info); }
        return paths;
    }

    public void Dispose()
    {
        if (Interface != IntPtr.Zero) Native.WinUsb_Free(Interface);
        Interface = IntPtr.Zero;
        file.Dispose();
    }
}

internal static class Native
{
    public const uint Present = 0x2, DeviceInterface = 0x10, NoMoreItems = 259;
    public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    public const uint ShareRead = 1, ShareWrite = 2, OpenExisting = 3, FileAttributeNormal = 0x80;
    public const uint FileFlagOverlapped = 0x40000000;
    public const uint PipeTransferTimeout = 3;
    public static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)] public struct DeviceInterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] public struct UsbInterfaceDescriptor { public byte Length, DescriptorType, InterfaceNumber, AlternateSetting, NumEndpoints, InterfaceClass, InterfaceSubClass, InterfaceProtocol, Interface; }
    [StructLayout(LayoutKind.Sequential)] public struct WinUsbPipeInformation { public int PipeType; public byte PipeId; public ushort MaximumPacketSize; public byte Interval; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct WinUsbSetupPacket { public byte RequestType, Request; public ushort Value, Index, Length; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr info, IntPtr device, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info, ref DeviceInterfaceData data, IntPtr detail, int detailSize, out int required, IntPtr deviceInfo);
    [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_Initialize(SafeFileHandle file, out IntPtr usb);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_Free(IntPtr usb);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_QueryInterfaceSettings(IntPtr usb, byte setting, out UsbInterfaceDescriptor descriptor);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_QueryPipe(IntPtr usb, byte setting, byte index, out WinUsbPipeInformation pipe);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_SetPipePolicy(IntPtr usb, byte pipe, uint policy, uint length, ref uint value);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_WritePipe(IntPtr usb, byte pipe, byte[] buffer, uint length, out uint written, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_ReadPipe(IntPtr usb, byte pipe, byte[] buffer, uint length, out uint read, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)] public static extern bool WinUsb_ControlTransfer(IntPtr usb, WinUsbSetupPacket setup, byte[] buffer, uint length, out uint transferred, IntPtr overlapped);
}
