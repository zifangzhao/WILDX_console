using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class NpBleDiscoveryTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Get(object target,string name){return target.GetType().GetField(name,All).GetValue(target);}
    static void Observe(object form,ulong value,string name,short rssi)
    {
        var method=form.GetType().GetMethod("ObservePeer",All);
        var type=Enum.Parse(method.GetParameters()[1].ParameterType,"Public");
        method.Invoke(form,new object[]{value,type,name,rssi});
    }
    static void Run(Assembly app)
    {
        var type=app.GetType("USB512_interface.NpBleForm",true);
        using(var form=(Form)Activator.CreateInstance(type,true)){
            var devices=(ComboBox)Get(form,"devices");var address=(TextBox)Get(form,"address");
            Observe(form,0xAABBCCDDEEFF,"WILD-X V1",-45);
            Need(devices.Items.Count==1&&devices.SelectedIndex==0,"First peer was not selected");
            Need(address.Text=="AABBCCDDEEFF","Default address did not follow first peer");
            Observe(form,0x112233445566,"WILD-X V2",-50);
            Need(devices.SelectedIndex==0&&address.Text=="AABBCCDDEEFF","Second advertisement changed target");
            devices.SelectedIndex=1;Need(address.Text=="112233445566","Explicit combo selection did not update address");
            address.Text="12:34:56:78:9A:BC";
            Observe(form,0x112233445566,"WILD-X V2",-31);
            Need(address.Text=="12:34:56:78:9A:BC","RSSI refresh replaced manually typed address");
            Need(devices.SelectedIndex==1&&devices.Items.Count==2,"Refresh lost selection or duplicated peer");
            Need(devices.SelectedItem.ToString().Contains("-31 dBm"),"RSSI was not refreshed");
            Observe(form,0xAABBCCDDEEFF,"WILD-X V1",-30);
            Need(address.Text=="12:34:56:78:9A:BC","Other peer refresh replaced typed address");
            address.Text="123";Observe(form,0x112233445566,null,-29);
            Need(address.Text=="123","Advertisement replaced partial manual input");
            devices.SelectedIndex=0;Need(address.Text=="AABBCCDDEEFF","Explicit selection did not replace manual address");
            address.Clear();Observe(form,0x112233445566,null,-28);
            Need(address.Text=="AABBCCDDEEFF","Blank address did not use current combo item");
            Need(devices.Items[1].ToString().Contains("WILD-X V2"),"Unnamed advertisement erased device name");
            type.GetField("busy",All).SetValue(form,true);Observe(form,1,"ignored",-1);
            Need(devices.Items.Count==2,"Busy form accepted new peer");
            Need((bool)type.GetProperty("SessionOwned",All).GetValue(form,null),"Active command lost ownership");
            type.GetField("busy",All).SetValue(form,false);
            Need(!(bool)type.GetProperty("SessionOwned",All).GetValue(form,null),"Discovery-only form owns USB session");
            form.Dispose();Observe(form,2,"closed",-2);
            Need(devices.Items.Count==2,"Closed form accepted advertisement");
        }
        using(var form=(Form)Activator.CreateInstance(type,true)){
            var address=(TextBox)Get(form,"address");address.Text="DEADBEEF0001";
            Observe(form,0xAABBCCDDEEFF,"WILD-X",-40);
            Need(address.Text=="DEADBEEF0001","First discovery replaced address entered before scan result");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try{Run(Assembly.LoadFrom(Path.GetFullPath(args[0])));Console.WriteLine("PASS "+checks+" actual-app BLE discovery/selection checks; no radio started or device connected.");return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
