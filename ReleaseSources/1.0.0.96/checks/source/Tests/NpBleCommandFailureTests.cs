using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CE32_console;

namespace CE32_console {
 static class NpV1StoreBinding {
  internal static void Validate(byte[] header,byte[] record,int offset=0){throw new InvalidOperationException("Unexpected SD export in BLE command test");}
 }
}

namespace USB512_interface {
 class NpBleConnection {
  internal bool Connected=true;
  internal int Epoch=1;
  internal Func<byte[],Task<byte[]>> Exchange;
  internal Task<byte[]> ExchangeAsync(byte[] request){return Exchange(request);}
ACTUAL_PARSE_STATUS
 }
 class NpBleForm {
  class TextControl {internal string Text;}
  readonly NpBleConnection connection=new NpBleConnection();
  readonly TextControl state=new TextControl();
  NpSdStatus current;
  int recorderVersion=2;
  ulong connectedAddress=123;
  uint sequence;
  bool busy,hostBusy,stopRequested;
  readonly Task refreshTask=Task.CompletedTask;
  readonly List<string> messages=new List<string>();
  int metadataObservations,stopCommands,exchanges;
  void UpdateControls(){}
  void NotifyState(){}
  void OfferFirmwareReview(){}
  void ShowStatus(NpSdStatus status){current=status;state.Text="State "+status.State;}
  void ObserveBleRecordingMetadata(NpSdStatus status){metadataObservations++;}
  void Append(string message){messages.Add(message);}
  Task StopRecording(){stopCommands++;return Task.CompletedTask;}
ACTUAL_RUN
ACTUAL_SEND
  static int checks;
  static void Need(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
  static void Put(byte[] bytes,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,at,4);}
  static byte[] Reply(int version,uint op,uint id,uint error,uint flags){
   var b=new byte[64];uint[] words={version==2?NpV2SdControl.ReplyMagic:0x31524c4eU,(uint)version,op,id,error,1,0,3,20000,7,8192,1000000,2000000,flags,id};
   for(int i=0;i<words.Length;i++)Put(b,i*4,words[i]);Put(b,60,NpSdControl.Crc(b,60));return b;
  }
  static void CheckAssemblyParser(string path){
   var assembly=Assembly.LoadFrom(path);
   var method=assembly.GetType("USB512_interface.NpBleConnection",true).GetMethod("ParseStatus",BindingFlags.NonPublic|BindingFlags.Static);
   foreach(int version in new[]{1,2}){
    var bytes=Reply(version,2,41,8,128U|17U|65536U);
    try{method.Invoke(null,new object[]{bytes,version,2U,41U});throw new Exception("Actual parser accepted a rejection");}
    catch(TargetInvocationException wrapped){
     var ex=wrapped.InnerException;Need(ex is IOException && ex.GetType().Name=="NpSdCommandException","Actual parser discarded the validated rejection status");
     var status=ex.GetType().GetField("Status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ex);
     Need((uint)status.GetType().GetField("Flags",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(status)==(128U|17U|65536U),"Actual parser lost FULL flags");
     Need(version==1 || ex.Message.Contains("No unreserved SD space"),"Actual parser omitted the V2 storage reason");
    }
    bytes[60]^=1;
    try{method.Invoke(null,new object[]{bytes,version,2U,41U});throw new Exception("Actual parser accepted bad CRC");}
    catch(TargetInvocationException wrapped){Need(wrapped.InnerException is IOException && wrapped.InnerException.GetType().Name!="NpSdCommandException","Bad CRC was treated as a trusted device rejection");}
   }
  }
  static void CheckRejectedMetadata(string path,string output){
   var assembly=Assembly.LoadFrom(path);var formType=assembly.GetType("USB512_interface.NpBleForm",true);
   var settingsType=assembly.GetType("USB512_interface.NpRecordingSettings",true);
   const BindingFlags fields=BindingFlags.NonPublic|BindingFlags.Instance;
   foreach(int version in new[]{1,2}){
    string folder=Path.Combine(output,"rejected-metadata-v"+version);Directory.CreateDirectory(folder);
    object settings=Activator.CreateInstance(settingsType,fields,null,new object[]{folder,null,"ble_sd",version==2,null,null,null},null);
    object form=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(formType);
    GC.SuppressFinalize(form);
    formType.GetField("recorderVersion",fields).SetValue(form,version);
    formType.GetField("bleRecordingMetadata",fields).SetValue(form,settings);
    formType.GetField("bleMetadataState",fields).SetValue(form,UInt32.MaxValue);
    var statusType=assembly.GetType("CE32_console.NpSdStatus",true);
    object status=Activator.CreateInstance(statusType,true);
    foreach(var entry in new Dictionary<string,uint>{{"Operation",2},{"RequestId",41},{"Error",8},{"State",version==2?3U:1U},{"Mask",3},{"Hz",20000},{"Flags",65536U|17U|128U}})
     statusType.GetField(entry.Key,fields).SetValue(status,entry.Value);
    var observe=formType.GetMethod("ObserveBleRecordingMetadata",fields);observe.Invoke(form,new[]{status});
    var document=(Dictionary<string,object>)settingsType.GetField("document",fields).GetValue(settings);
    Need((string)document["state"]=="incomplete" && (bool)document["recording_complete"]==false,"Rejected START metadata claimed a recording completed");
    Need(document.ContainsKey("start_rejection") && !document.ContainsKey("recording_first_observed_utc_host"),"Rejected START adopted a pre-existing active session or lost its rejection receipt");
    Need((bool)formType.GetField("bleMetadataFinished",fields).GetValue(form),"Rejected host request remained eligible for later session association");
    statusType.GetField("Error",fields).SetValue(status,0U);observe.Invoke(form,new[]{status});
    Need(!document.ContainsKey("recording_first_observed_utc_host"),"Later status was attached to a rejected START");
   }
  }
  static async Task Tests(){
   foreach(int version in new[]{1,2})foreach(uint flags in new[]{17U|128U|65536U,17U|128U|131072U,17U|128U|131072U|262144U|32768U|NpV2SdControl.Stored7500}){
    var f=new NpBleForm{recorderVersion=version};
    f.connection.Exchange=request=>{f.exchanges++;return Task.FromResult(Reply(version,2,41,8,flags));};
    var request=version==2?NpV2SdControl.Request(2,41,20000,0,0,3):NpSdControl.Request(2,41,3,20000);
    await f.Run(async()=>{await f.Send(request);});
    Need(f.current!=null && f.current.Flags==flags && f.current.Error==8 && f.current.Fault==0,"Rejected command erased device state or invented a probe fault");
    Need(!f.busy && f.exchanges==1 && f.stopCommands==0 && f.metadataObservations==1,"Rejected command retained UI ownership, replayed a mutation, or lost metadata");
    Need(f.messages.Count==1 && f.state.Text.Contains("operation 2: error 8"),"Rejection not reported");
    if(version==2){
     string reason=NpV2SdControl.StartStorageError(f.current);
     Need((flags&65536U)==0 || reason.Contains("No unreserved SD space"),"FULL was not explained");
     Need((flags&131072U)==0 || reason.Contains("catalog needs recovery"),"Recovery was not explained");
     f.current.Error=0;f.current.Operation=0;
     Need(NpV2SdControl.Describe(f.current).Contains((flags&65536U)!=0?"No unreserved SD space":"catalog needs recovery"),"Read-only polling hid the storage condition");
    }
   }
   for(int invalid=0;invalid<8;invalid++){
    int mode=invalid;var f=new NpBleForm{current=new NpSdStatus{State=3}};
    f.connection.Exchange=request=>{
     f.exchanges++;var b=Reply(2,2,41,8,17U|128U|65536U);
     if(mode==0)b[60]^=1;
     if(mode==1){Put(b,12,42);Put(b,60,NpSdControl.Crc(b,60));}
     if(mode==2)f.connection.Epoch++;
     if(mode==3)f.connectedAddress++;
     if(mode==4)f.connection.Connected=false;
     if(mode==5)f.recorderVersion=1;
     if(mode==6)throw new TimeoutException("No matching receipt");
     if(mode==7)throw new IOException("BLE disconnected");
     return Task.FromResult(b);
    };
    await f.Run(async()=>{await f.Send(NpV2SdControl.Request(2,41,20000,0,0,3));});
    Need(f.current==null && f.metadataObservations==0,"Uncorrelated/transport failure adopted stale device status");
    Need(!f.busy && f.exchanges==1 && f.stopCommands==0,"Uncertain command was replayed or kept the UI busy");
   }
   var unknown=new NpSdStatus{State=1,Mask=3,Hz=20000,Error=8,Operation=2,Flags=17U|128U};
   string text=NpV2SdControl.StartStorageError(unknown);
   Need(!text.Contains("No unreserved") && !text.Contains("catalog needs recovery"),"Error 8 alone fabricated FULL/recovery flags");
   Console.WriteLine("PASS "+checks+" BLE rejection/status/peer/no-replay checks; no hardware.");
  }
  [STAThread]static int Main(string[] args){try{CheckAssemblyParser(args[0]);CheckRejectedMetadata(args[0],args[1]);Tests().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
