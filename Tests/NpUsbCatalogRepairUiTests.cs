using System;
using System.Reflection;
using System.Windows.Forms;

static class NpUsbCatalogRepairUiTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Get(object o,string n){return o.GetType().GetField(n,Flags).GetValue(o);}
    static void Set(object o,string n,object v){o.GetType().GetField(n,Flags).SetValue(o,v);}
    [STAThread] static int Main(string[] args){try{
        var app=Assembly.LoadFrom(args[0]);var type=app.GetType("CE32_console.NpDownloadForm",true);
        using(var form=(Form)Activator.CreateInstance(type,Flags,null,new object[]{null,false},null)){
            var sources=(ComboBox)Get(form,"sources");var repair=(Button)Get(form,"repairCatalog");var recover=(Button)Get(form,"recover");var format=(Button)Get(form,"format");
            Need(repair.Name=="repairUsbCatalog" && repair.Text=="Repair SD catalog…","Real repair button missing");
            Need(repair.Parent==format.Parent && repair.Parent!=recover.Parent,"Repair is not in existing card actions, separate from read-only recovery");
            var sourceType=type.GetNestedType("Source",BindingFlags.NonPublic);
            Action<string,bool,string> choose=(path,usb,media)=>{var source=Activator.CreateInstance(sourceType,true);Set(source,"Path",path);Set(source,"Usb",usb);Set(source,"Label",path);Set(source,"MediaType",media);sources.Items.Add(source);sources.SelectedItem=source;};
            choose(@"\\?\usb#vid_1209&pid_b386#001a0020434250062039314e#{test}",true,null);
            Need(repair.Enabled && !recover.Enabled && format.Enabled,"V2 repair/format missing or confused with V1 recovery");
            type.GetMethod("SetUsbBlocked",Flags).Invoke(form,new object[]{true});Need(!repair.Enabled,"Capture/BLE/USB owner did not block Repair");
            type.GetMethod("SetUsbBlocked",Flags).Invoke(form,new object[]{false});Need(repair.Enabled,"Repair stayed disabled after ownership released");
            type.GetMethod("SetBusy",Flags).Invoke(form,new object[]{true});Need(!repair.Enabled,"Download/maintenance did not block Repair");
            type.GetMethod("SetBusy",Flags).Invoke(form,new object[]{false});
            choose(@"\\?\usb#vid_1209&pid_b386#npv1-001e002a5942500b20313354#{test}",true,null);Need(repair.Enabled && recover.Enabled && format.Enabled,"V1 shared repair controls missing or recovery/format regressed");
            type.GetMethod("SetUsbBlocked",Flags).Invoke(form,new object[]{true});Need(!repair.Enabled,"V1 repair bypassed USB ownership");
            type.GetMethod("SetUsbBlocked",Flags).Invoke(form,new object[]{false});Need(repair.Enabled,"V1 repair stayed blocked");
            choose(@"\\.\PhysicalDrive99",false,"Removable Media");Need(!repair.Enabled && recover.Enabled,"SD reader admitted to USB repair");
            Need(type.GetMethod("RepairUsbCatalogAsync",Flags)!=null,"Shared USB Mode entry point missing");
        }
        Console.WriteLine("PASS "+checks+" actual compiled V1/V2 repair/recovery UI checks; no discovery, clicks or hardware.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
