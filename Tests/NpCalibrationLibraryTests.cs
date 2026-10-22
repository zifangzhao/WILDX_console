using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using USB512_interface;

static class NpCalibrationLibraryTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static string root,factory;static int checks;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object Call(object value,string method,params object[] args){try{return (value is Type?(Type)value:value.GetType()).GetMethod(method,All).Invoke(value is Type?null:value,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object Field(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static void Set(object value,string name,object field){value.GetType().GetField(name,All).SetValue(value,field);}
    static void Write(string path,string value){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value);}
    static string Config(string serial,int ap=500,int lfp=250){return Json.Serialize(new{Profile="Np1",SerialNumber=serial,Channels=Enumerable.Range(0,384).Select(ch=>new{Channel=ch,ApGain=ap,LfpGain=lfp})});}
    static object Mapping(string serial,int ap=500){return Call(T("NpCalibrationLibrary"),"Configuration",Config(serial,ap));}
    static NpAdcCalibration Resolve(object library,string serial,object mapping=null){return (NpAdcCalibration)Call(library,"Resolve",serial,mapping);}
    static object Scan(string path){return Call(T("NpCalibrationLibrary"),"Scan",path,null);}
    static void Refuse(Action action,string why){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static void Factory(string serial,bool np2)
    {
        string path=Path.Combine(factory,"nested",serial,"renamed-gain.csv");var text=new StringBuilder(serial+"\n");
        for(int ch=0;ch<384;ch++){text.Append(ch);for(int col=1;col<=(np2?1:16);col++)text.Append(',').Append((1+col/100.0).ToString("R",CultureInfo.InvariantCulture));text.Append('\n');}Write(path,text.ToString());
        if(!np2){text=new StringBuilder(serial+"\n");for(int ch=0;ch<32;ch++)text.Append(ch).Append(",19,11,5,0,3,8,2,513\n");Write(Path.Combine(Path.GetDirectoryName(path),"renamed-adc.csv"),text.ToString());}
    }
    static string Imro(string folder,int gain=500)
    {
        string file=Path.Combine(folder,"probe_1.imro");var text=new StringBuilder("(0,384)");for(int ch=0;ch<384;ch++)text.Append('(').Append(ch).Append(" 0 0 ").Append(gain).Append(" 250 1)");Write(file,text.ToString());return file;
    }
    static void Recording(string folder,string serial,bool np2,int ap=500,bool imro=false)
    {
        Write(Path.Combine(folder,"recording_settings.json"),Json.Serialize(new{actual=new{probe_mask=1},output=new{dat_layout="per_probe",lfp_sample_rate=new{present=!np2},ap_upsampled_15_to_20=false},probes=new[]{new{probe=1,identity=new{serial=serial},imro_file=imro?"probe_1.imro":null,imro_state=imro?"device_confirmed_applied":null,channel_mapping=np2?null:Enumerable.Range(0,384).Select(ch=>new{output_channel=ch,ap_gain_index=ap==500?3:4,lfp_gain_index=2})}}}));
        Write(Path.Combine(folder,np2?"np2_sd_export_status.txt":"capture_status.txt"),np2?"sample_encoding=signed_nominal_adc_counts\n":"sample_transform=512 - raw10\n");
        using(var file=new BinaryWriter(File.Create(Path.Combine(folder,"np1_ap.dat"))))for(int n=0;n<768;n++)file.Write((short)100);
        if(imro)Imro(folder,ap);
    }
    static object Fixtures()
    {
        factory=Path.Combine(root,"factory");for(int p=1;p<=4;p++)Factory((1000+p).ToString(),true);Factory("9001",false);
        Write(Path.Combine(factory,"unrelated.csv"),"other,headers\ntext\n");var library=Scan(factory);
        Need((int)library.GetType().GetProperty("Count",All).GetValue(library,null)==5,"Factory indexing depends on file names or physical-port limit");
        Need(Resolve(library,"1001").Serial=="1001" && Resolve(library,"1001").Np2,"NP2 lookup incorrectly requires an IMRO/gain input");
        var profile=Resolve(library,"9001",Mapping("9001"));Need(profile.ApGain==500&&profile.LfpGain==250&&profile.DigitalAdc,"NP1 ADC/gain pair or IMRO gains were not selected");Need(profile.Correct(100,0)==104 && profile.Correct(100,0,true)==111,"Wrong factory gain column selected from IMRO");
        Refuse(()=>Resolve(library,"9001"),"NP1 gains were silently guessed");Refuse(()=>Resolve(library,"1001",Mapping("9001")),"Configuration model/SN conflict accepted");
        var mixed=Json.Deserialize<Dictionary<string,object>>(Config("9001"));var rows=((IEnumerable)mixed["Channels"]).Cast<Dictionary<string,object>>().ToArray();rows[0]["ApGain"]=1000;
        Refuse(()=>Call(T("NpCalibrationLibrary"),"Configuration",Json.Serialize(mixed)),"Mixed NP1 gains were collapsed");
        string input=Path.Combine(root,"recordings"),output=Path.Combine(root,"corrected");
        for(int n=1;n<=4;n++)Recording(Path.Combine(input,"session"+n),(1000+n).ToString(),true);
        Recording(Path.Combine(input,"np1_500"),"9001",false,500,true);Recording(Path.Combine(input,"np1_1000"),"9001",false,1000);
        string info=Path.Combine(input,"probe_info_only");Recording(info,"9001",false,500,true);File.Delete(Path.Combine(info,"recording_settings.json"));
        Write(Path.Combine(info,"probe_info.json"),Json.Serialize(new{probes=new[]{new{physical_port=1,identity=new{serial="9001"},imro_file="probe_1.imro"}}}));
        string np2Settings=Path.Combine(input,"session1/recording_settings.json");var np2Doc=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(np2Settings));var np2Probe=((IEnumerable)np2Doc["probes"]).Cast<Dictionary<string,object>>().Single();np2Probe["channel_mapping"]=Enumerable.Range(0,384).Select(ch=>new{output_channel=ch,site=ch});Write(np2Settings,Json.Serialize(np2Doc));
        Recording(Path.Combine(input,"missing_sn"),null,true);Recording(Path.Combine(input,"conflict"),"999999",true);
        var format=T("NpCalibrationBatch+Encoding");var items=(IList)Call(T("NpCalibrationBatch"),"ScanLibrary",input,output,new NpCalibrationSet(),library,Enum.ToObject(format,0),(Func<bool>)(()=>false),null,false);
        Need(items.Count==9 && items.Cast<object>().Count(p=>(string)Field(p,"Status")=="Ready")==7,"Automatic scan did not resolve more than3 serials / separate NP1 gains / standalone probe info");
        foreach(var item in items.Cast<object>().Where(p=>(string)Field(p,"Status")=="Ready")){
            var loaded=((NpCalibrationSet)Field(item,"Calibration")).Get(1);string name=Path.GetFileName(Path.GetDirectoryName((string)Field(item,"Input")));
            Need(loaded.ApGain==(name=="np1_500"||name=="probe_info_only"?500:name=="np1_1000"?1000:0),"Different recording gains reused one cached profile");
        }
        string report=(string)Call(T("NpCalibrationBatch"),"Run",items,output,(Func<bool>)(()=>false),null,false);
        Need(File.Exists(report) && items.Cast<object>().Count(p=>(string)Field(p,"Status")=="Complete")==7,"Automatic calibration batch failed");
        Need(File.Exists(Path.Combine(output,"np1_500/np1_ap.dat.calibration/probe1.imro")),"Matched IMRO omitted from calibration provenance");
        Need(File.ReadAllText(Path.Combine(output,"np1_500/np1_ap.dat.calibration.json")).Contains("probe_imros"),"IMRO/SN binding omitted from receipt");
        foreach(string file in Directory.GetFiles(input,"*.dat",SearchOption.AllDirectories))Need(BitConverter.ToInt16(File.ReadAllBytes(file),0)==100,"Automatic calibration changed original data");
        string duplicate=Path.Combine(factory,"duplicate.csv");File.Copy(Path.Combine(factory,"nested/1001/renamed-gain.csv"),duplicate);Need(Resolve(Scan(factory),"1001").Serial=="1001","Identical duplicate factory files were rejected");
        File.WriteAllText(duplicate,File.ReadAllText(duplicate).Replace("1.01","1.02"));Refuse(()=>Resolve(Scan(factory),"1001"),"Conflicting duplicate factory files were silently chosen");File.Delete(duplicate);
        return library;
    }
    static void Wait(Task task){while(!task.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(10);}task.GetAwaiter().GetResult();}
    static void Ui(object library)
    {
        using(var form=(Form)Activator.CreateInstance(T("NpCalibrationForm"),All,null,new object[]{"library-test",null,null},null)){
            var events=(System.ComponentModel.EventHandlerList)typeof(System.ComponentModel.Component).GetProperty("Events",All).GetValue(form,null);object key=typeof(Form).GetField("EVENT_SHOWN",All).GetValue(null);if(events[key]!=null)events.RemoveHandler(key,events[key]);
            ((TextBox)Field(form,"folder")).Text=factory;Set(form,"library",library);Call(form,"SetBusy",false);var serials=(ComboBox[])Field(form,"serials");var imros=(TextBox[])Field(form,"imros");
            imros[0].Text=Path.Combine(root,"recordings/np1_500/probe_1.imro");serials[0].Text="9001";serials[1].Text="1002";
            Wait((Task)Call(form,"Match",0));Wait((Task)Call(form,"Match",1));Wait((Task)Call(form,"Apply"));
            var selected=(NpCalibrationSet)Call(T("NpCalibrationStore"),"Get","library-test");Need(selected.Count==2&&selected.Get(1).Serial=="9001"&&selected.Get(2).Serial=="1002","Simplified GUI did not apply SN/IMRO-matched profiles");
            string saved=Path.Combine(root,"live_metadata");Directory.CreateDirectory(saved);Call(T("NpCalibrationStore"),"SaveImros","library-test",saved);Need(File.Exists(Path.Combine(saved,"calibration/probe1.imro")),"Live calibration IMRO snapshot not retained");
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
            foreach(int width in new[]{850,1000,1250}){form.ClientSize=new Size(width,440);Application.DoEvents();foreach(var controls in new Control[][]{serials,imros})foreach(var c in controls)Need(c.Parent.ClientRectangle.Contains(c.Bounds),"Calibration input clipped");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"calibration-library-"+width+".png"));}}
            Call(form,"ImportRecording",Path.Combine(root,"recordings/np1_500/recording_settings.json"));Need(serials[0].Text=="9001"&&serials[1].Text==""&&imros[0].Text.EndsWith("probe_1.imro"),"Recording import retained another recording's assignments");
            ((TextBox)Field(form,"folder")).Text=Path.Combine(root,"different-factory");Need(Field(form,"library")==null&&!((Button)Field(form,"batch")).Enabled,"Changing folder retained a stale calibration index");
        }
    }
    static void Real(string path)
    {
        var before=Directory.GetFiles(path,"*.csv",SearchOption.AllDirectories).ToDictionary(p=>p,p=>(string)Call(T("NpCalibrationLibrary"),"Hash",p));
        object library=Scan(path);var serials=((IEnumerable)library.GetType().GetProperty("Serials",All).GetValue(library,null)).Cast<string>().ToArray();Need(serials.Length==27,"Unexpected real factory serial count");
        int np1=0,np2=0;foreach(string serial in serials){NpAdcCalibration profile;try{profile=Resolve(library,serial);np2++;}catch(InvalidDataException){profile=Resolve(library,serial,Mapping(serial));np1++;}Need(profile.Serial==serial,"Real factory lookup returned another probe");Need(profile.Np2||profile.DigitalAdc,"Real NP1 ADC pair not found");}
        foreach(var pair in before)Need((string)Call(T("NpCalibrationLibrary"),"Hash",pair.Key)==pair.Value,"User factory file changed");Console.WriteLine("Real factory folder: "+serials.Length+" serials (NP1 "+np1+", NP2 "+np2+"); "+before.Count+" CSVs read-only.");
    }
    [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();Control.CheckForIllegalCrossThreadCalls=true;app=Assembly.LoadFrom(Path.GetFullPath(args[0]));root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);var library=Fixtures();Ui(library);if(args.Length>2)Real(args[2]);Console.WriteLine("PASS "+checks+" calibration folder/SN/IMRO/recursive/UI checks; no hardware or source data writes.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
