using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // A retained register-read sample, not a reconstructed ADC/FIFO sample.
    internal sealed class NpImuSample
    {
        public string kind="sample",reset_reason,raw_snapshot_hex;
        public long index,sequence_gap,transport_gaps;
        public int segment;
        public uint sequence,irq_ms,sensor_time_24,observed_ms,valid,queue_overflow,missed_estimate;
        public uint? firmware_session,packet_sequence;
        public ulong sensor_time_unwrapped;
        public bool mcu_clock_reset;
        public long? host_receipt_qpc,host_receipt_pc_us;
        public int[] ag,mag;
        public NpImuSample() {}
    }

    internal sealed class NpImuSampleState
    {
        internal long Observations,Invalid,Duplicates,Samples,SequenceGaps,Resets;
        NpImuSample last;bool pendingReset,mcuReset;int segment;
        static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
        internal NpImuSample Accept(byte[] b,int o,uint observed,uint effectiveValid,uint? session,uint? packet,long transportGaps,uint overflow)
        {
            Observations++;
            if(b==null||o<0||b.Length-o<140||U(b,o)!=0x31534e53||U(b,o+4)!=1)
                throw new InvalidDataException("Unsupported native IMU snapshot.");
            uint seq=U(b,o+20),irq=U(b,o+32),native=U(b,o+44);
            if((effectiveValid&1)==0||seq==0||native>=0x1000000||unchecked(observed-irq)>100) {
                Invalid++;if(last!=null&&seq==0&&irq==0&&native==0)pendingReset=true;return null;
            }
            if(last!=null&&!pendingReset&&seq==last.sequence&&irq==last.irq_ms&&native==last.sensor_time_24&&session==last.firmware_session) {Duplicates++;return null;}
            uint ds=last==null?1:unchecked(seq-last.sequence),dt=last==null?0:unchecked(irq-last.irq_ms);
            uint dn=last==null?0:(native-last.sensor_time_24)&0xffffff;
            string reason=null;
            if(last!=null) {
                if(session!=last.firmware_session){reason="firmware_session_changed";mcuReset=true;}
                else if(dt>=0x80000000U){reason="mcu_clock_reversed";mcuReset=true;}
                else if(pendingReset||ds==0||ds>=0x80000000U)reason="sensor_or_sequence_reset";
                else if(dt>=300000||dn==0||dn>=0x800000)reason="sensor_wrap_or_reset_ambiguous";
                // A broad consistency check rejects resets; it is not oscillator calibration.
                else if(dt>20&&(dn<dt*12.8||dn>dt*51.2))reason="sensor_clock_discontinuity";
                if(reason!=null){segment++;Resets++;}
            }
            long gap=last!=null&&reason==null?(long)ds-1:0;SequenceGaps+=gap;
            var sample=new NpImuSample {index=Samples++,segment=segment,reset_reason=reason,mcu_clock_reset=mcuReset,
                sequence=seq,irq_ms=irq,sensor_time_24=native,sensor_time_unwrapped=last!=null&&reason==null?last.sensor_time_unwrapped+dn:native,
                observed_ms=observed,valid=effectiveValid,firmware_session=session,packet_sequence=packet,transport_gaps=transportGaps,
                sequence_gap=gap,queue_overflow=overflow,missed_estimate=U(b,o+128),
                raw_snapshot_hex=BitConverter.ToString(b,o,140).Replace("-",""),ag=new int[6],mag=new int[3]};
            for(int i=0;i<6;i++)sample.ag[i]=BitConverter.ToInt32(b,o+48+i*4);
            for(int i=0;i<3;i++)sample.mag[i]=BitConverter.ToInt32(b,o+72+i*4);
            pendingReset=false;last=sample;return sample;
        }
    }

    internal sealed class NpImuSampleJournal : IDisposable
    {
        internal const string FileName="imu_native_samples.jsonl";
        readonly NpImuSampleState state=new NpImuSampleState();
        readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly string folder;
        StreamWriter output;long written;internal string Error {get;private set;}
        internal NpImuSampleJournal(string directory,string identity)
        {
            folder=directory;
            try {
                output=new StreamWriter(new FileStream(Path.Combine(folder,FileName),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536),new UTF8Encoding(false));
                output.WriteLine(json.Serialize(new{kind="metadata",schema=1,device_identity=identity,host_epoch=NpHostClock.Epoch,
                    session_id=Guid.NewGuid().ToString("N"),source="live_verified_sensor_metadata",clock=NpHostClock.Receipt,
                    reference="Sensor register counter paired with MCU I2C-completion ms; no ADC-conversion or sensor-filter phase calibration."}));
            }catch(Exception ex){Error=ex.Message;}
        }
        internal void Observe(byte[] body,int offset,uint observed,uint valid,uint? session,uint? packet,long transportGaps,uint overflow)
        {
            if(output==null)return;
            try {
                var row=state.Accept(body,offset,observed,valid,session,packet,transportGaps,overflow);if(row==null)return;
                row.host_receipt_qpc=Stopwatch.GetTimestamp();row.host_receipt_pc_us=(long)NpHostClock.UnixUs(row.host_receipt_qpc.Value);
                output.WriteLine(json.Serialize(row));written++;
            }catch(Exception ex){Error=ex.Message;try{output.Dispose();}catch{}output=null;}
        }
        public void Dispose()
        {
            try{if(output!=null)output.Dispose();}catch(Exception ex){Error=ex.Message;}output=null;
            try{File.WriteAllText(Path.Combine(folder,"imu_native_samples_status.json"),json.Serialize(new{schema=1,observations=state.Observations,
                retained_samples=written,accepted_samples=state.Samples,invalid_observations=state.Invalid,duplicate_observations=state.Duplicates,
                sequence_gaps=state.SequenceGaps,reset_segments=state.Resets,error=Error,
                limitation="Distinct valid retained observations only; sequence gaps and missed estimates are not an exact count of lost ADC conversions. analogin.dat remains a held-value compatibility grid."}));}catch{}
        }
    }

    internal static class NpImuTimeCorrection
    {
        internal const string CsvName="imu_samples.timestamps.calibrated.csv",TimeName="imu_samples.timestamps.calibrated.dat",ReportName="imu_timestamp_calibration.json";
        sealed class Clock {internal uint Tick;internal double Send,Receive;internal int Segment;}
        sealed class Mapping {internal string Epoch,Identity,Source,EvidenceError;internal bool SampleIdentityKnown=true;internal double HalfWidth;internal List<Clock> Points=new List<Clock>();}
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static string F(double n){return n.ToString("R",Inv);}
        static string Cell(string s){return s.IndexOfAny(new[]{',','"','\r','\n'})<0?s:"\""+s.Replace("\"","\"\"")+"\"";}
        static void ValidateSample(NpImuSample row,long index)
        {
            if(row==null||row.ag==null||row.ag.Length!=6||row.mag==null||row.mag.Length!=3||row.index!=index||
                row.sequence==0||(row.valid&1)==0||row.sensor_time_24>=0x1000000||unchecked(row.observed_ms-row.irq_ms)>100||
                row.raw_snapshot_hex==null||row.raw_snapshot_hex.Length!=280||(row.sensor_time_unwrapped&0xffffff)!=row.sensor_time_24)
                throw new InvalidDataException("Malformed native IMU row.");
            var b=new byte[140];for(int i=0;i<b.Length;i++)b[i]=Byte.Parse(row.raw_snapshot_hex.Substring(i*2,2),NumberStyles.HexNumber,Inv);
            if(BitConverter.ToUInt32(b,0)!=0x31534e53||BitConverter.ToUInt32(b,4)!=1||BitConverter.ToUInt32(b,20)!=row.sequence||
                BitConverter.ToUInt32(b,32)!=row.irq_ms||BitConverter.ToUInt32(b,44)!=row.sensor_time_24||(row.valid&~BitConverter.ToUInt32(b,12))!=0)
                throw new InvalidDataException("Native IMU fields disagree with the retained raw snapshot.");
            for(int i=0;i<9;i++)if(BitConverter.ToInt32(b,48+i*4)!=(i<6?row.ag[i]:row.mag[i-6]))throw new InvalidDataException("Native IMU values disagree with raw data.");
        }
        static string Uid(string s){var m=Regex.Match(s??"",@"(?i)(?<![0-9a-f])[0-9a-f]{24}(?![0-9a-f])");return m.Success?m.Value.ToUpperInvariant():null;}
        static string Hash(string p){using(var h=SHA256.Create())using(var s=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(s)).Replace("-","");}
        static Dictionary<string,object> Object(string s){return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(s);}
        static double N(Dictionary<string,object> d,string k){return Convert.ToDouble(d[k],Inv);}
        static string S(Dictionary<string,object> d,string k){return d.ContainsKey(k)?Convert.ToString(d[k],Inv):null;}
        static Mapping ReadClock(string folder)
        {
            string path=Path.Combine(folder,"first_sample_timestamps.json");var map=new Mapping();
            var stream=NpUsbTimingEvidence.Read(folder);
            if(stream!=null&&stream.Points.Count>=2) {
                map.Epoch=stream.Epoch;map.Identity=stream.Uid;map.Source=NpUsbTimingEvidence.FileName;map.HalfWidth=stream.HalfWidth;map.EvidenceError=stream.Error;
                foreach(var p in stream.Points)map.Points.Add(new Clock{Tick=p.Record.SealMs,Send=stream.PcUs(p.SendQpc),Receive=stream.PcUs(p.ReceiveQpc),Segment=p.Segment});
                return map;
            }
            if(File.Exists(path)) {
                var d=Object(File.ReadAllText(path));map.Epoch=S(d,"host_epoch");map.Identity=S(d,"device_identity");map.Source=Path.GetFileName(path);
                object pc;if(d.TryGetValue("pc_clock",out pc))map.HalfWidth=N((Dictionary<string,object>)pc,"anchor_half_width_us");
                object points;if(d.TryGetValue("clock_observations",out points))foreach(Dictionary<string,object> p in (IEnumerable)points)
                    map.Points.Add(new Clock{Tick=Convert.ToUInt32(p["DeviceMs"]),Send=N(p,"SendPcUs"),Receive=N(p,"ReceivePcUs")});
            } else {
                path=Path.Combine(folder,"clock_observations.csv");if(!File.Exists(path))return map;
                using(var reader=new StreamReader(path)) {
                    string[] header=(reader.ReadLine()??"").Split(',');string line;
                    foreach(string key in new[]{"device_identity","host_epoch","device_ms","host_utc_us","uncertainty_us","kind"})if(!header.Contains(key))return map;
                    while((line=reader.ReadLine())!=null) {
                        var c=line.Split(',');if(c.Length!=header.Length)throw new InvalidDataException("Malformed IMU clock query row.");
                        var d=header.Select((h,i)=>new{h,v=c[i]}).ToDictionary(x=>x.h,x=>x.v);
                        if(d["kind"]!="USB_EP0_CONTROL_ONLY")continue;
                        if(map.Points.Count==0){map.Epoch=d["host_epoch"];map.Identity=d["device_identity"];map.Source=Path.GetFileName(path);}
                        if(map.Epoch!=d["host_epoch"]||Uid(map.Identity)!=Uid(d["device_identity"]))throw new InvalidDataException("Mixed IMU clock identities/epochs.");
                        double pc=Double.Parse(d["host_utc_us"],Inv),u=Double.Parse(d["uncertainty_us"],Inv);
                        if(u<1000||u>5000000)throw new InvalidDataException("Invalid legacy control-clock uncertainty.");
                        map.Points.Add(new Clock{Tick=UInt32.Parse(d["device_ms"],Inv),Send=pc-u,Receive=pc+u});
                    }
                }
            }
            return map;
        }
        static bool Bounds(Mapping map,NpImuSample row,out double low,out double high)
        {
            low=high=Double.NaN;if(!map.SampleIdentityKnown||row.mcu_clock_reset||map.Points.Count<2||String.IsNullOrWhiteSpace(map.Epoch)||Uid(map.Identity)==null)return false;
            for(int i=1;i<map.Points.Count;i++) {
                var a=map.Points[i-1];var b=map.Points[i];uint span=unchecked(b.Tick-a.Tick),pos=unchecked(row.irq_ms-a.Tick);
                if(a.Segment!=b.Segment)continue; // Explicit missing-query interval, never silently bridged.
                double midpointSpan=(b.Send+b.Receive-a.Send-a.Receive)/2;
                if(span==0||span>=0x80000000U||a.Send<1577836800000000||b.Receive>4102444800000000||
                    a.Receive<a.Send||b.Receive<b.Send||b.Send<=a.Receive||midpointSpan/span<900||midpointSpan/span>1100)
                    return false;
                if(pos==0||pos>=span)continue; // no extrapolation, including uncertain boundary milliseconds
                // Query and event integer-ms phases may differ by up to one ms.
                low=Math.Floor(a.Send+(pos-1.0)/span*(b.Send-a.Send)-map.HalfWidth-1);
                high=Math.Ceiling(a.Receive+(pos+1.0)/span*(b.Receive-a.Receive)+map.HalfWidth+1);
                return high>=low;
            }
            return false;
        }
        internal static string Correct(string directory,bool replace=false)
        {
            string folder=Path.GetFullPath(directory),journal=Path.Combine(folder,NpImuSampleJournal.FileName),raw=Path.Combine(folder,"imu_snapshots.bin");
            if(!File.Exists(journal)&&!File.Exists(raw))return "{\"status\":\"unavailable\",\"reason\":\"No native IMU samples retained.\"}";
            string csv=Path.Combine(folder,CsvName),times=Path.Combine(folder,TimeName),report=Path.Combine(folder,ReportName);
            if(!replace&&(File.Exists(csv)||File.Exists(times)))throw new IOException("IMU derived timestamps already exist; use ReplaceDerived to regenerate them.");
            var map=ReadClock(folder);string source=File.Exists(journal)?journal:raw;
            string journalStatus=Path.Combine(folder,"imu_native_samples_status.json"),journalError=null;
            if(source==journal&&File.Exists(journalStatus))journalError=S(Object(File.ReadAllText(journalStatus)),"error");
            string rawIdentity=null;
            if(source==raw) {
                string status=Path.Combine(folder,"imu_observation_status.txt"),experiment=Path.Combine(folder,"experiment.json");
                if(File.Exists(status)){var match=Regex.Match(File.ReadAllText(status),@"(?m)^device_uid=([^\r\n]+)");if(match.Success)rawIdentity=Uid(match.Groups[1].Value);}
                if(File.Exists(experiment)) {
                    string other=Uid(S(Object(File.ReadAllText(experiment)),"uid"));
                    if(rawIdentity!=null&&other!=null&&other!=rawIdentity)throw new InvalidDataException("Raw IMU recording identity receipts disagree.");
                    rawIdentity=rawIdentity??other;
                }
                if(rawIdentity!=null&&Uid(map.Identity)!=null&&rawIdentity!=Uid(map.Identity))throw new InvalidDataException("Raw IMU and clock-query device identities differ.");
                map.SampleIdentityKnown=rawIdentity!=null;
            }
            var state=new NpImuSampleState();long rows=0,mapped=0;double minWidth=Double.PositiveInfinity,maxWidth=0;
            string id=Guid.NewGuid().ToString("N"),ct=csv+"."+id+".partial",tt=times+"."+id+".partial";
            try {
                using(var output=new StreamWriter(ct,false,new UTF8Encoding(false)))using(var binary=new BinaryWriter(File.Open(tt,FileMode.CreateNew))) {
                    output.WriteLine("sample_index,segment,sequence,sensor_time_24,sensor_time_unwrapped,irq_device_ms,pc_unix_seconds,pc_lower_unix_seconds,pc_upper_unix_seconds,host_epoch,status,sequence_gap,transport_gaps,queue_overflow,missed_conversion_estimate,valid_flags,ax_micro_g,ay_micro_g,az_micro_g,gx_milli_dps,gy_milli_dps,gz_milli_dps,mx_nano_tesla,my_nano_tesla,mz_nano_tesla,reset_reason");
                    Action<NpImuSample> emit=row=> {
                        ValidateSample(row,rows);
                        double lo,hi;bool valid=Bounds(map,row,out lo,out hi);double pc=valid?(lo+(hi-lo)/2)/1000000:Double.NaN;
                        binary.Write(pc);if(valid){mapped++;double w=hi-lo;minWidth=Math.Min(minWidth,w);maxWidth=Math.Max(maxWidth,w);}
                        string[] values={row.index.ToString(),row.segment.ToString(),row.sequence.ToString(),row.sensor_time_24.ToString(),row.sensor_time_unwrapped.ToString(),row.irq_ms.ToString(),
                            valid?F(pc):"",valid?F(lo/1000000):"",valid?F(hi/1000000):"",map.Epoch??"",valid?"mapped_i2c_completion_proxy":row.mcu_clock_reset?"mcu_reset_unbound":"clock_not_bracketed",
                            row.sequence_gap.ToString(),row.transport_gaps.ToString(),row.queue_overflow.ToString(),row.missed_estimate.ToString(),row.valid.ToString()};
                        output.Write(String.Join(",",values.Select(Cell)));foreach(int value in row.ag)output.Write(","+value.ToString(Inv));
                        foreach(int value in row.mag)output.Write(","+((row.valid&2)!=0?value.ToString(Inv):""));output.WriteLine(","+Cell(row.reset_reason??""));rows++;
                    };
                    if(source==journal) {
                        using(var reader=new StreamReader(journal)) {
                            var meta=Object(reader.ReadLine()??"");
                            if(S(meta,"kind")!="metadata"||N(meta,"schema")!=1)throw new InvalidDataException("Unsupported native IMU journal.");
                            if(map.Points.Count>0&&(S(meta,"host_epoch")!=map.Epoch||Uid(S(meta,"device_identity"))!=Uid(map.Identity)))throw new InvalidDataException("Native IMU journal and clock-query identity/epoch differ.");
                            if(map.Points.Count==0){map.Epoch=S(meta,"host_epoch");map.Identity=S(meta,"device_identity");}
                            string line;var json=new JavaScriptSerializer();while((line=reader.ReadLine())!=null){if(line.Length>16384)throw new InvalidDataException("Oversized native IMU row.");emit(json.Deserialize<NpImuSample>(line));}
                        }
                    } else {
                        using(var reader=new BinaryReader(File.OpenRead(raw))) {
                            if(reader.BaseStream.Length%140!=0)throw new InvalidDataException("Partial retained IMU snapshot; raw source preserved.");
                            while(reader.BaseStream.Position<reader.BaseStream.Length){var b=reader.ReadBytes(140);var row=state.Accept(b,0,BitConverter.ToUInt32(b,16),BitConverter.ToUInt32(b,12),null,null,0,0);if(row!=null)emit(row);}
                        }
                    }
                }
                Commit(ct,csv);Commit(tt,times);
                var result=new{schema=1,status=rows==0?"no_valid_samples":String.IsNullOrEmpty(journalError)?"processed":"processed_incomplete_native_journal",native_journal_error=journalError,source=Path.GetFileName(source),source_sha256=Hash(source),
                    samples=rows,mapped_samples=mapped,unmapped_samples=rows-mapped,csv=CsvName,timestamps=TimeName,
                    timestamp_format="Little-endian float64 PC Unix seconds, one per retained sample row; NaN where mapping is unavailable.",
                    clock_queries=map.Points.Count,clock_source=map.Source,clock_evidence_error=map.EvidenceError,device_identity=map.Identity,host_epoch=map.Epoch,
                    sample_identity_bound=map.SampleIdentityKnown,raw_recording_uid=rawIdentity,native_counter_nominal_hz=25600,
                    sample_identity_scope=source==raw?"Anonymous snapshots associated through recording-status/experiment UID and clock-journal UID; not a per-snapshot UID receipt.":"Native journal metadata bound to clock-query UID and host epoch.",
                    replay_invalid_observations=source==raw?(long?)state.Invalid:null,replay_duplicate_observations=source==raw?(long?)state.Duplicates:null,
                    min_clock_interval_us=mapped>0?(double?)minWidth:null,max_clock_interval_us=mapped>0?(double?)maxWidth:null,
                    reference="MCU I2C-completion timestamp proxy paired with native sensor register counter. Conditional affine MCU-to-PC mapping, integer-ms phase included; host receipt not used as sample time.",
                    limitation="Not a bound on ADC acquisition, sensor read-latch-to-completion, sensor/filter delay, clock nonlinearity, physical NP/GPIO alignment or lossless 200 Hz capture. analogin.dat remains a held-value grid. No missing samples are filled.",
                    csv_sha256=Hash(csv),timestamps_sha256=Hash(times)};
                string text=new JavaScriptSerializer().Serialize(result);AtomicReport(report,text);return text;
            }finally{if(File.Exists(ct))File.Delete(ct);if(File.Exists(tt))File.Delete(tt);}
        }
        static void Commit(string source,string target){if(File.Exists(target))File.Replace(source,target,null);else File.Move(source,target);}
        static void AtomicReport(string path,string text){string t=path+"."+Guid.NewGuid().ToString("N")+".partial";try{File.WriteAllText(t,text);Commit(t,path);}finally{if(File.Exists(t))File.Delete(t);}}
        internal static void Automatic(string folder){try{Correct(folder);}catch(Exception ex){try{AtomicReport(Path.Combine(folder,ReportName),new JavaScriptSerializer().Serialize(new{schema=1,status="unavailable",reason=ex.Message}));}catch{}}}
    }
}
