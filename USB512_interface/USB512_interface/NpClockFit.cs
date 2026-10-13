using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace USB512_interface {
 // Offline observations only. HostUtcMicroseconds must share a retained
 // monotonic/UTC anchor, not independent wall-clock receipt timestamps.
 internal sealed class NpClockObservation {
  internal readonly string DeviceUid,CaptureSession,HostClockEpoch,Failure;
  internal readonly uint ClockGeneration,DeviceTickMs;
  internal readonly long HostMonotonicMicroseconds,HostUtcMicroseconds;
  internal readonly double UncertaintyMicroseconds;
  internal readonly bool Discontinuity;
  internal NpClockObservation(string uid,string session,uint generation,uint tick,long monotonicUs,long utcUs,
   double uncertaintyUs,string hostClockEpoch,string failure=null,bool discontinuity=false) {
   DeviceUid=uid;CaptureSession=session;ClockGeneration=generation;DeviceTickMs=tick;
   HostMonotonicMicroseconds=monotonicUs;HostUtcMicroseconds=utcUs;UncertaintyMicroseconds=uncertaintyUs;
   HostClockEpoch=hostClockEpoch;Failure=failure;Discontinuity=discontinuity;
  }
 }
 internal sealed class NpClockFitPoint {
  internal readonly NpClockObservation Observation;
  internal readonly ulong UnwrappedDeviceMilliseconds;
  internal readonly double ResidualMicroseconds,UncertaintyMicroseconds;
  internal NpClockFitPoint(NpClockObservation observation,ulong tick,double residual,double uncertainty) {
   Observation=observation;UnwrappedDeviceMilliseconds=tick;ResidualMicroseconds=residual;UncertaintyMicroseconds=uncertainty;
  }
 }
 internal sealed class NpClockFitResult {
  internal string DeviceUid,CaptureSession,HostClockEpoch,Error;
  internal uint ClockGeneration;
  internal int ObservationCount,DistinctDeviceTicks;
  internal ulong FirstDeviceMilliseconds,LastDeviceMilliseconds;
  internal double ReferenceUtcMicroseconds,SlopeMicrosecondsPerMillisecond,OffsetMicroseconds;
  internal double DriftPpm=Double.NaN,RmsResidualMicroseconds,MaxResidualMicroseconds,MaxUncertaintyMicroseconds,SpanSeconds;
  internal ReadOnlyCollection<NpClockFitPoint> Points=Array.AsReadOnly(new NpClockFitPoint[0]);
  internal ReadOnlyCollection<NpClockObservation> FailedObservations=Array.AsReadOnly(new NpClockObservation[0]);
  internal bool Success {get {return Error==null;}}
  internal const string Limitation="Offline estimated control-clock mapping, not a bound on physical NP/IMU alignment; residuals do not prove 1 ms accuracy.";
  internal bool TryMapDeviceMilliseconds(ulong tick,out double utcUs) {
   utcUs=Double.NaN;if(!Success || tick<FirstDeviceMilliseconds || tick>LastDeviceMilliseconds)return false;
   utcUs=ReferenceUtcMicroseconds+(tick-FirstDeviceMilliseconds)*SlopeMicrosecondsPerMillisecond;return true;
  }
 }
 internal static class NpClockFit {
  internal const uint MinimumSpanMilliseconds=1000,DriftSpanMilliseconds=60000;
  internal static List<NpClockFitResult> FitGroups(IEnumerable<NpClockObservation> observations) {
   if(observations==null)throw new ArgumentNullException("observations");
   var groups=new Dictionary<Tuple<string,string,uint,string>,List<NpClockObservation>>();
   foreach(var o in observations) {
    if(o==null)throw new ArgumentException("Null clock observation.","observations");
    var key=Tuple.Create(o.DeviceUid,o.CaptureSession,o.ClockGeneration,o.HostClockEpoch);
    List<NpClockObservation> group;if(!groups.TryGetValue(key,out group))groups.Add(key,group=new List<NpClockObservation>());group.Add(o);
   }
   var results=new List<NpClockFitResult>();foreach(var group in groups.Values)results.Add(Fit(group));return results;
  }
  internal static NpClockFitResult Fit(IList<NpClockObservation> observations) {
   var r=new NpClockFitResult();var good=new List<NpClockObservation>();var failed=new List<NpClockObservation>();
   if(observations==null || observations.Count==0){r.Error="No observations.";return r;}
   r.ObservationCount=observations.Count;var identity=observations[0];
   if(identity==null){r.Error="Null observation.";return r;}
   r.DeviceUid=identity.DeviceUid;r.CaptureSession=identity.CaptureSession;r.ClockGeneration=identity.ClockGeneration;r.HostClockEpoch=identity.HostClockEpoch;
   if(String.IsNullOrWhiteSpace(r.DeviceUid) || String.IsNullOrWhiteSpace(r.CaptureSession) || String.IsNullOrWhiteSpace(r.HostClockEpoch)) {
    r.Error="Exact device, capture session and host clock epoch are required.";return r;
   }
   foreach(var o in observations) {
    if(o==null || o.DeviceUid!=r.DeviceUid || o.CaptureSession!=r.CaptureSession || o.ClockGeneration!=r.ClockGeneration || o.HostClockEpoch!=r.HostClockEpoch) {
     r.Error="Mixed identity/session/clock generations; fit separately.";return r;
    }
    if(o.Discontinuity){r.Error="Explicit clock discontinuity; start a new epoch.";return r;}
    if(o.Failure!=null){failed.Add(o);continue;}
    if(o.HostMonotonicMicroseconds<0 || o.HostUtcMicroseconds<=0 || Double.IsNaN(o.UncertaintyMicroseconds) ||
     Double.IsInfinity(o.UncertaintyMicroseconds) || o.UncertaintyMicroseconds<0){r.Error="Invalid clock observation.";return r;}
    good.Add(o);
   }
   r.FailedObservations=failed.AsReadOnly();
   if(good.Count<3){r.Error="At least three successful observations are required.";return r;}
   var ticks=new ulong[good.Count];var x=new double[good.Count];var y=new double[good.Count];
   ticks[0]=good[0].DeviceTickMs;r.FirstDeviceMilliseconds=ticks[0];r.DistinctDeviceTicks=1;
   double meanX=0,meanY=0;
   for(int i=0;i<good.Count;i++) {
    if(i>0) {
     long hostDelta=good[i].HostMonotonicMicroseconds-good[i-1].HostMonotonicMicroseconds;
     long utcDelta=good[i].HostUtcMicroseconds-good[i-1].HostUtcMicroseconds;
     uint deviceDelta=unchecked(good[i].DeviceTickMs-good[i-1].DeviceTickMs);
     if(hostDelta<0 || hostDelta>=0x80000000L*1000 || deviceDelta>=0x80000000U) {
      r.Error="Reset, nonchronological observations or ambiguous device tick wrap.";return r;
     }
     if(Math.Abs((double)utcDelta-hostDelta)>1000) {r.Error="Host wall-clock step/discontinuity.";return r;}
     // Gross rate steps/reset aliases exceed this deliberately loose 1% gate.
     // Small resets indistinguishable from tick progression require an explicit epoch.
     double allowance=Math.Max(1000,good[i].UncertaintyMicroseconds)+Math.Max(1000,good[i-1].UncertaintyMicroseconds)+hostDelta*0.01;
     if(Math.Abs(deviceDelta*1000.0-hostDelta)>allowance){r.Error="Device clock step/reset inconsistent with host interval.";return r;}
     ticks[i]=ticks[i-1]+deviceDelta;if(deviceDelta!=0)r.DistinctDeviceTicks++;
    }
    x[i]=ticks[i]-ticks[0];y[i]=good[i].HostUtcMicroseconds-good[0].HostUtcMicroseconds;meanX+=x[i];meanY+=y[i];
   }
   r.LastDeviceMilliseconds=ticks[ticks.Length-1];r.SpanSeconds=(r.LastDeviceMilliseconds-r.FirstDeviceMilliseconds)/1000.0;
   if(r.DistinctDeviceTicks<3 || r.LastDeviceMilliseconds-r.FirstDeviceMilliseconds<MinimumSpanMilliseconds) {
    r.Error="Three distinct device ticks spanning at least one second are required.";return r;
   }
   meanX/=good.Count;meanY/=good.Count;double xx=0,xy=0;
   for(int i=0;i<good.Count;i++){double dx=x[i]-meanX;xx+=dx*dx;xy+=dx*(y[i]-meanY);}
   r.SlopeMicrosecondsPerMillisecond=xy/xx;
   if(r.SlopeMicrosecondsPerMillisecond<=0 || Double.IsInfinity(r.SlopeMicrosecondsPerMillisecond) || Double.IsNaN(r.SlopeMicrosecondsPerMillisecond)) {
    r.Error="Invalid fitted clock rate.";return r;
   }
   double intercept=meanY-r.SlopeMicrosecondsPerMillisecond*meanX;
   r.ReferenceUtcMicroseconds=good[0].HostUtcMicroseconds+intercept;
   r.OffsetMicroseconds=r.ReferenceUtcMicroseconds-r.FirstDeviceMilliseconds*1000.0;
   if(r.SpanSeconds>=DriftSpanMilliseconds/1000.0)r.DriftPpm=(r.SlopeMicrosecondsPerMillisecond/1000.0-1)*1000000;
   var points=new List<NpClockFitPoint>();double squares=0;
   for(int i=0;i<good.Count;i++) {
    double residual=y[i]-(intercept+r.SlopeMicrosecondsPerMillisecond*x[i]),uncertainty=Math.Max(1000,good[i].UncertaintyMicroseconds);
    points.Add(new NpClockFitPoint(good[i],ticks[i],residual,uncertainty));squares+=residual*residual;
    r.MaxResidualMicroseconds=Math.Max(r.MaxResidualMicroseconds,Math.Abs(residual));r.MaxUncertaintyMicroseconds=Math.Max(r.MaxUncertaintyMicroseconds,uncertainty);
    if(Math.Abs(residual)>uncertainty)r.Error="Linear model contradicts an observation uncertainty; split/check clock epochs.";
   }
   r.Points=points.AsReadOnly();r.RmsResidualMicroseconds=Math.Sqrt(squares/good.Count);return r;
  }
 }
}
