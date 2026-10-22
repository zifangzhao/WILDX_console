using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

static class NpBleRecordingSettingsTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app;static int checks;
 static Type T(string name){return app.GetType(name,true);}
 static object Get(object obj,string name){return obj.GetType().GetField(name,All).GetValue(obj);}
 static void Set(object obj,string name,object value){obj.GetType().GetField(name,All).SetValue(obj,value);}
 static object Call(object obj,string method,params object[] args){return obj.GetType().GetMethod(method,All).Invoke(obj,args);}
 static void Need(bool good,string why){checks++;if(!good)throw new Exception(why);}
 static Func<TValue> Read<TValue>(object profile){return ()=>(TValue)profile;}
 static Delegate Reader(object profile){return (Delegate)typeof(NpBleRecordingSettingsTests).GetMethod("Read",All).MakeGenericMethod(T("USB512_interface.NpUsbRecordingProfile")).Invoke(null,new[]{profile});}
 static void Render(Form form,string output){
  using(var host=new Form{Text=form.Text,ClientSize=form.ClientSize,Font=form.Font,BackColor=form.BackColor,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
   foreach(Control child in form.Controls.Cast<Control>().ToArray())host.Controls.Add(child);
   host.Show();Application.DoEvents();
   using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(output);}
  }
 }
 static void Settings(bool v2,string output){
  // Only the profile model is invoked on the uninitialized host. No BLE
  // constructor, watcher, connection, recording or hardware is used here.
  object host=FormatterServices.GetUninitializedObject(T("USB512_interface.NpBleForm"));
  object status=Activator.CreateInstance(T("CE32_console.NpSdStatus"),true);
  Set(status,"Mask",3U);Set(status,"Hz",20000U);Set(status,"Flags",1048576U|17U|512U);Set(status,"State",v2?1U:0U);
  Set(host,"current",status);Set(host,"recorderVersion",v2?2:1);Set(host,"np2AvailableMask",3U);
  Set(host,"acknowledgedSdProfiles",new HashSet<Tuple<uint,uint,uint>>());
  using(var profiles=new ComboBox()){
   Set(host,"profiles",profiles);Call(host,"Profiles",status);
   object profile=Call(host,"RecordingProfile");
   var validate=(Func<int,int,string>)Get(profile,"Validate");
   Need(validate(3,20000)==null,"P1+P2 at20k was rejected");
   Need(validate(5,20000)!=null,"Unsupported P1+P3 at20k was admitted");
   Need(((int[])Get(profile,"Rates")).Contains(20000),"20k missing from sample rates");
   if(!v2){
    int modes=0;
    for(int mask=1;mask<=7;mask++)foreach(int hz in new[]{7500,15000,20000,30000}){
     bool expected=hz<=15000 || mask==1 || mask==2 || mask==4 || mask==3;
     bool allowed=validate(mask,hz)==null;if(allowed)modes++;
     Need(allowed==expected,"V1 SD combination disagrees with current firmware: "+mask+" / "+hz);
    }
    Need(modes==22,"Current V1 did not expose the22 supported SD combinations");
    Set(status,"Flags",17U|512U);var canSelect=T("CE32_console.NpV1BleProtocol").GetMethod("CanSelect",All);
    Need(!(bool)canSelect.Invoke(null,new object[]{status,3U,20000U}),"Legacy firmware gained unadvertised20k support");
    Set(status,"Flags",17U|512U|524288U);
    Need((bool)canSelect.Invoke(null,new object[]{status,3U,20000U}) && !(bool)canSelect.Invoke(null,new object[]{status,1U,20000U}),"Legacy scaled20 capability expanded to unsupported combinations");
    Set(status,"Flags",1048576U|17U|512U);
   }
   {
    var warning=(Func<int,int,string>)Get(profile,"Warning");var accepted=(Action<int,int>)Get(profile,"WarningAccepted");
    Need(warning(3,30000)!=null && warning(3,30000)!=null,"Unaccepted experimental profile was silently dismissed");
    accepted(3,30000);Need(warning(3,30000)==null,"Accepted profile repeated its warning");
    ((HashSet<Tuple<uint,uint,uint>>)Get(host,"acknowledgedSdProfiles")).Clear();Need(warning(3,30000)!=null,"A new connection retained the previous experimental-profile acknowledgment");
   }
   object options=Activator.CreateInstance(T("USB512_interface.NpSdRecordingOptions"),true);
   Set(options,"MediaAvailable",!v2);Set(options,"Camera",true);Set(options,"CameraHz",40);
   using(var dialog=(Form)Activator.CreateInstance(T("USB512_interface.NpRecordingDialog"),All,null,new object[]{v2,3,20000,false,false,false,Reader(profile),null,options},null)){
    Call(dialog,"ReadProfile");var ports=(CheckBox[])Get(dialog,"ports");var accept=(Button)Get(dialog,"accept");
    Need(ports[0].Checked&&ports[1].Checked&&!ports[2].Checked,"USB-style port selection lost");
    Need(ports[2].Enabled==!v2,"Unavailable V2 P3 enabled");
    Need(accept.Enabled,"Valid two-probe20k settings cannot save");
    Need(((CheckBox)Get(dialog,"merge")).Parent==null&&((CheckBox)Get(dialog,"misc")).Parent==null,"USB host-output options exposed for SD recording");
    Need(dialog.GetType().GetField("allowUnqualified",All)==null && options.GetType().GetField("AllowUnqualified",All)==null,"Persistent unqualified-profile checkbox still exists");
    ports[0].Checked=ports[1].Checked=false;Need(!accept.Enabled,"Empty probe selection can save");
    ports[0].Checked=ports[1].Checked=true;
    Render(dialog,Path.Combine(output,v2?"ble-recording-v2.png":"ble-recording-v1.png"));
   }
   if(!v2)using(var dialog=(Form)Activator.CreateInstance(T("USB512_interface.NpRecordingDialog"),All,null,new object[]{false,3,20000,false,false,false,Reader(profile),null,options},null)){
    Call(dialog,"ReadProfile");((ComboBox)Get(dialog,"source")).SelectedIndex=1;
    Need(((Button)Get(dialog,"accept")).Enabled,"Supported media settings cannot save");
    ((CheckBox)Get(dialog,"camera")).Checked=false;Need(!((Button)Get(dialog,"accept")).Enabled,"No media streams admitted");
    ((CheckBox)Get(dialog,"microphone")).Checked=true;Call(dialog,"SaveSdOptions");
    Need((bool)Get(options,"Media")&&!(bool)Get(options,"Camera")&&(bool)Get(options,"Microphone"),"Media choices were not saved");
   }
   if(v2){
    Set(host,"np2AvailableMask",7U);Call(host,"Profiles",status);
    var all=(Func<int,int,string>)Get(Call(host,"RecordingProfile"),"Validate");int modes=0;
    for(int mask=1;mask<=7;mask++)foreach(int hz in new[]{7500,15000,20000,30000}){
     bool allowed=all(mask,hz)==null;if(allowed)modes++;
     Need(allowed==(mask==1 || mask==3 || mask==7),"V2 SD combination disagrees with advertised prefix profiles");
    }
    Need(modes==12,"Three-port V2 did not expose all12 advertised SD combinations");
    Set(status,"Flags",17U|512U);Call(host,"Profiles",status);
    all=(Func<int,int,string>)Get(Call(host,"RecordingProfile"),"Validate");
    Need(all(7,7500)!=null && all(7,20000)==null,"Legacy V2 gained unadvertised7.5k support or lost20k");
   }
  }
 }
 [STAThread]static int Main(string[] args){try{
  app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);Settings(false,args[1]);Settings(true,args[1]);
  object failed=Activator.CreateInstance(T("CE32_console.NpSdStatus"),true);Set(failed,"State",4U);Set(failed,"Fault",2U);Set(failed,"Mask",3U);
  string text=(string)T("CE32_console.NpV1BleProtocol").GetMethod("StateText",All).Invoke(null,new[]{failed});
  Need(text.Contains("Probe capture source failed")&&text.Contains("state 4, fault 2")&&!text.Contains("SD write failed"),"V1 source fault misdiagnosed");
  Need(text.StartsWith("Stopped after recording error"),"Finalized fault remains waiting for Complete");
  Set(failed,"Flags",4U);text=(string)T("CE32_console.NpV1BleProtocol").GetMethod("StateText",All).Invoke(null,new[]{failed});Need(!text.StartsWith("Stopped"),"Faulted recorder declared stopped before ownership cleared");Set(failed,"Flags",0U);
  Set(failed,"Fault",4U);text=(string)T("CE32_console.NpV1BleProtocol").GetMethod("StateText",All).Invoke(null,new[]{failed});Need(text.Contains("SD write failed"),"SD write fault not distinguished");
  Console.WriteLine("PASS "+checks+" actual-app BLE settings/capability/fault checks; no hardware.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
