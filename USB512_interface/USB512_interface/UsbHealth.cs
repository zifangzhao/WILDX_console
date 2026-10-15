using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace USB512_interface {
    // Observed bulk-IN attempts only. A native pending result is not a failure.
    internal static class UsbHealth {
        internal sealed class Snapshot {
            internal long Session,Success,Failed,Bytes,Cancelled,Pending,Unobserved,Integrity;
            internal long First,Last,DurationTicks,MinDuration=long.MaxValue,MaxDuration,GapTicks,MaxGap,GapCount;
            internal string Target,Speed,IntegrityScope;
            internal int SpeedError;
            internal byte Pipe;
            internal bool Closed;
            internal Dictionary<int,long> Errors=new Dictionary<int,long>();
            internal double FailureRate {get{return Success+Failed==0?0:(double)Failed/(Success+Failed);}}
        }
        internal sealed class Attempt {internal Snapshot Stats;internal IntPtr Handle;internal long Started;internal byte Pipe;internal bool CancelRequested,Done;}
        sealed class Session {
            internal long Id;internal string Target,Speed;internal int SpeedError;internal bool Closed,Diagnostic;
            internal readonly Dictionary<byte,Snapshot> Pipes=new Dictionary<byte,Snapshot>();
            internal readonly Dictionary<IntPtr,Attempt> Pending=new Dictionary<IntPtr,Attempt>();
            internal readonly List<Attempt> InFlight=new List<Attempt>();
        }
        static readonly object gate=new object();
        static readonly Dictionary<IntPtr,Session> active=new Dictionary<IntPtr,Session>();
        static readonly List<Session> history=new List<Session>();
        static long nextSession;
        internal static void Open(IntPtr handle,string target,byte? speed,int speedError) {
            lock(gate) {
                var s=new Session{Id=++nextSession,Target=target,Speed=SpeedName(speed),SpeedError=speedError};
                active[handle]=s;history.Add(s);
                while(history.Count(s0=>s0.Closed)>32)history.Remove(history.First(s0=>s0.Closed));
            }
        }
        // WinUSB exposes a coarse speed class, not an exact link bit rate.
        internal static string SpeedName(byte? speed){return speed==1?"Low speed":speed==2?"Full speed":speed==3?"High speed or higher":speed.HasValue?"unavailable (Windows speed "+speed.Value+")":"unavailable";}
        internal static Attempt Begin(IntPtr handle,byte pipe,long now) {
            lock(gate) {
                Session s;if(!active.TryGetValue(handle,out s))return null;
                Snapshot p;if(!s.Pipes.TryGetValue(pipe,out p)){p=new Snapshot{Session=s.Id,Target=s.Target,Speed=s.Speed,SpeedError=s.SpeedError,Pipe=pipe,First=now};s.Pipes.Add(pipe,p);}
                p.First=Math.Min(p.First,now);p.Pending++;var a=new Attempt{Stats=p,Handle=handle,Started=now,Pipe=pipe};s.InFlight.Add(a);return a;
            }
        }
        internal static void Submitted(IntPtr handle,Attempt attempt,IntPtr overlapped,bool success,int error,uint bytes,bool deferImmediate,long now) {
            if(attempt==null)return;
            lock(gate) {
                Session s;
                if(overlapped!=IntPtr.Zero && ((!success&&error==997)||(success&&deferImmediate)) && active.TryGetValue(handle,out s))s.Pending.Add(overlapped,attempt);
                else Finish(attempt,success,error,bytes,now);
            }
        }
        internal static void Completed(IntPtr handle,IntPtr overlapped,bool success,int error,uint bytes,long now) {
            lock(gate) {
                Session s;Attempt a;if(!active.TryGetValue(handle,out s)||!s.Pending.TryGetValue(overlapped,out a))return;
                if(!success&&(error==996||error==997))return;
                s.Pending.Remove(overlapped);Finish(a,success,error,bytes,now);
            }
        }
        static void Finish(Attempt a,bool success,int error,uint bytes,long now) {
            if(a.Done)return;a.Done=true;Session s;if(active.TryGetValue(a.Handle,out s))s.InFlight.Remove(a);var p=a.Stats;p.Pending--;
            if(!success&&error==995&&a.CancelRequested){p.Cancelled++;return;}
            if(success){p.Success++;p.Bytes+=bytes;}else {p.Failed++;long n;p.Errors.TryGetValue(error,out n);p.Errors[error]=n+1;}
            long duration=Math.Max(0,now-a.Started);p.DurationTicks+=duration;p.MinDuration=Math.Min(p.MinDuration,duration);p.MaxDuration=Math.Max(p.MaxDuration,duration);
            if(p.Last!=0){long gap=Math.Max(0,now-p.Last);p.GapTicks+=gap;p.MaxGap=Math.Max(p.MaxGap,gap);p.GapCount++;}p.Last=Math.Max(p.Last,now);
        }
        internal static void Aborting(IntPtr handle,byte pipe){lock(gate){Session s;if(active.TryGetValue(handle,out s))foreach(var a in s.InFlight.Where(a=>a.Pipe==pipe))a.CancelRequested=true;}}
        internal static void Close(IntPtr handle){lock(gate){Session s;if(!active.TryGetValue(handle,out s))return;foreach(var a in s.InFlight){a.Stats.Pending--;a.Stats.Unobserved++;a.Done=true;}s.Pending.Clear();s.InFlight.Clear();s.Closed=true;foreach(var p in s.Pipes.Values)p.Closed=true;active.Remove(handle);}}
        internal static void Integrity(IntPtr handle,byte pipe,bool failed,string scope){lock(gate){Session s;Snapshot p;if(active.TryGetValue(handle,out s)&&s.Pipes.TryGetValue(pipe,out p)){p.IntegrityScope=scope;if(failed)p.Integrity++;}}}
        internal static void BeginRecording(string target){lock(gate){foreach(var s in history.Where(s=>Same(s.Target,target)))s.Pipes.Clear();}}
        internal static void Diagnostic(IntPtr handle){lock(gate){Session s;if(active.TryGetValue(handle,out s))s.Diagnostic=true;}}
        static bool Same(string a,string b){return a!=null&&b!=null&&string.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
        internal static Snapshot[] Snapshots(string target){return ReadSnapshots(target,false);}
        static Snapshot[] ReadSnapshots(string target,bool diagnostic){lock(gate){var sessions=history.Where(s=>Same(s.Target,target)&&s.Diagnostic==diagnostic);if(diagnostic)sessions=sessions.OrderByDescending(s=>s.Id).Take(1);return sessions.SelectMany(s=>s.Pipes.Values).Select(p=>new Snapshot{
            Session=p.Session,Target=p.Target,Speed=p.Speed,SpeedError=p.SpeedError,Pipe=p.Pipe,Closed=p.Closed,Success=p.Success,Failed=p.Failed,Bytes=p.Bytes,Cancelled=p.Cancelled,Pending=p.Pending,Unobserved=p.Unobserved,Integrity=p.Integrity,IntegrityScope=p.IntegrityScope,
            First=p.First,Last=p.Last,DurationTicks=p.DurationTicks,MinDuration=p.MinDuration,MaxDuration=p.MaxDuration,GapTicks=p.GapTicks,MaxGap=p.MaxGap,GapCount=p.GapCount,Errors=new Dictionary<int,long>(p.Errors)}).ToArray();}}
        internal static string Category(int error){return error==121||error==1460?"timeout":error==1167||error==55||error==433?"device unavailable/disconnected":error==995?"unexpected abort":error==31?"generic I/O (cause unspecified)":"Windows I/O error";}
        static double Ms(double ticks){return ticks*1000/Stopwatch.Frequency;}
        internal static string Summary(string target) {return Summarize(Snapshots(target));}
        static string Summarize(Snapshot[] snapshots) {
            var all=snapshots.Where(p=>p.Pipe==0x81).ToArray();
            if(all.Length==0)return "USB health: waiting for bulk transfers";
            long good=all.Sum(p=>p.Success),bad=all.Sum(p=>p.Failed),first=all.Min(p=>p.First),last=all.Max(p=>p.Last);
            string rate=good+bad==0?"n/a":(100.0*bad/(good+bad)).ToString("F3")+"%";
            double seconds=(double)(last-first)/Stopwatch.Frequency;
            string throughput=seconds>0?(all.Sum(p=>p.Bytes)/seconds/1e6).ToString("F2")+" MB/s avg":"throughput unavailable";
            return string.Format("USB {0} | {1:N0} OK / {2:N0} failed ({3}) | {4}\r\n{5:N0} bytes | {6:N0} pending | {7:N0} cancelled | timeouts {8:N0}",
                all.Last().Speed,good,bad,rate,throughput,all.Sum(p=>p.Bytes),all.Sum(p=>p.Pending),all.Sum(p=>p.Cancelled),all.Sum(p=>p.Errors.Where(e=>e.Key==121||e.Key==1460).Sum(e=>e.Value)));
        }
        internal static string Report(string target) {return Describe(target,Snapshots(target));}
        internal static string DiagnosticReport(string target){return Describe(target,ReadSnapshots(target,true));}
        static string Describe(string target,Snapshot[] snapshots) {
            var b=new StringBuilder();b.AppendLine(target??"No USB target selected");b.AppendLine(Summarize(snapshots));
            foreach(var p in snapshots) {
                long n=p.Success+p.Failed;
                b.AppendLine();b.AppendFormat("Session {0}, EP 0x{1:X2}, {2}, {3}\r\n",p.Session,p.Pipe,p.Closed?"closed":"open",p.Speed);
                if(p.SpeedError!=0)b.AppendLine("Speed query Windows error: "+p.SpeedError);
                b.AppendFormat("Completed attempts: {0}; successful: {1}; failed: {2}; bytes: {3}\r\n",n,p.Success,p.Failed,p.Bytes);
                b.AppendLine("Failure rate: "+(n==0?"unavailable":p.FailureRate.ToString("P3"))+" (failed / completed; excludes pending and intentional cancellation)");
                b.AppendFormat("Pending: {0}; intentional cancellation: {1}; unobserved at handle close: {2}\r\n",p.Pending,p.Cancelled,p.Unobserved);
                b.AppendLine(n==0?"Transfer duration: unavailable":string.Format("Transfer duration min/mean/max: {0:F3} / {1:F3} / {2:F3} ms",Ms(p.MinDuration),Ms((double)p.DurationTicks/n),Ms(p.MaxDuration)));
                b.AppendLine(p.GapCount==0?"Observed completion gap: unavailable":string.Format("Observed completion gap mean/max: {0:F3} / {1:F3} ms",Ms((double)p.GapTicks/p.GapCount),Ms(p.MaxGap)));
                foreach(var error in p.Errors.OrderBy(e=>e.Key))b.AppendFormat("Windows {0} (0x{0:X8}), {1}: {2}\r\n",error.Key,Category(error.Key),error.Value);
                b.AppendLine("Application integrity failures: "+(p.IntegrityScope==null?"unavailable (not instrumented for this stream)":p.Integrity+" ("+p.IntegrityScope+")"));
            }
            b.AppendLine();b.AppendLine("Scope: bulk-IN only. Average throughput spans the first submission to the last observed completion, including recovery gaps. Timing is host-observed, not USB packet timing.");
            b.AppendLine("Stalls, controller transaction errors/retries, and device producer/buffer overruns: unavailable from this observer. ETW: not collected; no controller events are included in the application failure rate.");
            b.AppendLine("USB bulk can retry errors internally without reporting a failed transfer. These counters cannot measure every physical CRC error/retry or a cable bit-error rate. A slow transfer alone does not identify a bad cable. Electrical eye/jitter measurements require external equipment.");
            return b.ToString();
        }
    }
}
