using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class NpDownloadSourceTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    const string Uid="001A0020434250062039314E";
    static Assembly app;static Type download;static int checks;
    static string PathFor(string serial,string guid="one"){return @"\\?\usb#vid_1209&pid_b386#"+serial+"#{"+guid+"}";}
    static object Get(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static object Call(object value,string name,params object[] args){try{return(value is Type?(Type)value:value.GetType()).GetMethod(name,All).Invoke(value is Type?null:value,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static bool Same(string a,string b){return(bool)Call(download,"SameSource",a,b);}
    static Form FormFor(string path){return(Form)Activator.CreateInstance(download,All,null,new object[]{path,false},null);}
    static void Poll(Form form,params string[] paths){Call(form,"PollUsbPaths",(object)paths);}
    static ComboBox Sources(Form form){return(ComboBox)Get(form,"sources");}
    [STAThread]static int Main(string[] args){try{
        app=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));download=app.GetType("CE32_console.NpDownloadForm",true);
        string bare=PathFor(Uid),modern=PathFor("npv2-"+Uid,"two"),neighbor=PathFor("npv2-001B0019434250062039314E");
        Need(Same(bare,modern),"V2 prefix/GUID change lost exact UID");Need(Same(bare,bare.ToLowerInvariant()),"Case changed source");
        Need(!Same(bare,neighbor),"Neighbor matched");Need(!Same(modern,PathFor("npv1-"+Uid)),"V1/V2 prefixes conflated");
        Need(!Same(null,null)&&!Same(bare,null),"Missing identity matched");
        Need(!Same(bare,bare.Replace("b386","b387")),"Recovery interface matched download service");
        using(var form=FormFor(bare)){
            var sources=Sources(form);Poll(form,bare,neighbor);Need(sources.SelectedItem!=null&&(string)Get(sources.SelectedItem,"Path")==bare,"Bound source not selected");
            object item=sources.SelectedItem;Poll(form,modern,neighbor);Need(sources.SelectedItem==item,"Re-enumeration lost selected object");
            Need((string)Get(item,"Path")==modern,"Selected path was not refreshed");Need((string)Get(form,"boundSourcePath")==modern,"Binding not refreshed");
            Poll(form,modern.ToLowerInvariant(),neighbor,modern);Need(sources.Items.Count==2&&sources.SelectedItem==item,"Case duplicate created ambiguous source");
            var records=(CheckedListBox)Get(form,"records");records.Items.Add("stale recording");download.GetField("listedSource",All).SetValue(form,item);((ProgressBar)Get(form,"progress")).Value=100;
            Poll(form,neighbor);Need(sources.SelectedIndex==-1,"Missing target replaced by neighbor");Need(records.Items.Count==0&&Get(form,"listedSource")==null,"Absent device retained stale recordings");
            Need(((ProgressBar)Get(form,"progress")).Value==0,"Absent device retained completion bar");Need(!((Button)Get(form,"format")).Enabled&&!((Button)Get(form,"list")).Enabled&&!((Button)Get(form,"save")).Enabled,"Absent-source actions enabled");
            Need(((Label)Get(form,"status")).Text.Contains("absent"),"Missing-device explanation absent");
            Poll(form,modern,neighbor);Need(sources.SelectedItem!=null&&(string)Get(sources.SelectedItem,"Path")==modern,"Same UID did not reconnect");
            Poll(form,modern,PathFor("npv2-"+Uid,"duplicate"),neighbor);Need(sources.SelectedIndex==-1,"Ambiguous same UID auto-selected");Need(!((Button)Get(form,"format")).Enabled,"Ambiguous source can format");
            Poll(form,modern,neighbor);Need(sources.SelectedItem!=null&&(string)Get(sources.SelectedItem,"Path")==modern,"Unique UID not restored");
            ((Button)Get(form,"list")).Enabled=false;Call(form,"SetUsbBlocked",true);Need(!((Button)Get(form,"format")).Enabled&&!((Button)Get(form,"list")).Enabled,"BLE/capture owner not respected");
        }
        using(var form=FormFor(null)){Poll(form,modern);Need(Sources(form).SelectedIndex==0,"Single initial source not selected");}
        using(var form=FormFor(null)){Poll(form,modern,neighbor);Need(Sources(form).SelectedIndex==-1,"Multiple initial sources auto-selected");}
        Console.WriteLine("PASS "+checks+" actual-app download source/reconnection/ambiguity checks; no hardware access.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
