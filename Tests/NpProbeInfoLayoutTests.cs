using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;

static class NpProbeInfoLayoutTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static string root;static int checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object Field(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static void Set(object value,string name,object field){value.GetType().GetField(name,All).SetValue(value,field);}
    static object Call(object value,string name,params object[] args){try{return (value is Type?(Type)value:value.GetType()).GetMethod(name,All).Invoke(value is Type?null:value,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static Dictionary<string,object> Read(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
    static Dictionary<string,object>[] Probes(Dictionary<string,object> doc){return ((IEnumerable)doc["probes"]).Cast<Dictionary<string,object>>().ToArray();}
    static Dictionary<string,object> Identity(Dictionary<string,object> probe){return (Dictionary<string,object>)probe["identity"];}
    static object Settings(string name,bool v2,int mask,bool children=false,string source="live_usb")
    {
        string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
        object value=Activator.CreateInstance(T("NpRecordingSettings"),All,null,new object[]{folder,null,source,v2,null,null,null},null);
        Call(value,"Actual",20000,20000,mask,false,children,"synthetic descriptor");return value;
    }
    static void Metadata()
    {
        var settings=Settings("v2",true,7,true);var header=new byte[512];System.Text.Encoding.ASCII.GetBytes("NP2USB03").CopyTo(header,0);
        BitConverter.GetBytes(7U).CopyTo(header,76);BitConverter.GetBytes(7U).CopyTo(header,80);BitConverter.GetBytes(1U).CopyTo(header,52);
        for(int p=0;p<3;p++){BitConverter.GetBytes(100001UL+(ulong)p).CopyTo(header,96+p*8);System.Text.Encoding.ASCII.GetBytes("NP2013").CopyTo(header,120+p*8);}
        Call(T("NpProbeIdentity"),"V2Header",settings,header,"fixture verified native header");Call(settings,"Write");
        var probes=Probes(Read(Path.Combine(root,"v2/probe_info.json")));
        for(int p=0;p<3;p++){
            Need(Convert.ToInt32(probes[p]["physical_port"])==p+1,"Physical ports were compacted/sorted incorrectly");
            Need((string)Identity(probes[p])["serial"]==(100001+p).ToString(),"Wrong EEPROM LE64 serial");
            Need((bool)Identity(probes[p])["available"],"Valid identity unavailable");
            var child=Probes(Read(Path.Combine(root,"v2/probe"+(p+1)+"/probe_info.json")));
            Need(child.Length==1 && Convert.ToInt32(child[0]["physical_port"])==p+1,"Per-probe folder contains another probe's identity");
        }
        Need(Read(Path.Combine(root,"v2/recording_settings.json"))["probe_info_file"].Equals("probe_info.json"),"Shared identity file omitted from metadata");
        Need(File.ReadAllText(Path.Combine(root,"v2/recording_files.json")).Contains("probe_info.json"),"Probe info omitted from bundle index");
        foreach(bool generated in new[]{false,true}){
            var absent=Settings(generated?"generated":"absent",true,7);
            var h=(byte[])header.Clone();if(generated)BitConverter.GetBytes(5U).CopyTo(h,52);else{Array.Clear(h,96,8);for(int n=0;n<8;n++)h[104+n]=255;BitConverter.GetBytes(3U).CopyTo(h,80);}
            Call(T("NpProbeIdentity"),"V2Header",absent,h,"fixture");Call(absent,"Write");
            foreach(var p in Probes(Read(Path.Combine(root,generated?"generated":"absent","probe_info.json"))))Need(!(bool)Identity(p)["available"] && Identity(p)["serial"]==null,"Missing/generated/non-live serial was fabricated");
        }
        foreach(string magic in new[]{"NP2USB01","NP2USB02"}){
            var legacy=Settings(magic,true,2);var h=new byte[512];System.Text.Encoding.ASCII.GetBytes(magic).CopyTo(h,0);BitConverter.GetBytes(99999UL).CopyTo(h,80);System.Text.Encoding.ASCII.GetBytes("NP2013").CopyTo(h,88);
            Call(T("NpProbeIdentity"),"V2Header",legacy,h,"legacy fixture");Call(legacy,"Write");var p=Probes(Read(Path.Combine(root,magic,"probe_info.json")));
            Need(p.Length==1 && Convert.ToInt32(p[0]["physical_port"])==2 && (string)Identity(p[0])["serial"]=="99999","Legacy single identity was interpreted as a V3 array");
        }
        var v1=Settings("v1",false,3);var wire=new byte[1684];System.Text.Encoding.ASCII.GetBytes("NPM1").CopyTo(wire,0);wire[4]=1;
        BitConverter.GetBytes(2U).CopyTo(wire,8);BitConverter.GetBytes(1U).CopyTo(wire,16);BitConverter.GetBytes(1234567890123UL).CopyTo(wire,20);
        object receipt=Call(T("NpProbeManifest"),"Parse",wire);Call(v1,"Receipt",receipt);Call(v1,"Write");var vp=Probes(Read(Path.Combine(root,"v1/probe_info.json")));
        Need((string)Identity(vp[0])["serial"]=="1234567890123","V1 manifest serial not normalized");Need(Identity(vp[1])["serial"]==null,"Legacy unavailable V1 port was filled from another port");
        wire[16]=3;receipt=Call(T("NpProbeManifest"),"Parse",wire);Call(v1,"Receipt",receipt);Call(v1,"Write");Need(Identity(Probes(Read(Path.Combine(root,"v1/probe_info.json")))[0])["serial"]==null,"Absent-EEPROM fallback became a measured SN");
        var old=Settings("old_sd",false,5,false,"sd_export");Call(old,"Write");Need(Probes(Read(Path.Combine(root,"old_sd/probe_info.json"))).All(p=>Identity(p)["serial"]==null),"Old SD export requires a new firmware identity");
    }
    static object Status(bool v2,uint state,uint fault=0)
    {
        var type=app.GetType("CE32_console.NpSdStatus",true);var s=Activator.CreateInstance(type,true);
        foreach(var pair in new Dictionary<string,uint>{{"State",state},{"Fault",fault},{"Mask",3},{"Hz",20000},{"Flags",17},{"Start",8192},{"Capacity",100000},{"Chunks",128}})type.GetField(pair.Key,All).SetValue(s,pair.Value);
        return s;
    }
    static void Ble()
    {
        foreach(bool v2 in new[]{false,true})using(var form=(Form)Activator.CreateInstance(T("NpBleForm"),true)){
            ((Timer)Field(form,"timer")).Stop();Set(form,"recorderVersion",v2?2:1);object settings=Settings(v2?"ble_v2":"ble_v1",v2,3,false,"ble_sd");Set(form,"bleRecordingMetadata",settings);
            Call(form,"ObserveBleRecordingMetadata",Status(v2,v2?3U:1U));Call(form,"ObserveBleRecordingMetadata",Status(v2,v2?5U:3U));
            var doc=Read(Path.Combine(root,v2?"ble_v2":"ble_v1","recording_settings.json"));Need((bool)doc["recording_complete"],"BLE terminal metadata not saved");Need(Probes(doc).All(p=>Identity(p)["serial"]==null),"BLE configuration digest became an EEPROM serial");
            Set(form,"bleMetadataFinished",false);Call(form,"RetireBleRecordingMetadata");Need(Field(form,"bleRecordingMetadata")==null,"Disconnect retained a journal for the next device");
        }
    }
    static IEnumerable<Control> Children(Control c){foreach(Control child in c.Controls){yield return child;foreach(var descendant in Children(child))yield return descendant;}}
    static void NoDiscovery(Form form)
    {
        var events=(EventHandlerList)typeof(Component).GetProperty("Events",All).GetValue(form,null);
        foreach(string name in new[]{"EVENT_LOAD","EVENT_SHOWN"}){var field=typeof(Form).GetField(name,All);Need(field!=null,"Unknown Form event key "+name);object key=field.GetValue(null);if(events[key]!=null)events.RemoveHandler(key,events[key]);}
    }
    static void Layout()
    {
        using(var form=(Form)Activator.CreateInstance(T("Form1"),true)){
            NoDiscovery(form);foreach(Form child in Children(form).OfType<Form>())NoDiscovery(child);
            foreach(IComponent component in ((IContainer)Field(form,"components")).Components)if(component is Timer)((Timer)component).Stop();
            ((Timer)Field(Field(form,"bleWorkspace"),"timer")).Stop();
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.MinimumSize=Size.Empty;form.Show();Application.DoEvents();
            foreach(int width in new[]{1044,1180,1420}){
                form.ClientSize=new Size(width,920);Application.DoEvents();
                var folder=(Control)Field(form,"usbDestinationFolder");var device=(Control)Field(form,"comboDrives");
                Need(folder.Left==device.Left && folder.Width==device.Width,"Device/folder entry columns differ at "+width);
                Need(Math.Abs((folder.Top+folder.Height/2)-(folder.Parent.Height/2))<=1,"Folder text entry not vertically centered");
                foreach(string name in new[]{"usbAcquisitionActions","usbHealthCard","captureActivityCard"}){
                    var card=form.Controls.Find(name,true).Single();foreach(var c in Children(card).Where(c=>c is Button || c is CheckBox || c is Label))Need(c.Parent.ClientRectangle.Contains(c.Bounds),"Clipped control "+name+" / "+c.Text+" "+c.Bounds+" in "+c.Parent.ClientRectangle);
                }
                foreach(var led in Children(form).Where(c=>c.Name.StartsWith("gpioChannelLed")))Need(led.Text=="" && led.Height>=led.Font.Height,"GPIO indicator is still a clipped text glyph");
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"usb-layout-"+width+".png"));}
            }
            Set(form,"lastCaptureReport","fixture");((Label)Field(form,"lblSpeed")).Text="Waiting for USB recording to stop";
            ((Label)Field(form,"recordingSummary")).Text="Elapsed 123:45:56 | Recorded size 100.00 GiB | Disk free 500.00 GiB";
            ((Label)Field(form,"lblRecInfo")).Text="P1 + P2 + P3 · AP 30.00 kS/s\r\nData 60.00 MiB/s\r\nRecording interrupted; reconnecting to the same device.";
            form.ClientSize=new Size(1044,920);Application.DoEvents();var info=(Label)Field(form,"lblRecInfo");Need(info.Height>=info.GetPreferredSize(new Size(info.Width,0)).Height,"Wrapped capture status clipped");
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"usb-layout-wrapped.png"));}
            form.Scale(new SizeF(1.25F,1.25F));
            var fonts=Children(form).Select(control=>new{Control=control,Font=control.Font}).ToArray();
            foreach(var item in fonts)item.Control.Font=new Font(item.Font.FontFamily,item.Font.Size*1.25F,item.Font.Style);
            form.ClientSize=new Size(1420,1050);Application.DoEvents();
            foreach(string name in new[]{"usbAcquisitionActions","usbHealthCard","captureActivityCard"})foreach(var control in Children(form.Controls.Find(name,true).Single()).Where(c=>c is Button || c is CheckBox || c is Label))
                Need(control.Parent.ClientRectangle.Contains(control.Bounds),"Clipped enlarged control: "+control.Text);
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"usb-layout-enlarged.png"));}
        }
    }
    static void Sd(string fixtures)
    {
        var bridge=app.GetType("CE32_console.NpCe64Export",true).GetMethod("ExportCalibrated",All);
        foreach(string name in new[]{"healthy","gap","overflow"}){
            string output=Path.Combine(root,"sd_"+name);object result;
            using(var card=File.OpenRead(Path.Combine(fixtures,name+".img")))result=bridge.Invoke(null,new object[]{card,8192L,0L,output,(Func<bool>)(()=>false),null,null});
            var header=(byte[])result.GetType().GetProperty("IdentityHeader").GetValue(result,null);
            Need(header.Length==512 && File.ReadAllBytes(Path.Combine(output,"probe_identity_header.bin")).SequenceEqual(header),"SD receipt did not retain exactly the verified header");
            Need(File.Exists(Path.Combine(output,"probe_info.json")),"SD export omitted probe info");
            foreach(string file in Directory.GetFiles(output,"*.dat"))Need(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(fixtures,name+"_dat",Path.GetFileName(file)))),"Identity metadata changed neural samples");
            foreach(var probe in Probes(Read(Path.Combine(output,"probe_info.json")))){
                int p=Convert.ToInt32(probe["physical_port"])-1;ulong serial=BitConverter.ToUInt64(header,96+p*8);
                Need((serial==0 || serial==UInt64.MaxValue)?Identity(probe)["serial"]==null:(string)Identity(probe)["serial"]==serial.ToString(),"SD probe serial differs from historical header");
            }
        }
    }
    [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();app=Assembly.LoadFrom(Path.GetFullPath(args[0]));root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);Metadata();Ble();Layout();if(args.Length>2)Sd(args[2]);Console.WriteLine("PASS "+checks+" actual-app probe metadata, legacy fallback, BLE lifecycle, native SD export and layout checks; no hardware/discovery/network.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
