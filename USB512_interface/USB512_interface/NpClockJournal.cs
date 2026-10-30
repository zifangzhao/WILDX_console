using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace USB512_interface {
 // Optional, low-rate timing evidence. No device clock changes or RAW rewrites.
 internal sealed class NpClockJournal : IDisposable {
  readonly long hostTick=NpHostClock.OriginTicks,hostUtc=NpHostClock.OriginUtcUs;
  readonly string identity,session,epoch=NpHostClock.Epoch,folder;
  readonly List<NpClockObservation> observations=new List<NpClockObservation>();
  StreamWriter csv;long lastFitTick=-2000000;bool disposed;NpBleClockEvidence evidence;
  internal string Error {get;private set;}
  internal NpClockJournal(string directory,string deviceIdentity,string captureSession) {
   folder=directory;identity=deviceIdentity;session=captureSession;
   try {
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder,"pc_clock.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(NpHostClock.Receipt));
    csv=new StreamWriter(Path.Combine(folder,"clock_observations.csv"),false,new UTF8Encoding(false));
    csv.WriteLine("device_identity,session,host_epoch,generation,device_ms,host_monotonic_us,host_utc_us,uncertainty_us,kind");
   } catch(Exception ex){Error=ex.Message;}
  }
  internal long NowMicroseconds {get{return (long)((Stopwatch.GetTimestamp()-hostTick)*1000000.0/Stopwatch.Frequency);}}
  internal void Add(uint generation,uint deviceMs,long midpointUs,double uncertaintyUs,string kind) {
   if(disposed || csv==null)return;
   uncertaintyUs=Math.Max(1000,uncertaintyUs);
   var o=new NpClockObservation(identity,session,generation,deviceMs,midpointUs,hostUtc+midpointUs,uncertaintyUs,epoch);
   try {
    csv.WriteLine(String.Join(",",new[]{identity,session,epoch,generation.ToString(),deviceMs.ToString(),
     midpointUs.ToString(),o.HostUtcMicroseconds.ToString(),uncertaintyUs.ToString("R",CultureInfo.InvariantCulture),kind}));
    // Retain one point every two seconds for low-rate offline fitting;
    // every raw observation remains on disk. No per-sample fit in capture.
    if(midpointUs-lastFitTick>=2000000){observations.Add(o);lastFitTick=midpointUs;}
   } catch(Exception ex){Error=ex.Message;try{csv.Dispose();}catch{}csv=null;}
  }
  internal NpBleClockAttempt BeginExchange(byte[] request,long qpc,int connectionEpoch,NpBleClockContext context) {
   try {if(disposed)return null;if(evidence==null)evidence=new NpBleClockEvidence(folder,identity,connectionEpoch);return evidence.Begin(request,qpc,context);}catch{return null;}
  }
  internal void FinishExchange(NpBleClockAttempt attempt,byte[] response,long qpc,Exception error) {if(evidence!=null)evidence.Finish(attempt,response,qpc,error);}
  internal void ClockGap(NpBleClockContext context,string reason,long qpc) {if(evidence!=null)evidence.Gap(context,reason,qpc);}
  internal void ClockClosed(long qpc,string reason) {if(evidence!=null)evidence.Close(qpc,reason);}
  public void Dispose() {
   if(disposed)return;disposed=true;if(evidence!=null)evidence.Dispose();
   try {
    if(csv!=null)csv.Dispose();
    var lines=new List<string>{NpClockFitResult.Limitation,"raw_data_changed=false","device_identity="+identity,
     "identity_scope=exact USB MCU UID, or BLE transport address only where MCU UID is unavailable",
     "raw_exchange_evidence_error="+(evidence==null?"not_enabled":evidence.Error??"none"),
     "formula=UTC_us = reference_utc_us + (unwrapped_device_ms - first_device_ms) * slope_us_per_ms",
     "fit_coverage_only=true","session="+session,"host_epoch="+epoch,"journal_error="+(Error??"none")};
    foreach(var r in NpClockFit.FitGroups(observations)) {
     lines.Add("generation="+r.ClockGeneration+"; valid="+r.Success+"; error="+(r.Error??"none")+
      "; first_device_ms="+r.FirstDeviceMilliseconds+"; last_device_ms="+r.LastDeviceMilliseconds+
      "; reference_utc_us="+r.ReferenceUtcMicroseconds.ToString("R",CultureInfo.InvariantCulture)+
      "; slope_us_per_ms="+r.SlopeMicrosecondsPerMillisecond.ToString("R",CultureInfo.InvariantCulture)+
      "; drift_ppm="+r.DriftPpm.ToString("R",CultureInfo.InvariantCulture)+
      "; rms_residual_us="+r.RmsResidualMicroseconds.ToString("R",CultureInfo.InvariantCulture)+
      "; max_uncertainty_us="+r.MaxUncertaintyMicroseconds.ToString("R",CultureInfo.InvariantCulture));
    }
    File.WriteAllLines(Path.Combine(folder,"clock_linear_fit.txt"),lines);
   }catch(Exception ex){Error=ex.Message;}
  }
 }
}
