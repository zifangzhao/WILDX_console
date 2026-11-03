using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Actual firmware slot ordinals, never resampled observations. Optional
    // metadata failure is isolated by the owning caller from neural data.
    internal sealed class NpImuGridWriter:IDisposable {
        readonly string folder;readonly uint generation;
        readonly FileStream data,validity;readonly StreamWriter gaps,segments;
        readonly byte[] row=new byte[32];bool disposed,completed;
        internal ulong Rows {get;private set;} internal ulong Slots {get;private set;} internal ulong Observed,Missing,AgRepeated,MagRepeated;
        ulong segmentSlot,segmentRow;bool sparse;
        ulong? firstObserved,lastObserved;NpImuGridPhaseRecord phase;byte[] phaseRaw;uint phaseSourceHz;string phaseError;
        ulong phaseReferenceTick;
        readonly TimeZoneInfo localZone=TimeZoneInfo.Local;
        const string PcTimeUnavailable="in-row PC time words=unavailable";
        internal bool PhaseQualified {get;private set;}
        internal bool SourceWindowVerified {get;private set;}
        internal bool EnforcedScheduleVerified {get;private set;}
        internal bool FirstSlotPhaseValid {get;private set;}
        string firstSlotError="Saved first-source/first-slot evidence has not been verified.";
        internal sealed class Checkpoint {internal ulong Rows,Slots,Observed,Missing,Ag,Mag,SegmentSlot,SegmentRow;internal ulong? First,Last;internal long GapBytes,SegmentBytes;internal bool Sparse;}
        internal Checkpoint Mark(){gaps.Flush();segments.Flush();return new Checkpoint{Rows=Rows,Slots=Slots,Observed=Observed,Missing=Missing,Ag=AgRepeated,Mag=MagRepeated,GapBytes=gaps.BaseStream.Position,SegmentBytes=segments.BaseStream.Position,SegmentSlot=segmentSlot,SegmentRow=segmentRow,Sparse=sparse,First=firstObserved,Last=lastObserved};}
        internal void Restore(Checkpoint p){if(p==null)return;gaps.Flush();segments.Flush();data.SetLength(checked((long)p.Rows*32));data.Position=data.Length;validity.SetLength(checked((long)p.Rows));validity.Position=validity.Length;gaps.BaseStream.SetLength(p.GapBytes);gaps.BaseStream.Position=p.GapBytes;segments.BaseStream.SetLength(p.SegmentBytes);segments.BaseStream.Position=p.SegmentBytes;Rows=p.Rows;Slots=p.Slots;Observed=p.Observed;Missing=p.Missing;AgRepeated=p.Ag;MagRepeated=p.Mag;segmentSlot=p.SegmentSlot;segmentRow=p.SegmentRow;sparse=p.Sparse;firstObserved=p.First;lastObserved=p.Last;}
        internal static NpImuGridPhaseBinding PhaseBinding(string uid,ulong token,NpFirstSyncReceipt receipt){
            var bytes=new byte[12];var words=NpFirstSyncUsb.UidWords(uid);for(int i=0;i<3;i++)Buffer.BlockCopy(BitConverter.GetBytes(words[i]),0,bytes,i*4,4);
            return new NpImuGridPhaseBinding{Uid=bytes,Session=receipt.Session,CaptureGeneration=receipt.Generation,SourceEpoch=receipt.SourceEpoch,CounterDomain=receipt.Domain,Token=token};
        }
        internal static bool FirstSourceMatches(NpFirstSyncReceipt receipt,int port,long firstTick,uint firstCounter,long apRows,long frames,long rawBytes){
            if(receipt==null||port<1||port>3||firstTick<0||apRows<=0||frames<13||rawBytes<=0)return false;
            var r=NpFirstSyncWire.Parse(receipt.Raw);if(r.Family!=1||(r.Mask&(1U<<(port-1)))==0)return false;
            var p=r.Probes[port-1];return (ulong)firstTick==p.FirstTick&&firstCounter==p.Counter&&
                (ulong)rawBytes>=p.FirstTick&&(ulong)rawBytes-p.FirstTick>=780UL;
        }
        static decimal FirstOffsetUs(long numerator,uint denominator,uint sourceHz,bool upper){
            decimal n=(decimal)numerator*1000000m,d=(decimal)denominator*sourceHz,q=Decimal.Truncate(n/d),r=n-q*d;
            // Correct quotient rounding using exact integer products before
            // outward rounding, including large valid wire numerators.
            if(r<0){q--;r+=d;}else if(r>=d){q++;r-=d;}
            return upper&&r!=0?q+1:q;
        }
        internal bool AcceptPhase(byte[] raw,NpImuGridPhaseBinding binding,uint sourceHz,NpFirstSyncReceipt receipt){
            if(disposed||completed)return false;
            try{
                var parsed=NpImuGridPhase.Read(raw,binding);if(parsed.CaptureGeneration!=generation||sourceHz==0)throw new InvalidDataException("Grid phase final generation/source mismatch.");
                var r=NpFirstSyncWire.Parse(receipt.Raw);var a=r.Aux[0];
                if((r.Flags&(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts|NpFirstSyncWire.SourceClock))!=(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts|NpFirstSyncWire.SourceClock)||
                    r.Session!=binding.Session||r.Generation!=binding.CaptureGeneration||r.SourceEpoch!=binding.SourceEpoch||r.Domain!=binding.CounterDomain||r.SourceHz!=sourceHz||
                    (r.AuxMask&1)==0||a.Id!=1||(a.Flags&(1|4))!=(1|4)||a.CounterBits!=64||a.Counter!=parsed.FirstAcceptedSlot||a.RateNumerator!=1250||a.RateDenominator!=1)
                    throw new InvalidDataException("Grid phase contradicts the final NPSTART first native slot/rate proof.");
                // An explicit unknown interval is valid retained evidence,
                // not an assertion that a first-source point exists. Never
                // borrow a nominal/release timestamp to fill that interval.
                // NSGCv2 (or the matched SD declaration) attests configured,
                // enforced copy deadlines. A same-domain NSGP then needs no
                // redundant HAL POINT; v1's early observed maximum does not
                // establish that guarantee. First-only evidence stays valid.
                bool measuredStartOnly=parsed.Flags==NpImuGridPhase.IntervalValid&&parsed.MaximumJitter==UInt32.MaxValue&&(a.Flags&64)==0;
                bool enforcedCounterOnly=EnforcedScheduleVerified&&parsed.Flags==3&&(a.Flags&64)==0;
                if((parsed.Flags&NpImuGridPhase.IntervalValid)!=0&&((a.Flags&64)==0||a.StampKind!=1)&&!measuredStartOnly&&!enforcedCounterOnly)
                    throw new InvalidDataException("Claimed grid phase interval lacks the final NPSTART first-source point proof.");
                if(phaseRaw!=null){bool same=phaseRaw.Length==raw.Length;for(int i=0;same&&i<raw.Length;i++)same=phaseRaw[i]==raw[i];if(!same)throw new InvalidDataException("Conflicting once-only grid phase.");return phaseError==null;}
                phaseRaw=(byte[])raw.Clone();phase=parsed;phaseSourceHz=sourceHz;phaseReferenceTick=r.ReferenceTick;File.WriteAllBytes(Path.Combine(folder,"imu_grid_phase.bin"),phaseRaw);return phaseError==null;
            }catch(InvalidDataException ex){phaseError=ex.Message;PhaseQualified=false;Status(false,phaseError);return false;}
        }
        internal const ulong MaximumGap=1250UL*60; // bounded work per malformed/absent batch
        internal NpImuGridWriter(string folder,uint generation,bool enforcedScheduleVerified=false){
            if(generation==0)throw new InvalidDataException("IMU grid requires an actual final capture generation.");this.folder=folder;this.generation=generation;
            EnforcedScheduleVerified=enforcedScheduleVerified;
            data=new FileStream(Path.Combine(folder,"analogin.dat"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
            try{validity=new FileStream(Path.Combine(folder,"analogin_validity.dat"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);gaps=new StreamWriter(new FileStream(Path.Combine(folder,"imu_grid_gaps.csv"),FileMode.CreateNew,FileAccess.Write,FileShare.Read));gaps.WriteLine("first_slot,slot_count,reason");segments=new StreamWriter(new FileStream(Path.Combine(folder,"imu_grid_segments.csv"),FileMode.CreateNew,FileAccess.Write,FileShare.Read));segments.WriteLine("first_slot,row_count,first_file_row");Status(false,"Capture has not finalized; source window unverified.");}
            catch{data.Dispose();if(validity!=null)validity.Dispose();if(gaps!=null)gaps.Dispose();if(segments!=null)segments.Dispose();throw;}
        }
        static short Scale(int n,int numerator,int denominator){long a=n<0?-(long)n:n;long v=(a*numerator+denominator/2)/denominator;if(n<0)v=-v;return (short)Math.Max(short.MinValue,Math.Min(short.MaxValue,v));}
        static void Put(byte[] b,int at,short n){b[at]=(byte)n;b[at+1]=(byte)(n>>8);}
        void Emit(int[] values,byte flags){Array.Clear(row,0,row.Length);for(int a=0;a<9;a++)Put(row,2+a*2,values!=null&&(flags&(a<6?2:4))!=0?Scale(values[a],a<3?4096:a<6?164:1,a<3?1000000:a<6?10000:300):short.MinValue);Put(row,20,short.MinValue);Put(row,22,-1);data.Write(row,0,32);validity.WriteByte(flags);Rows++;Slots++;}
        void CloseSegment(){if(Rows!=segmentRow)segments.WriteLine(segmentSlot+","+(Rows-segmentRow)+","+segmentRow);segmentSlot=Slots;segmentRow=Rows;}
        void MissingUntil(ulong end,string reason){if(end<Slots)throw new InvalidDataException("IMU grid slot range goes backwards.");if(end==Slots)return;ulong n=end-Slots;gaps.WriteLine(Slots.ToString(CultureInfo.InvariantCulture)+","+n.ToString(CultureInfo.InvariantCulture)+","+reason+(n>MaximumGap?"_sparse":""));Missing+=n;if(n>MaximumGap){CloseSegment();Slots=end;segmentSlot=end;sparse=true;return;}while(Slots<end)Emit(null,0);}
        internal void Feed(byte[] frame){if(disposed||completed)throw new ObjectDisposedException("IMU grid writer");var b=NpImuGridWire.Parse(frame);if(b.CaptureGeneration!=generation||b.FirstSlot<Slots)throw new InvalidDataException("IMU grid generation/overlapping slot range mismatch.");MissingUntil(b.FirstSlot,"transport_gap");int r=0;for(int i=0;i<b.SlotCount;i++){int bit=1<<i;if((b.ObservedMask&bit)==0){gaps.WriteLine(Slots+",1,producer_missing");Emit(null,0);Missing++;continue;}if(!firstObserved.HasValue)firstObserved=Slots;lastObserved=Slots;byte flags=1;if((b.AgValid&bit)!=0)flags|=2;if((b.MagValid&bit)!=0)flags|=4;if((b.AgRepeat&bit)!=0){flags|=8;AgRepeated++;}if((b.MagRepeat&bit)!=0){flags|=16;MagRepeated++;}if((b.ErrorMask&bit)!=0)flags|=32;Emit(b.Rows[r++],flags);Observed++;}}
        internal void Complete(ulong sourceExtent,uint sourceHz,ulong referenceTick,bool verified,bool firstSourceVerified=false){
            if(disposed||completed)return;completed=true;
            FirstSlotPhaseValid=firstSourceVerified&&phaseError==null&&phase!=null&&
                (phase.Flags&NpImuGridPhase.IntervalValid)!=0&&phaseSourceHz==sourceHz&&sourceHz!=0&&
                firstObserved.HasValue&&firstObserved.Value==phase.FirstAcceptedSlot;
            firstSlotError=FirstSlotPhaseValid?null:phaseError??"Saved first NP byte/counter, measured first-slot interval or actual stored first ordinal unavailable.";
            if(!verified||sourceHz==0){Status(false,"Retained recording/source proof unavailable.");return;}
            // No zero-phase assumption: first accepted slot has a positive
            // interval, and loss of that actual stored observation disqualifies
            // the phase even if the remaining sequence is regular.
            if(!EnforcedScheduleVerified||!FirstSlotPhaseValid||phaseError!=null||phase==null||(phase.Flags&3)!=3){Status(false,phaseError??"Enforced schedule, saved first slot or phase/jitter bound unavailable.");return;}
            // All supported NP rates have integer source ticks per slot.
            // Compare cross-products exactly so a tiny phase fraction cannot
            // disappear when added to a large genuine slot ordinal.
            ulong delta=lastObserved.Value-phase.FirstAcceptedSlot;
            uint jitter=delta==0?0:phase.MaximumJitter;
            decimal step=sourceHz/1250U,remaining=(decimal)sourceExtent-referenceTick-delta*step-jitter;
            bool lowerKnown=delta==0||phase.LowerNumerator+(step-phase.MaximumJitter)*phase.Denominator>=0;
            SourceWindowVerified=sourceHz%1250==0&&sourceExtent>=referenceTick&&lowerKnown&&remaining>=0&&phase.UpperNumerator<=remaining*phase.Denominator;
            Status(true,null);
        }
        internal void Report(string error){FirstSlotPhaseValid=false;firstSlotError=error;try{Status(false,error);}catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){}}
        internal void Status(bool qualified,string error){PhaseQualified=false;if(!qualified)SourceWindowVerified=false;File.WriteAllLines(Path.Combine(folder,"analogin_encoding.txt"),new[]{
            "schema=NP_IMU_NSG1_GRID_V1","file=analogin.dat; record_bytes=32; words=16 little-endian 16-bit words",
            "sensor_words=2-4 accel; 5-7 gyro; 8-10 magnetometer; WILD-equivalent counts",
            "accel_counts=round_away_from_zero(micro_g*4096/1000000); gyro_counts=round_away_from_zero(milli_dps*164/10000); mag_counts=round_away_from_zero(nano_tesla/300)",
            "unavailable_sensor=-32768; word11 slowADC=-32768; word12 nativeSequence=-1; remaining unavailable words=0",
            "output_grid_hz=1250; period_us=800; first_slot=0; NP_reference_phase_offset_us="+(qualified?"see once-only source-tick bounds":"unknown"),
            "sampling_semantics=HSE-scheduled cached snapshots; repeated conversions allowed; not physical conversion-phase lock",
            "enforced_schedule_verified="+EnforcedScheduleVerified,"source_window_verified="+SourceWindowVerified,"grid_phase_qualified="+qualified,"grid_phase_scope=retained observed cached copies; normal qualified HSE configuration and observed-fault invalidation",
            "enforced_schedule_evidence=explicit matched version2 selection/declaration; NSGP jitter flag or historical observed maximum alone is insufficient",
            "np_sample_grid_locked=false; first accepted slot has positive phase, no SAMPLE0_LOCK inferred",
            "final_capture_generation="+generation,"rows="+Rows,"slot_extent="+Slots,"sparse_unavailable_segments="+sparse,
            "terminal_tail_coverage=unknown; no producer-measured final scheduled ordinal/drop receipt; retained-row source-window check is not tail completeness",
            "segments_file=imu_grid_segments.csv; maps file rows to genuine slot ordinals; gaps above 60000ms are not filled",
            "first_observed_slot="+(firstObserved.HasValue?firstObserved.Value.ToString(CultureInfo.InvariantCulture):"unknown"),
            "phase_file="+(phaseRaw==null?"unknown":"imu_grid_phase.bin"),"first_accepted_slot="+(phase==null?"unknown":phase.FirstAcceptedSlot.ToString(CultureInfo.InvariantCulture)),
            "phase_lower_source_tick_numerator="+(phase==null?"unknown":phase.LowerNumerator.ToString(CultureInfo.InvariantCulture)),
            "phase_upper_source_tick_numerator="+(phase==null?"unknown":phase.UpperNumerator.ToString(CultureInfo.InvariantCulture)),
            "phase_denominator="+(phase==null?"unknown":phase.Denominator.ToString(CultureInfo.InvariantCulture)),
            "maximum_relative_slot_jitter_source_ticks="+(phase==null?"unknown":phase.MaximumJitter.ToString(CultureInfo.InvariantCulture)),
            "phase_source_hz="+phaseSourceHz,"relative_time_formula_seconds=first observed slot: T0+[A/F,B/F]; later observed slot: T0+[(A-J)/F+delta/1250,(B+J)/F+delta/1250]; A/B=phase numerators/denominator, F=phase_source_hz, J=source-tick jitter; missing rows have no value/time claim",
            "jitter_semantics=configured enforced relative copy bound; not an early observed maximum; applies only when grid_phase_qualified=True",
            "first_slot_phase_valid="+FirstSlotPhaseValid,"first_slot_phase_scope="+(FirstSlotPhaseValid?"start_only; first actually observed stored slot":"unknown"),
            "first_slot_event=accepted scheduled cached snapshot; not native sensor conversion phase or SAMPLE0_LOCK",
            "first_slot_offset_lower_us="+(FirstSlotPhaseValid?FirstOffsetUs(phase.LowerNumerator,phase.Denominator,phaseSourceHz,false).ToString(CultureInfo.InvariantCulture):"unknown"),
            "first_slot_offset_upper_us="+(FirstSlotPhaseValid?FirstOffsetUs(phase.UpperNumerator,phase.Denominator,phaseSourceHz,true).ToString(CultureInfo.InvariantCulture):"unknown"),
            "first_slot_interval_rounding=outward whole microseconds; exact source-tick fractions retained above",
            "first_slot_phase_error="+(firstSlotError??"none"),
            "first_slot_time_formula=T0 + measured first-slot interval; no claim for later slots when grid_phase_qualified=False",
            "RTC_to_NP_uncertainty=separate; see recording_start_utc.json; no per-slot UTC file",
            "observed="+Observed,"missing="+Missing,"ag_repeated="+AgRepeated,"mag_repeated="+MagRepeated,
            "validity_file=analogin_validity.dat; bits observed1,AGvalid2,MAGvalid4,AGrepeat8,MAGrepeat16,error32; missing=0",
            "gaps_file=imu_grid_gaps.csv; native_sensor_conversion_phase_locked=false",PcTimeUnavailable,"error="+(error??"none")});PhaseQualified=qualified;}

        void WritePcTime(){
            if(!completed||!FirstSlotPhaseValid)return;
            string clockPath=Path.Combine(folder,"recording_start_utc.json");
            if(!File.Exists(clockPath)||new FileInfo(clockPath).Length>1048576)return;
            var clock=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(clockPath));
            if(clock==null)return;
            object Get(string key){object value;return clock.TryGetValue(key,out value)?value:null;}
            bool Flag(string key){return Equals(Get(key),true);}
            ulong Number(string key){ulong n;return UInt64.TryParse(Convert.ToString(Get(key),CultureInfo.InvariantCulture),NumberStyles.None,CultureInfo.InvariantCulture,out n)?n:UInt64.MaxValue;}
            string uid=String.Format(CultureInfo.InvariantCulture,"{0:X8}{1:X8}{2:X8}",
                BitConverter.ToUInt32(phase.Uid,0),BitConverter.ToUInt32(phase.Uid,4),BitConverter.ToUInt32(phase.Uid,8));
            if(!Flag("utc_valid")||!Flag("rtc_local_mapping_qualified")||!Flag("np_start_byte_sync_confirmed")||
                !Equals(Get("clock_reference"),"device_hardware_rtc")||
                !String.Equals(Get("device_identity") as string,uid,StringComparison.OrdinalIgnoreCase)||
                Number("session_id")!=phase.Session||Number("capture_generation")!=generation||
                Number("source_epoch")!=phase.SourceEpoch||Number("counter_domain")!=phase.CounterDomain||
                Number("reference_source_tick")!=phaseReferenceTick||Number("reference_source_hz")!=phaseSourceHz)return;
            var calibration=Get("pc_epoch_calibration") as Dictionary<string,object>;
            object declared,calGeneration;
            if(calibration==null||!calibration.TryGetValue("declared",out declared)||!Equals(declared,true)||
                !calibration.TryGetValue("generation",out calGeneration)||Convert.ToUInt32(calGeneration,CultureInfo.InvariantCulture)==0)return;
            ulong originUs=Number("recording_start_unix_us");
            if(originUs<946684800000000UL||originUs>=4102444800000000UL)return;
            var originUtc=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddTicks((long)originUs*10);
            int offsetMinutes;
            bool originalOffset=Int32.TryParse(Convert.ToString(Get("recording_pc_utc_offset_minutes"),CultureInfo.InvariantCulture),
                NumberStyles.Integer,CultureInfo.InvariantCulture,out offsetMinutes)&&offsetMinutes>=-840&&offsetMinutes<=840;
            if(!originalOffset)offsetMinutes=(int)localZone.GetUtcOffset(originUtc).TotalMinutes;
            const long dayUs=86400000000L;
            decimal phaseUs=((decimal)phase.LowerNumerator+phase.UpperNumerator)*500000m/(phase.Denominator*(decimal)phaseSourceHz);
            long firstLocalUs=((long)Math.Floor(originUs+phaseUs)+offsetMinutes*60000000L)%dayUs;
            string path=Path.Combine(folder,"analogin.dat"),temporary=path+"."+Guid.NewGuid().ToString("N")+".partial";
            string encodingPath=Path.Combine(folder,"analogin_encoding.txt");
            string encoding=File.ReadAllText(encodingPath);
            if(!encoding.Contains(PcTimeUnavailable))return;
            ulong stamped=0;
            try{
                using(var input=new BinaryReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536)))
                using(var observed=new BinaryReader(new FileStream(Path.Combine(folder,"analogin_validity.dat"),FileMode.Open,FileAccess.Read,FileShare.Read,65536)))
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536)){
                    ulong fileRow=0,previousEnd=0;
                    foreach(string line in File.ReadLines(Path.Combine(folder,"imu_grid_segments.csv")).Skip(1)){
                        string[] fields=line.Split(',');
                        ulong slot,count,start;
                        if(fields.Length!=3||!UInt64.TryParse(fields[0],out slot)||!UInt64.TryParse(fields[1],out count)||
                            !UInt64.TryParse(fields[2],out start)||start!=fileRow||slot<previousEnd||count==0||count>Rows-fileRow)
                            throw new InvalidDataException("Invalid IMU grid segment while writing PC time.");
                        previousEnd=checked(slot+count);
                        while(count>0){
                            int batch=(int)Math.Min(count,2048UL);
                            byte[] rows=input.ReadBytes(batch*32);
                            byte[] valid=observed.ReadBytes(batch);
                            if(rows.Length!=batch*32||valid.Length!=batch)throw new EndOfStreamException("Incomplete IMU grid row or validity data.");
                            for(int i=0;i<batch;i++,slot++){
                                if((valid[i]&1)==0||slot<phase.FirstAcceptedSlot||(!PhaseQualified&&slot!=phase.FirstAcceptedSlot))continue;
                                ulong delta=(slot-phase.FirstAcceptedSlot)%(ulong)(dayUs/800);
                                uint ms=(uint)(((firstLocalUs+(long)delta*800L)%dayUs)/1000L);
                                int at=i*32+28;
                                for(int b=0;b<4;b++)rows[at+b]=(byte)(ms>>(8*b));
                                stamped++;
                            }
                            output.Write(rows,0,rows.Length);
                            count-=(ulong)batch;
                            fileRow+=(ulong)batch;
                        }
                    }
                    if(fileRow!=Rows||input.BaseStream.Position!=input.BaseStream.Length||observed.BaseStream.Position!=observed.BaseStream.Length)
                        throw new InvalidDataException("IMU grid segments do not cover the stored rows.");
                    output.Flush(true);
                }
                if(stamped==0)return;
                File.Replace(temporary,path,null);
                string description="in-row PC time words=15-16, uint32 little-endian local milliseconds since midnight, modulo 86400000"+
                    Environment.NewLine+"pc_time_basis=PC-calibrated device RTC at NP STARTBYTE plus measured first-slot phase midpoint and true slot ordinal"+
                    Environment.NewLine+"pc_time_local_offset_minutes="+offsetMinutes.ToString(CultureInfo.InvariantCulture)+
                    Environment.NewLine+"pc_time_local_offset_basis="+(originalOffset||Flag("usb_device_rtc_mapping_selected")?"recording PC":"export PC timezone at recording origin")+
                    Environment.NewLine+"pc_time_rows="+stamped.ToString(CultureInfo.InvariantCulture)+
                    Environment.NewLine+"pc_time_scope="+(PhaseQualified?"observed_slots":"first_observed_slot")+
                    Environment.NewLine+"pc_time_validity=observed slots within the qualified phase scope only; zero also represents midnight";
                File.WriteAllText(encodingPath,encoding.Replace(PcTimeUnavailable,description));
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }

        public void Dispose(){
            if(disposed)return;
            disposed=true;
            try{CloseSegment();data.Flush(true);validity.Flush(true);gaps.Flush();segments.Flush();}
            finally{data.Dispose();validity.Dispose();gaps.Dispose();segments.Dispose();}
            try{WritePcTime();}
            catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is UnauthorizedAccessException||ex is ArgumentException||
                ex is InvalidOperationException||ex is FormatException||ex is OverflowException){
                System.Diagnostics.Trace.WriteLine("Optional IMU PC time: "+ex.Message);
            }
        }
    }
}
