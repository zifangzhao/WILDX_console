using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface {
    internal sealed partial class NpBleForm {
#if NP_FIRST_SYNC_ENABLE
        NpFirstSyncSdClient bleFirstSyncClient;
        CancellationTokenSource bleFirstSyncStartCancel,bleFirstSyncWarmCancel;
        ulong bleFirstSyncPeer;
        uint bleFirstSyncStopId,bleFirstSyncLastStopId;
        bool bleFirstSyncRetiringStop,bleFirstSyncDefiniteStopRejection;
        internal NpFirstSyncSdAttempt LastFirstSyncAttempt {get;private set;}
        string ValidFirstSyncUid(string uid){
            try{NpFirstSyncSd.Query(NpFirstSyncSd.Caps,1,recorderVersion,uid);return uid.ToUpperInvariant();}
            catch(InvalidDataException){return null;}
        }
        string KnownFirstSyncUid(){
            string direct=ValidFirstSyncUid(bleDeviceUid);
            string identity=firmwareIdentity!=null&&firmwareIdentity.Family==(uint)recorderVersion?ValidFirstSyncUid(firmwareIdentity.Uid):null;
            if(direct!=null&&identity!=null&&!String.Equals(direct,identity,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Logger UID evidence changed. Reconnect before recording.");
            return direct??identity;
        }
        bool FirstSyncConnectionMatches(NpFirstSyncSdClient client){
            return client!=null&&connection.Connected&&connection.Epoch==client.ConnectionEpoch&&recorderVersion==client.Family&&
                connectedAddress==bleFirstSyncPeer&&String.Equals(KnownFirstSyncUid(),client.Uid,StringComparison.OrdinalIgnoreCase);
        }
        NpFirstSyncSdClient BindFirstSyncClient(){
            if(bleFirstSyncClient!=null){
                if(!FirstSyncConnectionMatches(bleFirstSyncClient))throw new IOException("The original SD Start connection changed. Reconnect and inspect its recording status.");
                return bleFirstSyncClient;
            }
            string uid=KnownFirstSyncUid();if(uid==null||connectedAddress==0)return null;
            bleFirstSyncPeer=connectedAddress;NpFirstSyncSdClient client=null;
            client=new NpFirstSyncSdClient(uid,recorderVersion,connection.Epoch,
                ()=>ReferenceEquals(bleFirstSyncClient,client)&&(busy||bleFirstSyncRetiringStop)&&!hostBusy&&!closing&&FirstSyncConnectionMatches(client),
                (q,c)=>connection.ExchangeAsync(0xb8,q,c));
            client.EvidenceChanged=PersistBleFirstSyncAttempt;
            bleFirstSyncClient=client;return client;
        }
        async Task WarmBleFirstSyncCapabilities(){
            if(!busy||hostBusy||closing||stopRequested||!connection.Connected||connectedAddress==0||
                recorderVersion!=1&&recorderVersion!=2||current==null||current.Busy||current.Fault!=0||current.Error!=0||
                !(recorderVersion==1?NpV1BleProtocol.CanConfigure(current):NpV2SdControl.CanConfigure(current))||
                bleFirstSyncClient!=null&&bleFirstSyncClient.Attempt!=null)return;
            int epoch=connection.Epoch,family=recorderVersion;ulong peer=connectedAddress;var status=current;
            Func<bool> same=()=>busy&&!hostBusy&&!closing&&!stopRequested&&connection.Connected&&connection.Epoch==epoch&&
                recorderVersion==family&&connectedAddress==peer&&ReferenceEquals(current,status)&&!status.Busy&&status.Fault==0&&status.Error==0&&
                (family==1?NpV1BleProtocol.CanConfigure(status):NpV2SdControl.CanConfigure(status));
            using(var warm=new CancellationTokenSource()){
                bleFirstSyncWarmCancel=warm;
                try{
                    string uid=KnownFirstSyncUid();
                    if(uid==null&&family==1){
                        // STATUS is read-only even when preparation is disabled.
                        // Its correlated MCU UID is identity only, never proof
                        // that probes are prepared or recording has started.
                        var request=NpProbePreparation.Request(NpProbePreparation.Status,Next());byte[] raw;
                        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(warm.Token)){
                            deadline.CancelAfter(750);raw=await connection.ExchangeAsync(0xb8,request,deadline.Token);
                            deadline.Token.ThrowIfCancellationRequested();
                        }
                        if(!same())return;var identity=NpProbePreparation.Parse(raw,request);
                        if(identity.Result!=0&&identity.Result!=1)return;
                        uid=ValidFirstSyncUid(identity.Uid);if(uid==null)return;
                        string previous=KnownFirstSyncUid();if(previous!=null&&!String.Equals(previous,uid,StringComparison.OrdinalIgnoreCase))return;
                        bleDeviceUid=uid;
                    }
                    if(uid==null||!same())return;var client=BindFirstSyncClient();if(client==null)return;
                    using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(warm.Token)){
                        deadline.CancelAfter(750);if(!await client.ReadCapabilitiesAsync(deadline.Token))return;
                    }
                    if(!same()||(client.SupportedFeatures&NpFirstSyncSd.FeatureFineClock)==0)return;
                    using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(warm.Token)){
                        deadline.CancelAfter(750);await client.ReadFineAuxCapabilitiesAsync(deadline.Token);
                    }
                    if(!same()||(client.FineAuxSupportedFeatures&NpFirstSyncSd.FeatureFineCounter64)==0)return;
                    using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(warm.Token)){
                        deadline.CancelAfter(750);await client.ReadFineCounter64CapabilitiesAsync(deadline.Token);
                    }
                    if(same()&&family==2&&(client.FineCounter64SupportedFeatures&29)==29){
                        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(warm.Token)){
                            deadline.CancelAfter(750);
                            try{await client.ReadGrid16CapabilitiesAsync(deadline.Token);}
                            catch(OperationCanceledException) when(!warm.IsCancellationRequested&&same()){
                                // An old provider's optional local timeout must
                                // not suppress already-supported v3 RTC warmup.
                            }
                        }
                    }
                    if(same()&&family==2&&(client.Grid16SupportedFeatures&29)==29){
                        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(warm.Token)){
                            deadline.CancelAfter(750);
                            try{await client.ReadGrid16AppendCapabilitiesAsync(deadline.Token);}
                            catch(OperationCanceledException) when(!warm.IsCancellationRequested&&same()){
                                // Optional v5 discovery cannot remove v4 or RTC support.
                            }
                        }
                    }
                    if(same()&&client.FineCounter64SupportedFeatures!=0)await WarmBleHardwareRtc(client,warm.Token);
                }catch(Exception){/* Optional discovery never prevents ordinary connection/legacy recording. */}
                finally{if(ReferenceEquals(bleFirstSyncWarmCancel,warm))bleFirstSyncWarmCancel=null;}
            }
        }
        void RequireNoPendingFirstSyncStart(){
            if(recorderVersion==1||recorderVersion==2)KnownFirstSyncUid();
            if(bleFirstSyncClient!=null&&bleFirstSyncClient.Attempt!=null)
                throw new IOException("Confirm Stop for the previous SD Start before starting another recording.");
        }
        void CancelBleFirstSyncStart(){CancelBleRtc();connection.ClearRecordingFineClock();if(bleFirstSyncStartCancel!=null)bleFirstSyncStartCancel.Cancel();if(bleFirstSyncWarmCancel!=null)bleFirstSyncWarmCancel.Cancel();}
        void ResetBleFirstSync(){
            CancelBleFirstSyncStart();
            ResetBleRtc();
            if(bleFirstSyncClient!=null&&bleFirstSyncClient.Attempt!=null)LastFirstSyncAttempt=bleFirstSyncClient.Attempt;
            bleFirstSyncStartCancel=bleFirstSyncWarmCancel=null;bleFirstSyncClient=null;bleFirstSyncPeer=0;bleFirstSyncStopId=bleFirstSyncLastStopId=0;bleFirstSyncDefiniteStopRejection=false;
        }
        async Task<NpSdStatus> SendBleSdStart(byte[] legacy){
            RequireStartNotCanceled();
            if(NpSdGeometry.U(legacy,16)==0)return await Send(legacy); // Existing media-only path.
            var client=BindFirstSyncClient();if(client==null)return await Send(legacy);
            using(var cancel=new CancellationTokenSource()){
                bleFirstSyncStartCancel=cancel;
                try{
                    sequence=NpSdGeometry.U(legacy,12); // Preserve the logical legacy mutation ID, even after a lost ACK.
                    var attempt=await client.StartAsync(legacy,cancel.Token);RequireStartNotCanceled();
                    if(attempt==null)return await Send(legacy);
                    if(attempt.Rejected){
                        configurationReceipts.Reset();configurationStatus.Text="Recording was rejected before capture started.";
                        throw new IOException("First-sync SD Start rejected: "+attempt.Acknowledgement.Result+". No legacy Start was sent.");
                    }
                    // NPSA confirms queued/historical acceptance only. The normal
                    // recorder status controls all recording/IMU/UI state.
                    if((client.SupportedFeatures&NpFirstSyncSd.FeatureFineClock)!=0 &&
                        connection.ConfigureRecordingFineClock(client,attempt,()=>ReferenceEquals(bleFirstSyncClient,client)&&
                            !closing&&FirstSyncConnectionMatches(client))){
                        recordingClockSchedule.Reset();fineRecordingClockStart=-1;nextFineRecordingClock=0;
                    }
                    return await ReadStatus();
                }finally{
                    if(client.Attempt!=null)LastFirstSyncAttempt=client.Attempt;
                    if(ReferenceEquals(bleFirstSyncStartCancel,cancel))bleFirstSyncStartCancel=null;
                }
            }
        }
        Task<bool> PrepareBleFirstSyncStop(){
            var client=bleFirstSyncClient;if(client==null||client.Attempt==null)return Task.FromResult(false);
            if(!FirstSyncConnectionMatches(client))throw new IOException("The SD Start belongs to a different connection. No Stop was sent.");
            return Task.FromResult(true);
        }
        uint BleFirstSyncStopRequestId(NpSdStatus status,uint ordinaryId){
            var attempt=bleFirstSyncClient==null?null:bleFirstSyncClient.Attempt;if(attempt==null)return ordinaryId;
            uint latest=attempt.Id;
            if(status!=null&&unchecked((int)(status.LastId-latest))>0)latest=status.LastId;
            if(bleFirstSyncLastStopId!=0&&unchecked((int)(bleFirstSyncLastStopId-latest))>0)latest=bleFirstSyncLastStopId;
            uint id=unchecked(latest+1);if(id==0)id=1;
            // Both controllers admit mutations in signed modulo-32 order. A
            // later Stop makes any still-delayed B8 Start stale at dispatch.
            if(unchecked((int)(id-attempt.Id))<=0||status!=null&&unchecked((int)(id-status.LastId))<=0||
                bleFirstSyncLastStopId!=0&&unchecked((int)(id-bleFirstSyncLastStopId))<=0)
                throw new IOException("No unambiguous Stop request ID follows the pending SD Start and recorder status.");
            return id;
        }
        void MarkBleFirstSyncStop(uint id){
            bleFirstSyncDefiniteStopRejection=false;
            if(bleFirstSyncClient!=null&&bleFirstSyncClient.Attempt!=null)bleFirstSyncStopId=bleFirstSyncLastStopId=id;
        }
        async Task<NpSdStatus> SendBleSdStop(byte[] request){
            uint id=NpSdGeometry.U(request,12);MarkBleFirstSyncStop(id);
            try{return await Send(request);}
            catch(NpSdCommandException ex){
                var client=bleFirstSyncClient;
                if(client!=null&&client.Attempt!=null&&FirstSyncConnectionMatches(client)&&ex.Version==client.Family&&
                    ex.Status.Error!=0&&ex.Status.Operation==3&&ex.Status.RequestId==id&&bleFirstSyncStopId==id){
                    // Definite rejection permits a later explicit Stop with a
                    // new ID. Uncertain delivery keeps its no-replay marker.
                    bleFirstSyncStopId=0;bleFirstSyncDefiniteStopRejection=true;
                    if(V2)pendingStop=null;
                }
                throw;
            }
        }
        void CompleteBleFirstSyncStop(NpSdStatus status){
            var client=bleFirstSyncClient;
            if(client==null||client.Attempt==null||bleFirstSyncStopId==0||status==null||!StopConfirmed(status)||status.LastId!=bleFirstSyncStopId)return;
            if(!FirstSyncConnectionMatches(client))return;
            // Pure local retirement under the exact normal-status owner's lease;
            // background terminal observation performs no new control transfer.
            bleFirstSyncRetiringStop=true;
            try{client.RetireAfterConfirmedStop(client.Uid,client.Family,client.ConnectionEpoch,true);connection.ClearRecordingFineClock();bleFirstSyncStopId=bleFirstSyncLastStopId=0;}
            finally{bleFirstSyncRetiringStop=false;}
        }
        bool ObserveIssuedBleFirstSyncStop(NpSdStatus status){
            if(bleFirstSyncStopId==0||bleFirstSyncClient==null||bleFirstSyncClient.Attempt==null)return false;
            if(!FirstSyncConnectionMatches(bleFirstSyncClient))throw new IOException("Stop completion belongs to the original logger connection.");
            CompleteBleFirstSyncStop(status);
            if(bleFirstSyncClient.Attempt==null)ReportStopped(status);
            else Append("Stop completion is unconfirmed; read status. No Stop was replayed.");
            return true;
        }
#else
        Task WarmBleFirstSyncCapabilities(){return Task.CompletedTask;}
        void RequireNoPendingFirstSyncStart(){}
        void CancelBleFirstSyncStart(){}
        void ResetBleFirstSync(){}
        Task<NpSdStatus> SendBleSdStart(byte[] legacy){return Send(legacy);}
        Task<bool> PrepareBleFirstSyncStop(){return Task.FromResult(false);}
        uint BleFirstSyncStopRequestId(NpSdStatus status,uint ordinaryId){return ordinaryId;}
        void MarkBleFirstSyncStop(uint id){}
        Task<NpSdStatus> SendBleSdStop(byte[] request){return Send(request);}
        bool bleFirstSyncDefiniteStopRejection {get{return false;}}
        void CompleteBleFirstSyncStop(NpSdStatus status){}
        bool ObserveIssuedBleFirstSyncStop(NpSdStatus status){return false;}
#endif
    }
}
