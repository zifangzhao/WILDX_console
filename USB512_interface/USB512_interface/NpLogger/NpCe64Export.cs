using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CE32_console
{
    // NP extension of CE64's directory and ephys/misc container. Never send
    // these raw GPIO bytes through the Intan channel-remapping decoder.
    internal static class NpCe64Export
    {
        internal const string SensorColumns="chunk,collection_ms,magic,version,enabled,valid,published_ms,imu_sequence,mag_sequence,env_sequence,imu_tick_ms,mag_tick_ms,env_tick_ms,sensor_time_24,accel_x_micro_g,accel_y_micro_g,accel_z_micro_g,gyro_x_milli_dps,gyro_y_milli_dps,gyro_z_milli_dps,mag_x_nano_tesla,mag_y_nano_tesla,mag_z_nano_tesla,temperature_centi_c,pressure_pa,humidity_milli_percent,imu_chip_id,env_chip_id,imu_error,env_error,imu_io_errors,env_io_errors,imu_timeouts,env_timeouts,missed_imu_samples,invalid_mag_samples,ignored_completions";
        internal static uint SensorRecord(byte[] raw,int offset,uint chunk,FileStream binary,FileStream csv) {
            var b=new byte[164];Buffer.BlockCopy(raw,offset,b,0,b.Length);
            if(Encoding.ASCII.GetString(b,0,8)!="NPSENS01" || U(b,8)!=chunk || U(b,16)!=140 || U(b,160)!=Crc(b,160))
                throw new InvalidDataException("NP sensor sequence/size/CRC failure");
            uint tick=U(b,12),enabled=U(b,28),valid=U(b,32);
            if(U(b,20)!=0x31534e53 || U(b,24)!=1 || (enabled&~3U)!=0 || (valid&~7U)!=0 || U(b,36)!=tick ||
               ((valid&3)!=0 && (enabled&1)==0) || ((valid&4)!=0 && (enabled&2)==0))
                throw new InvalidDataException("Unsupported NP sensor snapshot");
            uint[] ages={100,200,3000};
            for(int i=0;i<3;i++)if((valid&(1U<<i))!=0 && (U(b,40+i*4)==0 || unchecked(tick-U(b,52+i*4))>ages[i]))
                throw new InvalidDataException("Stale sensor marked valid");
            var line=new StringBuilder().Append(chunk).Append(',').Append(tick);
            for(int i=0;i<35;i++) {
                line.Append(',');if(i>=12 && i<=21)line.Append(BitConverter.ToInt32(b,20+i*4));else line.Append(U(b,20+i*4));
            }
            var bytes=Encoding.ASCII.GetBytes(line.Append('\n').ToString());csv.Write(bytes,0,bytes.Length);binary.Write(b,0,b.Length);
            return valid;
        }
        // Shared latest-value representation, not reconstructed sensor FIFO data.
        // AG scales match the CE64 +/-8g, +/-2000dps configuration. Compensated
        // magnetic values use 0.3uT/count; this cannot recreate packed BMM registers.
        internal static ulong SensorAnalog(IEnumerable<byte[]> snapshots,uint start,uint span,ulong? startUtc,FileStream analog,TextWriter times,Func<bool> cancelled,out uint finalValid) {
            if(span==0 || span>=0x80000000U)throw new InvalidDataException("Invalid sensor START/STOP timeline");
            var events=new List<Tuple<int,uint,byte[]>>[2];
            for(int group=0;group<2;group++) {
                var seen=new HashSet<ulong>();events[group]=new List<Tuple<int,uint,byte[]>>();
                foreach(var b in snapshots) {
                    uint bit=1U<<group,valid=U(b,32)&bit,seq=U(b,40+group*4),tick=valid!=0?U(b,52+group*4):U(b,12);
                    if(valid!=0&&!seen.Add(((ulong)seq<<32)|tick))continue;
                    events[group].Add(Tuple.Create(unchecked((int)(tick-start)),valid,b));
                }
                events[group]=events[group].OrderBy(e=>e.Item1).ThenBy(e=>e.Item2==0?1:0).ToList();
            }
            int[] at={0,0};var current=new Tuple<int,uint,byte[]>[2];var row=new byte[32];
            ulong count=((ulong)span*1000+799)/800;
            finalValid=0;
            times.WriteLine("row,start_relative_us,device_tick_ms_u32,valid_imu_mag_mask,imu_source_tick_ms,mag_source_tick_ms,imu_sequence,mag_sequence,utc_us,pc_time_valid");
            for(ulong i=0;i<count;i++) {
                if(i%1250==0&&cancelled!=null&&cancelled())throw new OperationCanceledException();
                ulong us=i*800;uint tick=unchecked(start+(uint)(us/1000)),valid=0;
                for(int n=0;n<16;n++){row[n*2]=0;row[n*2+1]=0x80;} // validity sidecar owns meaning, not sentinel alone
                for(int group=0;group<2;group++) {
                    var list=events[group];while(at[group]<list.Count&&(long)list[at[group]].Item1*1000<=(long)us)current[group]=list[at[group]++];
                    var e=current[group];long maxAge=group==0?100000:200000;
                    if(e==null||e.Item2==0||(long)us-(long)e.Item1*1000>maxAge)continue;
                    bool representable=true;int first=group==0?12:18,total=group==0?6:3;
                    for(int axis=0;axis<total;axis++) {
                        int physical=BitConverter.ToInt32(e.Item3,20+(first+axis)*4);
                        double code=group==0?(axis<3?physical*4096.0/1000000.0:physical*16.4/1000.0):physical/300.0;
                        long value=(long)Math.Round(code,MidpointRounding.AwayFromZero);
                        if(value<short.MinValue||value>short.MaxValue){representable=false;break;}
                        int word=group==0?1+axis:7+axis;byte[] encoded=BitConverter.GetBytes((short)value);row[word*2]=encoded[0];row[word*2+1]=encoded[1];
                    }
                    if(representable)valid|=1U<<group;
                    else for(int axis=0;axis<total;axis++){int word=group==0?1+axis:7+axis;row[word*2]=0;row[word*2+1]=0x80;}
                }
                ulong? utc=startUtc.HasValue?(ulong?)checked(startUtc.Value+us):null;
                if(utc.HasValue) {
                    // CE64 low/high word packing; UTC milliseconds-of-day is
                    // derived only from the validated START clock, not chunk IDs.
                    byte[] packed=BitConverter.GetBytes((uint)((utc.Value/1000)%86400000UL));
                    Buffer.BlockCopy(packed,0,row,28,4);
                }
                analog.Write(row,0,row.Length);
                finalValid=valid;
                var imu=current[0];var mag=current[1];
                times.WriteLine(string.Join(",",new object[]{i,us,tick,valid,imu==null?null:(object)U(imu.Item3,52),mag==null?null:(object)U(mag.Item3,56),imu==null?null:(object)U(imu.Item3,40),mag==null?null:(object)U(mag.Item3,44),utc,utc.HasValue?1:0}));
            }
            return count;
        }
        internal static bool IsNp(uint tag) { return (tag & 0xffff0000U)==0x4e500000U; }
        static uint U(byte[] b,int p) { return BitConverter.ToUInt32(b,p); }
        static void Exact(Stream stream,byte[] b,Func<bool> cancelled=null) {
            int p=0,n;while(p<b.Length) {if(cancelled!=null && cancelled())throw new OperationCanceledException("NP prefix export cancelled");n=stream.Read(b,p,b.Length-p);if(n==0)throw new EndOfStreamException("Truncated NP recording");p+=n;}
        }
        static uint Crc(byte[] b,int n) {
            uint c=0xffffffffU;for(int i=0;i<n;i++){c^=b[i];for(int bit=0;bit<8;bit++)c=(c>>1)^((c&1)!=0?0xedb88320U:0U);}return ~c;
        }
        static byte[] Header(Stream stream,long lba,string magic) {
            var b=new byte[512];stream.Position=checked(lba*512);Exact(stream,b);
            if(Encoding.ASCII.GetString(b,0,8)!=magic || U(b,508)!=Crc(b,508))throw new InvalidDataException("NP header/footer CRC or signature failure");
            return b;
        }
        internal static void Export(Stream card,long start,long directoryEnd,string output,Func<bool> cancelled,Action<int> progress)
        { ExportCore(card,start,directoryEnd,output,cancelled,progress,null); }

        internal static void ExportCalibrated(Stream card,long start,long directoryEnd,string output,Func<bool> cancelled,Action<int> progress,USB512_interface.NpCalibrationSet calibration)
        { ExportCore(card,start,directoryEnd,output,cancelled,progress,calibration==null || calibration.Count==0?null:calibration.Copy()); }

        // Explicit read-only salvage, not a replacement footer or a finalized
        // recording. Reuse the validated v6 per-probe geometry and decoder.
        internal static void ExportPowerInterruptedPrefix(Stream card,long start,long directoryEnd,string output,Func<bool> cancelled,Action<int> progress)
        { ExportCore(card,start,directoryEnd,output,cancelled,progress,null,true); }

        sealed class PrefixPoint {
            internal uint Chunks,LastImu;internal bool HaveImu;
            internal long ImuGaps;internal long[] Lengths,ApRows,LfpRows;internal uint[] ValidCounts;
        }

        static void ExportCore(Stream card,long start,long directoryEnd,string output,Func<bool> cancelled,Action<int> progress,USB512_interface.NpCalibrationSet calibration,bool recoverPowerCut=false)
        {
            if(recoverPowerCut) {
                if(cancelled!=null && cancelled())throw new OperationCanceledException("NP prefix export cancelled");
                if(card==null || !card.CanRead || !card.CanSeek || start<8192 || start>uint.MaxValue || directoryEnd>uint.MaxValue ||
                   directoryEnd<start+5 || card.Length/512<start+5)
                    throw new InvalidDataException("Bounded, readable V1 recording extent required for power-cut prefix export");
            }
            // V2 shares the CE64 catalog, but its NP2 payload is not NP1 data.
            // Load the same strict decoder used by the WILD release bundle.
            var signature=new byte[8];card.Position=checked((start+3)*512);Exact(card,signature);
            if(recoverPowerCut && Encoding.ASCII.GetString(signature)!="NPSDLOG1")
                throw new InvalidDataException("Power-cut prefix export supports only V1 NPSDLOG1 format6");
            if(Encoding.ASCII.GetString(signature)=="N2SDLOG1") {
                if(start<0 || start>uint.MaxValue)throw new InvalidDataException("Invalid V2 SD start LBA");
                if(String.IsNullOrWhiteSpace(output) || Directory.Exists(output) || File.Exists(output))
                    throw new IOException("A fresh NP export folder is required; existing outputs are never overwritten.");
                bool success=false,exported=false;string error=null;
                USB512_interface.NpV2SdDatExport.Result result=null;
                try {
                    result=USB512_interface.NpV2SdDatExport.ExportCommitted(card,(uint)start,output,directoryEnd,cancelled,progress,calibration);
                    exported=true;success=result.RecordingComplete;
                    if(!success)error="Recording incomplete (stored state "+result.StoredState+", fault "+result.StoredFault+"); verified committed prefix exported.";
                }
                catch(Exception ex){error=ex.Message;throw;}
                finally {
                    string descriptor=Path.Combine(output,"descriptor.bin");
                    if(File.Exists(descriptor)) {
                        // The strict exporter saved this descriptor only after validation.
                        byte[] d=File.ReadAllBytes(descriptor);
                        var settings=USB512_interface.NpRecordingSettings.Saved(output,true,(int)U(d,16),(int)U(d,24),U(d,8),U(d,12),"sd_export");
                        settings.Context("dat_files","neuropixels_probeN_ap.dat in recorded physical probe order; .partial suffix until fully verified");
                        settings.Context("descriptor_file","descriptor.bin");
                        if(exported && result!=null && result.CommittedBlocks>0){
                            var identityHeader=result.IdentityHeader;
                            USB512_interface.NpProbeIdentity.V2Header(settings,identityHeader,"Verified historical SD payload header; not the currently connected probe");
                            File.WriteAllBytes(Path.Combine(output,"probe_identity_header.bin"),identityHeader);
                        }
                        bool nativeSuccess=exported;Exception metadataError=null;
                        // Retain the exact dual full requests or triple fingerprints
                        // with the DAT. These are recorded intents, not applied proof.
                        try {
                            var configuration=new byte[2048];card.Position=checked((start+8)*512);Exact(card,configuration);
                            settings.SdConfiguration(configuration,d);
                            var parameters=new byte[1536];card.Position=checked(start*512);Exact(card,parameters);
                            File.WriteAllBytes(Path.Combine(output,"CE_params.bin"),parameters);
                        }catch(Exception ex){metadataError=ex;settings.Context("configuration_archive_error",ex.Message);success=false;if(error==null)error="Recording metadata archive failed: "+ex.Message;}
                        if(calibration!=null)settings.Context("adc_calibration_file",USB512_interface.NpCalibrationSet.ManifestName);
                        if(File.Exists(Path.Combine(output,"utc.bin"))) {
                            byte[] utc=File.ReadAllBytes(Path.Combine(output,"utc.bin"));
                            settings.Context("original_utc_receipt_file","utc.bin");
                            settings.Context("original_start_time_evidence","Validated UTC receipt: capture-start bracket, not ADC synchronization.");
                            if((U(utc,16)&3)==3)settings.Context("recording_start_utc_bracket_us",USB512_interface.NpRecordingSettings.Fields(
                                "before",BitConverter.ToUInt64(utc,56),"after",BitConverter.ToUInt64(utc,64)));
                        }
                        settings.Finished(success,error);settings.Write();
                        if(nativeSuccess && metadataError!=null)throw new IOException("DAT exported, but recording metadata could not be archived.",metadataError);
                    }
                }
                return;
            }
            if(NpMediaExport.IsTyped(NpDownloadCatalog.Sector(card,start+3))) {
                if(recoverPowerCut)throw new InvalidDataException("Typed media requires its explicit media-prefix recovery entry; no export was started.");
                if(calibration!=null && calibration.Count!=0)throw new InvalidDataException("Neuropixels calibration does not apply to media recordings.");
                NpMediaExport.Export(card,start,directoryEnd,output,cancelled,progress);return;
            }
            if(Directory.Exists(output))throw new IOException("NP export folder already exists; choose a new destination.");
            Directory.CreateDirectory(output);bool complete=false;string failure="none";
            var opened=new List<FileStream>();var report=new StringBuilder();
            USB512_interface.NpRecordingSettings recordingSettings=null;
            uint[] validCounts=new uint[3];
            uint recoveredChunks=0;bool prefixFinished=false;string prefixBoundary="not scanned";
            try {
                byte[] h=Header(card,start+3,"NPSDLOG1"),f=Header(card,start+4,"NPSDEND1");
                NpSdGeometry.Validate(h,f,start,directoryEnd);
                if(recoverPowerCut && (U(h,8)!=6 || U(h,12)==0 || U(h,20)!=1 || U(f,60)<1 || U(f,60)>4 ||
                    (U(f,60)==3 && U(f,64)==0 && U(f,68)>0)))
                    throw new InvalidDataException("Explicit prefix recovery requires an initial/incomplete V1 format6 recording");
                File.WriteAllBytes(Path.Combine(output,"descriptor.bin"),h);
                File.WriteAllBytes(Path.Combine(output,"footer.bin"),f);
                uint mask=U(h,16),width=U(h,20),hs=U(h,48),chunks=U(f,68);
                if(recoverPowerCut) {
                    long bound=Math.Min(Math.Min(directoryEnd,(long)U(h,44)),card.Length/512);
                    chunks=(uint)Math.Max(0,(bound-start-hs)/128);
                    prefixBoundary="Reached bounded extent/stream end";
                    report.AppendLine("power_interrupted=True").AppendLine("firmware_committed=False").AppendLine("finalized=False").AppendLine("lossless=False")
                        .AppendLine("recovery_scan_limit_chunks="+chunks).AppendLine("recovery_bound_end_lba="+bound)
                        .AppendLine("recovery_data_start_lba="+(start+hs)).AppendLine("calibration=none; nominal ADC counts only");
                }
                if(calibration!=null){calibration.RequireModel(false,(int)mask);calibration.Save(output);}
                bool sensors=U(h,8)>=4,paired=U(h,80)==2;
                recordingSettings=USB512_interface.NpRecordingSettings.Saved(output,false,(int)U(h,24),(int)mask,U(h,8),U(h,12),"sd_export");
                recordingSettings.Context("dat_files","npN_ap.dat, npN_lfp.dat in physical probe order; analogin.dat");
                recordingSettings.Context("configuration_archive_file","CE_params.bin; exact probe selection not stored in this SD format");
                if(recoverPowerCut){recordingSettings.Context("power_cut_prefix_export",true);recordingSettings.Context("recording_finalized",false);recordingSettings.Context("prefix_calibration","none; nominal ADC counts only");}
                if(calibration!=null)recordingSettings.Context("adc_calibration_file",USB512_interface.NpCalibrationSet.ManifestName);
                report.AppendLine("format_version="+U(h,8)).AppendLine("probe_mask="+mask).AppendLine("probe_count="+NpSdGeometry.Count(mask));
                report.AppendLine("ap_sample_rate_hz="+U(h,24)).AppendLine("lfp_sample_rate_hz="+(U(h,24)/12.0).ToString("0.######",CultureInfo.InvariantCulture))
                    .AppendLine("lfp_rate_numerator="+U(h,24)).AppendLine("lfp_rate_denominator=12");
                report.AppendLine("misc_snapshot=chunk_collection_time").AppendLine("battery_valid=False");
                report.AppendLine("session="+U(h,12)).AppendLine("start_lba="+start).AppendLine("reserved_end_lba="+directoryEnd)
                    .AppendLine("committed_chunks="+U(f,68)).AppendLine("channels_per_band=384")
                    .AppendLine("sample_format=little-endian signed int16; sample-major, channel0..383")
                    .AppendLine(calibration==null?"sample_units=nominal ADC counts; no gain calibration or microvolt conversion":"sample_units=calibrated nominal ADC counts; factory host correction; no hardware trim or microvolt conversion claim")
                    .AppendLine("sample_transform_version=1").AppendLine(calibration==null?"sample_transform=512 - raw10":"sample_transform=factory digital ADC threshold/offset; 512 - corrected_raw10; factory gain")
                    .AppendLine(calibration==null?"sample_range=-511..512":"sample_range=int16; factory-dependent; no clipping")
                    .AppendLine("sample_polarity=inverted relative to raw ASIC ADC codes; ASIC negative analog gain undone")
                    .AppendLine("legacy_dat_encoding=USB console <=1.0.3.16 NP1 DAT: unsigned raw codes 0..1023 stored as int16; existing DAT files are not modified")
                    .AppendLine("startup_qualification=2 consecutive superframes; sync/fixed/fill/heartbeat/counter continuity")
                    .AppendLine("adc_bit_integrity=unknown; NP source has no payload checksum");
                var decoders=new NpCe64Decoder[3];var raws=new FileStream[3];var aps=new FileStream[3];var lfps=new FileStream[3];
                long[] apRows=new long[3],lfpRows=new long[3];int lastProgress=-1;
                uint lastImu=0;long imuGaps=0;bool haveImu=false;
                Func<string,FileStream> create=name=>{var fs=new FileStream(Path.Combine(output,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read,131072);opened.Add(fs);return fs;};
                // CE64 calls this the misc stream, but the host convention
                // uses analogin.dat for the one retained low-rate sidecar.
                var ephys=create("ephys.bin");var misc=create("analogin.dat");
                FileStream sensorBin=null,sensorCsv=null;
                if(sensors) {
                    sensorBin=create("sensors.bin");sensorCsv=create("sensors.csv");
                    byte[] columns=Encoding.ASCII.GetBytes(SensorColumns+"\n");sensorCsv.Write(columns,0,columns.Length);
                    report.AppendLine("sensors=latest-value polling; deduplicate each sensor sequence; not lossless FIFO");
                }
                var parameters=create("CE_params.bin");card.Position=start*512;var ps=new byte[1536];Exact(card,ps);parameters.Write(ps,0,ps.Length);
                for(int p=0;p<3;p++)if((mask&(1U<<p))!=0) {
                    decoders[p]=new NpCe64Decoder(NpCe64LaneLayout.GpioV1Quad);
                    // V1 SD quiesce resets the probe/capture session before
                    // START; REC_NRESET remains held until DMA is preset.
                    decoders[p].DeclareRecordingReset();
                    raws[p]=create("np"+(p+1)+".lane8");aps[p]=create("np"+(p+1)+"_ap.dat");lfps[p]=create("np"+(p+1)+"_lfp.dat");
                }
                int laneBytes=(int)U(h,32),payloadBytes=laneBytes*4;
                var raw=new byte[65024];var m=new byte[512];var chronological=new byte[payloadBytes/width];var sampleBytes=new byte[768];
                var firstPoint=new PrefixPoint[3];var lastPoint=new PrefixPoint[3];var rejectedDecoder=new bool[3];
                Func<uint,PrefixPoint> savePoint=index=>new PrefixPoint{Chunks=index,Lengths=opened.Select(file=>file.Position).ToArray(),
                    ApRows=(long[])apRows.Clone(),LfpRows=(long[])lfpRows.Clone(),ValidCounts=(uint[])validCounts.Clone(),
                    LastImu=lastImu,HaveImu=haveImu,ImuGaps=imuGaps};
                Action<PrefixPoint> restorePoint=point=>{
                    for(int i=0;i<opened.Count;i++){opened[i].SetLength(point.Lengths[i]);opened[i].Position=point.Lengths[i];}
                    Array.Copy(point.ApRows,apRows,3);Array.Copy(point.LfpRows,lfpRows,3);Array.Copy(point.ValidCounts,validCounts,3);
                    lastImu=point.LastImu;haveImu=point.HaveImu;imuGaps=point.ImuGaps;recoveredChunks=point.Chunks;
                };
                var stagedAp=recoverPowerCut?new MemoryStream():null;var stagedLfp=recoverPowerCut?new MemoryStream():null;
                Exception prefixError=null;
                card.Position=(start+hs)*512;
                for(uint chunk=0;chunk<chunks;chunk++) {
                    int selected=paired?NpSdGeometry.Probe(mask,chunk):-1;
                    PrefixPoint point=recoverPowerCut?savePoint(chunk):null;
                    long nativeBefore=recoverPowerCut?raws[selected].Position:0;
                    bool qualifiedBefore=recoverPowerCut && decoders[selected].FramesChecked>0;
                    string phase="read";
                    if(recoverPowerCut){if(firstPoint[selected]==null)firstPoint[selected]=point;stagedAp.SetLength(0);stagedAp.Position=0;stagedLfp.SetLength(0);stagedLfp.Position=0;}
                    try {
                    if(cancelled!=null && cancelled())throw new OperationCanceledException("NP export cancelled; saved outputs are incomplete");
                    Exact(card,raw,recoverPowerCut?cancelled:null);Exact(card,m,recoverPowerCut?cancelled:null);
                    phase="descriptor";
                    if(paired) {
                        var d=new byte[512];Buffer.BlockCopy(raw,61632,d,0,512);
                        if(Encoding.ASCII.GetString(d,0,8)!="NPSDBLK1" || U(d,508)!=Crc(d,508))throw new InvalidDataException("NP chunk descriptor CRC/signature failure");
                        NpSdGeometry.Descriptor(h,d,chunk,start+hs+(long)chunk*128);
                        if(U(h,8)==6)decoders[selected].DeclareFixedWord((ushort)U(d,100));
                    }else NpV1StoreBinding.Validate(h,raw,61632);
                    for(int j=payloadBytes+(sensors?164:0);j<raw.Length;j++)
                        if(!(paired && j>=61632 && j<62144) && raw[j]!=0)throw new InvalidDataException("Nonzero NP ephys transport padding");
                    phase="sensor";
                    if(sensors) {
                        uint valid=SensorRecord(raw,payloadBytes,chunk,sensorBin,sensorCsv);
                        for(int i=0;i<3;i++)if((valid&(1U<<i))!=0)validCounts[i]++;
                        if((valid&1)!=0) {
                            uint seq=U(raw,payloadBytes+40);
                            if(haveImu && seq!=lastImu){uint delta=unchecked(seq-lastImu);if(delta>=0x80000000U)throw new InvalidDataException("IMU publication sequence went backwards");imuGaps+=delta-1;}
                            lastImu=seq;haveImu=true;
                        }
                    }
                    phase="misc";
                    for(int row=0;row<16;row++)if(BitConverter.ToUInt16(m,row*32+22)!=(ushort)(chunk*16+row) || U(m,row*32+28)!=chunk)
                        throw new InvalidDataException("NP misc sequence discontinuity");
                    phase="write";
                    ephys.Write(raw,0,payloadBytes);misc.Write(m,0,m.Length);
                    for(int p=0;p<3;p++)if(decoders[p]!=null && (!paired || p==selected)) {
                        int column=width==1?0:p;
                        for(int i=0;i<laneBytes/width;i++)for(int lane=0;lane<4;lane++)
                            chronological[4*i+lane]=raw[lane*laneBytes+i*width+column];
                        raws[p].Write(chronological,0,chronological.Length);
                        int probe=p;
                        phase="decode";
                        decoders[p].Feed(chronological,(ap,n,lfp,l)=>{
                            if(calibration!=null)calibration.Correct(probe+1,ap,n,lfp,l);
                            apRows[probe]+=n;lfpRows[probe]+=l;
                            for(int row=0;row<n;row++){Buffer.BlockCopy(ap,row*768,sampleBytes,0,768);if(recoverPowerCut)stagedAp.Write(sampleBytes,0,768);else aps[probe].Write(sampleBytes,0,768);}
                            for(int row=0;row<l;row++){Buffer.BlockCopy(lfp,row*768,sampleBytes,0,768);if(recoverPowerCut)stagedLfp.Write(sampleBytes,0,768);else lfps[probe].Write(sampleBytes,0,768);}
                        });
                        if(recoverPowerCut){phase="write";stagedAp.WriteTo(aps[p]);stagedLfp.WriteTo(lfps[p]);}
                    }
                    if(recoverPowerCut){lastPoint[selected]=point;recoveredChunks=chunk+1;}
                    int percent=(int)((chunk+1)*100L/chunks);
                    if(progress!=null && percent!=lastProgress){lastProgress=percent;progress(percent);}
                    } catch(Exception ex) {
                        if(!recoverPowerCut)throw;
                        PrefixPoint keep=point;
                        if(phase=="decode") {
                            rejectedDecoder[selected]=true;
                            // Feed checks frames across chunk seams. If the
                            // rejected frame began in the prior native chunk,
                            // remove that chunk too; never retain a torn frame.
                            long failedAt=decoders[selected].StartupDiscardedBytes+decoders[selected].FramesChecked*60;
                            if(!qualifiedBefore || decoders[selected].LfpPhaseErrors!=0)keep=firstPoint[selected];
                            else if(failedAt<nativeBefore && lastPoint[selected]!=null)keep=lastPoint[selected];
                        }
                        restorePoint(keep);
                        prefixBoundary="chunk "+chunk+" "+phase+": "+ex.Message;
                        bool storedBoundary=ex is InvalidDataException || ex is EndOfStreamException ||
                            ((phase=="descriptor" || phase=="decode") && ex is IOException);
                        if(!storedBoundary)prefixError=ex;
                        break;
                    }
                }
                if(recoverPowerCut) {
                    PrefixPoint trim=null;
                    for(int p=0;p<3;p++)if(lastPoint[p]!=null && lastPoint[p].Chunks<recoveredChunks && !rejectedDecoder[p]) {
                        try{decoders[p].Complete();}
                        catch(IOException ex){var keep=decoders[p].FramesChecked==0 || decoders[p].LfpPhaseErrors!=0?firstPoint[p]:lastPoint[p];
                            if(trim==null || keep.Chunks<trim.Chunks)trim=keep;
                            prefixBoundary+="; probe "+(p+1)+" final partial frame: "+ex.Message;}
                    }
                    if(trim!=null)restorePoint(trim);
                    prefixFinished=prefixError==null;
                }
                for(int p=0;p<3;p++)if(decoders[p]!=null && chunks>0){if(!recoverPowerCut)decoders[p].Complete();string name="np"+(p+1);
                    report.AppendLine(name+"_fixed_word="+decoders[p].ExpectedFixedWord).AppendLine(name+(recoverPowerCut?"_frames_scanned=":"_frames=")+decoders[p].FramesChecked).AppendLine(name+"_ap_rows="+apRows[p]).AppendLine(name+"_lfp_rows="+lfpRows[p])
                        .AppendLine(name+(recoverPowerCut?"_scanned_frame_errors=":"_frame_errors=")+decoders[p].FrameErrors).AppendLine(name+(recoverPowerCut?"_scanned_counter_errors=":"_counter_errors=")+decoders[p].CounterErrors)
                        .AppendLine(name+"_relocks="+decoders[p].Resynchronizations).AppendLine(name+"_startup_discarded_bytes="+decoders[p].StartupDiscardedBytes)
                        .AppendLine(name+"_first_superframe_counter="+decoders[p].FirstSuperframeCounter).AppendLine(name+"_initial_lfp_slot="+decoders[p].InitialLfpSlot)
                        .AppendLine(name+"_startup_discarded_lfp_superframes="+decoders[p].StartupDiscardedLfpSuperframes).AppendLine(name+"_lfp_phase_errors="+decoders[p].LfpPhaseErrors)
                        .AppendLine(name+"_lfp_first_cycle_ap_row_index="+(decoders[p].InitialLfpSlot==0?0:12-decoders[p].InitialLfpSlot));}
                report.AppendLine("missing_saved_imu_publications="+imuGaps);
                report.AppendLine("imu_valid="+(validCounts[0]>0)).AppendLine("valid_imu_snapshots="+validCounts[0])
                    .AppendLine("valid_mag_snapshots="+validCounts[1]).AppendLine("valid_env_snapshots="+validCounts[2]);
                foreach(var file in opened)file.Flush();
                complete=!recoverPowerCut && chunks>0 && U(f,60)==3 && U(f,64)==0;
                report.AppendLine("download_complete="+(!recoverPowerCut)).AppendLine("stored_state="+U(f,60)).AppendLine("stored_fault="+U(f,64));
                if(recoverPowerCut){failure="Power-interrupted, incomplete recording; recovered prefix is not firmware-committed or lossless.";if(prefixError!=null)throw prefixError;}
                else if(!complete)failure=chunks==0?"No committed samples; recording metadata retained.":"Firmware recording incomplete; only its verified committed prefix was exported.";
            } catch(Exception ex){if(recoverPowerCut)prefixFinished=false;failure=ex.Message;throw;}
            finally {
                foreach(var file in opened)file.Dispose();
                if(recoverPowerCut)report.AppendLine("power_cut_prefix_export=True").AppendLine("recovered="+(recoveredChunks>0)).AppendLine("recovered_prefix_chunks="+recoveredChunks)
                    .AppendLine("prefix_export_finished="+prefixFinished).AppendLine("prefix_boundary="+prefixBoundary)
                    .AppendLine("prefix_limits=contiguous whole chunks only; partial final NP1 frame retained natively but never converted; scanned decoder counters may include the excluded boundary; no repair, footer synthesis or sample padding");
                File.WriteAllText(Path.Combine(output,"capture_status.txt"),"complete="+complete+"\r\nfailure="+failure+"\r\n"+report.ToString());
                if(recordingSettings!=null) {
                    recordingSettings.Streams(true,validCounts[0]>0,false);
                    recordingSettings.Context("imu_valid_snapshots",validCounts[0]);
                    recordingSettings.Finished(complete,failure=="none"?null:failure);recordingSettings.Write();
                }
            }
        }
    }
}

