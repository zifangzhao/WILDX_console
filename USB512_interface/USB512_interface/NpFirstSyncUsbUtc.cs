using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace USB512_interface
{
        // Post-recording evidence adapter only. Clock intervals come from the
        // existing acquisition mapper after explicit producer qualification.
    internal static class NpFirstSyncUsbUtc
    {
        const string StatusFile="first_sync_usb_status.json";
        const string ClockJournal="acquisition_clock_queries.jsonl";
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        static void Need(bool ok,string message){if(!ok)throw new InvalidDataException("First-sync USB UTC: "+message);}
        static bool Flag(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)&&value is bool&&(bool)value;}
        static string Hash(string path){if(!File.Exists(path))return null;using(var h=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(input)).Replace("-","");}
        static bool UidMatches(string identity,string uid){return identity!=null&&uid!=null&&uid.Length==24&&Regex.IsMatch(identity,"(?<![0-9A-Fa-f])"+Regex.Escape(uid)+"(?![0-9A-Fa-f])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);}
        static bool Declared(string folder)
        {
            // Once this recording declared v2, losing/removing its raw
            // declaration cannot silently turn it into a legacy recording.
            if(File.Exists(Path.Combine(folder,StatusFile)))return true;
            string journal=Path.Combine(folder,NpUsbTimingEvidence.FileName);
            if(File.Exists(journal)){
                using(var input=new StreamReader(journal)){
                    var line=new StringBuilder();int ch;while((ch=input.Read())!=-1&&ch!='\n'&&line.Length<=32768)line.Append((char)ch);
                    Need(line.Length<=32768,"oversized declaration metadata");
                    var meta=Json.Deserialize<Dictionary<string,object>>(line.ToString());if(meta.ContainsKey("first_sync_transport"))return true;
                }
            }
            if(File.Exists(Path.Combine(folder,NpFirstSyncUsbEvidence.ReceiptFile)))return true;
            string status=Path.Combine(folder,"usb_timing_status.json");if(!File.Exists(status))return false;
            Need(new FileInfo(status).Length<=65536,"oversized negotiation status");var d=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(status));
            object value;if(Flag(d,"first_sync_enabled"))return true;
            if(d.TryGetValue("first_sync_negotiation",out value)&&value!=null)return Flag(Json.ConvertToType<Dictionary<string,object>>(value),"attempted");
            return false;
        }
        static NpFirstSyncBinding Binding(NpFirstSyncReceipt r){return new NpFirstSyncBinding {Family=r.Family,Session=r.Session,Generation=r.Generation,
            SourceEpoch=r.SourceEpoch,Domain=r.Domain,CounterHz=r.CounterHz,ProfileCrc=r.ProfileCrc,Mask=r.Mask,ApHz=r.ApHz};}
        static NpFirstSyncUsbHistory Verify(string folder,string identity,int family,string epoch,NpAcquisitionAnchor anchor,NpUsbTimingHistory stream)
        {
            var h=NpFirstSyncUsbEvidence.Read(folder);Need(h!=null&&h.Qualified,h==null?"journal unavailable":h.Error??"receipt unavailable");
            var r=h.Receipt;
            bool nativeGeneration=false;
            if(stream!=null&&stream.Generation==0&&h.FineQualified&&h.FineRoot!=null){
                var root=h.FineRoot;var info=NpFirstSyncUsb.ParseInfo(root.Info,h.Uid,family);
                Need(info.HardwareRtc&&root.Uid==h.Uid&&root.Token==h.Token&&root.Generation==h.Generation&&root.Receipt.SequenceEqual(r.Raw),"feature15 final root context mismatch");
                NpFirstSyncWire.ValidateSourceAnchor(root.Anchor,r.Raw);
                var source=NpAcquisitionAnchor.Parse(root.Anchor,BitConverter.ToUInt32(root.Anchor,8),family);
                Need(anchor!=null&&anchor.Flags==source.Flags&&anchor.Generation==source.Generation&&anchor.CounterHz==source.CounterHz&&anchor.SourceHz==source.SourceHz&&anchor.SampleHz==source.SampleHz&&anchor.Origin==source.Origin&&
                    anchor.BeforeMs==source.BeforeMs&&anchor.AfterMs==source.AfterMs&&anchor.BeforeCycles==source.BeforeCycles&&anchor.AfterCycles==source.AfterCycles,"actual stopped anchor disagrees with strict feature15 source root");
                nativeGeneration=true;
            }
            Need(stream!=null&&stream.Family==family&&stream.Epoch==epoch&&UidMatches(identity,h.Uid)&&h.Uid==stream.Uid&&h.Token==stream.Token&&(h.Generation==stream.Generation||nativeGeneration),"current identity/host epoch/token/capture disagreement");
            Need(anchor!=null&&anchor.SourceValid&&anchor.Generation==h.Generation&&anchor.SampleHz==r.ApHz&&anchor.SourceHz==r.SourceHz,"current source anchor generation/rate unavailable or different");
            Need(r.Family==family&&r.Generation==h.Generation&&r.Session==h.Generation,"USB session32 must equal the actual final capture generation");
            return h;
        }
        static NpFirstSyncUtcBounds ClockBounds(NpFirstSyncUsbHistory h,NpAcquisitionAnchor anchor,
            Func<ulong,Dictionary<string,object>> mapper,out string error)
        {
            error=null;var r=h.Receipt;
            if((r.Flags&NpFirstSyncWire.SourceClock)==0)return null;
            try{
                Need(mapper!=null,"qualified source-clock mapper unavailable");
                Need(anchor.FirstSourceBracketValid,"actual first-source counter bracket is unavailable");
                var point=mapper(r.ReferenceTick);Need(point!=null,"raw fine-clock evidence does not bound this STARTBYTE");
                Need(Convert.ToUInt32(point["generation"])==r.Generation&&Convert.ToUInt32(point["counter_domain"])==r.Domain&&
                    Convert.ToUInt32(point["counter_hz"])==r.CounterHz&&Convert.ToUInt32(point["source_hz"])==r.SourceHz&&
                    Convert.ToUInt32(point["sample_hz"])==r.ApHz&&Convert.ToUInt64(point["exported_source_tick"])==r.ReferenceTick&&
                    Convert.ToUInt64(point["anchor_origin"])==anchor.Origin,"source-clock mapper final generation/domain/rates/reference/origin mismatch");
                return new NpFirstSyncUtcBounds {DeviceIdentity=h.Uid,Binding=Binding(r),SourceHz=r.SourceHz,ReferenceTick=r.ReferenceTick,
                    LowerUs=Convert.ToDecimal(point["pc_us_lower"],CultureInfo.InvariantCulture),UpperUs=Convert.ToDecimal(point["pc_us_upper"],CultureInfo.InvariantCulture),
                    SourceClockQualified=true,Evidence="Producer-qualified NPAR exported source origin plus STARTBYTE offset; existing raw fine-clock affine interval; network correction separate"};
            }catch(Exception failure){error=failure.Message;return null;}
        }
        static Dictionary<string,object> Canonical(NpFirstSyncUsbHistory h,IEnumerable<Dictionary<string,object>> rows,NpFirstSyncUtcBounds clock)
        {
            var r=h.Receipt;var items=rows.ToList();uint mask=0;
            foreach(var row in items.Where(x=>Convert.ToString(x["band"])=="ap")){int p=Convert.ToInt32(row["probe"]);Need(p>=1&&p<=3,"observed probe index");mask|=1U<<(p-1);}
            Need(mask==r.Mask,"observed AP probe set disagrees with receipt");
            var binding=Binding(r);
            // The canonical number stays in the exported byte coordinate.
            // Only the qualified acquisition mapper applies NPAR.Origin when
            // relating that position to its physical receiver clock bracket.
            return NpFirstSyncUtc.Build(r.Raw,binding,h.Uid,new NpFirstSyncUtcContext {DeviceIdentity=h.Uid,Binding=binding,IdentityAndBindingValidated=true,
                Evidence="strict USB v2 negotiation/pages and active source receipt"},NpRecordingUtc.StartsFromNpRows(items),clock);
        }
        static void Status(string folder,string error,bool clockQualified=false){try{File.WriteAllText(Path.Combine(folder,StatusFile),Json.Serialize(new {schema=1,declared=true,error,clock_mapping_qualified=clockQualified,
            limitation="STARTBYTE UTC requires producer-qualified SOURCE_CLOCK and independently bounded raw fine-clock evidence. Its interval is conditional on the device clock model; AP/arrival/ARM/discovery time is not T0."}));}catch{}}
        internal static bool TryWrite(string folder,string identity,int family,string epoch,NpAcquisitionAnchor anchor,NpUsbTimingHistory stream,IEnumerable<Dictionary<string,object>> rows,
            Func<ulong,Dictionary<string,object>> sourceClockBounds=null)
        {
            bool declared;string error=null;try{declared=Declared(folder);}catch(Exception failure){declared=true;error=failure.Message;}
            if(!declared)return false;var items=rows.ToList();Dictionary<string,object> report=null;NpFirstSyncUsbHistory history=null;string journalHash=null,bodyHash=null,clockHash=null,clockError=null;
            try{if(error!=null)throw new InvalidDataException(error);journalHash=Hash(Path.Combine(folder,NpUsbTimingEvidence.FileName));bodyHash=Hash(Path.Combine(folder,NpFirstSyncUsbEvidence.ReceiptFile));clockHash=Hash(Path.Combine(folder,ClockJournal));
                Need(journalHash!=null,"declared raw journal missing");history=Verify(folder,identity,family,epoch,anchor,stream);
                var clock=ClockBounds(history,anchor,sourceClockBounds,out clockError);report=Canonical(history,items,clock);}
            catch(Exception failure){error=failure.Message;}
            if(report==null){
                var starts=NpRecordingUtc.StartsFromNpRows(items);foreach(var s in starts)s.UtcLowerUs=s.UtcUpperUs=null;
                report=NpRecordingUtc.Build(true,starts,family,stream==null?identity:stream.Uid,anchor==null?(uint?)null:anchor.Generation,anchor==null?(uint?)null:anchor.Generation,
                    "Declared USB first-sync evidence unavailable",new NpRecordingUtc.StreamStart {Kind="neuropixels"});
                report["reference_quality"]="declared_first_sync_unavailable";report["np_start_byte_sync_confirmed"]=false;
            }
            report["usb_bound_first_sync"]=history!=null&&history.Qualified&&error==null;report["usb_first_sync_refresh_required"]=true;report["usb_first_sync_error"]=error;
            report["usb_expected_identity"]=identity;report["usb_host_epoch"]=epoch;report["usb_expected_token_hex"]=stream==null?null:stream.Token.ToString("X16",CultureInfo.InvariantCulture);
            report["pc_host_epoch"]=Flag(report,"reference_clock_mapping_qualified")?epoch:null;
            report["usb_ap_hz"]=anchor==null?(object)null:anchor.SampleHz;report["usb_source_hz"]=anchor==null?(object)null:anchor.SourceHz;
            report["usb_journal_sha256"]=journalHash;report["usb_receipt_file_sha256"]=bodyHash;
            report["usb_clock_query_journal_sha256"]=clockHash;report["usb_source_clock_error"]=clockError;
            if(clockError!=null){report["clock_mapping_error"]=clockError;report["unavailable_reason"]=clockError;}
            if(history!=null&&history.Qualified&&error==null){report["usb_profile_crc"]=history.Receipt.ProfileCrc;report["usb_probe_mask"]=history.Receipt.Mask;}
            var rtc=NpRtcUsbArchive.Read(folder);
            if(rtc!=null && rtc.Enabled) {
                NpMetadataFile.WriteText(Path.Combine(folder,"recording_start_pc_estimate.json"),Json.Serialize(report)+Environment.NewLine);
                var root=rtc.SourceRoot;
                if(error!=null || history==null || !history.FineQualified || root==null ||
                    root.Uid!=history.Uid || root.Token!=history.Token || root.Generation!=history.Generation ||
                    !root.Receipt.SequenceEqual(history.FineRoot.Receipt) || !root.Anchor.SequenceEqual(history.FineRoot.Anchor))root=null;
                var rtcReport=NpRtcRecordingUtc.FromUsb(rtc.Raw,root==null?null:root.Receipt,root==null?null:root.Anchor,
                    family,root==null?identity:root.Uid,root==null?0:root.Token,
                    root==null?0:NpFirstSyncUsb.ParseInfo(root.Info,root.Uid,family).Features,NpRecordingUtc.StartsFromNpRows(items));
                foreach(var field in rtcReport)report[field.Key]=field.Value;
                report["usb_device_rtc_mapping_selected"]=true;
                report["usb_device_rtc_cache_sha256"]=Hash(Path.Combine(folder,NpRtcUsbArchive.RawFile));
                report["usb_device_rtc_evidence_error"]=error??rtc.Error;
                report["pc_clock_estimate_file"]="recording_start_pc_estimate.json";
                report["pc_clock_estimate_sha256"]=Hash(Path.Combine(folder,"recording_start_pc_estimate.json"));
            }
            NpRecordingUtc.Write(folder,report);Status(folder,error,Flag(report,"reference_clock_mapping_qualified"));return true;
        }
        internal static bool Refresh(string folder,IEnumerable<Dictionary<string,object>> rows,int family,string identity,uint? generation)
        {
            string path=Path.Combine(folder,NpRecordingUtc.FileName);if(!File.Exists(path))return false;
            var previous=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));if(!Flag(previous,"usb_first_sync_refresh_required"))return false;
            try{
                Need(Flag(previous,"usb_bound_first_sync"),"declared original first-sync binding was never qualified");
                Need(Convert.ToInt32(previous["logger_version"])==family&&generation.HasValue&&Convert.ToUInt32(previous["capture_generation"])==generation.Value&&
                    UidMatches(identity,Convert.ToString(previous["device_identity"])),"layout refresh identity/final generation changed");
                Need(Convert.ToString(previous["usb_journal_sha256"])==Hash(Path.Combine(folder,NpUsbTimingEvidence.FileName))&&
                    Convert.ToString(previous["usb_receipt_file_sha256"])==(Hash(Path.Combine(folder,NpFirstSyncUsbEvidence.ReceiptFile))??""),"archived raw journal/body changed");
                if(Flag(previous,"usb_device_rtc_mapping_selected"))
                    Need(Convert.ToString(previous["usb_device_rtc_cache_sha256"])==(Hash(Path.Combine(folder,NpRtcUsbArchive.RawFile))??""),"archived RTC cache changed");
                object oldClockHash;if(!Flag(previous,"usb_device_rtc_mapping_selected")&&previous.TryGetValue("usb_clock_query_journal_sha256",out oldClockHash))
                    Need(Convert.ToString(oldClockHash)==(Hash(Path.Combine(folder,ClockJournal))??""),"archived raw fine-clock journal changed");
                var stream=NpUsbTimingEvidence.Read(folder);Need(stream!=null&&stream.Anchor!=null,"archived source anchor missing");
                var anchor=NpAcquisitionAnchor.Parse(stream.Anchor,BitConverter.ToUInt32(stream.Anchor,8),family);
                var h=Verify(folder,Convert.ToString(previous["usb_expected_identity"]),family,Convert.ToString(previous["usb_host_epoch"]),anchor,stream);
                Need(h.Token.ToString("X16",CultureInfo.InvariantCulture)==Convert.ToString(previous["usb_expected_token_hex"])&&h.Receipt.Session==Convert.ToUInt32(previous["session_id"])&&
                    h.Receipt.SourceEpoch==Convert.ToUInt32(previous["source_epoch"])&&h.Receipt.ProfileCrc==Convert.ToUInt32(previous["usb_profile_crc"])&&
                    h.Receipt.Mask==Convert.ToUInt32(previous["usb_probe_mask"])&&h.Receipt.ApHz==Convert.ToUInt32(previous["usb_ap_hz"])&&
                    h.Receipt.SourceHz==Convert.ToUInt32(previous["usb_source_hz"])&&BitConverter.ToUInt32(h.Receipt.Raw,324).ToString("X8",CultureInfo.InvariantCulture)==Convert.ToString(previous["first_sync_receipt_crc32"]),"archived source/session/profile binding changed");
                Func<ulong,Dictionary<string,object>> clock=null;string clockError=null;
                try{clock=NpAcquisitionTiming.ArchivedSourceClockBounds(folder,Convert.ToString(previous["usb_expected_identity"]),family);}
                catch(Exception failure){clockError=failure.Message;}
                if(clockError!=null){string captured=clockError;clock=tick=>{throw new InvalidDataException(captured);};}
                TryWrite(folder,Convert.ToString(previous["usb_expected_identity"]),family,Convert.ToString(previous["usb_host_epoch"]),anchor,stream,NpRecordingUtc.SafeFirstSyncLayout(previous,rows),clock);
                if(previous.ContainsKey(NpDerivedImuGridUtc.SealKey)) {
                    var refreshed=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));NpSensorFineTime gridClock=null;
                    try{if(h.FineQualified)gridClock=NpSensorFineTime.FromUsb(h.FineRoot);}
                    catch(Exception optional) when(optional is IOException||optional is UnauthorizedAccessException||optional is ArgumentException||optional is OverflowException){}
                    string gridError;NpDerivedImuGridUtc.Refresh(folder,previous,refreshed,gridClock,out gridError);NpRecordingUtc.Write(folder,refreshed);
                }
            }catch(Exception failure){
                NpRecordingUtc.InvalidateFirstSync(previous,failure.Message);previous["usb_bound_first_sync"]=false;previous["usb_first_sync_error"]=failure.Message;
                string gridError;NpDerivedImuGridUtc.Refresh(folder,previous,previous,null,out gridError);
                NpRecordingUtc.Write(folder,previous);Status(folder,failure.Message);
            }
            return true;
        }
    }
}
