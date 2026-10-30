using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;
using USB512_interface;

static class NpHardwareRtcClockTests {
    const ulong Epoch=1793200000000000UL;
    static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static void Seal(byte[] b){Put(b,60,NpFirstSyncWire.Crc(b,60));}
    static byte[] Change(byte[] b,int at,uint n){var c=(byte[])b.Clone();Put(c,at,n);Seal(c);return c;}
    static void Reject(Action action,string why){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static byte[] Reply(byte[] q,uint flags=23,uint generation=37,uint uncertainty=4000,ulong utc=Epoch,uint error=0,uint state=1,uint rx=100,uint tx=102){var b=new byte[64];Put(b,0,NpHardwareRtcClock.ReplyMagic);Put(b,4,2);Put(b,8,U(q,8));Put(b,12,U(q,12));Put(b,16,error);Put(b,20,rx);Put(b,24,tx);Put(b,28,generation);Put(b,32,uncertainty);Put(b,36,(flags&2)!=0?1U:0);Put64(b,40,utc);Put(b,48,state);Put(b,52,(flags&1)!=0?100U:0);Put(b,56,flags);Seal(b);return b;}
    static void Codec(){
        var query=NpHardwareRtcClock.Request(0,17);Need(U(query,4)==2&&query.Skip(16).Take(44).All(b=>b==0),"observation geometry/payload");
        var legacy=NpV2OnlineClock.Request(0,17);Need(U(legacy,4)==1&&Change(legacy,4,2).SequenceEqual(query),"legacy v1 query changed beyond explicit version/CRC");
        foreach(ulong utc in new[]{NpHardwareRtcClock.MinimumUtc,NpHardwareRtcClock.MaximumUtc})foreach(uint uncertainty in new[]{1000U,500000U}){var q=NpHardwareRtcClock.Request(1,18,0,utc,uncertainty,37);Need(U(q,16)==0&&BitConverter.ToUInt64(q,20)==utc&&U(q,28)==uncertainty&&U(q,32)==37,"commit boundary/refHAL0 geometry");}
        Reject(()=>NpHardwareRtcClock.Request(1,18,0,Epoch,999),"HAL-grid minimum uncertainty");
        Reject(()=>NpHardwareRtcClock.Request(0,0),"zeroID");Reject(()=>NpHardwareRtcClock.Request(2,18),"unknownop");Reject(()=>NpHardwareRtcClock.Request(0,18,1),"observationpayload");Reject(()=>NpHardwareRtcClock.Request(1,18,0,NpHardwareRtcClock.MinimumUtc-1),"tooearlyUTC");Reject(()=>NpHardwareRtcClock.Request(1,18,0,NpHardwareRtcClock.MaximumUtc+1),"toolateUTC");Reject(()=>NpHardwareRtcClock.Request(1,18,0,Epoch,500001),"uncertaintybound");
        foreach(int at in new[]{0,4,8,16,20,28,32,36,59})Reject(()=>NpHardwareRtcClock.ValidateRequest(Change(query,at,99)),"resealed query invalid field");
        var pending=Reply(query,17,91,0,0);var p=NpHardwareRtcClock.Parse(pending,query);Need(!p.PcCalibrated&&p.Generation==91&&p.Utc==0,"opaque uncalibrated generation lost");
        var valid=Reply(query);Need(NpHardwareRtcClock.Parse(valid,query).PcCalibrated,"valid calibrated reply");Need(NpHardwareRtcClock.Parse(Reply(query,error:999),query).Error==999,"generic nonzero error receipt not retained");
        var unsupported=Reply(query,0,0,0,0);Need(NpHardwareRtcClock.Parse(unsupported,query).QuantumUs==0,"unsupported receipt shape");
        foreach(var q in new[]{query,NpHardwareRtcClock.Request(1,18,123,Epoch,5000,37)})for(int i=0;i<64;i++){var bad=(byte[])q.Clone();bad[i]^=1;Reject(()=>NpHardwareRtcClock.ValidateRequest(bad),"requestCRC");}
        for(int i=0;i<64;i++){var bad=(byte[])valid.Clone();bad[i]^=1;Reject(()=>NpHardwareRtcClock.Parse(bad,query),"replyCRC");}
        foreach(var m in new[]{new uint[]{0,0},new uint[]{4,1},new uint[]{8,1},new uint[]{12,18},new uint[]{28,0},new uint[]{32,500001},new uint[]{36,0},new uint[]{36,2},new uint[]{52,0},new uint[]{56,55},new uint[]{56,7},new uint[]{56,22},new uint[]{56,21}})Reject(()=>NpHardwareRtcClock.Parse(Change(valid,(int)m[0],m[1]),query),"reply structure/dependency");
        Reject(()=>NpHardwareRtcClock.Parse(Change(pending,32,1),query),"pending uncertainty must0");Reject(()=>NpHardwareRtcClock.Parse(Change(pending,52,0),query),"hardware quantum100");Reject(()=>NpHardwareRtcClock.Parse(Change(unsupported,52,100),query),"unsupported quantum0");
        foreach(ulong utc in new[]{NpHardwareRtcClock.MinimumUtc-1,NpHardwareRtcClock.MaximumUtc+1}){var bad=(byte[])valid.Clone();Put64(bad,40,utc);Seal(bad);Reject(()=>NpHardwareRtcClock.Parse(bad,query),"calendar range");}
        Reject(()=>NpHardwareRtcClock.Parse(Reply(query,17,37,0,Epoch),query),"invalid calendar cannot carryUTC");
    }
    sealed class Fake {
        internal readonly List<byte[]> Calls=new List<byte[]>();
        internal long Pc=(long)Epoch;internal uint Hal=1000,Generation=37,Uncertainty,Reference;
        internal long RoundTripUs=10000;internal uint ReadOffsetUs=500;
        internal ulong ReferenceUtc;internal uint Sequence,Pending=2;internal bool Committed;
        internal Func<byte[],int,byte[]> Mutate;
        internal Action<byte[],int> OnCall;
        internal uint Id(){return ++Sequence;}
        internal long Now(){return Pc;}
        internal Task<byte[]> Exchange(byte[] q,CancellationToken cancel){
            Calls.Add((byte[])q.Clone());int call=Calls.Count;OnCall?.Invoke(q,call);uint rx=unchecked(Hal+(uint)((RoundTripUs-2000)/2000)),tx=unchecked(rx+2);Pc+=RoundTripUs;Hal=unchecked(Hal+(uint)(RoundTripUs/1000));
            if(U(q,8)==1){Need(!Committed,"helper replayed commit");Need(U(q,32)==Generation,"commit expectedgen mismatch");Committed=true;Generation=909;Uncertainty=U(q,28);Reference=U(q,16);ReferenceUtc=BitConverter.ToUInt64(q,20);}
            bool calibrated=Committed&&U(q,8)==0&&Pending==0;if(Committed&&U(q,8)==0&&Pending!=0)Pending--;
            ulong actual=Committed?ReferenceUtc+(ulong)unchecked(rx-Reference)*1000+ReadOffsetUs:0;
            var r=Reply(q,Committed?(calibrated?23U:19U):17U,Generation,calibrated?Uncertainty:0,actual,0,1,rx,tx);
            if(Mutate!=null)r=Mutate(r,call);return Task.FromResult(r);
        }
        internal int Commits {get{return Calls.Count(q=>U(q,8)==1);}}
    }
    sealed class SettlingPeer {
        readonly Stopwatch elapsed=Stopwatch.StartNew();uint sequence,generation=37,uncertainty,reference;ulong utc;long commitAt;
        internal int PendingMs=990,Commits,PendingReads;internal Action FirstPending;
        internal readonly List<long> ReadbackMs=new List<long>();
        internal long Now(){return (long)Epoch+(long)(elapsed.ElapsedTicks*1000000.0/Stopwatch.Frequency);}
        internal uint Id(){return ++sequence;}
        uint Hal {get{return 1000+(uint)elapsed.ElapsedMilliseconds;}}
        internal async Task<byte[]> Exchange(byte[] q,CancellationToken cancel){
            cancel.ThrowIfCancellationRequested();uint rx=Hal;await Task.Delay(1,cancel).ConfigureAwait(false);
            if(U(q,8)==1){Need(Commits==0&&U(q,32)==generation,"settling peer COMMIT repeated/unbound");Commits++;generation=909;uncertainty=U(q,28);reference=U(q,16);utc=BitConverter.ToUInt64(q,20);commitAt=elapsed.ElapsedMilliseconds;}
            uint tx=Hal;bool read=Commits!=0&&U(q,8)==0,ready=read&&elapsed.ElapsedMilliseconds-commitAt>=PendingMs;
            if(read){ReadbackMs.Add(elapsed.ElapsedMilliseconds-commitAt);if(!ready){PendingReads++;if(PendingReads==1)FirstPending?.Invoke();}}
            return Reply(q,ready?23U:17U,generation,ready?uncertainty:0,ready?utc+(ulong)unchecked(tx-reference)*1000:0,0,1,rx,tx);
        }
    }
    static async Task Settling(){
        var peer=new SettlingPeer();var result=await NpHardwareRtcClock.Synchronize(peer.Exchange,peer.Id,s=>s==1,peer.Now,CancellationToken.None);
        Need(result.Confirmed&&peer.Commits==1&&peer.PendingReads>0&&result.ReadbackQueries<=8&&peer.ReadbackMs.Last()>=990,"Calendar settling confirmed too early or replayed COMMIT");
        Need(peer.ReadbackMs.Take(peer.ReadbackMs.Count-1).All(ms=>ms<990),"Unconfirmed calendar was treated as calibrated");
        Need(peer.ReadbackMs.Zip(peer.ReadbackMs.Skip(1),(a,b)=>b-a).All(ms=>ms>=200),"Pending readbacks were not spaced200ms");
        var late=new SettlingPeer{PendingMs=1990};var lateResult=await NpHardwareRtcClock.Synchronize(late.Exchange,late.Id,s=>s==1,late.Now,CancellationToken.None);
        Need(lateResult.Confirmed&&late.Commits==1&&lateResult.ReadbackQueries>8&&lateResult.ReadbackQueries<=NpHardwareRtcClock.MaximumReadbacks&&late.ReadbackMs.Last()>=1990,"Near2s completion was abandoned or COMMIT replayed");
        Need(late.ReadbackMs.Take(late.ReadbackMs.Count-1).All(ms=>ms<1990),"Late pending reply became false calibration proof");
        var fast=new SettlingPeer{PendingMs=0};var immediate=await NpHardwareRtcClock.Synchronize(fast.Exchange,fast.Id,s=>s==1,fast.Now,CancellationToken.None);
        Need(immediate.Confirmed&&immediate.ReadbackQueries==1&&fast.Commits==1&&fast.PendingReads==0,"Already confirmed peer entered settling delay/replayed mutation");
        using(var cancel=new CancellationTokenSource()){
            var stopped=new SettlingPeer{PendingMs=2000};stopped.FirstPending=()=>cancel.CancelAfter(50);bool canceled=false;
            try{await NpHardwareRtcClock.Synchronize(stopped.Exchange,stopped.Id,s=>s==1,stopped.Now,cancel.Token);}catch(NpHardwareRtcCalibrationException ex){canceled=true;Need(ex.Attempt.CommitAttempted&&!ex.Attempt.Confirmed&&ex.Attempt.ReadbackQueries==1,"Cancellation during settling lost attempt context");}
            Need(canceled&&stopped.Commits==1&&stopped.ReadbackMs.Count==1,"Cancellation during spacing issued another read/write");
        }
        var pending=new SettlingPeer{PendingMs=5000};bool bounded=false;
        try{await NpHardwareRtcClock.Synchronize(pending.Exchange,pending.Id,s=>s==1,pending.Now,CancellationToken.None);}catch(NpHardwareRtcCalibrationException ex){bounded=true;Need(ex.Attempt.CommitAttempted&&!ex.Attempt.Confirmed&&ex.Attempt.ReadbackQueries==NpHardwareRtcClock.MaximumReadbacks&&pending.Commits==1&&pending.ReadbackMs.Last()-pending.ReadbackMs.First()>=2400,"Pending budget did not cover2s deadline+400ms observation margin or replayed COMMIT");}
        Need(bounded,"Never-settled calendar was confirmed");
    }
    static async Task<NpHardwareRtcCalibrationException> Fails(Fake f,Action<NpHardwareRtcCalibrationException> check=null,CancellationToken cancel=default(CancellationToken)){
        try{await NpHardwareRtcClock.Synchronize(f.Exchange,f.Id,s=>s==1,f.Now,cancel);}catch(NpHardwareRtcCalibrationException e){Need(e.Attempt!=null&&!e.Attempt.Confirmed,"failure retained false confirmation");if(check!=null)check(e);return e;}throw new Exception("failure was accepted");
    }
    static async Task Success(){foreach(uint hal in new[]{1000U,uint.MaxValue-25U,unchecked(0U-76U)}){
        var f=new Fake{Hal=hal};var r=await NpHardwareRtcClock.Synchronize(f.Exchange,f.Id,s=>s==1,f.Now,CancellationToken.None);
        Need(r.Confirmed&&r.CommitAttempted&&r.Observations==8&&r.ReadbackQueries==3&&f.Commits==1&&r.Accepted.Generation==909&&r.Receipt.Generation==909,"pending commit/readback lifecycle");Need(r.PcUncertaintyUs==5000&&r.BestRoundTripUs==10000&&r.PhaseLowerUs<=0&&r.PhaseUpperUs>=0,"measured PC phase bounds/uncertainty");Need(f.Calls.Count==12&&f.Calls.Select(q=>U(q,12)).Distinct().Count()==12,"bounded fresh read-only requests");
        var raw=r.CommitRequest;raw[16]^=1;Need(!r.CommitRequest.SequenceEqual(raw),"commit evidence is mutable");Need(U(r.CommitRequest,32)==37&&BitConverter.ToUInt64(r.CommitRequest,20)==Epoch+76000,"best observation transport/reference mapping");if(hal==unchecked(0U-76U))Need(U(r.CommitRequest,16)==0,"HALref0 was treated as unavailable");
    }var fresh=new Fake{Generation=0,Pending=0};Need((await NpHardwareRtcClock.Synchronize(fresh.Exchange,fresh.Id,s=>s==1,fresh.Now,CancellationToken.None)).Confirmed,"initial opaque generation0 rejected");}
    static async Task Failures(){
        foreach(int mode in Enumerable.Range(0,8)){
            int failure=mode;var f=new Fake();f.Mutate=(b,n)=>{if(n!=2)return b;if(failure==0)return Change(b,48,2);if(failure==1)return Change(b,56,U(b,56)|8);if(failure==2)return Change(b,28,38);if(failure==3)return Reply(f.Calls.Last(),0,37,0,0);if(failure==4)return Change(b,16,777);if(failure==5)return Change(b,4,1);if(failure==6){b[60]^=1;return b;}throw new TimeoutException();};
            await Fails(f,e=>Need(!e.Attempt.CommitAttempted&&f.Commits==0,"invalid/active/unsupported observation mutated RTC"));
        }
        foreach(int mode in Enumerable.Range(0,9)){
            int failure=mode;var f=new Fake();f.Mutate=(b,n)=>{if(n==9){if(failure==0)throw new TimeoutException();if(failure==1)throw new InvalidOperationException("transport lost");if(failure==2)return Change(b,16,91);if(failure==3)return Change(b,28,37);if(failure==4){b[60]^=1;return b;}}if(n>=10){if(failure==5)return Change(b,28,910);if(failure==6)return Change(b,56,U(b,56)|8);if(failure==7){f.Pending=99;return Reply(f.Calls.Last(),19,909,0,Epoch,0,1,U(b,20),U(b,24));}if(failure==8)throw new TimeoutException();}return b;};
            await Fails(f,e=>{Need(e.Attempt.CommitAttempted&&f.Commits==1,"ambiguous/rejected commit was replayed or lost");if(failure==4)Need(e.Attempt.CommitAcknowledgement!=null,"malformed acknowledgement raw bytes lost");if(failure>=7)Need(e.Attempt.ReadbackQueries==NpHardwareRtcClock.MaximumReadbacks&&f.Calls.Count==9+NpHardwareRtcClock.MaximumReadbacks,"read-only pending/timeout budget not bounded");});
        }
        var phase=new Fake{Pending=0};phase.Mutate=(b,n)=>{if(n>=10){Put64(b,40,BitConverter.ToUInt64(b,40)+1000000);Seal(b);}return b;};await Fails(phase,e=>Need(e.Attempt.ReadbackQueries==1&&phase.Commits==1,"bad phase retried mutation/readback"));
        var uncertainty=new Fake{Pending=0};uncertainty.Mutate=(b,n)=>n>=10?Change(b,32,4000):b;await Fails(uncertainty,e=>Need(uncertainty.Commits==1,"bad uncertainty replayed commit"));
        foreach(bool after in new[]{false,true})using(var cancel=new CancellationTokenSource()){
            var f=new Fake();if(after)f.OnCall=(q,n)=>{if(n==9)cancel.Cancel();};else cancel.Cancel();await Fails(f,e=>Need(e.Attempt.CommitAttempted==after&&f.Commits==(after?1:0),"cancellation delivery context lost"),cancel.Token);
        }
        var back=new Fake();int now=0;try{await NpHardwareRtcClock.Synchronize(back.Exchange,back.Id,s=>true,()=>now++==0?(long)Epoch:(long)Epoch-1,CancellationToken.None);throw new Exception("backward PC accepted");}catch(NpHardwareRtcCalibrationException e){Need(!e.Attempt.CommitAttempted&&back.Commits==0,"backward PC mutated RTC");}
        var ids=new Fake();try{await NpHardwareRtcClock.Synchronize(ids.Exchange,()=>1,s=>true,ids.Now,CancellationToken.None);throw new Exception("duplicate ID accepted");}catch(NpHardwareRtcCalibrationException e){Need(!e.Attempt.CommitAttempted&&ids.Calls.Count==1,"duplicate ID sent/mutated");}
        var incoherent=new Fake();incoherent.Mutate=(b,n)=>{if(n==2){Put(b,20,U(b,20)+1000);Put(b,24,U(b,24)+1000);Seal(b);}return b;};await Fails(incoherent,e=>Need(!e.Attempt.CommitAttempted&&incoherent.Commits==0,"incoherent HAL/PC observations mutated RTC"));
        var delayed=new Fake{RoundTripUs=1002000};await Fails(delayed,e=>Need(!e.Attempt.CommitAttempted&&delayed.Commits==0&&e.Attempt.Observations==8,"excess PC uncertainty admitted a commit"));
        var boundary=new Fake{RoundTripUs=1000000,Pending=0};var inclusive=await NpHardwareRtcClock.Synchronize(boundary.Exchange,boundary.Id,s=>s==1,boundary.Now,CancellationToken.None);Need(inclusive.Confirmed&&inclusive.PcUncertaintyUs==500000,"inclusive PC uncertainty boundary rejected");
        foreach(uint offset in new[]{0U,2000U}){var f=new Fake{Pending=0,ReadOffsetUs=offset};Need((await NpHardwareRtcClock.Synchronize(f.Exchange,f.Id,s=>s==1,f.Now,CancellationToken.None)).Confirmed,"actual calendar read at RX/TX endpoint rejected");}
        var stopped=new Fake{Pending=0};stopped.Mutate=(b,n)=>Change(b,48,5);Need((await NpHardwareRtcClock.Synchronize(stopped.Exchange,stopped.Id,s=>s==5,stopped.Now,CancellationToken.None)).Confirmed,"caller stopped-state predicate was replaced by V2 assumptions");
        var requestMutation=new Fake();requestMutation.OnCall=(q,n)=>{if(n==1){Put(q,12,999);Seal(q);}};await Fails(requestMutation,e=>Need(!e.Attempt.CommitAttempted&&e.Attempt.LastReply!=null,"mutated transport request changed original reply binding"));
    }
    static int Main(){try{Codec();Success().GetAwaiter().GetResult();Failures().GetAwaiter().GetResult();Settling().GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" hardware RTC v2 codec/calibration checks; deterministic fake transport, no hardware");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
