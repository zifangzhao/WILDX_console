using System;
using System.Globalization;
using System.IO;

namespace USB512_interface {
    internal enum NpSdUsbTestPhase : uint {
        None=0,QueuedUnacknowledged=1,Accepted=2,Running=3,Finished=4,RejectedNoStart=5,InterruptedUnknown=6
    }
    internal enum NpSdUsbTestOutcome : uint {
        Pending=0,DeviceCompleteReadbackRequired=1,ThroughputLoss=2,ProbeFault=3,Unsupported=4,
        IdentityMismatch=5,Catalog=6,StorageIo=7,Cancelled=8,Internal=9,Interrupted=10
    }
    [Flags]
    internal enum NpSdUsbTestValidity : uint {
        CurrentCardIdentity=1,FreshCatalog=2,SourceQuiet=4,SdQuiet=8,ClosedTerminal=16,AppendAvailable=32,
        CaptureCountersKnown=64,AdmittedKnown=128,PerformanceKnown=256,AuxKnown=512,
        PriorProfileRestored=1024,ExpectedSoftwareHandoff=2048,SourceGapsKnown=4096,
        MissingTicksKnown=8192,PrefixCountHighWaterKnown=16384,RejectedNoMediaMutation=32768
    }
    // Immutable parsed bytes. Grouped fields are null without their validity
    // bit, even if a producer left a zero placeholder. Raw retains every byte.
    internal sealed class NpSdUsbTestReport {
        readonly byte[] raw;
        internal readonly int Family;
        internal readonly string ExpectedUid;
        internal NpSdUsbTestReport(byte[] bytes,int family,string uid){raw=(byte[])bytes.Clone();Family=family;ExpectedUid=uid;}
        uint W(int n){return BitConverter.ToUInt32(raw,n*4);}
        ulong Q(int n){return BitConverter.ToUInt64(raw,n*4);}
        uint? Value(int n){uint v=W(n);return v==UInt32.MaxValue?(uint?)null:v;}
        uint? Known(int n,NpSdUsbTestValidity flag){return Has(flag)?Value(n):null;}
        ulong? Known64(int n,NpSdUsbTestValidity flag){ulong v=Q(n);return Has(flag)&&v!=UInt64.MaxValue?(ulong?)v:null;}
        internal byte[] Raw {get{return (byte[])raw.Clone();}}
        internal uint Capabilities {get{return W(3);}}
        internal NpSdUsbTestPhase Phase {get{return (NpSdUsbTestPhase)W(4);}}
        internal NpSdUsbTestOutcome Outcome {get{return (NpSdUsbTestOutcome)W(5);}}
        internal NpSdUsbTestValidity Validity {get{return (NpSdUsbTestValidity)W(6);}}
        internal bool Has(NpSdUsbTestValidity flags){return (Validity&flags)==flags;}
        internal uint BridgeError {get{return W(7);}}
        internal ulong Ticket {get{return Q(8);}}
        internal uint PlanCrc {get{return W(10);}}
        internal uint InnerId {get{return W(11);}}
        internal uint RecordFamily {get{return W(12);}}
        internal uint Mask {get{return W(13);}}
        internal uint Hz {get{return W(14);}}
        internal uint Seconds {get{return W(15);}}
        internal uint? TargetNeuralBlocks {get{return Ticket==0?null:Value(16);}}
        internal string Uid {get{return W(17).ToString("X8")+W(18).ToString("X8")+W(19).ToString("X8");}}
        internal uint? Capacity {get{return Known(20,NpSdUsbTestValidity.CurrentCardIdentity);}}
        internal uint[] Cid {get{return Has(NpSdUsbTestValidity.CurrentCardIdentity)?new[]{W(21),W(22),W(23),W(24)}:null;}}
        internal uint? Frontier {get{return Known(25,NpSdUsbTestValidity.FreshCatalog);}}
        internal uint? CatalogFlags {get{return Known(26,NpSdUsbTestValidity.FreshCatalog);}}
        internal uint SupportedRates {get{return W(27);}}
        internal uint MaximumSeconds {get{return W(28);}}
        internal uint? ExpectedFrontier {get{return Ticket==0?null:Value(29);}}
        internal uint? PriorMask {get{return Value(30);}}
        internal uint? PriorHz {get{return Value(31);}}
        internal uint? Session {get{return Value(32);}}
        internal uint? ReserveStart {get{return Value(33);}}
        internal uint? ReserveEnd {get{return Value(34);}}
        internal uint? PayloadStart {get{return Value(35);}}
        internal uint? DurableEnd {get{return Value(36);}}
        internal uint? StoredNeuralBlocks {get{return Known(37,NpSdUsbTestValidity.PrefixCountHighWaterKnown);}}
        internal uint? AdmittedHighWater {get{return Known(38,NpSdUsbTestValidity.AdmittedKnown);}}
        internal uint? LogicalPrefix {get{return Known(39,NpSdUsbTestValidity.PrefixCountHighWaterKnown);}}
        internal uint? StoredHighWater {get{return Known(40,NpSdUsbTestValidity.PrefixCountHighWaterKnown);}}
        internal uint? AppState {get{return Value(41);}}
        internal uint? WriterState {get{return Value(42);}}
        internal uint? WriterError {get{return Value(43);}}
        internal uint? CommandError {get{return Value(44);}}
        internal uint? Drops {get{return Known(45,NpSdUsbTestValidity.CaptureCountersKnown);}}
        internal uint? Overflows {get{return Known(46,NpSdUsbTestValidity.CaptureCountersKnown);}}
        internal uint? SourceGaps {get{return Known(47,NpSdUsbTestValidity.SourceGapsKnown);}}
        internal ulong? MissingTicks {get{return Known64(48,NpSdUsbTestValidity.MissingTicksKnown);}}
        internal ulong? DiscardedTicks {get{return Known64(50,NpSdUsbTestValidity.CaptureCountersKnown);}}
        internal uint? RecordMilliseconds {get{return Value(52);}}
        internal uint? DrainMilliseconds {get{return Value(53);}}
        internal uint? WriteCount {get{return Known(54,NpSdUsbTestValidity.PerformanceKnown);}}
        internal ulong? TotalWriteMicroseconds {get{return Known64(55,NpSdUsbTestValidity.PerformanceKnown);}}
        internal uint? MaximumWriteMicroseconds {get{return Known(57,NpSdUsbTestValidity.PerformanceKnown);}}
        internal uint? MaximumServiceMicroseconds {get{return Known(58,NpSdUsbTestValidity.PerformanceKnown);}}
        internal uint? SdHz {get{return Known(59,NpSdUsbTestValidity.PerformanceKnown);}}
        internal uint? CpuHz {get{return Known(60,NpSdUsbTestValidity.PerformanceKnown);}}
        internal uint? AuxQueueDrops {get{return Known(61,NpSdUsbTestValidity.AuxKnown);}}
        internal uint? AuxSchedulerMisses {get{return Known(62,NpSdUsbTestValidity.AuxKnown);}}
        internal bool Supports(uint mask,uint hz){
            int rate=hz==7500?0:hz==15000?1:hz==20000?2:hz==30000?3:-1;
            return (Capabilities&NpSdUsbTestWire.Capability)!=0 && mask>0 && mask<=7 && rate>=0 &&
                (SupportedRates&(1U<<((int)mask*4+rate)))!=0;
        }
    }
    // Pure NQT1/NQR1 codec. No command delivery, media access, replay,
    // time-out policy or claim that a device-complete result passed readback.
    internal static class NpSdUsbTestWire {
        internal const int PlanBytes=128,ReportBytes=256;
        internal const byte GetReportRequest=0xa3,SubmitRequest=0xa4;
        internal const uint PlanMagic=0x3154514e,ReportMagic=0x3152514e,Abi=1,Capability=0x10000,ExplicitNewTestReconcile=1;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ulong L(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        static void Put64(byte[] b,int at,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,8);}
        static void Require(bool ok,string message){if(!ok)throw new InvalidDataException("SD USB test: "+message);}
        static bool Rate(uint hz){return hz==7500 || hz==15000 || hz==20000 || hz==30000;}
        static void KnownWords(byte[] b,NpSdUsbTestReport r,NpSdUsbTestValidity flag,params int[] words){
            if(r.Has(flag))foreach(int n in words)Require(U(b,n*4)!=UInt32.MaxValue,"known field sentinel");
        }
        static uint[] UidWords(string uid){
            Require(uid!=null && uid.Length==24,"exact UID required");var w=new uint[3];
            for(int i=0;i<3;i++)Require(UInt32.TryParse(uid.Substring(i*8,8),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out w[i]),"UID encoding");
            Require((w[0]|w[1]|w[2])!=0 && (w[0]&w[1]&w[2])!=UInt32.MaxValue,"UID sentinel");return w;
        }
        static uint PlanCrc(byte[] b){
            var covered=new byte[124];Buffer.BlockCopy(b,0,covered,0,60);Buffer.BlockCopy(b,64,covered,60,64);
            return NpFirstSyncWire.Crc(covered,covered.Length);
        }
        static void MatchWords(byte[] b,int at,uint[] expected){for(int i=0;i<expected.Length;i++)Require(U(b,at+i*4)==expected[i],"identity mismatch");}
        static NpFirstSyncSdRequest Inner(byte[] inner,int family,string uid){
            var q=NpFirstSyncSd.ParseRequest(inner,family,uid);
            Require(q.Operation==NpFirstSyncSd.Start && q.Version==(family==1?3:5),"matched finite NPSD3/V1 or NPSD5/V2 START required");
            if(family==1)Require(U(inner,44)>U(inner,40) && U(inner,44)!=UInt32.MaxValue,"finite V1 end LBA required");
            else Require(U(inner,40)!=0 && U(inner,40)!=UInt32.MaxValue,"finite V2 block count required");
            return q;
        }
        internal static NpSdUsbTestReport ParseReport(byte[] raw,int family,string uid){
            Require(raw!=null && raw.Length==ReportBytes && (family==1 || family==2),"report size/family");var words=UidWords(uid);
            Require(U(raw,0)==ReportMagic && U(raw,4)==Abi && U(raw,8)==ReportBytes && U(raw,252)==NpFirstSyncWire.Crc(raw,252),"report tag/ABI/size/CRC");
            Require(U(raw,16)<=6 && U(raw,20)<=10 && (U(raw,24)&~65535U)==0,"report phase/outcome/validity");
            var r=new NpSdUsbTestReport(raw,family,uid.ToUpperInvariant());
            MatchWords(raw,68,words);Require(r.RecordFamily==(uint)family,"record family mismatch");
            if(r.Has(NpSdUsbTestValidity.CurrentCardIdentity)){
                Require(r.Capacity.HasValue && r.Capacity!=0,"capacity");
                uint any=0,all=UInt32.MaxValue;foreach(uint w in r.Cid){any|=w;all&=w;}Require(any!=0 && all!=UInt32.MaxValue,"CID sentinel");
            }
            KnownWords(raw,r,NpSdUsbTestValidity.FreshCatalog,25,26);
            if(r.Frontier.HasValue && r.Capacity.HasValue)Require(r.Frontier<=r.Capacity,"catalog frontier exceeds capacity");
            KnownWords(raw,r,NpSdUsbTestValidity.CaptureCountersKnown,45,46);
            if(r.Has(NpSdUsbTestValidity.CaptureCountersKnown))Require(L(raw,200)!=UInt64.MaxValue,"known discarded tick sentinel");
            KnownWords(raw,r,NpSdUsbTestValidity.SourceGapsKnown,47);
            if(r.Has(NpSdUsbTestValidity.MissingTicksKnown))Require(L(raw,192)!=UInt64.MaxValue,"known missing tick sentinel");
            KnownWords(raw,r,NpSdUsbTestValidity.PrefixCountHighWaterKnown,37,39,40);
            KnownWords(raw,r,NpSdUsbTestValidity.AdmittedKnown,38);
            if(r.Has(NpSdUsbTestValidity.PrefixCountHighWaterKnown)){
                Require(r.LogicalPrefix<=r.StoredNeuralBlocks && r.StoredNeuralBlocks<=r.StoredHighWater,"P/N/H order");
                if(r.Capacity.HasValue)Require(r.StoredHighWater<=r.Capacity,"H exceeds capacity");
                if(r.AdmittedHighWater.HasValue)Require(r.StoredHighWater<=r.AdmittedHighWater,"H exceeds admitted high water");
            }
            if(r.AdmittedHighWater.HasValue && r.Capacity.HasValue)Require(r.AdmittedHighWater<=r.Capacity,"A exceeds capacity");
            KnownWords(raw,r,NpSdUsbTestValidity.PerformanceKnown,54,57,58,59,60);
            if(r.Has(NpSdUsbTestValidity.PerformanceKnown))Require(L(raw,220)!=UInt64.MaxValue && r.SdHz>0 && r.CpuHz>0,"known performance fields");
            KnownWords(raw,r,NpSdUsbTestValidity.AuxKnown,61,62);
            if(r.Has(NpSdUsbTestValidity.PriorProfileRestored))Require(r.PriorMask>0 && r.PriorMask<=7 && r.PriorHz.HasValue && Rate(r.PriorHz.Value),"restored prior profile");
            if(r.Ticket==0)Require(U(raw,64)==UInt32.MaxValue && U(raw,116)==UInt32.MaxValue,"no-ticket plan fields");
            else Require(r.TargetNeuralBlocks.HasValue && r.TargetNeuralBlocks>0 && r.ExpectedFrontier.HasValue && r.Mask>0 && r.Mask<=7 && Rate(r.Hz) && r.InnerId!=0 && r.Seconds>=1 && r.Seconds<=120,"ticket plan fields");
            Require(r.MaximumSeconds>=1 && r.MaximumSeconds<=120,"maximum seconds");return r;
        }
        internal static byte[] BuildPlan(NpSdUsbTestReport report,byte[] innerRequest,uint seconds,bool explicitReconcile=false){
            Require(report!=null,"report required");var r=ParseReport(report.Raw,report.Family,report.ExpectedUid);
            Require(r.Has(NpSdUsbTestValidity.CurrentCardIdentity|NpSdUsbTestValidity.FreshCatalog|NpSdUsbTestValidity.AppendAvailable),"fresh card/catalog/test availability required");
            bool unknown=r.Phase==NpSdUsbTestPhase.InterruptedUnknown;
            Require(r.Phase==NpSdUsbTestPhase.None || r.Phase==NpSdUsbTestPhase.Finished || r.Phase==NpSdUsbTestPhase.RejectedNoStart || unknown && explicitReconcile,"previous attempt unresolved");
            if(unknown)Require(r.Has(NpSdUsbTestValidity.SourceQuiet|NpSdUsbTestValidity.SdQuiet) && r.CatalogFlags==0,"explicit reconciliation requires fresh quiet writable state");
            Require(seconds>=1 && seconds<=120 && seconds<=r.MaximumSeconds,"duration");
            var q=Inner(innerRequest,r.Family,r.ExpectedUid);Require(q.Token!=r.Ticket,"new test requires distinct ticket");
            Require(r.Supports(U(innerRequest,32),U(innerRequest,36)),"unsupported profile");
            Require(r.Frontier.HasValue && r.Capacity.HasValue && r.Frontier<r.Capacity,"catalog frontier");
            if(r.Family==1)Require(U(innerRequest,44)<=r.Capacity,"finite V1 end exceeds capacity");
            var b=new byte[PlanBytes];Put(b,0,PlanMagic);Put(b,4,Abi);Put64(b,8,q.Token);
            MatchWords(innerRequest,12,UidWords(r.Uid));for(int i=0;i<3;i++)Put(b,16+i*4,U(innerRequest,12+i*4));
            Put(b,28,r.Capacity.Value);for(int i=0;i<4;i++)Put(b,32+i*4,r.Cid[i]);Put(b,48,r.Frontier.Value);Put(b,52,seconds);Put(b,56,explicitReconcile?ExplicitNewTestReconcile:0);
            Buffer.BlockCopy(innerRequest,0,b,64,64);Put(b,60,PlanCrc(b));return b;
        }
        internal static bool ValidateMatch(byte[] plan,NpSdUsbTestReport report){
            try{
                Require(report!=null && plan!=null && plan.Length==PlanBytes,"match arguments");
                var r=ParseReport(report.Raw,report.Family,report.ExpectedUid);
                Require(U(plan,0)==PlanMagic && U(plan,4)==Abi && (U(plan,56)&~ExplicitNewTestReconcile)==0 && U(plan,60)==PlanCrc(plan),"plan tag/ABI/flags/CRC");
                var inner=new byte[64];Buffer.BlockCopy(plan,64,inner,0,64);var q=Inner(inner,r.Family,r.ExpectedUid);
                Require(L(plan,8)==q.Token && r.Ticket==q.Token && r.PlanCrc==U(plan,60) && r.InnerId==q.Id &&
                    r.RecordFamily==(uint)r.Family && r.Mask==U(inner,32) && r.Hz==U(inner,36) && r.Seconds==U(plan,52) &&
                    r.ExpectedFrontier==U(plan,48),"attempt/context mismatch");
                Require(r.Seconds>=1 && r.Seconds<=120,"duration mismatch");
                if(r.Family==2)Require(r.TargetNeuralBlocks==U(inner,40),"target NN count mismatch");
                MatchWords(plan,16,UidWords(r.Uid));
                if(r.Has(NpSdUsbTestValidity.CurrentCardIdentity)){
                    Require(U(plan,28)==r.Capacity,"capacity mismatch");MatchWords(plan,32,r.Cid);
                }
                // A removed/unknown card can still report this attempt's
                // terminal failure. This correlation is not SameCard or Pass.
                return true;
            }catch(InvalidDataException){return false;}
        }
    }
}
