using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace USB512_interface {
    // Borrowed capture handle, separate OUT01 pipe. Retire before owner frees it.
    internal sealed class NpUsbTimingOut : IDisposable {
        readonly object gate=new object();readonly Action<byte[]> testWrite;IntPtr handle;volatile bool writing;bool disposed;
        [DllImport("winusb.dll",EntryPoint="WinUsb_WritePipe",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]static extern bool WritePipe(IntPtr h,byte pipe,byte[] bytes,uint count,out uint sent,IntPtr overlapped);
        [DllImport("winusb.dll",EntryPoint="WinUsb_SetPipePolicy",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]static extern bool SetBool(IntPtr h,byte pipe,uint policy,uint length,ref byte value);
        internal NpUsbTimingOut(IntPtr captureHandle) {
            if(captureHandle==IntPtr.Zero)throw new ArgumentException("Capture handle required.");handle=captureHandle;
            uint timeout=1000;byte raw=0;
            if(!USB512WinUsbNative.WinUsbSetPipePolicy(handle,1,3,4,ref timeout)||!SetBool(handle,1,7,1,ref raw))throw new Win32Exception(Marshal.GetLastWin32Error(),"Optional timing OUT policy unavailable.");
        }
        internal NpUsbTimingOut(Action<byte[]> fakeWrite){testWrite=fakeWrite??throw new ArgumentNullException("fakeWrite");}
        internal void Write(byte[] bytes) {
            if(bytes==null||bytes.Length!=64)throw new ArgumentException("Timing OUT request must be64 bytes.");
            lock(gate) {
                if(disposed)throw new ObjectDisposedException("NP timing OUT");
                writing=true;try{if(testWrite!=null){testWrite(bytes);return;}uint sent;if(!WritePipe(handle,1,bytes,64,out sent,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error(),"Optional timing OUT failed.");
                    if(sent!=64)throw new IOException("Optional timing OUT was truncated.");}
                finally{writing=false;}
            }
        }
        internal void Abort(){IntPtr h=handle;if(h!=IntPtr.Zero&&writing)USB512WinUsbNative.WinUsbAbortPipe(h,1);}
        public void Dispose(){Abort();lock(gate){disposed=true;handle=IntPtr.Zero;}}
    }
}
