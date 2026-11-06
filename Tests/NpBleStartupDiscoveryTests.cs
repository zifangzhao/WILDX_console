using System;
using System.Diagnostics;
using System.IO;

// Execute the actual initial discovery method with a bounded fake transport.
// No WinRT device, radio, application assembly or recorder operation is used.
static class NpBleStartupDiscoveryTests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static string Method(string source,string marker){
        int start=source.IndexOf(marker,StringComparison.Ordinal);Need(start>=0,"Missing method: "+marker);
        int open=source.IndexOf('{',start),depth=0;
        for(int at=open;at<source.Length;at++){if(source[at]=='{')depth++;if(source[at]=='}' && --depth==0)return source.Substring(start,at-start+1);}
        throw new Exception("Unclosed method: "+marker);
    }
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    static int Main(string[] args){
        string folder=null;
        try{
            Need(args.Length==2,"<NpBleConnection.cs> <csc.exe>");
            string source=File.ReadAllText(args[0]);
            string initial=Method(source,"internal async Task<Tuple<int,NpSdStatus>> ReadInitialDevice(");
            string read=Method(source,"internal static async Task<Tuple<int,NpSdStatus>> ReadDevice(");
            string version=Method(source,"internal static int DeviceVersion(");
            string parse=Method(source,"internal static NpSdStatus ParseStatus(");
            string discovery=Method(source,"static bool DiscoveryRequest(");
            string form=File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"NpBleForm.cs"));
            string status=Method(form,"async Task<NpSdStatus> ReadStatus()");
            Need(status.Contains("if(recorderVersion==0)") && status.Contains("connection.ReadInitialDevice(Next)"),"GUI initial status bypasses readiness discovery.");
            Need(initial.Contains("30000") && initial.Contains("catch(TimeoutException") && initial.Contains("Task.Delay(250") && initial.Contains("ReadDevice("),"Missing finite read-only timeout retry policy.");
            Need(!initial.Contains("RescueReset") && !initial.Contains("WriteFrame") && !initial.Contains("Send("),"Initial discovery invokes a mutation path.");
            folder=Path.Combine(Path.GetTempPath(),"NpBleStartup_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string program=Path.Combine(folder,"Harness.cs"),exe=Path.Combine(folder,"Harness.exe");
            File.WriteAllText(program,Harness.Replace("ACTUAL_INITIAL",initial).Replace("ACTUAL_READ",read).Replace("ACTUAL_VERSION",version).Replace("ACTUAL_PARSE",parse).Replace("ACTUAL_DISCOVERY",discovery));
            var compile=new ProcessStartInfo(args[1],"/nologo /target:exe /r:System.Core.dll /out:"+Quote(exe)+" "+Quote(program)){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=Process.Start(compile)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();Need(p.ExitCode==0,"Harness compile failed: "+output);}
            var run=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=Process.Start(run)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();Need(p.ExitCode==0,"Harness failed: "+output);Console.Write(output);}
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){}}
    }
    const string Harness=@"using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
sealed class CancellationTokenSource : IDisposable {
 readonly System.Threading.CancellationTokenSource inner;internal static CancellationTokenSource Last;internal static int Created,Disposed;internal int Budget;
 public CancellationTokenSource(int ms){inner=new System.Threading.CancellationTokenSource();Last=this;Created++;CancelAfter(ms);}
 public CancellationToken Token{get{return inner.Token;}}public bool IsCancellationRequested{get{return inner.IsCancellationRequested;}}
 public void CancelAfter(int ms){Budget=ms;inner.CancelAfter(ms);}public void Cancel(){inner.Cancel();}public void Dispose(){Disposed++;inner.Dispose();}
}
sealed class NpSdStatus {internal uint Error;}
sealed class NpSdCommandException : IOException {internal NpSdCommandException(int version,NpSdStatus status,string message):base(""Device fault ""+status.Error){}}
static class NpSdGeometry {internal static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}}
static class NpSdControl {
 internal const uint ReplyMagic=0x31524c4e;
 internal static byte[] Request(uint op,uint id){return Wire.Request(1,op,id);}
 internal static NpSdStatus Parse(byte[] b,uint op,uint id){return Wire.Parse(b,1,op,id);}
}
static class NpV2SdControl {
 internal const uint ReplyMagic=0x32524c4e;
 internal static byte[] Request(uint op,uint id){return Wire.Request(2,op,id);}
 internal static NpSdStatus Parse(byte[] b,uint op,uint id){return Wire.Parse(b,2,op,id);}
 internal static string StartStorageError(NpSdStatus status){return ""storage fault"";}
}
static class Wire {
 internal static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
 internal static byte[] Request(int family,uint op,uint id){var b=new byte[64];Put(b,0,family==2?0x32444c4eU:0x31444c4eU);Put(b,4,(uint)family);Put(b,8,op);Put(b,12,id);return b;}
 internal static byte[] Reply(int family,uint op,uint id,uint error=0){var b=Request(family,op,id);Put(b,0,family==2?NpV2SdControl.ReplyMagic:NpSdControl.ReplyMagic);Put(b,16,error);return b;}
 internal static NpSdStatus Parse(byte[] b,int family,uint op,uint id){if(b==null || b.Length!=64 || BitConverter.ToUInt32(b,0)!=(family==2?NpV2SdControl.ReplyMagic:NpSdControl.ReplyMagic) || BitConverter.ToUInt32(b,4)!=(uint)family || BitConverter.ToUInt32(b,8)!=op || BitConverter.ToUInt32(b,12)!=id)throw new IOException(""Malformed status"");return new NpSdStatus{Error=BitConverter.ToUInt32(b,16)};}
}
sealed class Link {
 readonly object receiveLock=new object();internal CancellationTokenSource connectTimeout;internal bool connectCanceled;int epoch=7;internal bool Connected=true;
 internal int Epoch{get{return Volatile.Read(ref epoch);}}
 internal readonly List<uint> StatusIds=new List<uint>();internal int Reads,DiscoveryReads;internal Func<byte,byte[],CancellationToken,Task<byte[]>> Transport;
 internal Task<byte[]> ExchangeAsync(byte command,byte[] request,CancellationToken cancel){
  cancel.ThrowIfCancellationRequested();Reads++;
  if(command==0x82){Checks.Need(request.SequenceEqual(new byte[]{0x81,0,0}),""discovery mutated payload"");DiscoveryReads++;}
  else{Checks.Need(command==0xb9 && request.Length==64 && BitConverter.ToUInt32(request,8)==0,""initial discovery sent mutation"");StatusIds.Add(BitConverter.ToUInt32(request,12));}
  return Transport(command,request,cancel);
 }
 internal void Disconnect(){lock(receiveLock){connectCanceled=true;Connected=false;epoch++;if(connectTimeout!=null)connectTimeout.Cancel();}}
 internal void Bounce(){lock(receiveLock){Connected=false;epoch++;Connected=true;}}
 void TraceConnect(Stopwatch elapsed,string stage){}
 ACTUAL_INITIAL
 ACTUAL_READ
 ACTUAL_VERSION
 ACTUAL_PARSE
 ACTUAL_DISCOVERY
}
static class Checks {
 static int checks;internal static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static byte[] Reply(byte cmd,byte[] req,int family=2,uint error=0){return Wire.Reply(family,0,cmd==0x82?1:BitConverter.ToUInt32(req,12),error);}
 static async Task<Exception> Failure(Task task){try{await task;return null;}catch(Exception e){return e;}}
 static async Task Run(){
  foreach(int family in new[]{1,2}){
   var r=new Link();uint id=1;int misses=2;r.Transport=(cmd,req,t)=>{if(cmd==0xb9 && misses-->0)throw new TimeoutException(""bridge not ready"");return Task.FromResult(Reply(cmd,req,family));};
   var result=await r.ReadInitialDevice(()=>++id);
   Need(result.Item1==family && result.Item2.Error==0 && r.StatusIds.SequenceEqual(new uint[]{2,3,4}),""retry/fresh status IDs"");
   Need(r.DiscoveryReads==3 && r.Reads==6 && r.connectTimeout==null,""read-only retry bound/owned retirement"");
  }
  var deadline=new Link();deadline.Transport=(cmd,req,t)=>{Need(CancellationTokenSource.Last.Budget==30000,""initial deadline not30sec"");CancellationTokenSource.Last.Cancel();throw new TimeoutException(""not ready"");};
  uint next=2;var failure=await Failure(deadline.ReadInitialDevice(()=>next++));
  Need(failure!=null && deadline.Reads==1 && deadline.connectTimeout==null,""expired deadline retried or leaked"");
  var pending=new Link();var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  pending.Transport=async(cmd,req,t)=>{entered.TrySetResult(true);await Task.Delay(10000,t);return Reply(cmd,req);};
  var attempt=pending.ReadInitialDevice(()=>next++);await entered.Task;var watch=Stopwatch.StartNew();pending.Disconnect();failure=await Failure(attempt);
  Need(failure!=null && watch.ElapsedMilliseconds<300 && pending.Reads==1 && pending.connectTimeout==null,""explicit close did not cancel owned discovery promptly"");
  var bounced=new Link();bounced.Transport=(cmd,req,t)=>{bounced.Bounce();throw new TimeoutException(""old epoch"");};
  failure=await Failure(bounced.ReadInitialDevice(()=>next++));Need(failure!=null && bounced.Reads==1 && bounced.StatusIds.Count==0 && bounced.connectTimeout==null,""epoch change sent a later read"");
  foreach(string mode in new[]{""fault"",""malformed"",""io""}){
   var r=new Link();r.Transport=(cmd,req,t)=>{if(mode==""io"")throw new IOException(""transport rejected"");return Task.FromResult(mode==""malformed""?new byte[3]:Reply(cmd,req,2,8));};
   failure=await Failure(r.ReadInitialDevice(()=>next++));Need(failure is IOException && r.Reads==1 && r.connectTimeout==null,mode+"" was retried/accepted"");
   if(mode==""fault"")Need(failure is NpSdCommandException,""valid device fault lost trusted classification"");
  }
  Need(CancellationTokenSource.Created==CancellationTokenSource.Disposed,""owned deadline leaked"");
 }
 static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine(""PASS actual initial discovery readiness; checks=""+checks+""; modeled transport/deadline, no radio"");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
";
}
