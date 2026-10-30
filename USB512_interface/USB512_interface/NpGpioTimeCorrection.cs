using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Threading;
using System.Security.Cryptography;

namespace USB512_interface
{
    // Derived event times only; raw RTC events and clock queries are preserved.
    internal static class NpGpioTimeCorrection
    {
        const string CsvName="gpio_rtc_events.timestamps.calibrated.csv",ReportName="gpio_timestamp_calibration.json";
        sealed class Query {internal double Device,Send,Receive;internal string Epoch;}
        sealed class RawSession {internal string Folder,Events,Sync;internal Dictionary<string,object> Metadata;internal bool Linked;}
        sealed class Origin {internal bool Valid;internal string Error,Hash,Identity,Epoch,PcFile,PcHash,CanonicalClock;internal uint Session,Generation;internal decimal Lower,Upper;}
        sealed class Pending {internal bool Again=true,Replace;}
        static readonly object queueGate=new object();
        static readonly Dictionary<string,Pending> queued=new Dictionary<string,Pending>(StringComparer.OrdinalIgnoreCase);
        static double Number(string s){return Double.Parse(s,CultureInfo.InvariantCulture);}
        static bool Finite(double value){return !Double.IsNaN(value)&&!Double.IsInfinity(value);}
        static string F(double value){return value.ToString("R",CultureInfo.InvariantCulture);}
        static string F(decimal value){return value.ToString("G29",CultureInfo.InvariantCulture);}
        static string EventValue(string line,string key){var m=Regex.Match(line,@"(?:^|, )"+Regex.Escape(key)+@": ([^,]+)");return m.Success?m.Groups[1].Value:null;}
        static bool Flag(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)&&v is bool&&(bool)v;}
        static void Need(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
        static Dictionary<string,object> ReadJson(string path,int maximum=65536){Need(new FileInfo(path).Length<=maximum,"Oversized GPIO timing metadata.");return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
        static string Hash(string path){if(!File.Exists(path))return null;using(var h=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(input)).Replace("-","");}
        static void Same(Dictionary<string,object> a,Dictionary<string,object> b,IEnumerable<string> fields){
            foreach(string key in fields)Need(a.ContainsKey(key)&&b.ContainsKey(key)&&a[key]!=null&&b[key]!=null&&
                String.Equals(Convert.ToString(a[key],CultureInfo.InvariantCulture),Convert.ToString(b[key],CultureInfo.InvariantCulture),StringComparison.OrdinalIgnoreCase),"PC estimate differs from canonical NP source: "+key);
        }
        static void Pin(string folder,Dictionary<string,object> canonical,Dictionary<string,object> pc,string key,string file){
            object a,b;Need(canonical.TryGetValue(key,out a)&&pc.TryGetValue(key,out b)&&Convert.ToString(a)==Convert.ToString(b),"PC estimate raw seal differs: "+key);
            string expected=String.IsNullOrEmpty(Convert.ToString(a))?null:Convert.ToString(a);
            Need((expected==null||Regex.IsMatch(expected,@"\A[0-9A-Fa-f]{64}\z"))&&String.Equals(Hash(Path.Combine(folder,file)),expected,StringComparison.OrdinalIgnoreCase),"PC estimate raw evidence changed: "+file);
        }
        static Dictionary<string,object> PcEstimate(string folder,Dictionary<string,object> canonical,Origin origin){
            const string file="recording_start_pc_estimate.json";
            Need(Flag(canonical,"np_start_byte_sync_confirmed")&&Flag(canonical,"receipt_source_clock_mapping_flag")&&
                Convert.ToString(canonical["reference"])=="lowest_selected_probe_first_validated_start_byte"&&
                Convert.ToString(canonical["reference_quality"])=="producer_validated_start_byte"&&
                canonical.ContainsKey("network_correction_applied")&&canonical["network_correction_applied"] is bool&&!(bool)canonical["network_correction_applied"],"RTC canonical NP reference is unqualified.");
            bool usb=Flag(canonical,"usb_bound_first_sync"),sd=Flag(canonical,"sd_bound_first_sync");Need(usb^sd,"RTC canonical transport binding is unavailable.");
            Need(Flag(canonical,usb?"usb_device_rtc_mapping_selected":"sd_device_rtc_mapping_selected")&&
                Convert.ToString(canonical["pc_clock_estimate_file"])==file,"RTC canonical has no declared local PC estimate.");
            string expected=Convert.ToString(canonical["pc_clock_estimate_sha256"]),path=Path.Combine(folder,file);
            Need(File.Exists(path)&&new FileInfo(path).Length<=1048576,"PC estimate is missing or oversized.");
            byte[] bytes=File.ReadAllBytes(path);Need(bytes.Length<=1048576,"PC estimate is oversized.");
            string actual;using(var h=SHA256.Create())actual=BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","");
            Need(Regex.IsMatch(expected,@"\A[0-9A-Fa-f]{64}\z")&&String.Equals(actual,expected,StringComparison.OrdinalIgnoreCase),"PC estimate is missing, unsealed or changed.");
            string text=Encoding.UTF8.GetString(bytes);if(text.Length>0&&text[0]=='\ufeff')text=text.Substring(1);
            var pc=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);
            Same(canonical,pc,new[]{"schema","format","device_identity","logger_version","session_id","capture_generation","source_epoch","counter_domain","counter_hz",
                "reference","reference_quality","reference_source_tick","reference_source_hz","first_sync_receipt_crc32"});
            uint family=Convert.ToUInt32(pc["logger_version"]),session=Convert.ToUInt32(pc["session_id"]);NpFirstSyncReceipt receipt;string uid;ulong token;
            if(usb){
                Need(Flag(pc,"usb_bound_first_sync")&&!Flag(pc,"sd_bound_first_sync"),"PC estimate transport differs.");
                Same(canonical,pc,new[]{"usb_expected_token_hex","usb_host_epoch","usb_ap_hz","usb_source_hz","usb_profile_crc","usb_probe_mask"});
                Pin(folder,canonical,pc,"usb_journal_sha256",NpUsbTimingEvidence.FileName);Pin(folder,canonical,pc,"usb_receipt_file_sha256",NpFirstSyncUsbEvidence.ReceiptFile);
                Pin(folder,canonical,pc,"usb_clock_query_journal_sha256","acquisition_clock_queries.jsonl");
                var source=NpFirstSyncUsbEvidence.Read(folder);var stream=NpUsbTimingEvidence.Read(folder);
                Need(source!=null&&source.RtcEnabled&&source.FineQualified&&stream!=null&&source.Uid==stream.Uid&&source.Token==stream.Token&&source.Generation==stream.Generation&&
                    stream.Epoch==Convert.ToString(pc["usb_host_epoch"]),"Current USB source/PC epoch evidence is unavailable.");
                receipt=source.Receipt;uid=source.Uid;token=source.Token;
            }else{
                Need(Flag(pc,"sd_bound_first_sync")&&Flag(pc,"sd_fine_clock_mapping_qualified")&&!Flag(pc,"usb_bound_first_sync"),"PC estimate lacks strict SD fine-clock mapping.");
                Same(canonical,pc,new[]{"sd_bound_token_hex","sd_recorded_clock_crc32","sd_ap_hz","sd_profile_crc","sd_probe_mask"});
                Pin(folder,canonical,pc,"sd_fine_clock_sector_sha256",NpFineSdArchive.RawFile);
                var json=new JavaScriptSerializer();Need(pc.ContainsKey("sd_fine_clock_inputs")&&canonical.ContainsKey("sd_fine_clock_inputs")&&
                    json.Serialize(pc["sd_fine_clock_inputs"])==json.Serialize(canonical["sd_fine_clock_inputs"])&&
                    json.Serialize(pc["sd_fine_clock_inputs"])==json.Serialize(NpFirstSyncSdUtc.FineInputs(folder)),"SD PC exchange evidence set changed.");
                var raw=File.ReadAllBytes(Path.Combine(folder,"recorded_clock.bin"));Need(raw.Length==512&&BitConverter.ToUInt32(raw,508).ToString("X8")==Convert.ToString(pc["sd_recorded_clock_crc32"]),"SD clock seal changed.");
                var fine=NpFineClockWire.ReadCompanion(File.ReadAllBytes(Path.Combine(folder,NpFineSdArchive.RawFile)),raw,(int)family,session,Convert.ToUInt32(pc["sd_ap_hz"]),
                    Convert.ToString(pc["device_identity"]),UInt64.Parse(Convert.ToString(pc["sd_bound_token_hex"]),NumberStyles.HexNumber,CultureInfo.InvariantCulture));
                Need(fine!=null&&fine.FineCounter64,"Current SD source clock evidence is unavailable.");receipt=fine.Bound.Receipt;uid=fine.Bound.Uid;token=fine.Bound.Token;
            }
            Need(uid==Convert.ToString(pc["device_identity"])&&receipt.Family==family&&receipt.Session==session&&receipt.Generation==Convert.ToUInt32(pc["capture_generation"])&&
                receipt.SourceEpoch==Convert.ToUInt32(pc["source_epoch"])&&receipt.Domain==Convert.ToUInt32(pc["counter_domain"])&&receipt.CounterHz==Convert.ToUInt32(pc["counter_hz"])&&
                receipt.SourceHz==Convert.ToUInt32(pc["reference_source_hz"])&&receipt.ReferenceTick==Convert.ToUInt64(pc["reference_source_tick"])&&
                BitConverter.ToUInt32(receipt.Raw,324).ToString("X8")==Convert.ToString(pc["first_sync_receipt_crc32"])&&
                token.ToString("X16")==Convert.ToString(pc[usb?"usb_expected_token_hex":"sd_bound_token_hex"]),"PC estimate does not match current final NP source.");
            origin.PcFile=file;origin.PcHash=expected;return pc;
        }
        static RawSession Raw(string folder)
        {
            string current=folder,port=null,identity=null;var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);bool linked=false;
            for(int depth=0;depth<8;depth++){
                Need(visited.Add(current),"Cyclic GPIO raw-session link.");
                string events=Path.Combine(current,"gpio_rtc_events.txt"),sync=Path.Combine(current,"gpio_rtc_sync.csv");
                if(File.Exists(events)&&File.Exists(sync)){
                    Dictionary<string,object> metadata=null;string session=Path.Combine(current,"gpio_rtc_session.json");
                    if(File.Exists(session))metadata=ReadJson(session);
                    if(linked)Need(metadata!=null&&Convert.ToString(metadata["format"])=="wild.gpio_rtc"&&
                        String.Equals(Convert.ToString(metadata["port"]),port,StringComparison.OrdinalIgnoreCase)&&
                        String.Equals(Convert.ToString(metadata["pnp_device_id"]),identity,StringComparison.OrdinalIgnoreCase),"GPIO link does not match its original session identity.");
                    return new RawSession {Folder=current,Events=events,Sync=sync,Metadata=metadata,Linked=linked};
                }
                string link=Path.Combine(current,"gpio_rtc_link.json");Need(File.Exists(link),"GPIO raw events/queries missing.");var d=ReadJson(link);
                string nextPort=Convert.ToString(d["port"]),nextIdentity=Convert.ToString(d["pnp_device_id"]);
                Need(Flag(d,"continuous_across_neural_interruptions")&&!String.IsNullOrWhiteSpace(nextPort)&&!String.IsNullOrWhiteSpace(nextIdentity),"Unqualified GPIO continuation link.");
                if(linked)Need(String.Equals(port,nextPort,StringComparison.OrdinalIgnoreCase)&&String.Equals(identity,nextIdentity,StringComparison.OrdinalIgnoreCase),"GPIO identity changed in continuation links.");
                port=nextPort;identity=nextIdentity;string source=null;
                foreach(var pair in new[]{new[]{"session_file","gpio_rtc_session.json"},new[]{"events_file","gpio_rtc_events.txt"},new[]{"clock_queries_file","gpio_rtc_sync.csv"}}){
                    string path=Path.GetFullPath(Path.Combine(current,Convert.ToString(d[pair[0]])));
                    Need(String.Equals(Path.GetFileName(path),pair[1],StringComparison.OrdinalIgnoreCase),"GPIO link has an unexpected raw filename.");
                    string parent=Path.GetDirectoryName(path);if(source==null)source=parent;else Need(String.Equals(source,parent,StringComparison.OrdinalIgnoreCase),"GPIO link mixes raw sessions.");
                }
                current=source;linked=true;
            }
            throw new InvalidDataException("GPIO continuation link depth exceeds eight sessions.");
        }
        static Origin Canonical(string folder,string epoch)
        {
            var origin=new Origin();string path=Path.Combine(folder,NpRecordingUtc.FileName);
            try{
                Need(File.Exists(path),"Canonical NP recording start is unavailable.");byte[] bytes=File.ReadAllBytes(path);Need(bytes.Length<=1048576,"Oversized canonical NP timing metadata.");
                string text=Encoding.UTF8.GetString(bytes);if(text.Length>0&&text[0]=='\ufeff')text=text.Substring(1);
                var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);
                using(var hash=SHA256.Create())origin.Hash=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");
                origin.CanonicalClock=Convert.ToString(d["clock_reference"]);origin.PcFile=NpRecordingUtc.FileName;origin.PcHash=origin.Hash;
                if(origin.CanonicalClock=="device_hardware_rtc"){origin.PcFile=origin.PcHash=null;d=PcEstimate(folder,d,origin);}
                bool usb=Flag(d,"usb_bound_first_sync"),sd=Flag(d,"sd_bound_first_sync")&&Flag(d,"sd_fine_clock_mapping_qualified");
                Need(Convert.ToInt32(d["schema"])==1&&Convert.ToString(d["format"])=="neuropixels.recording_start_utc"&&
                    Flag(d,"utc_valid")&&Flag(d,"np_start_byte_sync_confirmed")&&Flag(d,"reference_clock_mapping_qualified")&&
                    Flag(d,"receipt_source_clock_mapping_flag")&&(usb^sd)&&
                    Convert.ToString(d["reference"])=="lowest_selected_probe_first_validated_start_byte"&&Convert.ToString(d["reference_quality"])=="producer_validated_start_byte"&&
                    Convert.ToString(d["clock_reference"])=="pc_wall_clock"&&d.ContainsKey("network_correction_applied")&&d["network_correction_applied"] is bool&&!(bool)d["network_correction_applied"],"Canonical NP STARTBYTE/PC mapping is not qualified.");
                if(sd)Need(Regex.IsMatch(Convert.ToString(d["sd_bound_token_hex"]),@"\A[0-9A-Fa-f]{16}\z")&&Convert.ToString(d["sd_bound_token_hex"])!="0000000000000000"&&
                    Regex.IsMatch(Convert.ToString(d["sd_fine_clock_sector_sha256"]),@"\A[0-9A-Fa-f]{64}\z")&&
                    Convert.ToUInt32(d["counter_domain"])!=0&&Convert.ToUInt32(d["counter_hz"])>=1000000,"Canonical SD fine-clock binding is unavailable.");
                object pcEpoch;origin.Epoch=d.TryGetValue("pc_host_epoch",out pcEpoch)&&!String.IsNullOrWhiteSpace(Convert.ToString(pcEpoch))?Convert.ToString(pcEpoch):
                    d.ContainsKey("usb_host_epoch")?Convert.ToString(d["usb_host_epoch"]):null;
                Need(!String.IsNullOrWhiteSpace(origin.Epoch)&&origin.Epoch==epoch,"Canonical NP and GPIO do not have the same evidenced PC epoch.");
                origin.Identity=Convert.ToString(d["device_identity"]);origin.Session=Convert.ToUInt32(d["session_id"]);origin.Generation=Convert.ToUInt32(d["capture_generation"]);
                Need(Regex.IsMatch(origin.Identity,@"\A[0-9A-Fa-f]{24}\z")&&origin.Identity!=new string('0',24)&&!origin.Identity.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase)&&
                    origin.Session!=0&&origin.Generation!=0&&(!Flag(d,"usb_bound_first_sync")||origin.Session==origin.Generation)&&Convert.ToUInt32(d["source_epoch"])!=0&&
                    (Convert.ToInt32(d["logger_version"])==1||Convert.ToInt32(d["logger_version"])==2),"Canonical NP capture identity is unavailable.");
                origin.Lower=Convert.ToDecimal(d["utc_lower_us"],CultureInfo.InvariantCulture);origin.Upper=Convert.ToDecimal(d["utc_upper_us"],CultureInfo.InvariantCulture);
                Need(origin.Lower>=1577836800000000m&&origin.Upper<=4102444800000000m&&origin.Lower<=origin.Upper,"Invalid canonical NP clock interval.");origin.Valid=true;
            }catch(Exception error){origin.Error=error.Message;}
            return origin;
        }
        internal static string Correct(string folder,bool replace=false)
        {
            folder=Path.GetFullPath(folder);string target=Path.Combine(folder,CsvName);RawSession rawSession;
            if(File.Exists(target)&&!replace)throw new IOException("GPIO calibrated timestamps already exist; use ReplaceDerived to regenerate them.");
            try{rawSession=Raw(folder);}catch(Exception error){return WriteReport(folder,new{schema=1,status="unavailable",reason=error.Message});}
            string events=rawSession.Events,sync=rawSession.Sync;
            var queries=new Dictionary<uint,Query>();var points=new List<Query>();
            using(var reader=new StreamReader(sync)) {
                string[] header=(reader.ReadLine()??"").Split(',');int epoch=Array.IndexOf(header,"host_epoch"),pc=Array.IndexOf(header,"host_pc_unix_us");
                if(epoch<0||pc<0)return WriteReport(folder,new{schema=1,status="unavailable",reason="Legacy GPIO log has no shared QPC/PC observations."});
                string line;while((line=reader.ReadLine())!=null) {
                    var c=line.Split(',');uint id;if(c.Length<=Math.Max(epoch,pc)||!UInt32.TryParse(c[1],out id)||String.IsNullOrEmpty(c[pc]))continue;
                    if(c[0]=="query")queries[id]=new Query {Send=Number(c[pc]),Epoch=c[epoch]};
                    else if(c[0]=="reply"&&c[2]!=""&&c[4]!="") {
                        Query q;if(!queries.TryGetValue(id,out q))continue;queries.Remove(id);q.Receive=Number(c[pc]);q.Device=Number(c[4]);
                        if(q.Epoch!=c[epoch]||!Finite(q.Send)||!Finite(q.Receive)||!Finite(q.Device)||q.Send<1577836800000000||q.Receive>4102444800000000||
                            q.Receive<q.Send||q.Receive-q.Send>5000000||q.Device%1!=0||q.Device<0||q.Device>253402300799999)continue;
                        if(points.Count>0&&(q.Device<points[points.Count-1].Device||q.Send<points[points.Count-1].Send))
                            return WriteReport(folder,new{schema=1,status="unavailable",reason="GPIO RTC reset/clock reversal requires separate epochs; raw observations retained."});
                        if(points.Count>0&&q.Device==points[points.Count-1].Device){if(q.Receive-q.Send<points[points.Count-1].Receive-points[points.Count-1].Send)points[points.Count-1]=q;}
                        else points.Add(q);
                    }
                }
            }
            if(points.Count<2||points.Select(p=>p.Epoch).Distinct().Count()!=1)return WriteReport(folder,new{schema=1,status="unavailable",reason="Insufficient consistent shared-PC GPIO clock queries."});
            if(rawSession.Linked){try{var pcClock=(Dictionary<string,object>)rawSession.Metadata["pc_clock"];
                Need(Convert.ToString(pcClock["host_epoch"])==points[0].Epoch,"GPIO linked session and raw queries disagree on the PC epoch.");
            }catch(Exception error){return WriteReport(folder,new{schema=1,status="unavailable",reason=error.Message});}}
            var origin=Canonical(folder,points[0].Epoch);
            string temporary=target+"."+Guid.NewGuid().ToString("N")+".partial";long count=0,mapped=0,relativeMapped=0;int at=0;
            try {
                using(var output=new StreamWriter(temporary,false,new UTF8Encoding(false))) {
                    output.WriteLine("event_index,channel,sequence,device_rtc_ms,pc_unix_seconds,pc_lower_unix_seconds,pc_upper_unix_seconds,host_epoch,status,np_start_relative_seconds,np_start_relative_lower_seconds,np_start_relative_upper_seconds,np_start_reference_status");
                    foreach(string line in File.ReadLines(events)) {
                        if(String.IsNullOrWhiteSpace(line))continue;
                        string device=EventValue(line,"DeviceMs"),sequence=EventValue(line,"Sequence"),channel=EventValue(line,"Data"),raw=EventValue(line,"PcClock");
                        double d;uint seq;byte ch;if(!Double.TryParse(device,NumberStyles.None,CultureInfo.InvariantCulture,out d)||!UInt32.TryParse(sequence,out seq)||!Byte.TryParse(channel,out ch))throw new InvalidDataException("Malformed GPIO event at row "+count);
                        while(at+1<points.Count&&points[at+1].Device<d)at++;
                        Query a=points[at],b=at+1<points.Count?points[at+1]:null;
                        bool valid=raw==a.Epoch&&b!=null&&d>=a.Device&&d<=b.Device&&b.Device-a.Device>1&&b.Device-a.Device<=10000;
                        string values=",,,",status="clock_not_bracketed",relative=",,,np_start_unavailable";
                        if(valid) {
                            double f=(d-a.Device)/(b.Device-a.Device);
                            // Both query and event use integer device RTC milliseconds.
                            // Their unknown sub-ms phases can differ by up to 1 ms.
                            double quantum=Math.Max(1000,(b.Receive-a.Send)/(b.Device-a.Device-1));
                            double lo=a.Send+f*(b.Send-a.Send)-quantum,hi=a.Receive+f*(b.Receive-a.Receive)+quantum;
                            values=F((lo+hi)/2000000)+","+F(lo/1000000)+","+F(hi/1000000)+",";status="mapped";mapped++;
                            if(origin.Valid){decimal lower=Math.Floor((decimal)lo)-origin.Upper,upper=Math.Ceiling((decimal)hi)-origin.Lower;
                                relative=F((lower+upper)/2000000m)+","+F(lower/1000000m)+","+F(upper/1000000m)+",mapped";relativeMapped++;}
                        }
                        output.WriteLine(count+","+ch+","+seq+","+device+","+values+(raw??"")+","+status+","+relative);count++;
                    }
                }
                if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
            return WriteReport(folder,new{schema=1,status="written",file=CsvName,events=count,mapped_events=mapped,unmapped_events=count-mapped,clock_queries=points.Count,
                host_epoch=points[0].Epoch,reference="Same QPC-to-PC Unix axis as NP derived timestamps; network correction separate.",
                raw_source_folder=rawSession.Folder,continued_raw_session=rawSession.Linked,
                np_relative_mapped_events=relativeMapped,np_reference=new {file=NpRecordingUtc.FileName,sha256=origin.Hash,valid=origin.Valid,reason=origin.Error,
                    device_identity=origin.Identity,session_id=origin.Valid?(uint?)origin.Session:null,capture_generation=origin.Valid?(uint?)origin.Generation:null,host_epoch=origin.Epoch,
                    canonical_clock_reference=origin.CanonicalClock,pc_origin_file=origin.PcFile,pc_origin_sha256=origin.PcHash},
                np_relative_mapping="Event PC interval minus the producer-qualified canonical STARTBYTE PC interval; includes both uncertainties. No first-receive/Start-click substitution or direct hardware lock is inferred.",
                mapping="Linear interpolation between bracketing device/PC query intervals, maximum 10-second query gap; no extrapolation.",
                limitation="Conditional clock mapping with integer-RTC phase uncertainty. Physical GPIO edge/IRQ and probe ADC/filter/receiver latency are not measured."});
        }
        static string WriteReport(string folder,object value){
            string text=new JavaScriptSerializer().Serialize(value),path=Path.Combine(folder,ReportName),temporary=path+"."+Guid.NewGuid().ToString("N")+".partial";
            try{File.WriteAllText(temporary,text);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}return text;
        }
        static void RunAutomatic(string folder,bool replace){try{Correct(folder,replace);}catch(Exception ex){try{WriteReport(folder,new{schema=1,status="unavailable",reason=ex.Message});}catch{}}}
        internal static void Automatic(string folder){RunAutomatic(folder,false);}
        internal static void Queue(string folder)
        {
            Enqueue(folder,false);
        }
        internal static void QueueCompleted(string folder)
        {
            if(String.IsNullOrWhiteSpace(folder))return;
            if(!(File.Exists(Path.Combine(folder,"gpio_rtc_events.txt"))&&File.Exists(Path.Combine(folder,"gpio_rtc_sync.csv")))&&
                !File.Exists(Path.Combine(folder,"gpio_rtc_link.json")))return;
            Enqueue(folder,true);
        }
        static void Enqueue(string folder,bool replace)
        {
            folder=Path.GetFullPath(folder);
            if(!replace&&File.Exists(Path.Combine(folder,CsvName)))return;
            Pending state;
            lock(queueGate){
                if(queued.TryGetValue(folder,out state)){
                    if(replace){state.Again=true;state.Replace=true;}return;
                }
                state=new Pending {Replace=replace};queued.Add(folder,state);
            }
            ThreadPool.QueueUserWorkItem(_=>{
                while(true){
                    bool runReplace;lock(queueGate){runReplace=state.Replace;state.Again=false;state.Replace=false;}
                    try{WriteReport(folder,new{schema=1,status="processing",reference="Shared NP/GPIO PC epoch; raw files preserved."});}catch{}
                    RunAutomatic(folder,runReplace);
                    lock(queueGate){if(state.Again)continue;queued.Remove(folder);return;}
                }
            });
        }
    }
}
