using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace USB512_interface {
 // Optional metadata consumer, called only by the ordered verifier worker.
 // No device ownership/thread and no EP0 transfer during bulk acquisition.
 internal sealed class NpUsbSensorMonitor : IDisposable {
  readonly string folder,uid;readonly Action<NpImuDisplay> display;readonly bool saving;
  NpUsbSensors sensors;NpClockJournal journal;FileStream raw;
  bool disposed,sensorDisabled;long records,samples,nextDisplay;
  bool fineSnapshotUsed;
#if NP_FIRST_SYNC_ENABLE
  NpUsbFineSensors fine;
  internal void ConfigureFine(Func<NpFirstSyncUsbFineRoot> source){if(saving&&fine==null)try{fine=new NpUsbFineSensors(folder,2,uid,source);}catch(InvalidDataException){/* Unknown legacy identity keeps the ordinary sensor path. */}}
  internal void CompleteFine(ulong sourceEnd,bool evidenceComplete){if(fine!=null)fine.Complete(sourceEnd,evidenceComplete&&!sensorDisabled&&sensors!=null&&sensors.OptionalRejectedRecords==0);}
#endif
  internal volatile string Error;
  internal long Records {get{return Interlocked.Read(ref records);}}
  internal long Samples {get{return Interlocked.Read(ref samples);}}
  internal NpImuDisplay LastDisplay;
  void ReportError(Exception ex) {
   bool changed=Error!=ex.Message;
   Error=ex.Message;
   LastDisplay=LastDisplay??new NpImuDisplay {Values=new double[9]};LastDisplay.Error=Error;
   if(changed && display!=null)try{display(LastDisplay);}catch{}
  }
  void Reject(byte[] b,int offset,int length,Exception ex) {
   if(sensors!=null){sensors.ReportOptionalFailure(b,offset,length,ex.Message);LastDisplay=sensors.GetDisplay();}
   ReportError(ex);
  }
  // Preserve callers/idle diagnostic signature; never retain the borrowed handle.
  internal NpUsbSensorMonitor(NpV2UsbDevice captureDevice,string directory,string deviceUid,bool saveSensors,Action<NpImuDisplay> onDisplay):
   this(captureDevice,directory,deviceUid,saveSensors,onDisplay,null,0,0) {}
  internal NpUsbSensorMonitor(NpV2UsbDevice captureDevice,string directory,string deviceUid,bool saveSensors,Action<NpImuDisplay> onDisplay,
   NpSdImuGridOrigin origin,uint sourceHz,uint generation) {
   folder=directory;uid=deviceUid;display=onDisplay;saving=saveSensors;
   try {
    if(saving)journal=new NpClockJournal(folder,uid,Guid.NewGuid().ToString("N"));
    sensors=new NpUsbSensors(folder,saving,uid,origin,sourceHz,generation);
    if(saving)raw=new FileStream(Path.Combine(folder,"imu_snapshots.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
   }catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException){ReportError(ex);sensorDisabled=true;}
  }
  // Only caller-owned stopped/control-only windows may invoke this method.
  // Host bulk-arrival time is NEVER a precision-clock observation.
  internal void ObserveIdleClock(NpV2UsbDevice device) {
   if(disposed || journal==null)return;
   try {
    long start=journal.NowMicroseconds;
    byte[] snapshot=device.GetSensorSnapshot();
    long end=journal.NowMicroseconds;
    RequireSnapshot(snapshot,0);
    journal.Add(0,BitConverter.ToUInt32(snapshot,16),start+(end-start)/2,
     (end-start)/2.0+1000,"USB_EP0_CONTROL_ONLY");
   }catch(Exception ex){ReportError(ex);}
  }
  internal void FeedBlock(byte[] block,int offset) {
   if(disposed || sensorDisabled)return;
   try {
    if(block==null || offset<0 || block.Length-offset<512)throw new IOException("Optional sensor header bounds are invalid.");
    uint flags=BitConverter.ToUInt32(block,offset+52);
    if((flags&128U)==0U)return;
    if((flags&64U)!=0U)throw new IOException("Optional sensor header overlaps raw D8 metadata.");
#if NP_FIRST_SYNC_ENABLE
    if(fine!=null)fine.Feed(block,offset,512);
#endif
    uint magic=BitConverter.ToUInt32(block,offset+256);
    if(magic==0U)return; // Sparse publisher clears only magic; other bytes may be stale.
    if((flags&4096U)!=0U){
     var compatible=NpUsbSensors.LegacyFineSnapshot(block,offset+256);fineSnapshotUsed=true;FeedSnapshot(compatible,0);return;
    }
    FeedSnapshot(block,offset+256);
   }catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException){Reject(block,offset,512,ex);}
  }
  // Explicit idle HIL may feed snapshots obtained through its OWN device handle.
  internal void FeedSnapshot(byte[] snapshot,int offset) {
   if(disposed || sensorDisabled)return;
   // ABI rejection is local to this sparse optional snapshot. Do not disable
   // later valid IMU data, and never bypass the owning NN verifier upstream.
   try {RequireSnapshot(snapshot,offset);}
   catch(IOException ex){Reject(snapshot,offset,140,ex);return;}
   try {
    if(sensors==null)return;
    uint observedMs=BitConverter.ToUInt32(snapshot,offset+16);
    if(raw!=null)raw.Write(snapshot,offset,140);sensors.FeedSnapshot(snapshot,offset,observedMs);
    Interlocked.Exchange(ref samples,sensors.ImuSamples);Interlocked.Increment(ref records);
    long now=journal==null?DateTime.UtcNow.Ticks/10:journal.NowMicroseconds;
    if(now>=nextDisplay){nextDisplay=now+200000;LastDisplay=sensors.GetDisplay();if(display!=null)try{display(LastDisplay);}catch{}}
   }catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException){Reject(snapshot,offset,140,ex);sensorDisabled=true;}
  }
  static void RequireSnapshot(byte[] snapshot,int offset) {
   if(snapshot==null || offset<0 || snapshot.Length-offset<140 ||
    BitConverter.ToUInt32(snapshot,offset)!=0x31534e53U || BitConverter.ToUInt32(snapshot,offset+4)!=1U)
    throw new IOException("Optional IMU snapshot ABI is unsupported.");
  }
  // Capture owner joins the verifier worker BEFORE finalizing these files/stats.
  public void Dispose() {
   if(disposed)return;disposed=true;
   try{if(sensors!=null)sensors.Dispose();}catch(Exception ex){Error=ex.Message;}
   try{if(raw!=null)raw.Dispose();}catch(Exception ex){Error=ex.Message;}
#if NP_FIRST_SYNC_ENABLE
   if(fine!=null)fine.Dispose();
#endif
   if(journal!=null){journal.Dispose();if(journal.Error!=null)Error=journal.Error;}
   if(!saving)return;
   try {File.WriteAllLines(Path.Combine(folder,"imu_observation_status.txt"),new[]{
    "optional=true; failures never stop neural recording","device_uid="+uid,
    "snapshot_bytes=140; raw_file=imu_snapshots.bin; native_sensor_time_24_offset=44",
    "snapshot_provenance="+(fineSnapshotUsed?"private SNS1v3-to-v1 compatibility copies; exact original carriers in imu_usb_fine_records.bin; retained original HAL publication ms, not fine counter phase":"original SNS1v1"),
    "source=sparse_verified_bulk_headers; maximum_publish_hz=20; not lossless native IMU sampling",
    "clock_observations=bracketed_EP0_control_only; bulk_arrival_not_used_for_clock_fit",
    "snapshots="+Records.ToString(CultureInfo.InvariantCulture),
    "distinct_imu_snapshots="+Samples.ToString(CultureInfo.InvariantCulture),"error="+(Error??"none"),
    "rejected_optional_records="+(sensors==null?0:sensors.OptionalRejectedRecords).ToString(CultureInfo.InvariantCulture),
    "first_rejected_raw_file="+(sensors==null||sensors.OptionalRejectedRecords==0?"none":NpUsbSensors.RejectedFileName),
    "native_sample_journal="+NpImuSampleJournal.FileName,"native_journal_error="+(sensors==null?"unavailable":sensors.NativeJournalError??"none"),
    "analogin.dat is a held-value compatibility grid, not lossless native IMU sampling",
    NpClockFitResult.Limitation});}catch(Exception ex){Error=ex.Message;}
  }
 }
}
