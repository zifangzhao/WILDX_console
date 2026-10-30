using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface {
    internal sealed partial class NpBleConnection {
        NpBleClockContext clockContext;
        sealed class RecordingFineClock {
            internal NpFirstSyncSdClient Client;
            internal NpFirstSyncSdAttempt Attempt;
            internal Func<bool> Current;
        }
        RecordingFineClock recordingFineClock;
        long recordingFineStatusAt;
        bool recordingFineStatus,recordingFineSeen;
        CancellationTokenSource recordingClockCancel;
        Task<byte[]> recordingClockTask=Task.FromResult<byte[]>(null);
        internal bool ClockPending {get{return !recordingClockTask.IsCompleted;}}
        internal bool PreviewLowerPriorityBusy {get{return ImuPending || ClockPending;}}
        bool FineClockCurrent(RecordingFineClock fine) {
            try {
                return fine!=null && ReferenceEquals(recordingFineClock,fine) && Connected && fine.Client.ConnectionEpoch==Epoch &&
                    ReferenceEquals(fine.Client.Attempt,fine.Attempt) && fine.Attempt.Acknowledgement!=null && !fine.Attempt.Rejected &&
                    fine.Attempt.Token!=0 && fine.Attempt.ConnectionEpoch==Epoch && fine.Attempt.Family==fine.Client.Family &&
                    fine.Attempt.Uid==fine.Client.Uid && (fine.Client.SupportedFeatures&NpFirstSyncSd.FeatureFineClock)!=0 && fine.Current();
            }catch{return false;}
        }
        internal bool RecordingFineClockConfigured {get{
            if(FineClockCurrent(recordingFineClock))return true;
            if(recordingFineClock!=null)ClearRecordingFineClock();return false;
        }}
        internal bool ConfigureRecordingFineClock(NpFirstSyncSdClient client,NpFirstSyncSdAttempt attempt,Func<bool> current) {
            if(client==null || attempt==null || current==null)return false;
            var prior=recordingFineClock;
            if(prior!=null && ReferenceEquals(prior.Client,client) && ReferenceEquals(prior.Attempt,attempt))return FineClockCurrent(prior);
            ClearRecordingFineClock();
            var fine=new RecordingFineClock {Client=client,Attempt=attempt,Current=current};recordingFineClock=fine;
            if(FineClockCurrent(fine))return true;
            recordingFineClock=null;return false;
        }
        internal void ClearRecordingFineClock() {
            recordingFineClock=null;recordingFineStatus=recordingFineSeen=false;CancelRecordingClock();
        }
        string FineClockToken(int family,string uid,NpSdStatus status) {
            var fine=recordingFineClock;
            if(!FineClockCurrent(fine) || family!=fine.Client.Family || !String.Equals(uid,fine.Client.Uid,StringComparison.OrdinalIgnoreCase) ||
                status==null || !NpBleRecordingClockSchedule.Confirmed(family,status.State,status.Error,status.Fault) ||
                status.Mask!=NpSdGeometry.U(fine.Attempt.Legacy,16) || status.Hz!=NpSdGeometry.U(fine.Attempt.Legacy,20) ||
                status.LastId!=fine.Attempt.Id)return null;
            return fine.Attempt.Token.ToString("X16",System.Globalization.CultureInfo.InvariantCulture);
        }
        internal void SetClockContext(string uid,string folder,NpSdStatus status) {
            SetClockContext(0,uid,folder,status);
        }
        internal void SetClockContext(int family,string uid,string folder,NpSdStatus status) {
            string token=FineClockToken(family,uid,status);
            if(recordingFineClock!=null){
                bool active=status!=null && NpBleRecordingClockSchedule.Confirmed(family,status.State,status.Error,status.Fault);
                if(!FineClockCurrent(recordingFineClock) || family!=recordingFineClock.Client.Family ||
                    !String.Equals(uid,recordingFineClock.Client.Uid,StringComparison.OrdinalIgnoreCase) ||
                    recordingFineSeen && !active || active && token==null &&
                        (recordingFineSeen || status.LastId==recordingFineClock.Attempt.Id))ClearRecordingFineClock();
                else if(token!=null)recordingFineSeen=true;
            }
            recordingFineStatus=token!=null;recordingFineStatusAt=Stopwatch.GetTimestamp();
            lock(clockJournalLock)clockContext=new NpBleClockContext {
                device_family=family==1 || family==2?family:0,
                device_uid=Regex.IsMatch(uid??"","\\A[0-9A-Fa-f]{24}\\z")?uid.ToUpperInvariant():null,
                host_recording_receipt=folder,status_state=status==null?(uint?)null:status.State,
                status_start_lba=status==null?(uint?)null:status.Start,status_sample_hz=status==null?(uint?)null:status.Hz,
                status_probe_mask=status==null?(uint?)null:status.Mask,sd_record_token_hex=token};
        }
        internal void CancelRecordingClock() {
            var cancel=recordingClockCancel;try{if(cancel!=null)cancel.Cancel();}catch(ObjectDisposedException){}
        }
        internal async Task RetireRecordingClock() {
            CancelRecordingClock();
            try{await recordingClockTask;}catch{} // Failure belongs to its own evidence, never to IMU/control.
        }
        internal Task<byte[]> ReadRecordingClock(uint id,CancellationToken cancel) {
            return BeginRecordingClockRead(id,cancel,null);
        }
        // Explicit experimental opt-in only; normal recording polling remains
        // v1. A requested hold is not measured device processing time.
        internal Task<byte[]> ReadRecordingClockDeferred(uint id,uint holdUs,CancellationToken cancel) {
            if(holdUs>NpFineClockWire.MaximumDeferredHoldUs)throw new ArgumentOutOfRangeException(nameof(holdUs));
            return BeginRecordingClockRead(id,cancel,holdUs);
        }
        Task<byte[]> BeginRecordingClockRead(uint id,CancellationToken cancel,uint? holdUs) {
            if(ImuPending || ClockPending || !Connected)return Task.FromResult<byte[]>(null);
            var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancel);recordingClockCancel=lifetime;
            recordingClockTask=ReadRecordingClockCore(id,lifetime,holdUs);return recordingClockTask;
        }
        async Task<byte[]> ReadRecordingClockCore(uint id,CancellationTokenSource lifetime,uint? holdUs) {
            try {
                lifetime.CancelAfter(750);
                if(ImuPending)return null;
                var fine=recordingFineClock;
                if(fine!=null){
                    if(!FineClockCurrent(fine) || !recordingFineStatus ||
                        Stopwatch.GetTimestamp()-recordingFineStatusAt>5L*Stopwatch.Frequency)return null;
                    ulong nonce;do{nonce=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);}while(nonce==0);
                    byte[] request=holdUs.HasValue?NpFineClockWire.RequestDeferred(fine.Client.Uid,id,nonce,holdUs.Value):NpFineClockWire.Request(fine.Client.Uid,id,nonce);
                    byte[] reply=await ExchangeAsync(0xb9,request,lifetime.Token,true);
                    // The raw journal retains a warm-up/unknown reply. A later
                    // poll gets a new ID/nonce; no control mutation is replayed.
                    return FineClockCurrent(fine)?reply:null;
                }
                if(holdUs.HasValue)return null; // No deferred downgrade to an unbound legacy query.
                return await ExchangeAsync(0xb9,NpV2OnlineClock.Request(0,id),lifetime.Token,true);
            }finally{if(recordingClockCancel==lifetime)recordingClockCancel=null;lifetime.Dispose();}
        }
        internal void ClockCoverageGap(string reason) {
            lock(clockJournalLock){if(clockJournal!=null)clockJournal.ClockGap(clockContext,reason,Stopwatch.GetTimestamp());}
        }
    }
}
