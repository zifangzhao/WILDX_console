using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

// Actual delivered profile reader/UI; delegates model the IN-only USB boundary.
// No device discovery, mode command, firmware update or recording is performed.
static class NpV1RecordingProfileTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app;static int checks,serviceReads,statusReads,capReads;static bool? legacyVerification;
 static object service,status,caps;
 static Type T(string name){return app.GetType("USB512_interface."+name,true);}
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static object Get(object o,string name){return o.GetType().GetField(name,All).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,All).SetValue(o,value);}
 static object Invoke(MethodInfo method,object target,params object[] args){try{return method.Invoke(target,args);}catch(TargetInvocationException ex){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}}
 static TValue Service<TValue>(){serviceReads++;if(service is Exception)throw (Exception)service;return (TValue)service;}
 static TValue Status<TValue>(){statusReads++;if(status is Exception)throw (Exception)status;return (TValue)status;}
 static TValue Capabilities<TValue>(bool verify){capReads++;legacyVerification=verify;if(caps is Exception)throw (Exception)caps;return (TValue)caps;}
 static Delegate Reader(string method,Type result,bool argument=false){return Delegate.CreateDelegate((argument?typeof(Func<,>).MakeGenericType(typeof(bool),result):typeof(Func<>).MakeGenericType(result)),typeof(NpV1RecordingProfileTests).GetMethod(method,All).MakeGenericMethod(result));}
 static object Profile(){return Invoke(T("Form1").GetMethod("ReadNpV1RecordingProfile",All),null,Reader("Service",T("NpV1Management+Status")),Reader("Status",T("XeNpStatus")),Reader("Capabilities",T("NpModeCapabilities"),true));}
 static object State(uint role=3){var s=Activator.CreateInstance(T("NpV1Management+Status"),true);Set(s,"Split",true);Set(s,"Role",role);return s;}
 static object Legacy(bool streaming=false,int hz=30000){var s=Activator.CreateInstance(T("XeNpStatus"),true);Set(s,"Streaming",streaming);Set(s,"ApSampleRateHz",hz);return s;}
 static object Caps(bool selectable=true,int mask=7,int hz=7500){var c=Activator.CreateInstance(T("NpModeCapabilities"),true);Set(c,"AvailableMask",7);Set(c,"ActiveMask",mask);Set(c,"ApHz",hz);Set(c,"Selectable",selectable);Set(c,"WireId",0x76);Set(c,"BatchPackets",128);var rates=(int[])Get(c,"Rates");rates[0]=15;rates[1]=15;rates[2]=3;return c;}
 static void Reset(object s,object live,object capability){service=s;status=live;caps=capability;serviceReads=statusReads=capReads=0;legacyVerification=null;}
 static void Reject(string why){bool rejected=false;try{Profile();}catch(IOException){rejected=true;}Check(rejected,why);}
 static void Modern(){
  foreach(uint role in new[]{2U,3U}) {
   Reset(State(role),new Exception("Streaming-only 0x42 must not run"),Caps());var p=Profile();
   Check(serviceReads==1&&statusReads==0&&capReads==1&&legacyVerification==false,"Modern role sent live-status/fallback request");
   Check((int)Get(p,"AvailableMask")==7&&(int)Get(p,"SelectedMask")==7&&(int)Get(p,"SelectedHz")==7500,"Existing firmware profile lost");
   var validate=(Func<int,int,string>)Get(p,"Validate");
   for(int mask=1;mask<=7;mask++)foreach(int hz in new[]{7500,15000,20000,30000})Check(validate(mask,hz)==null,"Existing NP1 advertised resolution rejected: "+mask+"/"+hz);
   Check(validate(0,15000)!=null&&validate(8,15000)!=null,"Unadvertised connector admitted");
   Check(((string)Get(p,"Status")).Contains("SD-download")== (role==3),"USB mode not reported accurately");
  }
  foreach(string field in new[]{"Busy","PendingMode"}){var s=State();Set(s,field,1U);Reset(s,new Exception(),Caps());Reject("Busy/transition admitted");Check(statusReads==0&&capReads==0,"Busy state performed further queries");}
  foreach(uint role in new[]{0U,1U,4U}){Reset(State(role),new Exception(),Caps());Reject("Unsupported service role admitted");Check(statusReads==0&&capReads==0,"Wrong role performed further queries");}
  var recovery=State(2);Set(recovery,"Recovery",true);Reset(recovery,new Exception(),Caps());Reject("Recovery personality admitted");
  Reset(State(),new Exception("No 0x42 in download mode"),null);Reject("Missing download capabilities invented");Check(statusReads==0,"Missing download caps fell back to 0x42");
  Reset(State(2),Legacy(false,15000),null);var fixedProfile=Profile();Check((int)Get(fixedProfile,"SelectedHz")==15000&&statusReads==1,"Existing fixed streaming firmware lost");
 }
 static void OldFirmware(){
  Reset(null,Legacy(),Caps());var p=Profile();Check(serviceReads==1&&statusReads==1&&capReads==1&&legacyVerification==true,"Legacy status/capability sequence changed");Check((int)Get(p,"AvailableMask")==7,"Legacy selectable connectors lost");
  Reset(null,Legacy(false,15000),null);p=Profile();var valid=(Func<int,int,string>)Get(p,"Validate");Check((int)Get(p,"AvailableMask")==0&&(int)Get(p,"SelectedHz")==15000,"Fixed legacy firmware needs upgrade");Check(valid(0,15000)==null&&valid(1,15000)!=null&&valid(0,30000)!=null,"Legacy profile fabricated connector/rate support");
  Reset(null,Legacy(true),Caps());Reject("Legacy streaming allowed settings changes");Check(capReads==0,"Legacy busy performed capability query");
  Reset(State(2),null,Caps(false,1,30000));p=Profile();valid=(Func<int,int,string>)Get(p,"Validate");Check((int)Get(p,"AvailableMask")==1&&valid(1,30000)==null&&valid(3,15000)!=null,"Fixed firmware request changed");
  foreach(int code in new[]{121,5,1167,995}) {
   Reset(new Win32Exception(code,"service transport "+code),new Exception(),Caps());bool failed=false;try{Profile();}catch(Win32Exception ex){failed=ex.NativeErrorCode==code;}Check(failed&&statusReads==0&&capReads==0,"Service failure became legacy firmware");
   Reset(State(),new Exception(),new Win32Exception(code,"caps transport "+code));failed=false;try{Profile();}catch(Win32Exception ex){failed=ex.NativeErrorCode==code;}Check(failed&&statusReads==0,"Capability failure issued a live request");
  }
 }
 static void Dialog(){
  Reset(State(),new Exception("0x42 unavailable in download mode"),Caps(true,3,20000));
  var current=Profile();Check((int)Get(current,"SelectedMask")==3,"Default selected advertised P3 instead of the active P1+P2 profile");
  var warning=(Func<int,int,string>)Get(current,"Warning");Check(warning(3,20000)==null,"Advertised P1+P2 20 kHz still requires experimental confirmation");Check(warning(3,30000)!=null,"Two-probe 30 kHz qualification warning lost");
  var read=Reader("ReadProfile",T("NpUsbRecordingProfile"));
  using(var form=(Form)Activator.CreateInstance(T("NpRecordingDialog"),All,null,new object[]{false,0,30000,true,true,true,read,(Action<IWin32Window>)(owner=>{})},null)) {
   Invoke(T("NpRecordingDialog").GetMethod("ReadProfile",All),form);
   Check(((Button)Get(form,"accept")).Enabled,"Yoshi download-mode dialog still blocks Save");var ports=(CheckBox[])Get(form,"ports");
   Check(!((CheckBox)Get(form,"automatic")).Checked && !ports[2].Checked,"V1 selection still locks or defaults to P3");
   Check(ports[0].Enabled&&ports[1].Enabled&&ports[2].Enabled,"Yoshi cannot select probes");ports[1].Checked=ports[2].Checked=false;
   Check((int)T("NpRecordingDialog").GetProperty("ProbeMask",All).GetValue(form,null)==1,"Manual probe request lost");
   service=new Win32Exception(121,"service timeout (Win32 121)");Invoke(T("NpRecordingDialog").GetMethod("ReadProfile",All),form);
   Check(!((Button)Get(form,"accept")).Enabled&&!ports[0].Enabled&&((TextBox)Get(form,"status")).Text.Contains("121"),"Failure retained a stale profile or hid diagnostic code");
  }
  Reset(null,Legacy(false,15000),null);
  using(var form=(Form)Activator.CreateInstance(T("NpRecordingDialog"),All,null,new object[]{false,0,15000,true,true,true,read,(Action<IWin32Window>)(owner=>{})},null)) {
   Invoke(T("NpRecordingDialog").GetMethod("ReadProfile",All),form);Check(((Button)Get(form,"accept")).Enabled,"Fixed legacy firmware lost Save after manual selection change");
   Check((int)T("NpRecordingDialog").GetProperty("ProbeMask",All).GetValue(form,null)==0,"Fixed legacy mask was invented");
  }
 }
 static TValue ReadProfile<TValue>(){return (TValue)Profile();}
 [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Modern();OldFirmware();Dialog();Console.WriteLine("PASS "+checks+" actual-app NP1 profile/legacy/UI checks: download and streaming modes, no download 0x42, existing selectable/fixed firmware, busy/transition/recovery refusal, exact transport errors and probe selection; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
