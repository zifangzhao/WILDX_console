using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace USB512_interface {
    // Pure optional-evidence adapter. Every result is rebuilt from the raw
    // RTC/source bytes; a cached Qualified flag never establishes the clock.
    // No hardware, transport, PC-arrival estimator or network fit runs here.
    internal static class NpRtcRecordingUtc {
        static NpFirstSyncBinding Binding(NpFirstSyncReceipt r){return new NpFirstSyncBinding {Family=r.Family,Session=r.Session,Generation=r.Generation,
            SourceEpoch=r.SourceEpoch,Domain=r.Domain,CounterHz=r.CounterHz,ProfileCrc=r.ProfileCrc,Mask=r.Mask,ApHz=r.ApHz};}
        static byte[] Copy(byte[] raw){return raw==null?null:(byte[])raw.Clone();}
        static List<NpRecordingUtc.StreamStart> Observations(IEnumerable<NpRecordingUtc.StreamStart> observed){return observed==null?new List<NpRecordingUtc.StreamStart>():
            observed.Select(s=>s==null?null:new NpRecordingUtc.StreamStart {Id=s.Id,Kind=s.Kind,Band=s.Band,DataFile=s.DataFile,Clock=s.Clock,Probe=s.Probe,SampleIndex=s.SampleIndex,
                SourceTick=s.SourceTick,SourceHz=s.SourceHz,RateNumerator=s.RateNumerator,RateDenominator=s.RateDenominator,DataPresent=s.DataPresent,AuxId=s.AuxId,
                FirstSyncCounter=s.FirstSyncCounter,UtcLowerUs=s.UtcLowerUs,UtcUpperUs=s.UtcUpperUs}).ToList();}
        internal static Dictionary<string,object> FromSd(byte[] rtcSector,byte[] clockSector,byte[] companion,int family,uint session,uint apHz,string uid,ulong token,
            IEnumerable<NpRecordingUtc.StreamStart> observations){
            var observed=Observations(observations);
            rtcSector=Copy(rtcSector);clockSector=Copy(clockSector);companion=Copy(companion);
            try{
                var source=NpFirstSyncSd.ReadSector(clockSector,family,session,apHz,uid,token);
                if(source==null)return Unknown(observed,family,uid,session,"Sealed SD STARTBYTE source binding is absent.");
                NpRtcStartBinding rtcBinding=null;byte[] body=null;string error=null;
                try{
                    rtcBinding=NpRtcStartWire.BindSd(clockSector,companion,family,session,apHz,uid,token);
                    NpRtcStartWire.ParseSector(rtcSector,rtcBinding);body=rtcSector.Take(NpRtcStartWire.Bytes).ToArray();
                }catch(InvalidDataException ex){error=ex.Message;}
                return Build(source.Receipt.Raw,source.Uid,observed,body,rtcBinding,error,"strict sealed SD UID/token/session/profile STARTBYTE binding");
            }catch(InvalidDataException ex){return Unknown(observed,family,uid,session,ex.Message);}
            catch(OverflowException ex){return Unknown(observed,family,uid,session,ex.Message);}
        }
        // selectedFeatures15 is the explicit future RTC transport contract,
        // not an upgrade inferred from an old INFO7 or a small counter word.
        internal static Dictionary<string,object> FromUsb(byte[] rtcBody,byte[] receipt,byte[] anchor,int family,string uid,ulong token,uint selectedFeatures,
            IEnumerable<NpRecordingUtc.StreamStart> observations){
            var observed=Observations(observations);
            rtcBody=Copy(rtcBody);receipt=Copy(receipt);anchor=Copy(anchor);
            try{
                var binding=NpRtcStartWire.BindUsb(receipt,anchor,family,uid,token,selectedFeatures);
                return Build(binding.Receipt,binding.Uid,observed,rtcBody,binding,null,"strict explicitly selected RTC/continuous64 USB source binding");
            }catch(InvalidDataException ex){return Unknown(observed,family,uid,null,ex.Message);}
            catch(OverflowException ex){return Unknown(observed,family,uid,null,ex.Message);}
        }
        static Dictionary<string,object> Build(byte[] rawReceipt,string uid,List<NpRecordingUtc.StreamStart> observed,byte[] rawRtc,NpRtcStartBinding binding,string containerError,string evidence){
            var receipt=NpFirstSyncWire.Parse(rawReceipt);var expected=Binding(receipt);
            NpRtcStartBounds local=null;NpRtcStartStamp stamp=null;
            if(containerError==null&&binding!=null&&rawRtc!=null){
                // Map and Parse both recheck the original identity/context/CRC.
                local=NpRtcStartWire.Map(rawRtc,binding);
                try{stamp=NpRtcStartWire.Parse(rawRtc,binding);}catch(InvalidDataException){}
            }
            NpFirstSyncUtcBounds clock=null;
            if(local!=null&&local.Qualified)clock=new NpFirstSyncUtcBounds {Kind=NpFirstSyncUtcBoundsKind.HardwareRtc,DeviceIdentity=uid,Binding=Binding(receipt),
                SourceHz=receipt.SourceHz,ReferenceTick=receipt.ReferenceTick,LowerUs=local.LowerUs,UpperUs=local.UpperUs,SourceClockQualified=true,SourcePrecisionUs=100,
                Evidence="strict NPRTC001 hardware calendar/crossstamp and continuous source binding; 100 us RTC quantum with both fine-counter read intervals retained"};
            var context=new NpFirstSyncUtcContext {DeviceIdentity=uid,Binding=Binding(receipt),IdentityAndBindingValidated=true,Evidence=evidence};
            var report=NpFirstSyncUtc.Build(rawReceipt,expected,uid,context,observed,clock);
            string error=clock!=null?null:containerError??(local==null?"Optional hardware RTC body is absent.":local.Error??("Hardware RTC local map unavailable: "+local.Reason));
            Decorate(report,observed,local,stamp,error);
            return report;
        }
        static Dictionary<string,object> Unknown(List<NpRecordingUtc.StreamStart> observed,int family,string uid,uint? session,string error){
            var clean=new List<NpRecordingUtc.StreamStart>();
            foreach(var s in observed)if(s!=null)clean.Add(new NpRecordingUtc.StreamStart {Id=s.Id,Kind=s.Kind,Band=s.Band,DataFile=s.DataFile,Clock=s.Clock,Probe=s.Probe,
                SampleIndex=s.SampleIndex,SourceTick=s.SourceTick,SourceHz=s.SourceHz,RateNumerator=s.RateNumerator,RateDenominator=s.RateDenominator,DataPresent=s.DataPresent,
                AuxId=s.AuxId,FirstSyncCounter=s.FirstSyncCounter});
            // An explicit unavailable origin prevents the generic legacy AP
            // row estimator from becoming this failed RTC attempt's T0.
            var report=NpRecordingUtc.Build(true,clean,family,uid,session,null,"Unqualified optional RTC/source evidence",new NpRecordingUtc.StreamStart());
            report["np_start_byte_sync_confirmed"]=false;report["reference_clock_mapping_qualified"]=false;
            Decorate(report,observed,null,null,error);return report;
        }
        static void Decorate(Dictionary<string,object> report,List<NpRecordingUtc.StreamStart> observed,NpRtcStartBounds local,NpRtcStartStamp stamp,string error){
            bool qualified=local!=null&&local.Qualified&&(bool)report["utc_valid"];
            report["clock_reference"]="device_hardware_rtc";report["network_correction_applied"]=false;
            report["rtc_local_mapping_qualified"]=qualified;report["local_interval_width_us"]=qualified?(object)local.WidthUs:null;
            report["local_interval_half_width_us"]=qualified?(object)local.HalfWidthUs:null;report["local_sub_ms_verified"]=qualified&&local.SubMillisecond;
            report["rtc_map_reason"]=qualified?"Ok":local==null?"Unavailable":local.Reason.ToString();
            report["rtc_evidence_error"]=error;report["rtc_body_identity_context_valid"]=stamp!=null;
            report["rtc_calendar_scope"]="hardware RTC years2000..2099; upper interval endpoint may equal2100 boundary";
            report["rtc_hardware_evidence"]=stamp==null?null:new Dictionary<string,object> {
                {"flags",stamp.Flags},{"rtc_input_hz",stamp.InputHz},{"asynchronous_prescaler",stamp.Psca},{"synchronous_prescaler",stamp.Pscs},
                {"subsecond_register",stamp.Ssr},{"calr",stamp.Calr},{"counter_before64",stamp.Before64},{"counter_after64",stamp.After64}
            };
            bool pc=stamp!=null&&(stamp.Flags&NpRtcStartWire.PcCalibrated)!=0;
            report["pc_epoch_calibration"]=new Dictionary<string,object> {
                {"declared",pc},{"generation",pc?(object)stamp.CalibrationGeneration:null},{"declared_uncertainty_us",pc?(object)stamp.PcUncertaintyUs:null},
                {"included_in_local_interval",false},{"meaning","RTC epoch calibration evidence; independent of the local RTC-to-NP interval and network accuracy"}
            };
            report["pc_clock_observations"]=observed.Where(s=>s!=null&&(s.UtcLowerUs.HasValue||s.UtcUpperUs.HasValue)).Select(s=>new Dictionary<string,object> {
                {"id",s.Id},{"utc_lower_us",s.UtcLowerUs},{"utc_upper_us",s.UtcUpperUs},{"included_in_device_rtc_mapping",false}
            }).ToArray();
            report["pc_host_epoch"]=null;
            if(!qualified){report["clock_mapping_error"]=error;report["unavailable_reason"]=error;}
            report["scope"]="T0 is the lowest selected probe's validated first STARTBYTE mapped to the device hardware RTC. Local interval width includes RTC and source-counter quantization. PC epoch calibration/observations and network accuracy remain separate; physical ADC/filter and native sensor conversion phase are unknown.";
        }
    }
}
