using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Layout-time association only. Initial attachment requires the actual
    // successful writer return and its strict, immutable source context.
    // Cached IMU status flags are never read; native conversion/ADC phase and
    // value integrity are not inferred from the derived software row clock.
    internal static class NpDerivedImuGridUtc {
        internal const string RowId="imu.derived_grid_1250",SealKey="derived_imu_grid_layout";
        const string Data="analogin.dat",Validity="analogin_validity.dat",Format="neuropixels.committed_derived_imu_grid";
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("Derived IMU grid UTC: "+why);}
        static object Get(IDictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?v:null;}
        static bool Flag(IDictionary<string,object> d,string key){return Get(d,key) is bool&&(bool)d[key];}
        static string Text(IDictionary<string,object> d,string key){return Convert.ToString(Get(d,key),Inv);}
        static decimal Number(IDictionary<string,object> d,string key){Need(Get(d,key)!=null,"missing "+key);return Convert.ToDecimal(d[key],Inv);}
        static List<Dictionary<string,object>> Rows(Dictionary<string,object> d){
            object value=Get(d,"streams");Need(value!=null,"canonical stream list is missing");
            var typed=value as IEnumerable<Dictionary<string,object>>;
            return typed!=null?typed.Select(row=>new Dictionary<string,object>(row)).ToList():
                new JavaScriptSerializer().ConvertToType<Dictionary<string,object>[]>(value).Select(row=>new Dictionary<string,object>(row)).ToList();
        }
        static bool Derived(Dictionary<string,object> row){return Text(row,"id")==RowId||Text(row,"stream")=="derived_imu_grid";}
        static void Drop(Dictionary<string,object> report){
            report.Remove(SealKey);
            if(Get(report,"streams")!=null){var rows=Rows(report);rows.RemoveAll(Derived);report["streams"]=rows;}
        }
        static void CanonicalBinding(Dictionary<string,object> report,NpSensorFineTime clock){
            Need(report!=null&&clock!=null&&clock.Continuous64,"strict continuous source context required");
            Need(Flag(report,"np_start_byte_sync_confirmed")&&Text(report,"reference")=="lowest_selected_probe_first_validated_start_byte"&&
                Text(report,"reference_quality")=="producer_validated_start_byte","canonical NP STARTBYTE reference is unavailable");
            string mode=clock.IsUsb?"usb":"sd",other=clock.IsUsb?"sd":"usb";
            Need(Flag(report,mode+"_bound_first_sync")&&!Flag(report,other+"_bound_first_sync"),"canonical transport/source binding differs");
            Need(String.Equals(Text(report,"device_identity"),clock.Uid,StringComparison.OrdinalIgnoreCase)&&Number(report,"logger_version")==clock.Family&&
                Number(report,"session_id")==clock.Session&&Number(report,"capture_generation")==clock.Generation&&Number(report,"source_epoch")==clock.SourceEpoch&&
                Number(report,"counter_domain")==clock.Domain&&Number(report,"counter_hz")==clock.CounterHz&&Number(report,"reference_source_hz")==clock.SourceHz&&
                Number(report,"reference_source_tick")==clock.ReferenceTick,"canonical identity/session/counter/reference differs");
            ulong token;string tokenText=Text(report,clock.IsUsb?"usb_expected_token_hex":"sd_bound_token_hex");
            Need(tokenText.Length==16&&UInt64.TryParse(tokenText,NumberStyles.AllowHexSpecifier,Inv,out token)&&token==clock.Token,"canonical token differs");
            uint crc;string crcText=Text(report,"first_sync_receipt_crc32");
            Need(crcText.Length==8&&UInt32.TryParse(crcText,NumberStyles.AllowHexSpecifier,Inv,out crc)&&crc==clock.ReceiptCrc,"canonical source receipt CRC differs");
        }
        static long ExpectedRows(NpSensorFineTime clock,ulong sourceEnd){
            Need(sourceEnd>clock.ReferenceTick,"verified NP extent does not contain the reference");
            ulong extent=sourceEnd-clock.ReferenceTick;
            return checked((long)(extent/clock.SourceHz*1250UL+((extent%clock.SourceHz)*1250UL+clock.SourceHz-1UL)/clock.SourceHz));
        }
        static byte Files(string folder,long rows){
            Need(rows>0,"committed grid has no rows");
            Need(new FileInfo(Path.Combine(folder,Data)).Length==checked(rows*32L)&&new FileInfo(Path.Combine(folder,Validity)).Length==rows,
                "committed DAT/validity file extent differs");
            using(var input=File.OpenRead(Path.Combine(folder,Validity))){int mask=input.ReadByte();Need(mask>=0&&(mask&~3)==0,"first-row validity is invalid");return (byte)mask;}
        }
        static Dictionary<string,object> Seal(NpSensorFineTime c,ulong end,long rows,byte mask){return new Dictionary<string,object> {
            {"schema",1},{"format",Format},{"transport",c.IsUsb?"usb":"sd"},{"uid",c.Uid},{"token_hex",c.Token.ToString("X16",Inv)},
            {"family",c.Family},{"session",c.Session},{"generation",c.Generation},{"source_epoch",c.SourceEpoch},{"domain",c.Domain},{"counter_hz",c.CounterHz},
            {"source_hz",c.SourceHz},{"reference_tick",c.ReferenceTick},{"receipt_crc32",c.ReceiptCrc.ToString("X8",Inv)},
            {"t0_counter_lower",c.T0CounterLower.ToString("G29",Inv)},{"t0_counter_upper",c.T0CounterUpper.ToString("G29",Inv)},{"verified_source_end",end},{"rows",rows},
            {"data_file",Data},{"data_bytes",checked(rows*32L)},{"validity_file",Validity},{"validity_bytes",rows},{"first_row_validity_mask",mask},
            {"scope","Committed software grid layout and NP reference association; no native conversion phase or payload-value integrity attestation"}
        };}
        static void Apply(Dictionary<string,object> report,Dictionary<string,object> seal,NpSensorFineTime clock,byte mask){
            string reference=Text(report,"clock_reference");
            bool utc=Flag(report,"utc_valid")&&Flag(report,"reference_clock_mapping_qualified")&&
                (reference=="pc_wall_clock"||reference=="device_hardware_rtc"&&Flag(report,"rtc_local_mapping_qualified"));
            if(utc)Need(Get(report,"utc_lower_us")!=null&&Get(report,"utc_upper_us")!=null&&Number(report,"utc_lower_us")>=(reference=="device_hardware_rtc"?946684800000000m:1577836800000000m)&&
                Number(report,"utc_upper_us")<=4102444800000000m&&Number(report,"utc_lower_us")<=Number(report,"utc_upper_us"),"canonical UTC interval is invalid");
            var rows=Rows(report);rows.RemoveAll(Derived);rows.Add(new Dictionary<string,object> {
                {"id",RowId},{"stream","derived_imu_grid"},{"probe",null},{"band",null},{"data_file",Data},{"data_present",true},{"sample_index",0L},
                {"sample_rate_numerator",1250U},{"sample_rate_denominator",1U},{"source_tick",0m},{"source_hz",1250UL},{"source_clock","analogin.derived_1250hz_grid"},
                {"offset_source_ticks",0m},{"offset_from_recording_start_us",0m},{"offset_source_hz",clock.SourceHz},{"offset_source_clock",NpFirstSyncUtc.SourceClock},
                {"utc_valid",utc},{"first_sample_utc_lower_us",utc?Get(report,"utc_lower_us"):null},{"first_sample_utc_upper_us",utc?Get(report,"utc_upper_us"):null},
                {"clock_mapping",utc?"Committed derived row0 equals the qualified NP STARTBYTE reference interval":null},{"clock_reference",Get(report,"clock_reference")},
                {"software_derived_grid",true},{"grid_row0_np_reference_locked",true},{"first_row_sensor_validity_mask",mask},
                {"native_sensor_conversion_phase_locked",null},{"native_sensor_conversion_phase_known",false},
                {"sampling_semantics","800 us software rows; held or unavailable sensor values; row0 time is NP STARTBYTE, not a native acquisition/exposure event"},
                {"unknown_words","ADC/GPIO and other non-sensor words have no inferred NP phase"}
            });
            report["streams"]=rows;report[SealKey]=seal;report.Remove("derived_imu_grid_error");
        }
        static bool Failure(Exception e){return e is IOException||e is UnauthorizedAccessException||e is InvalidDataException||e is FormatException||e is OverflowException||e is ArgumentException||e is InvalidCastException||e is InvalidOperationException||e is KeyNotFoundException;}
        // The caller invokes this only after the common writer's successful
        // Commit, and atomically saves the returned report if true. The writer
        // result is never modified, including on optional metadata failure.
        internal static bool AppendAfterCommit(string folder,Dictionary<string,object> report,NpSdFineAnalogResult actual,NpSensorFineTime clock,ulong verifiedSourceEnd,out string error){
            error=null;
            try{
                Need(actual!=null&&actual.Applied,"actual successful grid writer return required");CanonicalBinding(report,clock);
                long rows=ExpectedRows(clock,verifiedSourceEnd);Need(actual.Rows==rows,"writer rows differ from verified NP extent/reference");
                byte mask=Files(folder,rows);Apply(report,Seal(clock,verifiedSourceEnd,rows,mask),clock,mask);return true;
            }catch(Exception ex) when(Failure(ex)){
                error=ex.Message;if(report!=null){try{Drop(report);}catch(Exception cleanup) when(Failure(cleanup)){}report["derived_imu_grid_error"]=error;}return false;
            }
        }
        // A previous canonical layout seal is useful only with freshly
        // revalidated source evidence and unchanged file shape/row0 validity.
        // It is not a cached Applied/Qualified flag or a native-sample receipt.
        internal static bool Refresh(string folder,Dictionary<string,object> previous,Dictionary<string,object> newReport,NpSensorFineTime freshClock,out string error){
            error=null;
            try{
                if(previous==null||Get(previous,SealKey)==null){
                    if(newReport!=null&&(Get(newReport,SealKey)!=null||Get(newReport,"streams")!=null&&Rows(newReport).Any(Derived)))
                        throw new InvalidDataException("No previous committed derived grid layout seal.");
                    return false;
                }
                var seal=new JavaScriptSerializer().ConvertToType<Dictionary<string,object>>(previous[SealKey]);
                Need(seal.Count<=32&&Number(seal,"schema")==1&&Text(seal,"format")==Format,"unknown/unbounded grid layout seal");
                CanonicalBinding(previous,freshClock);CanonicalBinding(newReport,freshClock);
                Need(Text(seal,"transport")== (freshClock.IsUsb?"usb":"sd")&&Text(seal,"uid")==freshClock.Uid&&Text(seal,"token_hex")==freshClock.Token.ToString("X16",Inv)&&
                    Number(seal,"family")==freshClock.Family&&Number(seal,"session")==freshClock.Session&&Number(seal,"generation")==freshClock.Generation&&
                    Number(seal,"source_epoch")==freshClock.SourceEpoch&&Number(seal,"domain")==freshClock.Domain&&Number(seal,"counter_hz")==freshClock.CounterHz&&
                    Number(seal,"source_hz")==freshClock.SourceHz&&Number(seal,"reference_tick")==freshClock.ReferenceTick&&Text(seal,"receipt_crc32")==freshClock.ReceiptCrc.ToString("X8",Inv)&&
                    Number(seal,"t0_counter_lower")==freshClock.T0CounterLower&&Number(seal,"t0_counter_upper")==freshClock.T0CounterUpper,"grid layout/source root changed");
                ulong end=checked((ulong)Number(seal,"verified_source_end"));long rows=ExpectedRows(freshClock,end);
                Need(Number(seal,"rows")==rows&&Text(seal,"data_file")==Data&&Number(seal,"data_bytes")==checked(rows*32L)&&
                    Text(seal,"validity_file")==Validity&&Number(seal,"validity_bytes")==rows,"grid layout geometry changed");
                byte mask=Files(folder,rows);Need(Number(seal,"first_row_validity_mask")==mask,"grid row0 validity changed");Apply(newReport,seal,freshClock,mask);return true;
            }catch(Exception ex) when(Failure(ex)){
                error=ex.Message;if(newReport!=null){try{Drop(newReport);}catch(Exception cleanup) when(Failure(cleanup)){}newReport["derived_imu_grid_error"]=error;}return false;
            }
        }
    }
}
