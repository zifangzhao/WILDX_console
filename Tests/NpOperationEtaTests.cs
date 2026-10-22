using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using USB512_interface;

static class NpOperationEtaTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static string root;static int checks;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static Type T(string name){return app.GetType(name.Contains(".")?name:"USB512_interface."+name,true);}
    static object Call(object value,string name,params object[] args){try{return (value is Type?(Type)value:value.GetType()).GetMethod(name,All).Invoke(value is Type?null:value,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object Field(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static void Set(object value,string name,object data){value.GetType().GetField(name,All).SetValue(value,data);}
    static void Wait(Task task){while(!task.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(10);}task.GetAwaiter().GetResult();Application.DoEvents();}
    static string Hash(string path){using(var stream=File.OpenRead(path))using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream));}
    static string Folder(string name){string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);return folder;}
    static void Show(Form form){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();}
    static void Render(Form form,string name){using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,name+".png"));}}
    static void Format()
    {
        Action<double,double,double,bool,string> check=(done,total,seconds,known,expected)=>Need((string)Call(T("NpProgressEstimate"),"Remaining",done,total,seconds,known)==expected,"Incorrect ETA for "+done+" / "+total);
        check(0,100,30,true,"Estimating time…");check(25,100,2,true,"Estimating time…");check(25,100,60,true,"~00:03:00 remaining");check(75,100,60,true,"~00:00:20 remaining");
        check(99,100,100,true,"~00:00:02 remaining");check(1,100,3600,true,"~99:00:00 remaining");check(100,100,60,true,"Finalizing…");check(101,100,60,true,"Finalizing…");
        check(50,100,60,false,"Time remaining unknown (size unknown)");check(0,0,60,true,"Time remaining unknown (size unknown)");check(50,100,Double.NaN,true,"Estimating time…");
        check(50,100,Double.PositiveInfinity,true,"Estimating time…");check(1,100,Double.MaxValue,true,"Estimating time…");check(Double.NaN,100,60,true,"Estimating time…");
        check(100,1000,10,true,"~00:01:30 remaining");check(900,1000,90,true,"~00:00:10 remaining");
        Need((string)Call(T("NpProgressEstimate"),"Duration",90061.0)=="25:01:01","Elapsed time wraps at 24 hours");
        Need((string)Call(T("NpProgressEstimate"),"Duration",Double.NaN)=="--:--:--","Invalid elapsed time accepted");
    }
    static NpCalibrationSet Profiles()
    {
        string gain=Path.Combine(root,"gain.csv");File.WriteAllText(gain,"1001\n"+String.Join("\n",Enumerable.Range(0,384).Select(c=>c+",1.01"))+"\n");
        var profiles=new NpCalibrationSet();profiles.Set(1,new NpAdcCalibration(gain,null,"1001"));return profiles;
    }
    static string Dat(string folder,string name,int blocks=24,short value=100)
    {
        string path=Path.Combine(folder,name);var buffer=new byte[768*1024];for(int n=0;n<buffer.Length;n+=2){buffer[n]=(byte)value;buffer[n+1]=(byte)(value>>8);}
        using(var file=File.Create(path))for(int n=0;n<blocks;n++)file.Write(buffer,0,buffer.Length);return path;
    }
    static void Metadata(string folder)
    {
        File.WriteAllText(Path.Combine(folder,"recording_settings.json"),Json.Serialize(new{actual=new{probe_mask=1},output=new{dat_layout="per_probe",lfp_sample_rate=new{present=false}},probes=new[]{new{probe=1,identity=new{serial="1001"}}}}));
        File.WriteAllText(Path.Combine(folder,"capture_status.txt"),"sample_encoding=signed_nominal_adc_counts\n");
    }
    static void Calibration(NpCalibrationSet profiles)
    {
        string folder=Folder("single"),input=Dat(folder,"np1_ap.dat"),output=Path.Combine(folder,"corrected.dat"),before=Hash(input);
        using(var form=(Form)Activator.CreateInstance(T("NpCalibrationManualForm"),All,null,new object[]{"eta-test",true},null)){
            ((NpCalibrationSet)Field(form,"calibration")).Set(1,profiles.Get(1));Show(form);var status=(Label)Field(form,"status");var messages=new List<string>();status.TextChanged+=delegate{messages.Add(status.Text);};
            Wait((Task)Call(form,"CorrectFile",input,output,new[]{1},false,false));
            Need(File.Exists(output)&&!File.Exists(output+".partial")&&Hash(input)==before,"Single correction failed or changed source");
            Need(messages.Any(p=>p.StartsWith("Correcting DAT")&&p.Contains("Elapsed")&&(p.Contains("remaining")||p.Contains("Estimating"))),"Single DAT has no running progress/ETA");
            Need(status.Text.Contains("Corrected DAT saved")&&status.Text.Contains("Elapsed")&&!status.Text.Contains("remaining"),"Single DAT completion retained stale ETA");
            Render(form,"single-calibration-eta");string cancelled=Path.Combine(folder,"cancelled.dat");var task=(Task)Call(form,"CorrectFile",input,cancelled,new[]{1},false,false);Set(form,"cancelled",true);Wait(task);
            Need(!File.Exists(cancelled)&&status.Text.Contains("cancelled")&&!(bool)Field(form,"busy"),"Cancellation publishes complete DAT or leaves GUI busy");
        }
        string source=Folder("batch/source"),target=Path.Combine(root,"batch/output");string first=Path.Combine(source,"session1");Directory.CreateDirectory(first);File.Copy(input,Path.Combine(first,"np1_ap.dat"));Metadata(first);
        string second=Path.Combine(source,"session2");Directory.CreateDirectory(second);Dat(second,"np1_ap.dat",2);Metadata(second);
        using(var form=(Form)Activator.CreateInstance(T("NpCalibrationBatchForm"),All,null,new object[]{profiles,null},null)){
            ((TextBox)Field(form,"source")).Text=source;((TextBox)Field(form,"output")).Text=target;Show(form);Wait((Task)Call(form,"Scan"));
            Need(((IList)Field(form,"items")).Count==2,"Batch source scan failed");var status=(Label)Field(form,"status");var messages=new List<string>();status.TextChanged+=delegate{messages.Add(status.Text);};
            Wait((Task)Call(form,"Calibrate"));Need(messages.Any(p=>p.Contains("Calibrating")&&p.Contains("Elapsed")&&(p.Contains("remaining")||p.Contains("Estimating"))),"Batch has no running queue ETA");
            Need(((ProgressBar)Field(form,"progress")).Value==100&&status.Text.Contains("2 complete")&&status.Text.Contains("Elapsed"),"Batch completion is incorrect");Render(form,"batch-calibration-eta");
        }
    }
    static void Merge()
    {
        foreach(bool v2 in new[]{false,true}){
            string folder=Folder(v2?"v2-merge":"v1-merge");var saved=Call(T("NpRecordingSettings"),"Saved",folder,v2,20000,3,v2?14U:6U,99U,"sd_export",null,true);Call(saved,"Write");
            if(v2)File.WriteAllText(Path.Combine(folder,"np2_sd_export_status.txt"),"Source gap count: 0\n");
            foreach(string band in v2?new[]{"ap"}:new[]{"ap","lfp"})foreach(int p in new[]{1,2})Dat(folder,v2?"neuropixels_probe"+p+"_ap.dat":"np"+p+"_"+band+".dat",p,(short)(p*100));
            var percents=new List<int>();var messages=new List<string>();Call(T("CE32_console.NpSdDatMerge"),"ExportTimed",folder,v2,3,20000,(Func<bool>)(()=>false),(Action<int,string>)((p,m)=>{percents.Add(p);messages.Add(m);}));
            Need(percents.First()==0&&percents.Last()==100&&percents.Zip(percents.Skip(1),(a,b)=>a<=b).All(p=>p),"Merge progress is nonmonotonic or incomplete");
            Need(messages.Any(m=>m.Contains("Elapsed"))&&messages.Any(m=>m.Contains("Finalizing")),"Merge ETA/finalization absent");
            string ap=Path.Combine(folder,"neuropixels_ap.dat");using(var file=File.OpenRead(ap)){var row=new byte[1536];file.Read(row,0,row.Length);Need(BitConverter.ToInt16(row,0)==100&&BitConverter.ToInt16(row,768)==200,"Merge channel block order changed");}
            Need(File.Exists(Path.Combine(folder,"unpaired_tail/probe2_ap.dat")),"Merge dropped unpaired tail");
        }
    }
    static void Media()
    {
        string folder=Folder("media");File.WriteAllText(Path.Combine(folder,"recording.json"),Json.Serialize(new{status="validated transport",format_version=8,microphone_samples=2048,microphone_hz=200000,camera_frames=0,camera_requested_or_effective_hz=0}));
        string mic=Path.Combine(folder,"microphone.dat");File.WriteAllBytes(mic,Enumerable.Range(0,4096).Select(n=>(byte)n).ToArray());File.WriteAllBytes(Path.Combine(folder,"camera.raw"),new byte[0]);string before=Hash(mic);
        var messages=new List<string>();Action<int,string> report=(p,m)=>{Need(p>=0&&p<=100,"Media progress outside range");messages.Add(m);};
        Call(T("CE32_console.NpMediaExport"),"ConvertFiles",folder,(Func<bool>)(()=>false),report);
        Need(messages.Any(m=>m.Contains("Microphone WAV")&&m.Contains("Elapsed")),"WAV conversion ETA absent");string wav=Path.Combine(folder,"microphone.wav");Need(new FileInfo(wav).Length==4140&&Hash(mic)==before,"WAV conversion changed source/sample count");
        messages.Clear();string saved=Hash(wav);Call(T("CE32_console.NpMediaExport"),"ConvertFiles",folder,(Func<bool>)(()=>false),report);
        Need(messages.Any(m=>m.Contains("Verifying existing")&&m.Contains("Elapsed"))&&Hash(wav)==saved,"WAV verification ETA absent or existing WAV changed");
    }
    static void Video(string ffmpeg)
    {
        Environment.SetEnvironmentVariable("CE32_FFMPEG",ffmpeg);string folder=Folder("video");
        File.WriteAllText(Path.Combine(folder,"recording.json"),Json.Serialize(new{status="validated transport",format_version=8,microphone_samples=0,microphone_hz=0,camera_frames=2,camera_requested_or_effective_hz=50}));
        File.WriteAllBytes(Path.Combine(folder,"microphone.dat"),new byte[0]);var frame=new byte[160000];
        for(int y=0;y<320;y++){for(int n=0;n<12;n++)frame[y*492+n]=0x55;for(int x=0;x<320;x+=2){int a=512+(x+y)%5*10,b=512+(x+y+1)%5*10,at=y*492+12+x/2*3;frame[at]=(byte)(128|a>>3);frame[at+1]=(byte)((a&7)<<5|8|b>>7);frame[at+2]=(byte)((b&127)<<1);}}
        string path=Path.Combine(folder,"camera.raw");File.WriteAllBytes(path,frame.Concat(frame).ToArray());string before=Hash(path);var messages=new List<string>();
        Call(T("CE32_console.NpMediaExport"),"ConvertFiles",folder,(Func<bool>)(()=>false),(Action<int,string>)((p,m)=>messages.Add(m)));
        Need(messages.Any(m=>m.Contains("Camera MP4")&&m.Contains("Elapsed")),"MP4 stage ETA absent");Need(File.Exists(Path.Combine(folder,"camera.mp4"))&&!File.Exists(Path.Combine(folder,"camera.partial.mp4")),"MP4 not finalized");Need(Hash(path)==before,"MP4 conversion modified native frames");
    }
    static void DownloadLayout()
    {
        using(var form=(Form)Activator.CreateInstance(T("CE32_console.NpDownloadForm"),All,null,new object[]{null,false},null)){
            Show(form);var status=(Label)Field(form,"status");status.Text="Session 12345 (2/4): 45% | native download ~45.00 / 100.00 GiB · ~42.5 MB/s | ~00:20:30 remaining | Selected downloads: ~01:25:30 remaining · Elapsed 00:14:30\r\nE:\\Recordings\\Mouse1\\session_20261022_120000";
            foreach(int width in new[]{760,950,1250}){form.ClientSize=new Size(width,550);Application.DoEvents();int needed=TextRenderer.MeasureText(status.Text,status.Font,new Size(status.ClientSize.Width-status.Padding.Horizontal,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+status.Padding.Vertical;
                Need(status.Height>=needed&&form.ClientRectangle.Contains(status.Bounds),"Download ETA/status text clipped");Render(form,"download-eta-"+width);}
        }
    }
    [STAThread]static int Main(string[] args)
    {
        int result=0;try{Application.EnableVisualStyles();Control.CheckForIllegalCrossThreadCalls=true;app=Assembly.LoadFrom(Path.GetFullPath(args[0]));root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);Format();var profiles=Profiles();
            using(var runner=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){runner.Shown+=delegate{runner.BeginInvoke(new Action(()=>{try{Calibration(profiles);Merge();Media();if(args.Length>2)Video(args[2]);DownloadLayout();Console.WriteLine("PASS "+checks+" actual-app ETA, single/batch calibration, merge, media, cancellation and UI checks; synthetic files only, no hardware.");}catch(Exception ex){Console.Error.WriteLine(ex);result=1;}finally{runner.Close();}}));};Application.Run(runner);}
        }catch(Exception ex){Console.Error.WriteLine(ex);result=1;}return result;
    }
}
