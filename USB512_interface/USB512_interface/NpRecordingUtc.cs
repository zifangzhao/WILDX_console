using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // One user-facing start-time convention. Old sectors and timing journals
    // remain evidence adapters; neither an ARM stamp nor a Start click is a
    // substitute for a source position. Legacy observations remain estimates;
    // an explicit producer origin requires a separately validated adapter.
    internal static class NpRecordingUtc
    {
        internal const string FileName="recording_start_utc.json";
        internal static bool DeviceRtcRefreshSupported {
            get {
#if NP_FIRST_SYNC_ENABLE
                return true;
#else
                return false;
#endif
            }
        }
        internal sealed class StreamStart
        {
            internal string Id,Kind,Band,DataFile,Clock;
            internal int? Probe;
            internal long SampleIndex;
            internal decimal? SourceTick,UtcLowerUs,UtcUpperUs;
            internal ulong SourceHz;
            internal uint RateNumerator,RateDenominator=1;
            internal bool DataPresent;
            internal byte AuxId=0;
            internal ulong? FirstSyncCounter=null;
        }
        static object Get(IDictionary<string,object> row,string key)
        {object value;return row.TryGetValue(key,out value)?value:null;}
        static decimal? Decimal(object value)
        {return value==null?(decimal?)null:Convert.ToDecimal(value,CultureInfo.InvariantCulture);}
        static bool ValidUtc(StreamStart point,decimal minimumUtcUs=1577836800000000m)
        {return point!=null&&point.UtcLowerUs.HasValue&&point.UtcUpperUs.HasValue&&
            point.UtcLowerUs.Value>=minimumUtcUs&&point.UtcUpperUs.Value<=4102444800000000m&&
            point.UtcLowerUs.Value<=point.UtcUpperUs.Value;}
        static string Iso(decimal? microseconds)
        {return microseconds.HasValue?new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)
            .AddTicks(checked((long)microseconds.Value*10)).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",CultureInfo.InvariantCulture):null;}
        internal static Dictionary<string,object> Build(bool npEnabled,IEnumerable<StreamStart> observations,
            int loggerVersion,string identity,uint? session,uint? generation,string evidence,StreamStart sourceOrigin=null,decimal minimumUtcUs=1577836800000000m)
        {
            var streams=observations.ToList();
            var candidates=streams.Where(s=>s.DataPresent&&s.SourceTick.HasValue&&s.SourceTick.Value>=0&&s.SourceHz!=0&&
                (!npEnabled||s.Kind=="neuropixels"&&s.Band=="ap")).ToList();
            // Never compare numeric ticks from different clocks, or choose the
            // best UTC estimate instead of the first actual source position.
            bool common=candidates.Count>0&&!String.IsNullOrEmpty(candidates[0].Clock)&&candidates.All(s=>s.Clock==candidates[0].Clock&&s.SourceHz==candidates[0].SourceHz);
            var reference=sourceOrigin==null?(common?candidates.OrderBy(s=>s.SourceTick.Value).ThenBy(s=>s.Probe??0).First():null):
                npEnabled&&sourceOrigin.Kind=="neuropixels"&&sourceOrigin.SourceTick.HasValue&&sourceOrigin.SourceTick.Value>=0&&
                sourceOrigin.SourceHz!=0&&!String.IsNullOrEmpty(sourceOrigin.Clock)?sourceOrigin:null;
            bool valid=ValidUtc(reference,minimumUtcUs);
            decimal? low=valid?(decimal?)Math.Floor(reference.UtcLowerUs.Value):null;
            decimal? high=valid?(decimal?)Math.Ceiling(reference.UtcUpperUs.Value):null;
            decimal? midpoint=valid?(decimal?)Math.Floor((low.Value+high.Value)/2m):null;
            var output=new List<Dictionary<string,object>>();
            foreach(var stream in streams){
                bool same=reference!=null&&stream.SourceTick.HasValue&&stream.SourceHz==reference.SourceHz&&stream.Clock==reference.Clock;
                decimal? offset=same?(decimal?)(stream.SourceTick.Value-reference.SourceTick.Value):null;
                bool clockValid=ValidUtc(stream,minimumUtcUs);
                output.Add(new Dictionary<string,object>{
                    {"id",stream.Id},{"stream",stream.Kind},{"probe",stream.Probe},{"band",stream.Band},
                    {"data_file",stream.DataFile},{"data_present",stream.DataPresent},{"sample_index",stream.DataPresent?(object)stream.SampleIndex:null},
                    {"sample_rate_numerator",stream.RateNumerator==0?(object)null:stream.RateNumerator},
                    {"sample_rate_denominator",stream.RateNumerator==0?(object)null:stream.RateDenominator},
                    {"source_tick",stream.SourceTick},{"source_hz",stream.SourceHz==0?(object)null:stream.SourceHz},
                    {"source_clock",stream.Clock},{"offset_source_ticks",offset},
                    {"offset_from_recording_start_us",same?(object)(offset.Value*1000000m/stream.SourceHz):null},
                    {"utc_valid",clockValid},{"first_sample_utc_lower_us",clockValid?(object)Math.Floor(stream.UtcLowerUs.Value):null},
                    {"first_sample_utc_upper_us",clockValid?(object)Math.Ceiling(stream.UtcUpperUs.Value):null}
                });
            }
            return new Dictionary<string,object>{
                {"schema",1},{"format","neuropixels.recording_start_utc"},{"logger_version",loggerVersion},
                {"device_identity",identity},{"session_id",session},{"capture_generation",generation},
                {"reference",reference==null?null:sourceOrigin!=null?"lowest_selected_probe_first_validated_start_byte":npEnabled?"first_decoded_synced_neuropixels_ap_row":"first_evidenced_enabled_stream_sample"},
                {"reference_stream",reference==null?null:reference.Id},{"reference_probe",reference==null?null:reference.Probe},
                {"reference_data_file",reference==null||!reference.DataPresent?null:reference.DataFile},
                {"reference_sample_index",reference==null||!reference.DataPresent?(object)null:reference.SampleIndex},
                {"reference_source_tick",reference==null?null:reference.SourceTick},
                {"reference_source_hz",reference==null?(object)null:reference.SourceHz},
                {"reference_quality",reference==null?"unavailable":sourceOrigin!=null?"producer_validated_start_byte":npEnabled?"decoded_first_row_estimate":"media_sample_timestamp_estimate"},
                {"utc_valid",valid},{"recording_start_utc",Iso(midpoint)},{"recording_start_unix_us",midpoint},
                {"utc_lower_us",low},{"utc_upper_us",high},
                {"uncertainty_us",valid?(object)Math.Ceiling((high.Value-low.Value)/2m):null},
                {"clock_reference","pc_wall_clock"},{"network_correction_applied",false},
                {"np_sync_release_confirmed",null},{"hardware_start_locked",null},{"physical_adc_time_calibrated",false},
                {"evidence",evidence},{"streams",output},
                {"unavailable_reason",valid?null:reference==null?"No common-clock first sample is evidenced.":"No qualified first-sample UTC mapping; ARM, Start click and USB arrival are not substituted."},
                {"scope","Digital sample/frame timestamp estimate. Relative offsets use the declared nominal device clock; hardware simultaneous release and physical ADC/filter latency are not attested."}
            };
        }
        internal static List<StreamStart> StartsFromNpRows(IEnumerable<Dictionary<string,object>> rows)
        {
            var starts=new List<StreamStart>();
            foreach(var row in rows){
                string band=Convert.ToString(Get(row,"band"),CultureInfo.InvariantCulture);
                int probe=Convert.ToInt32(Get(row,"probe"),CultureInfo.InvariantCulture);
                bool pc=Get(row,"pc_time_valid") is bool&&(bool)Get(row,"pc_time_valid");
                starts.Add(new StreamStart{Id="probe"+probe+"."+band,Kind="neuropixels",Band=band,Probe=probe,
                    DataFile=Get(row,"data_file")==null?null:Convert.ToString(Get(row,"data_file"),CultureInfo.InvariantCulture),SampleIndex=0,
                    SourceTick=Decimal(Get(row,"decoder_first_row_source_tick")),
                    SourceHz=Get(row,"source_hz")==null?0:Convert.ToUInt64(Get(row,"source_hz"),CultureInfo.InvariantCulture),
                    RateNumerator=Get(row,"rate_numerator")==null?0:Convert.ToUInt32(Get(row,"rate_numerator"),CultureInfo.InvariantCulture),
                    RateDenominator=Get(row,"rate_denominator")==null?1:Convert.ToUInt32(Get(row,"rate_denominator"),CultureInfo.InvariantCulture),
                    Clock="neuropixels.source_ticks",DataPresent=Convert.ToInt64(Get(row,"dat_rows"),CultureInfo.InvariantCulture)>0,
                    UtcLowerUs=pc?Decimal(Get(row,"pc_us_lower")):null,UtcUpperUs=pc?Decimal(Get(row,"pc_us_upper")):null});
            }
            return starts;
        }
        internal static Dictionary<string,object> FromNpRows(IEnumerable<Dictionary<string,object>> rows,
            int loggerVersion,string identity,uint? session,uint? generation,string evidence,IEnumerable<StreamStart> additionalStreams=null)
        {
            var starts=StartsFromNpRows(rows);
            if(additionalStreams!=null)starts.AddRange(additionalStreams);
            return Build(true,starts,loggerVersion,identity,session,generation,evidence);
        }
        internal static void Write(string folder,Dictionary<string,object> document)
        {
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,FileName);
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".partial";
            try{
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(document)+"\n",new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static void InvalidateFirstSync(Dictionary<string,object> document,string error)
        {
            document["utc_valid"]=false;foreach(string key in new[]{"recording_start_utc","recording_start_unix_us","utc_lower_us","utc_upper_us","uncertainty_us",
                "reference_clock_evidence","reference_clock_bounds_kind","reference_source_precision_us","auxiliary_pre_origin_exclusion_confirmed","auxiliary_release_confirmed",
                "hardware_start_locked","first_sync_probe_positions","first_sync_auxiliary_positions"})document[key]=null;
            document["np_start_byte_sync_confirmed"]=false;document["reference_clock_mapping_qualified"]=false;document["receipt_source_clock_mapping_flag"]=false;
            document["reference_quality"]="archived_start_byte_unverified";document["unavailable_reason"]=document["clock_mapping_error"]=error;
            document["hardware_start_lock_scope"]="Unverified archive; no current sample-lock or DAT association is attested.";
            document["scope"]="Archived STARTBYTE numbers are retained for diagnosis only. Current bound evidence failed validation; UTC, sync/sample lock and DAT associations are unavailable. No AP-row fallback is substituted.";
            // Optional RTC documents carry a second, device-local accuracy
            // claim. A failed raw seal/binding invalidates that claim too.
            // Existing legacy documents gain no RTC fields here.
            foreach(string key in new[]{"rtc_local_mapping_qualified","local_sub_ms_verified","rtc_body_identity_context_valid"})if(document.ContainsKey(key))document[key]=false;
            foreach(string key in new[]{"local_interval_width_us","local_interval_half_width_us"})if(document.ContainsKey(key))document[key]=null;
            if(document.ContainsKey("rtc_map_reason"))document["rtc_map_reason"]="unverified_archive";
            if(document.ContainsKey("rtc_evidence_error"))document["rtc_evidence_error"]=error;
            var streams=new JavaScriptSerializer().ConvertToType<Dictionary<string,object>[]>(document["streams"]);
            foreach(var stream in streams){
                stream["utc_valid"]=false;foreach(string key in new[]{"first_sample_utc_lower_us","first_sample_utc_upper_us","offset_source_ticks","offset_from_recording_start_us",
                    "offset_source_hz","offset_source_clock","clock_mapping","data_file","sample_index"})stream[key]=null;
                stream["data_present"]=false;foreach(string key in stream.Keys.Where(k=>k.StartsWith("receipt_",StringComparison.Ordinal)).ToArray())stream[key]=null;
            }
            document["streams"]=streams;
        }
        internal static List<Dictionary<string,object>> SafeFirstSyncLayout(Dictionary<string,object> previous,IEnumerable<Dictionary<string,object>> rows)
        {
            var old=new JavaScriptSerializer().ConvertToType<Dictionary<string,object>[]>(previous["streams"]);var safe=new List<Dictionary<string,object>>();
            foreach(var row in rows){
                var copy=new Dictionary<string,object>(row);string id="probe"+Convert.ToInt32(row["probe"])+"."+Convert.ToString(row["band"]);
                var prior=old.SingleOrDefault(s=>Convert.ToString(s["id"])==id);
                if(prior==null||Convert.ToString(prior["source_tick"],CultureInfo.InvariantCulture)!=Convert.ToString(row["decoder_first_row_source_tick"],CultureInfo.InvariantCulture)||
                    Convert.ToUInt64(prior["source_hz"])!=Convert.ToUInt64(row["source_hz"])){
                    copy["decoder_first_row_source_tick"]=null;copy["pc_time_valid"]=false;
                }
                safe.Add(copy);
            }
            return safe;
        }
        // Used after DAT layout changes. It adapts existing validated report
        // rows; it does not promote legacy ARM evidence to NP first-row UTC.
        internal static void RefreshFromFirstSampleReport(string folder)
        {
            var json=new JavaScriptSerializer();string path=Path.Combine(folder,"first_sample_timestamps.json");
            if(!File.Exists(path))return;
            var report=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
            object rows;if(!report.TryGetValue("rows",out rows))return;
            object value;uint? session=report.TryGetValue("session_id",out value)&&value!=null?(uint?)Convert.ToUInt32(value):null;
            uint? generation=null;
            if(report.TryGetValue("anchor",out value)&&value!=null){var anchor=json.ConvertToType<Dictionary<string,object>>(value);object g;if(anchor.TryGetValue("generation",out g)&&g!=null)generation=Convert.ToUInt32(g);}
            string identity=report.TryGetValue("device_identity",out value)?Convert.ToString(value):null;
#if NP_FIRST_SYNC_ENABLE
            if(NpFirstSyncSdUtc.Refresh(folder,json.ConvertToType<Dictionary<string,object>[]>(rows),Convert.ToInt32(report["logger_version"]),session))return;
            if(NpFirstSyncUsbUtc.Refresh(folder,json.ConvertToType<Dictionary<string,object>[]>(rows),Convert.ToInt32(report["logger_version"]),identity,generation))return;
#endif
            Write(folder,FromNpRows(json.ConvertToType<Dictionary<string,object>[]>(rows),Convert.ToInt32(report["logger_version"]),identity,session,generation,"first_sample_timestamps.json"));
        }
    }
}
