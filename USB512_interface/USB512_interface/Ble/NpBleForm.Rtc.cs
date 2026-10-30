using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface {
    internal sealed partial class NpBleForm {
#if NP_FIRST_SYNC_ENABLE
        int bleRtcEpoch,bleRtcFamily;
        ulong bleRtcPeer;
        string bleRtcUid,bleRtcError;
        bool bleRtcSupported,bleRtcAttempted;
        object bleRtcSetupKey,bleRtcPendingSetup,bleRtcCalibrationSetup;
        uint bleRtcPendingMask,bleRtcPendingHz;
        sealed class BleRtcUncertainty {
            internal NpHardwareRtcCalibration Attempt;internal object Key;
            internal int Epoch,Family;internal ulong Peer;internal byte[] Probe;
            internal DateTime Begin;internal long Qpc;
        }
        readonly Dictionary<string,BleRtcUncertainty> bleRtcUncertain=new Dictionary<string,BleRtcUncertainty>(StringComparer.OrdinalIgnoreCase);
        uint bleRtcMask,bleRtcHz;
        byte[] bleRtcProbe;
        CancellationTokenSource bleRtcCancel;
        NpHardwareRtcCalibration bleRtcCalibration;
        NpHardwareRtcCalibration bleRtcPreviousCompleted;
        DateTime bleRtcHostBegin;
        long bleRtcHostQpc;
        bool BleRtcBound(){return connection.Connected&&connection.Epoch==bleRtcEpoch&&recorderVersion==bleRtcFamily&&
            connectedAddress==bleRtcPeer&&String.Equals(KnownFirstSyncUid(),bleRtcUid,StringComparison.OrdinalIgnoreCase);}
        void CancelBleRtc(){if(bleRtcCancel!=null)bleRtcCancel.Cancel();}
        void ResetBleRtc(){CancelBleRtc();bleRtcSupported=bleRtcAttempted=false;bleRtcUid=null;bleRtcProbe=null;bleRtcCalibration=bleRtcPreviousCompleted=null;bleRtcError=null;bleRtcSetupKey=bleRtcPendingSetup=bleRtcCalibrationSetup=null;}
        void BeginBleRtcSettingsSetup(NpSdStatus status){bleRtcSetupKey=bleRtcPendingSetup=new object();bleRtcPendingMask=status.Mask;bleRtcPendingHz=status.Hz;}
        void BeginBleRtcRecordingSetup(NpSdStatus status){
            bleRtcSetupKey=bleRtcPendingSetup!=null&&bleRtcPendingMask==status.Mask&&bleRtcPendingHz==status.Hz?bleRtcPendingSetup:new object();
            bleRtcPendingSetup=null;
        }
        bool HasBleRtcUncertainty {get{string uid=KnownFirstSyncUid();return uid!=null&&bleRtcUncertain.ContainsKey(uid);}}
        bool BleRtcArmed {get{return probePreparation!=null||probePreparationAttempt!=null;}}
        bool ModernBleRtc {get{return bleRtcSupported&&BleRtcBound()||HasBleRtcUncertainty;}}
        void ShowBleRtcStatus(){clockStatus.Text=bleRtcCalibration!=null&&bleRtcCalibration.Confirmed&&ReferenceEquals(bleRtcCalibrationSetup,bleRtcSetupKey)&&!HasBleRtcUncertainty?"RTC synced":"RTC time unconfirmed";}
        // Only the successful continuous64 Connect warmup may call this once.
        // A read-only unsupported/old-provider result never trains eight samples.
        async Task WarmBleHardwareRtc(NpFirstSyncSdClient client,CancellationToken cancel){
            if(client==null||client.FineCounter64SupportedFeatures==0||!FirstSyncConnectionMatches(client)||
                !busy||hostBusy||closing||stopRequested||BleRtcArmed)return;
            if(bleRtcUid!=null&&BleRtcBound())return;
            bleRtcEpoch=connection.Epoch;bleRtcFamily=recorderVersion;bleRtcPeer=connectedAddress;bleRtcUid=client.Uid;
            int epoch=bleRtcEpoch,family=bleRtcFamily;ulong peer=bleRtcPeer;string uid=bleRtcUid;
            Func<bool> same=()=>bleRtcEpoch==epoch&&bleRtcFamily==family&&bleRtcPeer==peer&&bleRtcUid==uid&&FirstSyncConnectionMatches(client)&&BleRtcBound();
            bleRtcSupported=bleRtcAttempted=false;bleRtcProbe=null;bleRtcCalibration=null;bleRtcError=null;
            try{
                var request=NpHardwareRtcClock.Request(0,Next());byte[] probe;
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                    deadline.CancelAfter(750);probe=await connection.ExchangeAsync(0xb9,request,deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                }
                if(!same()||!busy||hostBusy||closing||stopRequested||BleRtcArmed)return;
                bleRtcProbe=probe;var reply=NpHardwareRtcClock.Parse(probe,request);
                bleRtcSupported=reply.Error==0&&(reply.Flags&(NpHardwareRtcClock.Hardware|NpHardwareRtcClock.HseDiv32))==
                    (NpHardwareRtcClock.Hardware|NpHardwareRtcClock.HseDiv32);
            }catch(Exception ex){if(same())bleRtcError=ex.Message;/* Optional capability discovery; legacy recording remains usable. */}
        }
        // Return true for a supported v2 path even on failure/unknown delivery:
        // a later legacy synchronization must never follow its COMMIT attempt.
        async Task<bool> PrepareBleHardwareRtc(NpSdStatus status,bool explicitRequest=false){
            if(HasBleRtcUncertainty)return true; // Exact UID, retained across reconnect; no COMMIT or legacy fallback.
            if(!bleRtcSupported)return false;
            if(!BleRtcBound())throw new IOException("RTC setup belongs to the original logger connection. Reconnect and read status.");
            if(BleRtcArmed)return true; // No phase write after PREPARE, including unknown PREPARE.
            if(!busy||hostBusy||closing||stopRequested||status==null||status.Busy||status.Fault!=0||status.Error!=0||
                !(V2?NpV2SdControl.CanConfigure(status):NpV1BleProtocol.CanConfigure(status)))return true;
            if(explicitRequest)BeginBleRtcSettingsSetup(status);
            var key=bleRtcSetupKey??(bleRtcSetupKey=new object());
            if(bleRtcAttempted&&ReferenceEquals(bleRtcCalibrationSetup,key)&&status.Mask==bleRtcMask&&status.Hz==bleRtcHz)return true;
            string uid=bleRtcUid;int epoch=bleRtcEpoch,family=bleRtcFamily;ulong peer=bleRtcPeer;byte[] probe=bleRtcProbe;
            Action check=()=>{
                RequireStartNotCanceled();
                if(!BleRtcBound()||bleRtcUid!=uid||bleRtcEpoch!=epoch||bleRtcFamily!=family||bleRtcPeer!=peer||
                    !ReferenceEquals(bleRtcSetupKey,key)||!busy||hostBusy||closing||BleRtcArmed)
                    throw new IOException("RTC setup connection or stopped owner changed.");
            };
            if(bleRtcCalibration!=null&&bleRtcCalibration.Confirmed)bleRtcPreviousCompleted=bleRtcCalibration;
            bleRtcCalibration=null;bleRtcError=null;
            bleRtcAttempted=true;bleRtcCalibrationSetup=key;bleRtcMask=status.Mask;bleRtcHz=status.Hz;bleRtcHostBegin=DateTime.UtcNow;bleRtcHostQpc=Stopwatch.GetTimestamp();
            DateTime begin=bleRtcHostBegin;long qpc=bleRtcHostQpc;
            using(var cancel=new CancellationTokenSource()){
                bleRtcCancel=cancel;
                try{
                    var completed=await NpHardwareRtcClock.Synchronize(async(q,c)=>{
                        check();byte[] raw;
                        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(c)){
                            deadline.CancelAfter(750);raw=await connection.ExchangeAsync(0xb9,q,deadline.Token);
                            deadline.Token.ThrowIfCancellationRequested();
                        }
                        check();return raw;
                    },Next,state=>family==1?state==1||state==5:state==0||state==1||state==5||state==7,cancel.Token);
                    check();bleRtcCalibration=completed;clockStatus.Text="RTC synced";
                }catch(NpHardwareRtcCalibrationException ex){
                    if(ex.Attempt.CommitAttempted&&!ex.Attempt.Confirmed)bleRtcUncertain[uid]=new BleRtcUncertainty {
                        Attempt=ex.Attempt,Key=key,Epoch=epoch,Family=family,Peer=peer,Probe=probe,Begin=begin,Qpc=qpc};
                    check();bleRtcCalibration=ex.Attempt;bleRtcError=ex.Message;
                    clockStatus.Text="RTC time unconfirmed";Append("RTC synchronization unconfirmed; recording remains available.");
                }finally{if(ReferenceEquals(bleRtcCancel,cancel))bleRtcCancel=null;}
            }
            return true;
        }
        void SaveBleRtcCalibration(){
            string known=KnownFirstSyncUid();BleRtcUncertainty retained;
            if(bleRecordingMetadata!=null&&known!=null&&bleRtcUncertain.TryGetValue(known,out retained)){
                NpHardwareRtcEvidence.Save(bleRecordingMetadata,"BLE",known,retained.Epoch,retained.Begin,retained.Qpc,true,retained.Probe,retained.Attempt,retained.Attempt.Error);
                bleRecordingMetadata.Context("hardware_rtc_retained_uncertainty",NpRecordingSettings.Fields("original_family",retained.Family,"original_peer",retained.Peer.ToString("X12"),
                    "same_logical_setup",ReferenceEquals(retained.Key,bleRtcSetupKey),"scope","Historical unknown mutative attempt for this exact UID; reconnect does not authorize another COMMIT."));return;
            }
            if(bleRecordingMetadata==null||bleRtcUid==null||!BleRtcBound())return;
            NpHardwareRtcEvidence.Save(bleRecordingMetadata,"BLE",bleRtcUid,bleRtcEpoch,bleRtcHostBegin,bleRtcHostQpc,
                bleRtcSupported,bleRtcProbe,bleRtcCalibration,bleRtcError);
            bleRecordingMetadata.Context("hardware_rtc_same_logical_setup",ReferenceEquals(bleRtcCalibrationSetup,bleRtcSetupKey));
            if(bleRtcPreviousCompleted!=null)bleRecordingMetadata.Context("hardware_rtc_previous_completed",NpRecordingSettings.Fields(
                "commit_request_base64",Convert.ToBase64String(bleRtcPreviousCompleted.CommitRequest),"commit_ack_base64",Convert.ToBase64String(bleRtcPreviousCompleted.CommitAcknowledgement),
                "last_readback_base64",Convert.ToBase64String(bleRtcPreviousCompleted.LastReply)));
        }
#else
        void CancelBleRtc(){}
        void ResetBleRtc(){}
        void BeginBleRtcSettingsSetup(NpSdStatus status){}
        void BeginBleRtcRecordingSetup(NpSdStatus status){}
        bool ModernBleRtc {get{return false;}}
        void ShowBleRtcStatus(){}
        Task<bool> PrepareBleHardwareRtc(NpSdStatus status,bool explicitRequest=false){return Task.FromResult(false);}
        void SaveBleRtcCalibration(){}
#endif
    }
#if NP_FIRST_SYNC_ENABLE
    // Historical control evidence only. This never declares recording T0,
    // current hardware phase, sub-ms PC accuracy or RTC-to-NP sample lock.
    internal static class NpHardwareRtcEvidence {
        static string Raw(byte[] b){return b==null?null:Convert.ToBase64String(b);}
        internal static void Save(NpRecordingSettings settings,string transport,string uid,int epoch,DateTime begin,long qpc,
            bool supported,byte[] probe,NpHardwareRtcCalibration attempt,string error){
            settings.Context("hardware_rtc_calibration",NpRecordingSettings.Fields(
                "transport",transport,"uid",uid,"connection_epoch",epoch==0?null:(object)epoch,"supported_v2",supported,
                "host_observation_begin_utc",begin==DateTime.MinValue?null:begin.ToString("O"),"host_qpc_begin",qpc,"host_qpc_hz",Stopwatch.Frequency,
                "commit_attempted",attempt!=null&&attempt.CommitAttempted,"historical_readback_confirmed",attempt!=null&&attempt.Confirmed,
                "setting_generation",attempt==null||attempt.Receipt==null?null:(object)attempt.Receipt.Generation,
                "declared_pc_calibration_uncertainty_us",attempt==null?null:(object)attempt.PcUncertaintyUs,
                "readback_pc_phase_lower_us",attempt==null?null:(object)attempt.PhaseLowerUs,"readback_pc_phase_upper_us",attempt==null?null:(object)attempt.PhaseUpperUs,"error",error,
                "probe_base64",Raw(probe),"commit_request_base64",attempt==null?null:Raw(attempt.CommitRequest),
                "commit_ack_base64",attempt==null?null:Raw(attempt.CommitAcknowledgement),"last_readback_base64",attempt==null?null:Raw(attempt.LastReply),
                "scope","Historical stopped control attempt on this bound connection; recording start requires its own on-device RTC/NP cross-stamp."));
        }
    }
#endif
}
