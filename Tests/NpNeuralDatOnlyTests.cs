using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class NpNeuralDatOnlyTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly current,baseline;static string root;static int checks;
    static void Need(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    static bool Raw(string path){string name=Path.GetFileName(path);return name=="ephys.bin"||name.EndsWith(".lane8",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".raw",StringComparison.OrdinalIgnoreCase);}
    static void NoRaw(string folder){if(Directory.Exists(folder))Need(!Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Any(Raw),"Neural raw file created: "+folder);}
    static Exception Export(Assembly app,NpV1PowerCutRecoveryTests.SparseCard card,string folder,bool recovery,int cancelMode,bool verify)
    {
        bool stop=false;Func<bool> cancel=()=>cancelMode==1?stop:cancelMode==2?card.DataBytes>=4096:false;
        var method=app.GetType("CE32_console.NpCe64Export",true).GetMethod(recovery?"ExportPowerInterruptedPrefix":"Export",All);
        try{method.Invoke(null,new object[]{card,8192L,card.ReservedEnd,folder,cancel,(Action<int>)(p=>{if(verify)NoRaw(folder);if(cancelMode==1)stop=true;})});return null;}
        catch(TargetInvocationException ex){return ex.InnerException;}
    }
    static Dictionary<string,string> Status(string folder){return File.ReadAllLines(Path.Combine(folder,"capture_status.txt")).Where(x=>x.Contains("=")).Select(x=>x.Split(new[]{'='},2)).ToDictionary(x=>x[0],x=>x[1]);}
    static void Case(string name,Func<NpV1PowerCutRecoveryTests.SparseCard> create,bool recovery=false,int cancelMode=0)
    {
        string old=Path.Combine(root,name+"_72"),folder=Path.Combine(root,name+"_73");
        var original=create();var card=create();
        var before=Export(baseline,original,old,recovery,cancelMode,false);
        var after=Export(current,card,folder,recovery,cancelMode,true);
        Need((after!=null)==(name=="strict_decode_error"||cancelMode!=0),"Unexpected case outcome: "+name+" "+after);
        Need((before==null?null:before.GetType().FullName)==(after==null?null:after.GetType().FullName),"Failure behavior changed: "+name+"\r\n72: "+before+"\r\n73: "+after);
        Need(original.DataBytes==card.DataBytes,"Recovery read boundary changed: "+name);NoRaw(folder);
        var files=Directory.GetFiles(old).Where(p=>!Raw(p)).Select(Path.GetFileName).OrderBy(x=>x).ToArray();
        Need(files.SequenceEqual(Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(x=>x)),"Decoded/metadata file set changed: "+name);
        foreach(string file in files){
            if(file.EndsWith(".json")||file=="capture_status.txt")continue;
            Need(File.ReadAllBytes(Path.Combine(old,file)).SequenceEqual(File.ReadAllBytes(Path.Combine(folder,file))),"Decoded/metadata bytes changed: "+name+"/"+file);
        }
        var prior=Status(old);var actual=Status(folder);
        foreach(var entry in prior)if(entry.Key!="prefix_limits")Need(actual.ContainsKey(entry.Key)&&actual[entry.Key]==entry.Value,"Status changed: "+name+"/"+entry.Key);
        Need(actual["raw_neural_saved"]=="False"&&actual["lane8_saved"]=="False","Output policy not recorded");
        if(name.StartsWith("complete_")||name.StartsWith("incomplete_"))Need(Directory.GetFiles(folder,"*_ap.dat").Any(p=>new FileInfo(p).Length>0),"Expected decoded AP samples: "+name);
    }
    static IEnumerable<Control> Children(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var nested in Children(c))yield return nested;}}
    static void Ui()
    {
        var formType=current.GetType("CE32_console.NpDownloadForm",true);
        using(var form=(Form)Activator.CreateInstance(formType,All,null,new object[]{null,false},null)){
            Need(!Children(form).Any(c=>c.Text=="NP1 fast archive…"),"Raw-only archive action remains in download page");
            Need(Children(form).Any(c=>c.Text=="MP4 + WAV"),"Camera conversion control changed");
            var record=Activator.CreateInstance(current.GetType("CE32_console.NpDownloadRecord",true),true);
            record.GetType().GetField("Chunks",All).SetValue(record,10U);
            record.GetType().GetField("FormatVersion",All).SetValue(record,6U);
            var size=formType.GetMethod("RequiredBytes",All);
            Need((long)size.Invoke(null,new[]{record})==10L*65536*2,"Neural output estimate still includes redundant raw copies");
            record.GetType().GetField("FormatVersion",All).SetValue(record,7U);
            Need((long)size.Invoke(null,new[]{record})==10L*65536*4,"Camera output estimate changed");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try{
            string appFolder=Path.GetDirectoryName(Path.GetFullPath(args[0]));
            AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{string file=Path.Combine(appFolder,new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
            current=Assembly.LoadFrom(Path.GetFullPath(args[0]));baseline=Assembly.LoadFile(Path.GetFullPath(args[1]));
            root=Path.GetFullPath(args[2]);if(Directory.Exists(root))throw new IOException("Fresh test output required");Directory.CreateDirectory(root);
            foreach(uint mask in new uint[]{1,3,7}){
                uint selected=mask;
                Case("complete_mask"+mask,()=>{var c=new NpV1PowerCutRecoveryTests.SparseCard(8,8,8192,selected,15000);c.FinalizeFooter(8);return c;});
                Case("incomplete_mask"+mask,()=>{var c=new NpV1PowerCutRecoveryTests.SparseCard(8,8,8192,selected,15000);c.IncompleteFooter(8);return c;});
            }
            Case("recovered_prefix",()=>new NpV1PowerCutRecoveryTests.SparseCard(2),true);
            Case("torn_frame_at_seam",()=>{var c=new NpV1PowerCutRecoveryTests.SparseCard(2);c.BadSync(0,1023);return c;},true);
            Case("strict_decode_error",()=>{var c=new NpV1PowerCutRecoveryTests.SparseCard(8);c.FinalizeFooter(8);c.BadSync(2,100);return c;});
            Case("cancel_between_chunks",()=>new NpV1PowerCutRecoveryTests.SparseCard(6),true,1);
            Case("cancel_partial_read",()=>{var c=new NpV1PowerCutRecoveryTests.SparseCard(6);c.MaxReadBytes=4096;return c;},true,2);
            Ui();Console.WriteLine("PASS "+checks+" actual-app DAT-only checks; byte-equal AP/LFP/sensor/metadata outputs vs72, recovery/cancel, no transient neural raw files. Synthetic source only; no hardware.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
