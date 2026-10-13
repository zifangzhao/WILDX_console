using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class NpUniversalWorkflowTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static int checks;static string output;
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Get(object target,string name){return target.GetType().GetField(name,All).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,All).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,All).Invoke(target,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    static object Prop(object target,string name){return target.GetType().GetProperty(name,All).GetValue(target,null);}
    static Func<TValue> Read<TValue>(object value){return ()=>{if(value is Exception)throw (Exception)value;return (TValue)value;};}
    static Delegate Reader(object value){return (Delegate)typeof(NpUniversalWorkflowTests).GetMethod("Read",All).MakeGenericMethod(T("NpUsbRecordingProfile")).Invoke(null,new[]{value});}
    static IEnumerable<Control> Children(Control owner){foreach(Control child in owner.Controls){yield return child;foreach(var next in Children(child))yield return next;}}
    static void Click(Control button){typeof(Button).GetMethod("OnClick",All).Invoke(button,new object[]{EventArgs.Empty});}
    static void Render(Form form,string name)
    {
        // Reparent into an offscreen host so device-owning Shown/Load events do
        // not run. The supplied form never starts discovery or opens hardware.
        using(var host=new Form {Text=form.Text,ClientSize=form.ClientSize,Font=form.Font,BackColor=form.BackColor,ForeColor=form.ForeColor,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
            foreach(Control control in form.Controls.Cast<Control>().ToArray())host.Controls.Add(control);
            host.Show();Application.DoEvents();
            foreach(var button in Children(host).OfType<Button>().Where(b=>b.Visible))Check(button.Bottom<=button.Parent.ClientSize.Height,"Clipped button: "+button.Text);
            using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,name+".png"),ImageFormat.Png);}
        }
    }
    static void Recording(bool np2)
    {
        object profile=Activator.CreateInstance(T("NpUsbRecordingProfile"),true);
        Set(profile,"AvailableMask",np2?3:7);Set(profile,"SelectedMask",3);Set(profile,"SelectedHz",20000);Set(profile,"Rates",np2?new[]{15000,20000,30000}:new[]{7500,15000,20000,30000});
        Set(profile,"Status",np2?"Neuropixels V2 · 384 AP channels per probe":"Neuropixels V1 · 384 AP + 384 LFP channels per probe");
        Set(profile,"Validate",(Func<int,int,string>)((mask,hz)=>mask==0 || np2 && mask!=1 && mask!=2 && mask!=3?"Unsupported connectors":null));
        Set(profile,"Warning",(Func<int,int,string>)((mask,hz)=>null));
        using(var form=(Form)Activator.CreateInstance(T("NpRecordingDialog"),All,null,new object[]{np2,0,20000,true,true,true,Reader(profile),(Action<IWin32Window>)(owner=>{})},null)){
            Call(form,"ReadProfile");Check(form.Text=="Recording settings","Generation-specific recording screen");
            Check((int)Prop(form,"ProbeMask")==0 && (int)Prop(form,"SampleHz")==20000,"Device selection/rate lost");
            var ports=(CheckBox[])Get(form,"ports");var automatic=(CheckBox)Get(form,"automatic");automatic.Checked=false;
            Check(ports[0].Enabled && ports[1].Enabled && ports[2].Enabled==!np2,"Unavailable connector admitted");
            ports[0].Checked=ports[1].Checked=false;Check(!((Button)Get(form,"accept")).Enabled,"Empty profile admitted");
            ports[0].Checked=true;Check((int)Prop(form,"ProbeMask")==1 && !((CheckBox)Get(form,"merge")).Enabled,"Single-probe merge enabled");
            Check((bool)typeof(Control).GetMethod("GetState",All).Invoke(Get(form,"upsample"),new object[]{2})==!np2,"Unsupported NP2 resampling shown");
            ((CheckBox)Get(form,"misc")).Checked=false;Check(!(bool)Prop(form,"Misc"),"Misc setting ignored");
            Click((Button)Get(form,"accept"));Check(form.DialogResult==DialogResult.OK,"Valid profile not retained");
            Render(form,np2?"recording-v2":"recording-v1");
        }
        using(var failed=(Form)Activator.CreateInstance(T("NpRecordingDialog"),All,null,new object[]{np2,0,20000,true,true,true,Reader(new IOException("status failed")),(Action<IWin32Window>)(owner=>{})},null)){
            Call(failed,"ReadProfile");Check(!((Button)Get(failed,"accept")).Enabled,"Stale profile accepted after read failure");
            Check(((TextBox)Get(failed,"status")).Text.Contains("status failed"),"Status failure hidden");
        }
    }
    static Form Configuration(bool np2,IDictionary requests,ISet<int> apply)
    {
        return (Form)Activator.CreateInstance(T("NpConfigurationDialog"),All,null,new object[]{null,null,np2,3,np2?requests:null,np2?apply:null,np2?null:requests,null},null);
    }
    static void Configuration()
    {
        var maps=new Dictionary<int,string>{{1,"map1"},{2,"map2"}};var apply=new HashSet<int>{1,2};
        using(var form=Configuration(true,maps,apply)){
            Check(form.Text=="Probe configuration","Separate probe configuration title");
            Check(((ComboBox)Get(form,"connector")).Items.Count==2,"Unadvertised connector shown");
            Click(Children(form).OfType<Button>().Single(b=>b.Text=="Clear request"));
            Check(maps.Count==2 && apply.Count==2,"Cancel leaked staged edits to host requests");
            Render(form,"probe-configuration-v2");
        }
        using(var form=Configuration(true,maps,apply)){
            Click(Children(form).OfType<Button>().Single(b=>b.Text=="Clear request"));
            Click(Children(form).OfType<Button>().Single(b=>b.Text=="Save settings"));
            Check(!maps.ContainsKey(1) && maps[2]=="map2" && apply.SetEquals(new[]{2}),"Save discarded another connector's map");
        }
        Type request=T("Form1").Assembly.GetType("WildNpConfiguration.NpConfigRequest",true);
        Type dictionary=typeof(Dictionary<,>).MakeGenericType(typeof(byte),request);var v1=(IDictionary)Activator.CreateInstance(dictionary);
        var selection=new byte[1576];selection[0]=78;selection[1]=80;selection[2]=66;selection[3]=49;selection[4]=1;
        v1.Add((byte)0,Activator.CreateInstance(request,new object[]{"{}",selection}));
        using(var form=Configuration(false,v1,null)){
            Check(form.Text=="Probe configuration","V1 did not use common probe editor");
            Check(((CheckBox)Get(form,"applyOnCapture")).Checked && !((CheckBox)Get(form,"applyOnCapture")).Enabled,"V1 mandatory restaging lost");
            Click(Children(form).OfType<Button>().Single(b=>b.Text=="Clear request"));Check(v1.Count==1,"V1 Cancel removed retained request");
            Render(form,"probe-configuration-v1");
        }
        foreach(bool np2 in new[]{false,true})using(var diagnostics=(Form)Activator.CreateInstance(T("NpDiagnosticsForm"),All,null,new object[]{"offline",np2},null)){
            Check(Children(diagnostics).OfType<Button>().Single(b=>b.Text=="Run + hold self-test").Enabled==np2,"Unsupported self-test admitted");
            if(!np2){bool rejected=false;try{((Task)Call(diagnostics,"Test",new object[]{null})).GetAwaiter().GetResult();}catch(IOException){rejected=true;}Check(rejected,"V1 sent V2 self-test command");}
            Render(diagnostics,np2?"diagnostics-v2":"diagnostics-v1");
        }
    }
    static void ReportsAndOwnership()
    {
        string folder=Path.Combine(output,"report-source");Directory.CreateDirectory(folder);
        foreach(string name in new[]{"neuropixels_capture_status.txt","neuropixels_np2usb_status.txt"}){
            File.WriteAllText(Path.Combine(folder,name),"native_fault=17; saved rows incomplete");
            var report=(string)T("NpReportForm").GetMethod("CaptureText",All).Invoke(null,new object[]{"NP","selected-uid","Capture error: probe stage failed",folder});
            Check(report.Contains(name) && report.Contains("native_fault=17") && report.Contains("probe stage failed"),"Native failure evidence omitted");
        }
        using(var form=(Form)Activator.CreateInstance(T("Form1"),true)){
            var kind=form.GetType().GetField("selectedDeviceKind",All).FieldType;Set(form,"selectedDeviceKind",Enum.Parse(kind,"NpV2"));Set(form,"devicePath","offline");
            Call(form,"UpdateStartEnabled");Check(((Button)Get(form,"probeConfigurationButton")).Enabled,"Idle probe controls unavailable");
            Set(form,"running",true);Call(form,"UpdateStartEnabled");
            Check(!((Button)Get(form,"probeConfigurationButton")).Enabled && !((Button)Get(form,"diagnosticsButton")).Enabled,"Maintenance admitted during recording");
        }
    }
    static void Imports(string root)
    {
        string imro=Path.Combine(output,"np1.imro");var rows=new System.Text.StringBuilder("(0,384)\r\n");
        for(int channel=0;channel<384;channel++)rows.Append("("+channel+" 0 0 1000 50 1)\r\n");File.WriteAllText(imro,rows.ToString());
        foreach(bool np2 in new[]{false,true}){
            Type request=app.GetType("WildNpConfiguration.NpConfigRequest",true);
            IDictionary maps=np2?(IDictionary)new Dictionary<int,string>():(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(byte),request));
            var apply=new HashSet<int>();using(var form=Configuration(np2,maps,apply)){
                ((ComboBox)Get(form,"connector")).SelectedItem=2;
                var task=(Task)Call(form,"ImportFile",np2?Path.Combine(root,"ProbeConfig","probe_3.imro"):imro);
                var deadline=Stopwatch.StartNew();while(!task.IsCompleted && deadline.Elapsed.TotalSeconds<30){Application.DoEvents();System.Threading.Thread.Sleep(10);}
                Check(task.IsCompleted,"Import blocked the UI context");task.GetAwaiter().GetResult();
                Check(maps.Count==0,"Import applied host settings before Save");
                Click(Children(form).OfType<Button>().Single(b=>b.Text=="Save settings"));
                Check(maps.Count==1,"Compiled request not saved");
                if(np2){Check(maps.Contains(2) && apply.SetEquals(new[]{2}),"NP2 IMRO filename overrode selected connector");
                    var prepared=T("NpConfigurationDialog").GetMethod("PrepareV2Request",All).Invoke(null,new[]{maps[2],(object)2});Check((byte)Prop(prepared,"Port")==1,"NP2 prepared wrong connector");}
                else {Check(maps.Contains((byte)1),"NP1 did not retain selected connector");Check((byte)Prop(maps[(byte)1],"Port")==1,"NP1 prepared wrong connector");}
            }
        }
    }
    static void Receipts()
    {
        foreach(bool np2 in new[]{false,true}){
            Type request=np2?T("Np2ConfigRequest"):app.GetType("WildNpConfiguration.NpConfigRequest",true);
            Type reply=np2?T("Np2ConfigReply"):app.GetType("WildNpConfiguration.NpConfigReply",true);
            Type client=np2?T("Np2ConfigClient"):app.GetType("WildNpConfiguration.NpConfigClient",true);
            var payload=new byte[np2?876:1576];payload[0]=78;payload[1]=np2?(byte)50:(byte)80;payload[2]=66;payload[3]=49;payload[4]=np2?(byte)4:(byte)2;
            for(int i=0;i<32;i++)payload[8+i]=(byte)(i+1);
            object req=Activator.CreateInstance(request,All,null,new object[]{"{}",payload},null),receipt=Activator.CreateInstance(reply,true);
            var id=new byte[32];Buffer.BlockCopy(payload,8,id,0,32);Set(receipt,"ConfigurationId",id);Set(receipt,"PortState",(byte)3);Set(receipt,"Verification",np2?(byte)1:(byte)3);
            uint crc=(uint)client.GetMethod("Crc",All).Invoke(null,new object[]{payload,payload.Length});Set(receipt,"PayloadCrc",crc);
            if(np2)Set(receipt,"Model",(byte)4);
            var format=T("NpConfigurationDialog").GetMethod("Receipt",All,null,new[]{reply,request},null);
            string text=(string)format.Invoke(null,new[]{receipt,req});Check(text.Contains("Matches the applied host request"),"Valid receipt not confirmed");
            foreach(string invalid in new[]{"crc","verification","id"}){
                Set(receipt,"PayloadCrc",invalid=="crc"?crc^1:crc);Set(receipt,"Verification",invalid=="verification"?(byte)0:np2?(byte)1:(byte)3);id[0]=invalid=="id"?(byte)99:(byte)1;
                bool rejected=false;try{format.Invoke(null,new[]{receipt,req});}catch(TargetInvocationException ex){rejected=ex.InnerException is IOException;}
                Check(rejected,"Mismatched "+invalid+" receipt claimed application");
            }
        }
    }
    [STAThread]static int Main(string[] args)
    {
        try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Application.EnableVisualStyles();Recording(false);Recording(true);Configuration();ReportsAndOwnership();Imports(args[2]);Receipts();Console.WriteLine("PASS "+checks+" universal workflow checks; no discovery, USB/BLE traffic or hardware commands.");return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
