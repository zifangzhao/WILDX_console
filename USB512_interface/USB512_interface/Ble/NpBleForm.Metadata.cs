using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBleForm
    {
        NpRecordingSettings bleRecordingMetadata;
        NpClosedLoopSettings.Setup bleClosedLoopSetup;
        string bleMetadataFolder,bleDeviceUid;
        string blePendingSessionFolder;
        readonly TextBox bleDestinationFolder=new TextBox {Name="bleDestinationFolder",Width=600,AccessibleName="BLE recording output folder"};
        readonly CheckBox hostGpioEnabled=new CheckBox {Name="bleGpioRtcEnabled",Text="GPIO / RTC logging",AutoSize=true};
        Button bleSelectFolder;
        internal Func<string> ChooseBleSessionFolder;
        internal CheckBox HostGpioLogging {get{return hostGpioEnabled;}}
        uint bleMetadataState=UInt32.MaxValue,bleMetadataFault;
        bool bleMetadataRecordingSeen,bleMetadataFinished,bleAnchorAttempted,bleAnchorStartRejected;
        Control BuildBleSessionControls() {
            bleDestinationFolder.Text=Properties.Settings.Default.BleRecordingFolder;
            var options=new FlowLayoutPanel();
            bleSelectFolder=new Button {Text="Select folder",AutoSize=true};
            bleSelectFolder.Click+=delegate {string folder=SelectBleSessionFolder();if(folder!=null)bleDestinationFolder.Text=folder;};
            options.Controls.Add(bleSelectFolder);options.Controls.Add(hostGpioEnabled);
            return FieldGrid("Output folder",bleDestinationFolder,null,null,options);
        }
        string SelectBleSessionFolder() {
            using(var dialog=new FolderBrowserDialog {Description="Choose where to save BLE session metadata, timing and GPIO logs.",SelectedPath=bleDestinationFolder.Text})
                return dialog.ShowDialog(DialogOwner)==DialogResult.OK?dialog.SelectedPath:null;
        }
        bool PrepareBleSessionFolder() {
            blePendingSessionFolder=null;
            string root=bleDestinationFolder.Text.Trim();
            if(root.Length==0){root=ChooseBleSessionFolder==null?SelectBleSessionFolder():ChooseBleSessionFolder();if(String.IsNullOrWhiteSpace(root))return false;}
            root=Path.GetFullPath(root);
            string folder=NpFolderNaming.CreateFresh(root,NpFolderNaming.DownloadName("BLE-"+connectedAddress.ToString("X12"),DateTime.Now,""));
            using(var probe=new FileStream(Path.Combine(folder,".wildx-write-"+Guid.NewGuid().ToString("N")),FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){
                probe.WriteByte(0);probe.Flush(true);
            }
            bleDestinationFolder.Text=root;blePendingSessionFolder=folder;
            try{Properties.Settings.Default.BleRecordingFolder=root;Properties.Settings.Default.Save();}
            catch(Exception ex){Append("Output folder preference could not be saved: "+ex.Message);}
            try{NpFineClockEvidenceImport.RegisterSessionFolder(folder,connectedAddress.ToString("X12"));}
            catch(Exception ex){Append("Automatic SD timing-log lookup could not be saved: "+ex.Message+" Session files will remain in "+folder);}
            return true;
        }
        void BeginBleSessionStorage() {
            if(String.IsNullOrEmpty(blePendingSessionFolder))throw new IOException("Select a writable BLE output folder before recording.");
            RetireBleRecordingMetadata();bleMetadataFolder=blePendingSessionFolder;
            connection.UseRecordingClockFolder(bleMetadataFolder);
            connection.SetClockContext(recorderVersion,bleDeviceUid,bleMetadataFolder,current);
            File.WriteAllText(Path.Combine(bleMetadataFolder,"pc_clock.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(NpHostClock.Receipt));
        }
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
            if(String.IsNullOrEmpty(blePendingSessionFolder))throw new IOException("Select a writable BLE output folder before recording.");
            if(bleMetadataFolder!=blePendingSessionFolder)BeginBleSessionStorage();
            blePendingSessionFolder=null;
            try {
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
                settings.Context("host_session_folder",bleMetadataFolder);
                settings.Context("folder_time_scope","Folder timestamp is the local host request time, not the first neural sample or authoritative recording T0.");
                settings.Context("session_log_file","ble_session.log");
                var calibration=NpCalibrationStore.Get("ble:"+connectedAddress.ToString("X12"));
                if(calibration.Count!=0){
                    string directory=Path.Combine(bleMetadataFolder,"preview_calibration");Directory.CreateDirectory(directory);calibration.Save(directory);
                    var inputs=new System.Collections.Generic.List<object>();
                    foreach(int port in calibration.Ports){var profile=calibration.Get(port);
                        inputs.Add(new {probe=port,serial=profile.Serial,model=profile.Model,ap_gain=profile.ApGain,lfp_gain=profile.LfpGain,
                            gain_sha256=profile.GainHash,adc_sha256=profile.AdcHash});}
                    NpCalibrationStore.SaveImros("ble:"+connectedAddress.ToString("X12"),directory);
                    File.WriteAllText(Path.Combine(directory,"preview_calibration.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(
                        new {schema=1,scope="BLE waveform preview inputs only; on-device SD data is not calibrated by this host snapshot",probes=inputs}));
                    settings.Context("preview_calibration_file","preview_calibration/preview_calibration.json");
                }
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
            } catch(Exception ex){BleMetadataFailed(ex);throw new IOException("BLE session metadata could not be saved. Recording was not started.",ex);}
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
