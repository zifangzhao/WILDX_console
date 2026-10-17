using System;
using System.IO;
using System.Reflection;
using CE32_console;

// Compiles canonical pure protocol sources; never opens the app or a device.
internal static class NpBlePreviewProfilesTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Profiles(){
        for(uint mask=0;mask<=8;mask++)foreach(uint hz in new uint[]{0,7500,10000,15000,20000,30000,40000}){
            bool valid=(mask==1 || mask==3 || mask==7) && (hz==7500 || hz==15000 || hz==20000 || hz==30000);
            var status=new NpSdStatus{Mask=mask,Hz=hz,State=3,Flags=128};
            Need(NpV2SdControl.CanPreview(status)==valid,"Incorrect recording profile admission: "+mask+"/"+hz);
            status.Flags=0;Need(!NpV2SdControl.CanPreview(status),"Preview admitted without device capability");
            status.Flags=128;foreach(uint state in new uint[]{0,1,2,4,5,6,7,255}){status.State=state;Need(!NpV2SdControl.CanPreview(status),"Non-recording state admitted");}
        }
        Need(!NpV2SdControl.CanPreview(null),"Null recording status admitted");
        var preview=typeof(BleFeaturesTests).GetMethod("Preview",All);
        var values=new object[preview.GetParameters().Length];for(int i=0;i<values.Length;i++)values[i]=preview.GetParameters()[i].DefaultValue;
        values[4]=7500U;Need(new NpV2PreviewBurst(91).Accept((byte[])preview.Invoke(null,values)),"7.5-kHz P1/P2 preview metadata rejected");
        values[4]=10000U;bool refused=false;try{new NpV2PreviewBurst(91).Accept((byte[])preview.Invoke(null,values));}catch(IOException){refused=true;}
        Need(refused,"Unsupported preview metadata rate admitted");
    }
    static void FrozenRegression(string path){
        var app=Assembly.LoadFrom(Path.GetFullPath(path));var type=app.GetType("CE32_console.NpV2PreviewBurst",true);
        var burst=Activator.CreateInstance(type,All,null,new object[]{91U},null);
        var make=typeof(BleFeaturesTests).GetMethod("Preview",All);
        var parameters=make.GetParameters();var values=new object[parameters.Length];for(int i=0;i<values.Length;i++)values[i]=parameters[i].DefaultValue;
        values[4]=15000U;byte[] packet=(byte[])make.Invoke(null,values);bool rejected=false;
        try{type.GetMethod("Accept",All).Invoke(burst,new object[]{packet});}catch(TargetInvocationException ex){rejected=ex.InnerException is IOException && ex.InnerException.Message=="Invalid preview metadata";}
        Need(rejected,"Frozen baseline did not reproduce the known 15-kHz metadata rejection");
        Console.WriteLine("Frozen baseline reproduces Invalid preview metadata for an otherwise valid 15-kHz P1 packet; no inference about uncaptured hardware fields.");
    }
    static int Main(string[] args){try{
        if(args.Length==1)FrozenRegression(args[0]);Profiles();
        // Reuse the maintained ADC/phase/metadata checks against this source fixture.
        typeof(BleFeaturesTests).GetField("app",All).SetValue(null,Assembly.GetExecutingAssembly());
        typeof(BleFeaturesTests).GetMethod("PreviewDecode",All).Invoke(null,null);
        int protocol=(int)typeof(BleFeaturesTests).GetField("checks",All).GetValue(null);
        Console.WriteLine("PASS "+checks+" profile/baseline and "+protocol+" preview ADC/phase/metadata checks: P1/P2 7.5/15/20/30, including mask7 acquisition; P3 remains unavailable. No app/dependency build or hardware.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex is TargetInvocationException?ex.InnerException:ex);return 1;}}
}
