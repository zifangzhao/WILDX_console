using System;
using System.Collections.Generic;
using System.Linq;
using CE32_console;
using USB512_interface;

static class NpSdSweepPolicyTests {
    static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Refuse(Action action,string why){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Need(rejected,why);}
    static void Profiles(){
        var all=new uint[]{30000,20000,15000,7500};int v1=0,v2=0;
        for(uint mask=1;mask<=7;mask++)foreach(int family in new[]{1,2}){
            var calls=new List<uint>();var rates=NpSdSweepPolicy.Rates(family,mask,hz=>{calls.Add(hz);return true;});
            uint fixedMask=mask;
            var expected=all.Where(hz=>family==1?NpV1BleProtocol.TheoreticalMode(fixedMask,hz):NpV2SdControl.ProfileTicks(fixedMask,hz)!=0).ToArray();
            Need(rates.SequenceEqual(expected)&&calls.SequenceEqual(expected),"Rate order/mask differs from actual firmware predicate");
            if(family==1)v1+=rates.Length;else v2+=rates.Length;
            Need(NpSdSweepPolicy.Rates(family,mask,hz=>false).Length==0,"Unadvertised profile was tested");
            Need(NpSdSweepPolicy.Rates(family,mask,hz=>hz!=7500).SequenceEqual(expected.Where(hz=>hz!=7500)),"Unavailable7.5k was not skipped");
            Need(NpSdSweepPolicy.Rates(family,mask,hz=>hz!=20000).SequenceEqual(expected.Where(hz=>hz!=20000)),"Legacy20k capability was assumed");
            for(int i=0;i<rates.Length;i++){
                decimal demand=NpSdSweepPolicy.PhysicalBytesPerSecond(family,mask,rates[i]);Need(demand>0,"Nonpositive physical demand");
                if(i>0)Need(demand<NpSdSweepPolicy.PhysicalBytesPerSecond(family,mask,rates[i-1]),"Sweep is not descending physical demand");
            }
        }
        Need(v1==22&&v2==12,"Existing profile admission changed; no board availability was invented");
        Refuse(()=>NpSdSweepPolicy.Rates(1,3,null),"Missing fresh support callback accepted");
        foreach(int family in new[]{0,3,-1})Refuse(()=>NpSdSweepPolicy.Rates(family,1,h=>true),"Unknown family accepted");
        foreach(uint mask in new[]{0U,8U,UInt32.MaxValue})Refuse(()=>NpSdSweepPolicy.Rates(1,mask,h=>true),"Invalid fixed mask accepted");
        var mutable=NpSdSweepPolicy.Rates(2,7,h=>true);mutable[0]=1;Need(NpSdSweepPolicy.Rates(2,7,h=>true)[0]==30000,"Caller mutated shared policy order");
    }
    static void Physical(){
        foreach(uint mask in Enumerable.Range(1,7).Select(i=>(uint)i))foreach(uint hz in new[]{7500U,15000U,20000U,30000U}){
            if(!NpV1BleProtocol.TheoreticalMode(mask,hz)){Refuse(()=>NpSdSweepPolicy.PhysicalBytesPerSecond(1,mask,hz),"Illegal V1 demand accepted");continue;}
            decimal expected=65536m*hz*780m*NpSdGeometry.Count(mask)/61440m;
            Need(NpSdSweepPolicy.PhysicalBytesPerSecond(1,mask,hz)==expected,"V1 fixed physical chunk demand differs");
        }
        uint[] masks={1,3,7},rates={30000,20000,15000,7500};
        decimal[][] expectedV2={new[]{18300000m,12200000m,9175000m,4612500m},new[]{36500000m,73000000m/3m,18300000m,9175000m},new[]{54750000m,36500000m,27375000m,13725000m}};
        for(int p=0;p<masks.Length;p++)for(int r=0;r<rates.Length;r++){
            decimal actual=NpSdSweepPolicy.PhysicalBytesPerSecond(2,masks[p],rates[r]);Need(actual==expectedV2[p][r],"SD16 physical K-stride vector differs");
            decimal nn=46592m*rates[r]*1600m/NpV2SdControl.ProfileTicks(masks[p],rates[r]);Need(actual>nn,"1250Hz AUX position overhead omitted");
        }
        foreach(uint mask in new[]{2U,4U,5U,6U})Refuse(()=>NpSdSweepPolicy.PhysicalBytesPerSecond(2,mask,20000),"Invalid V2 lane geometry accepted");
        foreach(uint hz in new[]{0U,10000U,UInt32.MaxValue})Refuse(()=>NpSdSweepPolicy.PhysicalBytesPerSecond(2,7,hz),"Unknown rate accepted");
    }
    static void Decisions(){
        foreach(NpSdSweepEvidence e in Enum.GetValues(typeof(NpSdSweepEvidence))){
            Need(NpSdSweepPolicy.Decide(e,false)==NpSdSweepDecision.Inconclusive,"Unknown owner/idle/card state authorized a next mutation");
            var d=NpSdSweepPolicy.Decide(e,true);
            Need((d==NpSdSweepDecision.TryLower)==(e==NpSdSweepEvidence.VerifiedThroughputLoss),"Non-throughput error lowered rate");
            Need((d==NpSdSweepDecision.Passed)==(e==NpSdSweepEvidence.VerifiedPass),"Unverified evidence became Passed");
            if(e!=NpSdSweepEvidence.VerifiedPass&&e!=NpSdSweepEvidence.VerifiedThroughputLoss&&e!=NpSdSweepEvidence.VerifiedIntegrityFailure)Need(d==NpSdSweepDecision.Inconclusive,"Setup/transport/cancel/missing proof blamed card speed");
        }
        Need(NpSdSweepPolicy.Decide(NpSdSweepEvidence.VerifiedIntegrityFailure,true)==NpSdSweepDecision.NotPassed,"Confirmed corruption silently advanced sweep");
        Need(NpSdSweepPolicy.Decide(default(NpSdSweepEvidence),true)==NpSdSweepDecision.Inconclusive,"Default evidence is not fail-closed");
        Need(NpSdSweepPolicy.Decide((NpSdSweepEvidence)999,true)==NpSdSweepDecision.Inconclusive,"Future/invalid evidence inferred success");
        // Model controller decisions only: no device is represented. A verified
        // pass stops at the first rate; unknown delivery stops without fallback.
        foreach(var outcomes in new[]{new[]{NpSdSweepEvidence.VerifiedPass},new[]{NpSdSweepEvidence.VerifiedThroughputLoss,NpSdSweepEvidence.VerifiedPass},new[]{NpSdSweepEvidence.TransportOrUnknownDelivery}}){
            var rates=NpSdSweepPolicy.Rates(2,7,h=>true);int attempts=0;
            foreach(var outcome in outcomes){Need(attempts<rates.Length,"Sweep exceeded supported rates");attempts++;if(NpSdSweepPolicy.Decide(outcome,true)!=NpSdSweepDecision.TryLower)break;}
            Need(attempts==outcomes.Length,"Sweep retried or advanced after terminal decision");
        }
    }
    static int Main(){try{Profiles();Physical();Decisions();Console.WriteLine("PASS "+checks+" SD sweep policy checks; actual profile predicates, physical V1/SD16 demand, safe classification; no I/O or hardware.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
