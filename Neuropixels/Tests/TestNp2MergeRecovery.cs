using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// File-only test of the delivered GUI. Never constructs/shows its main window,
// enumerates devices, opens WinUSB, starts BLE or writes firmware/SD hardware.
static class Np2MergeRecoveryTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly gui; static Type formType, mergeType; static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static object CallTyped(Type t,object target,string name,Type[] types,params object[] args){
  var method=t.GetMethod(name,All,null,types,null);if(method==null)throw new MissingMethodException(t.FullName,name+"("+String.Join(",",Array.ConvertAll(types,x=>x.Name))+")");
  try{return method.Invoke(target,args);}catch(TargetInvocationException ex){ExceptionDispatchInfo.Capture(ex.InnerException??ex).Throw();throw;}
 }
 static object Call(object target,string name,params object[] args){return CallTyped(target.GetType(),target,name,Array.ConvertAll(args,x=>x.GetType()),args);}
 static object Get(object o,string field){return o.GetType().GetField(field,All).GetValue(o);}
 static void Set(object o,string field,object value){o.GetType().GetField(field,All).SetValue(o,value);}
 static object New(string name){return Activator.CreateInstance(gui.GetType("USB512_interface."+name,true),true);}
 static short Value(int p,int row,int ch){return (short)((p*991+row*7+ch*13)%4096-2047);}
 static byte[] Source(string folder,int p,int rows,bool partial=false){
  string child=Path.Combine(folder,"probe"+p);Directory.CreateDirectory(child);var bytes=new byte[rows*768+(partial?1:0)];
  for(int r=0;r<rows;r++)for(int ch=0;ch<384;ch++){short v=Value(p,r,ch);bytes[(r*384+ch)*2]=(byte)v;bytes[(r*384+ch)*2+1]=(byte)(v>>8);}
  File.WriteAllBytes(Path.Combine(child,"neuropixels_ap.dat"),bytes);return bytes;
 }
 static long Merge(string folder,uint mask,uint hz,int[] phases,bool complete,Func<bool> cancel){return (long)CallTyped(mergeType,null,"Run",new[]{typeof(string),typeof(uint),typeof(uint),typeof(int[]),typeof(bool),typeof(Func<bool>)},folder,mask,hz,phases,complete,cancel);}
 static void Expect<T>(Action action,string why)where T:Exception{try{action();}catch(T){Check(true,why);return;}throw new Exception("Expected "+typeof(T).Name+": "+why);}
 static void Merges(string root){
  foreach(uint mask in new uint[]{3,6,7})foreach(uint hz in new uint[]{15000,20000,30000}){
   string folder=Path.Combine(root,"merge-"+mask+"-"+hz);Directory.CreateDirectory(folder);var originals=new byte[4][];int n=0;
   for(int p=1;p<=3;p++)if((mask&(1U<<(p-1)))!=0){originals[p]=Source(folder,p,1301+p);n++;}
   int common=mask==6?1303:1302;Check(Merge(folder,mask,hz,new[]{0,137,1599},true,()=>false)==common,"Common row extent");
   byte[] dat=File.ReadAllBytes(Path.Combine(folder,"neuropixels_ap.dat"));Check(dat.Length==common*n*768,"Merged shape");
   for(int r=0;r<common;r++){int block=0;for(int p=1;p<=3;p++)if(originals[p]!=null){for(int ch=0;ch<384;ch++)Check(BitConverter.ToInt16(dat,(r*n*384+block*384+ch)*2)==Value(p,r,ch),"Signed row/channel/probe ordering");block++;}}
   for(int p=1;p<=3;p++)if(originals[p]!=null)Check(Convert.ToBase64String(originals[p])==Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder,"probe"+p,"neuropixels_ap.dat"))),"Source or tail modified");
   string report=File.ReadAllText(Path.Combine(folder,"np2_merge_status.txt"));Check(report.Contains("no padding")&&report.Contains("NOT proof of simultaneous")&&report.Contains("sample_hz="+hz),"Gap/timing/rate provenance");
   if((mask&2)!=0)Check(report.Contains("probe2_first_row_source_tick=1463"),"Phase origin not preserved");
   Expect<IOException>(()=>Merge(folder,mask,hz,new[]{0,137,1599},true,()=>false),"No overwrite");
   Check(dat.Length==File.ReadAllBytes(Path.Combine(folder,"neuropixels_ap.dat")).Length,"Existing merge changed");
  }
  string bad=Path.Combine(root,"partial-row");Source(bad,1,3);Source(bad,2,3,true);
  Expect<InvalidDataException>(()=>Merge(bad,3,20000,new[]{0,0,0},false,()=>false),"Reject partial row");Check(!File.Exists(Path.Combine(bad,"neuropixels_ap.dat")),"Published malformed output");
  string cancel=Path.Combine(root,"cancel");Source(cancel,1,3000);Source(cancel,2,3000);int polls=0;
  Expect<OperationCanceledException>(()=>Merge(cancel,3,20000,new[]{0,0,0},false,()=>++polls>1),"Cancel between batches");
  Check(!File.Exists(Path.Combine(cancel,"neuropixels_ap.dat"))&&!File.Exists(Path.Combine(cancel,"neuropixels_ap.dat.partial")),"Cancelled merge published or leaked partial");
  Check(new FileInfo(Path.Combine(cancel,"probe1","neuropixels_ap.dat")).Length==3000*768,"Cancelled source changed");
  string empty=Path.Combine(root,"empty");Source(empty,1,0);Source(empty,2,3);
  Expect<InvalidDataException>(()=>Merge(empty,3,20000,new[]{0,0,0},false,()=>false),"Empty input");
  Expect<InvalidDataException>(()=>Merge(empty,3,20000,new[]{-1,0,0},false,()=>false),"Missing phase");
  Expect<ArgumentException>(()=>Merge(empty,1,20000,new[]{0,0,0},false,()=>false),"Single probe is already one DAT");
 }
 static bool Retry(Exception e){return (bool)CallTyped(formType,null,"NpV2Retryable",new[]{typeof(Exception)},e);}
 static bool Retry(object r){return (bool)CallTyped(formType,null,"NpV2Retryable",new[]{r.GetType()},r);}
 static object Overrun(){
  object r=New("NpV2RecordingResult"),capture=New("NpV2CaptureStatus"),source=New("NpV2SourceStatus");
  var cw=new uint[32];cw[0]=827667022U;cw[1]=1;cw[2]=255;cw[3]=4;cw[12]=6;
  var sw=new uint[32];sw[0]=827535950U;sw[1]=1;sw[2]=11;
  Set(capture,"Words",cw);Set(source,"Words",sw);Set(r,"FinalCapture",capture);Set(r,"FinalSource",source);
  Set(r,"Error","Stage: Capture / PSSI ring overwrite/USB drain backlog.");Set(r,"ReceivedBlocks",4U);Set(r,"VerifiedBlocks",4L);return r;
 }
 static void Overruns(){
  object r=Overrun();Check(Retry(r),"Stopped clean-prefix overrun not admitted");
  var cw=(uint[])Get(Get(r,"FinalCapture"),"Words");var sw=(uint[])Get(Get(r,"FinalSource"),"Words");
  foreach(uint mask in new uint[]{15,0x3C0,0xF000,0xF3CF}){sw[21]=mask;sw[4]=16;Check(Retry(r),"Shank warning blocks overrun recovery");}
  sw[4]=0;Check(!Retry(r),"Unmarked shank failure admitted");sw[21]=0;
  foreach(uint mask in new uint[]{0x10,0x20,0x400,0x800,0x10000,0x20000,0x40000,0x80000000}){sw[21]=mask;sw[4]=16;Check(!Retry(r),"Base/unknown chain failure admitted");}sw[21]=sw[4]=0;
  foreach(int index in new[]{0,1,2,3,12,23}){uint saved=cw[index];cw[index]=index==23?1U:0U;Check(!Retry(r),"Invalid/active/unrelated capture admitted");cw[index]=saved;}
  foreach(uint fault in new uint[]{1,2,3,4,5,7}){cw[12]=fault;Check(!Retry(r),"Non-overrun producer fault admitted");}cw[12]=6;
  foreach(int index in new[]{0,1,2,3,7}){uint saved=sw[index];sw[index]=index==3 || index==7?4U:0U;Check(!Retry(r),"Invalid/active/I2C/source failure admitted");sw[index]=saved;}
  foreach(string field in new[]{"RawWriterError","DecodeError"}){Set(r,field,"failure");Check(!Retry(r),"Writer/decode fault admitted");Set(r,field,null);}
  foreach(string field in new[]{"RawDroppedBlocks","DecodedDroppedLaneBlocks"}){Set(r,field,1L);Check(!Retry(r),"Dropped prefix admitted");Set(r,field,0L);}
  foreach(string field in new[]{"Complete","CancelledBeforeCapture"}){Set(r,field,true);Check(!Retry(r),"Complete/cancelled recording restarted");Set(r,field,false);}
  Set(r,"VerifiedBlocks",3L);Check(!Retry(r),"Unverified prefix admitted");Set(r,"VerifiedBlocks",0L);Set(r,"ReceivedBlocks",0U);Check(!Retry(r),"Empty overrun retried");
  r=Overrun();Set(r,"FinalCapture",null);Check(!Retry(r),"Unknown capture receipt admitted");r=Overrun();Set(r,"FinalSource",null);Check(!Retry(r),"Unknown source receipt admitted");
 }
 static void StoredOverrun(string path){
  var data=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
  Check((string)data["uid"]=="001A0004434250062039314E" && !(bool)data["complete"],"Wrong preserved receipt");
  object r=Overrun();Set(r,"ReceivedBlocks",Convert.ToUInt32(data["received"]));Set(r,"VerifiedBlocks",Convert.ToInt64(data["verified"]));
  Set(r,"RawDroppedBlocks",Convert.ToInt64(data["raw_drops"]));Set(r,"DecodedDroppedLaneBlocks",Convert.ToInt64(data["decoded_drops"]));Set(r,"Error",(string)data["error"]);
  Set(Get(r,"FinalCapture"),"Words",Array.ConvertAll((object[])data["final_capture"],x=>Convert.ToUInt32(x)));
  Set(Get(r,"FinalSource"),"Words",Array.ConvertAll((object[])data["final_source"],x=>Convert.ToUInt32(x)));
  Check(Retry(r),"Actual preserved triple20 overrun would still stop session");
 }
 static void Policies(){
  foreach(int code in new[]{2,6,31,121,1167})foreach(string prefix in new[]{"CreateFile for V2 WinUSB failed","WinUsb_Initialize for V2 failed","V2 queue bulk read failed","V2 finish bulk read failed","V2 wait for bulk read failed"})Check(Retry(new Win32Exception(code,prefix)),"Transport reopen policy");
  foreach(Exception e in new Exception[]{new Win32Exception(32,"CreateFile for V2 WinUSB failed"),new Win32Exception(31,"Disk write failed"),new Win32Exception(31,"V2 control OUT 0x7A failed"),new TimeoutException("No fresh matching NP2 configuration receipt; configuration outcome is unknown and was not replayed."),new TimeoutException("NP2 source configuration did not reach the USB read-arm window within 200 seconds."),new IOException("V2 firmware failed during setup: capture=2, producer=0, source=8.")})Check(!Retry(e),"Unsafe restart admitted");
  Check(Retry(new IOException("Selected NP2 USB device is no longer connected; waiting for the same UID.")),"Missing pinned target recovery");
  foreach(int code in new[]{31,121,1167}){
   object r=New("NpV2RecordingResult");Set(r,"Error","IOException: V2 bulk completion failed (Win32 "+code+"; received=4) -> Win32Exception: V2 finish bulk read failed (Win32 error "+code+")");Set(r,"VerifiedBlocks",4L);Set(r,"ReceivedBlocks",4U);Check(Retry(r),"Clean transport-failed prefix rejected");
   foreach(string f in new[]{"RawWriterError","DecodeError"}){Set(r,f,"failure");Check(!Retry(r),"Writer/decode fault retried");Set(r,f,null);}
   foreach(string f in new[]{"RawDroppedBlocks","DecodedDroppedLaneBlocks"}){Set(r,f,1L);Check(!Retry(r),"Drops retried");Set(r,f,0L);}
   Set(r,"VerifiedBlocks",3L);Check(!Retry(r),"Verifier failure retried");Set(r,"VerifiedBlocks",4L);
   object status=New("NpV2CaptureStatus");var words=new uint[32];words[2]=255;Set(status,"Words",words);Set(r,"FinalCapture",status);Check(!Retry(r),"Firmware fault retried");
  }
  var describe=gui.GetType("USB512_interface.NpV2Recording",true).GetMethod("DescribeError",All);
  string text=(string)describe.Invoke(null,new object[]{new Win32Exception(1167,"V2 queue bulk read failed")});
  Check((bool)CallTyped(formType,null,"NpV2TransportRetryable",new[]{typeof(string)},text),"Actual exception formatter loses native code");
 }
 static object RecoveryForm(string root){
  // Allocate without the main constructor/Load; inject only policy state and
  // detached controls. Timer ticks are not pumped, so no START/device access.
  object form=FormatterServices.GetUninitializedObject(formType);
  foreach(string name in new[]{"btnStart","btnStop","btnRefresh","btnNpLogger","btnSdInfo","btnDfu","btnStreams","btnSystemInfoRefresh"})Set(form,name,new Button());
  Set(form,"lblRecInfo",new Label());Set(form,"comboDrives",new ComboBox());Set(form,"npAutoRecover",new CheckBox{Checked=true});Set(form,"npRetryTimer",new System.Windows.Forms.Timer());
  Set(form,"npSessionLabel",new TextBox {Text="RatID_sleep"});
  foreach(string name in new[]{"npPortConfigurations","npV2HostConfigurations","npV2ApplyConfigurations","npV2PersistedConfigurations"}){var f=formType.GetField(name,All);f.SetValue(form,Activator.CreateInstance(f.FieldType));}
  Set(form,"selectedDeviceKind",Enum.Parse(formType.GetNestedType("DeviceKind",All),"NpV2"));Set(form,"saveFolder",root);
  Set(form,"devicePath","usb#vid_1209&pid_b387#npv2-001a0004434250062039314e#{fixture}");Set(form,"npV2RequestedProbeMask",7U);Set(form,"npV2RequestedSampleHz",15000U);return form;
 }
 static void Recovery(string root){
  Directory.CreateDirectory(root);object f=RecoveryForm(root);
  string first=(string)Call(f,"NpSegmentFolder",Path.Combine(root,"fallback"));Set(f,"runFolder",first);Directory.CreateDirectory(first);File.WriteAllText(Path.Combine(first,"prefix.dat"),"KEEP");
  Check(Path.GetDirectoryName(first)==root && Path.GetFileName(first).StartsWith("RatID_sleep_wild_"),"Timestamped recording not created directly under output folder");Check((string)Get(f,"npSessionDeviceUid")=="001A0004434250062039314E","UID not pinned");
  var seen=DateTime.UtcNow;Call(f,"NpFirstReceive",seen,first);Set(f,"neuropixelsPayloadBytes",1024L);Set(f,"npV2RecoverySafe",true);
  Check((bool)Call(f,"NpFinishRecovery","Capture error: transport interrupted"),"Safe NP2 continuation not queued");
  Check(((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Enabled,"Retry timer not started");Check(File.ReadAllText(Path.Combine((string)Get(f,"npSessionFolder"),"session_status.txt")).Contains("lossless=False"),"Gap reported lossless");
  Call(f,"UpdateStartEnabled");Check(!((Button)Get(f,"btnStart")).Enabled&&!((Button)Get(f,"btnStreams")).Enabled,"Waiting recovery allows manual Start/profile edits");
  ((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Stop();Set(f,"npInternalRestart",true);
  string second=(string)Call(f,"NpSegmentFolder","unused");Set(f,"runFolder",second);Directory.CreateDirectory(second);
  Check(second!=first && Path.GetDirectoryName(second)==root && Path.GetFileName(second).StartsWith("RatID_sleep_wild_") && File.ReadAllText(Path.Combine(first,"prefix.dat"))=="KEEP","Restart did not create a separate timestamped recording or overwrote prefix");Check((uint)Get(f,"npV2RequestedProbeMask")==7U&&(uint)Get(f,"npV2RequestedSampleHz")==15000U,"Profile not retained");
  var link=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(second,"recovery_link.json")));
  Check((string)link["previous_recording_folder"]==first && (string)link["first_recording_folder"]==first && !(bool)link["gap_duration_known"],"Independent recording lost recovery/error linkage");
  Check(Directory.GetDirectories(root,"segment_*",SearchOption.AllDirectories).Length==0,"Recovery still creates segment folders");
  Call(f,"NpFirstReceive",seen.AddSeconds(2),second);Set(f,"npV2RecoveryComplete",true);Check(!(bool)Call(f,"NpFinishRecovery","Stopped cleanly"),"Clean stop retried");
  string session=(string)Get(f,"npSessionFolder");string events=File.ReadAllText(Path.Combine(session,"session_events.csv"));Check(events.Contains("resumed_first_host_receive")&&events.Contains("missing sample count/duration UNKNOWN"),"Gap journal missing");Check(File.ReadAllText(Path.Combine(session,"session_status.txt")).Contains("lossless=False"),"Recovered session reported lossless");
  // Exercise waiting Stop without a pumped callback or any hardware START.
  Set(f,"npRecoveryCancelled",false);Set(f,"captureThread",new Thread(()=>{}));((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Start();Call(f,"NpCancelRecovery");Check((bool)Get(f,"npRecoveryCancelled")&&!((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Enabled,"Stop leaves retry armed");Set(f,"captureThread",null);
  object g=RecoveryForm(root);string clean=(string)Call(g,"NpSegmentFolder","unused");Set(g,"runFolder",clean);Set(g,"npV2RecoveryComplete",false);Call(g,"NpFinishRecovery","Stopped best-effort with incomplete DAT");Check(File.ReadAllText(Path.Combine((string)Get(g,"npSessionFolder"),"session_status.txt")).Contains("state=incomplete"),"Incomplete non-error stop claimed complete");
  object bounded=RecoveryForm(root);for(int n=0;n<12;n++){if(n>0)Set(bounded,"npInternalRestart",true);string folder=(string)Call(bounded,"NpSegmentFolder","unused");Set(bounded,"runFolder",folder);Set(bounded,"npV2RecoverySafe",true);bool retry=(bool)Call(bounded,"NpFinishRecovery","Capture error: missing USB");Check(retry==(n<11),"Retry bound changed");((System.Windows.Forms.Timer)Get(bounded,"npRetryTimer")).Stop();}
  object v1=RecoveryForm(root);Set(v1,"selectedDeviceKind",Enum.Parse(formType.GetNestedType("DeviceKind",All),"XeNp"));Set(v1,"devicePath","usb#vid_1209&pid_b386#00190027434250062039314e#{fixture}");
  string v1First=(string)Call(v1,"NpSegmentFolder","unused");Set(v1,"runFolder",v1First);File.WriteAllText(Path.Combine(v1First,"prefix.dat"),"V1 KEEP");
  Check((bool)Call(v1,"NpFinishRecovery","Capture error: NP USB read timed out"),"V1 retry policy changed");((System.Windows.Forms.Timer)Get(v1,"npRetryTimer")).Stop();Set(v1,"npInternalRestart",true);
  string v1Next=(string)Call(v1,"NpSegmentFolder","unused");Set(v1,"runFolder",v1Next);
  Check(v1Next!=v1First && Path.GetDirectoryName(v1Next)==root && File.ReadAllText(Path.Combine(v1First,"prefix.dat"))=="V1 KEEP" && File.Exists(Path.Combine(v1Next,"recovery_link.json")),"V1 restart lost separate files/linkage");
  Check(!(bool)Call(v1,"NpFinishRecovery","Stopped cleanly"),"V1 clean stop retried");
  foreach(object form in new[]{f,g,bounded,v1}){((System.Windows.Forms.Timer)Get(form,"npRetryTimer")).Dispose();foreach(string name in new[]{"btnStart","btnStop","btnRefresh","btnNpLogger","btnSdInfo","btnDfu","btnStreams","btnSystemInfoRefresh","lblRecInfo","comboDrives","npAutoRecover","npSessionLabel"})((Control)Get(form,name)).Dispose();}
 }
 [STAThread]static int Main(string[] args){try{gui=Assembly.LoadFrom(Path.GetFullPath(args[0]));formType=gui.GetType("USB512_interface.Form1",true);mergeType=gui.GetType("USB512_interface.NpV2DatMerge",true);string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);bool recoveryOnly=args.Length>=3 && args[2]=="--recovery-only";if(!recoveryOnly)Merges(root);Policies();Overruns();if(args.Length>=4)StoredOverrun(args[3]);Recovery(Path.Combine(root,"recovery"));Console.WriteLine("PASS "+checks+" actual GUI "+(recoveryOnly?"targeted recovery":"merge/recovery")+" checks; exact overrun/transport policy, fresh recordings/UID/options, gap journal, bounded retry and Stop; no hardware accessed.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
