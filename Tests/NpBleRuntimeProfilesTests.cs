using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

// Exercise only the built form's profile-population method. No constructor,
// window, watcher, connection, command, or hardware is created.
internal static class NpBleRuntimeProfilesTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Set(object instance,string name,object value){instance.GetType().GetField(name,All).SetValue(instance,value);}
    static uint Read(object instance,string name){return (uint)instance.GetType().GetField(name,All).GetValue(instance);}
    [STAThread]
    static int Main(string[] args)
    {
        try{
            if(args.Length!=2)throw new ArgumentException("<checked-app.exe> <NpBleForm.cs>");
            string source=File.ReadAllText(args[1]);
            Need(source.Contains("NpV2SdControl.Request(1, NpV2SdControl.NextId(s), p.Hz, 0, 0, p.Mask)"),"Apply drops the selected mask.");
            Need(source.Contains("NpV2SdControl.Request(2, NpV2SdControl.NextId(s), p.Hz, blocks, 0, p.Mask)"),"Start drops the selected mask.");
            Need(source.Contains("availableMask:(int)np2AvailableMask") && source.Contains("recorderVersion=0;np2AvailableMask=0;"),"IMRO capability or reconnect reset is stale.");
            Assembly app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type type=app.GetType("USB512_interface.NpBleForm",true);
            object form=FormatterServices.GetUninitializedObject(type);
            using(var profiles=new ComboBox()){
                Set(form,"profiles",profiles);Set(form,"recorderVersion",2);Set(form,"np2AvailableMask",3U);
                MethodInfo populate=type.GetMethod("Profiles",All);
                populate.Invoke(form,new object[]{null});
                Need(profiles.Items.Count==2,"Legacy device gained triple SD choices.");
                for(int i=0;i<2;i++)Need(Read(profiles.Items[i],"Mask")==3U && Read(profiles.Items[i],"Hz")== (i==0?20000U:30000U),"Legacy choices changed.");
                Set(form,"np2AvailableMask",7U);populate.Invoke(form,new object[]{null});
                Need(profiles.Items.Count==4,"Runtime device lacks four closed profiles.");
                Need(Read(profiles.Items[2],"Mask")==7U && Read(profiles.Items[2],"Hz")==15000U,"Triple15 missing.");
                Need(Read(profiles.Items[3],"Mask")==7U && Read(profiles.Items[3],"Hz")==20000U,"Triple20 missing.");
                profiles.SelectedIndex=3;populate.Invoke(form,new object[]{null});
                Need(profiles.SelectedIndex==3,"Status poll changed a selected triple profile.");
                Set(form,"np2AvailableMask",3U);populate.Invoke(form,new object[]{null});
                Need(profiles.Items.Count==2 && Read(profiles.SelectedItem,"Mask")==3U,"Capability downgrade retained triple choice.");
                Set(form,"recorderVersion",0);populate.Invoke(form,new object[]{null});
                Need(profiles.Items.Count==0,"Disconnected profile menu is not empty.");
            }
            Console.WriteLine("PASS "+checks+" actual built BLE profile-menu/routing checks; no radio or recording qualification.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
