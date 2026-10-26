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
    static void CpuCost(){
        byte[] data=new byte[1020*512+32];new Random(19).NextBytes(data);
        var method=T("CE32_console.NpDownloadStream").GetMethod("Crc",All);
        var crc=(Func<byte[],int,int,uint>)Delegate.CreateDelegate(typeof(Func<byte[],int,int,uint>),method);
        uint value=crc(data,32,data.Length-32);var trials=new List<object>();
        for(int n=0;n<5;n++){var watch=Stopwatch.StartNew();uint check=0;for(int i=0;i<1024;i++)check^=crc(data,32,data.Length-32);watch.Stop();Need(check==0,"CRC repeat mismatch");trials.Add(new{MB_s=(data.Length-32)*1024.0/watch.Elapsed.TotalSeconds/1e6,seconds=watch.Elapsed.TotalSeconds});}
        report["crc_trials"]=trials;report["crc_value"]=value;
        report["scope"]="Actual published 150 host CRC delegate; no device access";
    }
    static void Transport(string path,object record){
        const long bytes=256L*1024*1024;long first=N(record,"Start")*512,last=N(record,"End")*512;
        Need(first+bytes+3L*1020*512<=last,"Selected recording too short for bounded prefix plus legacy read-ahead allowance");
        var trials=new List<object>();report["transport_trials"]=trials;string reference=null;
        uint[] batches={32,256,256,32,32,256};
        for(int n=0;n<batches.Length;n++)using(var card=Open(path)){
            string before=(string)Call(card,"Diagnostics");
            var input=(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,first+bytes,(Func<bool>)(()=>false),1024*1024);
            string hashText;double seconds;long done=0;var timer=new Stopwatch();var cleanup=new Stopwatch();
            try{
                var maximum=T("CE32_console.NpDownloadStream").GetField("burstMaximum",All);Need((uint)maximum.GetValue(card)==(app.GetName().Version.Revision==151?256U:32U),"Expected compiled bounded batch policy");
                // Test-only runtime change, bounded by the exact advertised maximum.
                byte[] caps=(byte[])Call(card,"Control",(byte)0xc0,(byte)0x67,(ushort)0,(ushort)0,(ushort)32);Need(BitConverter.ToUInt32(caps,0)==0x3143424e&&BitConverter.ToUInt32(caps,8)>=batches[n],"Requested batch exceeds negotiated capability");maximum.SetValue(card,batches[n]);
                using(var hash=SHA256.Create()){
                    byte[] buffer=new byte[1024*1024];timer.Start();
                    while(done<bytes){int got=input.Read(buffer,0,(int)Math.Min(buffer.Length,bytes-done));Need(got>0,"Truncated transport prefix");hash.TransformBlock(buffer,0,got,buffer,0);done+=got;}
                    hash.TransformFinalBlock(new byte[0],0,0);timer.Stop();seconds=timer.Elapsed.TotalSeconds;hashText=BitConverter.ToString(hash.Hash).Replace("-","");
                }
            }finally{timer.Stop();cleanup.Start();input.Dispose();cleanup.Stop();}
            string after=(string)Call(card,"Diagnostics");Need(Counter(after,"errors")==Counter(before,"errors"),"Firmware errors increased");
            if(reference==null)reference=hashText;Need(reference==hashText,"Transport prefix SHA256 changed between trials");
            var trial=new{index=n,batch=batches[n],consumed_physical_sd_bytes=done,seconds,MB_per_second=done/seconds/1e6,source_read_seconds=N(input,"SourceReadTicks")/(double)Stopwatch.Frequency,source_bytes=N(input,"SourceBytes"),sha256=hashText,cleanup_ms=cleanup.Elapsed.TotalMilliseconds,firmware_before=before,firmware_after=after};
            trials.Add(trial);Console.WriteLine("Transport batch"+batches[n]+": "+trial.MB_per_second.ToString("F3")+" MB/s; CRC and SHA validated");
            File.WriteAllText(Path.Combine(root,"progress.json"),new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Serialize(report));
        }
    }
    static void Benchmark(string path,object record){
        long first=N(record,"Start")*512,last=N(record,"End")*512,logicalBytes=N(record,"Chunks")*((bool)V(record,"V2")?46592L:65536L);
        var trials=new List<object>();report["trials"]=trials;string reference=null;var json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};

        // Alternating A/B order avoids assigning all warm-cache benefit to prefetch.
        for(int n=0;n<6;n++){
            uint batch=(n==1||n==2||n==5)?256U:32U;bool buffered=true;string output=Path.Combine(root,"export_"+n+(buffered?"_prefetch":"_serial"));
            using(var card=Open(path)){
                string before=(string)Call(card,"Diagnostics");var watch=Stopwatch.StartNew();
                Stream prefetch=buffered?(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,last,(Func<bool>)(()=>false),1024*1024):null;
                try{T("CE32_console.NpDownloadStream").GetField("burstMaximum",All).SetValue(card,batch);Call(T("CE32_console.NpCe64Export"),"ExportCalibrated",prefetch??card,first/512,last/512,output,(Func<bool>)(()=>false),null,null);}
                finally{if(prefetch!=null)prefetch.Dispose();watch.Stop();}
                string after=(string)Call(card,"Diagnostics");var files=DataFiles(output);string actual=json.Serialize(files.OrderBy(p=>p.Key).ToArray());
                if(reference==null)reference=actual;Need(reference==actual,"Exported DAT/IMRO/metadata bytes differ between trials");
                Need(Counter(after,"errors")==Counter(before,"errors"),"Firmware errors increased during export");var trial=new{index=n,batch,prefetch=buffered,logical_source_bytes=logicalBytes,seconds=watch.Elapsed.TotalSeconds,MB_per_second=logicalBytes/watch.Elapsed.TotalSeconds/1e6,files,
                    source_read_seconds=prefetch==null?(double?)null:N(prefetch,"SourceReadTicks")/(double)Stopwatch.Frequency,
                    consumer_wait_seconds=prefetch==null?(double?)null:N(prefetch,"ConsumerWaitTicks")/(double)Stopwatch.Frequency,
                    prefetch_source_bytes=prefetch==null?(long?)null:N(prefetch,"SourceBytes"),firmware_before=before,firmware_after=after};
                trials.Add(trial);Console.WriteLine("Trial "+n+" batch"+batch+": "+trial.MB_per_second.ToString("F2")+" MB/s; "+watch.Elapsed.TotalSeconds.ToString("F2")+" s; DAT hashes match");
                File.WriteAllText(Path.Combine(root,"benchmark-progress.json"),json.Serialize(report));
            }
        }
    }
    static void CancelCheck(string path,object record){
        long first=N(record,"Start")*512,last=first+256L*1024*1024;byte[] prefix=new byte[1024*1024];string expected=null;var trials=new List<object>();
        foreach(uint batch in new uint[]{32,256}){
            using(var card=Open(path)){
                string before=(string)Call(card,"Diagnostics");var input=(Stream)New("CE32_console.NpDownloadPrefetchStream",card,first,last,(Func<bool>)(()=>false),1024*1024);
                var cleanup=new Stopwatch();uint pending;
                try{T("CE32_console.NpDownloadStream").GetField("burstMaximum",All).SetValue(card,batch);Need(input.Read(prefix,0,prefix.Length)==prefix.Length,"Short cancellation prefix");using(var sha=SHA256.Create()){string actual=BitConverter.ToString(sha.ComputeHash(prefix)).Replace("-","");if(expected==null)expected=actual;Need(actual==expected,"Prefix differs across batch cancellation checks");}}
                finally{cleanup.Start();input.Dispose();cleanup.Stop();}
                byte[] caps=(byte[])Call(card,"Control",(byte)0xc0,(byte)0x67,(ushort)0,(ushort)0,(ushort)32);pending=BitConverter.ToUInt32(caps,16);Need(pending==0,"Burst remained active after cleanup");
                string after=(string)Call(card,"Diagnostics");Need(Counter(after,"errors")==Counter(before,"errors"),"Firmware errors increased during retirement");trials.Add(new{batch,retirement_ms=cleanup.Elapsed.TotalMilliseconds,pending,sha256=expected});
            }
            using(var reopened=Open(path)){reopened.Position=first;Need(reopened.Read(prefix,0,prefix.Length)==prefix.Length,"Short reopen prefix");using(var sha=SHA256.Create())Need(BitConverter.ToString(sha.ComputeHash(prefix)).Replace("-","")==expected,"Reopened prefix changed after retirement");}
        }
        report["cancel_trials"]=trials;report["reopened_prefix_hashes_match"]=true;
    }
    static async Task Run(string action,string uid){
        Need(uid=="001A0004434250062039314E"||uid=="001D00065942500B20313354"||uid=="001E002A5942500B20313354","Exact approved V1/V2 UID required");string[] paths=Paths().Where(p=>String.Equals(Uid(p),uid,StringComparison.OrdinalIgnoreCase)).ToArray();Need(paths.Length==1&&V1(paths[0])==(uid!="001A0004434250062039314E"),"Exact device family/UID required");report["devices"]=paths.Select(Inventory).ToArray();if(action=="inventory")return;
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
            var eligible=records.Where(p=>(bool)V(p,"Downloadable")&&(bool)V(p,"Complete")&&N(p,"Chunks")>0&&(N(p,"End")-N(p,"Start"))*512>=256L*1024*1024+3L*1020*512).ToArray();
            Need(eligible.Length>0,"No sufficiently long complete existing recording; no recording or card change attempted");
            object chosen=session==0?eligible.OrderBy(p=>N(p,"End")-N(p,"Start")).First():eligible.Single(p=>N(p,"Session")==session);
            if(uid=="001D00065942500B20313354"&&session==1792385836)Need(N(chosen,"Start")==62128128&&N(chosen,"End")==62799872&&N(chosen,"Chunks")==5120&&N(chosen,"ProbeMask")==3,"Preserved V1 fixture tuple changed");
            report["selected_record"]=Fields(chosen);if(action=="transport")Transport(path,chosen);if(action=="cancel")CancelCheck(path,chosen);
            if(action=="export")Benchmark(path,chosen);
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
        if(args.Length<4||(args[2]!="cpu"&&args[2]!="inventory"&&args[2]!="transport"&&args[2]!="export"&&args[2]!="cancel"))return 2;root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);if(File.Exists(Path.Combine(root,args[2]+".json")))throw new IOException("Fresh output required");int result=1;
        try{string exe=Path.GetFullPath(args[0]);app=Assembly.LoadFrom(exe);report["executable_sha256"]=Hash(exe);report["version"]=app.GetName().Version.ToString();Need((app.GetName().Version.ToString()=="1.0.0.150"&&Hash(exe)=="BE4C320DEECC1E28C4DACACAD6C2F2F65220B375B60A4E1080C43C1B3F118CF3")||(app.GetName().Version.ToString()=="1.0.0.151"&&Hash(exe)=="023D19C256109965E97C0E0787FD53E271D169C8F6C033AF6A97157EA0DC5EFE"),"Exact checked150 or151 required");
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string file=Path.Combine(Path.GetDirectoryName(exe),new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
            session=args.Length>4?UInt32.Parse(args[4]):0;if(args[2]=="cpu")CpuCost();else Run(args[2],args[3]).GetAwaiter().GetResult();report["result"]="pass";result=0;
        }catch(Exception ex){report["result"]="fail";report["error"]=ex.ToString();}
        string json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Serialize(report);File.WriteAllText(Path.Combine(root,args[2]+".json"),json);Console.WriteLine("Result="+(result==0?"pass":"fail")+"; report="+Path.Combine(root,args[2]+".json"));if(result!=0)Console.Error.WriteLine(report["error"]);return result;
    }
}
