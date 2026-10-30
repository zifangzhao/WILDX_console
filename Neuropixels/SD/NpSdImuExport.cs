using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace USB512_interface
{
    // Caller-supplied position of the NP reference in the sensor's millisecond
    // clock. Constructing this value DOES NOT qualify that clock relation.
    // The owner must first verify the exact UID/token/final receipt and the
    // native observation/counter relation. NPAR/DWT or UTC ARM midpoint is not
    // fractional HAL-ms proof. Native IMU conversion phase stays separate.
    public sealed class NpSdImuGridOrigin
    {
        public readonly uint DeviceMs,SourceHz,Generation;
        public readonly decimal FractionUs;
        public readonly string Evidence;
        public readonly ulong ReferenceTick;
        public NpSdImuGridOrigin(uint deviceMs,decimal fractionUs,uint sourceHz,uint generation,string evidence,ulong referenceTick=0)
        {
            if(fractionUs<0||fractionUs>=1000||sourceHz==0||generation==0||String.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("A qualified final-generation NP byte-zero origin is required.");
            DeviceMs=deviceMs;FractionUs=fractionUs;SourceHz=sourceHz;Generation=generation;Evidence=evidence;ReferenceTick=referenceTick;
        }
        public void RequireCapture(uint sourceHz,uint generation)
        {
            if(sourceHz==0||generation==0||SourceHz!=sourceHz||Generation!=generation)
                throw new InvalidDataException("NP IMU grid origin/source rate/final generation mismatch.");
        }
    }
    // SD-only SNS2 v2. Its first 112 bytes preserve the corresponding SNS1
    // fields; trailing SNS1 diagnostic counters are absent, never zero-filled.
    // Header 396..507 contains SNS2; 508..511 contains its IEEE CRC32.
    // Relative uint32 offsets: 0 magic, 4 version, 8 enabled, 12 valid,
    // 16 publication ms, 20/24/28 AG/mag/env sequences, 32/36/40 source ms,
    // 44 native sensor_time_24; 48..68 signed accel micro-g/gyro milli-dps,
    // 72..80 signed mag nT, 84 signed temperature centi-C, 88 pressure Pa,
    // 92 humidity milli-percent, 96/100 chip IDs, 104/108 sensor errors.
    // enabled bits: 1 IMU, 2 environment; valid bits: 1 AG, 2 mag, 4 env.
    // Raw D8's 184..383 lookahead and optional CL's 184..255 do not overlap.
    internal sealed class NpSdImuSnapshot
    {
        internal const uint Flag=0x800U,FineFlag=4096U,Magic=0x32534e53U;
        internal const int Offset=396,Bytes=112,RecordBytes=116;
        internal const int FineOffset=384,FineRecordBytes=128;
        internal readonly byte[] Raw;
        internal bool Continuous64;
        internal uint Enabled,Valid,PublishedMs,ImuSequence,MagSequence,EnvSequence,ImuMs,MagMs,EnvMs,SensorTime;
        internal int[] Ag=new int[6],Mag=new int[3];
        NpSdImuSnapshot(byte[] raw){Raw=raw;}
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        internal static uint Crc(byte[] b,int at,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[at+i];for(int k=0;k<8;k++)c=(c>>1)^((0U-(c&1U))&0xedb88320U);}return ~c;}
        internal static bool Present(byte[] block){return (U(block,52)&(Flag|FineFlag))!=0;}
        internal static bool FineAllowed(NpFineSdCompanion context){return context!=null&&context.FineAux&&context.Bound!=null&&context.Bound.Receipt!=null;}
        internal static void ValidateGeometry(uint flags,bool allowFine=false)
        {
            uint allowed=511U|1024U|Flag|(allowFine?FineFlag:0U);
            if((flags&~allowed)!=0 || (flags&128U)!=0 || (flags&32U)==0 ||
                (flags&(64U|1024U))==(64U|1024U) || ((flags&FineFlag)!=0&&(flags&Flag)==0))
                throw new InvalidDataException("SD IMU metadata overlaps another header extension or uses unknown flags.");
        }
        internal static NpSdImuSnapshot Parse(byte[] block,NpFineSdCompanion context=null)
        {
            if(block==null||block.Length<512)throw new InvalidDataException("SD IMU header is truncated.");
            uint flags=U(block,52);
            if((flags&(Flag|FineFlag))==0)return null;
            bool fine=(flags&FineFlag)!=0;ValidateGeometry(flags,true);
            bool zero=true;for(int i=fine?FineOffset:Offset;i<512;i++)zero&=block[i]==0;
            if(zero)return null;
            if(fine){
                if(!FineAllowed(context))throw new InvalidDataException("Fine sensor source context is unavailable; original carrier retained.");
                bool counter64=context.FineCounter64;uint word=U(block,Offset+4);
                var receipt=context.Bound.Receipt;var binding=new NpSensorFineBinding {Domain=receipt.Domain,Generation=receipt.Generation,SourceEpoch=receipt.SourceEpoch,CounterHz=receipt.CounterHz,SensorValid=counter64?(word>>12)&7U:U(block,Offset+12)};
                for(int i=0;i<3;i++){binding.Sequence[i]=U(block,Offset+20+i*4);binding.SourceMs[i]=U(block,Offset+32+i*4);}
                var header=new byte[512];Buffer.BlockCopy(block,0,header,0,512);NpSensorFineStamp stamp;NpSensorFine64Stamp stamp64;
                var state=counter64?NpSensorFine64Wire.SdParse(header,binding,true,out stamp64):NpSensorFineWire.SdParse(header,binding,true,out stamp);
                if(state!=NpSensorFineState.Valid)
                    throw new InvalidDataException("FineAux SD sensor stamp/version/context/CRC mismatch.");
            }else if(U(block,Offset)!=Magic||U(block,Offset+4)!=2U||U(block,508)!=Crc(block,Offset,Bytes))
                throw new InvalidDataException("SD IMU metadata magic/version/CRC mismatch.");
            var raw=new byte[RecordBytes];Buffer.BlockCopy(block,Offset,raw,0,RecordBytes);
            var s=new NpSdImuSnapshot(raw){Enabled=U(raw,8),Valid=U(raw,12),PublishedMs=U(raw,16),
                ImuSequence=U(raw,20),MagSequence=U(raw,24),EnvSequence=U(raw,28),
                ImuMs=U(raw,32),MagMs=U(raw,36),EnvMs=U(raw,40),SensorTime=U(raw,44)};
            s.Continuous64=fine&&context.FineCounter64;
            if(s.Continuous64){s.Enabled=(U(raw,4)>>8)&3U;s.Valid=(U(raw,4)>>12)&7U;} // retain original+16 HAL-ms as provenance only
            if((s.Enabled&~3U)!=0||(s.Valid&~7U)!=0||((s.Valid&3U)!=0&&(s.Enabled&1U)==0)||
                ((s.Valid&4U)!=0&&(s.Enabled&2U)==0)||s.SensorTime>=0x1000000U)
                throw new InvalidDataException("Invalid SD IMU flags or sensor clock.");
            uint[] seq={s.ImuSequence,s.MagSequence,s.EnvSequence},tick={s.ImuMs,s.MagMs,s.EnvMs},age={100U,200U,3000U};
            for(int i=0;i<3;i++)if((s.Valid&(1U<<i))!=0&&(seq[i]==0||!s.Continuous64&&unchecked(s.PublishedMs-tick[i])>age[i]))
                throw new InvalidDataException("SD sensor marked valid without a fresh source observation.");
            for(int i=0;i<6;i++)s.Ag[i]=BitConverter.ToInt32(raw,48+i*4);
            for(int i=0;i<3;i++)s.Mag[i]=BitConverter.ToInt32(raw,72+i*4);
            return s;
        }
        // The caller owns this reusable copy. Never alter the archived header
        // or a capture/DMA buffer to make the established NN verifier accept it.
        internal static void NormalizeNeuralCopy(byte[] original,byte[] copy,bool allowFine=false)
        {
            if(original==null||copy==null||copy.Length!=original.Length||original.Length<512||ReferenceEquals(original,copy))
                throw new ArgumentException("A separate complete SD block copy is required.");
            if(!Present(original))throw new ArgumentException("SD IMU flag is not present.");
            uint flags=U(original,52);
            ValidateGeometry(flags,allowFine);bool fine=(flags&FineFlag)!=0;
            Buffer.BlockCopy(original,0,copy,0,original.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(flags&~(Flag|(fine?FineFlag:0U))),0,copy,52,4);
            Array.Clear(copy,fine?FineOffset:Offset,fine?FineRecordBytes:RecordBytes);
        }
    }

    // A bounded offline observer. Sensor failures are recorded independently
    // from neural integrity; the original compact sensor bytes remain exact.
    internal sealed class NpSdImuExport:IDisposable
    {
        readonly string folder;
        FileStream raw,fineRaw;StreamWriter csv;readonly NpFineSdCompanion fineContext;
        long fineCarriers,fineInvalid,fineAbsent;bool fineAdvertised,fineWriteDisabled,ignoredFineOrigin,gridNpLocked;
        string gridOriginEvidence;
        long observations,accepted,invalid,duplicates,sequenceGaps,resets;
        long gridRows,lateGridEvents;
        decimal? firstAgOffset,firstMagOffset;
        uint previousSequence,previousMs,previousSensor;bool havePrevious,advertised,completed,disposed,writeDisabled,gridComplete;
        string error;
        internal bool Advertised {get{return advertised;}}
        internal long Observations {get{return observations;}}
        internal long Samples {get{return accepted;}}
        internal long SequenceGaps {get{return sequenceGaps;}}
        internal string Error {get{return error;}}
        internal long GridRows {get{return gridRows;}}
        internal bool GridComplete {get{return gridComplete;}}
        internal bool FineAdvertised {get{return fineAdvertised;}}
        internal bool GridNpLocked {get{return gridNpLocked;}}
        internal string GridOriginEvidence {get{return gridOriginEvidence;}}
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        internal NpSdImuExport(string folder,NpFineSdCompanion fineContext=null){this.folder=folder;this.fineContext=fineContext;}
        internal void Feed(byte[] block)
        {
            if(!NpSdImuSnapshot.Present(block))return;
            advertised=true;
            bool fine=(U(block,52)&NpSdImuSnapshot.FineFlag)!=0;
            if(fine){fineAdvertised=true;if(!fineWriteDisabled)try{
                if(fineRaw==null)fineRaw=new FileStream(Path.Combine(folder,"imu_sd_fine_records.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
                fineRaw.Write(block,24,16);fineRaw.Write(block,NpSdImuSnapshot.FineOffset,NpSdImuSnapshot.FineRecordBytes);fineCarriers++;
            }catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException){fineWriteDisabled=true;if(error==null)error=ex.Message;}}
            if(writeDisabled)return;
            bool parsed=false;
            try{
                var s=NpSdImuSnapshot.Parse(block,fineContext);if(s==null){if(fine)fineAbsent++;return;}
                parsed=true;
                observations++;
                if(!fine&&raw==null)raw=new FileStream(Path.Combine(folder,"imu_sd_records.sns2"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
                if(csv==null){
                    csv=new StreamWriter(new FileStream(Path.Combine(folder,"sensors.csv"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536),new UTF8Encoding(false));
                    csv.WriteLine("block_sequence,source_tick,magic,version,enabled,valid,published_ms,imu_sequence,mag_sequence,env_sequence,imu_tick_ms,mag_tick_ms,env_tick_ms,sensor_time_24,accel_x_micro_g,accel_y_micro_g,accel_z_micro_g,gyro_x_milli_dps,gyro_y_milli_dps,gyro_z_milli_dps,mag_x_nano_tesla,mag_y_nano_tesla,mag_z_nano_tesla,temperature_centi_c,pressure_pa,humidity_milli_percent,imu_chip_id,env_chip_id,imu_error,env_error,imu_io_errors,env_io_errors,imu_timeouts,env_timeouts,missed_imu_samples,invalid_mag_samples,ignored_completions");
                }
                if(!fine){raw.Write(block,24,16);raw.Write(s.Raw,0,s.Raw.Length);}
                var line=new StringBuilder().Append(BitConverter.ToUInt64(block,24).ToString(Inv)).Append(',').Append(BitConverter.ToUInt64(block,32).ToString(Inv));
                for(int i=0;i<28;i++)line.Append(',').Append(s.Continuous64&&i==2?s.Enabled.ToString(Inv):s.Continuous64&&i==3?s.Valid.ToString(Inv):
                    i>=12&&i<=21?BitConverter.ToInt32(s.Raw,i*4).ToString(Inv):U(s.Raw,i*4).ToString(Inv));
                line.Append(",,,,,,,");csv.WriteLine(line.ToString()); // seven absent auxiliary counters
                if((s.Valid&1U)==0)return;
                if(havePrevious&&s.ImuSequence==previousSequence&&s.ImuMs==previousMs&&s.SensorTime==previousSensor){duplicates++;return;}
                if(havePrevious){uint delta=unchecked(s.ImuSequence-previousSequence),dt=unchecked(s.ImuMs-previousMs),ds=(s.SensorTime-previousSensor)&0xffffffU;
                    if(delta==0||delta>=0x80000000U||dt>=0x80000000U||ds>=0x800000U)resets++;
                    else if(delta>1U)sequenceGaps+=delta-1U;
                }
                accepted++;havePrevious=true;previousSequence=s.ImuSequence;previousMs=s.ImuMs;previousSensor=s.SensorTime;
            }catch(OperationCanceledException){throw;}
            catch(Exception ex){
                invalid++;if(error==null)error=ex.Message;
                if(fine)fineInvalid++;
                if(parsed)writeDisabled=true;
                else if(invalid==1)try{var bad=new byte[fine?144:132];Buffer.BlockCopy(block,24,bad,0,16);Buffer.BlockCopy(block,fine?NpSdImuSnapshot.FineOffset:NpSdImuSnapshot.Offset,bad,16,fine?NpSdImuSnapshot.FineRecordBytes:NpSdImuSnapshot.RecordBytes);File.WriteAllBytes(Path.Combine(folder,"imu_sd_first_bad_record.bin"),bad);}catch{}
            }
        }
        sealed class SensorEvent
        {
            internal decimal Us;internal bool Valid;internal int[] Values;
        }
        sealed class StoredSnapshot {internal ulong Sequence;internal NpSdImuSnapshot Snapshot;}
        IEnumerable<StoredSnapshot> StoredSnapshots(bool fine){
            string path=Path.Combine(folder,fine?"imu_sd_fine_records.bin":"imu_sd_records.sns2");if(!File.Exists(path))yield break;
            using(var input=File.OpenRead(path)){
                var record=new byte[fine?144:132];var header=new byte[512];
                Buffer.BlockCopy(BitConverter.GetBytes(NpSdImuSnapshot.Flag|40U|(fine?NpSdImuSnapshot.FineFlag:0U)),0,header,52,4);
                while(input.Position<input.Length){
                    int used=0,n;while(used<record.Length&&(n=input.Read(record,used,record.Length-used))!=0)used+=n;
                    if(used!=record.Length)throw new InvalidDataException("Truncated sensor replay record.");
                    Buffer.BlockCopy(record,16,header,fine?NpSdImuSnapshot.FineOffset:NpSdImuSnapshot.Offset,record.Length-16);
                    NpSdImuSnapshot snapshot=null;
                    if(fine){try{snapshot=NpSdImuSnapshot.Parse(header,fineContext);}catch(InvalidDataException){}}
                    else snapshot=NpSdImuSnapshot.Parse(header);
                    if(snapshot!=null)yield return new StoredSnapshot{Sequence=BitConverter.ToUInt64(record,0),Snapshot=snapshot};
                }
            }
        }
        IEnumerable<NpSdImuSnapshot> Snapshots(){
            // Both archives are written in verified block order. Hold one
            // record from each; ordinary fallback never gains fine coordinates.
            using(var ordinary=StoredSnapshots(false).GetEnumerator())using(var fine=StoredSnapshots(true).GetEnumerator()){
                bool haveOrdinary=ordinary.MoveNext(),haveFine=fine.MoveNext();
                while(haveOrdinary||haveFine){
                    if(haveOrdinary&&(!haveFine||ordinary.Current.Sequence<=fine.Current.Sequence)){
                        yield return ordinary.Current.Snapshot;haveOrdinary=ordinary.MoveNext();
                    }else{yield return fine.Current.Snapshot;haveFine=fine.MoveNext();}
                }
            }
        }
        IEnumerable<SensorEvent> Events(int group,uint originMs,decimal fractionUs)
        {
            bool seen=false,lastValid=false;uint lastSequence=0,lastTick=0;decimal lastUs=0;
            foreach(var snapshot in Snapshots()){
                bool valid=(snapshot.Valid&(1U<<group))!=0;
                uint sequence=group==0?snapshot.ImuSequence:snapshot.MagSequence;
                uint tick=valid?(group==0?snapshot.ImuMs:snapshot.MagMs):snapshot.PublishedMs;
                decimal us=unchecked((int)(tick-originMs))*1000m-fractionUs;
                if(us<0)continue; // never turn pre-origin sensor data into post-origin measurements
                if(seen&&valid==lastValid&&(!valid||sequence==lastSequence&&tick==lastTick))continue;
                if(seen&&us<lastUs){lateGridEvents++;continue;}
                seen=true;lastValid=valid;lastSequence=sequence;lastTick=tick;lastUs=us;
                if(valid){if(group==0&&!firstAgOffset.HasValue)firstAgOffset=us;if(group==1&&!firstMagOffset.HasValue)firstMagOffset=us;}
                yield return new SensorEvent {Us=us,Valid=valid,Values=group==0?snapshot.Ag:snapshot.Mag};
            }
        }
        static bool EncodeGroup(byte[] row,int first,SensorEvent source,decimal now,int group)
        {
            if(source==null||!source.Valid||now-source.Us>(group==0?100000m:200000m))return false;
            int count=group==0?6:3;
            for(int axis=0;axis<count;axis++){
                int physical=source.Values[axis];
                double code=group==0?(axis<3?physical*4096.0/1000000.0:physical*164.0/10000.0):physical/300.0;
                long value=(long)Math.Round(code,MidpointRounding.AwayFromZero);
                if(value<short.MinValue||value>short.MaxValue){
                    for(int n=0;n<count;n++){int at=(first+n)*2;row[at]=0;row[at+1]=0x80;}
                    return false;
                }
                int encoded=(first+axis)*2;row[encoded]=(byte)value;row[encoded+1]=(byte)(value>>8);
            }
            return true;
        }
        void AnalogGrid(uint sourceHz,ulong sourceExtent,NpSdImuGridOrigin origin,Func<bool> cancelled)
        {
            if(origin!=null&&origin.SourceHz!=sourceHz)throw new InvalidDataException("NP grid origin/source rate mismatch.");
            uint originMs=0;bool haveOrigin=origin!=null;
            if(origin!=null)originMs=origin.DeviceMs;
            else foreach(var snapshot in Snapshots())if((snapshot.Valid&1U)!=0){originMs=snapshot.ImuMs;haveOrigin=true;break;}
            decimal fractionUs=origin==null?0:origin.FractionUs;
            ulong count=checked(sourceExtent/sourceHz*1250UL+((sourceExtent%sourceHz)*1250UL+sourceHz-1UL)/sourceHz);
            string analog=Path.Combine(folder,"analogin.dat"),validity=Path.Combine(folder,"analogin_validity.dat");
            using(var output=new FileStream(analog+".partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536))
            using(var masks=new FileStream(validity+".partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536))
            using(var ag=Events(0,originMs,fractionUs).GetEnumerator())
            using(var mag=Events(1,originMs,fractionUs).GetEnumerator()){
                bool nextAg=haveOrigin&&ag.MoveNext(),nextMag=haveOrigin&&mag.MoveNext();SensorEvent heldAg=null,heldMag=null;
                var bytes=new byte[2048*32];var flags=new byte[2048];var row=new byte[32];int used=0;
                for(ulong index=0;index<count;index++){
                    if(index%1250UL==0&&cancelled!=null&&cancelled())throw new OperationCanceledException("SD IMU grid export cancelled.");
                    decimal now=index*800m;
                    while(nextAg&&ag.Current.Us<=now){heldAg=ag.Current;nextAg=ag.MoveNext();}
                    while(nextMag&&mag.Current.Us<=now){heldMag=mag.Current;nextMag=mag.MoveNext();}
                    for(int at=0;at<row.Length;at+=2){row[at]=0;row[at+1]=0x80;}
                    uint valid=0;if(EncodeGroup(row,1,heldAg,now,0))valid|=1;if(EncodeGroup(row,7,heldMag,now,1))valid|=2;
                    Buffer.BlockCopy(row,0,bytes,used*32,32);flags[used]=(byte)valid;used++;gridRows++;
                    if(used==flags.Length){output.Write(bytes,0,used*32);masks.Write(flags,0,used);used=0;}
                }
                if(used!=0){output.Write(bytes,0,used*32);masks.Write(flags,0,used);}
                output.Flush(true);masks.Flush(true);
            }
            File.Move(analog+".partial",analog);File.Move(validity+".partial",validity);
            File.WriteAllLines(Path.Combine(folder,"analogin_encoding.txt"),new[]{
                "schema=NP_SD_ANALOGIN_CE64_COUNTS_V1; file=analogin.dat; record_bytes=32; words=16 little-endian 16-bit words",
                "sensor_encoding=CE64-equivalent signed int16 counts reconstructed from physical units; not recovered raw ADC/register values",
                "sensor_words=2-4 accel; 5-7 gyro; 8-10 compensated magnetometer; unavailable=-32768; other words unavailable",
                "accel_counts=round_away_from_zero(accel_micro_g*4096/1000000); configured_range_g=+/-8",
                "gyro_counts=round_away_from_zero(gyro_milli_dps*16.4/1000); configured_range_dps=+/-2000",
                "mag_counts=round_away_from_zero(mag_nano_tesla/300); not packed BMM150 register values",
                "out_of_range=whole sensor group unavailable, matching existing SD export; validity_file=analogin_validity.dat; one byte per row; mask1=AG mask2=MAG",
                "output_grid_hz=1250; period_us=800; zero-order hold; not 1250 Hz sensor acquisition",
                "source_tick_per_grid_row_numerator="+sourceHz+"; denominator=1250; verified_np_source_extent="+sourceExtent,
                "np_byte_zero_sample_grid_locked="+(origin!=null)+"; native_sensor_conversion_phase_locked=false",
                "grid_origin_device_ms="+(haveOrigin?originMs.ToString(Inv):"unavailable")+"; fraction_us="+fractionUs.ToString(Inv),
                "origin_evidence="+(origin==null?"unavailable; unsynchronized first recorded AG tick fallback; NP offset and UTC unknown":origin.Evidence),
                "final_capture_generation="+(origin==null?"unavailable":origin.Generation.ToString(Inv)),
                "first_retained_AG_source_offset_us="+(firstAgOffset.HasValue?firstAgOffset.Value.ToString(Inv):"unavailable")+"; first_retained_MAG_source_offset_us="+(firstMagOffset.HasValue?firstMagOffset.Value.ToString(Inv):"unavailable"),
                "native_source_time=I2C completion milliseconds; sensor conversion/filter phase is not measured by this grid",
                "pre_origin_observations_excluded=true; AG expires after100ms; MAG after200ms; late_source_events_skipped="+lateGridEvents,
                "raw_physical_provenance=sensors.csv; ordinary=imu_sd_records.sns2; fine=imu_sd_fine_records.bin (when present); absent samples/counters are not synthesized; no PC/download-time UTC substituted"
            });
            gridComplete=true;gridNpLocked=origin!=null;gridOriginEvidence=origin==null?"Unsynchronized fallback; NP byte-zero offset and UTC unavailable; see encoding file":origin.Evidence;
        }
        internal void Complete(uint sourceHz=0,ulong sourceExtent=0,NpSdImuGridOrigin origin=null,Func<bool> cancelled=null)
        {
            if(sourceHz!=0&&advertised){
                try{
                    if(csv!=null){csv.Dispose();csv=null;}if(raw!=null){raw.Dispose();raw=null;}if(fineRaw!=null){fineRaw.Dispose();fineRaw=null;}
                    if(fineAdvertised&&origin!=null){ignoredFineOrigin=true;origin=null;if(error==null)error="FineAux caller grid origin ignored: producer continuity/phase contract is not qualified.";}
                    AnalogGrid(sourceHz,sourceExtent,origin,cancelled);
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){if(error==null)error=ex.Message;}
            }
            completed=true;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try{if(csv!=null)csv.Dispose();}catch(Exception ex){if(error==null)error=ex.Message;}
            try{if(raw!=null)raw.Dispose();}catch(Exception ex){if(error==null)error=ex.Message;}
            try{if(fineRaw!=null)fineRaw.Dispose();}catch(Exception ex){if(error==null)error=ex.Message;}
            if(!advertised)return;
            try{File.WriteAllLines(Path.Combine(folder,"imu_sd_export_status.txt"),new[]{
                fineAdvertised?(fineContext!=null&&fineContext.FineCounter64?"schema=SNS2-v4-continuous64-carrier128; raw_record_bytes=144; final qualification/output=see imu_fine_timing_status.json":
                    "schema=SNS2-v3-fine-carrier128; raw_record_bytes=144 (uint64 block sequence, uint64 source tick, original128B carrier); fine timing unqualified"):
                    "schema=SNS2-v2-prefix112; raw_record_bytes=132 (uint64 block sequence, uint64 source tick,112B snapshot,CRC32)",
                "export_complete="+completed,"snapshot_observations="+observations.ToString(Inv),"distinct_valid_imu_observations="+accepted.ToString(Inv),
                "duplicate_imu_observations="+duplicates.ToString(Inv),"invalid_sensor_observations="+invalid.ToString(Inv),"sequence_gaps="+sequenceGaps.ToString(Inv),"reset_segments="+resets.ToString(Inv),
                (fineContext!=null&&fineContext.FineCounter64?"fallback_derived_1250_grid_rows=":"derived_1250_grid_rows=")+gridRows.ToString(Inv),
                (fineContext!=null&&fineContext.FineCounter64?"fallback_derived_grid_complete=":"derived_grid_complete=")+gridComplete,
                "error="+(error??"none"),(fineAdvertised?"SNS2_v3_profile=":"SNS2_v2_profile=")+"accel200Hz_8g;gyro200Hz_2000dps;mag100Hz_regular_preset; hardware/firmware profile convention, not measured retained rate",
                "physical_units=accel_micro_g,gyro_milli_dps,mag_nano_tesla,temperature_centi_c,pressure_pa,humidity_milli_percent",
                "source=recorded latest-value snapshots; source sequence gaps do not count every lost ADC conversion",
                "absent_counters=imu_io_errors,env_io_errors,imu_timeouts,env_timeouts,missed_imu_samples,invalid_mag_samples,ignored_completions; empty CSV cells mean unavailable",
                "The 1250Hz compatibility grid repeats held values or unavailable sentinels. Native conversion rate is unchanged. Sensor export quality is independent from neural verification."
            });}catch{}
            if(fineAdvertised)try {File.WriteAllLines(Path.Combine(folder,"imu_sd_fine_encoding.txt"),new[]{
                "schema=NP_SD_FINE_AUX_RAW_V1; file=imu_sd_fine_records.bin; record_bytes=144",
                "layout=uint64_le block_sequence,uint64_le NP_source_tick,original_header_bytes384..511 (128B)",
                fineContext!=null&&fineContext.FineCounter64?"core=SNS2 at396..507; version4 with packed enabled/valid/mask; publicationCounter64 at404; original publication HAL-ms at412 retained as provenance, not phase":
                    "core=SNS2 at396..507; version3|(component_mask<<8); original native sequences20/24/28 and source_ms32/36/40 are retained inside that core",
                "raw_carriers_saved="+fineCarriers+"; absent_carriers="+fineAbsent+"; invalid_optional_records="+fineInvalid,
                (fineContext!=null&&fineContext.FineCounter64?"record_features25_bound=":"record_features9_bound=")+NpSdImuSnapshot.FineAllowed(fineContext)+"; raw_archive_disabled="+fineWriteDisabled,
                "caller_grid_origin_ignored="+ignoredFineOrigin+(fineContext!=null&&fineContext.FineCounter64?"; final_grid_np_lock=see imu_fine_timing_status.json":"; actual_grid_np_locked="+gridNpLocked),
                fineContext!=null&&fineContext.FineCounter64?"qualification_and_final_output=see imu_fine_timing_status.json; raw archive alone is not clock proof; native_conversion_phase_locked=false":
                    "fine_timestamp_qualified=false; fine_grid_phase_applied=false; producer_continuity_qualified=false; native_conversion_phase_locked=false",
                "All original carriers, including sparse/malformed optional data, are diagnostic provenance. A validated context/CRC is not native sensor conversion phase or physical sample lock."
            });}catch{}
        }
    }
}
