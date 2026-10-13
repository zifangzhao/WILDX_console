using System;
using System.Collections.Generic;
using USB512_interface;

// Source-only offline regression: no GUI, files, device calls or clock mutation.
internal static class TestNpClockFit {
 const string Uid="001A0004434250062039314E";
 const long Epoch=1770000000000000L;
 static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static NpClockObservation O(uint tick,long hostUs,long utcUs,double uncertainty=1000,string uid=Uid,string session="capture1",uint gen=1,
  string hostEpoch="host1",string failure=null,bool discontinuity=false) {
  return new NpClockObservation(uid,session,gen,tick,hostUs,utcUs,uncertainty,hostEpoch,failure,discontinuity);
 }
 static List<NpClockObservation> Line(uint start,double drift,int count=7,int intervalMs=20000) {
  var a=new List<NpClockObservation>();for(int i=0;i<count;i++) {
   long elapsed=(long)Math.Round(i*intervalMs*1000.0*(1+drift/1000000.0));
   a.Add(O(unchecked(start+(uint)(i*intervalMs)),elapsed,Epoch+elapsed));
  }return a;
 }
 static void Close(double a,double b,double tolerance,string why){Check(Math.Abs(a-b)<=tolerance,why+": "+a+" != "+b);}
 static void Main() {
  var a=Line(12345,25);var before=a.ToArray();var r=NpClockFit.Fit(a);
  Check(r.Success,"linear fit");Close(r.DriftPpm,25,0.000001,"drift");Close(r.ReferenceUtcMicroseconds,Epoch,0.25,"reference");
  Close(r.OffsetMicroseconds,Epoch-12345000,0.25,"nominal offset");Check(r.Points.Count==7 && r.DistinctDeviceTicks==7,"counts");
  Close(r.SpanSeconds,120,0.000001,"coverage");double mapped;
  Check(r.TryMapDeviceMilliseconds(12345+40000,out mapped),"covered map");Close(mapped,Epoch+40001000,0.25,"modeled UTC");
  Check(!r.TryMapDeviceMilliseconds(12344,out mapped) && Double.IsNaN(mapped),"no backward extrapolation");
  Check(!r.TryMapDeviceMilliseconds(r.LastDeviceMilliseconds+1,out mapped),"no forward extrapolation");
  for(int i=0;i<a.Count;i++)Check(Object.ReferenceEquals(a[i],before[i]) && a[i].DeviceTickMs==12345+(uint)(i*20000),"input immutable");
  Check(r.MaxResidualMicroseconds<0.0001 && r.MaxUncertaintyMicroseconds==1000,"residual/floor");
  a=Line(100,-40);int[] jitter={0,400,-300,100,-200,300,-100};
  for(int i=0;i<a.Count;i++){var old=a[i];a[i]=O(old.DeviceTickMs,old.HostMonotonicMicroseconds+jitter[i],old.HostUtcMicroseconds+jitter[i],5000);}
  r=NpClockFit.Fit(a);Check(r.Success && r.RmsResidualMicroseconds>100,"jitter retained");Close(r.DriftPpm,-40,4,"jitter drift");
  Check(r.Points[1].UncertaintyMicroseconds==5000,"per-point uncertainty");
  a=Line(UInt32.MaxValue-30000,0);r=NpClockFit.Fit(a);
  Check(r.Success && r.LastDeviceMilliseconds>UInt32.MaxValue,"ordinary wrap");Close(r.DriftPpm,0,0.000001,"wrap drift");
  a=Line(100000,0);a[3]=O(1,60000000,Epoch+60000000);Check(!NpClockFit.Fit(a).Success,"reset rejected");
  a=Line(100,0);a[1]=O(200,0x80000000L*1000,Epoch+0x80000000L*1000);Check(!NpClockFit.Fit(a).Success,"ambiguous gap");
  a=Line(100,0);a[2]=O(40100,39999999,Epoch+39999999);Check(NpClockFit.Fit(a).Success,"ordinary interval jitter");
  a[2]=O(40100,19999999,Epoch+19999999);Check(!NpClockFit.Fit(a).Success,"host order rejected");
  a=Line(100,0);a[2]=O(40100,40000000,Epoch+40100000);Check(!NpClockFit.Fit(a).Success,"wall step rejected");
  a=Line(100,0);a[2]=O(40100,40000000,Epoch+40000000,discontinuity:true);Check(!NpClockFit.Fit(a).Success,"explicit discontinuity");
  a=Line(100,0);var oldPoint=a[2];a[2]=O(oldPoint.DeviceTickMs,oldPoint.HostMonotonicMicroseconds,oldPoint.HostUtcMicroseconds,gen:2);
  Check(!NpClockFit.Fit(a).Success,"mixed generation rejected");
  var grouped=new List<NpClockObservation>();grouped.AddRange(Line(100,0));
  foreach(var o in Line(100,50))grouped.Add(O(o.DeviceTickMs,o.HostMonotonicMicroseconds,o.HostUtcMicroseconds,gen:2));
  foreach(var o in Line(100,20))grouped.Add(O(o.DeviceTickMs,o.HostMonotonicMicroseconds,o.HostUtcMicroseconds,session:"capture2"));
  foreach(var o in Line(100,10))grouped.Add(O(o.DeviceTickMs,o.HostMonotonicMicroseconds,o.HostUtcMicroseconds,uid:"001D00065942500B20313354"));
  foreach(var o in Line(100,5))grouped.Add(O(o.DeviceTickMs,o.HostMonotonicMicroseconds,o.HostUtcMicroseconds,hostEpoch:"host2"));
  var fits=NpClockFit.FitGroups(grouped);Check(fits.Count==5,"identity/session/gen/host epoch groups");foreach(var fit in fits)Check(fit.Success,"group fit");
  a=Line(100,0,3,250);Check(!NpClockFit.Fit(a).Success,"insufficient span");
  a=Line(100,0,3,2000);r=NpClockFit.Fit(a);Check(r.Success && Double.IsNaN(r.DriftPpm),"short span drift unavailable");
  a.Insert(1,a[0]);r=NpClockFit.Fit(a);Check(r.Success && r.Points.Count==4 && r.DistinctDeviceTicks==3,"duplicate counts");
  var repeats=new List<NpClockObservation>{a[0],a[0],a[0]};Check(!NpClockFit.Fit(repeats).Success,"duplicate-only no fit");
  a=Line(100,0);a.Insert(2,O(0,25000000,Epoch+25000000,failure:"timeout"));r=NpClockFit.Fit(a);
  Check(r.Success && r.ObservationCount==8 && r.Points.Count==7 && r.FailedObservations.Count==1,"failed observation retained, not fabricated");
  a=Line(100,0);a[3]=O(60100,60010000,Epoch+60010000,1000);Check(!NpClockFit.Fit(a).Success,"model/uncertainty contradiction");
  a=Line(100,0);a[2]=O(40100,40000000,Epoch+40000000,Double.NaN);Check(!NpClockFit.Fit(a).Success,"invalid uncertainty");
  a=Line(100,0);a[2]=O(40100,40000000,Epoch+40000000,uid:null);Check(!NpClockFit.Fit(a).Success,"missing identity");
  Check(!NpClockFit.Fit(new List<NpClockObservation>()).Success,"empty");
  Check(NpClockFitResult.Limitation.Contains("not a bound") && NpClockFitResult.Limitation.Contains("1 ms"),"honest precision limitation");
  Console.WriteLine("NP clock fit: PASS ("+checks+" checks); offline only, no raw file rewrite.");
 }
}
