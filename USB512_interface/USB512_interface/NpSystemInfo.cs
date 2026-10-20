using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CE32_console;
using Microsoft.Win32.SafeHandles;

namespace USB512_interface
{
    internal sealed class NpSystemInfo
    {
        internal const string NotReported = "Not reported by firmware";
        internal string Path, Device, UsbId = "Unavailable", Serial = "Unavailable", UsbRelease = "Unavailable";
        internal string Protocol = "Unavailable", Mode = "Unavailable", Status = "Not queried";
        internal string Card = "Unavailable", Capacity = "Unavailable", Bus = "Unavailable", CardSource = "No cached card information";
        internal XeNpSdInfo Sd;
        internal NpFirmwareIdentity Firmware;
        internal bool DeviceIdle;
        uint Family { get { return Device == "NP V1" ? 1U : Device == "NP V2" ? 2U : 0U; } }
        internal DateTime ReceivedAt;

        internal static NpSystemInfo Empty(string path, string device)
        {
            var result = new NpSystemInfo { Path = path, Device = device };
            var match = Regex.Match(path ?? "", @"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})[^#]*#([^#]+)#", RegexOptions.IgnoreCase);
            if (match.Success) {
                result.UsbId = "VID " + match.Groups[1].Value.ToUpperInvariant() + " / PID " + match.Groups[2].Value.ToUpperInvariant() + " (Windows)";
                result.Serial = match.Groups[3].Value;
            }
            return result;
        }
        internal static string CapacityText(ulong sectors)
        {
            if (sectors == 0) return "Unavailable";
            double bytes = sectors * 512.0;
            return (bytes / 1000000000.0).ToString("F2", CultureInfo.InvariantCulture) + " GB / " +
                (bytes / 1073741824.0).ToString("F2", CultureInfo.InvariantCulture) + " GiB";
        }
        internal static string UsbReleaseText(ushort value)
        {
            for (int i = 0; i < 4; i++) if (((value >> (i * 4)) & 15) > 9) return "0x" + value.ToString("X4") + " (raw USB release field)";
            return ((value >> 12) * 10 + ((value >> 8) & 15)) + "." + ((value >> 4) & 15) + (value & 15);
        }
        // The injected reader is also used by the offline tests. Every request is
        // IN-only; no mode change, card initialization, bulk read or pipe reset.
        internal static NpSystemInfo ReadSnapshot(string path, string device, Func<byte, byte, ushort, int, byte[]> read)
        {
            var result = Empty(path, device);
            try {
                byte[] descriptor = read(0x80, 6, 0x0100, 18);
                if (descriptor == null || descriptor.Length != 18 || descriptor[0] != 18 || descriptor[1] != 1)
                    throw new InvalidDataException("Incomplete USB device descriptor");
                result.UsbId = "VID " + BitConverter.ToUInt16(descriptor, 8).ToString("X4") + " / PID " + BitConverter.ToUInt16(descriptor, 10).ToString("X4") + " (device)";
                result.UsbRelease = UsbReleaseText(BitConverter.ToUInt16(descriptor, 12));
                result.Status = "USB descriptor received";
                if (device != "NP V1" && device != "NP V2") return result;
                if (BitConverter.ToUInt16(descriptor, 8) != 0x1209 || BitConverter.ToUInt16(descriptor, 10) != 0xb386)
                    throw new InvalidDataException("Selected NP device USB identity changed; no vendor query sent");
                byte[] service = read(0xc0, 0x6e, 0, 32);
                uint role, busy, pending;
                if (device == "NP V1") {
                    var state = NpV1Management.ParseSplitStatus(service, NpV1Management.UidFromPath(path));
                    role = state.Role; busy = state.Busy; pending = state.PendingMode;
                    result.Protocol = "NPS1 ABI " + BitConverter.ToUInt32(service, 4) + " (protocol ABI, not firmware version)";
                } else {
                    var state = NpV2ServiceStatus.Parse(service);
                    if (!state.Valid || state.Mode < 1 || state.Mode > 4 || state.Busy > 1 || state.Target > 4)
                        throw new InvalidDataException("Unsupported NP V2 service status");
                    string value = NpV2FirmwareForm.Uid(path);
                    if (value != null) {
                        if (state.Uid0 != Convert.ToUInt32(value.Substring(0, 8), 16) ||
                            state.UidXor != (Convert.ToUInt32(value.Substring(8, 8), 16) ^ Convert.ToUInt32(value.Substring(16, 8), 16)))
                            throw new InvalidDataException("NP V2 UID/status mismatch");
                    }
                    role = state.Mode; busy = state.Busy; pending = state.Target;
                    result.Protocol = "NPS2 ABI " + state.Abi + " (protocol ABI, not firmware version)";
                }
                result.Mode = role == 2 ? "USB streaming service" : role == 3 ? "SD download service" : role == 4 ? "Recovery / update service" : "SD application";
                result.Status = busy != 0 ? "Device busy; SD information not queried" : pending != 0 ? "Mode transition pending; SD information not queried" : "Idle snapshot";
                if (busy != 0 || pending != 0) return result;
                result.DeviceIdle = true;
                if((BitConverter.ToUInt32(service,12)&NpFirmwareIdentity.UsbCapability)!=0) {
                    try {result.Firmware=NpFirmwareIdentity.Parse(read(0xc0,NpFirmwareIdentity.UsbRequest,0,NpFirmwareIdentity.WireBytes),result.Family,
                        result.Family==1?NpV1Management.UidFromPath(path):NpV2FirmwareForm.Uid(path));}
                    catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is Win32Exception || ex is ArgumentException || ex is TimeoutException) {
                        result.Status+="; firmware identity unavailable: "+ex.Message;
                    }
                }
                if (role == 3) {
                    byte[] capacity = read(0xc0, 0x60, 0, 32);
                    if (capacity == null || capacity.Length != 32 || BitConverter.ToUInt32(capacity, 0) != 0x3144504e ||
                        BitConverter.ToUInt32(capacity, 4) != 1 || BitConverter.ToUInt32(capacity, 28) != 1 || BitConverter.ToUInt32(capacity, 16) > 4)
                        throw new InvalidDataException("Unsupported passive SD download information");
                    result.Capacity = CapacityText(BitConverter.ToUInt32(capacity, 8));
                    result.CardSource = "Passive download-service capacity; card identity and speed are not exposed";
                } else if (role == 2) {
                    byte[] cached = read(0xc0, 0x4b, 0, XeNpSdInfo.WireBytes);
                    result.Sd = XeNpSdInfo.Parse(cached, cached == null ? 0 : cached.Length);
                    result.CardSource = "Cached SD Info: " + result.Sd.SummaryState + "; snapshot " + result.Sd.Sequence + "; device uptime " + result.Sd.Tick + " ms";
                    if ((result.Sd.State == 2 || result.Sd.State == 3) && (result.Sd.Flags & 1) != 0) {
                        result.Card = result.Sd.Ascii(67, 5) + " / OEM " + result.Sd.Ascii(65, 2) + " / MID 0x" + result.Sd.Raw[64].ToString("X2");
                        result.Capacity = CapacityText(result.Sd.Sectors);
                        if (result.Sd.ClockHz != 0 && (result.Sd.BusWidth == 1 || result.Sd.BusWidth == 4))
                            result.Bus = (result.Sd.ClockHz / 1000000.0).ToString("F3", CultureInfo.InvariantCulture) + " MHz / " + result.Sd.BusWidth + "-bit / " + XeNpSdInfo.Mode(result.Sd.ActiveFunction, (result.Sd.Flags & 2) != 0);
                    }
                }
            } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is Win32Exception || ex is TimeoutException || ex is ArgumentException) {
                result.Status += "; unavailable query: " + ex.Message.Replace("\r", " ").Replace("\n", " ");
            } finally { result.ReceivedAt = DateTime.Now; }
            return result;
        }
        internal string Summary()
        {
            return "Firmware: " + NpFirmwareCatalog.Bundled.Installed(Family, Firmware) +
                "\r\nDevice UID: " + Serial + "\r\nSD card: " + Card + "\r\nSD capacity: " + Capacity +
                "\r\n" + Mode + " · " + Status;
        }
        internal string Report(string consoleVersion)
        {
            var text = new StringBuilder();
            text.AppendLine("DEVICE"); text.AppendLine("Device: " + Device); text.AppendLine("USB ID: " + UsbId);
            text.AppendLine("USB serial / UID: " + Serial); text.AppendLine("USB device release (bcdDevice): " + UsbRelease);
            text.AppendLine("Selected interface: " + Path); text.AppendLine();
            text.AppendLine("FIRMWARE AND SOFTWARE");
            text.AppendLine("Installed image versions: " + NpFirmwareCatalog.Bundled.Installed(Family, Firmware));
            text.AppendLine("Image identity source: " + (Firmware == null ? NotReported : "Firmware-reported cached manifest checks; NPF1 response CRC verified"));
            if(Firmware!=null){text.AppendLine("APP code checked this boot: "+((Firmware.Flags&4)!=0));text.AppendLine("USB code checked this boot: "+((Firmware.Flags&8)!=0));}
            var bundled = NpFirmwareCatalog.Bundled.For(Family);
            if (bundled != null) text.AppendLine("Bundled: " + bundled.Versions);
            text.AppendLine("Console version (running executable): " + consoleVersion); text.AppendLine("Protocol: " + Protocol);
            text.AppendLine("USB device release and protocol ABI are not semantic firmware versions."); text.AppendLine();
            text.AppendLine("SD CARD"); text.AppendLine("Card identity: " + Card); text.AppendLine("Total capacity: " + Capacity);
            text.AppendLine("Free recording space: Unavailable (not reported)"); text.AppendLine("Cached query-session bus: " + Bus);
            text.AppendLine("Measured recording/write speed: Unavailable (not reported)"); text.AppendLine("Snapshot source: " + CardSource);
            if (Sd != null && (Sd.State == 2 || Sd.State == 3) && (Sd.Flags & 1) != 0) {
                text.AppendLine("Card serial: " + Sd.Hex(73, 4)); text.AppendLine("CID: " + Sd.Hex(64, 16)); text.AppendLine("CSD: " + Sd.Hex(80, 16));
            }
            text.AppendLine("Cached query-session speed is not the recording application's operating mode."); text.AppendLine();
            text.AppendLine("STATUS"); text.AppendLine("Mode: " + Mode); text.AppendLine("Query: " + Status);
            text.AppendLine("Received: " + (ReceivedAt == default(DateTime) ? "Not queried" : ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            text.AppendLine("Read-only snapshot. No card initialization, tuning or capture command was sent.");
            return text.ToString();
        }
        internal static NpSystemInfo Read(string path, string device)
        {
            using (var control = new NpSystemInfoControl(path)) return ReadSnapshot(path, device, control.Read);
        }
    }

    // Dedicated idle-only EP0 handle. Capture constructors configure/reset bulk
    // pipes and must not be used for a passive system-info query.
    internal sealed class NpSystemInfoControl : IDisposable
    {
        SafeFileHandle file;
        IntPtr usb;
        internal NpSystemInfoControl(string path)
        {
            file = NpDownloadNative.CreateFile(path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000080, IntPtr.Zero);
            try {
                if (file.IsInvalid || !NpDownloadNative.WinUsbInitialize(file, out usb)) throw new Win32Exception(Marshal.GetLastWin32Error());
                // WinUSB's documented control-transfer timeout is five seconds.
                // At most three one-shot IN requests run, stopping on the first
                // failure. No bulk-pipe timeout or reset is used to bound EP0.
            } catch { Dispose(); throw; }
        }
        internal byte[] Read(byte type, byte request, ushort value, int length)
        {
            if ((type & 0x80) == 0 || length < 1 || length > 384) throw new ArgumentException("System info permits bounded IN requests only");
            byte[] bytes = new byte[length]; uint received;
            var setup = new NpDownloadNative.WINUSB_SETUP_PACKET { RequestType = type, Request = request, Value = value, Length = (ushort)length };
            if (!NpDownloadNative.WinUsbControlTransfer(usb, setup, bytes, (uint)length, out received, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (received != length) throw new IOException("Short system-info reply");
            return bytes;
        }
        public void Dispose() { if (usb != IntPtr.Zero) { NpDownloadNative.WinUsbFree(usb); usb = IntPtr.Zero; } if (file != null) file.Dispose(); }
    }
}
