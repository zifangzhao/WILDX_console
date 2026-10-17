using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

static class NpV1BleFailureReportTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app;static int checks;static readonly List<byte> reads=new List<byte>();static readonly Dictionary<byte,object> replies=new Dictionary<byte,object>();static int failPort=-1;
 static Type T(string name){return app.GetType(name,true);}
 static object Get(object target,string field){return target.GetType().GetField(field,All).GetValue(target);}
 static void Set(object target,string field,object value){target.GetType().GetField(field,All).SetValue(target,value);}
 static object Call(object target,string method,params object[] args){try{return target.GetType().GetMethod(method,All).Invoke(target,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static Task<TReply> Read<TReply>(byte port,CancellationToken cancel){cancel.ThrowIfCancellationRequested();reads.Add(port);if(port==failPort)throw new IOException("simulated unavailable receipt");return Task.FromResult((TReply)replies[port]);}
 static Delegate Reader(){return (Delegate)typeof(NpV1BleFailureReportTests).GetMethod("MakeReader",All).MakeGenericMethod(T("WildNpConfiguration.NpConfigReply")).Invoke(null,null);}
 static Func<byte,CancellationToken,Task<TReply>> MakeReader<TReply>(){return Read<TReply>;}
 static object Status(uint mask=3,uint flags=17){var value=Activator.CreateInstance(T("CE32_console.NpSdStatus"),true);Set(value,"Mask",mask);Set(value,"Hz",15000U);Set(value,"State",4U);Set(value,"Fault",2U);Set(value,"Flags",flags);return value;}
 static IDictionary Requests(){return (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(byte),T("WildNpConfiguration.NpConfigRequest")));}
 static object Request(byte port){var b=new byte[1576];b[0]=78;b[1]=80;b[2]=66;b[3]=49;b[4]=2;b[5]=port;return Activator.CreateInstance(T("WildNpConfiguration.NpConfigRequest"),new object[]{"{}",b});}
 static object Reply(byte state,object request=null){
  var reply=Activator.CreateInstance(T("WildNpConfiguration.NpConfigReply"));Set(reply,"PortState",state);Set(reply,"ConfigurationId",new byte[32]);
  if(request!=null){var data=(byte[])Get(request,"Selection");uint crc=(uint)T("WildNpConfiguration.NpConfigClient").GetMethod("Crc").Invoke(null,new object[]{data,data.Length});Set(reply,"PayloadCrc",crc);Set(reply,"Verification",(byte)3);}
  return reply;
 }
 static string Report(object s,IDictionary requests,CancellationToken cancel=default(CancellationToken)){
  reads.Clear();var task=(Task<string>)T("USB512_interface.NpBleForm").GetMethod("ReadV1FailureDetails",All).Invoke(null,new object[]{s,requests,Reader(),cancel});return task.GetAwaiter().GetResult();
 }
 static void PeerScope(){
  var type=T("USB512_interface.NpBleForm");var host=FormatterServices.GetUninitializedObject(type);
  Set(host,"v1HostConfigurations",Activator.CreateInstance(type.GetField("v1HostConfigurations",All).FieldType));
  Set(host,"connectedAddress",0xF5FAAFB34D50UL);var a=(IDictionary)Call(host,"V1Configurations");var request=Request(0);a.Add((byte)0,request);
  Set(host,"connectedAddress",0xF6D598CA53BAUL);var b=(IDictionary)Call(host,"V1Configurations");Need(b.Count==0&&!Object.ReferenceEquals(a,b),"Previous V1 logger's probe map leaked into another peer");b.Add((byte)1,Request(1));
  Set(host,"connectedAddress",0xF5FAAFB34D50UL);Need(Object.ReferenceEquals(a,Call(host,"V1Configurations"))&&a.Count==1&&!a.Contains((byte)1),"Reconnect lost own maps or inherited other peer's maps");
  Set(host,"connectedAddress",0UL);bool rejected=false;try{Call(host,"V1Configurations");}catch(IOException){rejected=true;}Need(rejected,"Disconnected configuration access selected an arbitrary peer");
 }
 static void Reports(){
  var requests=Requests();var request=Request(0);requests.Add((byte)0,request);replies[0]=Reply(4);replies[1]=Reply(3);
  string text=Report(Status(),requests);Need(reads.Count==2&&reads[0]==0&&reads[1]==1,"Diagnostics did not read only selected ports");
  Need(text.Contains("Probe 1: configuration Failed (4)")&&text.Contains("Requested configuration NOT confirmed applied"),"Failed map not reported");
  Need(text.Contains("STATUS error 0 confirms the command only")&&!text.Contains("Matching applied configuration receipt"),"STATUS command success promoted to probe success");
  Need(text.Contains("BLE host request: NP1 / profile Np1; selection model 2")&&text.Contains("Host configuration ID:")&&text.Contains("No host map retained for comparison"),"Requested model/identity or absent-map provenance omitted");
  Need(text.Contains("separate from USB-mode requests")&&text.Contains("Removing a host request does not clear firmware-held maps"),"BLE/USB request provenance or firmware-held map distinction omitted");
  Need(text.Contains("do not expose the I2C / capture-lock failure stage"),"Report falsely identifies precise source failure");
  replies[0]=Reply(3,request);text=Report(Status(1),requests);Need(text.Contains("Matching applied configuration receipt; analog signal not verified"),"Matching receipt not distinguished from analog verification");
  foreach(uint flags in new uint[]{21,25,17|262144}){text=Report(Status(3,flags),requests);Need(reads.Count==0&&text.Contains("not queried"),"Active/pending/preparing recorder was queried");}
  var healthy=Status();Set(healthy,"Fault",0U);Report(healthy,requests);Need(reads.Count==0,"Healthy recorder queried by fault report");
  failPort=0;text=Report(Status(),requests);Need(text.Contains("simulated unavailable receipt")&&reads.Count==2,"Unavailable port hid the other port's receipt");failPort=-1;
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();text=Report(Status(),requests,cancel.Token);Need(reads.Count==0&&text.Contains("unavailable"),"Canceled diagnostics continued issuing requests");}
  var stopped=Status();Set(stopped,"Flags",17U|8192U);string reason=null;
  try{T("CE32_console.NpV1BleProtocol").GetMethod("Mode",All).Invoke(null,new object[]{stopped,10U,2U});}catch(TargetInvocationException ex){reason=ex.InnerException.Message;}
  Need(reason!=null&&reason.Contains("already stopped")&&reason.Contains("fault 2")&&reason.Contains("no mode command was sent"),"Latched fault misreported as ongoing recording or mode bypassed");
 }
 static void TimeoutContract(){
  foreach(uint op in new uint[]{0,1,2,3}){
   var request=new byte[64];uint[] words={0x31444c4e,1,op,23};for(int i=0;i<words.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(words[i]),0,request,i*4,4);
   uint crc=(uint)T("CE32_console.NpSdControl").GetMethod("Crc",All).Invoke(null,new object[]{request,60});Buffer.BlockCopy(BitConverter.GetBytes(crc),0,request,60,4);
   var error=(TimeoutException)T("USB512_interface.NpBleConnection").GetMethod("MissingReceipt",All).Invoke(null,new object[]{(byte)0xb9,request});
   Need((error.GetType().Name=="NpV1ReceiptTimeout")== (op==0),"Mutation acquired read-only receipt continuation");
   if(op==0){Need((uint)Get(error,"Operation")==0U&&(uint)Get(error,"Transaction")==23U,"Receipt timeout lost its exact STATUS identity");request[60]^=1;error=(TimeoutException)T("USB512_interface.NpBleConnection").GetMethod("MissingReceipt",All).Invoke(null,new object[]{(byte)0xb9,request});Need(error.GetType()==typeof(TimeoutException),"Invalid STATUS frame acquired continuation contract");}
  }
 }
 [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));PeerScope();Reports();TimeoutContract();Console.WriteLine("PASS "+checks+" actual-app V1 peer isolation/failure-report/mode/receipt-contract checks; fake read callbacks only, no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
