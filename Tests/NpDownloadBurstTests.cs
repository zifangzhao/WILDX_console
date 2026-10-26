using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
namespace USB512_interface{static class UsbHealth{internal static int Errors;internal static void Integrity(IntPtr u,byte p,bool bad,string text){if(bad)Errors++;}}}
namespace CE32_console{
static class NpDownloadNative{
 internal static NpDownloadStream Active;
 internal static bool WinUsbReadPipe(IntPtr u,byte p,byte[] b,uint count,out uint n,IntPtr o){return Active.NativeRead(b,count,out n);}
 internal static bool WinUsbResetPipe(IntPtr u,byte p){Active.Calls.Add("reset");return true;}
 internal static bool WinUsbFlushPipe(IntPtr u,byte p){Active.Calls.Add("flush");return true;}
 internal static bool WinUsbFree(IntPtr u){NpDownloadStream.Need(Active.nativeActive==0,"Freed handle during native read");Active.FreeCalls++;return true;}
}
internal sealed partial class NpDownloadStream:Stream{
 class Timing{internal long BulkBytes,BulkCalls,BulkTicks,ReadTicks,ReadySleepTicks,Replies,SourceBytes,ValidatedBytes,ValidationTicks;}
 readonly Timing timing=new Timing();
 SafeFileHandle file;IntPtr usb=(IntPtr)1;long position,length=256L*1024*1024,cacheStart=-1;byte[] cache,wireBuffer;int cachePayloadCount;uint epoch=7,sequence,maxBlocks=1020;
 bool faulted,validatedRead,qualityTestSession,qualityRequestPending,canRecoverReads=true;internal bool IsSynthetic;internal string TransportDescription="High-speed USB";
 internal Func<bool> Cancelled;internal int RecoveredReads,ReadRecoveryAttempts=0;internal string ReadRecoveryDiagnostics="test";
 internal readonly List<string> Calls=new List<string>();readonly List<int> Plans=new List<int>();readonly List<byte[]> Buffers=new List<byte[]>();
 bool Supported=true,CorruptFirst,FailFirst,StartUnknown,CancelUnknown,BadRetirementEpoch;uint capVersion=1,capMaximum=4096,deviceEpoch=7,deviceSequence,deviceRemaining,deviceLba,armed;
 int reads;internal int nativeActive,FreeCalls;internal readonly ManualResetEventSlim GateStarted=new ManualResetEventSlim(),GateContinue=new ManualResetEventSlim();int BlockRead;
 static int checks;
 sealed class NativeBulkReadFailure:IOException{internal int NativeError;internal NativeBulkReadFailure(string m,int e):base(m){NativeError=e;}}
 void CheckRecoveryCancellation(){if(Cancelled!=null&&Cancelled())throw new OperationCanceledException();}
 void RecoverOwnedRead(uint lba,int retry,NativeBulkReadFailure e){throw new Exception("Unexpected transport replay");}
 void RetireOwnedRead(uint lba,int retry,NativeBulkReadFailure e){throw new Exception("Unexpected legacy retirement");}
 byte[] Control(byte type,byte request,ushort value,ushort index,ushort size){
  Calls.Add(request.ToString("X2"));byte[] b=new byte[32];
  if(request==0x67){if(!Supported)throw new IOException("Unsupported optional query");Put(b,0,0x3143424e);Put(b,4,capVersion);Put(b,8,capMaximum);Put(b,12,1020);Put(b,16,deviceRemaining);Put(b,20,deviceEpoch);return b;}
  if(request==0x60){Put(b,0,0x3144504e);Put(b,4,1);Put(b,8,(uint)(length/512));Put(b,12,1020);Put(b,16,deviceRemaining==0?0U:3U);Put(b,20,deviceSequence);Put(b,24,deviceEpoch+(BadRetirementEpoch?1U:0));Put(b,28,1);return b;}
  if(request==0x66){armed=(uint)value|((uint)index<<16);return new byte[0];}
  if(request==0x64){deviceLba=armed;deviceRemaining=value;Plans.Add(value);if(StartUnknown)throw new IOException("START outcome unknown");return new byte[0];}
  if(request==0x61){deviceLba=(uint)value|((uint)index<<16);deviceRemaining=1;return new byte[0];}
  if(request==0x62){Need(nativeActive==0,"Cancelled while worker still owned native call");if(CancelUnknown)throw new IOException("CANCEL outcome unknown");deviceRemaining=0;deviceEpoch++;return new byte[0];}
  throw new Exception("Unexpected control "+request.ToString("X2"));
 }
 internal bool NativeRead(byte[] b,uint count,out uint n){
  int call=Interlocked.Increment(ref reads);Interlocked.Increment(ref nativeActive);
  try{
   if(call==BlockRead){GateStarted.Set();Need(GateContinue.Wait(2000),"Read gate timeout");}
   Need(deviceRemaining>0,"Unannounced bulk read");Need(count==522272,"Wrong packet read bound");
   lock(Buffers)Buffers.Add(b);
   if(FailFirst&&call==1){n=0;return false;}
   uint lba=deviceLba;deviceLba+=1020;deviceRemaining--;deviceSequence++;
   for(int i=32;i<b.Length;i++)b[i]=(byte)(lba+i-32);
   Put(b,0,0x3142504e);Put(b,4,deviceSequence);Put(b,8,lba);Put(b,12,1020);Put(b,16,0);Put(b,20,Crc(b,32,b.Length-32));Put(b,24,deviceEpoch);Put(b,28,Crc(b,0,28));
   if(CorruptFirst&&call==1)b[100]^=1;n=(uint)b.Length;return true;
  }finally{Interlocked.Decrement(ref nativeActive);}
 }
 static void Put(byte[] b,int i,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,i,4);}
 internal static void Need(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
 static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 static NpDownloadStream Make(){var s=new NpDownloadStream();NpDownloadNative.Active=s;return s;}
 static byte[] ReadBytes(NpDownloadStream s,int n){byte[] b=new byte[n];Need(s.Read(b,0,n)==n,"Short read");return b;}
 public static int Main(){
  const int packet=1020*512;
  using(var s=Make()){s.Supported=false;ReadBytes(s,1);Need(s.DownloadedBytes==packet,"Validated legacy payload not counted");ReadBytes(s,17);s.Position=0;ReadBytes(s,1);Need(s.DownloadedBytes==packet,"Cache hits counted as USB transfers");s.Position=packet;ReadBytes(s,1);Need(s.DownloadedBytes==2L*packet,"Next legacy payload not counted");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);ReadBytes(s,1);Need(s.DownloadedBytes==packet,"Burst payload not counted exactly once");ReadBytes(s,packet);Need(s.DownloadedBytes==2L*packet,"Next validated burst payload not counted");}
  using(var s=Make()){s.CorruptFirst=true;s.BeginSequentialRange(0,4L*packet);Throws<InvalidDataException>(()=>ReadBytes(s,1));Need(s.DownloadedBytes==0,"Rejected CRC inflated download speed");}
  using(var s=Make()){var field=typeof(NpDownloadStream).GetField("downloadedBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);field.SetValue(s,8L*1024*1024*1024);Need(s.DownloadedBytes==8589934592L,"Byte count truncated above4GiB");}

  using(var s=Make()){s.Supported=false;s.BeginSequentialRange(0,4L*packet);ReadBytes(s,packet+3);Need(!s.Calls.Contains("66")&&!s.Calls.Contains("64"),"Legacy query failure armed a burst");Need(s.Calls.Count(x=>x=="61")==2,"Legacy request path changed");}
  using(var s=Make()){s.qualityTestSession=true;s.BeginSequentialRange(0,4L*packet);Need(s.Calls.Count==0,"Quality test negotiated burst");}
  using(var s=Make()){s.IsSynthetic=true;s.BeginSequentialRange(0,4L*packet);Need(s.Calls.Count==0,"Synthetic source negotiated burst");}
  using(var s=Make()){s.TransportDescription="Full-speed USB";s.BeginSequentialRange(0,4L*packet);Need(s.Calls.Count==0,"Full-speed source negotiated burst");}
  using(var s=Make()){s.capVersion=2;s.BeginSequentialRange(0,4L*packet);ReadBytes(s,1);Need(!s.Calls.Contains("64"),"Unknown NBC1 version started burst");}
  using(var s=Make()){Throws<ArgumentOutOfRangeException>(()=>s.BeginSequentialRange(-1,100));Need(s.Calls.Count==0,"Invalid range sent a query");}
  using(var s=Make()){long end=259L*packet+600L*512;s.BeginSequentialRange(0,end);byte[] b=new byte[1024*1024];long total=0;while(total<end){int n=s.Read(b,0,(int)Math.Min(b.Length,end-total));Need(n>0,"Unexpected end");total+=n;}s.EndSequentialRange();Need(s.Plans.SequenceEqual(new[]{256,3}),"Burst count crossed batch/range bound");Need(s.Calls.Count(x=>x=="61")==1,"Legacy tail not used");Need(s.Calls.Count(x=>x=="62")==0,"Completed batches were unnecessarily cancelled");}
  using(var s=Make()){s.capMaximum=2;s.BeginSequentialRange(0,5L*packet);ReadBytes(s,5*packet);s.EndSequentialRange();Need(s.Plans.SequenceEqual(new[]{2,2}),"Negotiated smaller maximum exceeded");Need(s.Calls.Count(x=>x=="61")==1,"Smaller maximum lost the legacy tail");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);s.BlockRead=2;ReadBytes(s,1);Need(s.GateStarted.Wait(1000),"No overlapping read");Need(s.Buffers.Count==1,"Blocked worker wrote buffer early");byte[] immutable=s.cache;uint before=Crc(immutable,32,packet);var end=Task.Run(()=>s.EndSequentialRange());Thread.Sleep(30);Need(!end.IsCompleted&&!s.Calls.Contains("62"),"Retirement ran before worker join");s.GateContinue.Set();Need(end.Wait(2000),"Worker was not joined");Need(Crc(immutable,32,packet)==before,"Prefetch overwrote immutable cache");Need(!Object.ReferenceEquals(s.Buffers[0],s.Buffers[1]),"Buffers alias");Need(s.Calls.Count(x=>x=="62")==1,"Cancellation count differs");}
  using(var s=Make()){s.BeginSequentialRange(0,8L*packet);ReadBytes(s,1);s.Position=7L*packet+5;ReadBytes(s,20);Need(s.Calls.Count(x=>x=="62")==1,"Seek did not retire active burst once");Need(s.Calls.Count(x=>x=="61")==1,"Short seek tail did not fall back");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);s.CorruptFirst=true;byte[] output=new byte[4];Throws<InvalidDataException>(()=>s.Read(output,0,4));Need(output.All(x=>x==0),"Invalid payload was published");s.EndSequentialRange();Need(s.Calls.Count(x=>x=="62")==1,"CRC failure session was not retired");Need(s.Calls.Count(x=>x=="64")==1,"CRC failure replayed burst");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);s.FailFirst=true;Throws<NativeBulkReadFailure>(()=>ReadBytes(s,1));s.EndSequentialRange();Need(s.Calls.Count(x=>x=="64")==1&&s.Calls.Count(x=>x=="62")==1,"Native failure replayed or skipped retirement");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);s.StartUnknown=true;Throws<IOException>(()=>ReadBytes(s,1));s.EndSequentialRange();Need(s.Calls.Count(x=>x=="64")==1&&s.Calls.Count(x=>x=="62")==1,"Unknown START was replayed");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);ReadBytes(s,1);s.CancelUnknown=true;Throws<IOException>(()=>s.EndSequentialRange());s.EndSequentialRange();Need(s.Calls.Count(x=>x=="62")==1,"Unknown CANCEL was replayed");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);ReadBytes(s,1);s.Cancelled=()=>true;Throws<OperationCanceledException>(()=>ReadBytes(s,1));s.EndSequentialRange();Need(s.Calls.Count(x=>x=="62")==1,"User cancellation did not retire owned burst");}
  using(var s=Make()){s.BeginSequentialRange(0,4L*packet);ReadBytes(s,1);s.BadRetirementEpoch=true;Throws<IOException>(()=>s.EndSequentialRange());Need(s.Calls.Count(x=>x=="62")==1,"Epoch mismatch replayed CANCEL");}
  Console.WriteLine("PASS production burst/Fill/validator tests: "+checks+" assertions");return 0;
 }

/*PRODUCTION_METHODS*/
}
}
