using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Isolated actual-handler HIL launcher. Only the sole V2 hardware owner runs
// the two explicit actions. No args/help/self-test construct no Forms/devices.
static class NpV2HandoverHardware
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    const string Uid="001A0004434250062039314E";
    const string GuiHash="2A776C032C2DB84EFE7A5D44281E02245C2B98F3AC61147A8AAC7B2593425881";
    const string SdkHash="4638FD2DBA09BC673DB0D0BFA5174DB1BF2609B684E736D74A4D406C297AD526";
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
    static readonly Dictionary<string,object> Report=new Dictionary<string,object>();
    static Assembly app;static string output;static int exit=1;
    static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
    static string Hash(string path){using(var h=SHA256.Create())using(var s=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(s)).Replace("-","");}
    static Type T(string name){return app.GetType(name.Contains(".")?name:"USB512_interface."+name,true);}
    static object Get(object value,string name){var t=value.GetType();var f=t.GetField(name,All);return f!=null?f.GetValue(value):t.GetProperty(name,All).GetValue(value,null);}
    static void Set(object value,string name,object data){value.GetType().GetField(name,All).SetValue(value,data);}
    static uint U(object value,string name){return Convert.ToUInt32(Get(value,name),CultureInfo.InvariantCulture);}
    static Dictionary<string,object> Fields(object value){return value.GetType().GetFields(All).Where(f=>!f.IsStatic).ToDictionary(f=>f.Name,f=>f.GetValue(value));}
    static MethodInfo Method(Type type,string name,int count){var m=type.GetMethods(All).Where(x=>x.Name==name&&x.GetParameters().Length==count).ToArray();Need(m.Length==1,"Missing/ambiguous actual API "+type.FullName+"."+name+"/"+count);return m[0];}
    static object Call(object owner,string name,params object[] args){try{return Method(owner as Type??owner.GetType(),name,args.Length).Invoke(owner is Type?null:owner,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
    static object New(Type type,params object[] args){try{return type.GetConstructors(All).Single(c=>!c.IsStatic&&c.GetParameters().Length==args.Length).Invoke(args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
    static async Task<object> Await(object value){var task=(Task)value;await task;var p=task.GetType().GetProperty("Result");return p==null?null:p.GetValue(task,null);}
    static void Log(string text){Console.Error.WriteLine(DateTime.UtcNow.ToString("O")+" "+text);if(output!=null)File.AppendAllText(Path.Combine(output,"events.log"),DateTime.UtcNow.ToString("O")+" "+text+Environment.NewLine);}
    static void Save(){if(output!=null)File.WriteAllText(Path.Combine(output,"handover.json"),Json.Serialize(Report));}
    static void ExactUid(string text){Need(String.Equals(text,Uid,StringComparison.OrdinalIgnoreCase),"Exact approved V2 UID "+Uid+" required");}
    static ulong Address(string text){ulong value;Need(text.Length==12&&UInt64.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value)&&value!=0,"Explicit12-hex observed BLE address required");return UInt64.Parse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture);}
    static uint Session(string text){uint value;Need(UInt32.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value!=0,"Explicit nonzero expected session required");return UInt32.Parse(text,CultureInfo.InvariantCulture);}
    static void LoadHost(string path){
        path=Path.GetFullPath(path);string folder=Path.GetDirectoryName(path),manifest=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","console165-freeze.json"));
        var freeze=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(manifest));var files=Json.ConvertToType<Dictionary<string,object>[]>(freeze["files"]);
        Need(Convert.ToString(files.Single(x=>Convert.ToString(x["path"])=="WILDX_console.exe")["sha256"])==GuiHash&&Hash(path)==GuiHash,"Exact sealed default165 host required");
        Need(Convert.ToString(files.Single(x=>Convert.ToString(x["path"])=="NpV2SdDatExport.dll")["sha256"])==SdkHash&&Hash(Path.Combine(folder,"NpV2SdDatExport.dll"))==SdkHash,"Exact sealed default165 SDK required");
        Need(!(bool)freeze["first_sync_enabled"]&&!(bool)freeze["preparation_enabled"],"Default-off sealed165 required");
        // Production RunHelper resolves against the launcher's AppDomain base.
        // Stage only these exact original165 runtime companions; no firmware.
        var helper=new List<object>();foreach(string name in new[]{"XeNpFirmwareUpdater.exe","XeNpFirmwareUpdater.dll","XeNpFirmwareUpdater.deps.json","XeNpFirmwareUpdater.runtimeconfig.json"}){
            string relative="FirmwareUpdater/"+name,expected=Convert.ToString(files.Single(x=>Convert.ToString(x["path"])==relative)["sha256"]),local=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"FirmwareUpdater",name);Need(Hash(local)==expected,"Pinned bundled mode helper missing/changed: "+name);helper.Add(new {path=relative,sha256=expected});}
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string dep=Path.Combine(folder,new AssemblyName(e.Name).Name+".dll");return File.Exists(dep)?Assembly.LoadFrom(dep):null;};
        app=Assembly.LoadFrom(path);Need(app.GetName().Version==new Version(1,0,0,165),"Host165 version mismatch");Report["host"]=path;Report["host_sha256"]=GuiHash;Report["sdk_sha256"]=SdkHash;Report["freeze_manifest_sha256"]=Hash(manifest);Report["bundled_helper"]=helper;
        Report["asset_scope"]="Mode helper resolved from isolated AppDomain base and hash matched to sealed165. Firmware catalog/theme resources remain resolved by actual host Assembly.Location/embedded resources; no lab/new firmware copied.";
    }
    static void Target(object form,object connection,int epoch,ulong peer){Need((bool)Get(connection,"Connected")&&Convert.ToInt32(Get(connection,"Epoch"))==epoch&&(ulong)Get(form,"connectedAddress")==peer,"Exact BLE connection/peer changed; no mode replay");}
    static object RandomAddressType(){var type=Nullable.GetUnderlyingType(Method(T("NpBleConnection"),"ConnectAsync",2).GetParameters()[1].ParameterType);Need(type!=null&&type.IsEnum,"Actual nullable BLE address-type API missing");return Enum.Parse(type,"Random");}
    static void IdleV2(object status){Need(U(status,"Error")==0&&U(status,"Fault")==0&&!(bool)Get(status,"Busy")&&(bool)Call(T("CE32_console.NpV2SdControl"),"CanSwitchMode",status),"Idle fault-free V2 with USB-mode support required");}
    static void AdmitCaps(object caps,byte requestedPort){
        string uid=U(caps,"Uid0").ToString("X8")+U(caps,"Uid1").ToString("X8")+U(caps,"Uid2").ToString("X8");
        // CAPS has its own schema: model support is word28. Bytes20..23
        // (configuration Port/Model) are zero-filled by the real CAPS producer.
        // Production CapsAsync already validates NPR2/op/transaction/CRC.
        Need(requestedPort<3&&uid==Uid&&U(caps,"AvailableMask")==7&&U(caps,"SupportedModel")==4&&U(caps,"Error")==0&&(U(caps,"AvailableMask")&(1U<<requestedPort))!=0,"Fresh real NPC2 CAPS UID/supported-model/availability/error mismatch");
    }
    static async Task BleUsb(ulong peer){
        object form=null,connection=null;
        try{
            form=New(T("NpBleForm"),false);Need(!((Form)form).IsHandleCreated&&!((Form)form).Visible,"Hidden form required");((CheckBox)Get(form,"poll")).Checked=false;
            ((System.Windows.Forms.Timer)Get(form,"timer")).Stop();((System.Windows.Forms.Timer)Get(form,"bleImuTimer")).Stop();Need(Get(form,"preview")==null,"Preview must remain absent");
            connection=Get(form,"connection");Log("Connect caller's observed Random BLE address "+peer.ToString("X12")+"; caller owns hardware lease");
            await Await(Call(connection,"ConnectAsync",peer,RandomAddressType()));Set(form,"connectedAddress",peer);int epoch=Convert.ToInt32(Get(connection,"Epoch"));
            var status=await Await(Call(form,"ReadStatus"));Target(form,connection,epoch,peer);Need(Convert.ToInt32(Get(form,"recorderVersion"))==2,"Actual status did not identify V2");IdleV2(status);Report["initial_status"]=Fields(status);Report["ble_peer"]=peer.ToString("X12");Report["connection_epoch"]=epoch;
            var transport=New(T("NpBleForm").GetNestedType("ConfigTransport",All),connection,null);var client=New(T("Np2ConfigClient"),transport);var replies=new List<object>();Report["npc2_caps0_2"]=replies;
            Report["caps_semantics"]="CAPS uses SupportedModel4 and AvailableMask; configuration Port/Model bytes are not CAPS identity. Actual CapsAsync returns parsed fields and does not expose the raw reply.";
            for(byte port=0;port<3;port++){
                var caps=await Await(Call(client,"CapsAsync",port,CancellationToken.None));Target(form,connection,epoch,peer);
                var evidence=new {requested_port=port,fields=Fields(caps),raw_reply_available=false};replies.Add(evidence);Log("NPC2 CAPS before admission: "+Json.Serialize(evidence));Save();
                AdmitCaps(caps,port);
            }
            Report["npc2_caps0_2"]=replies;Need((string)Get(form,"bleDeviceUid")==Uid,"Production V2 UID discovery disagrees");var identity=Get(form,"firmwareIdentity");Report["firmware_identity"]=identity==null?null:Fields(identity);Save();
            var fresh=await Await(Call(form,"ReadStatus"));Target(form,connection,epoch,peer);IdleV2(fresh);Report["before_handoff_status"]=Fields(fresh);
            Log("Invoke actual NpBleForm.SdTestDownloadMode once; no Start/Stop/reset/flash/card maintenance");await Await(Call(form,"SdTestDownloadMode"));
            Report["handler_returned"]=true;Report["disconnected_after_handler"]=!(bool)Get(connection,"Connected");Need((bool)Report["disconnected_after_handler"],"Production handoff did not disconnect BLE");
            Report["result"]="PASS actual BLE mode handler returned correlated ACK; USB presence/personality not verified by this action";
        }finally{
            if(form!=null){Report["gui_log"]=((TextBox)Get(form,"log")).Text;((System.Windows.Forms.Timer)Get(form,"timer")).Stop();((System.Windows.Forms.Timer)Get(form,"bleImuTimer")).Stop();}
            if(connection!=null&&(bool)Get(connection,"Connected"))Call(connection,"Disconnect");
            if(form!=null){((System.Windows.Forms.Timer)Get(form,"timer")).Dispose();((System.Windows.Forms.Timer)Get(form,"bleImuTimer")).Dispose();((IDisposable)form).Dispose();}Save();
        }
    }
    static async Task DownloadList(string path,uint expectedSession){
        Need((string)Call(T("NpV2FirmwareForm"),"Uid",path)==Uid,"Explicit USB path does not carry exact V2 UID");object form=null;
        try{
            form=New(T("CE32_console.NpDownloadForm"),path,false);Need(!((Form)form).IsHandleCreated&&!((Form)form).Visible,"Download form must remain hidden without discovery");((System.Windows.Forms.Timer)Get(form,"enumerationTimer")).Stop();
            var source=New(T("CE32_console.NpDownloadForm").GetNestedType("Source",All));Set(source,"Path",path);Set(source,"Label","Explicit verified V2 "+Uid);Set(source,"Usb",true);
            var choices=(ComboBox)Get(form,"sources");Need(choices.Items.Count==0,"Constructor unexpectedly discovered sources");choices.Items.Add(source);choices.SelectedItem=source;
            Log("Invoke actual private NpDownloadForm.ListAsync once; production PrepareSourceAsync/EnsureDownload/ReadCatalog route");await Await(Call(form,"ListAsync"));await Task.Yield();
            Report["status_text"]=((Label)Get(form,"status")).Text;Report["resolved_path"]=Get(source,"Path");Report["listed_source_matches"]=ReferenceEquals(Get(form,"listedSource"),source);Save();
            Need((bool)Report["listed_source_matches"],"Production ListAsync caught an error: "+Report["status_text"]);Need((string)Call(T("NpV2FirmwareForm"),"Uid",(string)Get(source,"Path"))==Uid,"Resolved USB source changed identity");Need(choices.Items.Count==1&&ReferenceEquals(choices.SelectedItem,source),"Source selection changed");
            var records=((CheckedListBox)Get(form,"records")).Items.Cast<object>().ToArray();Report["records"]=records.Select(r=>new {fields=Fields(r),v2=Get(r,"V2"),complete=Get(r,"Complete"),downloadable=Get(r,"Downloadable"),text=r.ToString()}).ToArray();
            Need(records.Any(r=>(bool)Get(r,"V2")&&U(r,"Session")==expectedSession),"Expected V2 session not present in actual list");Report["expected_session"]=expectedSession;
            Report["result"]="PASS actual download ListAsync selected exact UID and listed expected V2 session; parent independently verifies USB mode3 presence";
        }finally{if(form!=null){((System.Windows.Forms.Timer)Get(form,"enumerationTimer")).Stop();((IDisposable)form).Dispose();}Save();}
    }
    static void SelfTest(){
        foreach(string name in new[]{"NpBleForm","NpBleConnection","Np2ConfigClient","Np2ConfigReply","NpV2FirmwareForm","CE32_console.NpV2SdControl","CE32_console.NpDownloadForm","CE32_console.NpDownloadRecord"})Need(T(name)!=null,"Actual full type missing");
        Method(T("NpBleForm"),"ReadStatus",0);Method(T("NpBleForm"),"SdTestDownloadMode",0);Method(T("Np2ConfigClient"),"CapsAsync",2);Method(T("CE32_console.NpDownloadForm"),"ListAsync",0);Method(T("NpV2FirmwareForm"),"Uid",1);Method(T("CE32_console.NpV2SdControl"),"CanSwitchMode",1);
        Need(T("NpBleForm").GetNestedType("ConfigTransport",All).GetConstructors(All).Any(c=>c.GetParameters().Length==2),"Actual ConfigTransport ctor missing");
        var source=T("CE32_console.NpDownloadForm").GetNestedType("Source",All);foreach(string field in new[]{"Path","Label","Usb"})Need(source.GetField(field,All)!=null,"Actual Source field missing");
        foreach(string field in new[]{"sources","records","listedSource","enumerationTimer","status"})Need(T("CE32_console.NpDownloadForm").GetField(field,All)!=null,"Actual download field missing");
        Need(RandomAddressType().ToString()=="Random"&&Address("F5D68A6BCC7C")==0xF5D68A6BCC7CUL&&Session("1")==1,"Pure argument/address-type parsing failed");
        Need((string)Call(T("NpV2FirmwareForm"),"Uid",@"\?\usb#vid_1209&pid_b386#npv2-001a0004434250062039314e#{test}")==Uid,"Pure USB UID parser mismatch");
        var raw=new byte[64];raw[0]=(byte)'N';raw[1]=(byte)'P';raw[2]=(byte)'R';raw[3]=(byte)'2';raw[4]=2;raw[5]=5;
        foreach(var word in new[]{Tuple.Create(8,123U),Tuple.Create(16,876U),Tuple.Create(24,7U),Tuple.Create(28,4U),Tuple.Create(32,0x001A0004U),Tuple.Create(36,0x43425006U),Tuple.Create(40,0x2039314EU)})Buffer.BlockCopy(BitConverter.GetBytes(word.Item2),0,raw,word.Item1,4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)Call(T("Np2ConfigClient"),"Crc",raw,60)),0,raw,60,4);
        var fixture=Call(T("Np2ConfigClient"),"Parse",raw,(byte)5,123U);Need(U(fixture,"Model")==0&&U(fixture,"Port")==0,"Actual CAPS fixture schema changed");for(byte port=0;port<3;port++)AdmitCaps(fixture,port);
        foreach(var bad in new[]{new object[]{"Uid2",0U},new object[]{"SupportedModel",3U},new object[]{"AvailableMask",3U},new object[]{"Error",(byte)1}}){string field=(string)bad[0];object original=Get(fixture,field);Set(fixture,field,bad[1]);bool rejected=false;try{AdmitCaps(fixture,0);}catch(InvalidDataException){rejected=true;}Set(fixture,field,original);Need(rejected,"CAPS guard weakened: "+field);}
        Report["caps_schema_guard_fixture"]="PASS real165 Parse of firmware-shaped CAPS with Model/Port0; requested ports0..2 admitted; wrong UID/SupportedModel/mask/error rejected. Synthetic offline evidence only.";
        Report["result"]="PASS offline hashes/arguments/full reflection API names; no Form/device construction, enumeration or query";
    }
    static void Help(){Console.WriteLine("No args/help/self-test perform no hardware access. Sole hardware owner must hold the device lease.\nble-usb <host165> <exactUID> <BLE12hex> <fresh-out> --observed-random\ndownload-list <host165> <exactUID> <exactUSBpath> <expectedSession:uint> <fresh-out>\n--self-test <host165>");}
    [STAThread]static int Main(string[] args){
        if(args.Length==0||args[0]=="--help"||args[0]=="help"){Help();return 0;}string mode=args[0];ulong peer=0;uint session=0;
        try{
            Need(mode=="--self-test"&&args.Length==2||(mode=="ble-usb"||mode=="download-list")&&args.Length==6,"Invalid arguments; use --help");
            if(mode!="--self-test")ExactUid(args[2]);
            if(mode=="ble-usb"){peer=Address(args[3]);Need(args[5]=="--observed-random","Explicit --observed-random required");}
            if(mode=="download-list"){Need(args[3].IndexOf(Uid,StringComparison.OrdinalIgnoreCase)>=0&&args[3].IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0,"Explicit exact NP USB path required");session=Session(args[4]);}
            LoadHost(args[1]);Report["mode"]=mode;Report["uid"]=Uid;
            if(mode=="--self-test"){SelfTest();Console.WriteLine(Json.Serialize(Report));return 0;}
            output=Path.GetFullPath(args[mode=="ble-usb"?4:5]);Need(!Directory.Exists(output),"Fresh output required");Directory.CreateDirectory(output);
            EventHandler start=null;start=async(s,e)=>{Application.Idle-=start;try{if(mode=="ble-usb")await BleUsb(peer);else await DownloadList(args[3],session);exit=0;}catch(Exception ex){Report["error"]=ex.ToString();Log(ex.ToString());}finally{Save();Application.ExitThread();}};
            Application.Idle+=start;Application.Run();return exit;
        }catch(Exception ex){Report["error"]=ex.ToString();Console.Error.WriteLine(ex);Save();return 1;}
    }
}
