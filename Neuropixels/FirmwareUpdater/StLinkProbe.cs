using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Read-only ST-Link identity diagnostic. Never enters SWD, resets the target,
// writes target memory, or updates debugger firmware. Optional pipe recovery
// only clears the selected debugger's USB endpoint state.
internal static class StLinkProbe
{
    private const string Serial = "004B00213532511131333430";
    private static readonly Guid InterfaceGuid = new("DBCE1CD9-A320-4B51-A365-A0C3F3C5FB29");
    public static int Check(bool recoverPipes)
    {
        foreach (var path in WinUsbDevice.ListPaths(InterfaceGuid))
        {
            if (!path.Contains("vid_0483&pid_3754&mi_00", StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine("ST-Link interface: " + path);
            using var file = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite,
                Native.ShareRead | Native.ShareWrite, IntPtr.Zero, Native.OpenExisting,
                Native.FileAttributeNormal | Native.FileFlagOverlapped, IntPtr.Zero);
            if (file.IsInvalid) throw Error("Open ST-Link debug interface");
            if (!Native.WinUsb_Initialize(file, out var usb)) throw Error("WinUsb_Initialize ST-Link");
            try
            {
                byte[] descriptor = new byte[18];
                if (!GetDescriptor(usb, 1, 0, 0, descriptor, 18, out var size) || size != 18)
                    throw Error("Read ST-Link USB descriptor");
                byte[] serialBytes = new byte[128];
                if (!GetDescriptor(usb, 3, descriptor[16], 0x409, serialBytes, 128, out size) || size < 2)
                    throw Error("Read ST-Link USB serial");
                string serial = Encoding.Unicode.GetString(serialBytes, 2, checked((int)size - 2));
                Console.WriteLine("Verified USB serial: " + serial);
                if (serial != Serial) continue;
                uint timeout = 2000;
                foreach (byte ep in new byte[] { 0x01, 0x81 })
                {
                    if (!Native.WinUsb_SetPipePolicy(usb, ep, Native.PipeTransferTimeout, 4, ref timeout))
                        throw Error("Set ST-Link pipe timeout");
                    if (recoverPipes && !ResetPipe(usb, ep)) throw Error("Reset ST-Link USB pipe");
                }
                if (recoverPipes && !FlushPipe(usb, 0x81)) throw Error("Flush ST-Link reply pipe");
                byte[] command = new byte[16]; command[0] = 0xF1; // STLINK_GET_VERSION
                if (!Native.WinUsb_WritePipe(usb, 0x01, command, 16, out var written, IntPtr.Zero))
                    throw Error("ST-Link GET_VERSION write");
                Console.WriteLine("Version command bytes written: " + written);
                byte[] version = new byte[6];
                uint read=0;
                // Some reconnects leave a zero-length completion before the
                // reply. Consume only a bounded number; never parse stale data.
                for (int attempt=0; attempt<3 && read==0; attempt++)
                    if (!Native.WinUsb_ReadPipe(usb, 0x81, version, 6, out read, IntPtr.Zero))
                        throw Error("ST-Link GET_VERSION read");
                Console.WriteLine("Version reply: " + Convert.ToHexString(version.AsSpan(0, checked((int)read))));
                if (read != 6 || version[2] != 0x83 || version[3] != 0x04)
                    throw new IOException("ST-Link returned an invalid/truncated version reply.");
                Console.WriteLine("PASS: debugger USB version exchange; target memory was not accessed.");
                return 0;
            }
            finally { Native.WinUsb_Free(usb); }
        }
        throw new IOException("Selected ST-Link 004B debug interface not found.");
    }
    private static Exception Error(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, operation + " failed (Windows error " + error + ").");
    }
    [DllImport("winusb.dll", EntryPoint="WinUsb_GetDescriptor", SetLastError=true)]
    private static extern bool GetDescriptor(IntPtr usb, byte type, byte index, ushort language, byte[] buffer, uint length, out uint transferred);
    [DllImport("winusb.dll", EntryPoint="WinUsb_ResetPipe", SetLastError=true)]
    private static extern bool ResetPipe(IntPtr usb, byte pipe);
    [DllImport("winusb.dll", EntryPoint="WinUsb_FlushPipe", SetLastError=true)]
    private static extern bool FlushPipe(IntPtr usb, byte pipe);
}
