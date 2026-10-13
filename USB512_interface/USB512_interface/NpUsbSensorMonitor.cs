using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace USB512_interface {
 // Optional metadata consumer, called only by the ordered verifier worker.
 // No device ownership/thread and no EP0 transfer during bulk acquisition.
 internal sealed class NpUsbSensorMonitor : IDisposable {
  readonly string folder,uid;readonly Action<NpImuDisplay> display;
  NpUsbSensors sensors;NpClockJournal journal;FileStream raw;
  bool disposed,sensorDisabled;long records,samples,nextDisplay;
  internal volatile string Error;
  internal long Records {get{return Interlocked.Read(ref records);}}
  internal long Samples {get{return Interlocked.Read(ref samples);}}
  internal NpImuDisplay LastDisplay;
  // Preserve callers/idle diagnostic signature; never retain the borrowed handle.
  internal NpUsbSensorMonitor(NpV2UsbDevice captureDevice,string directory,string deviceUid,bool saveSensors,Action<NpImuDisplay> onDisplay) {
   folder=directory;uid=deviceUid;display=onDisplay;
   try {journal=new NpClockJournal(folder,uid,Guid.NewGuid().ToString("N"));}
   catch(Exception ex){Error=ex.Message;}
   if(saveSensors)try {
    sensors=new NpUsbSensors(folder);
    raw=new FileStream(Path.Combine(folder,"imu_snapshots.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
   }catch(Exception ex){Error=ex.Message;sensorDisabled=true;}
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
   }catch(Exception ex){Error=ex.Message;}
  }
  internal void FeedBlock(byte[] block,int offset) {
   if(disposed || sensorDisabled)return;
   try {
    if(block==null || offset<0 || block.Length-offset<512)throw new IOException("Optional sensor header bounds are invalid.");
    uint flags=BitConverter.ToUInt32(block,offset+52);
    if((flags&128U)==0U)return;
    if((flags&64U)!=0U)throw new IOException("Optional sensor header overlaps raw D8 metadata.");
    uint magic=BitConverter.ToUInt32(block,offset+256);
    if(magic==0U)return; // Sparse publisher clears only magic; other bytes may be stale.
    FeedSnapshot(block,offset+256);
   }catch(Exception ex){Error=ex.Message;sensorDisabled=true;}
  }
  // Explicit idle HIL may feed snapshots obtained through its OWN device handle.
  internal void FeedSnapshot(byte[] snapshot,int offset) {
   if(disposed || sensorDisabled)return;
   try {
    RequireSnapshot(snapshot,offset);
    if(sensors==null || raw==null)return;
    uint observedMs=BitConverter.ToUInt32(snapshot,offset+16);
    raw.Write(snapshot,offset,140);sensors.FeedSnapshot(snapshot,offset,observedMs);
    Interlocked.Exchange(ref samples,sensors.ImuSamples);Interlocked.Increment(ref records);
    long now=journal==null?DateTime.UtcNow.Ticks/10:journal.NowMicroseconds;
    if(now>=nextDisplay){nextDisplay=now+200000;LastDisplay=sensors.GetDisplay();if(display!=null)try{display(LastDisplay);}catch{}}
   }catch(Exception ex){Error=ex.Message;sensorDisabled=true;}
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
   if(journal!=null){journal.Dispose();if(journal.Error!=null)Error=journal.Error;}
   try {File.WriteAllLines(Path.Combine(folder,"imu_observation_status.txt"),new[]{
    "optional=true; failures never stop neural recording","device_uid="+uid,
    "snapshot_bytes=140; raw_file=imu_snapshots.bin; native_sensor_time_24_offset=44",
    "source=sparse_verified_bulk_headers; maximum_publish_hz=20; not lossless native IMU sampling",
    "clock_observations=bracketed_EP0_control_only; bulk_arrival_not_used_for_clock_fit",
    "snapshots="+Records.ToString(CultureInfo.InvariantCulture),
    "distinct_imu_snapshots="+Samples.ToString(CultureInfo.InvariantCulture),"error="+(Error??"none"),
    "analogin.dat is a held-value compatibility grid, not lossless native IMU sampling",
    NpClockFitResult.Limitation});}catch(Exception ex){Error=ex.Message;}
  }
 }
}
