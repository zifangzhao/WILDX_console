using System;
using System.IO;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBleForm
    {
        NpRecordingSettings bleRecordingMetadata;
        string bleMetadataFolder,bleDeviceUid;
        uint bleMetadataState=UInt32.MaxValue,bleMetadataFault;
        bool bleMetadataRecordingSeen,bleMetadataFinished;

        // The data is on SD. Keep host requests separately until association
        // with a historical recording is confirmed; never invent EEPROM data.
        void BeginBleRecordingMetadata(NpSdStatus before,byte[] request)
        {
            RetireBleRecordingMetadata();
            try {
                bleMetadataFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX","RecordingMetadata",
                    "BLE-"+connectedAddress.ToString("X12"),DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(bleMetadataFolder);
                string helper=Path.Combine(Path.GetDirectoryName(typeof(NpBleForm).Assembly.Location),"NPConfigurator","NPConfigurator.exe");
                var settings=new NpRecordingSettings(bleMetadataFolder,helper,"ble_sd",V2,null,null,bleDeviceUid);
                bleRecordingMetadata=settings;bleMetadataRecordingSeen=bleMetadataFinished=false;bleMetadataState=UInt32.MaxValue;
                settings.Requested((int)before.Hz,(int)before.Mask,false,false);settings.PlannedLayout((int)before.Mask,false,false);
                settings.Context("ble_address",connectedAddress.ToString("X12"));settings.Context("recording_start_requested_utc_host",DateTime.UtcNow.ToString("O"));
                settings.Context("start_request_id",BitConverter.ToUInt32(request,12));
                settings.Context("session_id",V2?null:(object)BitConverter.ToUInt32(request,32));
                settings.Context("start_lba",before.Start);settings.Context("card_capacity_sectors",before.Capacity);
                settings.Context("metadata_storage_scope","Host BLE receipt; data is on SD. Association with an exported recording is unverified.");
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
            } catch(Exception ex){BleMetadataFailed(ex);}
        }
        void ObserveBleRecordingMetadata(NpSdStatus status)
        {
            if(bleRecordingMetadata==null || bleMetadataFinished || status==null)return;
            bool recording=V2?status.State==3:status.State==1;
            if(status.State==bleMetadataState && status.Fault==bleMetadataFault && (!recording || bleMetadataRecordingSeen))return;
            try {
                if(recording && !bleMetadataRecordingSeen) {
                    bleMetadataRecordingSeen=true;
                    if(status.Mask!=0)bleRecordingMetadata.Actual((int)status.Hz,(int)status.Hz,(int)status.Mask,false,false,"BLE device-confirmed active profile");
                    bleRecordingMetadata.Context("recording_first_observed_utc_host",DateTime.UtcNow.ToString("O"));
                }
                bleMetadataState=status.State;bleMetadataFault=status.Fault;
                bleRecordingMetadata.Context("ble_device_status",NpRecordingSettings.Fields("state",status.State,"fault",status.Fault,"mask",status.Mask,
                    "sample_hz",status.Hz,"stored_chunks",status.Chunks,"start_lba",status.Start,"end_lba",status.End,"last_command_id",status.LastId));
                bool stopped=V2?NpV2SdControl.StopAlreadySatisfied(status):NpV1BleProtocol.StopAlreadySatisfied(status);
                if(stopped && (bleMetadataRecordingSeen || status.Fault!=0)) {
                    bleMetadataFinished=true;bool complete=status.Fault==0 && status.State==(V2?5U:3U);
                    bleRecordingMetadata.Finished(complete,complete?null:"Device stopped without a complete recording state.");
                    bleRecordingMetadata.Context("completion_evidence","BLE terminal state only; SD payload has not been downloaded or validated.");
                }
                bleRecordingMetadata.Write();
            }catch(Exception ex){BleMetadataFailed(ex);}
        }
        void RetireBleRecordingMetadata()
        {
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
