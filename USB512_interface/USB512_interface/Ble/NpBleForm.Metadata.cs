using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBleForm
    {
        NpRecordingSettings bleRecordingMetadata;
        NpClosedLoopSettings.Setup bleClosedLoopSetup;
        string bleMetadataFolder,bleDeviceUid;
        uint bleMetadataState=UInt32.MaxValue,bleMetadataFault;
        bool bleMetadataRecordingSeen,bleMetadataFinished,bleAnchorAttempted,bleAnchorStartRejected;
#if NP_FIRST_SYNC_ENABLE
        string bleFirstSyncEvidenceNoticeId;
        void PersistBleFirstSyncAttempt(NpFirstSyncSdAttempt attempt)
        {
            try{
                string folder=attempt.EvidencePath==null?bleMetadataFolder:Path.GetDirectoryName(attempt.EvidencePath);
                string path=NpFirstSyncSdAttemptEvidence.Write(folder,attempt);
                if(bleRecordingMetadata!=null&&folder==bleMetadataFolder){
                    bleRecordingMetadata.Context("first_sync_sd_attempt_file",Path.GetFileName(path));
                    bleRecordingMetadata.Context("first_sync_sd_attempt_declared",true);
                    bleRecordingMetadata.Context("first_sync_sd_record_association","Unverified. This host receipt is not automatically attached to an SD export.");
                }
                if(bleFirstSyncEvidenceNoticeId!=attempt.EvidenceId){bleFirstSyncEvidenceNoticeId=attempt.EvidenceId;Append("SD timing receipt: "+path);}
            }catch(Exception error){
                attempt.EvidenceFailed(error);
                if(bleFirstSyncEvidenceNoticeId!=attempt.EvidenceId){bleFirstSyncEvidenceNoticeId=attempt.EvidenceId;try{Append("SD timing receipt could not be saved: "+error.Message);}catch{}}
            }
        }
#endif
        string FirstSyncEvidenceReport()
        {
#if NP_FIRST_SYNC_ENABLE
            var attempt=bleFirstSyncClient==null?LastFirstSyncAttempt:bleFirstSyncClient.Attempt??LastFirstSyncAttempt;
            if(attempt!=null)return "\r\nSD timing receipt: "+(attempt.EvidencePath??"not saved")+
                (attempt.EvidenceError==null?"":"\r\nReceipt save error: "+attempt.EvidenceError);
#endif
            return String.Empty;
        }
        async Task ObserveBleAcquisitionAnchor(NpSdStatus status) {
            if(bleMetadataFolder==null || bleRecordingMetadata==null || bleAnchorAttempted || bleAnchorStartRejected || status==null || status.Chunks==0 ||
                status.Error!=0 || (status.State!=(V2?3U:1U) && status.State!=(V2?5U:3U)))return;
            bleAnchorAttempted=true;
            if(V2||connection.RecordingFineClockConfigured){
                try{
                    bleRecordingMetadata.Context("ble_legacy_startup_anchor",V2?"Not queried: the legacy BLE anchor is not advertised by V2.":"Not queried: the configured first-sync fine-clock path uses bound on-card evidence.");
                    bleRecordingMetadata.Context("ble_source_t0_policy","Source T0 must come from a valid bound on-card first-sync receipt during SD export; otherwise T0/UTC remain unknown. BLE preview, Start click and receipt arrival do not supply source T0.");
                    bleRecordingMetadata.Write();
                }catch{ /* Optional metadata cannot delay or fail capture. */ }
                return;
            }
            string folder=bleMetadataFolder;int version=recorderVersion,epoch=connection.Epoch;uint id=Next();
            var timing=new NpAcquisitionTiming(folder,bleDeviceUid??("BLE-"+connectedAddress.ToString("X12")),version);
            try {
                CancellationToken owner=CancellationToken.None;
#if NP_FIRST_SYNC_ENABLE
                if(bleFirstSyncStartCancel!=null)owner=bleFirstSyncStartCancel.Token;
#endif
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(owner)){
                    deadline.CancelAfter(750);
                    byte[] receipt=await connection.ExchangeAsync(0xb9,NpAcquisitionAnchor.Request(id),deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                    if(connection.Epoch!=epoch || folder!=bleMetadataFolder)return;
                    timing.Accept(receipt,id);
                }
            }catch(Exception ex){
                if(connection.Epoch!=epoch || folder!=bleMetadataFolder)return;
                timing.Read(()=>{throw new IOException("Optional BLE startup anchor: "+ex.Message);});
            }
            // SD decoding supplies each probe's row offset later. Do not guess from BLE preview.
            timing.Save(new NpFirstSampleRow[0]);
        }

        // The data is on SD. Keep host requests separately until association
        // with a historical recording is confirmed; never invent EEPROM data.
        void BeginBleRecordingMetadata(NpSdStatus before,byte[] request)
        {
            RetireBleRecordingMetadata();
            try {
                bleMetadataFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX","RecordingMetadata",
                    "BLE-"+connectedAddress.ToString("X12"),DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(bleMetadataFolder);
                if(bleClosedLoopSetup!=null)bleClosedLoopSetup.Save(bleMetadataFolder);
                string helper=Path.Combine(Path.GetDirectoryName(typeof(NpBleForm).Assembly.Location),"NPConfigurator","NPConfigurator.exe");
                var settings=new NpRecordingSettings(bleMetadataFolder,helper,"ble_sd",V2,null,null,bleDeviceUid);
                bleRecordingMetadata=settings;bleMetadataRecordingSeen=bleMetadataFinished=bleAnchorAttempted=bleAnchorStartRejected=false;bleMetadataState=UInt32.MaxValue;
                settings.Requested((int)before.Hz,(int)before.Mask,false,false);settings.PlannedLayout((int)before.Mask,false,false);
                connection.SetClockContext(recorderVersion,bleDeviceUid,bleMetadataFolder,before);
                settings.Context("ble_connection_parameters",connection.ReadConnectionParametersEvidence());
                SaveBleRtcCalibration();
                settings.Context("clock_evidence_scope","Raw read-only BLE MCU/PC exchange evidence; AP source pairs absent; on-card recording association unverified.");
                settings.Context("ble_address",connectedAddress.ToString("X12"));settings.Context("recording_start_requested_utc_host",DateTime.UtcNow.ToString("O"));
                settings.Context("start_request_id",BitConverter.ToUInt32(request,12));
                settings.Context("session_id",V2?null:(object)BitConverter.ToUInt32(request,32));
                settings.Context("start_lba",before.Start);settings.Context("card_capacity_sectors",before.Capacity);
                settings.Context("metadata_storage_scope","Host BLE receipt; data is on SD. Association with an exported recording is unverified.");
#if NP_FIRST_SYNC_ENABLE
                settings.Context("optional_sd_timing_receipts","If present: sd_start_*.json in this folder. This pointer alone does not declare an NPSD attempt or associate an SD recording.");
#endif
                settings.Context("identity_note","BLE firmware does not expose per-probe EEPROM identity. SD export uses historical native identity where present.");
                settings.DeviceEvidence("logger_version",recorderVersion);
                if(V2) {
                    System.Collections.Generic.Dictionary<int,string> requests;
                    var apply=Np2ApplyRequests(connectedAddress);
                    if(apply!=null && np2HostConfigurations.TryGetValue(connectedAddress,out requests))foreach(int port in apply) {
                        string json;if((before.Mask&(1U<<(port-1)))==0 || !requests.TryGetValue(port,out json))continue;
                        var staged=NpConfigurationDialog.PrepareV2Request(json,port);settings.Request(port,json,staged.Selection);
                    }
                } else if(before.Mask!=0)foreach(var entry in V1Configurations().Values)
                    if((before.Mask&(1U<<entry.Port))!=0)settings.Request(entry.Port+1,entry.Json,entry.Selection);
                settings.Context("configuration_scope","Retained host requests; not proof of applied routing.");settings.Write();
                if(probePreparation!=null){
                    settings.Context("probe_preparation",NpRecordingSettings.Fields("uid",probePreparation.Uid,"generation",probePreparation.Generation,
                        "state",probePreparation.State,"mask",probePreparation.Mask,"sample_hz",probePreparation.Hz,
                        "scope","Probes/clock only; not recording or first-sample time."));settings.Write();
                }
            } catch(Exception ex){BleMetadataFailed(ex);}
            NotifyHostRecordingPrepared();
        }
        void ObserveBleRecordingMetadata(NpSdStatus status)
        {
            if(bleRecordingMetadata==null || bleMetadataFinished || status==null)return;
            bool recording=status.Error==0 && (V2?status.State==3:status.State==1);
                if(status.Error==0 && status.State==bleMetadataState && status.Fault==bleMetadataFault && (!recording || bleMetadataRecordingSeen))return;
            try {
                if(recording && !bleMetadataRecordingSeen) {
                    bleMetadataRecordingSeen=true;
                    if(status.Mask!=0)bleRecordingMetadata.Actual((int)status.Hz,(int)status.Hz,(int)status.Mask,false,false,"BLE device-confirmed active profile");
                    bleRecordingMetadata.Context("recording_first_observed_utc_host",DateTime.UtcNow.ToString("O"));
                }
                bleMetadataState=status.State;bleMetadataFault=status.Fault;
                bleRecordingMetadata.Context("ble_device_status",NpRecordingSettings.Fields("state",status.State,"fault",status.Fault,"mask",status.Mask,
                    "sample_hz",status.Hz,"stored_chunks",status.Chunks,"start_lba",status.Start,"end_lba",status.End,"last_command_id",status.LastId));
                if(status.Operation==2 && status.Error!=0){
                    bleMetadataFinished=bleAnchorStartRejected=true;
                    bleRecordingMetadata.Finished(false,"Recording start rejected by device: error "+status.Error+"; fault "+status.Fault+".");
                    bleRecordingMetadata.Context("start_rejection",NpRecordingSettings.Fields("error",status.Error,"flags",status.Flags,"request_id",status.RequestId));
                    bleRecordingMetadata.Context("completion_evidence","Validated rejection of this START request; this host request did not start a recording.");
                }
                bool stopped=V2?NpV2SdControl.StopAlreadySatisfied(status):NpV1BleProtocol.StopAlreadySatisfied(status);
                if(!bleMetadataFinished && stopped && (bleMetadataRecordingSeen || status.Fault!=0)) {
                    bleMetadataFinished=true;bool complete=status.Fault==0 && status.State==(V2?5U:3U);
                    bleRecordingMetadata.Finished(complete,complete?null:"Device stopped without a complete recording state.");
                    bleRecordingMetadata.Context("completion_evidence","BLE terminal state only; SD payload has not been downloaded or validated.");
                }
                if(bleMetadataFinished)CloseBleImuJournal();
                bleRecordingMetadata.Write();
            }catch(Exception ex){BleMetadataFailed(ex);}
        }
        void RetireBleRecordingMetadata()
        {
            connection.ClearRecordingFineClock();
            CancelBleImuPoll();CloseBleImuJournal();
            if(bleRecordingMetadata==null)return;
            try {
                if(!bleMetadataFinished){bleRecordingMetadata.Context("connection_ended_utc_host",DateTime.UtcNow.ToString("O"));
                    bleRecordingMetadata.Context("completion_evidence","Connection ended before completion was confirmed; SD recording may continue.");bleRecordingMetadata.Write();}
            }catch(Exception ex){Append("Recording metadata: "+ex.Message);}
            finally{bleRecordingMetadata=null;}
        }
        void BleMetadataFailed(Exception ex){bleRecordingMetadata=null;Append("Recording metadata could not be saved: "+ex.Message+" Recording controls remain available.");}
    }
}
