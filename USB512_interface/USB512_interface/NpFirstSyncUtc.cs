using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace USB512_interface
{
    // Populated only after the owning USB/SD path validates its outer device
    // identity and complete active capture binding. The 328B body has no UID.
    internal sealed class NpFirstSyncUtcContext
    {
        internal string DeviceIdentity,Evidence;
        internal NpFirstSyncBinding Binding;
        internal bool IdentityAndBindingValidated;
    }
    internal enum NpFirstSyncUtcBoundsKind { Cycle,RecordedUtc2Milliseconds,HardwareRtc }
    // A separately qualified source-clock-to-PC interval at ReferenceTick.
    // The coarse kind is only for a strictly validated UTC2 source-origin
    // adapter, never UTC1/ARM, discovery/release or an arrival timestamp.
    internal sealed class NpFirstSyncUtcBounds
    {
        internal string DeviceIdentity,Evidence;
        internal NpFirstSyncBinding Binding;
        internal uint SourceHz;
        internal ulong ReferenceTick;
        internal decimal LowerUs,UpperUs;
        internal string HostEpoch=null;
        internal bool SourceClockQualified;
        internal NpFirstSyncUtcBoundsKind Kind=NpFirstSyncUtcBoundsKind.Cycle;
        internal uint SourcePrecisionUs=0;
    }
    internal static class NpFirstSyncUtc
    {
        internal const string SourceClock="neuropixels.source_ticks";
        static void Require(bool value,string message){if(!value)throw new InvalidDataException("NP first-sync UTC: "+message);}
        static bool Identity(string value)
        {
            if(value==null||value.Length!=24||value==new string('0',24)||String.Equals(value,new string('F',24),StringComparison.OrdinalIgnoreCase))return false;
            uint word;for(int i=0;i<3;i++)if(!UInt32.TryParse(value.Substring(i*8,8),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out word))return false;
            return true;
        }
        static bool SameIdentity(string a,string b){return Identity(a)&&Identity(b)&&String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
        static bool ValidBounds(decimal? lo,decimal? hi,bool hardwareRtc=false)
        {return lo.HasValue&&hi.HasValue&&lo.Value>=(hardwareRtc?946684800000000m:1577836800000000m)&&hi.Value<=4102444800000000m&&lo.Value<=hi.Value;}
        static string ClockError(NpFirstSyncReceipt r,string identity,NpFirstSyncUtcBounds bounds)
        {
            if((r.Flags&NpFirstSyncWire.SourceClock)==0)return "Producer has not qualified the final STARTBYTE/source-clock coordinate mapping.";
            if(bounds==null)return "No separately qualified PC bounds at the validated STARTBYTE reference.";
            if(!bounds.SourceClockQualified)return "Source-clock mapping is not qualified by both receipt and clock evidence.";
            if(!SameIdentity(identity,bounds.DeviceIdentity)||!NpFirstSyncWire.GateMatch(r,bounds.Binding)||
                bounds.SourceHz!=r.SourceHz||bounds.ReferenceTick!=r.ReferenceTick)return "Clock identity/generation/source epoch/domain/rate/reference mismatch.";
            if(String.IsNullOrWhiteSpace(bounds.Evidence)||!ValidBounds(bounds.LowerUs,bounds.UpperUs,bounds.Kind==NpFirstSyncUtcBoundsKind.HardwareRtc))
                return bounds.Kind==NpFirstSyncUtcBoundsKind.HardwareRtc?"Hardware RTC evidence or interval is invalid.":"Clock evidence or PC interval is invalid.";
            if(bounds.Kind==NpFirstSyncUtcBoundsKind.Cycle){
                if(r.Domain==0||r.CounterHz==0)return "Cycle bounds require a qualified counter domain/frequency.";
            }else if(bounds.Kind==NpFirstSyncUtcBoundsKind.RecordedUtc2Milliseconds){
                if(bounds.SourcePrecisionUs<1000||bounds.UpperUs-bounds.LowerUs<bounds.SourcePrecisionUs)
                    return "UTC2 millisecond source bounds must retain their quantified precision and at least 1 ms width.";
            }else if(bounds.Kind==NpFirstSyncUtcBoundsKind.HardwareRtc){
                if(r.Domain==0||r.CounterHz==0||bounds.SourcePrecisionUs!=100||bounds.UpperUs-bounds.LowerUs<100)
                    return "Hardware RTC bounds require the source counter domain and retained 100 us RTC quantum.";
            }else return "Unknown source-clock bounds kind.";
            return null;
        }
        static NpRecordingUtc.StreamStart Copy(NpRecordingUtc.StreamStart s)
        {
            return new NpRecordingUtc.StreamStart {Id=s.Id,Kind=s.Kind,Band=s.Band,DataFile=s.DataFile,Clock=s.Clock,Probe=s.Probe,
                SampleIndex=s.SampleIndex,SourceTick=s.SourceTick,SourceHz=s.SourceHz,RateNumerator=s.RateNumerator,RateDenominator=s.RateDenominator,
                DataPresent=s.DataPresent,AuxId=s.AuxId,FirstSyncCounter=s.FirstSyncCounter};
        }
        static void Map(NpRecordingUtc.StreamStart copy,NpRecordingUtc.StreamStart original,decimal offset,NpFirstSyncReceipt receipt,NpFirstSyncUtcBounds clock)
        {
            decimal delta=offset*1000000m/receipt.SourceHz;
            decimal low=clock.LowerUs+delta,high=clock.UpperUs+delta;
            // Existing wider observations remain evidence. Never intersect
            // intervals or turn an additional estimate into narrower bounds.
            if(clock.Kind!=NpFirstSyncUtcBoundsKind.HardwareRtc&&ValidBounds(original.UtcLowerUs,original.UtcUpperUs)){low=Math.Min(low,original.UtcLowerUs.Value);high=Math.Max(high,original.UtcUpperUs.Value);}
            copy.UtcLowerUs=Math.Floor(low);copy.UtcUpperUs=Math.Ceiling(high);
        }
        internal static Dictionary<string,object> Build(byte[] raw,NpFirstSyncBinding expected,string deviceIdentity,
            NpFirstSyncUtcContext context,IEnumerable<NpRecordingUtc.StreamStart> observations,NpFirstSyncUtcBounds clock=null)
        {
            var receipt=NpFirstSyncWire.Parse(raw);
            Require(NpFirstSyncWire.GateMatch(receipt,expected),"receipt does not match the active final capture binding");
            Require(context!=null&&context.IdentityAndBindingValidated&&SameIdentity(deviceIdentity,context.DeviceIdentity)&&
                !String.IsNullOrWhiteSpace(context.Evidence)&&NpFirstSyncWire.GateMatch(receipt,context.Binding),"validated outer identity/binding context required");
            Require(observations!=null,"observed stream starts required");
            var observed=observations.ToList();Require(observed.All(s=>s!=null),"null observed stream");
            Require(observed.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()==observed.Count,"duplicate observed stream ID");
            string clockError=ClockError(receipt,deviceIdentity,clock);bool mapped=clockError==null;
            var origin=new NpRecordingUtc.StreamStart {Id="probe"+receipt.Reference+".start_byte",Kind="neuropixels",Band="start_byte",Probe=receipt.Reference,
                Clock=SourceClock,SourceHz=receipt.SourceHz,SourceTick=receipt.ReferenceTick,DataPresent=false};
            if(mapped){origin.UtcLowerUs=clock.LowerUs;origin.UtcUpperUs=clock.UpperUs;}
            var starts=new List<NpRecordingUtc.StreamStart>();
            var offsets=new List<decimal?>();var auxSources=new List<NpFirstSyncAux>();var matchedCounters=new List<bool>();
            foreach(var actual in observed){
                var copy=Copy(actual);decimal? offset=null;NpFirstSyncAux aux=null;bool counterMatch=false;
                if(actual.AuxId!=0){
                    Require(actual.AuxId<=4&&(receipt.AuxMask&(1<<(actual.AuxId-1)))!=0,"observed auxiliary ID is not enabled in the receipt");
                    aux=receipt.Aux[actual.AuxId-1];counterMatch=actual.FirstSyncCounter.HasValue&&(aux.Flags&4)!=0&&actual.FirstSyncCounter.Value==aux.Counter;
                    if(counterMatch&&(aux.Flags&8)!=0)offset=aux.OffsetNumerator/(decimal)aux.OffsetDenominator;
                }else if(actual.Kind=="neuropixels"&&actual.Probe.HasValue){
                    int p=actual.Probe.Value;
                    Require(p>=1&&p<=3&&(receipt.Mask&(1<<(p-1)))!=0,"observed NP row belongs to an unselected probe");
                    if(actual.SourceTick.HasValue&&actual.Clock==SourceClock&&actual.SourceHz==receipt.SourceHz){
                        Require(actual.SourceTick.Value>=receipt.Probes[p-1].FirstTick,"observed NP row precedes its validated STARTBYTE");
                        offset=actual.SourceTick.Value-receipt.ReferenceTick;
                    }
                }
                if(mapped&&offset.HasValue&&(clock.Kind!=NpFirstSyncUtcBoundsKind.HardwareRtc||actual.DataPresent))Map(copy,actual,offset.Value,receipt,clock);
                starts.Add(copy);offsets.Add(offset);auxSources.Add(aux);matchedCounters.Add(counterMatch);
            }
            var report=NpRecordingUtc.Build(true,starts,(int)receipt.Family,deviceIdentity.ToUpperInvariant(),receipt.Session,receipt.Generation,
                context.Evidence+"; strict NPSTART1 receipt",origin,clock!=null&&clock.Kind==NpFirstSyncUtcBoundsKind.HardwareRtc?946684800000000m:1577836800000000m);
            var rows=(IList<Dictionary<string,object>>)report["streams"];
            for(int i=0;i<rows.Count;i++){
                var row=rows[i];var aux=auxSources[i];decimal? offset=offsets[i];
                // Native source_tick/source_hz/source_clock remain untouched.
                // These offsets explicitly use the NP source clock instead.
                row["offset_source_ticks"]=offset;row["offset_from_recording_start_us"]=offset.HasValue?(object)(offset.Value*1000000m/receipt.SourceHz):null;
                row["offset_source_hz"]=offset.HasValue?(object)receipt.SourceHz:null;row["offset_source_clock"]=offset.HasValue?SourceClock:null;
                if(aux!=null){
                    row["first_sync_auxiliary_id"]=observed[i].AuxId;row["observed_first_sync_counter"]=observed[i].FirstSyncCounter;
                    row["receipt_first_source_counter_matches_observed"]=matchedCounters[i];
                    row["receipt_first_source_offset_numerator"]=(aux.Flags&8)!=0?(object)aux.OffsetNumerator:null;
                    row["receipt_first_source_offset_denominator"]=(aux.Flags&8)!=0?(object)aux.OffsetDenominator:null;
                    row["receipt_sample_lock_attested"]=aux.SampleLocked;
                    row["receipt_auxiliary_release_confirmed"]=(aux.Flags&2)!=0;
                    row["receipt_pre_origin_exclusion_confirmed"]=(aux.Flags&32)!=0;
                    row["receipt_first_source_stamp_kind"]=aux.StampKind==1?"source_capture":aux.StampKind==2?"foreground_acceptance":"unavailable";
                }
                row["clock_mapping"]=mapped&&offset.HasValue&&(clock.Kind!=NpFirstSyncUtcBoundsKind.HardwareRtc||observed[i].DataPresent)?
                    clock.Kind==NpFirstSyncUtcBoundsKind.HardwareRtc?"qualified hardware RTC NP reference interval plus nominal source offset":"qualified NP reference interval plus nominal source offset":null;
            }
            var enabled=receipt.Aux.Where((a,i)=>(receipt.AuxMask&(1<<i))!=0).ToList();
            report["np_start_byte_sync_confirmed"]=true;
            report["auxiliary_pre_origin_exclusion_confirmed"]=enabled.Count==0?(object)null:(receipt.Flags&NpFirstSyncWire.AuxGated)!=0;
            report["auxiliary_release_confirmed"]=enabled.Count==0?(object)null:enabled.All(a=>(a.Flags&2)!=0);
            report["hardware_start_locked"]=enabled.Count==0?(object)null:enabled.All(a=>a.SampleLocked);
            report["hardware_start_lock_scope"]="Producer-attested first source points; an observed DAT row additionally requires its explicit native-counter match. Native conversion/filter phase is not attested.";
            report["native_imu_conversion_phase_locked"]=null;
            report["source_epoch"]=receipt.SourceEpoch;report["counter_domain"]=receipt.Domain;report["counter_hz"]=receipt.CounterHz;
            report["reference_clock_mapping_qualified"]=mapped;report["clock_mapping_error"]=clockError;
            report["receipt_source_clock_mapping_flag"]=(receipt.Flags&NpFirstSyncWire.SourceClock)!=0;
            report["reference_clock_evidence"]=mapped?clock.Evidence:null;
            report["pc_host_epoch"]=mapped&&clock.Kind!=NpFirstSyncUtcBoundsKind.HardwareRtc?clock.HostEpoch:null;
            report["reference_clock_bounds_kind"]=mapped?(clock.Kind==NpFirstSyncUtcBoundsKind.HardwareRtc?"device_hardware_rtc":clock.Kind==NpFirstSyncUtcBoundsKind.Cycle?"cycle":"recorded_UTC2_milliseconds"):null;
            if(clock!=null&&clock.Kind==NpFirstSyncUtcBoundsKind.HardwareRtc)report["clock_reference"]="device_hardware_rtc";
            report["reference_source_precision_us"]=mapped&&clock.SourcePrecisionUs!=0?(object)clock.SourcePrecisionUs:null;
            report["first_sync_receipt_crc32"]=BitConverter.ToUInt32(raw,324).ToString("X8",CultureInfo.InvariantCulture);
            report["first_sync_probe_positions"]=Enumerable.Range(0,3).Where(p=>(receipt.Mask&(1<<p))!=0).Select(p=>new {
                probe=p+1,first_start_byte_source_tick=receipt.Probes[p].FirstTick,phase=receipt.Probes[p].Phase,period=receipt.Probes[p].Period,
                native_counter=receipt.Family==1?(uint?)receipt.Probes[p].Counter:null}).ToArray();
            report["first_sync_auxiliary_positions"]=enabled.Select(a=>new {
                id=a.Id,native_counter=(a.Flags&4)!=0?(ulong?)a.Counter:null,native_counter_bits=(a.Flags&4)!=0?(byte?)a.CounterBits:null,
                offset_source_numerator=(a.Flags&8)!=0?(long?)a.OffsetNumerator:null,offset_source_denominator=(a.Flags&8)!=0?(uint?)a.OffsetDenominator:null,
                offset_source_hz=receipt.SourceHz,release_confirmed=(a.Flags&2)!=0,pre_origin_exclusion_confirmed=(a.Flags&32)!=0,
                producer_sample_lock_attested=a.SampleLocked,first_source_stamp_kind=a.StampKind,
                rate_numerator=a.RateNumerator==0?(uint?)null:a.RateNumerator,rate_denominator=a.RateNumerator==0?(uint?)null:a.RateDenominator}).ToArray();
            report["unavailable_reason"]=mapped?null:clockError;
            report["scope"]="T0 is the lowest selected probe's validated final-generation STARTBYTE. Discovery, release and USB arrival are not T0. Auxiliary exclusion/release and producer sample-lock flags are separate; native IMU conversion and physical ADC/filter phase remain unknown. Network correction is separate.";
            return report;
        }
    }
}
