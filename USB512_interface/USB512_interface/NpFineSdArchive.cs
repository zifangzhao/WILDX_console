using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Optional per-record timing evidence only. The native exporter owns card
    // I/O; no extra sector is read for legacy, media or unbound recordings.
    internal static class NpFineSdArchive {
        internal const string RawFile="first_source_clock_sector6.bin";
        internal const string StatusFile="fine_source_clock_status.json";
        internal static NpFineSdCompanion Read(string folder,byte[] clock,int family,uint session,uint apHz,uint mask,
            Func<byte[]> readSector,string expectedUid=null,ulong expectedToken=0) {
            if(clock==null || clock.Length!=512 || (family!=1&&family!=2) ||
                Encoding.ASCII.GetString(clock,0,8)!=(family==1?"N1SDUTC2":"N2SDUTC2") ||
                clock.Skip(160).Take(348).All(value=>value==0))return null;
            NpFirstSyncSdBound bound=null;NpFineSdCompanion result=null;
            string state="invalid_start_receipt",error=null,archiveError=null;bool attempted=false,saved=false;
            try {
                bound=NpFirstSyncSd.ReadSector(clock,family,session,apHz,expectedUid,expectedToken);
                if(bound==null)return null;
                if(bound.Receipt.Mask!=mask)throw new InvalidDataException("Recorded first-sync probe mask differs from the native recording descriptor.");
                state="read_error";attempted=true;
                byte[] raw=readSector();
                if(raw==null)throw new InvalidDataException("Optional first-source clock sector is missing.");
                // Preserve malformed/truncated nonzero evidence exactly too.
                if(raw.Any(value=>value!=0))try {
                    using(var output=new FileStream(Path.Combine(folder,RawFile),FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                        output.Write(raw,0,raw.Length);output.Flush(true);
                    }
                    saved=true;
                }catch(Exception ex){archiveError=ex.Message;}
                state="invalid_companion";
                result=NpFineClockWire.ReadCompanion(raw,clock,family,session,apHz,expectedUid,expectedToken);
                state=result==null?"absent":"source_context_valid";
            }catch(Exception ex){error=ex.Message;}
            try {
                var r=bound==null?null:bound.Receipt;
                var report=new Dictionary<string,object> {
                    {"schema",1},{"format","neuropixels.sd_first_source_clock"},{"optional",true},{"status",state},
                    {"logger_version",family},{"session_id",session},{"ap_hz",apHz},{"descriptor_probe_mask",mask},
                    {"sector6_read_attempted",attempted},{"raw_sector_file",saved?RawFile:null},{"raw_sector_saved",saved},
                    {"source_context_valid",result!=null},{"pc_mapping_available",false},{"utc_mapping_valid",false},
                    {"pc_mapping_reason","This archive validates saved source context only; no PC-clock fit or UTC mapping is evaluated."},
                    {"recorded_uid",bound==null?null:bound.Uid},{"recorded_token_hex",bound==null?null:bound.Token.ToString("X16")},
                    {"identity_scope","Saved recording receipt; does not identify or qualify a currently connected device."},
                    {"capture_generation",r==null?null:(object)r.Generation},{"source_epoch",r==null?null:(object)r.SourceEpoch},
                    {"counter_domain",r==null?null:(object)r.Domain},{"counter_hz",r==null?null:(object)r.CounterHz},
                    {"source_hz",r==null?null:(object)r.SourceHz},{"profile_crc32",r==null?null:(object)r.ProfileCrc},
                    {"profile_known",r==null?null:(object)((r.Flags&NpFirstSyncWire.Profile)!=0)},
                    {"error",error},{"archive_error",archiveError},
                    {"scope","Original optional +6 sector and strict recording-bound NPAR source witness only. No grid origin, sensor phase or physical sample lock is inferred."}
                };
                NpMetadataFile.WriteText(Path.Combine(folder,StatusFile),new JavaScriptSerializer().Serialize(report)+Environment.NewLine);
            }catch { /* Optional diagnostics cannot fail neural export. */ }
            return result;
        }
    }
}
