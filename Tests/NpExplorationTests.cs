using System;using System.IO;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using USB512_interface;
class NpExplorationTests{
 static int checks;static void Need(bool b,string m){checks++;if(!b)throw new Exception(m);}
 static NpExplorationPlan Plan(){var items=new List<NpExplorationSelection>();for(int i=0;i<3;i++)items.Add(new NpExplorationSelection{Port=1,Shank=0,Bank=i,FirstSite=i*384,LastSite=i*384+383});return NpExplorationPlan.Combine("UID",new Dictionary<int,List<NpExplorationSelection>>{{1,items}});}
 static void Reject(Action a){bool caught=false;try{a();}catch(IOException){caught=true;}Need(caught,"Expected failure");}
 static async Task Tests(){
  var p=Plan();int starts=0,stops=0;bool closed=true;NpExplorationRun active=null;
  await NpExploration.Run(p,60,s=>{Need(closed,"New configuration before previous cleanup");closed=false;starts++;active=new NpExplorationRun(1);active.Observe(1,1200000,20000);return Task.FromResult(active);},()=>{stops++;Task.Run(async()=>{await Task.Delay(25);closed=true;active.Done.SetResult(new NpExplorationResult{Complete=true,Folder="bank"});});},(i,text)=>{},CancellationToken.None);
  Need(starts==3&&stops==3&&closed,"Full scan did not stop/save each bank once");
  var rows=new NpExplorationRun(3);rows.Observe(1,1200000,20000);Need(rows.Seconds==0,"Timer ran before all probes supplied samples");rows.Observe(2,600000,20000);Need(rows.Seconds==30,"Timer does not use slowest probe");rows.Observe(2,600000,20000);Need(rows.Seconds==60,"One-minute sample target incorrect");rows.Observe(1,1,15000);Reject(()=>{var x=rows.Seconds;});
  starts=stops=0;using(var cancel=new CancellationTokenSource()){
   try{await NpExploration.Run(p,60,s=>{starts++;active=new NpExplorationRun(1);return Task.FromResult(active);},()=>{stops++;active.Done.SetResult(new NpExplorationResult{Complete=false});},(i,text)=>cancel.Cancel(),cancel.Token);throw new Exception("Cancel ignored");}catch(OperationCanceledException){}
   Need(starts==1&&stops==1,"Cancellation started another bank or failed to stop");
  }
  starts=stops=0;try{await NpExploration.Run(p,60,s=>{starts++;active=new NpExplorationRun(1);active.Done.SetResult(new NpExplorationResult{Complete=false});return Task.FromResult(active);},()=>stops++,(i,t)=>{},CancellationToken.None);throw new Exception("Early completion ignored");}catch(IOException){}Need(starts==1&&stops==0,"Early failure advanced scan/replayed stop");
  starts=stops=0;try{await NpExploration.Run(p,60,s=>{starts++;active=new NpExplorationRun(1);active.Observe(1,1200000,20000);return Task.FromResult(active);},()=>{stops++;active.Done.SetResult(new NpExplorationResult{Complete=false,Message="disk failure"});},(i,t)=>{},CancellationToken.None);throw new Exception("Incomplete file accepted");}catch(IOException){}Need(starts==1&&stops==1,"File failure advanced scan");
  using(var c=new CancellationTokenSource()){c.Cancel();starts=0;try{await NpExploration.Run(p,60,s=>{starts++;return Task.FromResult(new NpExplorationRun(1));},()=>{},(i,t)=>{},c.Token);}catch(OperationCanceledException){}Need(starts==0,"Pre-cancelled scan started recording");}
  Console.WriteLine("PASS "+checks+" sample-timed orchestration/cancellation/cleanup/failure tests; no hardware");
 }
 static int Main(){try{Tests().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
