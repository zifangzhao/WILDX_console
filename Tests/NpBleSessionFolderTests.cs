using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Run against the actual built app. Uninitialized Forms + injected controls
// deliberately skip all constructors, discovery, WinRT watchers and UI dialogs.
// A null connection is an early-I/O trap in cancelled/rejected Start tests.
static class NpBleSessionFolderTests {
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    const string Uid="001A0004434250062039314E";
    static Assembly app;static int checks;static string root;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=8*1024*1024};
    static Type T(string name){return app.GetType(name.Contains(".")?name:"USB512_interface."+name,true);}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args,null);}
    static object Bare(string name){return FormatterServices.GetUninitializedObject(T(name));}
    static object Get(object o,string name){var f=o.GetType().GetField(name,All);return f!=null?f.GetValue(o):o.GetType().GetProperty(name,All).GetValue(o,null);}
    static void Set(object o,string name,object value){var f=o.GetType().GetField(name,All);if(f==null)throw new MissingFieldException(o.GetType().FullName,name);f.SetValue(o,value);}
    static object Call(object owner,string name,params object[] args){var t=owner as Type??owner.GetType();var m=t.GetMethods(All).Single(x=>x.Name==name&&x.GetParameters().Length==args.Length);try{return m.Invoke(owner is Type?null:owner,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}}
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void NewCollection(object owner,string name){var f=owner.GetType().GetField(name,All);Set(owner,name,Activator.CreateInstance(f.FieldType));}
    static string Child(string name){return Path.Combine(root,name);}
    static bool Within(string folder,string parent){string prefix=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;return Path.GetFullPath(folder).StartsWith(prefix,StringComparison.OrdinalIgnoreCase);}
    static Dictionary<string,object> Read(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
    static object Ble(string destination,Func<string> choose,bool connection=false){
        object f=Bare("NpBleForm");Set(f,"bleDestinationFolder",new TextBox {Text=destination??""});Set(f,"ChooseBleSessionFolder",choose);Set(f,"log",new TextBox());
        // Out-of-range transport identity deliberately exercises the optional
        // locator's rejection path before it can touch real LocalAppData.
        // Valid48-bit locator registration is covered by the import fixture.
        Set(f,"recorderVersion",2);Set(f,"bleDeviceUid",Uid);Set(f,"connectedAddress",0x123456789abcdef0UL);
        foreach(string n in new[]{"np2ApplyConfigurations","np2HostConfigurations","v1HostConfigurations"})NewCollection(f,n);
        if(f.GetType().GetField("bleRtcUncertain",All)!=null)NewCollection(f,"bleRtcUncertain");
        if(connection)Set(f,"connection",New("NpBleConnection"));return f;
    }
    static void DisposeBle(object f){var c=Get(f,"connection");if(c!=null){var journal=Get(c,"clockJournal");if(journal!=null){((IDisposable)journal).Dispose();Set(c,"clockJournal",null);}((IDisposable)c).Dispose();}((TextBox)Get(f,"bleDestinationFolder")).Dispose();((TextBox)Get(f,"log")).Dispose();}
    static async Task Core(object f){await (Task)Call(f,"StartRecordingCore",0U,null);}
    static void Admission(){
        int picks=0;var canceled=Ble("",()=>{picks++;return null;});try{
            Need(!(bool)Call(canceled,"PrepareBleSessionFolder")&&picks==1,"Cancelled chooser admitted a session");
            Need(Get(canceled,"blePendingSessionFolder")==null,"Cancelled chooser retained a pending folder");
            Core(canceled).GetAwaiter().GetResult();Need(picks==2&&Get(canceled,"connection")==null,"Cancelled actual Start reached device I/O");
        }finally{DisposeBle(canceled);}
        string blocked=Child("not-a-directory");File.WriteAllText(blocked,"preserve this file");
        var rejected=Ble(Path.Combine(blocked,"child"),()=>{throw new Exception("Typed path unexpectedly opened chooser");});try{
            bool failed=false;try{Core(rejected).GetAwaiter().GetResult();}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
            Need(failed&&Get(rejected,"connection")==null,"Unwritable destination did not reject before device I/O");
            Need(Get(rejected,"blePendingSessionFolder")==null&&File.ReadAllText(blocked)=="preserve this file","Rejected destination retained session/fallback or changed existing data");
        }finally{DisposeBle(rejected);}
        string typed=Child("chosen root with spaces");var selected=Ble(typed,()=>{throw new Exception("Typed root unexpectedly reopened chooser");});try{
            Need((bool)Call(selected,"PrepareBleSessionFolder"),"Writable typed root rejected");string first=(string)Get(selected,"blePendingSessionFolder");
            Need(Within(first,typed)&&Directory.Exists(first)&&Path.GetFullPath(first)!=Path.GetFullPath(typed),"Session was not a fresh child under chosen root");
            Need(Directory.GetFiles(first).All(p=>Path.GetFileName(p)=="ble_session.log"),"Writability probe left residue before metadata");
            Need(File.Exists(Path.Combine(first,"ble_session.log")),"Optional locator rejection did not stay in the selected session log");
            Need((bool)Call(selected,"PrepareBleSessionFolder"),"Second session root rejected");string second=(string)Get(selected,"blePendingSessionFolder");
            Need(first!=second&&Within(second,typed)&&Directory.Exists(first),"Independent recording reused/removed prior session folder");
        }finally{DisposeBle(selected);}
        string chosen=Child("picker-root");picks=0;var picked=Ble("",()=>{picks++;return chosen;});try{
            Need((bool)Call(picked,"PrepareBleSessionFolder")&&picks==1,"Explicit chooser root was not accepted once");
            Need(Within((string)Get(picked,"blePendingSessionFolder"),chosen),"Chooser selection was replaced by a hidden destination");
        }finally{DisposeBle(picked);}
    }
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static uint Crc(byte[] b,int count){uint c=UInt32.MaxValue;for(int i=0;i<count;i++){c^=b[i];for(int n=0;n<8;n++)c=(c>>1)^((0U-(c&1))&0xedb88320U);}return ~c;}
    static byte[] Clock(uint magic,uint id){var b=new byte[64];Put(b,0,magic);Put(b,4,1);Put(b,12,id);Put(b,60,Crc(b,60));return b;}
    static byte[] Snapshot(){var b=new byte[140];Put(b,0,0x31534e53);Put(b,4,1);Put(b,8,3);Put(b,12,3);Put(b,16,1000);Put(b,20,1);Put(b,24,1);Put(b,32,995);Put(b,36,994);Put(b,44,128);Put(b,48,1000000);Put(b,52,unchecked((uint)-500000));Put(b,60,12500);Put(b,72,42000);return b;}
    static void Evidence(object form,string folder){
        object connection=Get(form,"connection"),context=Get(connection,"clockContext");
        Need((string)Get(context,"host_recording_receipt")==folder,"Actual clock context did not use the selected session");
        Need((string)Get(connection,"recordingClockFolder")==folder,"Actual storage binding did not retain the recording clock folder override");
        // Production journal creation remains lazy and requires a live device.
        // Inject only this real file writer; never weaken admission or open BLE.
        var clock=New("NpClockJournal",folder,"MCU-"+Uid,"session-folder-fixture");Set(connection,"clockJournal",clock);Set(connection,"clockJournalEpoch",Convert.ToInt32(Get(connection,"Epoch")));Set(connection,"clockJournalFolder",folder);
        Need(Get(clock,"Error")==null&&Path.GetFullPath((string)Get(connection,"ClockJournalFolder"))==Path.GetFullPath(folder),"Session clock writer could not use chosen folder");
        long start=Stopwatch.GetTimestamp();var request=Clock(0x3254504e,44);var reply=Clock(0x3252504e,44);Put(reply,20,100);Put(reply,24,101);Put(reply,52,1000);Put(reply,60,Crc(reply,60));
        var attempt=Call(clock,"BeginExchange",request,start,Convert.ToInt32(Get(connection,"Epoch")),context);Need(attempt!=null,"Read-only evidence fixture was not accepted");
        Call(clock,"FinishExchange",attempt,reply,start+Stopwatch.Frequency/100,null);
        Need(Directory.GetFiles(folder,"ble_clock_*.jsonl",SearchOption.AllDirectories).Length>0,"Clock evidence was not grouped under selected session");
        using(var imu=(IDisposable)New("NpBleImuSession",folder,Uid))Call(imu,"Accept",Snapshot());
        Need(File.Exists(Path.Combine(folder,"ble_imu_stream.txt"))&&File.Exists(Path.Combine(folder,"imu_native_samples.jsonl")),"Actual IMU receipt escaped selected session");
        var legacy=(byte[])Call(T("CE32_console.NpV2SdControl"),"Request",2U,31U,20000U,100U,0U,7U);
        var wrapped=(byte[])Call(T("NpFirstSyncSd"),"WrapLegacy",legacy,2,Uid,0x1122334455667788UL,0UL);
        var sd=New("NpFirstSyncSdAttempt",Uid,2,0,wrapped,legacy);Set(form,"bleFirstSyncEvidenceNoticeId",null);
        Call(form,"PersistBleFirstSyncAttempt",sd);
        string evidence=(string)Get(sd,"EvidencePath");Need(evidence!=null&&Within(evidence,folder)&&File.Exists(evidence),"NPSD attempt evidence escaped selected session");
        Need(Read(evidence)["record_association_confirmed"].Equals(false),"Folder grouping fabricated on-card association");
    }
    static void Metadata(){
        string chosen=Child("metadata-root");var f=Ble(chosen,()=>chosen,true);try{
            Need((bool)Call(f,"PrepareBleSessionFolder"),"Metadata destination admission failed");string folder=(string)Get(f,"blePendingSessionFolder");
            var status=New("CE32_console.NpSdStatus");Set(status,"Hz",20000U);Set(status,"Mask",7U);Set(status,"Capacity",65536U);Set(status,"Start",8192U);
            var request=(byte[])Call(T("CE32_console.NpV2SdControl"),"Request",2U,31U,20000U,100U,0U,7U);
            Call(f,"BeginBleSessionStorage");Call(f,"BeginBleRecordingMetadata",status,request);Need((string)Get(f,"bleMetadataFolder")==folder,"Metadata replaced admitted session destination");
            Need(Get(f,"bleRecordingMetadata")!=null&&File.Exists(Path.Combine(folder,"recording_settings.json")),"Initial mandatory recording metadata missing");
            Need(Get(f,"blePendingSessionFolder")==null&&(string)Read(Path.Combine(folder,"recording_settings.json"))["host_session_folder"]==folder,"Metadata did not consume and retain the admitted path");
            Need(!Convert.ToBoolean(Read(Path.Combine(folder,"recording_settings.json"))["recording_complete"]??false),"Host metadata prematurely claims completed SD data");
            Evidence(f,folder);
        }finally{DisposeBle(f);}
        var absent=Ble(Child("never-prepared"),()=>null,true);try{
            var s=New("CE32_console.NpSdStatus");bool failed=false;try{Call(absent,"BeginBleRecordingMetadata",s,new byte[64]);}catch(IOException){failed=true;}catch(InvalidOperationException){failed=true;}
            Need(failed&&Get(absent,"bleMetadataFolder")==null,"Metadata silently chose a fallback folder without admission");
        }finally{DisposeBle(absent);}
        var writeFailure=Ble(Child("metadata-write-failure"),()=>null,true);try{
            Need((bool)Call(writeFailure,"PrepareBleSessionFolder"),"Write-failure destination admission failed");string folder=(string)Get(writeFailure,"blePendingSessionFolder");Call(writeFailure,"BeginBleSessionStorage");
            Directory.CreateDirectory(Path.Combine(folder,"recording_settings.json"));string raw=Path.Combine(folder,"sentinel.dat");File.WriteAllBytes(raw,new byte[]{2,7,9});
            var s=New("CE32_console.NpSdStatus");Set(s,"Hz",20000U);Set(s,"Mask",7U);bool rejected=false;
            try{Call(writeFailure,"BeginBleRecordingMetadata",s,new byte[64]);}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}
            Need(rejected,"Mandatory initial metadata failure was swallowed before START");Need((string)Get(writeFailure,"bleMetadataFolder")==folder&&File.ReadAllBytes(raw).SequenceEqual(new byte[]{2,7,9}),"Metadata failure redirected storage or modified data");
        }finally{DisposeBle(writeFailure);}
    }
    sealed class Port:RealProxy {
        readonly BlockingCollection<byte[]> input=new BlockingCollection<byte[]>();internal int Opens,Closes;
        internal Port():base(T("INpGpioRtcPort")){}
        public override IMessage Invoke(IMessage message){var call=(IMethodCallMessage)message;try{object result=null;switch(call.MethodName){
            case "GetType":result=T("INpGpioRtcPort");break;case "Open":Opens++;break;case "Close":Closes++;break;case "Dispose":break;
            case "Read":byte[] bytes;if(!input.TryTake(out bytes,20))throw new TimeoutException();Buffer.BlockCopy(bytes,0,(byte[])call.Args[0],0,bytes.Length);result=bytes.Length;break;
            case "Write":var q=(byte[])call.Args[0];var r=new byte[15];r[0]=0x3c;r[1]=0x91;Buffer.BlockCopy(q,2,r,2,4);Buffer.BlockCopy(BitConverter.GetBytes(100000UL),0,r,6,8);r[14]=0x3e;input.Add(r);break;
            default:throw new InvalidOperationException(call.MethodName);
        }return new ReturnMessage(result,null,0,call.LogicalCallContext,call);}catch(Exception e){return new ReturnMessage(e,call);}}
    }
    static void Gpio(){
        object f=Bare("Form1");
        // Initialize WinForms' base state only, so the actual optional-warning
        // path can check IsHandleCreated. No application constructor runs.
        typeof(Form).GetConstructor(Type.EmptyTypes).Invoke(f,new object[0]);
        var enabled=new CheckBox();var devices=new ComboBox();var refresh=new Button();var status=new Label();var info=new TextBox();
        Set(f,"gpioRtcEnabled",enabled);Set(f,"gpioRtcDevices",devices);Set(f,"gpioRtcRefresh",refresh);Set(f,"gpioRtcStatus",status);Set(f,"gpioRtcInfo",info);NewCollection(f,"recordingWarnings");
        var device=New("NpGpioRtcDevice");Set(device,"Port","COM77");Set(device,"Identity","fixture-only-no-device");devices.Items.Add(device);devices.SelectedItem=device;
        int discoveries=0;Set(f,"gpioRtcDevicePresent",new Func<string,string,bool>((p,id)=>{discoveries++;return false;}));Set(f,"recordingWarningPresenter",new Action<string>(_=>{}));
        string folder=Child("gpio-disabled");Directory.CreateDirectory(folder);var settings=New("NpRecordingSettings",folder,"","ble_sd",true,null,null,Uid);
        try{
            Need(!enabled.Checked,"GPIO opt-in fixture unexpectedly enabled");Call(f,"StartGpioRtcForRecording",folder,settings);Call(settings,"Write");
            Need(discoveries==0&&Get(f,"gpioRtcLogger")==null&&!File.Exists(Path.Combine(folder,"gpio_rtc_start_error.txt")),"Unchecked GPIO attempted discovery/open");
            var gpio=Json.ConvertToType<Dictionary<string,object>>(Read(Path.Combine(folder,"recording_settings.json"))["gpio_rtc"]);Need(!(bool)gpio["requested"],"Unchecked GPIO metadata is not explicit");
            enabled.Checked=true;Call(f,"StartGpioRtcForRecording",folder,settings);Call(settings,"Write");
            Need(discoveries==1&&Get(f,"gpioRtcLogger")==null&&File.Exists(Path.Combine(folder,"gpio_rtc_start_error.txt")),"Checked GPIO did not validate the explicitly selected fake device");
            gpio=Json.ConvertToType<Dictionary<string,object>>(Read(Path.Combine(folder,"recording_settings.json"))["gpio_rtc"]);Need((bool)gpio["requested"],"Checked GPIO metadata lost the opt-in request");
            enabled.Checked=false;
            // A previously active independent logger must continue across new
            // neural folders, even when the new-session opt-in is unchecked.
            string original=Child("gpio-continuing");Directory.CreateDirectory(original);var port=new Port();var logger=New("NpGpioRtcLogger",original,device,port.GetTransparentProxy());Call(logger,"Start");Set(f,"gpioRtcLogger",logger);
            try{Call(f,"StartGpioRtcForRecording",folder,settings);Need(ReferenceEquals(Get(f,"gpioRtcLogger"),logger)&&port.Opens==1&&port.Closes==0&&discoveries==1,"Unchecked new-session option stopped/reopened a continuing logger");Need(File.Exists(Path.Combine(folder,"gpio_rtc_link.json")),"Continuing GPIO log lost its session link");}finally{((IDisposable)logger).Dispose();Set(f,"gpioRtcLogger",null);}
        }finally{enabled.Dispose();devices.Dispose();refresh.Dispose();status.Dispose();info.Dispose();((IDisposable)f).Dispose();}
    }
    [STAThread]static int Main(string[] args){try{
        if(args.Length!=2)throw new ArgumentException("Actual app path and fresh output are required");string path=Path.GetFullPath(args[0]);root=Path.GetFullPath(args[1]);if(Directory.Exists(root))throw new IOException("Fresh output required");Directory.CreateDirectory(root);
        string directory=Path.GetDirectoryName(path);AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string p=Path.Combine(directory,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};app=Assembly.LoadFrom(path);
        var settings=(ApplicationSettingsBase)T("USB512_interface.Properties.Settings").GetProperty("Default",All).GetValue(null,null);SettingsSavingEventHandler noSave=(s,e)=>e.Cancel=true;settings.SettingsSaving+=noSave;
        try{
            var gpioDefault=(DefaultSettingValueAttribute)settings.GetType().GetProperty("GpioRtcEnabled",All).GetCustomAttributes(typeof(DefaultSettingValueAttribute),false).Single();Need(String.Equals(gpioDefault.Value,"False",StringComparison.OrdinalIgnoreCase),"GPIO logging default is not opt-in");
            Admission();Metadata();Gpio();
        }finally{settings.SettingsSaving-=noSave;}
        File.WriteAllText(Path.Combine(root,"receipt.json"),Json.Serialize(new{status="PASS",checks,app_version=app.GetName().Version.ToString(),application_form_constructors=0,framework_form_base_initializations=1,hardware_calls=0,source_mutations=0,preferences_saved=false,locator_scope="Deliberate invalid transport identity tests optional failure isolation; valid locator import covered separately",scope="Actual folder/Start admission and grouped writer paths; GPIO simulated port only; no Form handles/Load/Shown"}));Console.WriteLine("PASS "+checks+" actual-app BLE folder admission/grouped evidence/GPIO opt-in checks; no hardware.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
