using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using USB512_interface;

static class NpCalibrationBatchTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static Type batch,format;static string root;static int checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Field(object item,string name){return item.GetType().GetField(name,All).GetValue(item);}
    static object Call(string name,params object[] args){try{return batch.GetMethod(name,All).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static IList Scan(string source,string output,NpCalibrationSet profiles,int encoding=0,bool overwrite=false){return (IList)Call("Scan",source,output,profiles,Enum.ToObject(format,encoding),(Func<bool>)(()=>false),null,overwrite);}
    static string Run(IList items,string output,Func<bool> cancel=null,bool overwrite=false){return (string)Call("Run",items,output,cancel,null,overwrite);}
    static void Refuse(Action action,string why){try{action();}catch(IOException){checks++;return;}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static string Dir(string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    static void Write(string path,string value){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value);}
    static NpAdcCalibration Profile(string folder,string serial,bool np2,double factor)
    {
        string file=Path.Combine(folder,serial+"_gainCalValues.csv");var text=new StringBuilder(serial+"\n");
        for(int ch=0;ch<384;ch++){text.Append(ch);for(int i=0;i<(np2?1:16);i++)text.Append(',').Append((factor+(i>=8?0.25:0)).ToString("R",CultureInfo.InvariantCulture));text.Append('\n');}
        Write(file,text.ToString());string adc=null;
        if(!np2){adc=Path.Combine(folder,serial+"_ADCCalibration.csv");text=new StringBuilder(serial+"\n");for(int n=0;n<32;n++)text.Append(n).Append(",16,16,0,0,0,0,").Append(n%7+1).Append(',').Append(512+n%3).Append('\n');Write(adc,text.ToString());}
        return new NpAdcCalibration(file,adc,serial,np2?0:500,np2?0:250);
    }
    static void Data(string path,int[] ports,int rows=3,bool legacy=false,bool invalid=false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));using(var writer=new BinaryWriter(File.Create(path)))for(int row=0;row<rows;row++)foreach(int p in ports)for(int ch=0;ch<384;ch++)writer.Write(invalid?Int16.MaxValue:legacy?(short)(p*10+ch%7):(short)(p*10+ch%7-20));
    }
    static void Settings(string folder,int[] ports,NpCalibrationSet profiles,bool merged=false,bool np2=false,bool encoding=true)
    {
        int mask=ports.Aggregate(0,(m,p)=>m|(1<<(p-1)));
        Write(Path.Combine(folder,"recording_settings.json"),Json.Serialize(new{format="neuropixels.recording_settings",actual=new{probe_mask=mask},output=new{dat_layout=merged?"merged":"per_probe",channel_count_per_dat=merged?384*ports.Length:384,channel_order="ascending physical probe; contiguous 384-channel blocks",ap_upsampled_15_to_20=false,lfp_sample_rate=new{present=!np2}},probes=ports.Select(p=>new{probe=p,identity=new{serial_hex="0x"+UInt64.Parse(profiles.Get(p).Serial,CultureInfo.InvariantCulture).ToString("X16",CultureInfo.InvariantCulture)}})}));
        Write(Path.Combine(folder,"probe_1.imro"),"fixture IMRO\n");
        if(encoding)Write(Path.Combine(folder,np2?"np2_sd_export_status.txt":"capture_status.txt"),np2?"sample_encoding=signed_nominal_adc_counts\n":"sample_transform=512 - raw10\n");
    }
    static void CorrectBytes(string input,string output,NpCalibrationSet profiles,int[] ports,bool lfp=false,bool legacy=false,byte[] original=null)
    {
        byte[] from=original??File.ReadAllBytes(input),to=File.ReadAllBytes(output);Need(from.Length==to.Length,"DAT size changed");
        for(int at=0;at<from.Length/2;at++){int p=ports[(at/384)%ports.Length];short value=BitConverter.ToInt16(from,at*2);if(legacy)value=(short)((profiles.Get(p).Np2?2048:512)-(ushort)value);Need(BitConverter.ToInt16(to,at*2)==profiles.Get(p).Correct(value,at%384,lfp),"Wrong sample/physical port/band correction");}
    }
    static object Item(IList items,string suffix){return items.Cast<object>().Single(p=>((string)Field(p,"Input")).EndsWith(suffix,StringComparison.OrdinalIgnoreCase));}
    static string Status(object item){return (string)Field(item,"Status");}
    static void Recursive(NpCalibrationSet profiles)
    {
        string source=Dir("recordings"),output=Path.Combine(root,"recordings_calibrated");
        string merged=Path.Combine(source,"day1","merged");Settings(merged,new[]{1,3},profiles,true);Write(Path.Combine(merged,"dat_merge.json"),"{\"physical_probe_order\":[3,1]}");Data(Path.Combine(merged,"neuropixels_ap.dat"),new[]{3,1});Data(Path.Combine(merged,"neuropixels_lfp.dat"),new[]{3,1});
        string individual=Path.Combine(source,"day2","individual");Settings(individual,new[]{1,3},profiles);Data(Path.Combine(individual,"np3_ap.dat"),new[]{3});Data(Path.Combine(individual,"np1_lfp.dat"),new[]{1});
        string parent=Path.Combine(source,"day2","live");Settings(parent,new[]{1,3},profiles);Data(Path.Combine(parent,"probe3","neuropixels_ap.dat"),new[]{3});Data(Path.Combine(parent,"unpaired_tail","probe1_ap.dat"),new[]{1});
        string failed=Path.Combine(source,"bad_sample");Settings(failed,new[]{1},profiles);Data(Path.Combine(failed,"np1_ap.dat"),new[]{1},3,false,true);
        string partial=Path.Combine(source,"partial");Settings(partial,new[]{1},profiles);File.WriteAllBytes(Path.Combine(partial,"np1_ap.dat"),new byte[769]);
        string calibrated=Path.Combine(source,"already");Settings(calibrated,new[]{1},profiles);Data(Path.Combine(calibrated,"np1_ap.dat"),new[]{1});Write(Path.Combine(calibrated,"np1_ap.dat.calibration.json"),"{}");
        string metadataOnly=Path.Combine(source,"metadata_applied");Settings(metadataOnly,new[]{1},profiles);var metadata=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(metadataOnly,"recording_settings.json")));metadata["adc_calibration_file"]="adc_calibration.json";Write(Path.Combine(metadataOnly,"recording_settings.json"),Json.Serialize(metadata));Data(Path.Combine(metadataOnly,"np1_ap.dat"),new[]{1});
        string unknown=Path.Combine(source,"unknown");Data(Path.Combine(unknown,"neuropixels_ap.dat"),new[]{1,3});
        Data(Path.Combine(source,"old.dat.source-metadata","np1_ap.dat"),new[]{1});Data(Path.Combine(source,".wildx_post_calibration_old","np1_ap.dat"),new[]{1});Data(Path.Combine(source,"analogin.dat"),new[]{1});
        string missing=Path.Combine(source,"missing_serial");Settings(missing,new[]{1},profiles);string json=File.ReadAllText(Path.Combine(missing,"recording_settings.json")).Replace("0x0000000000018A89","0x00000000000F423F");Write(Path.Combine(missing,"recording_settings.json"),json);Data(Path.Combine(missing,"np1_ap.dat"),new[]{1});
        string resampled=Path.Combine(source,"resampled");Settings(resampled,new[]{1},profiles);Write(Path.Combine(resampled,"recording_settings.json"),File.ReadAllText(Path.Combine(resampled,"recording_settings.json")).Replace("\"ap_upsampled_15_to_20\":false","\"ap_upsampled_15_to_20\":true"));Data(Path.Combine(resampled,"np1_ap.dat"),new[]{1});
        string gaps=Path.Combine(source,"gaps");Settings(gaps,new[]{1},profiles);Write(Path.Combine(gaps,"capture_status.txt"),"sample_transform=512 - raw10\nnp1_missing_ap_rows=1\n");Data(Path.Combine(gaps,"np1_ap.dat"),new[]{1});
        var originals=Directory.GetFiles(source,"*.dat",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        var items=Scan(source,output,profiles);Need(items.Count==14,"Recursive scan included auxiliary/derived files or lost a candidate");
        Need(items.Cast<object>().Count(p=>Status(p)=="Ready")==7,"Unexpected ready-file count");
        Need(((int[])Field(Item(items,"neuropixels_ap.dat".Insert(0,"merged"+Path.DirectorySeparatorChar)),"Ports")).SequenceEqual(new[]{3,1}),"Declared merged order was sorted/inferred");
        foreach(string name in new[]{"partial","already","metadata_applied","unknown","missing_serial","resampled","gaps"})Need(items.Cast<object>().Any(p=>((string)Field(p,"Input")).Contains(Path.DirectorySeparatorChar+name+Path.DirectorySeparatorChar)&&Status(p)=="Skipped"),"Unsafe file was not skipped: "+name);
        string report=Run(items,output);Need(File.Exists(report),"Batch report missing");Need(items.Cast<object>().Count(p=>Status(p)=="Complete")==6&&items.Cast<object>().Count(p=>Status(p)=="Failed")==1,"One bad file prevented other exports or was not reported");
        foreach(var original in originals)Need(File.ReadAllBytes(original.Key).SequenceEqual(original.Value),"Original input changed");
        foreach(var item in items.Cast<object>().Where(p=>Status(p)=="Complete")){
            string input=(string)Field(item,"Input"),target=(string)Field(item,"Output");CorrectBytes(input,target,profiles,(int[])Field(item,"Ports"),(bool)Field(item,"Lfp"));Need(File.Exists(target+".calibration.json")&&Directory.Exists(target+".calibration"),"Correction provenance missing");
            var saved=NpCalibrationSet.Read(target+".calibration");foreach(int p in (int[])Field(item,"Ports"))Need(saved.Get(p).AdcHash==profiles.Get(p).AdcHash&&saved.Get(p).GainHash==profiles.Get(p).GainHash,"Factory ADC/gain snapshot changed or missing");
            Need((string)Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(target+".calibration","adc_calibration.json")))["stage"]=="batch_host_post_calibration","Post-calibration stage mislabeled");
        }
        string tail=Path.Combine(output,"day2","live","unpaired_tail","probe1_ap.dat");Need(File.Exists(Path.Combine(tail+".source-metadata","parent.source-metadata","recording_settings.json")),"Parent recording/probe metadata lost for tail output");
        string copied=Path.Combine(output,"day1","merged","neuropixels_ap.dat.source-metadata","probe_1.imro");Need(File.ReadAllText(copied)=="fixture IMRO\n","IMRO snapshot missing");
        Refuse(()=>Scan(source,output,profiles),"Existing batch report accepted");
        var again=Scan(output,Path.Combine(root,"again"),profiles);Need(again.Cast<object>().All(p=>Status(p)=="Skipped"),"Calibrated files accepted for a second correction");
    }
    static void LegacyAndNp2(NpCalibrationSet profiles,string calibrationFolder)
    {
        string source=Dir("legacy"),output=Path.Combine(root,"legacy_out");Data(Path.Combine(source,"np1_ap.dat"),new[]{1},3,true);
        Need(Status(Scan(source,output,profiles)[0])=="Skipped","Unknown source encoding guessed");var items=Scan(source,output,profiles,2);Need(Status(items[0])=="Ready"&&((string)Field(items[0],"Detail")).Contains("manual assignment"),"Explicit legacy/manual assignment not visible");Run(items,output);CorrectBytes(Path.Combine(source,"np1_ap.dat"),Path.Combine(output,"np1_ap.dat"),profiles,new[]{1},false,true);
        var np2=new NpCalibrationSet();np2.Set(1,Profile(calibrationFolder,"303003",true,1.25));np2.Set(3,Profile(calibrationFolder,"404004",true,1.5));
        source=Dir("np2");output=Path.Combine(root,"np2_out");Settings(source,new[]{1,3},np2,true,true);Write(Path.Combine(source,"dat_merge.json"),"{\"physical_probe_order\":[1,3]}");Data(Path.Combine(source,"neuropixels_ap.dat"),new[]{1,3});Data(Path.Combine(source,"neuropixels_probe3_ap.dat"),new[]{3});
        var loaded=new NpCalibrationSet();loaded.Set(2,np2.Get(1));loaded.Set(1,np2.Get(3));items=Scan(source,output,loaded);Need(items.Cast<object>().All(p=>Status(p)=="Ready"),"Saved serial did not remap calibration across ports");Run(items,output);CorrectBytes(Path.Combine(source,"neuropixels_ap.dat"),Path.Combine(output,"neuropixels_ap.dat"),np2,new[]{1,3});CorrectBytes(Path.Combine(source,"neuropixels_probe3_ap.dat"),Path.Combine(output,"neuropixels_probe3_ap.dat"),np2,new[]{3});
    }
    static void Overwrite(NpCalibrationSet profiles)
    {
        string source=Dir("overwrite");Settings(source,new[]{3},profiles);string file=Path.Combine(source,"np3_ap.dat");Data(file,new[]{3});byte[] original=File.ReadAllBytes(file);
        var items=Scan(source,source,profiles,0,true);string report=Run(items,source,null,true);Need(Status(items[0])=="Complete"&&File.Exists(report),"Overwrite did not complete/report");CorrectBytes(file,file,profiles,new[]{3},false,false,original);
        Need(File.Exists(file+".calibration.json")&&!File.Exists(file+".post-calibration.pending.json"),"Overwrite provenance/journal state wrong");Need(Directory.GetDirectories(source,".wildx_post_calibration_*").Length==0,"Successful overwrite left a staging folder");Need(Status(Scan(source,source,profiles,0,true)[0])=="Skipped","Overwritten DAT accepted for double correction");
        Need(!(bool)Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file+".calibration.json"))["source_original_preserved"],"Overwrite receipt incorrectly claims original retained");
        Need(!(bool)Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(file+".calibration","adc_calibration.json")))["raw_preserved"],"Overwrite calibration manifest incorrectly claims raw retained");
        source=Dir("overwrite_cancel");Settings(source,new[]{1},profiles);file=Path.Combine(source,"np1_ap.dat");Data(file,new[]{1});original=File.ReadAllBytes(file);items=Scan(source,source,profiles,0,true);
        bool cancelled=false;try{Run(items,source,()=>Directory.GetFiles(source,"*.calibration.json",SearchOption.AllDirectories).Any(),true);}catch(OperationCanceledException){cancelled=true;}
        Need(cancelled&&File.ReadAllBytes(file).SequenceEqual(original)&&!File.Exists(file+".calibration.json"),"Cancellation replaced original or published incomplete provenance");Need(Directory.GetFiles(source,"post_calibration_report_*.json").Length==1,"Cancelled overwrite has no report");
        source=Dir("overwrite_locked");Settings(source,new[]{1},profiles);file=Path.Combine(source,"np1_ap.dat");Data(file,new[]{1});original=File.ReadAllBytes(file);items=Scan(source,source,profiles,0,true);
        using(var block=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){Run(items,source,null,true);Need(File.ReadAllBytes(file).SequenceEqual(original),"Failed replacement modified original");}
        Need(Status(items[0])=="Failed"&&File.Exists(file+".post-calibration.pending.json"),"Failed commit lost recovery evidence");Need(((string)Field(Scan(source,source,profiles,0,true)[0],"Detail")).Contains("interrupted overwrite"),"Interrupted overwrite can be corrected twice");
    }
    static void Ui(NpCalibrationSet profiles)
    {
        var type=app.GetType("USB512_interface.NpCalibrationBatchForm",true);
        using(var form=(Form)Activator.CreateInstance(type,All,null,new object[]{profiles},null)){
            var overwrite=(CheckBox)form.Controls.Find("calibrationBatchOverwrite",true).Single();var output=(TextBox)form.Controls.Find("calibrationBatchOutput",true).Single();
            var status=(Label)form.Controls.Find("calibrationBatchStatus",true).Single();
            Need(!overwrite.Checked&&output.Enabled,"Overwrite must be explicit");overwrite.Checked=true;Need(!output.Enabled&&status.Text.Contains("will be replaced"),"Overwrite mode/status not explicit");overwrite.Checked=false;Need(output.Enabled&&status.Text.Contains("originals are preserved"),"Copy mode/status did not recover");
            var source=(TextBox)form.Controls.Find("calibrationBatchSource",true).Single();source.Text=Path.Combine(root,"recordings");Need(output.Text.EndsWith("recordings_calibrated",StringComparison.OrdinalIgnoreCase),"Default mirrored output path missing");
            var grid=(DataGridView)form.Controls.Find("calibrationBatchFiles",true).Single();
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();output.Text=Path.Combine(root,"ui_preview_copy");
            var task=(System.Threading.Tasks.Task)type.GetMethod("Scan",All).Invoke(form,null);var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&watch.ElapsedMilliseconds<10000){Application.DoEvents();System.Threading.Thread.Sleep(10);}Need(task.IsCompleted,"GUI scan stalled");task.GetAwaiter().GetResult();Need(grid.Rows.Count==14&&((Button)type.GetField("start",All).GetValue(form)).Enabled,"GUI scan did not populate/enable the batch action");
            form.PerformLayout();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"batch-ui.png"));}form.Hide();
            Need(grid.ColumnCount==5&&grid.ReadOnly,"Batch preview grid missing/read-write");
        }
    }
    [STAThread]static int Main(string[] args)
    {
        try{
            app=Assembly.LoadFrom(Path.GetFullPath(args[0]));batch=app.GetType("USB512_interface.NpCalibrationBatch",true);format=batch.GetNestedType("Encoding",All);root=Path.GetFullPath(args[1]);if(Directory.Exists(root))throw new IOException("Fresh output required");Directory.CreateDirectory(root);
            string folder=Dir("factory");var profiles=new NpCalibrationSet();profiles.Set(1,Profile(folder,"101001",false,1.25));profiles.Set(3,Profile(folder,"202002",false,1.5));
            Recursive(profiles);LegacyAndNp2(profiles,folder);Overwrite(profiles);Ui(profiles);
            Console.WriteLine("PASS "+checks+" actual-app batch calibration checks: recursive merged/per-probe AP/LFP, declared order/serial remapping, skips/errors, copy preservation, legacy/NP2, cancellation and atomic overwrite/failed-commit journal. Synthetic files only; no hardware.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
