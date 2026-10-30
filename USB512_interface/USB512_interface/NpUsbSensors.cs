using System;
using System.IO;
using System.Globalization;
namespace USB512_interface {
 // Incoming metadata follows SD NPSENS01. The saved analogin.dat has the
 // CE64-equivalent sensor counts in a 16 x 16-bit layout on a held-value grid.
 internal sealed class NpUsbSensors : IDisposable {
  private readonly NpMisc1250 uniform;
  private readonly NpImuSampleJournal nativeJournal;
  private readonly string folder;
  private readonly bool saving;
#if NP_FIRST_SYNC_ENABLE
  NpUsbFineSensors fine;
  internal void ConfigureFine(string uid,Func<NpFirstSyncUsbFineRoot> source){if(saving&&fine==null)try{fine=new NpUsbFineSensors(folder,1,uid,source);}catch(InvalidDataException){/* Unknown legacy identity keeps the ordinary sensor path. */}}
  internal void CompleteFine(ulong sourceEnd,bool evidenceComplete){if(fine!=null)fine.Complete(sourceEnd,evidenceComplete&&!optionalDisabled&&OptionalRejectedRecords==0);}
#endif
  private bool optionalDisabled,optionalUsed,disposed,firstBadAttempted;
  internal const string RejectedFileName="imu_rejected_first.bin",OptionalStatusFile="imu_optional_status.txt";
  internal long OptionalRejectedRecords;
  internal string OptionalError,OptionalArchiveError;
  private string currentOptionalError;
  private int firstBadBytes;
  internal string NativeJournalError {get{return nativeJournal==null?null:nativeJournal.Error;}}
  internal long Misc1250Records { get { return uniform==null?0:uniform.Records; } }
  private bool haveImu,haveRecord,haveSession;
  private bool transportLossPending;
  private uint lastImu,lastRecord,session;
  internal long Records,ImuSamples,ImuSequenceGaps;
  internal long RecordSequenceGaps;
  internal uint Valid,Overflow,MissedEstimate,ImuError,EnvironmentError;
  internal string DisplayText="Waiting for NP sensor metadata...";
  private readonly double[] displayValues=new double[9];
  private readonly byte[] snapshot=new byte[140];
  private uint displaySequence,displayTick;
  internal NpImuDisplay GetDisplay() {return new NpImuDisplay {
   Values=(double[])displayValues.Clone(),Valid=Valid,Sequence=displaySequence,
   DeviceMs=displayTick,ImuError=ImuError,EnvironmentError=EnvironmentError,Overflow=Overflow,Error=currentOptionalError};}
  internal NpUsbSensors(string folder):this(folder,true) {}
  internal NpUsbSensors(string folder,bool save):this(folder,save,null) {}
  internal NpUsbSensors(string folder,bool save,string deviceIdentity):this(folder,save,deviceIdentity,null,0,0) {}
  // The owning recorder supplies an origin only after it has verified the
  // exact UID/token/final receipt and NP-to-sensor clock relation. This must
  // precede grid emission; a late receipt cannot relabel existing rows.
  internal NpUsbSensors(string folder,bool save,string deviceIdentity,NpSdImuGridOrigin origin,uint sourceHz,uint generation,bool legacyGrid=true) {
   this.folder=folder;saving=save;
   // Preserve the compatibility grid. Live recorders may additionally retain
   // distinct native observations with explicit time/validity provenance.
   if(origin!=null)origin.RequireCapture(sourceHz,generation);
   if(save&&legacyGrid)uniform=new NpMisc1250(folder,origin);
   if(save&&deviceIdentity!=null)nativeJournal=new NpImuSampleJournal(folder,deviceIdentity);
  }
  private static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
  private static int I(byte[] b,int o){return BitConverter.ToInt32(b,o);}
  private static bool Magic(byte[] b,int o,string text) {
   for(int i=0;i<text.Length;i++)if(b[o+i]!=(byte)text[i])return false;return true;
  }
  internal static uint Crc(byte[] b,int o,int n) {
   uint c=0xffffffff;for(int i=0;i<n;i++){c^=b[o+i];for(int j=0;j<8;j++)c=(c>>1)^((0-(c&1))&0xedb88320);}return ~c;
  }
  // Only the native paired worker calls this before its next intact record.
  // Do not clear firmware-session identity or make missing observations fresh.
  internal void NotifyTransportLoss() {transportLossPending=true;}
  internal static void RequirePacket(byte[] b,int o,int n) {
   if(b==null || o<0 || n!=506 || b.Length-o<n || !Magic(b,o,"NPSENS01") || U(b,o+16)!=140 ||
      U(b,o+20)!=0x31534e53 || U(b,o+24)!=1 || !Magic(b,o+164,"NPUSB001") ||
      U(b,o+172)!=1 || U(b,o+160)!=Crc(b,o,160) || U(b,o+502)!=Crc(b,o,502))
    throw new IOException("Invalid optional NP sensor metadata/version/CRC.");
  }
  private void RequireContinuity(byte[] b,int o) {
   uint record=U(b,o+8), sid=U(b,o+176);
   if(haveSession && session!=sid)throw new IOException("NP sensor firmware session changed without a new segment.");
   if(haveRecord && record!=unchecked(lastRecord+1)) {
    uint delta=unchecked(record-lastRecord);
    if(!transportLossPending || delta==0 || delta>=0x80000000U)
     throw new IOException("NP sensor record sequence gap.");
   }
  }
  internal void Feed(byte[] b,int o,int n) {RequirePacket(b,o,n);RequireContinuity(b,o);FeedRecord(b,o);}
  // Saving V1 recorder only: the outer packet parser has already admitted the
  // stream-5 boundary. Sensor integrity is independent of stream-6 NN geometry.
  // Strict Feed remains available to validators; cancellation/NN errors are
  // never caught here. Only the first bounded bad optional record is archived.
  internal void FeedOptional(byte[] b,int o,int n) {
   if(disposed || optionalDisabled)return;optionalUsed=true;
   try {RequirePacket(b,o,n);RequireContinuity(b,o);}
   catch(IOException ex){ReportOptionalFailure(b,o,n,ex.Message);transportLossPending=true;return;}
#if NP_FIRST_SYNC_ENABLE
   if(fine!=null)fine.Feed(b,o,n);
#endif
   try {FeedRecord(b,o);}
   catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException){
    optionalDisabled=true;ReportOptionalFailure(b,o,n,ex.Message);
   }
  }
  internal void ReportOptionalFailure(byte[] b,int o,int n,string error) {
   optionalUsed=true;OptionalRejectedRecords++;OptionalError=currentOptionalError=error;Valid=0;
   if(uniform!=null)uniform.Invalidate();
   DisplayText="NP IMU unavailable: "+error+" Neural recording continues unless a capture error is reported.";
   if(!saving || firstBadAttempted)return;firstBadAttempted=true;
   int count=b==null||o<0||o>b.Length||n<0?0:Math.Min(512,Math.Min(n,b.Length-o));
   if(count==0)return;
   try {using(var file=new FileStream(Path.Combine(folder,RejectedFileName),FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write(b,o,count);firstBadBytes=count;}}
   catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException){OptionalArchiveError=ex.Message;}
  }
  private void FeedRecord(byte[] b,int o) {
   uint record=U(b,o+8),sid=U(b,o+176);
   if(haveRecord && record!=unchecked(lastRecord+1))checked {RecordSequenceGaps+=unchecked(record-lastRecord)-1U;}
   transportLossPending=false;
   lastRecord=record;haveRecord=true;session=sid;haveSession=true;
   Overflow=U(b,o+184);
   FeedSnapshot(b,o+20,U(b,o+12));
  }
  internal void FeedSnapshot(byte[] b,int o,uint observedMs) {
   if(b==null || o<0 || b.Length-o<140 || U(b,o)!=0x31534e53 || U(b,o+4)!=1)
    throw new IOException("Invalid NP sensor snapshot ABI.");
   currentOptionalError=null;
   // Preserve the NP1 packet path; EP0 snapshots are optional observations,
   // not a lossless 200 Hz sensor stream. Native sequences remain visible.
   uint seq=U(b,o+20);
   Valid=U(b,o+12);MissedEstimate=U(b,o+128);
   if(seq==0 || unchecked(observedMs-U(b,o+32))>100)Valid&=~1U;
   if(U(b,o+24)==0 || unchecked(observedMs-U(b,o+36))>200)Valid&=~2U;
   if(U(b,o+28)==0 || unchecked(observedMs-U(b,o+40))>3000)Valid&=~4U;
   if(nativeJournal!=null)nativeJournal.Observe(b,o,observedMs,Valid,haveSession?(uint?)session:null,
    haveRecord?(uint?)lastRecord:null,RecordSequenceGaps,Overflow);
   // Existing uniform serializer takes a private copy; never alter RAW input.
   Buffer.BlockCopy(b,o,snapshot,0,140);
   Buffer.BlockCopy(BitConverter.GetBytes(Valid),0,snapshot,12,4);
   b=snapshot;o=0;
   ImuError=U(b,o+104);EnvironmentError=U(b,o+108);
   for(int axis=0;axis<9;axis++)displayValues[axis]=I(b,o+48+axis*4)/(axis<3?1e6:1e3);
   displaySequence=seq;displayTick=U(b,o+32);
   Records++;
   if(uniform!=null)uniform.FeedSnapshot(b,o,observedMs);
   if((Valid&1)!=0 && (!haveImu || seq!=lastImu)) {
    if(haveImu) {uint delta=unchecked(seq-lastImu);if(delta>1)ImuSequenceGaps+=delta-1;}
    ImuSamples++;lastImu=seq;haveImu=true;
   }
   string axes=(Valid&1)==0?"NOT READY":(Valid&2)==0?"6-axis; magnetic data unavailable":"9-axis";
   string magText=(Valid&2)==0?"Mag: unavailable (not zero-field data)":String.Format(CultureInfo.InvariantCulture,
    "Mag (uT): {0:F2}, {1:F2}, {2:F2}",I(b,o+72)/1e3,I(b,o+76)/1e3,I(b,o+80)/1e3);
   DisplayText=String.Format(CultureInfo.InvariantCulture,
    "NP IMU | {0} | sample {1} | device {2} ms\nAccel (g): {3:F3}, {4:F3}, {5:F3}\nGyro (deg/s): {6:F2}, {7:F2}, {8:F2}\n{9}\nErrors: IMU {10}, environment {11} | queue loss {12} | missed estimate {13}",
    axes,seq,U(b,o+32),I(b,o+48)/1e6,I(b,o+52)/1e6,I(b,o+56)/1e6,
    I(b,o+60)/1e3,I(b,o+64)/1e3,I(b,o+68)/1e3,magText,
    ImuError,EnvironmentError,Overflow,MissedEstimate);
  }
  // Private compatibility adaptation only. +16 remains the ORIGINAL HAL
  // publication milliseconds; no counter64/PC-derived phase is introduced.
  internal static byte[] LegacyFineSnapshot(byte[] b,int o) {
   if(b==null||o<0||b.Length-o<140||U(b,o)!=0x31534e53)throw new InvalidDataException("Optional fine sensor core is truncated or unknown.");
   uint word=U(b,o+4),enabled=(word>>8)&3,valid=(word>>12)&7,mask=(word>>16)&7;
   if(word!=(3U|(enabled<<8)|(valid<<12)|(mask<<16))||((valid&3)!=0&&(enabled&1)==0)||((valid&4)!=0&&(enabled&2)==0))throw new InvalidDataException("Optional fine sensor core version/flags are unsupported.");
   var copy=new byte[140];Buffer.BlockCopy(b,o,copy,0,copy.Length);
   Buffer.BlockCopy(BitConverter.GetBytes(1U),0,copy,4,4);Buffer.BlockCopy(BitConverter.GetBytes(enabled),0,copy,8,4);Buffer.BlockCopy(BitConverter.GetBytes(valid),0,copy,12,4);return copy;
  }
  public void Dispose(){
   if(disposed)return;disposed=true;
   try{try{if(uniform!=null)uniform.Dispose();}finally{if(nativeJournal!=null)nativeJournal.Dispose();}}
   finally {
#if NP_FIRST_SYNC_ENABLE
    if(fine!=null)fine.Dispose();
#endif
    if(saving&&optionalUsed)try {File.WriteAllLines(Path.Combine(folder,OptionalStatusFile),new[]{
    "optional=true; sensor failures do not relax or stop neural payload/header verification",
    "rejected_optional_records="+OptionalRejectedRecords.ToString(CultureInfo.InvariantCulture),"consumer_disabled="+optionalDisabled,
    "last_error="+(OptionalError??"none").Replace('\r',' ').Replace('\n',' '),
    "first_rejected_raw_file="+(firstBadBytes==0?"unavailable":RejectedFileName),"first_rejected_raw_bytes="+firstBadBytes,
    "raw_scope=first bounded optional record/snapshot only; rejected bytes are not decoded or counted as native samples",
    "archive_error="+(OptionalArchiveError??"none").Replace('\r',' ').Replace('\n',' ')});}
    catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException){OptionalArchiveError=ex.Message;}}
  }
 }

 internal sealed class NpImuDisplay {
  internal double[] Values;
  internal string Error;
  internal uint Valid,Sequence,DeviceMs,ImuError,EnvironmentError,Overflow;
 }

 // Zero-order hold on device source timestamps, not host arrival times. This
 // derived 1250 Hz grid never claims that repeated readings are fresh samples.
 // Every row is exactly the 32-byte CE64 analogin record layout:
 // [digin, ax, ay, az, gx, gy, gz, mx, my, mz, slow ADC, test,
 //  closed-loop 0, closed-loop 1, PC time low/high].
 internal sealed class NpMisc1250 : IDisposable {
  private readonly FileStream file;
  private readonly byte[] held=new byte[140],record=new byte[32];
  private readonly byte[] qualifiedSnapshot=new byte[140];
  private readonly NpSdImuGridOrigin origin;
  private readonly string folder;
  private bool started,disposed;
  private uint lastTick,lastSourceTick,lastSequence,lastObservation,pcOriginMs,deviceOriginMs;
  private decimal sourceUs;
  private ulong nextUs;
  private decimal? firstAgOffset,firstMagOffset;
  internal long Records;
  internal NpMisc1250(string folder,NpSdImuGridOrigin origin=null) {
   this.folder=folder;this.origin=origin;
   if(origin!=null){
    // Begin unavailable at NP reference, not at the first cached AG reading.
    started=true;deviceOriginMs=lastTick=lastSourceTick=lastObservation=origin.DeviceMs;
    sourceUs=-origin.FractionUs;
   }
   // This is the NP equivalent of the CE64 low-rate misc stream.  Name it
   // analogin.dat to match the rest of the WILD/CE64 acquisition ecosystem.
   file=new FileStream(Path.Combine(folder,"analogin.dat"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
   try {
    using(var metadata=new StreamWriter(new FileStream(Path.Combine(folder,"analogin_encoding.txt"),FileMode.CreateNew,FileAccess.Write,FileShare.Read))) {
     metadata.WriteLine("schema=NP_ANALOGIN_CE64_COUNTS_V1");
     metadata.WriteLine("file=analogin.dat; record_bytes=32; words=16 little-endian 16-bit words");
     metadata.WriteLine("sensor_encoding=CE64-equivalent signed int16 counts reconstructed from physical units; not recovered raw ADC/register values");
     metadata.WriteLine("sensor_words=2-4 accel; 5-7 gyro; 8-10 compensated magnetometer");
     metadata.WriteLine("accel_counts=round_away_from_zero(accel_micro_g*4096/1000000); counts_per_g=4096; configured_range_g=+/-8");
     metadata.WriteLine("gyro_counts=round_away_from_zero(gyro_milli_dps*164/10000); counts_per_dps=16.4; configured_range_dps=+/-2000");
     metadata.WriteLine("mag_counts=round_away_from_zero(mag_nano_tesla/300); nano_tesla_per_count=300; not packed BMM150 register values");
     metadata.WriteLine("out_of_range=saturate_to_int16; unavailable_sensor=-32768 (can also represent a valid minimum; consult snapshot validity)");
     metadata.WriteLine("output_grid_hz=1250; period_us=800; device-source-time zero-order hold; not 1250 Hz sensor acquisition");
     metadata.WriteLine("native_AG_profile=200 Hz accel and gyro, configured rate rather than measured retained rate");
     metadata.WriteLine("native_MAG_rate=not encoded in SNS1 v1; legacy/new profile rates must not be inferred from output grid");
     metadata.WriteLine("raw_SNS1_physical_units=accel_micro_g,gyro_milli_dps,mag_nano_tesla; snapshots/native journal remain unchanged");
     metadata.WriteLine(origin==null?"grid_origin=first valid AG source tick; no samples backfilled before it; AG expires after 100 ms, MAG after 200 ms":
      "grid_origin=caller-qualified final-generation NP reference; pre-origin AG/MAG excluded; AG expires after 100 ms, MAG after 200 ms");
     metadata.WriteLine("np_byte_zero_sample_grid_locked="+(origin!=null)+"; native_sensor_conversion_phase_locked=false");
     metadata.WriteLine("origin_evidence="+(origin==null?"unavailable; NP offset and UTC unknown":origin.Evidence));
     if(origin!=null){
      metadata.WriteLine("grid_origin_device_ms="+origin.DeviceMs+"; fraction_us="+origin.FractionUs.ToString(CultureInfo.InvariantCulture)+"; final_capture_generation="+origin.Generation);
      metadata.WriteLine("np_reference_source_tick="+origin.ReferenceTick+"; source_hz="+origin.SourceHz+"; grid_first_row_np_reference_offset_us=0");
      metadata.WriteLine("native_source_time=I2C completion milliseconds; native counter mapping is independent of held grid row0; no PC/UTC phase inferred");
     }
     metadata.WriteLine("other_words=1 digin(0 unavailable); 11 slow ADC(-32768 unavailable); 12 IMU sequence; 13-14 closed-loop(0 idle); 15-16 host local milliseconds-of-day");
    }
   } catch {file.Dispose();throw;}
  }
  private static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
  private static void Put(byte[] b,int o,uint v){for(int i=0;i<4;i++)b[o+i]=(byte)(v>>(8*i));}
  private static void Put16(byte[] b,int o,short v){b[o]=(byte)v;b[o+1]=(byte)(v>>8);}
  private static short Scale(int value,int numerator,int denominator) {
   long magnitude=value<0?-(long)value:value;
   long scaled=(magnitude*numerator+denominator/2)/denominator;
   if(value<0)scaled=-scaled;
   if(scaled>short.MaxValue)return short.MaxValue;
   if(scaled<short.MinValue)return short.MinValue;
   return (short)scaled;
  }
  // An untrusted optional record cannot advance source time or hold earlier
  // values out to its alleged timestamp. The next good observation may recover.
  internal void Invalidate(){Put(held,12,0);}
  internal void Feed(byte[] b,int o) {
   FeedSnapshot(b,o+20,U(b,o+12));
  }
  internal void FeedSnapshot(byte[] b,int o,uint now) {
   if(origin!=null){
    if(unchecked((int)(now-origin.DeviceMs))*1000m-origin.FractionUs<0)return;
    // Mask only a private grid copy. Native snapshot/journal stays unchanged.
    Buffer.BlockCopy(b,o,qualifiedSnapshot,0,140);b=qualifiedSnapshot;o=0;
    uint valid=U(b,12);
    for(int group=0;group<2;group++){
     decimal offset=unchecked((int)(U(b,32+group*4)-origin.DeviceMs))*1000m-origin.FractionUs;
     if(offset<0)valid&=~(1U<<group);
     else if((valid&(1U<<group))!=0){if(group==0&&!firstAgOffset.HasValue)firstAgOffset=offset;if(group==1&&!firstMagOffset.HasValue)firstMagOffset=offset;}
    }
    Put(b,12,valid);
   }
   uint seq=U(b,o+20),tick=U(b,o+32);
   bool late=false;
   if(!started) {
    // Do not backfill before the first actual AG observation.
    if((U(b,o+12)&1)==0)return;
    lastTick=lastSourceTick=tick;lastSequence=seq;started=true;deviceOriginMs=tick;
    pcOriginMs=(uint)DateTime.Now.TimeOfDay.TotalMilliseconds;
    Buffer.BlockCopy(b,o,held,0,140);
   } else if((U(b,o+12)&1)!=0) {
    bool fresh=seq!=lastSequence;
    if(fresh && unchecked(tick-lastSourceTick)>60000)
     throw new IOException("NP misc source clock discontinuity (>60 s); start a new segment.");
    uint delta=unchecked(tick-lastTick);
    if(delta>60000) {
     if(unchecked(lastTick-tick)>60000)throw new IOException("NP misc source clock discontinuity (>60 s); start a new segment.");
     // A status watermark can run ahead of a queued valid source sample. Preserve
     // its native observation, but never rewind/re-enable the held grid.
     late=true;
    } else if(fresh) {
     decimal time=sourceUs+(decimal)delta*1000;
     EmitBefore(time);sourceUs=time;lastTick=tick;
     Buffer.BlockCopy(b,o,held,0,140);
    }
    if(fresh){lastSourceTick=tick;lastSequence=seq;}
   }
   // NP_OPTIONAL_SENSOR_VALIDITY: a failure/status transition is not a
   // new IMU sample, but it must end the previous held-value interval.
   if(started && !late && U(held,12)!=U(b,o+12)) {
    uint delta=unchecked(now-lastTick);
    if(delta>60000)throw new IOException("NP misc status clock discontinuity.");
    decimal time=sourceUs+(decimal)delta*1000;
    EmitBefore(time);sourceUs=time;lastTick=now;
    Buffer.BlockCopy(b,o,held,0,140);
   }
   // Export observations can contain several queued samples with the same now.
   // Only use this watermark on close, after all those samples were consumed.
   if(unchecked(now-lastTick)>60000)throw new IOException("NP misc observation clock discontinuity.");
   lastObservation=now;
  }
  private void EmitBefore(decimal end) {
   while(nextUs<end) {
    Array.Clear(record,0,record.Length);
    // Digin, slow ADC and closed-loop outputs are not in NPSENS01.  Use the
    // CE64 unavailable/idle representations instead of fabricating values.
    Put16(record,0,0);                 // word 1: digital input unavailable
    uint atMs=unchecked(deviceOriginMs+(uint)((nextUs+(origin==null?0:origin.FractionUs))/1000));
    uint valid=U(held,12);
    bool ag=(valid&1)!=0 && unchecked(atMs-U(held,32))<=100;
    bool mag=(valid&2)!=0 && unchecked(atMs-U(held,36))<=200;
    // Fixed CE64 channel geometry is preserved. Disabled sensor channels
    // carry the unavailable sentinel, never plausible zero/stale readings.
    for(int axis=0;axis<6;axis++)
     Put16(record,2+axis*2,ag?Scale(BitConverter.ToInt32(held,48+axis*4),axis<3?4096:164,axis<3?1000000:10000):short.MinValue);
    for(int axis=0;axis<3;axis++)
     Put16(record,14+axis*2,mag?Scale(BitConverter.ToInt32(held,72+axis*4),1,300):short.MinValue);
    Put16(record,20,short.MinValue);   // word 11: slow ADC unavailable
    Put16(record,22,ag?unchecked((short)(ushort)U(held,20)):(short)-1); // word 12: valid IMU sequence or unavailable
    Put16(record,24,0);Put16(record,26,0); // words 13-14: closed-loop idle
    if(origin==null)Put(record,28,unchecked(pcOriginMs+(uint)(nextUs/1000)));
    file.Write(record,0,record.Length);
    Records++;nextUs+=800;
   }
  }
  public void Dispose() {
   if(disposed)return;disposed=true;
   try {
    if(started)EmitBefore(sourceUs+(decimal)unchecked(lastObservation-lastTick)*1000);
    file.Flush(true);
   } finally {file.Dispose();}
   if(origin!=null)File.AppendAllLines(Path.Combine(folder,"analogin_encoding.txt"),new[]{
    "first_retained_AG_source_offset_us="+(firstAgOffset.HasValue?firstAgOffset.Value.ToString(CultureInfo.InvariantCulture):"unavailable")+
     "; first_retained_MAG_source_offset_us="+(firstMagOffset.HasValue?firstMagOffset.Value.ToString(CultureInfo.InvariantCulture):"unavailable"),
    "qualified_grid_PC_time_words=unavailable; native sample counters remain in imu_native_samples.jsonl, not grid row indices"});
  }
 }
}
