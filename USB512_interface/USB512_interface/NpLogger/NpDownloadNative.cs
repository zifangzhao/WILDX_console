using System; using System.IO; using System.Collections.Generic; using System.ComponentModel; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
namespace CE32_console {
    internal static class NpDownloadNative
    {
        internal static readonly Guid InterfaceGuid = new Guid("4F6A8C79-3F02-4B0D-9E2C-1A9B5C7D42E1");
        internal static readonly Guid NeuropixelsInterfaceGuid = new Guid("8F5A4A7B-0AA6-4F8E-A14F-A2F7B3C6AA4E");
        internal static IList<string> EnumerateNpPaths()
        {
            var paths=new List<string>();
            foreach(var guid in new[]{NeuropixelsInterfaceGuid,new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED")})
                foreach(string path in EnumerateInterfacePaths(guid))
                {
                    int separator=path.LastIndexOf('#');
                    if(separator<0 || path.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)<0)continue;
                    string instance=path.Substring(0,separator);
                    if(!paths.Exists(p=>string.Equals(p.Substring(0,p.LastIndexOf('#')),instance,StringComparison.OrdinalIgnoreCase)))paths.Add(path);
                }
            return paths;
        }

        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const uint FileAttributeNormal = 0x00000080;
        // Match the tested Python WinUSB host setup.  The normal ReadPipe calls
        // may still complete synchronously with OVERLAPPED = NULL.
        internal const uint FileFlagOverlapped = 0x40000000;
        internal const int ErrorSemTimeout = 121;
        internal const int ErrorGenFailure = 31;
        internal const int ErrorOperationAborted = 995;
        internal const int ErrorIoPending = 997;
        internal const uint WaitObject0 = 0;
        internal const uint WaitTimeout = 0x00000102;
        internal const uint WaitFailed = 0xFFFFFFFF;
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;

        internal static IList<string> EnumerateInterfacePaths(Guid interfaceGuid)
        {
            var result = new List<string>();
            var info = SetupDiGetClassDevs(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (info == InvalidHandleValue)
            {
                return result;
            }
            try
            {
                for (uint index = 0; ; index++)
                {
                    var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                    interfaceData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                    {
                        if (Marshal.GetLastWin32Error() == 259)
                        {
                            break;
                        }
                        continue;
                    }

                    uint required;
                    SetupDiGetDeviceInterfaceDetail(info, ref interfaceData, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required == 0)
                    {
                        continue;
                    }
                    var detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(info, ref interfaceData, detail, required, out required, IntPtr.Zero))
                        {
                            var path = Marshal.PtrToStringAuto(IntPtr.Add(detail, 4));
                            if (!string.IsNullOrEmpty(path))
                            {
                                result.Add(path);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            internal int cbSize;
            internal Guid InterfaceClassGuid;
            internal int Flags;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct WINUSB_SETUP_PACKET
        {
            internal byte RequestType;
            internal byte Request;
            internal ushort Value;
            internal ushort Index;
            internal ushort Length;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct USB_INTERFACE_DESCRIPTOR
        {
            internal byte bLength;
            internal byte bDescriptorType;
            internal byte bInterfaceNumber;
            internal byte bAlternateSetting;
            internal byte bNumEndpoints;
            internal byte bInterfaceClass;
            internal byte bInterfaceSubClass;
            internal byte bInterfaceProtocol;
            internal byte iInterface;
        }

        internal enum USBD_PIPE_TYPE
        {
            Control,
            Isochronous,
            Bulk,
            Interrupt
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINUSB_PIPE_INFORMATION
        {
            internal USBD_PIPE_TYPE PipeType;
            internal byte PipeId;
            internal ushort MaximumPacketSize;
            internal byte Interval;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NATIVE_OVERLAPPED
        {
            internal IntPtr Internal;
            internal IntPtr InternalHigh;
            internal uint Offset;
            internal uint OffsetHigh;
            internal IntPtr Event;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ResetEvent(IntPtr eventHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        internal static bool WinUsbInitialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle, string target=null) { return USB512_interface.UsbHealthNative.Initialize(deviceHandle,out interfaceHandle,target); }

        internal static bool WinUsbFree(IntPtr interfaceHandle) { return USB512_interface.UsbHealthNative.Free(interfaceHandle); }

        [DllImport("winusb.dll", EntryPoint = "WinUsb_QueryInterfaceSettings", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbQueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, out USB_INTERFACE_DESCRIPTOR descriptor);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_QueryPipe", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbQueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WINUSB_PIPE_INFORMATION pipeInformation);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_SetPipePolicy", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbSetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint value);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_SetPowerPolicy", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbSetPowerPolicy(IntPtr interfaceHandle, uint policyType, uint valueLength, ref byte value);

        internal static bool WinUsbReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength, out uint transferred, IntPtr overlapped) { return USB512_interface.UsbHealthNative.Read(interfaceHandle,pipeId,buffer,bufferLength,out transferred,overlapped); }

        internal static bool WinUsbReadPipeOverlapped(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint transferred, IntPtr overlapped) { return USB512_interface.UsbHealthNative.ReadOverlapped(interfaceHandle,pipeId,buffer,bufferLength,out transferred,overlapped); }

        internal static bool WinUsbGetOverlappedResult(IntPtr interfaceHandle, IntPtr overlapped, out uint transferred, bool wait) { return USB512_interface.UsbHealthNative.Result(interfaceHandle,overlapped,out transferred,wait); }

        [DllImport("winusb.dll", EntryPoint = "WinUsb_ControlTransfer", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbControlTransfer(IntPtr interfaceHandle, WINUSB_SETUP_PACKET setupPacket, [In, Out] byte[] buffer, uint bufferLength, out uint transferred, IntPtr overlapped);

        internal static bool WinUsbAbortPipe(IntPtr interfaceHandle, byte pipeId) { return USB512_interface.UsbHealthNative.Abort(interfaceHandle,pipeId); }

        [DllImport("winusb.dll", EntryPoint = "WinUsb_ResetPipe", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbResetPipe(IntPtr interfaceHandle, byte pipeId);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_FlushPipe", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbFlushPipe(IntPtr interfaceHandle, byte pipeId);

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
    }
}
