using System;

namespace USB512_interface {
    internal sealed partial class NpBleForm {
        // The embedded console owns the independent GPIO/RTC logger. These
        // callbacks never add recorder commands or turn optional errors into
        // a recording gate. Disconnect/fault/terminal status do not invoke Stop.
        internal Action<string,NpRecordingSettings> HostRecordingPrepared;
        internal Action HostRecordingStopRequested;
        internal Func<bool> HostRecordingIsActive;

        bool HasHostRecording() {
            try{return HostRecordingIsActive!=null && HostRecordingIsActive();}
            catch{return false;}
        }
        void NotifyHostRecordingPrepared() {
            if(HostRecordingPrepared==null || bleRecordingMetadata==null || String.IsNullOrEmpty(bleMetadataFolder))return;
            try {
                HostRecordingPrepared(bleMetadataFolder,bleRecordingMetadata);
                bleRecordingMetadata.Write();
            } catch(Exception error) {
                Append("GPIO/RTC could not be linked: "+error.Message+" Recording controls remain available.");
            }
        }
        void StopHostRecordingExplicitly() {
            if(!HasHostRecording() || HostRecordingStopRequested==null)return;
            try{HostRecordingStopRequested();}
            catch(Exception error){Append("GPIO/RTC stop could not be confirmed: "+error.Message);}
        }
    }
}
