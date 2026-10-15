using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace USB512_interface {
    internal static class UsbHealthNative {
        [DllImport("kernel32.dll",EntryPoint="SetLastError",SetLastError=true)]static extern void RestoreError(uint error);
        [DllImport("winusb.dll",EntryPoint="WinUsb_Initialize",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool InitializeNative(SafeFileHandle file,out IntPtr usb);
        [DllImport("winusb.dll",EntryPoint="WinUsb_Free",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool FreeNative(IntPtr usb);
        [DllImport("winusb.dll",EntryPoint="WinUsb_QueryDeviceInformation",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool SpeedNative(IntPtr usb,uint kind,ref uint length,out byte speed);
        [DllImport("winusb.dll",EntryPoint="WinUsb_ReadPipe",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool ReadNative(IntPtr usb,byte pipe,[Out]byte[] buffer,uint length,out uint bytes,IntPtr overlapped);
        [DllImport("winusb.dll",EntryPoint="WinUsb_ReadPipe",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool ReadOverlappedNative(IntPtr usb,byte pipe,IntPtr buffer,uint length,out uint bytes,IntPtr overlapped);
        [DllImport("winusb.dll",EntryPoint="WinUsb_ReadPipe",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool ReadDeferredNative(IntPtr usb,byte pipe,IntPtr buffer,uint length,IntPtr bytes,IntPtr overlapped);
        [DllImport("winusb.dll",EntryPoint="WinUsb_GetOverlappedResult",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool ResultNative(IntPtr usb,IntPtr overlapped,out uint bytes,[MarshalAs(UnmanagedType.Bool)]bool wait);
        [DllImport("winusb.dll",EntryPoint="WinUsb_AbortPipe",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool AbortNative(IntPtr usb,byte pipe);
        static bool Return(bool result,int error){RestoreError((uint)error);return result;}
        internal static bool Initialize(SafeFileHandle file,out IntPtr usb,string target=null) {
            bool ok=InitializeNative(file,out usb);int error=ok?0:Marshal.GetLastWin32Error();
            if(ok){uint length=1;byte speed;bool known=SpeedNative(usb,1,ref length,out speed);int speedError=known?0:Marshal.GetLastWin32Error();UsbHealth.Open(usb,target,known&&length==1?(byte?)speed:null,speedError);}
            return Return(ok,error);
        }
        internal static bool Free(IntPtr usb){bool ok=FreeNative(usb);int error=ok?0:Marshal.GetLastWin32Error();if(ok)UsbHealth.Close(usb);return Return(ok,error);}
        internal static bool Read(IntPtr usb,byte pipe,byte[] buffer,uint length,out uint bytes,IntPtr overlapped){
            var a=UsbHealth.Begin(usb,pipe,Stopwatch.GetTimestamp());bool ok=ReadNative(usb,pipe,buffer,length,out bytes,overlapped);int error=ok?0:Marshal.GetLastWin32Error();
            UsbHealth.Submitted(usb,a,overlapped,ok,error,bytes,false,Stopwatch.GetTimestamp());return Return(ok,error);
        }
        internal static bool ReadOverlapped(IntPtr usb,byte pipe,IntPtr buffer,uint length,out uint bytes,IntPtr overlapped){
            var a=UsbHealth.Begin(usb,pipe,Stopwatch.GetTimestamp());bool ok=ReadOverlappedNative(usb,pipe,buffer,length,out bytes,overlapped);int error=ok?0:Marshal.GetLastWin32Error();
            UsbHealth.Submitted(usb,a,overlapped,ok,error,bytes,false,Stopwatch.GetTimestamp());return Return(ok,error);
        }
        internal static bool ReadDeferred(IntPtr usb,byte pipe,IntPtr buffer,uint length,IntPtr bytes,IntPtr overlapped){
            var a=UsbHealth.Begin(usb,pipe,Stopwatch.GetTimestamp());bool ok=ReadDeferredNative(usb,pipe,buffer,length,bytes,overlapped);int error=ok?0:Marshal.GetLastWin32Error();
            UsbHealth.Submitted(usb,a,overlapped,ok,error,0,true,Stopwatch.GetTimestamp());return Return(ok,error);
        }
        internal static bool Result(IntPtr usb,IntPtr overlapped,out uint bytes,bool wait){bool ok=ResultNative(usb,overlapped,out bytes,wait);int error=ok?0:Marshal.GetLastWin32Error();UsbHealth.Completed(usb,overlapped,ok,error,bytes,Stopwatch.GetTimestamp());return Return(ok,error);}
        internal static bool Abort(IntPtr usb,byte pipe){UsbHealth.Aborting(usb,pipe);bool ok=AbortNative(usb,pipe);int error=ok?0:Marshal.GetLastWin32Error();return Return(ok,error);}
    }
}
