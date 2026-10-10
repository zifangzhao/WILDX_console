using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Send through the Windows disk class driver, never around USBSTOR.
internal sealed class MscControl : IDisposable
{
    private static readonly Guid DiskInterface = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");
    private readonly SafeFileHandle file;
    private MscControl(SafeFileHandle handle) { file = handle; }

    public static void SelfTest()
    {
        if (Marshal.SizeOf<ScsiPassThrough>() != (IntPtr.Size == 8 ? 56 : 44) ||
            Marshal.OffsetOf<ScsiPassThrough>(nameof(ScsiPassThrough.DataBufferOffset)).ToInt32() != (IntPtr.Size == 8 ? 24 : 20) ||
            Marshal.OffsetOf<ScsiPassThrough>(nameof(ScsiPassThrough.Cdb)).ToInt32() != (IntPtr.Size == 8 ? 36 : 28))
            throw new InvalidOperationException("SCSI_PASS_THROUGH native layout mismatch.");
        Console.WriteLine("PASS: Windows SCSI_PASS_THROUGH native size/alignment (no device accessed).");
        if (!IsLoggerPath("usbstor#disk&ven_wildx&prod_np1_sd_storage") ||
            !IsLoggerPath("usbstor#disk&ven_xe_np&prod_np1_sd_storage") ||
            IsLoggerPath("usbstor#disk&ven_other&prod_np1_sd_storage") ||
            !IsLoggerIdentity("WILDX   NP1 SD STORAGE  ") ||
            !IsLoggerIdentity("XE_NP   NP1 SD STORAGE  ") ||
            IsLoggerIdentity("WILDX   UNRELATED DISK  "))
            throw new InvalidOperationException("WILDX/legacy MSC identity compatibility failed.");
        Console.WriteLine("PASS: WILDX and legacy MSC discovery/identity (no device accessed).");
    }

    private static bool IsLoggerPath(string path) =>
        path.Contains("usbstor#disk&ven_wildx&prod_np1_sd_storage", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("usbstor#disk&ven_xe_np&prod_np1_sd_storage", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoggerIdentity(string identity) =>
        identity is "WILDX   NP1 SD STORAGE  " or "XE_NP   NP1 SD STORAGE  ";

    public static IReadOnlyList<string> ListPaths() => WinUsbDevice.ListPaths(DiskInterface)
        .Where(IsLoggerPath).ToArray();

    public static MscControl Open()
    {
        var paths = ListPaths();
        if (paths.Count != 1)
            throw new IOException($"Expected exactly one WILDX MSC disk; found {paths.Count}. Disconnect extra WILDX disks if necessary.");
        var handle = Native.CreateFile(paths[0], Native.GenericRead | Native.GenericWrite,
            Native.ShareRead | Native.ShareWrite, IntPtr.Zero, Native.OpenExisting, Native.FileAttributeNormal, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new Win32Exception(error, $"Cannot open WILDX MSC disk (Windows error {error}). Windows may require an administrator terminal for SCSI pass-through.");
        }
        var device = new MscControl(handle);
        try
        {
            var inquiry = device.Command([0x12, 0, 0, 0, 36, 0], 36);
            if (inquiry.Length != 36 || !IsLoggerIdentity(Encoding.ASCII.GetString(inquiry, 8, 24)))
                throw new IOException("SCSI INQUIRY identity mismatch; refusing vendor commands.");
            return device;
        }
        catch { device.Dispose(); throw; }
    }

    public byte[] Status()
    {
        var data = Vendor(0, 64);
        if (data.Length != 64 || !data.AsSpan(0, 4).SequenceEqual("XNMS"u8) || Read32(data, 4) != 1)
            throw new IOException("MSC status signature/version mismatch.");
        return data;
    }

    public void PrintStatus()
    {
        var s = Status(); var flags = Read32(s, 8);
        Console.WriteLine($"MSC: medium={((flags & 1) != 0 ? "ready" : "not ready")}, access={((flags & 2) != 0 ? "read/write" : "read-only")}, blocks={Read32(s, 12)}, block bytes={Read32(s, 16)}");
        Console.WriteLine($"Loader ABI={Read32(s, 24)}, SD error=0x{Read32(s, 20):X8}, commands={Read32(s, 28)}, protocol errors={Read32(s, 32)}, read blocks={Read32(s, 36)}, written blocks={Read32(s, 40)}");
    }

    public void Switch(byte mode)
    {
        // Do not dismount or silently eject a mounted filesystem. The device
        // accepts this only after SCSI eject, or when no medium is present.
        Vendor(mode, 0);
    }

    private byte[] Vendor(byte mode, int bytes)
    {
        byte[] cdb = new byte[16]; cdb[0] = 0xC0; cdb[1] = mode;
        "XENP"u8.CopyTo(cdb.AsSpan(2)); return Command(cdb, bytes);
    }

    private byte[] Command(byte[] cdb, int dataBytes)
    {
        var header = Marshal.SizeOf<ScsiPassThrough>();
        var dataOffset = (header + 32 + 7) & ~7;
        var length = dataOffset + dataBytes;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.Copy(new byte[length], 0, buffer, length);
            var spt = new ScsiPassThrough {
                Length = checked((ushort)header), CdbLength = checked((byte)cdb.Length), SenseInfoLength = 32,
                DataIn = dataBytes == 0 ? (byte)2 : (byte)1, DataTransferLength = (uint)dataBytes,
                TimeOutValue = 10, DataBufferOffset = (UIntPtr)dataOffset, SenseInfoOffset = (uint)header,
                Cdb = new byte[16]
            };
            cdb.CopyTo(spt.Cdb, 0); Marshal.StructureToPtr(spt, buffer, false);
            if (!DeviceIoControl(file, 0x0004D004, buffer, length, buffer, length, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SCSI pass-through failed");
            spt = Marshal.PtrToStructure<ScsiPassThrough>(buffer);
            if (spt.ScsiStatus != 0)
            {
                byte[] sense = new byte[32]; Marshal.Copy(buffer + header, sense, 0, sense.Length);
                throw new IOException($"SCSI status 0x{spt.ScsiStatus:X2}, sense key 0x{sense[2] & 15:X2}, ASC/ASCQ {sense[12]:X2}/{sense[13]:X2}. Mode changes require an unlocked, SCSI-ejected medium (or no medium); mounted storage is never forcibly detached.");
            }
            if (spt.DataTransferLength > dataBytes) throw new IOException("Oversized SCSI response.");
            var result = new byte[spt.DataTransferLength]; Marshal.Copy(buffer + dataOffset, result, 0, result.Length);
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    public void Dispose() => file.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct ScsiPassThrough
    {
        public ushort Length;
        public byte ScsiStatus, PathId, TargetId, Lun, CdbLength, SenseInfoLength, DataIn;
        public uint DataTransferLength, TimeOutValue;
        public UIntPtr DataBufferOffset;
        public uint SenseInfoOffset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Cdb;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputBytes,
        IntPtr output, int outputBytes, out int returned, IntPtr overlapped);
}
