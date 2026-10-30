using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Optional native-record metadata. The exporter owns all card reads; this
    // helper cannot issue a command, validate payloads, or change NN admission.
    internal static class NpRtcSdArchive {
        internal const string RawFile="device_rtc_start_sector7.bin";
        internal const string StatusFile="device_rtc_start_status.json";
        internal static void Capture(string folder,byte[] clock,NpFineSdCompanion fine,Func<byte[]> readSector) {
            if(fine==null || !fine.FineCounter64)return;
            string status="unavailable",error=null,archiveError=null;bool attempted=false,saved=false;
            NpRtcStartBounds bounds=null;NpRtcStartStamp stamp=null;
            try {
                var recorded=fine.Bound;
                string finePath=Path.Combine(folder,NpFineSdArchive.RawFile);
                if(new FileInfo(finePath).Length!=512)throw new InvalidDataException("Archived source root sector has the wrong size.");
                var binding=NpRtcStartWire.BindSd(clock,File.ReadAllBytes(finePath),
                    (int)recorded.Receipt.Family,recorded.Receipt.Session,recorded.Receipt.ApHz,recorded.Uid,recorded.Token);
                attempted=true;var raw=readSector();
                if(raw==null || raw.Length!=512)throw new InvalidDataException("Optional device RTC start sector is missing or truncated.");
                if(raw.All(value=>value==0) || raw.All(value=>value==255))status="absent";
                else {
                    // Preserve original invalid evidence too. An existing raw
                    // archive is never replaced with a different recording.
                    try {
                        string path=Path.Combine(folder,RawFile);
                        if(File.Exists(path)) {
                            if(!File.ReadAllBytes(path).SequenceEqual(raw))throw new IOException("Existing RTC evidence differs from this recording.");
                        }else using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                            output.Write(raw,0,raw.Length);output.Flush(true);
                        }
                        saved=true;
                    }catch(Exception failure){archiveError=failure.Message;}
                    stamp=NpRtcStartWire.ParseSector(raw,binding);
                    bounds=NpRtcStartWire.Map(raw.Take(128).ToArray(),binding);
                    status=bounds.Qualified?"local_source_mapping_valid":"local_source_mapping_unqualified";
                    error=bounds.Error;
                }
            }catch(Exception failure){status="unavailable";error=failure.Message;}
            try {
                NpMetadataFile.WriteText(Path.Combine(folder,StatusFile),new JavaScriptSerializer().Serialize(new {
                    schema=1,optional=true,status,sector7_read_attempted=attempted,raw_sector_saved=saved,raw_sector_file=saved?RawFile:null,
                    local_rtc_to_np_mapping_qualified=bounds!=null&&bounds.Qualified,
                    local_interval_width_us=bounds!=null&&bounds.Qualified?(long?)bounds.WidthUs:null,
                    local_sub_ms_verified=bounds!=null&&bounds.SubMillisecond,
                    pc_calibration_generation=stamp==null?(uint?)null:stamp.CalibrationGeneration,
                    declared_pc_calibration_uncertainty_us=stamp==null?(uint?)null:stamp.PcUncertaintyUs,
                    error,archive_error=archiveError,
                    scope="Original device hardware RTC to final NP STARTBYTE evidence. PC calibration uncertainty and network correction are separate; native sensor conversion phase is not qualified."
                })+Environment.NewLine);
            }catch { /* Optional diagnostics cannot fail NN export. */ }
        }
    }
}
