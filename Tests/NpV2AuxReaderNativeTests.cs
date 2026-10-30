using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using USB512_interface;

// Native calls alone are replaced while compiling the exact production queued
// reader. No physical WinUSB/interface discovery or device access occurs.
namespace USB512_interface {
    internal static class USB512WinUsbNative {
        internal const int ErrorIoPending=997,ErrorGenFailure=31,ErrorOperationAborted=995,ErrorSemTimeout=121;
        internal const uint WaitObject0=0,WaitTimeout=258,WaitFailed=UInt32.MaxValue;
        [StructLayout(LayoutKind.Sequential)]internal struct NATIVE_OVERLAPPED {internal IntPtr InternalLow,InternalHigh;internal uint OffsetLow,OffsetHigh;internal IntPtr Event;}
        [DllImport("kernel32.dll",EntryPoint="SetLastError",SetLastError=true)]static extern void LastError(uint n);
        internal static readonly List<byte> Aborted=new List<byte>();internal static int Closed,NextEvent,Fill;internal static bool Timeout,Incomplete,Immediate,AbortFailure;
        internal static IntPtr CreateEvent(IntPtr a,bool b,bool c,string d){return new IntPtr(++NextEvent);}
        internal static bool CloseHandle(IntPtr e){Closed++;return true;}
        internal static bool ResetEvent(IntPtr e){return true;}
        internal static uint WaitForSingleObject(IntPtr e,uint ms){return Timeout?WaitTimeout:WaitObject0;}
        internal static bool WinUsbReadPipeOverlapped(IntPtr h,byte pipe,IntPtr buffer,uint n,out uint bytes,IntPtr ov){
            var b=new byte[(int)n];Fill++;for(int i=0;i<b.Length;i++)b[i]=(byte)Fill;Marshal.Copy(b,0,buffer,b.Length);bytes=Immediate?64U:0U;LastError(997);return Immediate;
        }
        internal static bool WinUsbGetOverlappedResult(IntPtr h,IntPtr ov,out uint bytes,bool wait){bytes=0;LastError(Incomplete?996U:995U);return false;}
        internal static bool WinUsbAbortPipe(IntPtr h,byte pipe){Aborted.Add(pipe);LastError(31);return !AbortFailure;}
        internal static bool WinUsbResetPipe(IntPtr h,byte pipe){throw new Exception("AUX must not invoke legacy reset/replay");}
    }
}
internal static class NpV2AuxReaderNativeTests {
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Bad(Action run,string why){try{run();}catch(IOException){checks++;return;}catch(TimeoutException){checks++;return;}catch(Win32Exception){checks++;return;}throw new Exception(why);}
    static void Main(){
        var r=new USB512WinUsbQueuedReader(new IntPtr(1),0x82,512,4,null,null,null,true);
        USB512WinUsbNative.Timeout=true;Check(r.ReadInto(new byte[512])==0,"Ordered timeout consumed/rotated pending read");Bad(r.Retire,"Pending wait timeout treated as retired");Check(!r.RetirementConfirmed&&USB512WinUsbNative.Closed==0,"Timeout freed slots");
        USB512WinUsbNative.Timeout=false;USB512WinUsbNative.Incomplete=true;Bad(r.Retire,"ERROR_IO_INCOMPLETE treated as retired");Check(!r.RetirementConfirmed&&USB512WinUsbNative.Closed==0,"Incomplete harvest freed native memory");
        USB512WinUsbNative.Incomplete=false;r.Retire();Check(r.RetirementConfirmed,"Terminal cancelled reads did not prove retirement");r.Dispose();Check(USB512WinUsbNative.Closed==4,"Proven retirement did not free all slots");Check(USB512WinUsbNative.Aborted.TrueForAll(p=>p==0x82),"AUX retired/reset EP81");
        USB512WinUsbNative.Immediate=true;var copy=new USB512WinUsbQueuedReader(new IntPtr(1),0x82,512,1,null,null,null,true);var into=new byte[512];int fillBefore=USB512WinUsbNative.Fill;Check(copy.ReadInto(into)==64&&into[63]==(byte)fillBefore,"Read buffer was rearmed before copy");copy.Retire();copy.Dispose();
        USB512WinUsbNative.Immediate=false;USB512WinUsbNative.AbortFailure=true;var held=new USB512WinUsbQueuedReader(new IntPtr(1),0x82,512,1,null,null,null,true);int closed=USB512WinUsbNative.Closed;Bad(held.Retire,"Abort failure treated as retired");held.Dispose();Check(!held.RetirementConfirmed&&USB512WinUsbNative.Closed==closed,"Dispose swallowed uncertainty then freed slots");
        var slots=(Array)typeof(USB512WinUsbQueuedReader).GetField("slots",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(held);object slot=slots.GetValue(0);var pin=(GCHandle)slot.GetType().GetField("Pin",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(slot);Check(pin.IsAllocated&&(IntPtr)slot.GetType().GetField("OverlappedPointer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(slot)!=IntPtr.Zero,"HOLD did not retain pinned slot/OVERLAPPED");
        Console.WriteLine("V2 AUX exact production queued-reader/native-call fixture: "+checks+" checks passed.");
    }
}
