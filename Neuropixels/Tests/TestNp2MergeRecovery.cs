using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;

// File-only test of the delivered GUI. Never constructs/shows its main window,
// enumerates devices, opens WinUSB, starts BLE or writes firmware/SD hardware.
static class Np2MergeRecoveryTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly gui; static Type formType, mergeType; static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static object CallTyped(Type t,object target,string name,Type[] types,params object[] args){
  try{return t.GetMethod(name,All,null,types,null).Invoke(target,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}
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
  foreach(string name in new[]{"npPortConfigurations","npV2PendingMerges"}){var f=formType.GetField(name,All);f.SetValue(form,Activator.CreateInstance(f.FieldType));}
  Set(form,"selectedDeviceKind",Enum.Parse(formType.GetNestedType("DeviceKind",All),"NpV2"));Set(form,"saveFolder",root);
  Set(form,"devicePath","usb#vid_1209&pid_b387#npv2-001a0004434250062039314e#{fixture}");Set(form,"npV2RequestedProbeMask",7U);Set(form,"npV2RequestedSampleHz",15000U);return form;
 }
 static void Recovery(string root){
  Directory.CreateDirectory(root);object f=RecoveryForm(root);
  string first=(string)Call(f,"NpSegmentFolder",Path.Combine(root,"fallback"));Set(f,"runFolder",first);Directory.CreateDirectory(first);File.WriteAllText(Path.Combine(first,"prefix.dat"),"KEEP");
  Check(first.EndsWith("segment_0001"),"NP2 session not created");Check((string)Get(f,"npSessionDeviceUid")=="001A0004434250062039314E","UID not pinned");
  var seen=DateTime.UtcNow;Call(f,"NpFirstReceive",seen,first);Set(f,"neuropixelsPayloadBytes",1024L);Set(f,"npV2RecoverySafe",true);
  Check((bool)Call(f,"NpFinishRecovery","Capture error: transport interrupted"),"Safe NP2 continuation not queued");
  Check(((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Enabled,"Retry timer not started");Check(File.ReadAllText(Path.Combine((string)Get(f,"npSessionFolder"),"session_status.txt")).Contains("lossless=False"),"Gap reported lossless");
  Call(f,"UpdateStartEnabled");Check(!((Button)Get(f,"btnStart")).Enabled&&!((Button)Get(f,"btnStreams")).Enabled,"Waiting recovery allows manual Start/profile edits");
  ((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Stop();Set(f,"npInternalRestart",true);
  string second=(string)Call(f,"NpSegmentFolder","unused");Set(f,"runFolder",second);Directory.CreateDirectory(second);
  Check(second.EndsWith("segment_0002")&&File.ReadAllText(Path.Combine(first,"prefix.dat"))=="KEEP","Restart overwrote prefix");Check((uint)Get(f,"npV2RequestedProbeMask")==7U&&(uint)Get(f,"npV2RequestedSampleHz")==15000U,"Profile not retained");
  Call(f,"NpFirstReceive",seen.AddSeconds(2),second);Set(f,"npV2RecoveryComplete",true);Check(!(bool)Call(f,"NpFinishRecovery","Stopped cleanly"),"Clean stop retried");
  string session=(string)Get(f,"npSessionFolder");string events=File.ReadAllText(Path.Combine(session,"session_events.csv"));Check(events.Contains("resumed_first_host_receive")&&events.Contains("missing sample count/duration UNKNOWN"),"Gap journal missing");Check(File.ReadAllText(Path.Combine(session,"session_status.txt")).Contains("lossless=False"),"Recovered session reported lossless");
  // Exercise waiting Stop without a pumped callback or any hardware START.
  Set(f,"npRecoveryCancelled",false);Set(f,"captureThread",new Thread(()=>{}));((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Start();Call(f,"NpCancelRecovery");Check((bool)Get(f,"npRecoveryCancelled")&&!((System.Windows.Forms.Timer)Get(f,"npRetryTimer")).Enabled,"Stop leaves retry armed");Set(f,"captureThread",null);
  object g=RecoveryForm(root);string clean=(string)Call(g,"NpSegmentFolder","unused");Set(g,"runFolder",clean);Set(g,"npV2RecoveryComplete",false);Call(g,"NpFinishRecovery","Stopped best-effort with incomplete DAT");Check(File.ReadAllText(Path.Combine((string)Get(g,"npSessionFolder"),"session_status.txt")).Contains("state=incomplete"),"Incomplete non-error stop claimed complete");
  object bounded=RecoveryForm(root);for(int n=0;n<12;n++){if(n>0)Set(bounded,"npInternalRestart",true);string folder=(string)Call(bounded,"NpSegmentFolder","unused");Set(bounded,"runFolder",folder);Set(bounded,"npV2RecoverySafe",true);bool retry=(bool)Call(bounded,"NpFinishRecovery","Capture error: missing USB");Check(retry==(n<11),"Retry bound changed");((System.Windows.Forms.Timer)Get(bounded,"npRetryTimer")).Stop();}
  foreach(object form in new[]{f,g,bounded}){((System.Windows.Forms.Timer)Get(form,"npRetryTimer")).Dispose();foreach(string name in new[]{"btnStart","btnStop","btnRefresh","btnNpLogger","btnSdInfo","btnDfu","btnStreams","btnSystemInfoRefresh","lblRecInfo","comboDrives","npAutoRecover"})((Control)Get(form,name)).Dispose();}
 }
 [STAThread]static int Main(string[] args){try{gui=Assembly.LoadFrom(Path.GetFullPath(args[0]));formType=gui.GetType("USB512_interface.Form1",true);mergeType=gui.GetType("USB512_interface.NpV2DatMerge",true);string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);Merges(root);Policies();Recovery(Path.Combine(root,"recovery"));Console.WriteLine("PASS "+checks+" actual GUI merge/recovery checks; signed order/tails/phases/cancel/overwrite, transport policy, fresh segments/UID/options, gap journal, bounded retry and Stop; no hardware accessed.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
