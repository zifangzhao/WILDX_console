using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    internal sealed class NpFirstSyncSdUtcContext
    {
        internal string ExpectedUid;
        internal ulong ExpectedToken=0;
        internal NpFirstSyncBinding CoordinateBinding=null;
        internal bool ImportLocalEvidence=true;
    }
    internal sealed class NpFirstSyncSdUtcState
    {
        internal NpFirstSyncSdBound Bound;
        internal byte[] ClockSector;
        internal string Error,OriginalCrc;
        internal uint ClockGeneration;
    }
    internal static class NpFirstSyncSdUtc
    {
        const string StatusFile="first_sync_sd_status.json";
        const string CoordinateEvidence="Sealed NPSTART1 SOURCE_CLOCK attests the same final-generation exported-source-zero coordinate as this validated UTC2 sector.";
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        static NpFirstSyncBinding Binding(NpFirstSyncReceipt r){return new NpFirstSyncBinding {Family=r.Family,Session=r.Session,Generation=r.Generation,
            SourceEpoch=r.SourceEpoch,Domain=r.Domain,CounterHz=r.CounterHz,ProfileCrc=r.ProfileCrc,Mask=r.Mask,ApHz=r.ApHz};}
        static bool Flag(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)&&value is bool&&(bool)value;}
        static string Hash(string path){using(var input=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");}
        internal static object[] FineInputs(string folder){
            var files=Directory.GetFiles(folder,"ble_clock_*.jsonl").OrderBy(p=>Path.GetFileName(p),StringComparer.Ordinal).ToArray();
            if(files.Length>128||files.Sum(p=>new FileInfo(p).Length)>67108864L)throw new InvalidDataException("Optional fine-clock evidence exceeds the bounded replay size.");
            return files.Select(p=>(object)new Dictionary<string,object>{{"file",Path.GetFileName(p)},{"sha256",Hash(p)}}).ToArray();
        }
        internal static NpFirstSyncSdUtcState Read(byte[] sector,uint session,int family,uint apHz,NpFirstSyncSdUtcContext context=null)
        {
            if(sector==null||sector.Length!=512||sector.Skip(160).Take(348).All(b=>b==0))return null;
            var state=new NpFirstSyncSdUtcState();
            try{
                state.Bound=NpFirstSyncSd.ReadSector(sector,family,session,apHz,context==null?null:context.ExpectedUid,context==null?0:context.ExpectedToken);
                if(state.Bound==null)return null;
                state.OriginalCrc=BitConverter.ToUInt32(sector,508).ToString("X8",CultureInfo.InvariantCulture);
                state.ClockGeneration=BitConverter.ToUInt32(sector,20); // clock sync generation, never capture generation
                state.ClockSector=(byte[])sector.Clone();Array.Clear(state.ClockSector,160,348);
                NpAcquisitionAnchor.Put(state.ClockSector,508,NpAcquisitionAnchor.Crc(state.ClockSector,508));
            }catch(Exception error){state.Error=error.Message;state.Bound=null;state.ClockSector=null;}
            return state;
        }
        static void Status(string folder,NpFirstSyncSdUtcState state,bool clockValid,bool coordinateProof,string error)
        {
            try{File.WriteAllText(Path.Combine(folder,StatusFile),Json.Serialize(new {schema=1,raw_file="recorded_clock.bin",bound_receipt_valid=state.Bound!=null,
                legacy_clock_and_provider_area_valid=clockValid,exported_source_zero_coordinate_confirmed=coordinateProof,
                clock_sync_generation=state.Bound==null?(uint?)null:state.ClockGeneration,capture_generation=state.Bound==null?(uint?)null:state.Bound.Receipt.Generation,
                error=error,limitation="STARTBYTE UTC requires producer-qualified source coordinates and independently bounded clock evidence. Fine NPAR/NPRF uses its measured interval; fallback UTC2 retains millisecond precision. ARM, discovery, release and download time are not substitutes."}));}catch{}
        }
        internal static void Write(string folder,NpFirstSyncSdUtcState state,IEnumerable<Dictionary<string,object>> rows,uint session,int family,uint apHz,
            bool clockValid,ulong lo,ulong hi,uint uncertainty,NpFirstSyncSdUtcContext context=null)
        {
            var items=rows.ToList();Dictionary<string,object> report=null;string error=state.Error;
            if(error==null&&!clockValid)error="Legacy UTC2 clock/provider area is invalid or unavailable; STARTBYTE UTC remains unknown.";
            // SOURCE_CLOCK is the producer's explicit coordinate attestation,
            // not an inference from the existence of a legacy UTC2 clock.
            // Optional caller expectations may narrow the accepted binding.
            bool coordinateProof=clockValid&&state.Bound!=null&&(state.Bound.Receipt.Flags&NpFirstSyncWire.SourceClock)!=0&&
                (context==null||((context.ExpectedUid==null||String.Equals(context.ExpectedUid,state.Bound.Uid,StringComparison.OrdinalIgnoreCase))&&
                    (context.ExpectedToken==0||context.ExpectedToken==state.Bound.Token)&&
                    (context.CoordinateBinding==null||NpFirstSyncWire.GateMatch(state.Bound.Receipt,context.CoordinateBinding))));
            try{
                if(state.Bound!=null){
                    var bound=state.Bound;var receipt=bound.Receipt;var binding=Binding(receipt);NpFirstSyncUtcBounds clock=null;
                    bool fineMapped=false;string fineError=null,fineHash=null;object[] fineInputs=null;
                    if(clockValid&&coordinateProof&&(receipt.Flags&NpFirstSyncWire.SourceClock)!=0){
                        decimal offset=receipt.ReferenceTick*1000000m/receipt.SourceHz;
                        clock=new NpFirstSyncUtcBounds {DeviceIdentity=bound.Uid,Binding=binding,SourceHz=receipt.SourceHz,ReferenceTick=receipt.ReferenceTick,
                            LowerUs=lo+offset-uncertainty,UpperUs=hi+offset+uncertainty+1000,SourceClockQualified=true,
                            Kind=NpFirstSyncUtcBoundsKind.RecordedUtc2Milliseconds,SourcePrecisionUs=1000,
                            Evidence="Strict UTC2 exported-source-zero bracket plus nominal STARTBYTE offset; "+CoordinateEvidence};
                    }
                    // The measured source counter and raw PC exchanges are an
                    // independent clock path. A missing legacy UTC commit must
                    // not discard otherwise qualified fine evidence.
                    if((receipt.Flags&NpFirstSyncWire.SourceClock)!=0&&
                        (context==null||((context.ExpectedUid==null||String.Equals(context.ExpectedUid,bound.Uid,StringComparison.OrdinalIgnoreCase))&&
                        (context.ExpectedToken==0||context.ExpectedToken==bound.Token)&&
                        (context.CoordinateBinding==null||NpFirstSyncWire.GateMatch(receipt,context.CoordinateBinding))))){
                        string rawPath=Path.Combine(folder,NpFineSdArchive.RawFile);
                        if(File.Exists(rawPath))try{
                            var companion=NpFineClockWire.ReadCompanion(File.ReadAllBytes(rawPath),File.ReadAllBytes(Path.Combine(folder,"recorded_clock.bin")),family,session,apHz,bound.Uid,bound.Token);
                            if(companion!=null){
                                if(context==null||context.ImportLocalEvidence)NpFineClockEvidenceImport.Import(folder,companion);
                                var fine=NpFineClockRecording.MapSd(folder,companion,out fineError);
                                if(fine!=null){
                                    fineHash=Hash(rawPath);fineInputs=FineInputs(folder);clock=fine;fineMapped=true;coordinateProof=true;error=null;
                                }
                            }
                        }catch(Exception optional){fineError=optional.Message;}
                    }
                    report=NpFirstSyncUtc.Build(receipt.Raw,binding,bound.Uid,new NpFirstSyncUtcContext {DeviceIdentity=bound.Uid,Binding=binding,
                        IdentityAndBindingValidated=true,Evidence="recorded_clock.bin strict sealed SD UID/token/family/session/AP-rate binding"},NpRecordingUtc.StartsFromNpRows(items),clock);
                    report["sd_bound_first_sync"]=true;report["sd_bound_token_hex"]=bound.Token.ToString("X16",CultureInfo.InvariantCulture);
                    report["sd_recorded_clock_crc32"]=state.OriginalCrc;report["sd_ap_hz"]=apHz;
                    report["sd_profile_crc"]=receipt.ProfileCrc;report["sd_probe_mask"]=receipt.Mask;
                    report["sd_clock_sync_generation"]=state.ClockGeneration;
                    report["sd_exported_source_zero_coordinate_confirmed"]=coordinateProof;
                    report["sd_coordinate_evidence"]=coordinateProof?(fineMapped?clock.Evidence:CoordinateEvidence):null;
                    report["sd_fine_clock_mapping_qualified"]=fineMapped;report["sd_fine_clock_error"]=fineError;
                    report["sd_fine_clock_sector_sha256"]=fineHash;report["sd_fine_clock_inputs"]=fineInputs;
                    report["sd_fine_clock_fit_status_file"]=File.Exists(Path.Combine(folder,"fine_clock_fit_status.json"))?"fine_clock_fit_status.json":null;
                }
            }catch(Exception failure){error=failure.Message;}
            if(report==null){
                var unknown=items.Select(row=>{var copy=new Dictionary<string,object>(row);copy["pc_time_valid"]=false;return copy;}).ToList();
                report=NpRecordingUtc.FromNpRows(unknown,family,null,session,null,"Optional first-sync SD evidence rejected; legacy source positions only");
                report["sd_bound_first_sync"]=false;report["np_start_byte_sync_confirmed"]=false;
            }
            report["sd_first_sync_refresh_required"]=true;report["first_sync_sd_error"]=error;report["sd_legacy_clock_and_provider_area_valid"]=clockValid;
            // A new typed RTC companion owns the canonical device time. Keep
            // the old PC estimate in its own file; never widen/narrow the
            // local RTC interval with arrival-clock evidence.
            string rtcPath=Path.Combine(folder,NpRtcSdArchive.RawFile);
            if(File.Exists(rtcPath) && state.Bound!=null) {
                NpMetadataFile.WriteText(Path.Combine(folder,"recording_start_pc_estimate.json"),Json.Serialize(report)+Environment.NewLine);
                byte[] rtc=null,companion=null,originalClock=null;string rtcHash=null;
                try {
                    if(new FileInfo(rtcPath).Length==512) {
                        rtc=File.ReadAllBytes(rtcPath);
                        using(var sha=System.Security.Cryptography.SHA256.Create())rtcHash=BitConverter.ToString(sha.ComputeHash(rtc)).Replace("-","");
                    }
                    string finePath=Path.Combine(folder,NpFineSdArchive.RawFile);
                    if(File.Exists(finePath)&&new FileInfo(finePath).Length==512)companion=File.ReadAllBytes(finePath);
                    string clockPath=Path.Combine(folder,"recorded_clock.bin");
                    if(File.Exists(clockPath)&&new FileInfo(clockPath).Length==512)originalClock=File.ReadAllBytes(clockPath);
                    if(originalClock!=null && BitConverter.ToUInt32(originalClock,508).ToString("X8",CultureInfo.InvariantCulture)!=state.OriginalCrc)originalClock=null;
                }catch { /* Missing optional bytes produce an unknown RTC map. */ }
                if(context!=null && ((context.ExpectedUid!=null&&!String.Equals(context.ExpectedUid,state.Bound.Uid,StringComparison.OrdinalIgnoreCase)) ||
                    (context.ExpectedToken!=0&&context.ExpectedToken!=state.Bound.Token) ||
                    (context.CoordinateBinding!=null&&!NpFirstSyncWire.GateMatch(state.Bound.Receipt,context.CoordinateBinding))))originalClock=null;
                var rtcReport=NpRtcRecordingUtc.FromSd(rtc,originalClock,companion,family,session,apHz,
                    state.Bound.Uid,state.Bound.Token,NpRecordingUtc.StartsFromNpRows(items));
                foreach(var field in rtcReport)report[field.Key]=field.Value;
                report["sd_device_rtc_mapping_selected"]=true;
                report["sd_device_rtc_sector_sha256"]=rtcHash;
                report["pc_clock_estimate_file"]="recording_start_pc_estimate.json";
                report["pc_clock_estimate_sha256"]=Hash(Path.Combine(folder,"recording_start_pc_estimate.json"));
                report["sd_fine_clock_mapping_qualified"]=false;
            }
            NpRecordingUtc.Write(folder,report);Status(folder,state,clockValid,coordinateProof,error);
        }
        // Called only after the first-sample layout report has been updated.
        // Revalidate the unchanged archived sector; never replace STARTBYTE T0
        // with an AP-row estimate merely because DAT files have been merged.
        internal static bool Refresh(string folder,IEnumerable<Dictionary<string,object>> rows,int family,uint? session)
        {
            string path=Path.Combine(folder,NpRecordingUtc.FileName);if(!File.Exists(path))return false;
            var previous=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));if(!Flag(previous,"sd_bound_first_sync")&&!Flag(previous,"sd_first_sync_refresh_required"))return false;
            try{
                if(!Flag(previous,"sd_bound_first_sync"))throw new InvalidDataException("Original optional first-sync SD binding was not qualified.");
                if(!session.HasValue||Convert.ToUInt32(previous["session_id"])!=session.Value||Convert.ToInt32(previous["logger_version"])!=family)
                    throw new InvalidDataException("Archived first-sync SD family/session changed during layout refresh.");
                var context=new NpFirstSyncSdUtcContext {ExpectedUid=Convert.ToString(previous["device_identity"]),
                    ExpectedToken=UInt64.Parse(Convert.ToString(previous["sd_bound_token_hex"]),NumberStyles.HexNumber,CultureInfo.InvariantCulture),ImportLocalEvidence=false};
                context.CoordinateBinding=new NpFirstSyncBinding {Family=(uint)family,Session=session.Value,Generation=Convert.ToUInt32(previous["capture_generation"]),
                    SourceEpoch=Convert.ToUInt32(previous["source_epoch"]),Domain=Convert.ToUInt32(previous["counter_domain"]),CounterHz=Convert.ToUInt32(previous["counter_hz"]),
                    ApHz=Convert.ToUInt32(previous["sd_ap_hz"]),ProfileCrc=Convert.ToUInt32(previous["sd_profile_crc"]),Mask=Convert.ToUInt32(previous["sd_probe_mask"])};
                uint apHz=Convert.ToUInt32(previous["sd_ap_hz"]);byte[] raw=File.ReadAllBytes(Path.Combine(folder,"recorded_clock.bin"));
                var state=Read(raw,session.Value,family,apHz,context);
                if(state==null||state.Bound==null||state.OriginalCrc!=Convert.ToString(previous["sd_recorded_clock_crc32"])||
                    state.Bound.Receipt.Generation!=Convert.ToUInt32(previous["capture_generation"])||state.Bound.Receipt.SourceEpoch!=Convert.ToUInt32(previous["source_epoch"]))
                    throw new InvalidDataException("Archived first-sync SD binding/seal is unavailable or changed.");
                if(Flag(previous,"sd_device_rtc_mapping_selected") &&
                    (!File.Exists(Path.Combine(folder,NpRtcSdArchive.RawFile)) ||
                    Hash(Path.Combine(folder,NpRtcSdArchive.RawFile))!=Convert.ToString(previous["sd_device_rtc_sector_sha256"])))
                    throw new InvalidDataException("Archived device RTC evidence changed during layout refresh.");
                if(Flag(previous,"sd_fine_clock_mapping_qualified")){
                    if(!File.Exists(Path.Combine(folder,NpFineSdArchive.RawFile))||Hash(Path.Combine(folder,NpFineSdArchive.RawFile))!=Convert.ToString(previous["sd_fine_clock_sector_sha256"])||
                        !previous.ContainsKey("sd_fine_clock_inputs")||Json.Serialize(previous["sd_fine_clock_inputs"])!=Json.Serialize(FineInputs(folder)))
                        throw new InvalidDataException("Archived fine-clock source or PC exchange evidence changed during layout refresh.");
                }
                var safe=NpRecordingUtc.SafeFirstSyncLayout(previous,rows);
                ulong lo,hi;uint uncertainty;bool valid=NpAcquisitionTiming.SdClockBounds(state.ClockSector,session.Value,family,apHz,out lo,out hi,out uncertainty);
                Write(folder,state,safe,session.Value,family,apHz,valid,lo,hi,uncertainty,context);
                if(previous.ContainsKey(NpDerivedImuGridUtc.SealKey)) {
                    var refreshed=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));NpSensorFineTime gridClock=null;
                    try{gridClock=NpSensorFineTime.FromSd(raw,File.ReadAllBytes(Path.Combine(folder,NpFineSdArchive.RawFile)),family,session.Value,apHz,state.Bound.Uid,state.Bound.Token);}
                    catch(Exception optional) when(optional is IOException||optional is UnauthorizedAccessException||optional is ArgumentException||optional is OverflowException){}
                    string gridError;NpDerivedImuGridUtc.Refresh(folder,previous,refreshed,gridClock,out gridError);NpRecordingUtc.Write(folder,refreshed);
                }
            }catch(Exception error){
                NpRecordingUtc.InvalidateFirstSync(previous,error.Message);previous["first_sync_sd_error"]=error.Message;
                previous["sd_exported_source_zero_coordinate_confirmed"]=false;previous["sd_bound_first_sync"]=false;previous["sd_first_sync_refresh_required"]=true;
                previous["sd_legacy_clock_and_provider_area_valid"]=false;previous["sd_coordinate_evidence"]=null;
                previous["sd_fine_clock_mapping_qualified"]=false;previous["sd_fine_clock_error"]=error.Message;previous["pc_host_epoch"]=null;
                string gridError;NpDerivedImuGridUtc.Refresh(folder,previous,previous,null,out gridError);
                NpRecordingUtc.Write(folder,previous);Status(folder,new NpFirstSyncSdUtcState {Error=error.Message},false,false,error.Message);
            }
            return true;
        }
    }
}
