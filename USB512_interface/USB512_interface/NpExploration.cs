using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    internal sealed class NpExplorationSelection
    {
        internal int Port,Shank,Bank,FirstSite,LastSite,NewSites,OverlapSites;
        internal string Json,Imro,ConfigurationId;
        internal byte[] Payload;
    }
    internal sealed class NpExplorationStep
    {
        internal int Index;
        internal NpExplorationSelection[] Probes;
        internal string Label {get{return "scan"+(Index+1).ToString("D4")+"_s"+Probes[0].Shank+"_sites"+Probes[0].FirstSite.ToString("D4")+"-"+Probes[0].LastSite.ToString("D4");}}
    }
    internal sealed class NpExplorationPlan
    {
        internal string DeviceUid;
        internal int Mask;
        internal NpExplorationStep[] Steps;
        static int Number(Dictionary<string,object> value,string key){return Convert.ToInt32(value[key]);}
        static string ReadFile(string root,object value,string extension,int max){
            string name=value as string;
            if(String.IsNullOrEmpty(name)||Path.GetFileName(name)!=name||!name.EndsWith(extension,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid scan-plan file name.");
            var f=new FileInfo(Path.Combine(root,name));if(!f.Exists||f.Length==0||f.Length>max)throw new InvalidDataException("Scan-plan file missing or oversized.");return f.FullName;
        }
        internal static List<NpExplorationSelection> LoadProbe(string root,int port,bool np2){
            var serializer=new JavaScriptSerializer{MaxJsonLength=2*1024*1024};string manifest=ReadFile(root,"plan.json",".json",128*1024);
            var doc=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(manifest));
            if(Number(doc,"schemaVersion")!=1||Number(doc,"connector")!=port||(string)doc["numbering"]!="zero-based"||
                (string)doc["profile"]!=(np2?"Np2Multi":"Np1"))throw new InvalidDataException("Unexpected probe scan model/connector.");
            var result=new List<NpExplorationSelection>();var steps=doc["steps"] as System.Collections.IEnumerable;
            if(steps==null)throw new InvalidDataException("Scan has no steps.");
            foreach(Dictionary<string,object> s in steps){
                int first=Number(s,"firstSite"),last=Number(s,"lastSite"),shank=Number(s,"shank");
                if(result.Count>=16||Number(s,"index")!=result.Count||first<0||last-first!=383||last>=(np2?1280:960)||shank<0||shank>=(np2?4:1))throw new InvalidDataException("Invalid scan window.");
                result.Add(new NpExplorationSelection{Port=port,Shank=shank,Bank=Number(s,"bank"),FirstSite=first,LastSite=last,NewSites=Number(s,"newSites"),OverlapSites=Number(s,"overlapSites"),
                    Json=File.ReadAllText(ReadFile(root,s["configuration"],".npconfig.json",1024*1024)),Imro=File.ReadAllText(ReadFile(root,s["imro"],".imro",128*1024)),
                    Payload=File.ReadAllBytes(ReadFile(root,s["selection"],".selection.bin",2048)),ConfigurationId=(string)s["configurationId"]});
            }
            if(result.Count!=(np2?16:3))throw new InvalidDataException("Incomplete probe scan.");return result;
        }
        internal static NpExplorationPlan Combine(string uid,Dictionary<int,List<NpExplorationSelection>> probes){
            if(String.IsNullOrWhiteSpace(uid)||probes==null||probes.Count==0)throw new ArgumentException("Select a device and probes.");
            int count=probes.First().Value.Count;if(count==0||probes.Any(p=>p.Key<1||p.Key>3||p.Value.Count!=count))throw new InvalidDataException("Probe scan lengths differ.");
            var steps=new NpExplorationStep[count];for(int i=0;i<count;i++)steps[i]=new NpExplorationStep{Index=i,Probes=probes.OrderBy(p=>p.Key).Select(p=>p.Value[i]).ToArray()};
            return new NpExplorationPlan{DeviceUid=uid,Mask=probes.Keys.Sum(p=>1<<(p-1)),Steps=steps};
        }
        internal static void SaveJson(string path,object value){File.WriteAllText(path,new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Serialize(value));}
        internal object Describe(){return new{schemaVersion=1,numbering="zero-based",device_uid=DeviceUid,probe_mask=Mask,steps=Steps.Select(s=>new{step=s.Index+1,label=s.Label,probes=s.Probes.Select(p=>new{port=p.Port,shank=p.Shank,bank=p.Bank,first_site=p.FirstSite,last_site=p.LastSite,new_sites=p.NewSites,overlap_sites=p.OverlapSites,configuration_id=p.ConfigurationId})})};}
    }
    internal sealed class NpExplorationResult
    {
        internal bool Complete;internal string Folder,Message;
    }
    internal sealed class NpExplorationRun
    {
        internal readonly int Mask;
        internal readonly TaskCompletionSource<NpExplorationResult> Done=new TaskCompletionSource<NpExplorationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly long[] rows=new long[4];readonly int[] rates=new int[4];
        volatile string error;
        internal NpExplorationRun(int mask){Mask=mask;}
        internal void Observe(int probe,int count,int rate){
            if(probe<1||probe>3||(Mask&(1<<(probe-1)))==0||count<0||rate<=0){error="Unexpected probe/sample profile during exploration.";return;}
            int prior=Interlocked.CompareExchange(ref rates[probe],rate,0);
            if(prior!=0&&prior!=rate){error="Sample rate changed during exploration.";return;}
            Interlocked.Add(ref rows[probe],count);
        }
        internal double Seconds {get{
            if(error!=null)throw new IOException(error);double seconds=Double.MaxValue;
            for(int p=1;p<=3;p++)if((Mask&(1<<(p-1)))!=0){int hz=Volatile.Read(ref rates[p]);seconds=Math.Min(seconds,hz==0?0:Interlocked.Read(ref rows[p])/(double)hz);}
            return seconds==Double.MaxValue?0:seconds;
        }}
        internal object Counts(){return Enumerable.Range(1,3).Where(p=>(Mask&(1<<(p-1)))!=0).Select(p=>new{probe=p,ap_rows=Interlocked.Read(ref rows[p]),sample_rate_hz=Volatile.Read(ref rates[p])}).ToArray();}
    }
    internal static class NpExploration
    {
        // Configuration changes happen only after Done: the existing capture
        // owner has drained, finalized metadata, and closed native/file handles.
        internal static async Task Run(NpExplorationPlan plan,double seconds,Func<NpExplorationStep,Task<NpExplorationRun>> start,Action stop,Action<int,string> report,CancellationToken cancel)
        {
            if(seconds<=0||Double.IsNaN(seconds)||Double.IsInfinity(seconds))throw new ArgumentOutOfRangeException("seconds");
            foreach(var step in plan.Steps){
                cancel.ThrowIfCancellationRequested();var run=await start(step);bool stopSent=false;Exception stopError=null;
                Action requestStop=()=>{if(stopSent)return;stopSent=true;try{stop();}catch(Exception ex){stopError=ex;report(step.Index,"Stop failed; waiting for capture cleanup: "+ex.Message);}};
                try{
                    var startup=Stopwatch.StartNew();var stalled=Stopwatch.StartNew();double prior=0;
                    while(!run.Done.Task.IsCompleted&&run.Seconds<seconds){
                        cancel.ThrowIfCancellationRequested();double elapsed=run.Seconds;
                        if(elapsed>prior){prior=elapsed;stalled.Restart();}
                        if(elapsed==0&&startup.Elapsed.TotalSeconds>120)throw new TimeoutException("No samples from all probes within120seconds; scan stopped.");
                        if(elapsed>0&&stalled.Elapsed.TotalSeconds>15)throw new TimeoutException("Sample progress stalled; scan stopped.");
                        report(step.Index,elapsed==0?"Configuring / waiting for samples…":elapsed.ToString("F1")+" / "+seconds.ToString("F1")+" s captured");
                        await Task.Delay(100,cancel);
                    }
                    cancel.ThrowIfCancellationRequested();
                    if(run.Seconds<seconds)throw new IOException("Recording ended before its scan window completed.");
                    if(!run.Done.Task.IsCompleted){report(step.Index,"Stopping / finalizing…");requestStop();}
                    var result=await run.Done.Task;
                    if(stopError!=null)throw new IOException("Stop could not be confirmed cleanly.",stopError);
                    if(!result.Complete)throw new IOException("Scan stopped: "+result.Message);
                    report(step.Index,"Saved · "+result.Folder);
                }finally{
                    if(!run.Done.Task.IsCompleted){report(step.Index,"Waiting for recording to stop…");requestStop();await run.Done.Task;}
                }
            }
        }
    }
}
