using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Production-API observer. Existing recordings only; no START, flash or card writes.
static class NpDownloadSpeedHardware
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static string root;static uint session;static Dictionary<string,object> report=new Dictionary<string,object>();
    static Type T(string name){return app.GetType(name,true);}
    static object V(object o,string name){var f=o.GetType().GetField(name,All);return f!=null?f.GetValue(o):o.GetType().GetProperty(name,All).GetValue(o,null);}
    static long N(object o,string name){return Convert.ToInt64(V(o,name));}
    static object Call(object o,string name,params object[] args){try{return (o as Type??o.GetType()).GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o is Type?null:o,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),All,null,args,null);}
    static void Need(bool value,string why){if(!value)throw new IOException(why);}
    static object Fields(object o){return o.GetType().GetFields(All).Where(f=>!f.IsStatic).ToDictionary(f=>f.Name,f=>f.GetValue(o));}
    static string Hash(string file){using(var sha=SHA256.Create())using(var input=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");}
    static string[] Paths(){return ((IEnumerable)Call(T("CE32_console.NpDownloadNative"),"EnumerateNpPaths")).Cast<string>().ToArray();}
    static string Uid(string path){return (string)Call(T("USB512_interface.NpV1Management"),"UidFromPath",path)??(string)Call(T("USB512_interface.NpV2FirmwareForm"),"Uid",path);}
    static bool V1(string path){return Call(T("USB512_interface.NpV1Management"),"UidFromPath",path)!=null;}
    static object Status(string path){
        if(!V1(path))return Call(T("USB512_interface.NpV2FirmwareForm"),"DownloadStatus",path,Uid(path));
        object target=((IEnumerable)Call(T("USB512_interface.NpV1Management"),"Enumerate")).Cast<object>().Single(p=>(string)V(p,"Path")==path);
        using(var manager=(IDisposable)New("USB512_interface.NpV1Management",target))return Call(manager,"ReadStatus");
    }
    static uint Mode(object status){return (uint)N(status,status.GetType().Name=="Status"?"Role":"Mode");}
    static byte[] Read(string path,byte request,int bytes){using(var control=(IDisposable)New("USB512_interface.NpSystemInfoControl",path))return (byte[])Call(control,"Read",(byte)0xc0,request,(ushort)0,bytes);}
    static object Inventory(string path){
        object status=Status(path);
        if(!V1(path)&&Mode(status)==2){var capture=Call(T("USB512_interface.NpV2CaptureStatus"),"Parse",Read(path,0x71,128));Need(N(capture,"State")==0&&N(capture,"SourceState")==0&&N(capture,"Error")==0,"V2 acquisition/source must be idle; no mode request sent");report["capture_idle_status"]=Fields(capture);}
        Need(N(status,"Busy")==0&&N(status,V1(path)?"PendingMode":"Target")==0,"Logger has an active operation; no mode/read attempted");
        object identity;try{identity=Call(T("USB512_interface.NpFirmwareIdentity"),"ReadUsb",path,V1(path)?1U:2U);identity=identity==null?null:Fields(identity);}catch(Exception ex){identity=ex.Message;}
        if(V1(path)&&Mode(status)==3){
            byte[] cap=Read(path,0x67,32);uint[] words=Enumerable.Range(0,8).Select(i=>BitConverter.ToUInt32(cap,i*4)).ToArray();
            Need(words[0]==0x3143424e&&words[1]==1&&words[2]==4096&&words[3]==1020&&words[4]==0&&words[6]==0&&words[7]==0,"V1 bounded-read capability/idle not confirmed");
            report["v1_burst_capability"]=new{words,confirmed=true};
        }
        return new{path,uid=Uid(path),generation=V1(path)?1:2,status=Fields(status),firmware=identity};
    }
    static Stream Open(string path){
        byte[] info=Read(path,0x60,32);Need(BitConverter.ToUInt32(info,0)==0x3144504e&&BitConverter.ToUInt32(info,8)>0&&BitConverter.ToUInt32(info,16)==0,"Download card is not ready/idle; no reset attempted");
        var stream=(Stream)New("CE32_console.NpDownloadStream",path,false);Need(((string)V(stream,"TransportDescription")).StartsWith("High-speed USB")&&!(bool)V(stream,"IsSynthetic"),"Real high-speed SD service required");return stream;
    }
    static Dictionary<string,object> DataFiles(string folder){return Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p)==".dat"||Path.GetExtension(p)==".imro"||Path.GetFileName(p)=="descriptor.bin"||Path.GetFileName(p)=="footer.bin").ToDictionary(p=>p.Substring(folder.Length),p=>(object)new{bytes=new FileInfo(p).Length,sha256=Hash(p)});}
    static void RawSource(string path,object record){
        long first=N(record,"Start")*512,last=N(record,"End")*512;
        using(var card=Open(path)){
            card.Position=first;string beforeRaw=(string)Call(card,"Diagnostics");byte[] buffer=new byte[1024*1024];long bytes=0;var timer=Stopwatch.StartNew();
            while(card.Position<last){int read=card.Read(buffer,0,(int)Math.Min(buffer.Length,last-card.Position));Need(read>0,"Unexpected end of raw source extent");bytes+=read;}
            timer.Stop();string afterRaw=(string)Call(card,"Diagnostics");report["raw_source_read"]=new{bytes,seconds=timer.Elapsed.TotalSeconds,MB_per_second=bytes/timer.Elapsed.TotalSeconds/1e6,before=beforeRaw,after=afterRaw,scope="Same bounded recording extent through production CRC-validating stream; no export/file write; physical extent bytes including metadata/padding"};
            Console.WriteLine("Raw validated source: "+(bytes/timer.Elapsed.TotalSeconds/1e6).ToString("F2")+" MB/s; "+timer.Elapsed.TotalSeconds.ToString("F2")+" s");
        }

    }
    static uint Counter(string text,string key){var m=System.Text.RegularExpressions.Regex.Match(text,@"(?:^| )"+key+@"=(\d+)");Need(m.Success,"Missing firmware counter "+key);return UInt32.Parse(m.Groups[1].Value);}
    static void Transport(string path,object record){
        const long bytes=256L*1024*1024;long first=N(record,"Start")*512,last=N(record,"End")*512;
        Need(first+bytes+3L*1020*512<=last,"Selected recording is too short for same-size prefix and legacy read-ahead allowance");
        var trials=new List<object>();report["transport_trials"]=trials;string reference=null;
        for(int n=0;n<3;n++)using(var card=Open(path)){
            string before=(string)Call(card,"Diagnostics");
            var input=(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,first+bytes,(Func<bool>)(()=>false),1024*1024);
            string hashText;double seconds;long done=0;var timer=Stopwatch.StartNew();var cleanup=new Stopwatch();
            try{using(var hash=SHA256.Create()){
                byte[] buffer=new byte[1024*1024];
                while(done<bytes){int got=input.Read(buffer,0,(int)Math.Min(buffer.Length,bytes-done));Need(got>0,"Truncated transport prefix");hash.TransformBlock(buffer,0,got,buffer,0);done+=got;}
                hash.TransformFinalBlock(new byte[0],0,0);timer.Stop();seconds=timer.Elapsed.TotalSeconds;hashText=BitConverter.ToString(hash.Hash).Replace("-","");
            }}finally{timer.Stop();cleanup.Start();input.Dispose();cleanup.Stop();}
            string after=(string)Call(card,"Diagnostics");Need(Counter(after,"errors")==Counter(before,"errors"),"Firmware errors increased");
            if(reference==null)reference=hashText;Need(reference==hashText,"Transport prefix SHA256 changed between trials");
            long payload=(long)unchecked(Counter(after,"sectors")-Counter(before,"sectors"))*512;
            var trial=new{index=n,consumed_physical_sd_bytes=done,seconds,MB_per_second=done/seconds/1e6,validated_packet_payload_bytes=payload,packet_payload_MB_s=payload/seconds/1e6,source_read_seconds=N(input,"SourceReadTicks")/(double)Stopwatch.Frequency,source_bytes=N(input,"SourceBytes"),sha256=hashText,cleanup_ms=cleanup.Elapsed.TotalMilliseconds,firmware_before=before,firmware_after=after};
            trials.Add(trial);Console.WriteLine("Transport "+n+": "+trial.MB_per_second.ToString("F2")+" MB/s; 256 MiB CRC+SHA validated");
        }
    }
    static void Benchmark(string path,object record){
        long first=N(record,"Start")*512,last=N(record,"End")*512,logicalBytes=N(record,"Chunks")*((bool)V(record,"V2")?46592L:65536L);
        var trials=new List<object>();report["trials"]=trials;string reference=null;var json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};

        // Alternating A/B order avoids assigning all warm-cache benefit to prefetch.
        for(int n=0;n<6;n++){
            bool buffered=n==1||n==2||n==5;string output=Path.Combine(root,"export_"+n+(buffered?"_prefetch":"_serial"));
            using(var card=Open(path)){
                string before=(string)Call(card,"Diagnostics");var watch=Stopwatch.StartNew();
                Stream prefetch=buffered?(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,last,(Func<bool>)(()=>false),1024*1024):null;
                try{Call(T("CE32_console.NpCe64Export"),"ExportCalibrated",prefetch??card,first/512,last/512,output,(Func<bool>)(()=>false),null,null);}
                finally{if(prefetch!=null)prefetch.Dispose();watch.Stop();}
                string after=(string)Call(card,"Diagnostics");var files=DataFiles(output);string actual=json.Serialize(files.OrderBy(p=>p.Key).ToArray());
                if(reference==null)reference=actual;Need(reference==actual,"Exported DAT/IMRO/metadata bytes differ between trials");
                var trial=new{index=n,prefetch=buffered,logical_source_bytes=logicalBytes,seconds=watch.Elapsed.TotalSeconds,MB_per_second=logicalBytes/watch.Elapsed.TotalSeconds/1e6,files,
                    source_read_seconds=prefetch==null?(double?)null:N(prefetch,"SourceReadTicks")/(double)Stopwatch.Frequency,
                    consumer_wait_seconds=prefetch==null?(double?)null:N(prefetch,"ConsumerWaitTicks")/(double)Stopwatch.Frequency,
                    prefetch_source_bytes=prefetch==null?(long?)null:N(prefetch,"SourceBytes"),firmware_before=before,firmware_after=after};
                trials.Add(trial);Console.WriteLine("Trial "+n+" "+(buffered?"prefetch":"serial")+": "+trial.MB_per_second.ToString("F2")+" MB/s; "+watch.Elapsed.TotalSeconds.ToString("F2")+" s; DAT hashes match");
                File.WriteAllText(Path.Combine(root,"benchmark-progress.json"),json.Serialize(report));
            }
        }
    }
    static async Task Run(string action,string uid){
        Need(uid=="001A0004434250062039314E"||uid=="001D00065942500B20313354","Exact approved V1/V2 UID required");string[] paths=Paths().Where(p=>String.Equals(Uid(p),uid,StringComparison.OrdinalIgnoreCase)).ToArray();Need(paths.Length==1&&V1(paths[0])==(uid=="001D00065942500B20313354"),"Exact device family/UID required");report["devices"]=paths.Select(Inventory).ToArray();if(action=="inventory")return;
        string path=paths.Single(p=>String.Equals(Uid(p),uid,StringComparison.OrdinalIgnoreCase));object initial=Status(path);uint original=Mode(initial);report["original_mode"]=original;
        Need(original==2||original==3,"Expected an idle USB streaming/download service");Need(!V1(path)||original==3,"V1 must already be idle in download mode; no mode change attempted");
        try{
            if(V1(path))path=(string)Call(T("USB512_interface.NpV1Management"),"EnsureDownload",path,(Func<bool>)(()=>false));
            else path=await (Task<string>)Call(T("USB512_interface.NpV2FirmwareForm"),"EnsureDownload",path,null);
            report["download_mode"]=Fields(Status(path));
            object[] records;
            using(var card=Open(path)){
                report["transport"]=V(card,"TransportDescription");report["card_bytes"]=card.Length;
                records=((IEnumerable)Call(T("CE32_console.NpDownloadCatalog"),"ReadAvailable",card)).Cast<object>().ToArray();report["catalog"]=records.Select(Fields).ToArray();
            }
            object chosen=records.Single(p=>N(p,"Session")==session);
            Need((bool)V(chosen,"Downloadable")&&(bool)V(chosen,"Complete")&&N(chosen,"Chunks")>0,"Existing complete recording required");
            if(V1(path))Need(N(chosen,"Start")==62128128&&N(chosen,"End")==62799872&&N(chosen,"Session")==1792385836&&N(chosen,"Chunks")==5120&&N(chosen,"ProbeMask")==3,"Exact preserved V1 fixture required");
            else Need(N(chosen,"Start")==425492480&&N(chosen,"End")==426291740&&N(chosen,"Session")==407207722&&N(chosen,"Chunks")==8468&&N(chosen,"ProbeMask")==7,"Exact preserved V2 fixture required");
            report["selected_record"]=Fields(chosen);Transport(path,chosen);
            if(action=="parity")Benchmark(path,chosen);
            using(var card=Open(path)){
                var after=((IEnumerable)Call(T("CE32_console.NpDownloadCatalog"),"ReadAvailable",card)).Cast<object>().ToArray();
                var json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};
                Need(json.Serialize(records.Select(Fields).ToArray())==json.Serialize(after.Select(Fields).ToArray()),"Catalog changed during read-only check");
                report["catalog_preserved"]=true;report["catalog_after_count"]=after.Length;
            }
        }finally{
            path=Paths().Single(p=>String.Equals(Uid(p),uid,StringComparison.OrdinalIgnoreCase));
            if(original==2){
                if(V1(path))report["restored"]=await Task.Run(()=>Call(T("USB512_interface.NpV1Management"),"EnsureStreaming",path));
                else report["restored"]=await (Task<string>)Call(T("USB512_interface.NpV2FirmwareForm"),"SwitchUsbMode",path,(ushort)2);
            }
            var returned=Stopwatch.StartNew();string fresh=null;object final=null;
            while(returned.Elapsed.TotalSeconds<30){
                var found=Paths().Where(p=>String.Equals(Uid(p),uid,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(found.Length==1){try{var observed=Status(found[0]);if(Mode(observed)==original&&N(observed,"Busy")==0&&N(observed,V1(found[0])?"PendingMode":"Target")==0){fresh=found[0];final=observed;break;}}catch(IOException){}}
                await Task.Delay(200);
            }
            Need(fresh!=null,"Exact idle original mode did not re-enumerate; no mode command replayed");report["final_status"]=Fields(final);
        }
    }
    [STAThread]static int Main(string[] args){
        if(args.Length<4||(args[2]!="inventory"&&args[2]!="transport"&&args[2]!="parity"))return 2;root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);if(File.Exists(Path.Combine(root,args[2]+".json")))throw new IOException("Fresh output required");int result=1;
        try{string exe=Path.GetFullPath(args[0]);app=Assembly.LoadFrom(exe);report["executable_sha256"]=Hash(exe);report["version"]=app.GetName().Version.ToString();Need(app.GetName().Version.ToString()=="1.0.0.141"&&Hash(exe)=="37A35A19B29D9F348D4D59BAEDAFB8E92270B89A4FB115F30F0E5E8A5E0E5268","Published141 required");
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string file=Path.Combine(Path.GetDirectoryName(exe),new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
            session=args.Length>4?UInt32.Parse(args[4]):0;Run(args[2],args.Length>3?args[3]:null).GetAwaiter().GetResult();report["result"]="pass";result=0;
        }catch(Exception ex){report["result"]="fail";report["error"]=ex.ToString();}
        string json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Serialize(report);File.WriteAllText(Path.Combine(root,args[2]+".json"),json);Console.WriteLine("Result="+(result==0?"pass":"fail")+"; report="+Path.Combine(root,args[2]+".json"));if(result!=0)Console.Error.WriteLine(report["error"]);return result;
    }
}
