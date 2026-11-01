using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

// One existing recording, one actual production export. No mode changes,
// recording, programming, card reset, format or delete APIs are used here.
static class NpDownloadSinglePassHardware
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static string root;static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};
    static readonly Dictionary<string,object> Report=new Dictionary<string,object>();
    static Type T(string name){return app.GetType(name,true);}
    static object V(object o,string name){var f=o.GetType().GetField(name,All);return f!=null?f.GetValue(o):o.GetType().GetProperty(name,All).GetValue(o,null);}
    static long N(object o,string name){return Convert.ToInt64(V(o,name));}
    static object Call(object o,string name,params object[] args){try{return (o as Type??o.GetType()).GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o is Type?null:o,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),All,null,args,null);}
    static void Need(bool value,string why){if(!value)throw new IOException(why);}
    static object Fields(object o){return o.GetType().GetFields(All).Where(f=>!f.IsStatic).ToDictionary(f=>f.Name,f=>f.GetValue(o));}
    static string Hash(string file){using(var sha=SHA256.Create())using(var input=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");}
    static void Save(){File.WriteAllText(Path.Combine(root,"result.json"),Json.Serialize(Report));}
    static string PathFor(string uid){return ((IEnumerable)Call(T("CE32_console.NpDownloadNative"),"EnumerateNpPaths")).Cast<string>().Single(p=>String.Equals((string)Call(T("USB512_interface.NpV2FirmwareForm"),"Uid",p),uid,StringComparison.OrdinalIgnoreCase));}
    static object Quiet(string path,string uid){
        var status=Call(T("USB512_interface.NpV2FirmwareForm"),"DownloadStatus",path,uid);
        Need(N(status,"Mode")==3&&N(status,"Busy")==0&&N(status,"Target")==0,"Exact V2 must already be idle in download mode; no mode/reset attempted");return status;
    }
    static byte[] Info(string path){using(var c=(IDisposable)New("USB512_interface.NpSystemInfoControl",path))return (byte[])Call(c,"Read",(byte)0xc0,(byte)0x60,(ushort)0,32);}
    static void Ready(byte[] b){Need(b.Length==32&&BitConverter.ToUInt32(b,0)==0x3144504e&&BitConverter.ToUInt32(b,8)>0&&BitConverter.ToUInt32(b,16)==0,"Real mounted idle card required; no card reset attempted");}
    static object[] Catalog(Stream s){return ((IEnumerable)Call(T("CE32_console.NpDownloadCatalog"),"ReadAvailable",s)).Cast<object>().ToArray();}
    static string CatalogIdentity(object[] records){return Json.Serialize(records.Select(r=>new{start=N(r,"Start"),end=N(r,"End"),id=N(r,"Session"),chunks=N(r,"Chunks"),state=N(r,"State"),fault=N(r,"Fault"),format=N(r,"FormatVersion")}).ToArray());}
    static Dictionary<string,object> DataFiles(string folder){return Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p)==".dat"||Path.GetExtension(p)==".imro"||Path.GetFileName(p)=="descriptor.bin"||Path.GetFileName(p)=="footer.bin"||Path.GetFileName(p).StartsWith("imu_")&&Path.GetExtension(p)==".csv").ToDictionary(p=>p.Substring(folder.Length),p=>(object)new{bytes=new FileInfo(p).Length,sha256=Hash(p)});}
    static void Run(string uid,uint session){
        string path=PathFor(uid);Report["path"]=path;Report["initial_status"]=Fields(Quiet(path,uid));
        byte[] before=Info(path);Ready(before);Thread.Sleep(200);byte[] second=Info(path);Ready(second);
        Need(before.SequenceEqual(second),"Download service changed during quiet check");Report["initial_info_hex"]=BitConverter.ToString(before);Save();
        using(var card=(Stream)New("CE32_console.NpDownloadStream",path,false)){
            Need(((string)V(card,"TransportDescription")).StartsWith("High-speed USB")&&!(bool)V(card,"IsSynthetic"),"Real high-speed USB required");
            var catalog=Catalog(card);Report["catalog_before"]=CatalogIdentity(catalog);
            var record=catalog.Single(r=>N(r,"Session")==session);
            Need((bool)V(record,"Downloadable")&&(bool)V(record,"Complete")&&N(record,"FormatVersion")==16&&N(record,"Chunks")>0,"Existing complete SD16 session required");
            long first=checked(N(record,"Start")*512),last=checked(N(record,"End")*512);
            Need(last>first&&last-first<1024L*1024*1024,"Benchmark is bounded to less than1GiB; larger record refused");Report["record"]=Fields(record);
            var timing=Call(card,"TimingSnapshot");string firmwareBefore=(string)Call(card,"Diagnostics");
            var watch=Stopwatch.StartNew();Stream prefetch=(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,last,(Func<bool>)(()=>false),1024*1024);
            try{Call(T("CE32_console.NpCe64Export"),"ExportCalibrated",prefetch,first/512,last/512,Path.Combine(root,"export"),(Func<bool>)(()=>false),null,null);}
            finally{prefetch.Dispose();watch.Stop();}
            Report["wall_seconds"]=watch.Elapsed.TotalSeconds;Report["prefetch_source_bytes"]=N(prefetch,"SourceBytes");
            Report["source_read_seconds"]=N(prefetch,"SourceReadTicks")/(double)Stopwatch.Frequency;
            Report["consumer_wait_seconds"]=N(prefetch,"ConsumerWaitTicks")/(double)Stopwatch.Frequency;
            Report["read_timing"]=Call(card,"PerformanceSince",timing,watch.Elapsed.TotalSeconds);
            Report["firmware_before"]=firmwareBefore;Report["firmware_after"]=(string)Call(card,"Diagnostics");Save();
            Need((string)Report["catalog_before"]==CatalogIdentity(Catalog(card)),"Catalog changed during read-only export");Report["catalog_unchanged"]=true;
        }
        Report["final_status"]=Fields(Quiet(PathFor(uid),uid));Ready(Info(PathFor(uid)));
        Report["files"]=DataFiles(Path.Combine(root,"export"));Report["result"]="PASS";
    }
    [STAThread]static int Main(string[] args){
        if(args.Length!=6)return 2;root=Path.GetFullPath(args[2]);Need(!Directory.Exists(root),"Fresh result directory required");Directory.CreateDirectory(root);int code=1;
        try{
            string exe=Path.GetFullPath(args[0]);Need(Hash(exe)==args[1].ToUpperInvariant(),"Application hash mismatch");
            Need(args[3]=="001A0004434250062039314E","Only the explicitly handed-off V2 UID is allowed");
            string sdk=Path.Combine(Path.GetDirectoryName(exe),"NpV2SdDatExport.dll");Need(Hash(sdk)==args[5].ToUpperInvariant(),"SDK hash mismatch");
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string file=Path.Combine(Path.GetDirectoryName(exe),new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};app=Assembly.LoadFrom(exe);
            Report["utc"]=DateTime.UtcNow.ToString("O");Report["pid"]=Process.GetCurrentProcess().Id;Report["uid"]=args[3];Report["executable_sha256"]=Hash(exe);Report["sdk_sha256"]=Hash(sdk);Report["version"]=app.GetName().Version.ToString();Report["helper_sha256"]=Hash(Assembly.GetExecutingAssembly().Location);
            using(var global=new Mutex(false,@"Local\WILDX_NP_TIMING_HIL"))using(var device=new Mutex(false,@"Local\WILDX_NP_TIMING_HIL_"+args[3])){
                bool g=false,d=false;try{
                    g=global.WaitOne(0);Need(g,"GLOBAL hardware lease is held; no I/O attempted");d=device.WaitOne(0);Need(d,"Exact UID lease is held; no I/O attempted");Report["exclusive_leases_acquired"]=true;Save();Run(args[3],UInt32.Parse(args[4]));code=0;
                }finally{if(d)device.ReleaseMutex();if(g)global.ReleaseMutex();Report["leases_released"]=true;}
            }
        }catch(Exception ex){Report["result"]="FAIL";Report["error"]=ex.ToString();}
        Save();Console.WriteLine(Report["result"]+"; "+Path.Combine(root,"result.json"));return code;
    }
}
