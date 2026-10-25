using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

// Test observer only. All normal SD commands and waveform UI use the shipped assembly.
class BleSuite {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app; static object connection,current; static bool controlBusy; static int version; static uint sequence;
 static string output,action,uid; static StreamWriter writer; static Form root,preview; static int exitCode=1;
 static uint savedMask,savedHz; static bool startSent,stopSent;
 static Type T(string name){return app.GetType(name,true);}
 static object Get(object o,string name){var f=o.GetType().GetField(name,F);return f!=null?f.GetValue(o):o.GetType().GetProperty(name,F).GetValue(o,null);}
 static object Call(object o,string name,params object[] args){var type=o as Type??o.GetType();var m=type.GetMethods(F).Single(x=>x.Name==name&&x.IsStatic==(o is Type)&&x.GetParameters().Length==args.Length&&x.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(x=>x));try{return m.Invoke(o is Type?null:o,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
 static object New(Type t,params object[] args){var c=t.GetConstructors(F).Single(x=>!x.IsStatic&&x.GetParameters().Length==args.Length&&x.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(x=>x));return c.Invoke(args);}
 static uint U(object o,string name){return Convert.ToUInt32(Get(o,name));}
 static void Need(bool b,string s){if(!b)throw new IOException(s);}
 static void Log(string s){writer.WriteLine(DateTime.UtcNow.ToString("O")+" "+s);writer.Flush();Console.WriteLine(s);}
 static async Task<object> Await(object o){var t=(Task)o;await t;var p=t.GetType().GetProperty("Result");return p==null?null:p.GetValue(t,null);}
 static uint Next(){sequence=unchecked(sequence+1);if(sequence==0)sequence=1;return sequence;}
 static object Fields(object o){return o.GetType().GetFields(F).Where(x=>!x.IsStatic).ToDictionary(x=>x.Name,x=>x.GetValue(o));}
 static void Save(string name,object o){File.WriteAllText(Path.Combine(output,name+".json"),new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Serialize(o));}
 static async Task<byte[]> Exchange(byte[] b){return (byte[])await Await(Call(connection,"ExchangeAsync",(byte)0xb9,b,CancellationToken.None));}
 static byte[] Request(uint op,uint mask=0,uint hz=0,uint end=0,uint session=0,uint flags=0){return version==2?(byte[])Call(T("CE32_console.NpV2SdControl"),"Request",op,Next(),hz,end,0UL,mask==0?3U:mask):(byte[])Call(T("CE32_console.NpSdControl"),"Request",op,Next(),mask,hz,op==2?U(current,"Start"):0U,end,session,flags,0U);}
 static async Task<object> Send(byte[] b){controlBusy=true;try {Log("request="+BitConverter.ToString(b));var r=await Exchange(b);Log("reply="+BitConverter.ToString(r));var s=Call(T(version==2?"CE32_console.NpV2SdControl":"CE32_console.NpSdControl"),"Parse",r,BitConverter.ToUInt32(b,8),BitConverter.ToUInt32(b,12));current=s;sequence=U(s,"LastId")>sequence?U(s,"LastId"):sequence;Need(U(s,"Error")==0,"Device command error "+U(s,"Error")+"; fault "+U(s,"Fault"));return s;}finally{controlBusy=false;}}
 static async Task<object> Status(){return await Send(Request(0));}
 static void StatusLog(string label){Log(label+"="+new JavaScriptSerializer().Serialize(Fields(current)));}
 static Func<TStatus> StatusGetter<TStatus>(){return ()=> (TStatus)current;}
 static Func<Task<TStatus>> StatusReader<TStatus>(){return async()=> (TStatus)await Status();}
 static Func<byte[],Task<TStatus>> Sender<TStatus>(){return async b=>(TStatus)await Send(b);}
 static object Delegate(string method){return typeof(BleSuite).GetMethod(method,F).MakeGenericMethod(T("CE32_console.NpSdStatus")).Invoke(null,null);}
 static async Task Identify(){
  if(version==2){var transport=New(T("USB512_interface.NpBleForm+ConfigTransport"),connection,null);var client=New(T("USB512_interface.Np2ConfigClient"),transport);var caps=await Await(Call(client,"CapsAsync",(byte)0,CancellationToken.None));
   string found=U(caps,"Uid0").ToString("X8")+U(caps,"Uid1").ToString("X8")+U(caps,"Uid2").ToString("X8");Save("caps",Fields(caps));Need(found==uid,"V2 MCU UID mismatch");Need((U(caps,"AvailableMask")&7)==7,"Three physical ports not available");Log("uid_verified="+found);
  }else{
   var rx=(GattCharacteristic)Get(connection,"rx");var pending=new List<byte>();var done=new TaskCompletionSource<string>();
   Windows.Foundation.TypedEventHandler<GattCharacteristic,GattValueChangedEventArgs> receive=(s,e)=>{var data=new byte[e.CharacteristicValue.Length];using(var read=DataReader.FromBuffer(e.CharacteristicValue))read.ReadBytes(data);lock(pending){pending.AddRange(data);for(int at=0;at+22<=pending.Count;at++)if(pending[at]==0xad&&pending[at+1]==0xda&&pending[at+20]==0xda&&pending[at+21]==0xda){var bytes=pending.Skip(at).Take(22).ToArray();done.TrySetResult(BitConverter.ToUInt32(bytes,2).ToString("X8")+BitConverter.ToUInt32(bytes,6).ToString("X8")+BitConverter.ToUInt32(bytes,10).ToString("X8"));}}};
   var transaction=(SemaphoreSlim)Get(connection,"transaction");await transaction.WaitAsync();rx.ValueChanged+=receive;try{await Await(Call(connection,"WriteFrame",new byte[]{0x3c,0xad,0xda,0x3e},(int)Get(connection,"Epoch"),CancellationToken.None));Need(await Task.WhenAny(done.Task,Task.Delay(5000))==done.Task,"V1 native full UID did not respond");Need(await done.Task==uid,"V1 MCU UID mismatch");Log("uid_verified="+await done.Task);}finally{rx.ValueChanged-=receive;transaction.Release();}
  }
 }
 static async Task<object> Catalog(string label){try{
  object c;if(version==1){Func<ushort,Task<byte[]>> read=async page=>(byte[])await Await(Call(connection,"ExchangeV1Storage",page,false));c=await Await(Call(T("USB512_interface.NpBleCatalog"),"ReadV1",current,read));}
  else{var reader=New(T("USB512_interface.NpV2BleCatalog"),Delegate("StatusReader"),(Func<byte[],Task<byte[]>>)Exchange,Delegate("Sender"),(Action)(()=>Need((bool)Get(connection,"Connected"),"Disconnected during catalog")));c=await Await(Call(reader,"Read"));}
  var records=((IEnumerable)Get(c,"Records")).Cast<object>().ToList();Save(label,records.Select(Fields).ToList());Log(label+"_count="+records.Count);return c;
 }catch(Exception e){Log(label+"_FAILED="+e);Save(label+"-failure",e.ToString());return null;}}
 static void OpenPreview(){preview=(Form)New(T(version==2?"USB512_interface.NpBlePreviewForm":"USB512_interface.NpV1BlePreviewForm"),connection,Delegate("StatusGetter"),(Func<bool>)(()=>controlBusy),null);preview.Show(root);}
 static Dictionary<string,object> PreviewSnapshot(string label){var value=new Dictionary<string,object>{{"label",label},{"checked",((CheckBox)Get(preview,"enabled")).Checked},{"info",((Label)Get(preview,"info")).Text},{"packets",Get(preview,"packets")},{"channel",((NumericUpDown)Get(preview,"channel")).Value},{"probe",((NumericUpDown)Get(preview,"probe")).Value},{"phase",Get(preview,"phase")},{"activeChannels",Get(preview,"activeChannels")},{"session",Get(preview,"session")},{"hz",Get(preview,"hz")},{"band",Get(preview,"activeBand")},{"request",Get(preview,"activeId")}};Save(label,value);Log("preview="+new JavaScriptSerializer().Serialize(value));using(var bmp=new Bitmap(preview.Width,preview.Height)){preview.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(output,label+".png"));}return value;}
 static async Task Stop(){if(stopSent)return;stopSent=true;if(preview!=null){preview.Close();preview.Dispose();preview=null;await Task.Delay(2500);}await Send(Request(3));Log("stop_sent_once=true");await WaitStopped();}
 static async Task WaitStopped(){var wait=Stopwatch.StartNew();while(wait.Elapsed.TotalSeconds<150){await Task.Delay(1000);await Status();StatusLog("stop_poll");if(!(bool)Get(current,"Busy")&&U(current,"State")== (version==2?5U:3U)){Need(U(current,"Fault")==0,"Stopped with fault");Log("stop_confirmed=true; drain_ms="+wait.ElapsedMilliseconds);return;}Need(U(current,"Fault")==0,"Recording fault during Stop");}throw new TimeoutException("Stop not confirmed; no replay");}
 static void Set(object o,string name,object value){o.GetType().GetField(name,F).SetValue(o,value);}
 static readonly object observationGate=new object();
 static StreamWriter sampleCsv;static FileStream notificationBytes;static long observedPackets,validSamples,invalidSamples,previewErrors,stalePackets,sourceGaps;
 static Dictionary<string,ulong> expectedTicks=new Dictionary<string,ulong>();
 static List<object> phaseResults=new List<object>();
 static void ObservePreview(int epoch,byte[] body){
  if(body.Length!=224 || BitConverter.ToUInt32(body,0)!=0x3156504e || ((BitConverter.ToUInt32(body,28)>>8)&15)!=5)return;
  var form=preview;if(form==null)return;
  lock(observationGate)lock(Get(form,"gate")){
   try{
    uint id=BitConverter.ToUInt32(body,4);
    if(epoch!=(int)Get(connection,"Epoch")||id!=U(form,"activeId")){stalePackets++;return;}
    var burst=New(T("CE32_console.NpV1PreviewBurst"),id);Need((bool)Call(burst,"Accept",body),"Compact parser rejected body");
    var values=(double[][])Call(burst,"DecodeCompact",U(form,"phase"));var channels=(ushort[])Get(burst,"Channels");var masks=(ushort[])Get(burst,"ValidMasks");var offsets=(uint[])Get(burst,"TickOffsets");
    Need(channels.SequenceEqual(new ushort[]{0,24,25,383}),"Unexpected compact channel list");
    uint band=U(burst,"Band"),probe=U(burst,"Probe"),stride=U(burst,"Stride");ulong first=(ulong)Get(burst,"FirstTick");
    for(int c=0;c<channels.Length;c++){
     string key=probe+":"+band+":"+channels[c];ulong expected,start=first+offsets[c];
     if(expectedTicks.TryGetValue(key,out expected)&&expected!=start)sourceGaps++;expectedTicks[key]=start+16UL*stride;
     for(int n=0;n<16;n++){
      bool valid=!Double.IsNaN(values[c][n]);if(valid)validSamples++;else invalidSamples++;
      sampleCsv.WriteLine(String.Join(",",band,probe,channels[c],start+(ulong)n*stride,valid?values[c][n].ToString(System.Globalization.CultureInfo.InvariantCulture):"",valid?1:0,id,U(burst,"Session"),stride,masks[c],U(burst,"Dropped")));
     }
    }
    observedPackets++;
   }catch(Exception e){previewErrors++;sampleCsv.WriteLine("# error,"+e.Message.Replace(',',';'));}
  }
 }
 static async Task ObservePhase(string name,int seconds,bool requirePreview){
  long before;lock(observationGate)before=observedPackets;uint chunks=U(current,"Chunks");var latencies=new List<long>();var watch=Stopwatch.StartNew();
  while(watch.Elapsed.TotalSeconds<seconds){await Task.Delay(1000);var poll=Stopwatch.StartNew();await Status();latencies.Add(poll.ElapsedMilliseconds);Need(U(current,"State")==1&&U(current,"Fault")==0,"Recording changed during "+name);}
  long received;lock(observationGate)received=observedPackets-before;
  Need(U(current,"Chunks")>chunks,"No committed SD progress during "+name);
  if(requirePreview){Need(received>0,"No compact packets during "+name);Need(((CheckBox)Get(preview,"enabled")).Checked,"Production preview disabled during "+name);Need((bool)Get(preview,"activeCompact"),"Production preview did not negotiate compact");PreviewSnapshot(name);}
  phaseResults.Add(new {phase=name,seconds=watch.Elapsed.TotalSeconds,packets=received,begin_chunks=chunks,end_chunks=U(current,"Chunks"),status_ms=latencies});Save("phase-results",phaseResults);
 }
 static void AuditCrc(){
  byte[] data=File.ReadAllBytes(Path.Combine(output,"ble-notifications.bin"));int seen=0,invalid=0;
  for(int at=0;at+228<=data.Length;at++)if(data[at]==0xad&&data[at+1]==0xba&&BitConverter.ToUInt32(data,at+2)==0x3156504e&&((data[at+31]&15)==5)&&data[at+226]==0xda&&data[at+227]==0xda){
   var body=new byte[224];System.Buffer.BlockCopy(data,at+2,body,0,224);seen++;
   if(BitConverter.ToUInt32(body,220)!=(uint)Call(T("CE32_console.NpSdControl"),"Crc",body,220))invalid++;
  }
  Save("preview-observer",new {observedPackets,validSamples,invalidSamples,previewErrors,stalePackets,sourceGaps,framed_compact_packets=seen,framed_crc_errors=invalid,scope="Framed preview payload CRC only; malformed headers/truncation and physical BLE errors are not counted as CRC errors."});
  Need(previewErrors==0&&invalid==0,"Preview decode/CRC errors observed; review preserved evidence");
 }
 static async Task WaitProfile(uint mask,uint hz){
  var wait=Stopwatch.StartNew();
  while(wait.Elapsed.TotalSeconds<30){
   await Status();Need(U(current,"Fault")==0,"Profile transition fault");
   if(!(bool)Get(current,"Busy")&&(U(current,"Flags")&(131072|262144))==0&&U(current,"Mask")==mask&&U(current,"Hz")==hz)return;
   await Task.Delay(500);
  }
  throw new TimeoutException("Profile did not become idle/ready within30s; no mutation replay");
 }
 static bool restore15;
 static async Task Record(){
  savedMask=U(current,"Mask");savedHz=restore15?15000U:U(current,"Hz");
  Need(!restore15||savedMask==3,"Explicit original15k restore requires P12");Save("initial-status",Fields(current));Save("restore-profile",new{mask=savedMask,hz=savedHz,from_prior_run=restore15});
  try{await RecordCore();}
  finally{if(!startSent)try{
   var wait=Stopwatch.StartNew();await Status();while(wait.Elapsed.TotalSeconds<30&&((bool)Get(current,"Busy")||(U(current,"Flags")&(131072|262144))!=0)){await Task.Delay(500);await Status();}
   Need(!(bool)Get(current,"Busy")&&(U(current,"Flags")&(131072|262144))==0&&U(current,"Fault")==0,"Pre-start profile restore requires confirmed idle");
   if(U(current,"Mask")!=savedMask||U(current,"Hz")!=savedHz){await Send(Request(1,savedMask,savedHz));await WaitProfile(savedMask,savedHz);}
   Log("pre_start_original_profile_restored=true");
  }catch(Exception e){Log("pre_start_profile_restore_failed="+e.Message);}}
 }
 static async Task RecordCore(){
  Need(version==1,"V1 only");
  Need(!(bool)Get(current,"Busy")&&U(current,"Fault")==0,"Recorder not idle");
  var before=await Catalog("catalog-before");Need(before!=null,"Initial catalog unavailable");
  var serializer=new JavaScriptSerializer();var oldRecords=((IEnumerable)Get(before,"Records")).Cast<object>().Select(x=>serializer.Serialize(Fields(x))).ToList();
  await Status();await Send(Request(1,3,20000));await WaitProfile(3,20000);
  Need((U(current,"Flags")&0x08000000)!=0,"Compact preview capability not advertised");
  uint reserve=(uint)Call(T("CE32_console.NpV1BleProtocol"),"EndLba",current,40U,50U,true);
  Need(reserve>U(current,"Start")&&((ulong)reserve-U(current,"Start"))*512<=2UL*1024*1024*1024,"Bound exceeds2GiB");
  var rx=(GattCharacteristic)Get(connection,"rx");
  Windows.Foundation.TypedEventHandler<GattCharacteristic,GattValueChangedEventArgs> raw=(s,e)=>{var data=new byte[e.CharacteristicValue.Length];using(var reader=DataReader.FromBuffer(e.CharacteristicValue))reader.ReadBytes(data);lock(observationGate)notificationBytes.Write(data,0,data.Length);};
  Action<int,byte[]> observer=ObservePreview;var received=connection.GetType().GetEvent("PreviewReceived",F);
  sampleCsv=new StreamWriter(Path.Combine(output,"preview-samples.csv"));sampleCsv.WriteLine("band,probe,channel,source_tick,raw10,valid,request,session,stride,mask,device_dropped");notificationBytes=File.Create(Path.Combine(output,"ble-notifications.bin"));
  received.GetAddMethod(true).Invoke(connection,new object[]{observer});rx.ValueChanged+=raw;
  try{
   byte[] start=Request(2,3,20000,reserve,unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()),(bool)Get(current,"Qualified")?0U:1U);
   startSent=true;await Send(start);var wait=Stopwatch.StartNew();bool recording=false;
   while(wait.Elapsed.TotalSeconds<65){await Task.Delay(500);await Status();StatusLog("start_poll");Need(U(current,"Fault")==0,"Start fault");if(U(current,"State")==1&&(U(current,"Flags")&(8|131072|262144))==0){recording=true;break;}}
   Need(recording,"Start not confirmed");Save("recording-status",Fields(current));
   await ObservePhase("preview-off-before",4,false);OpenPreview();
   ((NumericUpDown)Get(preview,"probe")).Value=1;((NumericUpDown)Get(preview,"traceCount")).Value=4;((NumericUpDown)Get(preview,"channel")).Value=0;
   var selected=(NumericUpDown[])Get(preview,"extraChannels");selected[0].Value=24;selected[1].Value=25;selected[2].Value=383;
   await ObservePhase("compact-ap",10,true);((ComboBox)Get(preview,"band")).SelectedIndex=1;await ObservePhase("compact-lfp",10,true);
   ((CheckBox)Get(preview,"enabled")).Checked=false;await ObservePhase("preview-off-after",4,false);await Stop();Save("stopped-status",Fields(current));
   var after=await Catalog("catalog-after");Need(after!=null,"Final catalog unavailable");var now=((IEnumerable)Get(after,"Records")).Cast<object>().Select(x=>serializer.Serialize(Fields(x))).ToList();
   Need(now.Count==oldRecords.Count+1&&oldRecords.SequenceEqual(now.Take(oldRecords.Count)),"Previous catalog changed or new recording missing");
   if(savedMask!=3||savedHz!=20000){await Status();await Send(Request(1,savedMask,savedHz));await WaitProfile(savedMask,savedHz);}
   Save("final-status",Fields(current));
  }finally{
   rx.ValueChanged-=raw;received.GetRemoveMethod(true).Invoke(connection,new object[]{observer});
   lock(observationGate){sampleCsv.Dispose();notificationBytes.Dispose();}
  }
  AuditCrc();Log("compact_preview_sd_test_completed=true; full_SD_payload_readback_required=true");
 }

 static string addressArgument;
 static async Task Run(string[] args){Need((args.Length==5||args.Length==6&&args[5]=="restore15")&&args[2]=="1"&&args[3]=="record"&&(Convert.ToUInt64(args[1],16)&0xffffffUL)==0xCA53BAUL,"Exact V1 CA53BA record action required");restore15=args.Length==6;output=Path.GetFullPath(args[4]);Directory.CreateDirectory(output);Need(!File.Exists(Path.Combine(output,"events.log")),"Use a fresh output directory");writer=new StreamWriter(Path.Combine(output,"events.log"));action=args[3];addressArgument=args[1];version=int.Parse(args[2]);uid=version==1?"001D00065942500B20313354":"001A0004434250062039314E";app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string p=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};connection=New(T("USB512_interface.NpBleConnection"));sequence=unchecked((uint)Environment.TickCount);
  try{Log("version="+FileVersionInfo.GetVersionInfo(args[0]).FileVersion+"; uid="+uid+"; action="+action);await Await(Call(connection,"ConnectAsync",Convert.ToUInt64(args[1],16),(BluetoothAddressType?)BluetoothAddressType.Random));Log("connect="+Get(connection,"LastConnectDiagnostic"));
   Func<byte,byte[],CancellationToken,Task<byte[]>> exchange=(command,b,ct)=>(Task<byte[]>)Call(connection,"ExchangeAsync",command,b,ct);var tuple=await Await(Call(T("USB512_interface.NpBleConnection"),"ReadDevice",exchange,(Func<uint>)Next));Need((int)Get(tuple,"Item1")==version,"Wrong protocol generation");current=Get(tuple,"Item2");sequence=U(current,"LastId");StatusLog("initial");await Identify();
   if(action=="mount"){Need(version==2&&U(current,"State")==1&&!(bool)Get(current,"Busy"),"Mount requires idle V2");await Send((byte[])Call(T("CE32_console.NpV2SdControl"),"StorageRequest",Next(),false));for(int i=0;i<30;i++){await Task.Delay(250);await Status();if(!(bool)Get(current,"Busy")&&U(current,"Fault")==0)break;}Need(U(current,"Fault")==0&&!(bool)Get(current,"Busy"),"Mount not complete");await Send(Request(1,3,20000));await Status();StatusLog("mounted-restored");}else if(action=="record")await Record();else if(action=="catalog")await Catalog("catalog");else if(action=="return"){if(version==2&&U(current,"Mask")!=3){Need(!(bool)Get(current,"Busy")&&U(current,"Fault")==0,"Profile restore requires idle");await Send(Request(1,3,20000));await Status();Need(U(current,"Mask")==3&&U(current,"Hz")==20000,"Original profile not restored");Log("original_profile_restored=true");}Need(!(bool)Get(current,"Busy")&&U(current,"Fault")==0,"Return requires idle fault-zero");var b=version==2?(byte[])Call(T("CE32_console.NpV2SdControl"),"ModeRequest",Next(),2U):(byte[])Call(T("CE32_console.NpV1BleProtocol"),"Mode",current,Next(),4U);await Send(b);Log("USB_return_ack=true");}
   Log("observer_completed=true");exitCode=0;
  }catch(Exception e){Log("FAIL="+e);Save("failure",e.ToString());if(startSent&&!stopSent)try{await Status();if((bool)Get(current,"Busy")&&U(current,"Fault")==0)await Stop();}catch(Exception cleanup){Log("cleanup="+cleanup);}}
  finally{if(preview!=null){preview.Close();preview.Dispose();}Call(connection,"Disconnect");Call(connection,"Dispose");writer.Dispose();root.Close();}
 }
 [STAThread]static int Main(string[] args){Application.EnableVisualStyles();root=new Form{Text="WILD-X BLE hardware validation",Width=400,Height=100,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)};root.Shown+=async(s,e)=>{try{await Run(args);}catch(Exception error){Console.Error.WriteLine(error);if(writer!=null){writer.WriteLine(error);writer.Flush();}root.Close();}};Application.Run(root);return exitCode;}
}
