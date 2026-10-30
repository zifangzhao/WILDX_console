using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface {
    internal sealed class NpFirstSyncSdAttempt {
        internal readonly string Uid;
        internal readonly int Family,ConnectionEpoch;
        internal readonly ulong Token;
        internal readonly uint Id;
        internal readonly ushort Version,Operation;
        readonly byte[] request,legacy;
        byte[] acknowledgementRaw,statusRequest,statusRaw;
        internal readonly string EvidenceId=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow.ToString("O");
        internal string EvidencePhase {get;private set;}="created";
        internal string EvidenceUpdatedUtc {get;private set;}
        internal string EvidencePath {get;private set;}
        internal string EvidenceError {get;private set;}
        internal bool BeforeDeliveryEvidencePersisted {get;private set;}
        internal string StatusError {get;private set;}
        internal bool StartTransportEntered {get;private set;}
        internal bool StatusTransportEntered {get;private set;}
        internal byte[] Request {get{return (byte[])request.Clone();}}
        internal byte[] Legacy {get{return (byte[])legacy.Clone();}}
        internal byte[] AcknowledgementRaw {get{return acknowledgementRaw==null?null:(byte[])acknowledgementRaw.Clone();}}
        internal byte[] StatusRequest {get{return statusRequest==null?null:(byte[])statusRequest.Clone();}}
        internal byte[] StatusRaw {get{return statusRaw==null?null:(byte[])statusRaw.Clone();}}
        internal NpFirstSyncSdReply Acknowledgement {get;private set;}
        internal NpFirstSyncSdReply LastStatus {get;private set;}
        internal string Error {get;private set;}
        internal bool OutcomeUnknown {get{return Acknowledgement==null;}}
        internal bool Rejected {get{return Acknowledgement!=null&&Acknowledgement.Result!=0;}}
        // The attempt is published immediately before delivery. Even an OK ACK
        // only establishes queued/historical acceptance, never recording.
        internal NpFirstSyncSdAttempt(string uid,int family,int epoch,byte[] wrapped,byte[] original){
            Uid=uid;Family=family;ConnectionEpoch=epoch;request=(byte[])wrapped.Clone();legacy=(byte[])original.Clone();
            Token=BitConverter.ToUInt64(request,24);Id=BitConverter.ToUInt32(request,8);Version=BitConverter.ToUInt16(request,4);Operation=BitConverter.ToUInt16(request,6);
        }
        internal void Confirm(NpFirstSyncSdReply reply){Acknowledgement=reply;}
        internal void Observe(NpFirstSyncSdReply reply){LastStatus=reply;}
        internal void Failed(Exception error){Error=error.Message;}
        internal void RetainAcknowledgement(byte[] raw){acknowledgementRaw=raw==null?null:(byte[])raw.Clone();}
        internal void BeginStatus(byte[] raw){statusRequest=(byte[])raw.Clone();statusRaw=null;LastStatus=null;StatusError=null;StatusTransportEntered=false;}
        internal void EnterStartTransport(){StartTransportEntered=true;}
        internal void EnterStatusTransport(){StatusTransportEntered=true;}
        internal void RetainStatus(byte[] raw){statusRaw=raw==null?null:(byte[])raw.Clone();}
        internal void StatusFailed(Exception error){StatusError=error.Message;}
        internal void Phase(string phase){EvidencePhase=phase;EvidenceUpdatedUtc=DateTime.UtcNow.ToString("O");}
        internal void EvidenceFailed(Exception error){EvidenceError=error.Message;}
        internal void EvidenceStored(string path){EvidencePath=path;if(EvidencePhase=="before_delivery")BeforeDeliveryEvidencePersisted=true;}
    }

    // Isolated state only. The caller owns the B8 transport and exclusive lease;
    // leaseValid must include this exact UID/family/connection epoch and owner.
    // Construct a new client for a new connection; never retarget its delegate.
    internal sealed class NpFirstSyncSdClient {
        internal readonly string Uid;
        internal readonly int Family,ConnectionEpoch;
        readonly Func<bool> leaseValid;
        readonly Func<byte[],CancellationToken,Task<byte[]>> exchange;
        int busy;
        bool capsChecked,fineAuxCapsChecked,fineCounter64CapsChecked,grid16CapsChecked,grid16AppendCapsChecked;
        uint supported,fineAuxSupported,fineCounter64Supported,grid16Supported,grid16AppendSupported,queryId;
        ulong tokenSequence;
        internal NpFirstSyncSdAttempt Attempt {get;private set;}
        internal string FallbackReason {get;private set;}
        internal Action<NpFirstSyncSdAttempt> EvidenceChanged=null;
        internal bool CapabilitiesChecked {get{return capsChecked;}}
        internal bool FineAuxCapabilitiesChecked {get{return fineAuxCapsChecked;}}
        internal uint FineAuxSupportedFeatures {get{return fineAuxSupported;}}
        internal string FineAuxFallbackReason {get;private set;}
        internal bool FineCounter64CapabilitiesChecked {get{return fineCounter64CapsChecked;}}
        internal uint FineCounter64SupportedFeatures {get{return fineCounter64Supported;}}
        internal string FineCounter64FallbackReason {get;private set;}
        internal bool Grid16CapabilitiesChecked {get{return grid16CapsChecked;}}
        internal uint Grid16SupportedFeatures {get{return grid16Supported;}}
        internal string Grid16FallbackReason {get;private set;}
        internal bool Grid16AppendCapabilitiesChecked {get{return grid16AppendCapsChecked;}}
        internal uint Grid16AppendSupportedFeatures {get{return grid16AppendSupported;}}
        internal string Grid16AppendFallbackReason {get;private set;}
        // Successfully discovered CAPS on this exact connection only. Reading
        // this value performs no query and never gates ordinary legacy Start.
        internal uint SupportedFeatures {get{return supported;}}

        internal NpFirstSyncSdClient(string uid,int family,int connectionEpoch,Func<bool> leaseValid,
            Func<byte[],CancellationToken,Task<byte[]>> oneShotB8Exchange){
            if(family!=1&&family!=2)throw new ArgumentOutOfRangeException("family");
            if(leaseValid==null)throw new ArgumentNullException("leaseValid");
            if(oneShotB8Exchange==null)throw new ArgumentNullException("oneShotB8Exchange");
            Uid=uid==null?null:uid.ToUpperInvariant();Family=family;ConnectionEpoch=connectionEpoch;
            this.leaseValid=leaseValid;exchange=oneShotB8Exchange;
            try{NpFirstSyncSd.Query(NpFirstSyncSd.Caps,1,family,Uid);}
            catch(InvalidDataException){capsChecked=true;FallbackReason="Exact logger UID is unavailable.";}
            do{tokenSequence=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);}while(tokenSequence==UInt64.MaxValue);
        }
        void CheckLease(){if(!leaseValid())throw new InvalidOperationException("The original logger connection and exclusive lease are required.");}
        void Check(CancellationToken cancel){CheckLease();cancel.ThrowIfCancellationRequested();}
        void Enter(CancellationToken cancel){
            if(Interlocked.CompareExchange(ref busy,1,0)!=0)throw new InvalidOperationException("An SD timing operation is already pending.");
            try{Check(cancel);}catch{Interlocked.Exchange(ref busy,0);throw;}
        }
        void Exit(){Interlocked.Exchange(ref busy,0);}
        void Evidence(NpFirstSyncSdAttempt attempt,string phase){
            attempt.Phase(phase);try{if(EvidenceChanged!=null)EvidenceChanged(attempt);}catch(Exception error){attempt.EvidenceFailed(error);}
        }
        uint NextId(){if(queryId==UInt32.MaxValue)throw new InvalidOperationException("Fresh SD timing request IDs are exhausted.");return ++queryId;}
        ulong NextToken(){if(tokenSequence==UInt64.MaxValue)throw new InvalidOperationException("Fresh SD recording tokens are exhausted.");return ++tokenSequence;}

        async Task<bool> Discover(CancellationToken cancel){
            if(capsChecked)return (supported&1)!=0;
            Check(cancel);var request=NpFirstSyncSd.Query(NpFirstSyncSd.Caps,NextId(),Family,Uid);
            capsChecked=true;FallbackReason="SD timing capability discovery was not completed.";
            try{
                var raw=await exchange((byte[])request.Clone(),cancel);Check(cancel);
                var reply=NpFirstSyncSd.ParseReply(raw,request,Family);
                if(reply.Result==0&&(reply.Supported&1)!=0){supported=reply.Supported;FallbackReason=null;return true;}
                FallbackReason="First-sync SD is unavailable (result "+reply.Result+").";
            }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is TimeoutException||ex is NotSupportedException){
                Check(cancel);FallbackReason="First-sync SD is unavailable: "+ex.Message;
            }
            return false;
        }
        internal async Task<bool> ReadCapabilitiesAsync(CancellationToken cancel){
            Enter(cancel);try{return await Discover(cancel);}finally{Exit();}
        }
        // Explicit optional warm-up only. Start never calls this method. A
        // failed/unknown version2 query cannot remove cached version1 support.
        internal async Task<bool> ReadFineAuxCapabilitiesAsync(CancellationToken cancel){
            Enter(cancel);
            try{
                if(Attempt!=null)return false; // Never retarget a submitted record.
                if(fineAuxCapsChecked)return (fineAuxSupported&13)==13;
                if(!await Discover(cancel) || (supported&NpFirstSyncSd.FeatureFineClock)==0){
                    fineAuxCapsChecked=true;FineAuxFallbackReason="Fine-clock SD capability is unavailable.";return false;
                }
                Check(cancel);var request=NpFirstSyncSd.QueryFineAux(NpFirstSyncSd.Caps,NextId(),Family,Uid);
                fineAuxCapsChecked=true;FineAuxFallbackReason="Fine-AUX SD discovery was not completed.";
                try{
                    var raw=await exchange((byte[])request.Clone(),cancel);Check(cancel);
                    var reply=NpFirstSyncSd.ParseReply(raw,request,Family);
                    if(reply.Result==0){fineAuxSupported=reply.Supported;FineAuxFallbackReason=null;return true;}
                    FineAuxFallbackReason="Fine-AUX SD is unavailable (result "+reply.Result+").";
                }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is TimeoutException||ex is NotSupportedException){
                    Check(cancel);FineAuxFallbackReason="Fine-AUX SD is unavailable: "+ex.Message;
                }
                return false;
            }finally{Exit();}
        }
        // Explicit Connect warm-up only, after version2 advertised bit16.
        // Failed/unknown discovery preserves cached v1; Start never probes v3.
        internal async Task<bool> ReadFineCounter64CapabilitiesAsync(CancellationToken cancel){
            Enter(cancel);
            try{
                if(Attempt!=null)return false;
                if(fineCounter64CapsChecked)return (fineCounter64Supported&29)==29;
                if(!fineAuxCapsChecked)return false; // Caller must warm version2 first.
                if((fineAuxSupported&NpFirstSyncSd.FeatureFineCounter64)==0){
                    fineCounter64CapsChecked=true;FineCounter64FallbackReason="Continuous64 SD capability is unavailable.";return false;
                }
                Check(cancel);var request=NpFirstSyncSd.QueryFineCounter64(NpFirstSyncSd.Caps,NextId(),Family,Uid);
                fineCounter64CapsChecked=true;FineCounter64FallbackReason="Continuous64 SD discovery was not completed.";
                try{
                    var raw=await exchange((byte[])request.Clone(),cancel);Check(cancel);
                    var reply=NpFirstSyncSd.ParseReply(raw,request,Family);
                    if(reply.Result==0){fineCounter64Supported=reply.Supported;FineCounter64FallbackReason=null;return true;}
                    FineCounter64FallbackReason="Continuous64 SD is unavailable (result "+reply.Result+").";
                }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is TimeoutException||ex is NotSupportedException){
                    Check(cancel);FineCounter64FallbackReason="Continuous64 SD is unavailable: "+ex.Message;
                }
                return false;
            }finally{Exit();}
        }
        // Optional V2 Connect warm-up only. Version4 opts into SD16; record25
        // retains its existing clock meaning. Start never probes this version.
        internal async Task<bool> ReadGrid16CapabilitiesAsync(CancellationToken cancel){
            Enter(cancel);
            try{
                if(Family!=2 || Attempt!=null)return false;
                if(grid16CapsChecked)return (grid16Supported&29)==29;
                if(!fineCounter64CapsChecked)return false;
                if((fineCounter64Supported&29)!=29){
                    grid16CapsChecked=true;Grid16FallbackReason="Continuous64 SD capability is unavailable.";return false;
                }
                Check(cancel);var request=NpFirstSyncSd.QueryGrid16(NpFirstSyncSd.Caps,NextId(),Family,Uid);
                grid16CapsChecked=true;Grid16FallbackReason="SD16 discovery was not completed.";
                try{
                    var raw=await exchange((byte[])request.Clone(),cancel);Check(cancel);
                    var reply=NpFirstSyncSd.ParseReply(raw,request,Family);
                    if(reply.Result==0){grid16Supported=reply.Supported;Grid16FallbackReason=null;return true;}
                    Grid16FallbackReason="SD16 is unavailable (result "+reply.Result+").";
                }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is TimeoutException||ex is NotSupportedException){
                    Check(cancel);Grid16FallbackReason="SD16 is unavailable: "+ex.Message;
                }
                return false;
            }finally{Exit();}
        }
        // Optional V2 Connect warm-up only, after a matching version4 CAPS.
        // Version5 selects SD16 schema3/order1; a failed query preserves v4.
        internal async Task<bool> ReadGrid16AppendCapabilitiesAsync(CancellationToken cancel){
            Enter(cancel);
            try{
                if(Family!=2 || Attempt!=null)return false;
                if(grid16AppendCapsChecked)return (grid16AppendSupported&29)==29;
                if(!grid16CapsChecked)return false;
                if((grid16Supported&29)!=29){
                    grid16AppendCapsChecked=true;Grid16AppendFallbackReason="SD16 capability is unavailable.";return false;
                }
                Check(cancel);var request=NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Caps,NextId(),Family,Uid);
                grid16AppendCapsChecked=true;Grid16AppendFallbackReason="SD16 append discovery was not completed.";
                try{
                    var raw=await exchange((byte[])request.Clone(),cancel);Check(cancel);
                    var reply=NpFirstSyncSd.ParseReply(raw,request,Family);
                    if(reply.Result==0){grid16AppendSupported=reply.Supported;Grid16AppendFallbackReason=null;return true;}
                    Grid16AppendFallbackReason="SD16 append is unavailable (result "+reply.Result+").";
                }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is TimeoutException||ex is NotSupportedException){
                    Check(cancel);Grid16AppendFallbackReason="SD16 append is unavailable: "+ex.Message;
                }
                return false;
            }finally{Exit();}
        }
        // Null means no Start delivery was attempted; the caller may separately
        // choose its ordinary legacy Start. Any delivery attempt remains stored,
        // including explicit rejection. This helper never sends legacy Start.
        internal async Task<NpFirstSyncSdAttempt> StartAsync(byte[] legacy,CancellationToken cancel){
            Enter(cancel);
            try{
                if(Attempt!=null)throw new InvalidOperationException("Confirm Stop on the original logger before another SD Start.");
                if(legacy==null||legacy.Length!=64)throw new ArgumentException("Existing 64-byte legacy Start required.","legacy");
                var original=(byte[])legacy.Clone();
                if(!await Discover(cancel))return null;
                uint operation=BitConverter.ToUInt32(original,8);
                if(Family==1&&operation==6 || Family==2&&operation==4&&(supported&2)==0){
                    FallbackReason="Scheduled first-sync SD Start is unsupported.";return null;
                }
                bool fineCounter64=(fineCounter64Supported&29)==29 && (operation!=4 || (fineCounter64Supported&NpFirstSyncSd.FeatureScheduled)!=0);
                bool grid16=Family==2 && (grid16Supported&29)==29 && (operation!=4 || (grid16Supported&NpFirstSyncSd.FeatureScheduled)!=0);
                bool grid16Append=Family==2 && (grid16AppendSupported&29)==29 && (operation!=4 || (grid16AppendSupported&NpFirstSyncSd.FeatureScheduled)!=0);
                bool fineAux=(fineAuxSupported&NpFirstSyncSd.FeatureFineCounter64)==0 && (fineAuxSupported&13)==13 &&
                    (operation!=4 || (fineAuxSupported&NpFirstSyncSd.FeatureScheduled)!=0);
                var wrapped=grid16Append?NpFirstSyncSd.WrapLegacyGrid16Append(original,Family,Uid,NextToken()):
                    grid16?NpFirstSyncSd.WrapLegacyGrid16(original,Family,Uid,NextToken()):
                    fineCounter64?NpFirstSyncSd.WrapLegacyFineCounter64(original,Family,Uid,NextToken()):
                    fineAux?NpFirstSyncSd.WrapLegacyFineAux(original,Family,Uid,NextToken()):NpFirstSyncSd.WrapLegacy(original,Family,Uid,NextToken());
                Check(cancel);uint id=BitConverter.ToUInt32(original,12);if(id>queryId)queryId=id;
                var attempt=new NpFirstSyncSdAttempt(Uid,Family,ConnectionEpoch,wrapped,original);
                FallbackReason=null;Attempt=attempt;
                Evidence(attempt,"before_delivery");
                try{
                    Check(cancel);attempt.EnterStartTransport();
                    var raw=await exchange((byte[])wrapped.Clone(),cancel);attempt.RetainAcknowledgement(raw);Check(cancel);
                    attempt.Confirm(NpFirstSyncSd.ParseReply(raw,wrapped,Family));Evidence(attempt,"acknowledged");return attempt;
                }catch(Exception ex){attempt.Failed(ex);Evidence(attempt,attempt.StartTransportEntered?"start_error":"prepared_not_delivered");throw;}
            }finally{Exit();}
        }
        internal async Task<NpFirstSyncSdReply> StatusAsync(CancellationToken cancel){
            Enter(cancel);
            try{
                var attempt=Attempt;if(attempt==null)throw new InvalidOperationException("No SD Start attempt is bound to this client.");
                var request=attempt.Version==5?NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Status,NextId(),Family,Uid,attempt.Token):
                    attempt.Version==4?NpFirstSyncSd.QueryGrid16(NpFirstSyncSd.Status,NextId(),Family,Uid,attempt.Token):
                    attempt.Version==3?NpFirstSyncSd.QueryFineCounter64(NpFirstSyncSd.Status,NextId(),Family,Uid,attempt.Token):
                    attempt.Version==2?NpFirstSyncSd.QueryFineAux(NpFirstSyncSd.Status,NextId(),Family,Uid,attempt.Token):
                    NpFirstSyncSd.Query(NpFirstSyncSd.Status,NextId(),Family,Uid,attempt.Token);
                attempt.BeginStatus(request);Evidence(attempt,"status_query");
                try{
                    Check(cancel);attempt.EnterStatusTransport();
                    var raw=await exchange((byte[])request.Clone(),cancel);attempt.RetainStatus(raw);Check(cancel);
                    var reply=NpFirstSyncSd.ParseReply(raw,request,Family);attempt.Observe(reply);Evidence(attempt,"status_observed");return reply;
                }catch(Exception error){attempt.StatusFailed(error);Evidence(attempt,attempt.StatusTransportEntered?"status_error":"status_not_delivered");throw;}
            }finally{Exit();}
        }
        // Caller proof must be an actual ordering/cancellation barrier: a fresh
        // confirmed Stop that owns/retires the queued B8 Start on this exact
        // connection. An idle NLD STATUS could overtake queued B8 work; neither
        // that nor NPSD NOT_FOUND is sufficient. No Stop is issued here.
        internal void RetireAfterConfirmedStop(string uid,int family,int connectionEpoch,bool freshOrderedStopConfirmed){
            Enter(CancellationToken.None);
            try{
                if(!freshOrderedStopConfirmed||family!=Family||connectionEpoch!=ConnectionEpoch||!String.Equals(uid,Uid,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Fresh ordered Stop confirmation on the original UID/family/connection epoch is required.");
                if(Attempt!=null)Evidence(Attempt,"retired_after_confirmed_stop");Attempt=null;
            }finally{Exit();}
        }
    }
}
