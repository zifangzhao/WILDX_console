using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

internal static class NpRecordingBundleTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static Type settings;static string root;static int checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static object Call(object target,string method,params object[] args){try{return (target as Type??target.GetType()).GetMethod(method,All).Invoke(target is Type?null:target,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    static Dictionary<string,object> Read(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
    static bool Equal(string a,string b){return File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));}
    static string Folder(string name){string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);return folder;}
    static byte[] Selection(bool np2,int port){var b=new byte[np2?876:1576];Encoding.ASCII.GetBytes(np2?"N2B1":"NPB1").CopyTo(b,0);b[4]=np2?(byte)4:(byte)2;b[5]=(byte)(port-1);for(int n=0;n<32;n++)b[8+n]=(byte)(port+n);return b;}
    static void Bundles(bool np2,bool merged)
    {
        string folder=Folder((np2?"v2":"v1")+(merged?"-merged":"-separate"));
        var metadata=new Dictionary<string,string>{{"clock_observations.csv","clock fixture"},{"clock_fit.json","{}"},{"capture_status.txt","incomplete fixture"},{"requested-configuration-applied-receipts.txt","receipt fixture"},{"adc_calibration.json","synthetic copy fixture"}};
        foreach(var item in metadata)File.WriteAllText(Path.Combine(folder,item.Key),item.Value);
        Directory.CreateDirectory(Path.Combine(folder,"calibration"));File.WriteAllText(Path.Combine(folder,"calibration","probe1_gain.csv"),"exact calibration fixture\r\n");
        for(int p=1;p<=3;p++){string child=Path.Combine(folder,"probe"+p);Directory.CreateDirectory(child);File.WriteAllBytes(Path.Combine(child,"neuropixels_ap.dat"),new byte[768]);}
        File.WriteAllBytes(Path.Combine(folder,"neuropixels.raw"),new byte[]{1,2,3});File.WriteAllBytes(Path.Combine(folder,"analogin.dat"),new byte[32]);
        if(merged)File.WriteAllBytes(Path.Combine(folder,"neuropixels_ap.dat"),new byte[2304]);
        object s=Activator.CreateInstance(settings,All,null,new object[]{folder,null,"live_usb",np2,DateTime.UtcNow,"peer","uid"},null);
        Call(s,"Actual",15000,15000,7,merged,true,"fixture");Call(s,"Request",1,"{}",Selection(np2,1));Call(s,"ConfirmRequest",1);
        Call(s,"Streams",true,true,false);Call(s,"Finished",false,"transport gap");Call(s,"Write");
        string suffix=np2?"n2b1":"npb1";
        Check(File.ReadAllBytes(Path.Combine(folder,"probe_1.requested-selection."+suffix)).SequenceEqual(Selection(np2,1)),"Requested binary not archived exactly");
        Check(Equal(Path.Combine(folder,"probe_1.requested-selection."+suffix),Path.Combine(folder,"probe_1.applied-selection."+suffix)),"Confirmed binary lost");
        for(int p=1;p<=3;p++){
            string child=Path.Combine(folder,"probe"+p);
            foreach(string name in metadata.Keys)Check(Equal(Path.Combine(folder,name),Path.Combine(child,name)),"Child missing shared metadata: "+name);
            Check(Equal(Path.Combine(folder,"calibration","probe1_gain.csv"),Path.Combine(child,"calibration","probe1_gain.csv")),"Child factory CSV changed");
            Check(File.Exists(Path.Combine(child,"probe_"+p+".json")),"Child probe file missing");
            Check(!File.Exists(Path.Combine(child,"neuropixels.raw")) && !File.Exists(Path.Combine(child,"analogin.dat")),"Bulk RAW/misc data duplicated");
            var local=Read(Path.Combine(child,"recording_settings.json"));Check(!(bool)local["recording_complete"] && (string)local["failure"]=="transport gap","Incomplete state lost");
            Check((int)((Dictionary<string,object>)local["output"])["channel_count_per_dat"]==384,"Merged channels leaked into child");
            var index=Read(Path.Combine(child,"recording_files.json"));var files=(IEnumerable)index["files"];
            Check(files.Cast<Dictionary<string,object>>().Any(f=>(string)f["path"]=="neuropixels_ap.dat"),"DAT omitted from bundle index");
            Check(files.Cast<Dictionary<string,object>>().All(f=>!Path.IsPathRooted((string)f["path"]) && !((string)f["path"]).StartsWith("../")),"Bundle uses external file paths");
        }
        Call(s,"Write");Check(File.Exists(Path.Combine(folder,"recording_files.json")),"Repeated metadata checkpoint failed");
        Call(s,"Streams",false,false,false);Call(s,"Write");Check(!(bool)((Dictionary<string,object>)Read(Path.Combine(folder,"recording_settings.json"))["misc_output"])["present"],"Disabled misc falsely advertised");
        string corrected=Path.Combine(root,"corrected-"+np2+merged+".dat");File.WriteAllBytes(corrected,new byte[768]);
        Call(settings,"CopySourceMetadata",Path.Combine(folder,"neuropixels.raw"),corrected);
        string provenance=corrected+".source-metadata";Check(Equal(Path.Combine(folder,"probe_1.requested-selection."+suffix),Path.Combine(provenance,"probe_1.requested-selection."+suffix)),"Corrected output lost source probe file");
        Check(!File.Exists(Path.Combine(provenance,"neuropixels.raw")),"Source provenance copied bulk data");
        bool refused=false;try{Call(settings,"CopySourceMetadata",Path.Combine(folder,"neuropixels.raw"),corrected);}catch(IOException){refused=true;}Check(refused,"Existing source provenance overwritten");
    }
    static uint Crc(byte[] b){uint c=~0U;foreach(byte v in b){c^=v;for(int n=0;n<8;n++)c=(c>>1)^((c&1)==0?0:0xedb88320U);}return ~c;}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void UnverifiedRequest()
    {
        foreach(byte verification in new byte[]{0x13,0xf3}){
            string folder=Folder("unverified-"+verification);
            object s=Activator.CreateInstance(settings,All,null,new object[]{folder,null,"live_usb",true,DateTime.UtcNow,"peer","uid"},null);
            Call(s,"Actual",15000,15000,1,false,false,"fixture");
            Call(s,"Request",1,"{}",Selection(true,1));Call(s,"ConfirmRequestVerification",1,verification);Call(s,"Write");
            var p=Read(Path.Combine(folder,"probe_1.json"));
            Check((string)p["configuration_state"]=="requested_applied_unverified","SR warning promoted to confirmed map");
            Check((int)p["configuration_verification"]==verification,"SR evidence lost");
            Check((string)p["selection_file"]==(string)p["requested_selection_file"],"Unverified request mislabelled applied");
            Check(File.ReadAllBytes(Path.Combine(folder,(string)p["selection_file"])).SequenceEqual(Selection(true,1)),"Unverified original request lost");
            Check(!File.Exists(Path.Combine(folder,"probe_1.applied-selection.n2b1")),"Confirmed binary invented after SR failure");
        }
    }
    static void SdExport(string fixtureExe)
    {
        var fixture=Assembly.LoadFile(Path.GetFullPath(fixtureExe)).GetType("NpV2SdDatExportTests",true).GetMethod("MakeImage",All);
        foreach(uint mask in new uint[]{3,7}){
            string image=Path.Combine(root,"sd"+mask+".img"),output=Path.Combine(root,"sd"+mask);uint hz=mask==3?30000U:20000U;
            fixture.Invoke(null,new object[]{image,hz,"",0U,mask,uint.MaxValue});
            byte[] configuration;
            using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){
                card.Position=(8192+8)*512L;configuration=new byte[2048];card.Read(configuration,0,configuration.Length);
                if(mask==3){
                    Encoding.ASCII.GetBytes("N2SDCFG1").CopyTo(configuration,0);Put(configuration,8,1);Put(configuration,12,2);Encoding.ASCII.GetBytes("N2IMRO1").CopyTo(configuration,64);configuration[72]=3;
                    var desc=new byte[512];card.Position=(8192+3)*512L;card.Read(desc,0,512);Buffer.BlockCopy(desc,16,configuration,16,28);Buffer.BlockCopy(desc,80,configuration,48,16);
                    Selection(true,1).CopyTo(configuration,128);Selection(true,2).CopyTo(configuration,1004);
                    Put(desc,76,Crc(configuration));Put(desc,508,Crc(desc.Take(508).ToArray()));card.Position=(8192+3)*512L;card.Write(desc,0,512);
                    var footer=new byte[512];card.Position=(8192+4)*512L;card.Read(footer,0,512);Put(footer,76,Crc(configuration));Put(footer,508,Crc(footer.Take(508).ToArray()));card.Position=(8192+4)*512L;card.Write(footer,0,512);
                    card.Position=(8192+8)*512L;card.Write(configuration,0,configuration.Length);
                }
            }
            using(var card=File.OpenRead(image))Call(app.GetType("CE32_console.NpCe64Export",true),"Export",card,8192L,16384L+8*91,output,(Func<bool>)(()=>false),null);
            var doc=Read(Path.Combine(output,"recording_settings.json"));Check((int)((Dictionary<string,object>)doc["actual"])["probe_mask"]==mask,"SD probe mask forced to dual");
            Check(File.ReadAllBytes(Path.Combine(output,"configuration.bin")).SequenceEqual(configuration),"SD configuration bytes changed");
            Check(File.Exists(Path.Combine(output,"CE_params.bin")) && File.Exists(Path.Combine(output,"descriptor.bin")) && File.Exists(Path.Combine(output,"footer.bin")) && File.Exists(Path.Combine(output,"recording_files.json")),"SD metadata not bundled");
            for(int p=1;p<=(mask==3?2:3);p++){
                var probe=Read(Path.Combine(output,"probe_"+p+".json"));Check((int)probe["probe"]==p,"SD probe identity lost");
                Check((string)probe["configuration_state"]==(mask==3?"stored_request_unverified":"stored_fingerprint_unverified"),"Stored request/fingerprint promoted to applied map");
                if(mask==3)Check(File.ReadAllBytes(Path.Combine(output,(string)probe["selection_file"])).SequenceEqual(Selection(true,p)),"Full SD request not retained");
                else Check(probe["imro_file"]==null && probe["channel_mapping"]==null,"Missing triple map invented");
            }
        }
    }
    static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));settings=app.GetType("USB512_interface.NpRecordingSettings",true);root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);foreach(bool np2 in new[]{false,true})foreach(bool merged in new[]{false,true})Bundles(np2,merged);UnverifiedRequest();SdExport(args[2]);Console.WriteLine("PASS "+checks+" actual-app recording bundle checks; files/images only, no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
