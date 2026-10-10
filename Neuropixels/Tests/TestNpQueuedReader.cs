using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
namespace USB512_interface {
internal static class USB512WinUsbNative {
 internal const int ErrorGenFailure=31,ErrorIoPending=997,ErrorOperationAborted=995,ErrorSemTimeout=121;
 internal const uint WaitTimeout=258,WaitObject0=0,WaitFailed=0xffffffff;
 [StructLayout(LayoutKind.Sequential)]internal struct NATIVE_OVERLAPPED {internal IntPtr Internal,InternalHigh;internal uint Offset,OffsetHigh;internal IntPtr Event;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern void SetLastError(uint e);
 internal class Request {internal IntPtr Overlap,Buffer,Event;internal uint Count;internal int Id;internal bool Done,Cancelled;}
 internal static List<Request> Requests=new List<Request>();
 internal static bool IncompleteAfterAbort;
 internal static bool AbortFails;
 internal static int AbortCalls;
 internal static int Closed,NextEvent;
 internal static IntPtr CreateEvent(IntPtr a,bool b,bool c,string d){return new IntPtr(++NextEvent);}
 internal static bool CloseHandle(IntPtr p){Closed++;return true;}
 internal static bool ResetEvent(IntPtr p){return true;}
 internal static bool WinUsbResetPipe(IntPtr h,byte p){throw new Exception("Unexpected pipe reset/replay");}
 internal static bool WinUsbReadPipeOverlapped(IntPtr h,byte p,IntPtr b,uint n,out uint sent,IntPtr o) {
  Requests.Add(new Request {Buffer=b,Count=n,Overlap=o,Event=((NATIVE_OVERLAPPED)Marshal.PtrToStructure(o,typeof(NATIVE_OVERLAPPED))).Event,Id=Requests.Count});
  sent=0;SetLastError(997);return false;
 }
 static Request Active(IntPtr o){for(int i=Requests.Count-1;i>=0;i--)if(Requests[i].Overlap==o)return Requests[i];throw new Exception();}
 internal static uint WaitForSingleObject(IntPtr e,uint ms) {
  for(int i=Requests.Count-1;i>=0;i--)if(Requests[i].Event==e)return Requests[i].Done?0U:258U;throw new Exception();
 }
 internal static bool WinUsbGetOverlappedResult(IntPtr h,IntPtr o,out uint n,bool wait) {
  var r=Active(o);n=r.Count;
  if(r.Cancelled){SetLastError(IncompleteAfterAbort?996U:995U);return false;}
  if(!r.Done){SetLastError(996);return false;}return true;
 }
 internal static bool WinUsbAbortPipe(IntPtr h,byte p) {
  AbortCalls++;if(AbortFails){SetLastError(31);return false;}
  foreach(var r in Requests)if(!r.Done){r.Done=true;r.Cancelled=true;}return true;
 }
 internal static void Complete(int id) {
  var r=Requests[id];var b=new byte[r.Count];for(int i=0;i<b.Length;i++)b[i]=(byte)id;
  Marshal.Copy(b,0,r.Buffer,b.Length);r.Done=true;
 }
 internal static void Reset(){Requests.Clear();IncompleteAfterAbort=AbortFails=false;Closed=NextEvent=AbortCalls=0;}
}
partial class XeNpWinUsbDevice {
 IntPtr winUsb=new IntPtr(1);byte bulkInPipe=0x81;uint deferredAdmissionSession;
 const byte RequestStop=0x41;
 internal bool FailStop;internal int Requests;
 void VendorOut(byte request,ushort value,ushort index) {
  if(request!=0x41 || value!=0 || index!=0)throw new Exception("STOP wire contract changed");
  Requests++;if(FailStop)throw new IOException("injected STOP control failure");
 }
}
class QueuedReaderTests {
 static void Check(bool b,string why){if(!b)throw new Exception(why);}
 static void Fifo(int depth) {
  USB512WinUsbNative.Reset();var r=new USB512WinUsbQueuedReader(new IntPtr(1),0x81,512,depth,null,null,null,true);
  Check(USB512WinUsbNative.Requests.Count==depth,"All requests prearmed");
  GC.Collect();GC.WaitForPendingFinalizers();
  USB512WinUsbNative.Complete(1);var b=new byte[512];
  Check(r.ReadInto(b)==0,"Later completion must not bypass pending head");
  USB512WinUsbNative.Complete(0);Check(r.ReadInto(b)==512 && b[0]==0,"Head order");
  Check(r.ReadInto(b)==512 && b[0]==1,"Second order");
  Check(USB512WinUsbNative.Requests.Count==depth+2,"Rearmed each copied completion");
  USB512WinUsbNative.Complete(2);USB512WinUsbNative.WinUsbAbortPipe(new IntPtr(1),0x81);
  Check(r.DrainStoppedInto(b)==512 && b[0]==2,"Stopped completed prefix");
  Check(r.DrainStoppedInto(b)==-1,"Cancelled tail stops drain");
  r.Dispose();Check(USB512WinUsbNative.Closed==depth,"All retired request resources freed");
 }
 static void IncompleteCancellation() {
  USB512WinUsbNative.Reset();var r=new USB512WinUsbQueuedReader(new IntPtr(1),0x81,512,8,null,null,null,true);
  USB512WinUsbNative.IncompleteAfterAbort=true;r.Dispose();
  Check(USB512WinUsbNative.Closed==0,"Never free request memory on incomplete cancellation");
  // Mock now confirms cancellation; explicitly release the test allocations.
  USB512WinUsbNative.IncompleteAfterAbort=false;r.ReleaseAfterRecoveryClose();
  Check(USB512WinUsbNative.Closed==8,"Test cleanup");
 }
 static void RearmAllocation() {
  USB512WinUsbNative.Reset();var reader=new USB512WinUsbQueuedReader(new IntPtr(1),0x81,512,8,null,null,null,true);
  var slots=(Array)typeof(USB512WinUsbQueuedReader).GetField("slots",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reader);
  object slot=slots.GetValue(0);
  var prepare=(Action)Delegate.CreateDelegate(typeof(Action),slot,slot.GetType().GetMethod("PrepareOverlapped",BindingFlags.NonPublic|BindingFlags.Instance));
  var pointer=(IntPtr)slot.GetType().GetField("OverlappedPointer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(slot);
  for(int n=0;n<1000;n++)prepare();
  var method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);
  var allocated=method==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);
  GC.Collect();GC.WaitForPendingFinalizers();int collections=GC.CollectionCount(0);long before=allocated==null?0:allocated();
  for(int n=0;n<100000;n++)prepare();
  long bytes=allocated==null?0:allocated()-before;
  Check(GC.CollectionCount(0)==collections,"Rearm generated Gen0 garbage");
  if(allocated!=null)Check(bytes==0,"Rearm managed allocation bytes="+bytes);
  var clean=(USB512WinUsbNative.NATIVE_OVERLAPPED)Marshal.PtrToStructure(pointer,typeof(USB512WinUsbNative.NATIVE_OVERLAPPED));
  Check(clean.Internal==IntPtr.Zero && clean.InternalHigh==IntPtr.Zero && clean.Offset==0 && clean.OffsetHigh==0 && clean.Event!=IntPtr.Zero,"Rearm changed native fields/event");
  Console.WriteLine("PASS 100000 actual rearm calls: managed allocation bytes="+bytes+"; Gen0 collections=0");reader.Dispose();
 }
 static void ReceiveBlock(string assemblyPath) {
  var assembly=Assembly.LoadFrom(assemblyPath);var type=assembly.GetType("USB512_interface.Form1",true).GetNestedType("ReceiveBlock",BindingFlags.NonPublic);
  Check(type.IsValueType,"ReceiveBlock still allocates an object per completion");
  var marker=type.GetMethod("CreateStreamRestartMarker",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
  Check((bool)type.GetProperty("IsStreamRestartMarker",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(marker,null),"Generic USB512 restart marker changed");
  byte[] buffer={1,2,3};var block=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{buffer,3},null);
  Check(ReferenceEquals(buffer,type.GetProperty("Buffer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(block,null)) &&
   (int)type.GetProperty("Count",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(block,null)==3 &&
   !(bool)type.GetProperty("IsStreamRestartMarker",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(block,null),"Payload ownership/count changed");
 }
 static void StopFailure() {
  USB512WinUsbNative.Reset();var device=new XeNpWinUsbDevice();device.FailStop=true;bool failed=false;
  try{device.Stop();}catch(IOException ex){failed=ex.Message=="injected STOP control failure";}
  Check(failed && device.Requests==1 && USB512WinUsbNative.AbortCalls==1,"STOP control failure was swallowed or cancellation skipped");
  device.FailStop=false;USB512WinUsbNative.AbortFails=true;failed=false;
  try{device.Stop();}catch(System.ComponentModel.Win32Exception){failed=true;}
  Check(failed,"STOP native abort failure was swallowed");
  USB512WinUsbNative.AbortFails=false;device.Stop();Check(device.Requests==3,"STOP success did not preserve request contract");
  Console.WriteLine("PASS actual STOP method: rejected control/abort failures, always attempted read cancellation; wire request unchanged");
 }
 static int Main(string[] args){Fifo(8);Fifo(16);Fifo(32);IncompleteCancellation();RearmAllocation();ReceiveBlock(args[0]);StopFailure();Console.WriteLine("PASS actual queued reader: depth8/16/32, pinned GC lifetime, FIFO, rearm, stopped-prefix drain, cancellation retention and generic restart-marker ownership");return 0;}
}
}
