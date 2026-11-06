using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using USB512_interface;
using CE32_console;

// Minimal modeled WinRT surface. Marker methods below are mechanically copied
// from production; no Form, device enumeration or physical radio is loaded.
namespace CE32_console {
 static class NpSdControl {internal static uint Crc(byte[] b,int n=60){return NpFirstSyncWire.Crc(b,n);}}
 static class NpSdGeometry {internal static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}}
}
namespace USB512_interface {
 static class NpV2SdControl {internal const uint RequestMagic=0x32444c4e;}
 static class NpV2PreviewBurst {internal const uint Magic=0x3256504e;}
 static class NpV1PreviewBurst {internal const uint Magic=0x3156504e;}
 [Flags]enum GattCharacteristicProperties {Write=4,WriteWithoutResponse=8}
 enum GattWriteOption {WriteWithResponse,WriteWithoutResponse}
 enum GattCommunicationStatus {Success,Unreachable}
 static class Buffers {internal static byte[] AsBuffer(this byte[] b){return b;}}
 sealed class Operation {internal Task<GattCommunicationStatus> Task;internal Task<GattCommunicationStatus> AsTask(CancellationToken t){t.ThrowIfCancellationRequested();return Task;}}
 sealed class GattCharacteristic {
  internal GattCharacteristicProperties CharacteristicProperties=GattCharacteristicProperties.Write|GattCharacteristicProperties.WriteWithoutResponse;
  internal readonly List<byte[]> Sent=new List<byte[]>();internal long LastInvoked;internal Func<byte[],Task<GattCommunicationStatus>> Send;
  internal Operation WriteValueAsync(byte[] b,GattWriteOption o){LastInvoked=Stopwatch.GetTimestamp();Sent.Add((byte[])b.Clone());return new Operation {Task=Send==null?Task.FromResult(GattCommunicationStatus.Success):Send(b)};}
 }
 sealed class GattSession {internal ushort MaxPduSize=23;}
 sealed class GattService {internal GattSession Session=new GattSession();}
 sealed class GattValueChangedEventArgs {internal byte[] CharacteristicValue;}
 sealed class DataReader : IDisposable {readonly byte[] b;DataReader(byte[] v){b=v;}internal static DataReader FromBuffer(byte[] v){return new DataReader(v);}internal uint UnconsumedBufferLength {get{return (uint)b.Length;}}internal void ReadBytes(byte[] d){b.CopyTo(d,0);}public void Dispose(){}}
 sealed class NpBleConnection {
  readonly object receiveLock=new object();readonly NpBleWire wire=new NpBleWire();internal readonly GattCharacteristic rx=new GattCharacteristic(),tx=new GattCharacteristic();
  internal readonly GattService service=new GattService();internal int epoch=7;internal bool Connected=true;internal int Epoch {get{return epoch;}}
  internal NpBleClockTransport clockTransport;TaskCompletionSource<byte[]> receipt;Func<byte,byte[],bool> matches;
  internal Action<int,byte[]> PreviewReceived=null;
ACTUAL_READ_ONLY
ACTUAL_POLICY
ACTUAL_WRITE
ACTUAL_RECEIVE
  internal Task<byte[]> Wait(byte[] request,NpBleClockTransport observer){clockTransport=observer;receipt=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);matches=(c,b)=>{try{if(c!=0xb9)return false;if(NpFineClockWire.IsRequest(request)){NpFineClockWire.ParseReply(b,request,BitConverter.ToUInt16(b,6)>>8);return true;}CE32_console.NpV2OnlineClock.Parse(b,0,BitConverter.ToUInt32(request,12));return true;}catch{return false;}};return receipt.Task;}
  internal void Feed(byte[] b,bool wrongSender=false){ValueChanged(wrongSender?new GattCharacteristic():rx,new GattValueChangedEventArgs{CharacteristicValue=b});}
  internal Task Send(byte[] frame){return WriteFrame(frame,7,CancellationToken.None);}
 }
}
static class NpBleTransportBoundaryTests {
 static int checks;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 static void Need(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static void Put(byte[] b,int at,uint value){BitConverter.GetBytes(value).CopyTo(b,at);}
 static byte[] Reply(uint id){var b=new byte[64];Put(b,0,0x3252504e);Put(b,4,1);Put(b,12,id);Put(b,20,100);Put(b,24,101);Put(b,52,1000);Put(b,60,NpFirstSyncWire.Crc(b,60));return b;}
 static byte[] Frame(byte[] body,byte cmd=0xb9,bool ad=true){var b=new byte[body.Length+(ad?4:3)];b[0]=ad?(byte)0xad:(byte)0x3c;b[1]=cmd;body.CopyTo(b,2);b[b.Length-1]=ad?(byte)0xda:(byte)0x3e;if(ad)b[b.Length-2]=0xda;return b;}
 static byte[] Request(uint id){return CE32_console.NpV2OnlineClock.Request(0,id);}
 static Dictionary<string,object> Snapshot(NpBleClockTransport t,long full=1,long continuation=Int64.MaxValue){return Json.Deserialize<Dictionary<string,object>>(Json.Serialize(t.Evidence(full,continuation)));}
 static long L(object n){return Convert.ToInt64(n);}
 static void Reassembly(){
  foreach(bool ad in new[]{false,true})for(int cut=1;cut<(ad?68:67);cut++){
   var q=Request(41);var t=new NpBleClockTransport(q);var c=new NpBleConnection();var wait=c.Wait(q,t);var b=Frame(Reply(41),ad:ad);
   c.Feed(b.Take(cut).ToArray());Need(!wait.IsCompleted,"truncated reply completed a transaction");c.Feed(b.Skip(cut).ToArray());Need(wait.IsCompleted,"fragmented valid reply lost");
   var d=Snapshot(t);Need((bool)d["reply_id_crc_correlated"]&&L(d["reply_start_byte"])==0&&L(d["reply_end_byte"])==b.Length&&L(d["reply_start_notification"])==1&&L(d["reply_complete_notification"])==2,"accepted frame lost notification attribution");
  }
  var request=Request(42);var observer=new NpBleClockTransport(request);var peer=new NpBleConnection();var task=peer.Wait(request,observer);byte[] valid=Frame(Reply(42));
  var preview=new byte[224];Put(preview,0,NpV2PreviewBurst.Magic);valid.CopyTo(preview,60);byte[] outer=Frame(preview,0xba);
  peer.Feed(outer.Take(100).ToArray());peer.Feed(outer.Skip(100).Concat(valid.Take(7)).ToArray());Need(!task.IsCompleted,"clock bytes inside a preview payload satisfied control correlation");peer.Feed(valid.Skip(7).ToArray());
  var result=Snapshot(observer);Need(task.IsCompleted&&L(result["reply_start_byte"])==outer.Length&&L(result["reply_start_notification"])==2&&L(result["reply_complete_notification"])==3,"payload false positive moved the real reply timestamp");
  var saved=Json.Serialize(result);peer.Feed(valid);Need(Json.Serialize(Snapshot(observer))==saved,"duplicate reply overwrote frozen attribution");
  foreach(string bad in new[]{"id","crc","tail","truncated"}){
   observer=new NpBleClockTransport(request);peer=new NpBleConnection();task=peer.Wait(request,observer);byte[] prefix=Frame(Reply(bad=="id"?99U:42U));
   if(bad=="crc")prefix[63]^=1;if(bad=="tail")prefix[prefix.Length-1]^=1;if(bad=="truncated")prefix=prefix.Take(9).Concat(new byte[20]).ToArray();
   peer.Feed(prefix);Need(!task.IsCompleted,"uncorrelated/malformed receipt completed request: "+bad);peer.Feed(valid);
   result=Snapshot(observer);Need(task.IsCompleted&&(bool)result["reply_id_crc_correlated"]&&L(result["reply_start_byte"])==prefix.Length,"malformed prefix moved accepted start: "+bad);
  }
  observer=new NpBleClockTransport(request);peer=new NpBleConnection();task=peer.Wait(request,observer);peer.Feed(valid,true);Need(!task.IsCompleted&&L(Snapshot(observer)["notifications_seen"])==0,"wrong characteristic contributed timing");
  for(int i=0;i<200;i++)peer.Feed(new byte[]{255});peer.Feed(valid.Take(1).ToArray());peer.Feed(valid.Skip(1).ToArray());result=Snapshot(observer);
  Need(task.IsCompleted&&(bool)result["reply_id_crc_correlated"]&&L(result["notification_segments_dropped"])>0&&Json.ConvertToType<object[]>(result["notification_segments"]).Length==128,"notification ledger was unbounded or lost a retained reply");
  var wire=new NpBleWire();wire.Feed(new byte[]{1,2,3});Need(wire.ReceivedBytes==3,"discarded bytes lost absolute accounting");wire.Clear();Need(wire.ReceivedBytes==0,"epoch clear kept old frame coordinates");
  Need(typeof(NpBleWire).GetMethod("Feed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!=null,"legacy reflection Feed API became ambiguous");
  observer=new NpBleClockTransport(request);peer=new NpBleConnection();task=peer.Wait(request,observer);byte[] unrelated=Frame(Reply(99));
  peer.Feed(new byte[]{7,8,9}.Concat(unrelated).Concat(valid).Concat(unrelated).ToArray());result=Snapshot(observer);
  Need(task.IsCompleted&&L(result["reply_start_byte"])==3+unrelated.Length&&L(result["reply_start_notification"])==1&&L(result["reply_complete_notification"])==1,"coalesced replies were attributed to unrelated data");
 }
 static void JournalAndWrite(string root){
  string folder=Path.Combine(root,"deferred");var q=Request(53);var journal=new NpBleClockEvidence(folder,"BLE-modeled",7);long full=Stopwatch.GetTimestamp();var attempt=journal.Begin(q,full,null);
  Need(!Directory.Exists(folder),"read-only Begin performed journal I/O before the transport boundary");var peer=new NpBleConnection();var wait=peer.Wait(q,attempt.Transport);
  peer.Send(NpBleWire.Frame(0xb9,q)).GetAwaiter().GetResult();Need(!Directory.Exists(folder)&&peer.tx.Sent.Select(x=>x.Length).SequenceEqual(new[]{20,20,20,7}),"write policy changed or probe flushed during delivery");
  byte[] b=Frame(Reply(53));peer.Feed(b.Take(10).ToArray());peer.Feed(b.Skip(10).ToArray());Need(wait.IsCompleted,"actual callback failed to resolve response");
  long delayed=Stopwatch.GetTimestamp()+Stopwatch.Frequency;journal.Finish(attempt,wait.Result,delayed,null);journal.Dispose();
  var rows=File.ReadAllLines(Path.Combine(folder,"ble_clock_exchange_evidence.jsonl")).Select(x=>Json.Deserialize<Dictionary<string,object>>(x)).ToArray();var finish=rows.Single(x=>Convert.ToString(x["event"])=="exchange_finished");var d=(Dictionary<string,object>)finish["transport_boundaries"];
  Need(L(finish["host_send_qpc"])==full&&L(finish["host_receive_qpc"])==delayed,"legacy full/continuation bounds changed");
  Need((bool)d["reply_id_crc_correlated"]&&(bool)d["boundary_order_valid"]&&!(bool)d["clock_bounds_applied"],"diagnostic bounds were applied or lacked correlation");
  Need(L(d["last_gatt_fragment_send_qpc"])<=peer.tx.LastInvoked&&L(d["reply_start_notification_qpc"])<=L(d["reply_complete_notification_qpc"])&&L(d["reply_complete_notification_qpc"])<delayed-Stopwatch.Frequency/2,"async continuation delay was mistaken for notification arrival");
  var writes=Json.ConvertToType<Dictionary<string,object>[]>(d["write_fragments"]);Need(writes.Length==4&&L(writes[3]["offset"])==60&&L(writes[3]["bytes"])==7&&writes.All(x=>(bool)x["success"]),"last fragment identity/completions missing");
  Need(peer.tx.Sent.SelectMany(x=>x).SequenceEqual(NpBleWire.Frame(0xb9,q)),"timing observer changed request bytes");
  var whole=new NpBleConnection();var wholeTransport=new NpBleClockTransport(q);whole.Wait(q,wholeTransport);whole.service.Session.MaxPduSize=70;whole.Send(NpBleWire.Frame(0xb9,q)).GetAwaiter().GetResult();
  var wholeWrites=Json.ConvertToType<Dictionary<string,object>[]>(Snapshot(wholeTransport)["write_fragments"]);Need(whole.tx.Sent.Count==1&&wholeWrites.Length==1&&L(wholeWrites[0]["bytes"])==67,"whole-frame control timing changed negotiated delivery");
  var pending=new NpBleClockEvidence(Path.Combine(root,"unfinished"),"BLE-modeled",8);pending.Begin(Request(54),delayed,null);pending.Dispose();var text=File.ReadAllText(Path.Combine(root,"unfinished","ble_clock_exchange_evidence.jsonl"));Need(text.Contains("exchange_started")&&text.Contains("unfinished_request_ids"),"deferred incomplete request disappeared on close");
  Need(journal.Begin(CE32_console.NpV2OnlineClock.Request(1,55),full,null)==null,"clock commit entered read-only observer");
  string failedPath=Path.Combine(root,"not-a-folder");File.WriteAllText(failedPath,"sentinel");var failed=new NpBleClockEvidence(failedPath,"BLE-modeled",9);var f=failed.Begin(Request(56),full,null);
  failed.Finish(f,null,delayed,new OperationCanceledException("control priority"));failed.Dispose();Need(failed.Error!=null&&File.ReadAllText(failedPath)=="sentinel","metadata failure affected transport or changed original bytes");
  var cancelled=new NpBleClockEvidence(Path.Combine(root,"cancelled"),"BLE-modeled",10);var a=cancelled.Begin(Request(57),full,null);cancelled.Finish(a,null,delayed,new OperationCanceledException("control priority"));cancelled.Dispose();
  Need(File.ReadAllText(Path.Combine(root,"cancelled","ble_clock_exchange_evidence.jsonl")).Contains("\"outcome\":\"cancelled\""),"deferred cancellation evidence was lost");
 }
 static void FineFrames(string root){
  foreach(int family in new[]{1,2})foreach(bool ad in new[]{false,true})foreach(int version in new[]{1,2}){
   string uid=family==1?"001D00065942500B20313354":"001A0004434250062039314E";
   var q=version==1?NpFineClockWire.Request(uid,71,0x1020304050607080UL):NpFineClockWire.RequestDeferred(uid,71,0x1020304050607080UL,4000);
   byte[] reply=NpFineClockWire.Reply(q,family,new NpFineClockReply {Flags=7,Domain=123,Hz=1200000,Generation=9,SourceEpoch=11,RxMs=10,RxCounter=12000,TxMs=11,TxCounter=13200,UartBaud=115201});
   var bytes=Frame(reply,ad:ad);
   for(int cut=1;cut<bytes.Length;cut++){
    var transport=new NpBleClockTransport(q);var peer=new NpBleConnection();var waiting=peer.Wait(q,transport);
    peer.Feed(bytes.Take(cut).ToArray());Need(!waiting.IsCompleted,"truncated NPRF completed: "+cut);
    peer.Feed(bytes.Skip(cut).ToArray());var evidence=Snapshot(transport);
    Need(waiting.IsCompleted&&(bool)evidence["reply_id_crc_correlated"]&&L(evidence["reply_end_byte"])==bytes.Length,"fragmented NPRF lost exact accepted-frame range");
   }
   foreach(string error in new[]{"nonce","crc","family","tail","version","hold"}){
    byte[] bad=(byte[])bytes.Clone();
    if(error=="nonce"){bad[26]^=1;Put(bad,78,NpFirstSyncWire.Crc(bad.Skip(2).Take(80).ToArray(),76));}
    if(error=="crc")bad[78]^=1;
    if(error=="family"){bad[9]=3;Put(bad,78,NpFirstSyncWire.Crc(bad.Skip(2).Take(80).ToArray(),76));}
    if(error=="tail")bad[bad.Length-1]^=1;
    if(error=="version"){bad[6]=(byte)(version==1?2:1);Put(bad,78,NpFirstSyncWire.Crc(bad.Skip(2).Take(80).ToArray(),76));}
    if(error=="hold"){Put(bad,70,version==1?1U:3999U);Put(bad,78,NpFirstSyncWire.Crc(bad.Skip(2).Take(80).ToArray(),76));}
    var transport=new NpBleClockTransport(q);var peer=new NpBleConnection();var waiting=peer.Wait(q,transport);peer.Feed(bad);
    Need(!waiting.IsCompleted,"uncorrelated NPRF completed: "+error);peer.Feed(bytes);
    Need(waiting.IsCompleted&&L(Snapshot(transport)["reply_start_byte"])==bad.Length,"bad NPRF changed following valid frame range: "+error);
   }
   var preview=new byte[224];Put(preview,0,NpV2PreviewBurst.Magic);bytes.CopyTo(preview,60);byte[] outer=Frame(preview,0xba);
   var observed=new NpBleClockTransport(q);var connection=new NpBleConnection();var task=connection.Wait(q,observed);
   connection.Feed(outer);Need(!task.IsCompleted,"NPRF bytes inside preview acquired timing");connection.Feed(bytes);
   Need(task.IsCompleted&&L(Snapshot(observed)["reply_start_byte"])==outer.Length,"real NPRF was confused with preview payload");
   string path=Path.Combine(root,"fine-"+family+"-"+ad+"-v"+version);var journal=new NpBleClockEvidence(path,"BLE-modeled",7);long start=Stopwatch.GetTimestamp();var attempt=journal.Begin(q,start,null);
   Need(attempt!=null&&attempt.Id==71&&!Directory.Exists(path),"fine read-only attempt lost ID or deferred I/O");
   connection=new NpBleConnection();task=connection.Wait(q,attempt.Transport);connection.Send(NpBleWire.Frame(0xb9,q)).GetAwaiter().GetResult();connection.Feed(bytes);
   journal.Finish(attempt,task.Result,Stopwatch.GetTimestamp(),null);journal.Dispose();
   var finish=File.ReadAllLines(Path.Combine(path,"ble_clock_exchange_evidence.jsonl")).Select(x=>Json.Deserialize<Dictionary<string,object>>(x)).Single(x=>Convert.ToString(x["event"])=="exchange_finished");
   Need(Convert.ToString(finish["clock_protocol"])=="NPRF"+version&&L(finish["requested_hold_us"])==(version==1?0:4000)&&(bool)finish["fine_clock_qualified"]&&L(finish["mcu_counter_hz"])==1200000&&L(finish["fine_clock_domain"])==123&&L(finish["acquisition_generation"])==9,"raw fine reply fields/version/hold not retained");
   Need(L(finish["mcu_rx_counter"])==12000&&L(finish["mcu_tx_counter"])==13200,"requested hold replaced measured counter timestamps");
   Need(Convert.ToString(finish["device_uid"])==uid&&Convert.ToString(finish["identity_scope"])=="exact_MCU_UID_request_and_reply"&&finish["device_boot_domain"]==null,"fine reply lost UID or invented boot identity");
  }
 }
 static int Main(string[] args){try{Directory.CreateDirectory(args[0]);Reassembly();JournalAndWrite(args[0]);FineFrames(args[0]);Console.WriteLine("PASS "+checks+" actual-source BLE frame-range/correlation/deferred-I/O/write/callback boundary checks; modeled GATT only.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
