using System;
using System.Diagnostics;
using System.IO;

// Mechanically extracts the real chunk policy and delivery loop. Modeled GATT only;
// no GUI, WinRT device, radio, recorder command, or alternate transport is loaded.
internal static class NpBleFrameWriteTests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    static string Method(string source,string marker){
        int start=source.IndexOf(marker,StringComparison.Ordinal);Need(start>=0,"Missing actual method: "+marker);
        int open=source.IndexOf('{',start),depth=0;Need(open>start,"Missing method body: "+marker);
        for(int at=open;at<source.Length;at++){if(source[at]=='{')depth++;if(source[at]=='}' && --depth==0)return source.Substring(start,at-start+1);}
        throw new Exception("Unterminated actual method: "+marker);
    }
    static int Main(string[] args)
    {
        string folder=null;
        try{
            if(args.Length!=2)throw new ArgumentException("<NpBleConnection.cs> <csc.exe>");
            string source=File.ReadAllText(args[0]);
            string policy=Method(source,"internal static int WriteChunkBytes(");
            string write=Method(source,"async Task WriteFrame(byte[] frame,int generation,CancellationToken cancel)");
            Need(write.Contains("CreateLinkedTokenSource(cancel)") && write.Contains("CancelAfter(5000)"),"Original owned five-second deadline changed.");
            Need(write.Contains("Session") && write.Contains("MaxPduSize") && write.Contains("WriteChunkBytes("),"Negotiated session is not used by the actual delivery loop.");
            Need(write.Contains("Task.Delay(3,timeout.Token)"),"Original fast-fragment pacing changed.");
            Need(!write.Contains(".Dispose()") && !write.Contains("catch") && !write.Contains("Disconnect("),"Borrowed GATT ownership or no-replay delivery changed.");
            folder=Path.Combine(Path.GetTempPath(),"NpBleFrameWrite_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string program=Path.Combine(folder,"WriteHarness.cs"),exe=Path.Combine(folder,"WriteHarness.exe");File.WriteAllText(program,Harness(policy,write));
            var compile=new ProcessStartInfo(args[1],"/nologo /target:exe /r:System.Core.dll /out:"+Quote(exe)+" "+Quote(program)){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(compile)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Actual methods failed to compile: "+output);}
            var run=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(run)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Actual write behavior failed: "+output);Console.Write(output);}
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){}}
    }
    static string Harness(string policy,string write)
    {
        return @"using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
static class NpV2SdControl { internal const uint RequestMagic=0x32444c4e; }
static class NpV2OnlineClock { internal const uint RequestMagic=0x3254504e; }
[Flags] enum GattCharacteristicProperties { Write=4,WriteWithoutResponse=8 }
enum GattWriteOption { WriteWithResponse,WriteWithoutResponse }
enum GattCommunicationStatus { Success,Unreachable }
static class BufferExtensions { public static byte[] AsBuffer(this byte[] bytes){return bytes;} }
sealed class AsyncOperation<T> {
 readonly Func<CancellationToken,Task<T>> run;
 internal AsyncOperation(Func<CancellationToken,Task<T>> run){this.run=run;}
 public Task<T> AsTask(CancellationToken token){return run(token);}
}
sealed class CancellationTokenSource : IDisposable {
 readonly System.Threading.CancellationTokenSource source;
 internal static CancellationTokenSource Last;internal static int Created;
 internal CancellationToken Parent;internal int Budget;internal bool Disposed;
 internal CancellationTokenSource(System.Threading.CancellationTokenSource source){this.source=source;Last=this;Created++;}
 public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancel){var c=new CancellationTokenSource(System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancel));c.Parent=cancel;return c;}
 public CancellationToken Token{get{return source.Token;}}
 public void CancelAfter(int ms){Budget=ms;source.CancelAfter(ms);}
 public void Cancel(){source.Cancel();}
 public void Dispose(){Disposed=true;source.Dispose();}
}
sealed class GattSession : IDisposable { public ushort MaxPduSize;internal int Disposals;public void Dispose(){Disposals++;} }
sealed class GattDeviceService : IDisposable { public GattSession Session;internal int Disposals;public void Dispose(){Disposals++;} }
sealed class Sent { internal byte[] Bytes;internal GattWriteOption Option; }
sealed class GattCharacteristic {
 public GattCharacteristicProperties CharacteristicProperties;
 internal readonly List<Sent> Sent=new List<Sent>();
 internal Func<byte[],GattWriteOption,CancellationToken,Task<GattCommunicationStatus>> Reply;
 public AsyncOperation<GattCommunicationStatus> WriteValueAsync(byte[] bytes,GattWriteOption option){
  return new AsyncOperation<GattCommunicationStatus>(token=>{
   Harness.Need(CancellationTokenSource.Last.Budget==5000 && token==CancellationTokenSource.Last.Token,""write lacks original linked deadline"");
   Sent.Add(new Sent{Bytes=(byte[])bytes.Clone(),Option=option});
   return Reply==null?Task.FromResult(GattCommunicationStatus.Success):Reply(bytes,option,token);
  });
 }
}
sealed class Connection {
 internal GattCharacteristic tx;internal GattDeviceService service;internal bool Linked=true;internal int epoch=7;
 internal bool Connected{get{return Linked && tx!=null;}}internal int Epoch{get{return Volatile.Read(ref epoch);}}
"+policy+@"
"+write+@"
 internal Task Deliver(byte[] frame,CancellationToken cancel){return WriteFrame(frame,7,cancel);}
}
static class Harness {
 static int checks;
 internal static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static byte[] Frame(uint magic){var b=Enumerable.Range(0,67).Select(i=>(byte)(i*29+7)).ToArray();b[0]=0x3c;b[1]=0xb9;b[66]=0x3e;Array.Copy(BitConverter.GetBytes(magic),0,b,2,4);return b;}
 static Connection New(int pdu,GattCharacteristicProperties properties){return new Connection{tx=new GattCharacteristic{CharacteristicProperties=properties},service=new GattDeviceService{Session=new GattSession{MaxPduSize=(ushort)pdu}}};}
 static Task<T> Failed<T>(Exception error){var t=new TaskCompletionSource<T>();t.SetException(error);return t.Task;}
 static async Task<Exception> Failure(Task task){try{await task;return null;}catch(Exception error){return error;}}
 static void Bytes(GattCharacteristic tx,byte[] frame,int[] sizes,GattWriteOption option){
  Need(tx.Sent.Count==sizes.Length,""wrong delivery count"");int at=0;
  for(int i=0;i<sizes.Length;i++){var sent=tx.Sent[i];Need(sent.Option==option && sent.Bytes.Length==sizes[i],""wrong size/ACK policy"");Need(sent.Bytes.SequenceEqual(frame.Skip(at).Take(sizes[i])),""payload changed or reordered"");at+=sizes[i];}
  Need(at==frame.Length,""incomplete delivered frame"");
 }
 static void Ownership(Connection c,GattDeviceService service,GattSession session,CancellationToken cancel){
  Need(CancellationTokenSource.Last.Disposed && CancellationTokenSource.Last.Parent==cancel && CancellationTokenSource.Last.Budget==5000,""linked deadline not retired"");
  Need(service.Disposals==0 && (session==null || session.Disposals==0),""borrowed service/session was disposed"");
 }
 static void Policy(){
  foreach(uint magic in new[]{NpV2SdControl.RequestMagic,NpV2OnlineClock.RequestMagic}){
   var b=Frame(magic);
   for(int pdu=0;pdu<=90;pdu++)foreach(bool ack in new[]{false,true})Need(Connection.WriteChunkBytes(b,pdu,ack)==(ack && pdu>=70?67:20),""PDU/ACK admission boundary"");
   for(int field=0;field<3;field++){var bad=(byte[])b.Clone();bad[new[]{0,1,66}[field]]^=1;Need(Connection.WriteChunkBytes(bad,70,true)==20,""malformed envelope admitted"");}
   foreach(int size in new[]{0,6,66,68,127})Need(Connection.WriteChunkBytes(new byte[size],70,true)==20,""non-control length admitted"");
   // Fixed bodies may contain framing delimiters. Do not truncate/resync them.
   b[12]=0x3c;b[13]=0xb9;b[20]=0x3e;Need(Connection.WriteChunkBytes(b,70,true)==67,""embedded body delimiters rejected"");
  }
  Need(Connection.WriteChunkBytes(null,70,true)==20 && Connection.WriteChunkBytes(Frame(NpV2SdControl.RequestMagic),-1,true)==20,""unknown/invalid MTU admitted"");
  foreach(uint magic in new[]{0x31444c4eU,0x3243504eU,0x3256504eU,0U,0xffffffffU})Need(Connection.WriteChunkBytes(Frame(magic),90,true)==20,""V1/config/preview/unknown policy changed"");
 }
 static async Task Delivery(){
  var both=GattCharacteristicProperties.Write|GattCharacteristicProperties.WriteWithoutResponse;
  foreach(uint magic in new[]{NpV2SdControl.RequestMagic,NpV2OnlineClock.RequestMagic}){
   var frame=Frame(magic);
   foreach(var properties in new[]{GattCharacteristicProperties.Write,both}){
    var c=New(70,properties);var service=c.service;var session=service.Session;await c.Deliver(frame,CancellationToken.None);
    Bytes(c.tx,frame,new[]{67},GattWriteOption.WriteWithResponse);Ownership(c,service,session,CancellationToken.None);
   }
  }
  foreach(int pdu in new[]{0,23,69}){
   var c=New(pdu,both);var service=c.service;var session=service.Session;var b=Frame(NpV2SdControl.RequestMagic);await c.Deliver(b,CancellationToken.None);
   Bytes(c.tx,b,new[]{20,20,20,7},GattWriteOption.WriteWithoutResponse);Ownership(c,service,session,CancellationToken.None);
  }
  foreach(int pdu in new[]{69,70}){
   var c=New(pdu,both);var b=Frame(NpV2OnlineClock.RequestMagic);b[12]=0x3c;b[13]=0xb9;b[20]=0x3e;await c.Deliver(b,CancellationToken.None);
   Bytes(c.tx,b,pdu==70?new[]{67}:new[]{20,20,20,7},pdu==70?GattWriteOption.WriteWithResponse:GattWriteOption.WriteWithoutResponse);
  }
  var nullSession=New(70,both);nullSession.service.Session=null;var saved=nullSession.service;var body=Frame(NpV2OnlineClock.RequestMagic);await nullSession.Deliver(body,CancellationToken.None);
  Bytes(nullSession.tx,body,new[]{20,20,20,7},GattWriteOption.WriteWithoutResponse);Ownership(nullSession,saved,null,CancellationToken.None);
  var nullService=New(70,both);nullService.service=null;await nullService.Deliver(body,CancellationToken.None);Bytes(nullService.tx,body,new[]{20,20,20,7},GattWriteOption.WriteWithoutResponse);
  Need(CancellationTokenSource.Last.Disposed,""null-service fallback leaked deadline"");
  var fastOnly=New(90,GattCharacteristicProperties.WriteWithoutResponse);await fastOnly.Deliver(body,CancellationToken.None);Bytes(fastOnly.tx,body,new[]{20,20,20,7},GattWriteOption.WriteWithoutResponse);
  var smallAck=New(69,GattCharacteristicProperties.Write);await smallAck.Deliver(body,CancellationToken.None);Bytes(smallAck.tx,body,new[]{20,20,20,7},GattWriteOption.WriteWithResponse);
  foreach(uint magic in new[]{0x31444c4eU,0x3243504eU,0x3256504eU,0U}){var c=New(90,both);var b=Frame(magic);await c.Deliver(b,CancellationToken.None);Bytes(c.tx,b,new[]{20,20,20,7},GattWriteOption.WriteWithoutResponse);}
  var legacy=New(90,both);var shortFrame=new byte[]{0x3c,0x82,0x81,0,0,0x3e};await legacy.Deliver(shortFrame,CancellationToken.None);Bytes(legacy.tx,shortFrame,new[]{6},GattWriteOption.WriteWithoutResponse);
  // The borrowed transmitter/chunk choice stays fixed for this delivery.
  var borrowed=New(69,both);var original=borrowed.tx;var other=new GattCharacteristic{CharacteristicProperties=both};var service2=borrowed.service;var session2=service2.Session;
  original.Reply=(b,o,t)=>{borrowed.tx=other;session2.MaxPduSize=90;return Task.FromResult(GattCommunicationStatus.Success);};await borrowed.Deliver(body,CancellationToken.None);
  Bytes(original,body,new[]{20,20,20,7},GattWriteOption.WriteWithoutResponse);Need(other.Sent.Count==0,""transmitter replaced during delivery"");Ownership(borrowed,service2,session2,CancellationToken.None);
 }
 static async Task Aborts(){
  var frame=Frame(NpV2SdControl.RequestMagic);var both=GattCharacteristicProperties.Write|GattCharacteristicProperties.WriteWithoutResponse;
  foreach(int pdu in new[]{69,70}){
   var c=New(pdu,both);c.tx.Reply=(b,o,t)=>Task.FromResult(GattCommunicationStatus.Unreachable);var e=await Failure(c.Deliver(frame,CancellationToken.None));
   Need(e is IOException && c.tx.Sent.Count==1,""failed write replayed or accepted"");Ownership(c,c.service,c.service.Session,CancellationToken.None);
   c=New(pdu,both);c.tx.Reply=(b,o,t)=>Failed<GattCommunicationStatus>(new IOException(""native write failure""));e=await Failure(c.Deliver(frame,CancellationToken.None));Need(e is IOException && c.tx.Sent.Count==1,""native exception replayed"");
   c=New(pdu,both);c.tx.Reply=async (b,o,t)=>{CancellationTokenSource.Last.Cancel();await Task.Delay(1,t);return GattCommunicationStatus.Success;};e=await Failure(c.Deliver(frame,CancellationToken.None));Need(e is OperationCanceledException && c.tx.Sent.Count==1 && CancellationTokenSource.Last.Disposed,""owned deadline cancel replayed or leaked"");
   c=New(pdu,both);c.tx.Reply=(b,o,t)=>{c.epoch++;return Task.FromResult(GattCommunicationStatus.Success);};e=await Failure(c.Deliver(frame,CancellationToken.None));Need(e is IOException && c.tx.Sent.Count==1,""epoch change accepted/replayed"");
   c=New(pdu,both);c.tx.Reply=(b,o,t)=>{c.Linked=false;return Task.FromResult(GattCommunicationStatus.Success);};e=await Failure(c.Deliver(frame,CancellationToken.None));Need(e is IOException && c.tx.Sent.Count==1,""disconnect accepted/replayed"");
  }
  using(var cancel=new System.Threading.CancellationTokenSource()){
   cancel.Cancel();var c=New(70,both);var e=await Failure(c.Deliver(frame,cancel.Token));Need(e is OperationCanceledException && c.tx.Sent.Count==0,""pre-canceled delivery sent bytes"");Ownership(c,c.service,c.service.Session,cancel.Token);
  }
  using(var cancel=new System.Threading.CancellationTokenSource()){
   var c=New(69,both);c.tx.Reply=(b,o,t)=>{cancel.Cancel();return Task.FromResult(GattCommunicationStatus.Success);};var e=await Failure(c.Deliver(frame,cancel.Token));Need(e is OperationCanceledException && c.tx.Sent.Count==1,""caller cancellation replayed remaining fragments"");Ownership(c,c.service,c.service.Session,cancel.Token);
  }
  foreach(bool disconnected in new[]{false,true}){
   var c=New(70,both);if(disconnected)c.Linked=false;else c.epoch++;int made=CancellationTokenSource.Created;
   var e=await Failure(c.Deliver(frame,CancellationToken.None));Need(e is IOException && c.tx.Sent.Count==0 && CancellationTokenSource.Created==made,""stale delivery touched endpoint"");
  }
 }
 static int Main(){try{Policy();Delivery().GetAwaiter().GetResult();Aborts().GetAwaiter().GetResult();Console.WriteLine(""PASS actual chunk policy/write loop; checks=""+checks+""; modeled GATT only, no radio/MTU qualification"");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}";
    }
}
