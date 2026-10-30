using System;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface {
    internal sealed partial class NpBleForm {
        readonly NpBleRecordingClockSchedule recordingClockSchedule=new NpBleRecordingClockSchedule();
        Task bleClockTask=Task.CompletedTask;
        bool recordingClockGapPending;
        long fineRecordingClockStart=-1,nextFineRecordingClock;
        void ObserveRecordingClockStatus(NpSdStatus status) {
            long now=bleImuClock.ElapsedMilliseconds;
            recordingClockSchedule.Status(status!=null && NpBleRecordingClockSchedule.Confirmed(recorderVersion,status.State,status.Error,status.Fault),
                status!=null && (NpBleRecordingClockSchedule.Capable(recorderVersion,status.Flags) || connection.RecordingFineClockConfigured),now);
            connection.SetClockContext(recorderVersion,bleDeviceUid,bleRecordingMetadata!=null?bleMetadataFolder:null,status);
            if(status==null || !NpBleRecordingClockSchedule.Confirmed(recorderVersion,status.State,status.Error,status.Fault) || !connection.RecordingFineClockConfigured)
                fineRecordingClockStart=-1;
            else if(fineRecordingClockStart<0)fineRecordingClockStart=now;
        }
        async Task RetireBleRecordingClock(){await connection.RetireRecordingClock();try{await bleClockTask;}catch{}}
        void ResetRecordingClock(){connection.ClearRecordingFineClock();recordingClockSchedule.Reset();recordingClockGapPending=false;fineRecordingClockStart=-1;nextFineRecordingClock=0;}
        async Task PollRecordingClockIfDue() {
            long now=bleImuClock.ElapsedMilliseconds;
            if(!recordingClockSchedule.Due(now))return;
            bool fine=connection.RecordingFineClockConfigured;
            if(fine && now<nextFineRecordingClock)return;
            if(closing || !connection.Connected || busy || hostBusy || stopRequested || stopping || pendingStop!=null ||
                !refreshTask.IsCompleted || !bleImuTask.IsCompleted || connection.ImuPending || !bleClockTask.IsCompleted) {
                recordingClockGapPending=true;return;
            }
            // Reuse the existing1s IMU-first timer: up to1Hz during the first
            // minute, then5s. No added timer, Start wait or sensor contention.
            if(fine)nextFineRecordingClock=now+(fineRecordingClockStart>=0 && now-fineRecordingClockStart<60000?1000:5000);
            else recordingClockSchedule.Attempt(now);
            bleClockTask=PollRecordingClock();await bleClockTask;
        }
        async Task PollRecordingClock() {
            int epoch=connection.Epoch;
            try {
                if(recordingClockGapPending){connection.ClockCoverageGap("periodic read deferred for recording/control/IMU priority");recordingClockGapPending=false;}
                byte[] reply=await connection.ReadRecordingClock(Next(),System.Threading.CancellationToken.None);
                if(reply==null){recordingClockGapPending=true;return;}
                if(connection.Epoch!=epoch)return;
                // The evidence path parses/stores the raw observation. It never
                // commits a clock or pretends an AP-to-MCU pair was measured.
            }catch(Exception) {
                if(connection.Epoch!=epoch)return;
                recordingClockGapPending=true;recordingClockSchedule.Failed(bleImuClock.ElapsedMilliseconds);
            }
        }
    }
}
