using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;
using System.Threading;
using System.Linq;

namespace USB512_interface
{
    // Optional startup evidence only. Never called from a sample/USB completion loop.
    internal sealed class NpAcquisitionAnchor
    {
        internal const uint RequestMagic=0x5141504e, ReplyMagic=0x5241504e;
        internal const uint FirstSource=0x10;
        internal uint Flags,Generation,BeforeMs,AfterMs,BeforeCycles,AfterCycles,CounterHz,SourceHz,SampleHz;
        internal ulong Origin;
        internal bool SourceValid { get { return (Flags&13)==13; } }
        internal bool FirstSourceBracketValid { get { return (Flags&FirstSource)!=0; } }
        internal static byte[] Request(uint id) {
            if(id==0)throw new ArgumentOutOfRangeException("id");
            var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,1);Put(b,8,id);Put(b,60,Crc(b,60));return b;
        }
        internal static NpAcquisitionAnchor Parse(byte[] b,uint id,int version) {
            if(b==null || b.Length!=64 || U(b,0)!=ReplyMagic || U(b,4)!=1 || U(b,8)!=id || U(b,60)!=Crc(b,60) || U(b,56)!=1000)
                throw new InvalidDataException("Invalid acquisition anchor receipt.");
            var a=new NpAcquisitionAnchor {Flags=U(b,12),Generation=U(b,16),BeforeMs=U(b,20),AfterMs=U(b,24),
                BeforeCycles=U(b,28),AfterCycles=U(b,32),CounterHz=U(b,36),SourceHz=U(b,40),Origin=BitConverter.ToUInt64(b,44),SampleHz=U(b,52)};
            if((version!=1 && version!=2) || (a.Flags&~0xff1fU)!=0 || (a.Flags!=0 && (a.Flags>>8)!=(uint)version) ||
                ((a.Flags&1)!=0 && (a.Generation==0 || unchecked(a.AfterMs-a.BeforeMs)>=0x80000000U)) ||
                ((a.Flags&2)!=0 && a.CounterHz==0) || ((a.Flags&4)!=0 && (!a.SourceValid || a.SampleHz==0 || a.SourceHz!=(ulong)a.SampleHz*(version==1?780U:1600U))) ||
                (a.FirstSourceBracketValid && ((a.Flags&3)!=3 || a.CounterHz<1000000 || a.CounterHz>1000000000 ||
                    unchecked(a.AfterMs-a.BeforeMs)>1 || unchecked(a.AfterCycles-a.BeforeCycles)>a.CounterHz/10000)))
                throw new InvalidDataException("Invalid acquisition anchor domain, flags or rates.");
            return a;
        }
        internal static uint U(byte[] b,int at) { return BitConverter.ToUInt32(b,at); }
        internal static void Put(byte[] b,int at,uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4); }
        internal static uint Crc(byte[] b,int count) {
            uint c=UInt32.MaxValue;for(int i=0;i<count;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((c&1)==0?0:0xedb88320U);}return ~c;
        }
    }

    // Optional C0/7F read-only reply. HAL milliseconds select the DWT wrap;
    // they do not supply fractional phase or precision to the PC mapping.
    internal sealed class NpCycleClockReply
    {
        internal const uint Magic=0x5248504e;
        public uint Id,Flags,Domain,Generation,BeforeMs,AfterMs,BeforeCycles,AfterCycles,CounterHz,AnchorDomain;
        public string Uid;
        internal static NpCycleClockReply Parse(byte[] bytes,uint id,int family)
        {
            if(bytes==null||bytes.Length!=64||NpAcquisitionAnchor.U(bytes,0)!=Magic||NpAcquisitionAnchor.U(bytes,4)!=1||id==0||
                NpAcquisitionAnchor.U(bytes,8)!=id||NpAcquisitionAnchor.U(bytes,60)!=NpAcquisitionAnchor.Crc(bytes,60))
                throw new InvalidDataException("Invalid high-resolution clock receipt.");
            var p=new NpCycleClockReply {Id=id,Flags=NpAcquisitionAnchor.U(bytes,12),Domain=NpAcquisitionAnchor.U(bytes,16),Generation=NpAcquisitionAnchor.U(bytes,20),
                BeforeMs=NpAcquisitionAnchor.U(bytes,24),AfterMs=NpAcquisitionAnchor.U(bytes,28),BeforeCycles=NpAcquisitionAnchor.U(bytes,32),AfterCycles=NpAcquisitionAnchor.U(bytes,36),
                CounterHz=NpAcquisitionAnchor.U(bytes,40),AnchorDomain=NpAcquisitionAnchor.U(bytes,44),
                Uid=NpAcquisitionAnchor.U(bytes,48).ToString("X8")+NpAcquisitionAnchor.U(bytes,52).ToString("X8")+NpAcquisitionAnchor.U(bytes,56).ToString("X8")};
            if((family!=1&&family!=2)||(p.Flags&~0x307U)!=0||p.Flags!=0&&(p.Flags>>8)!=(uint)family||
                p.Uid==new string('0',24)||p.Uid==new string('F',24)||
                (p.Flags&1)!=0&&(p.Domain==0||p.CounterHz<1000000||p.CounterHz>1000000000||
                    unchecked(p.AfterMs-p.BeforeMs)>2||unchecked(p.AfterCycles-p.BeforeCycles)>p.CounterHz/200)||
                (p.Flags&2)!=0&&(p.Flags&1)==0|| (p.Flags&4)!=0&&(p.Domain==0||p.AnchorDomain!=p.Domain||p.Generation==0))
                throw new InvalidDataException("Invalid high-resolution clock domain or bounds.");
            return p;
        }
        internal static long Unwrap(NpCycleClockReply origin,uint milliseconds,uint cycles)
        {
            uint elapsed=unchecked(milliseconds-origin.BeforeMs);
            if(elapsed>=0x80000000U)throw new InvalidDataException("Ambiguous device clock epoch.");
            double expected=elapsed*(double)origin.CounterHz/1000;
            long raw=unchecked(cycles-origin.BeforeCycles);
            long value=raw+checked((long)Math.Round((expected-raw)/4294967296.0))*4294967296L;
            if(value<0||Math.Abs(value-expected)>origin.CounterHz*.003)
                throw new InvalidDataException("Counter wrap/reset or HAL/DWT clock conflict.");
            return value;
        }
    }

    internal sealed class NpSampleSegment {
        public long row_offset,row_count;public ulong first_source_tick;
        internal NpSampleSegment(long offset,long count,ulong tick){row_offset=offset;row_count=count;first_source_tick=tick;}
    }

    internal sealed class NpFirstSampleRow
    {
        internal int Probe;internal string Band;internal ulong? SourceTick;internal uint SampleHz;internal string Note;
        internal string DataFile;internal int Channels;internal long DataRows;internal uint RateNumerator,RateDenominator;
        internal List<NpSampleSegment> Segments;
        internal NpFirstSampleRow ForData(string path,int channels,long rows,uint numerator,uint denominator=1,IEnumerable<NpSampleSegment> segments=null) {
            DataFile=path;Channels=channels;DataRows=rows;RateNumerator=numerator;RateDenominator=denominator;
            Segments=segments==null?(SourceTick.HasValue?new List<NpSampleSegment>{new NpSampleSegment(0,rows,SourceTick.Value)}:null):new List<NpSampleSegment>(segments);return this;
        }
        internal NpFirstSampleRow(int probe,string band,ulong? tick,uint hz,string note=null) {Probe=probe;Band=band;SourceTick=tick;SampleHz=hz;Note=note;}
    }

    internal sealed class NpAcquisitionTiming
    {
        readonly string folder,identity,epoch=NpHostClock.Epoch;readonly int version;
        readonly double hostAnchorHalfWidthUs=NpHostClock.AnchorHalfWidthUs;
        readonly long hostUtc=NpHostClock.OriginUtcUs,hostTick=NpHostClock.OriginTicks;
        readonly List<ClockPoint> clock=new List<ClockPoint>();
        readonly List<CyclePoint> cycleClock=new List<CyclePoint>();
        readonly HashSet<uint> cycleIds=new HashSet<uint>();
        static int cycleSequence=BitConverter.ToInt32(Guid.NewGuid().ToByteArray(),0);
        bool cycleUnavailable;string cycleError,queryJournalError;
        NpAcquisitionAnchor anchor;byte[] raw;string error;
        sealed class ClockPoint {public uint DeviceMs;public long PcUs,SendPcUs,ReceivePcUs;public double UncertaintyUs;}
        internal sealed class CyclePoint {public bool AfterCapture,DuringCapture;public long SendPcUs,ReceivePcUs;public NpCycleClockReply Reply;public string RawHex;}
        internal NpAcquisitionTiming(string directory,string deviceIdentity,int deviceVersion) {folder=directory;identity=deviceIdentity;version=deviceVersion;}
        NpAcquisitionTiming(string directory,string deviceIdentity,int deviceVersion,string archivedEpoch,double halfWidth)
            :this(directory,deviceIdentity,deviceVersion) {epoch=archivedEpoch;hostAnchorHalfWidthUs=halfWidth;}
        long Now {get{return hostUtc+(long)((Stopwatch.GetTimestamp()-hostTick)*1000000.0/Stopwatch.Frequency);}}
        static string Failure(Exception ex){var native=ex as System.ComponentModel.Win32Exception;return ex.Message+(native==null?"":" (Win32 "+native.NativeErrorCode+")");}
        void RecordClockAttempt(long sendQpc,long receiveQpc,uint? tick,Exception failure)
        {
            try {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder,"acquisition_clock_queries.jsonl"),new JavaScriptSerializer().Serialize(new {
                    schema=1,kind="stopped_mcu_ms_query",device_identity=identity,logger_version=version,host_epoch=epoch,
                    host_send_qpc=sendQpc,host_receive_qpc=receiveQpc,host_send_pc_us=NpHostClock.UnixUs(sendQpc),host_receive_pc_us=NpHostClock.UnixUs(receiveQpc),
                    pc_clock=NpHostClock.Receipt,device_ms=tick,success=failure==null,
                    native_error=failure is System.ComponentModel.Win32Exception?(int?)((System.ComponentModel.Win32Exception)failure).NativeErrorCode:null,
                    error=failure==null?null:Failure(failure),scope="Existing stopped/control-only query; never issued during bulk. No arrival-time substitute."})+Environment.NewLine);
            }catch(Exception ex){queryJournalError=ex.Message;}
        }
        // The delegate must return a freshly sampled device tick, NOT a cached sensor publication.
        internal void Observe(Func<uint> freshTick) {
            long sendQpc=Stopwatch.GetTimestamp();uint? observed=null;Exception failure=null;
            try {long before=(long)NpHostClock.UnixUs(sendQpc);uint tick=freshTick();observed=tick;long after=Now;
                if(after-before>5000000)throw new IOException("Clock request exceeded observation limit.");
                clock.Add(new ClockPoint {DeviceMs=tick,PcUs=before+(after-before)/2,SendPcUs=before,ReceivePcUs=after,UncertaintyUs=(after-before)/2.0+1000+NpHostClock.AnchorHalfWidthUs});
            }catch(Exception ex){failure=ex;error=Failure(ex);}
            finally{RecordClockAttempt(sendQpc,Stopwatch.GetTimestamp(),observed,failure);}
        }
        internal void Read(Func<byte[]> query,uint requestId=0) {
            anchor=null;raw=null;
            try {raw=query();anchor=NpAcquisitionAnchor.Parse(raw,requestId,version);}
            catch(Exception ex){error=Failure(ex);}
        }
        internal void Accept(byte[] bytes,uint requestId) {Read(()=>bytes,requestId);}
        void RecordCycleClockAttempt(uint id,bool afterCapture,long sendQpc,long receiveQpc,byte[] bytes,Exception failure)
        {
            try {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder,"acquisition_clock_queries.jsonl"),new JavaScriptSerializer().Serialize(new {
                    schema=1,kind="stopped_mcu_cycle_query",request_id=id,after_capture=afterCapture,
                    device_identity=identity,logger_version=version,host_epoch=epoch,
                    host_send_qpc=sendQpc,host_receive_qpc=receiveQpc,pc_clock=NpHostClock.Receipt,
                    raw_hex=bytes==null?null:BitConverter.ToString(bytes).Replace("-",""),success=failure==null,
                    error=failure==null?null:Failure(failure),scope="Fresh stopped 0x7F query; original wire bytes and host brackets. Not a source-event arrival timestamp."})+Environment.NewLine);
            }catch(Exception ex){queryJournalError=ex.Message;}
        }
        internal void ObserveCycleClock(Func<uint,byte[]> query,bool afterCapture=false)
        {
            if(cycleUnavailable)return;
            for(int i=0;i<8;i++) {
                uint id;do{id=unchecked((uint)Interlocked.Increment(ref cycleSequence));}while(id==0||cycleIds.Contains(id));cycleIds.Add(id);
                long sendQpc=Stopwatch.GetTimestamp(),receiveQpc=0;byte[] bytes=null;Exception failure=null;
                try {
                bytes=query(id);receiveQpc=Stopwatch.GetTimestamp();
                long before=(long)Math.Floor(NpHostClock.UnixUs(sendQpc));
                long after=(long)Math.Ceiling(NpHostClock.UnixUs(receiveQpc));
                var reply=NpCycleClockReply.Parse(bytes,id,version);cycleIds.Add(reply.Domain);cycleIds.Add(reply.AnchorDomain);
                if((reply.Flags&3)!=3||identity.IndexOf(reply.Uid,StringComparison.OrdinalIgnoreCase)<0||after<before||after-before>5000000)
                    throw new InvalidDataException("Continuous device cycle clock unavailable or device identity changed.");
                cycleClock.Add(new CyclePoint {AfterCapture=afterCapture,SendPcUs=before,ReceivePcUs=after,Reply=reply,RawHex=BitConverter.ToString(bytes).Replace("-","")});
                }catch(Exception ex){failure=ex;cycleUnavailable=true;cycleError=Failure(ex);break;}
                finally{RecordCycleClockAttempt(id,afterCapture,sendQpc,receiveQpc==0?Stopwatch.GetTimestamp():receiveQpc,bytes,failure);}
            }
        }
        internal void AcceptCyclePoint(byte[] bytes,uint id,bool afterCapture,long sendPcUs,long receivePcUs)
        {cycleClock.Add(new CyclePoint {AfterCapture=afterCapture,SendPcUs=sendPcUs,ReceivePcUs=receivePcUs,Reply=NpCycleClockReply.Parse(bytes,id,version)});}
        static bool PrecedesRoot(NpCycleClockReply point,NpAcquisitionAnchor root)
        {
            try{return NpCycleClockReply.Unwrap(point,root.BeforeMs,root.BeforeCycles)>=NpCycleClockReply.Unwrap(point,point.AfterMs,point.AfterCycles);}
            catch(InvalidDataException){return false;}
        }
        bool ApplyCycleBounds(Dictionary<string,object> row,ulong sourceTick)
        {
            try {
                if(cycleUnavailable||anchor==null||!anchor.SourceValid||(anchor.Flags&2)==0)return false;
                var post=cycleClock.Where(p=>(p.AfterCapture||p.DuringCapture)&&(p.Reply.Flags&7)==7&&p.Reply.Generation==anchor.Generation)
                    .OrderBy(p=>p.ReceivePcUs-p.SendPcUs).FirstOrDefault();
                if(post==null)return false;
                var pre=cycleClock.Where(p=>!p.AfterCapture&&p.Reply.Domain==post.Reply.AnchorDomain&&
                    PrecedesRoot(p.Reply,anchor)).OrderBy(p=>p.ReceivePcUs-p.SendPcUs).FirstOrDefault();
                if(pre==null)return false;
                var root=pre.Reply;var end=post.Reply;
                if((root.Flags&3)!=3||end.Domain!=root.Domain||end.CounterHz!=root.CounterHz||end.Uid!=root.Uid||identity.IndexOf(root.Uid,StringComparison.OrdinalIgnoreCase)<0||
                    root.CounterHz!=anchor.CounterHz||(end.Flags&7)!=7||end.Generation!=anchor.Generation||end.AnchorDomain!=root.Domain||
                    pre.ReceivePcUs<pre.SendPcUs||post.ReceivePcUs<post.SendPcUs||post.SendPcUs<=pre.ReceivePcUs)
                    throw new InvalidDataException("High-resolution clock reset, generation, identity or frequency mismatch.");
                double preEnd=NpCycleClockReply.Unwrap(root,root.AfterMs,root.AfterCycles);
                double postStart=NpCycleClockReply.Unwrap(root,end.BeforeMs,end.BeforeCycles),postEnd=NpCycleClockReply.Unwrap(root,end.AfterMs,end.AfterCycles);
                double offset=(double)sourceTick*anchor.CounterHz/anchor.SourceHz;
                double first=NpCycleClockReply.Unwrap(root,anchor.BeforeMs,anchor.BeforeCycles)+offset;
                double last=NpCycleClockReply.Unwrap(root,anchor.AfterMs,anchor.AfterCycles)+offset;
                if(first<preEnd||last<first||last>postStart||postStart<=preEnd||postEnd<postStart)
                    throw new InvalidDataException("First source row lies outside fresh cycle-clock observations.");
                double lowFraction=(first-preEnd)/(postEnd-preEnd),highFraction=last/postStart;
                // Keep integral epoch microseconds through JSON/decimal conversion.
                // Round outward for the host microsecond coordinate and double arithmetic.
                const double coordinateGuardUs=2;
                row["pc_us_lower"]=checked((long)Math.Floor(pre.SendPcUs+lowFraction*(post.SendPcUs-pre.SendPcUs)-hostAnchorHalfWidthUs-coordinateGuardUs));
                row["pc_us_upper"]=checked((long)Math.Ceiling(pre.ReceivePcUs+highFraction*(post.ReceivePcUs-pre.ReceivePcUs)+hostAnchorHalfWidthUs+coordinateGuardUs));
                row["pc_coordinate_rounding_guard_us"]=coordinateGuardUs;
                row["pc_time_valid"]=true;row["pc_mapping"]="dwt_cycle_affine_interval";row["counter_domain"]=root.Domain;
                row["pc_interval_width_us"]=Convert.ToDouble(row["pc_us_upper"])-Convert.ToDouble(row["pc_us_lower"]);
                row["pre_clock_query_id"]=root.Id;row["post_clock_query_id"]=end.Id;
                row["cycle_us_per_tick"]=(post.SendPcUs+(post.ReceivePcUs-post.SendPcUs)/2.0-pre.SendPcUs-(pre.ReceivePcUs-pre.SendPcUs)/2.0)/((postStart+postEnd-preEnd)/2.0);
                row["clock_interval_scope"]="Positive affine DWT-to-PC clock between fresh pre/post queries; query/counter brackets propagated. HAL ms selects wraps only; no physical ADC/GPIO latency calibration.";
                return true;
            }catch(Exception ex){cycleError=ex.Message;return false;}
        }
        // The producer must separately qualify that exported tick zero has the
        // NPAR coordinate. This mapper supplies clock bounds, not that proof.
        internal Dictionary<string,object> SourceClockBounds(ulong exportedSourceTick)
        {
            if(anchor==null||!anchor.FirstSourceBracketValid)return null;
            ulong total;try{total=checked(anchor.Origin+exportedSourceTick);}catch(OverflowException){return null;}
            var row=new Dictionary<string,object>();if(!ApplyCycleBounds(row,total))return null;
            row["counter_hz"]=anchor.CounterHz;row["source_hz"]=anchor.SourceHz;row["sample_hz"]=anchor.SampleHz;
            row["generation"]=anchor.Generation;row["exported_source_tick"]=exportedSourceTick;row["anchor_origin"]=anchor.Origin;
            return row;
        }
        internal static Func<ulong,Dictionary<string,object>> ArchivedSourceClockBounds(string directory,string deviceIdentity,int family)
        {
            try {
                var h=NpUsbTimingEvidence.Read(directory);if(h==null||h.Anchor==null||h.Family!=family)return null;
                var timing=new NpAcquisitionTiming(directory,deviceIdentity,family,h.Epoch,h.HalfWidth);
                timing.LoadArchivedCyclePoints(h);timing.LoadStreamEvidence(h,false);
                return timing.SourceClockBounds;
            }catch{return null;}
        }
        void LoadArchivedCyclePoints(NpUsbTimingHistory h)
        {
            string path=Path.Combine(folder,"acquisition_clock_queries.jsonl");if(!File.Exists(path))return;
            var json=new JavaScriptSerializer();var used=new HashSet<uint>();
            using(var input=new StreamReader(path)) {
                string line;while((line=input.ReadLine())!=null) {
                    if(line.Length>32768)throw new InvalidDataException("Oversized saved cycle-clock observation.");
                    if(input.EndOfStream&&!line.TrimEnd().EndsWith("}",StringComparison.Ordinal))break;
                    var d=json.Deserialize<Dictionary<string,object>>(line);object kind;
                    if(!d.TryGetValue("kind",out kind)||Convert.ToString(kind)!="stopped_mcu_cycle_query")continue;
                    if(Convert.ToInt32(d["schema"])!=1||Convert.ToString(d["host_epoch"])!=h.Epoch||Convert.ToInt32(d["logger_version"])!=version||
                        Convert.ToString(d["device_identity"]).IndexOf(h.Uid,StringComparison.OrdinalIgnoreCase)<0)
                        throw new InvalidDataException("Saved cycle-clock identity or epoch mismatch.");
                    var c=(Dictionary<string,object>)d["pc_clock"];
                    if(Convert.ToString(c["host_epoch"])!=h.Epoch||Convert.ToInt64(c["origin_qpc"])!=h.OriginQpc||
                        Convert.ToDouble(c["origin_pc_unix_us"])!=h.OriginPcUs||Convert.ToDouble(c["qpc_frequency"])!=h.QpcHz||
                        Convert.ToDouble(c["anchor_half_width_us"])!=h.HalfWidth)
                        throw new InvalidDataException("Saved cycle-clock host coordinate mismatch.");
                    if(!Convert.ToBoolean(d["success"]))continue;
                    uint id=Convert.ToUInt32(d["request_id"]);if(!used.Add(id))throw new InvalidDataException("Repeated saved cycle-clock query.");
                    long send=Convert.ToInt64(d["host_send_qpc"]),receive=Convert.ToInt64(d["host_receive_qpc"]);
                    if(send<=0||receive<send||h.PcUs(receive)-h.PcUs(send)>5000000)throw new InvalidDataException("Invalid saved cycle-clock host bracket.");
                    string hex=Convert.ToString(d["raw_hex"]);if(hex.Length!=128)throw new InvalidDataException("Invalid saved cycle-clock bytes.");
                    var bytes=new byte[64];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(hex.Substring(i*2,2),16);
                    var reply=NpCycleClockReply.Parse(bytes,id,version);
                    if(!String.Equals(reply.Uid,h.Uid,StringComparison.OrdinalIgnoreCase)||(reply.Flags&3)!=3)throw new InvalidDataException("Saved cycle-clock reply is unqualified.");
                    cycleClock.Add(new CyclePoint {AfterCapture=Convert.ToBoolean(d["after_capture"]),SendPcUs=(long)Math.Floor(h.PcUs(send)),
                        ReceivePcUs=(long)Math.Ceiling(h.PcUs(receive)),Reply=reply,RawHex=hex});
                }
            }
        }
        internal static ulong V2FirstRowTick(int phase) {
            if(phase<0 || phase>=1600)throw new ArgumentOutOfRangeException("phase");return (ulong)((1600-phase)%1600);
        }
        static object RecordingStart(IList<Dictionary<string,object>> rows,string hostEpoch)
        {
            var first=rows.Where(r=>Convert.ToString(r["band"])=="ap"&&Convert.ToInt64(r["dat_rows"])>0)
                .OrderBy(r=>r["decoder_first_row_source_tick"]==null?UInt64.MaxValue:Convert.ToUInt64(r["decoder_first_row_source_tick"]))
                .ThenBy(r=>Convert.ToInt32(r["probe"])).FirstOrDefault();
            bool valid=first!=null&&(bool)first["pc_time_valid"];
            double? low=valid?(double?)(Convert.ToDouble(first["pc_us_lower"])/1000000):null;
            double? high=valid?(double?)(Convert.ToDouble(first["pc_us_upper"])/1000000):null;
            return new {reference="earliest_retained_ap_row_on_decoder_source_clock",reference_probe=first==null?(int?)null:Convert.ToInt32(first["probe"]),
                data_file=first==null?null:first["data_file"],sample_index=first==null?(int?)null:0,host_epoch=hostEpoch,
                pc_time_valid=valid,pc_unix_seconds=valid?(low+high)/2:null,pc_lower_unix_seconds=low,pc_upper_unix_seconds=high,
                hardware_start_locked=false,physical_adc_time_calibrated=false,
                unavailable_reason=valid?null:first==null?"No retained AP row.":"No qualified first-row PC mapping; Start click and USB arrival are not substituted.",
                scope="Digital first retained row with per-probe source offsets; does not establish simultaneous starts of independent streams or physical ADC/filter phase."};
        }
        NpUsbTimingHistory LoadStreamEvidence(NpUsbTimingHistory history=null,bool archiveStart=true)
        {
            var h=history??NpUsbTimingEvidence.Read(folder);if(h==null)return null;
            if(h.Epoch!=epoch||h.Family!=version||identity.IndexOf(h.Uid,StringComparison.OrdinalIgnoreCase)<0)throw new InvalidDataException("Stream timing identity/host epoch mismatch.");
            if(h.Anchor!=null){anchor=NpAcquisitionAnchor.Parse(h.Anchor,BitConverter.ToUInt32(h.Anchor,8),version);raw=h.Anchor;}
            var points=h.Points.Where(p=>p.Segment==0).ToList();
            if(points.Count>=2) {
                clock.Clear();foreach(var p in points)clock.Add(new ClockPoint {DeviceMs=p.Record.SealMs,SendPcUs=(long)Math.Floor(h.PcUs(p.SendQpc)),ReceivePcUs=(long)Math.Ceiling(h.PcUs(p.ReceiveQpc)),
                    PcUs=(long)((h.PcUs(p.SendQpc)+h.PcUs(p.ReceiveQpc))/2),UncertaintyUs=(h.PcUs(p.ReceiveQpc)-h.PcUs(p.SendQpc))/2+1000+h.HalfWidth});
            }
            // Millisecond-only or partial R2 evidence must not erase the
            // independently qualified stopped 0x7F observations. Replace the
            // fine set only with a complete, matching in-capture clock domain.
            if(anchor!=null&&anchor.SourceValid&&(anchor.Flags&2)!=0&&h.Generation==anchor.Generation){
                // The reader binds the earlier prearm generation to this same
                // token's startup record; active points must match the anchor.
                var fine=points.Where(p=>p.Record.Fine&&p.Record.Hz==anchor.CounterHz&&String.Equals(p.Record.Uid,h.Uid,StringComparison.OrdinalIgnoreCase))
                    .Select(p=>new CyclePoint {DuringCapture=!p.Record.Prearm,SendPcUs=(long)Math.Floor(h.PcUs(p.SendQpc)),ReceivePcUs=(long)Math.Ceiling(h.PcUs(p.ReceiveQpc)),RawHex=BitConverter.ToString(p.Record.Body).Replace("-",""),
                        Reply=new NpCycleClockReply {Id=p.Record.Id,Flags=(uint)(version<<8)|3U|(p.Record.RootDomain==p.Record.Domain&&p.Record.Generation!=0?4U:0U),Domain=p.Record.Domain,Generation=p.Record.Generation,
                            BeforeMs=p.Record.RxMs,AfterMs=p.Record.SealMs,BeforeCycles=p.Record.RxCycles,AfterCycles=p.Record.SealCycles,CounterHz=p.Record.Hz,AnchorDomain=p.Record.RootDomain,Uid=p.Record.Uid}}).ToList();
                var post=fine.Where(p=>p.DuringCapture&&(p.Reply.Flags&7)==7&&p.Reply.Generation==anchor.Generation).OrderBy(p=>p.ReceivePcUs-p.SendPcUs).FirstOrDefault();
                var pre=post==null?null:fine.Where(p=>!p.DuringCapture&&p.Reply.Domain==post.Reply.AnchorDomain&&PrecedesRoot(p.Reply,anchor))
                    .OrderBy(p=>p.ReceivePcUs-p.SendPcUs).FirstOrDefault();
                bool qualified=false;
                if(pre!=null&&post.SendPcUs>pre.ReceivePcUs)try{
                    double preEnd=NpCycleClockReply.Unwrap(pre.Reply,pre.Reply.AfterMs,pre.Reply.AfterCycles);
                    double first=NpCycleClockReply.Unwrap(pre.Reply,anchor.BeforeMs,anchor.BeforeCycles),last=NpCycleClockReply.Unwrap(pre.Reply,anchor.AfterMs,anchor.AfterCycles);
                    double postStart=NpCycleClockReply.Unwrap(pre.Reply,post.Reply.BeforeMs,post.Reply.BeforeCycles),postEnd=NpCycleClockReply.Unwrap(pre.Reply,post.Reply.AfterMs,post.Reply.AfterCycles);
                    qualified=first>=preEnd&&last>=first&&last<=postStart&&postStart>preEnd&&postEnd>=postStart;
                }catch(InvalidDataException){ /* Retain independent evidence; do not rehabilitate a reset domain. */ }
                if(qualified){
                    cycleClock.Clear();cycleClock.AddRange(fine.Where(p=>p.Reply.Domain==post.Reply.Domain&&
                        (!p.DuringCapture||p.Reply.Generation==anchor.Generation&&p.Reply.AnchorDomain==post.Reply.Domain)));
                    cycleUnavailable=false;
                }
            }
            if(archiveStart&&h.StartCycles!=null)File.WriteAllBytes(Path.Combine(folder,"acquisition_cycle_start.bin"),h.StartCycles);
            return h;
        }
        internal void Save(IEnumerable<NpFirstSampleRow> rows) {
            try {
#if NP_FIRST_SYNC_ENABLE
                var expectedFirstSyncAnchor=anchor;
#endif
                NpUsbTimingHistory stream=null;string streamError=null;try{stream=LoadStreamEvidence();}catch(Exception ex){streamError=ex.Message;}
                Directory.CreateDirectory(folder);if(raw!=null)File.WriteAllBytes(Path.Combine(folder,"acquisition_anchor.bin"),raw);
                var mappedRows=BuildRows(rows);
                var report=new Dictionary<string,object> {{"schema",1},{"device_identity",identity},{"logger_version",version},{"host_epoch",epoch},
                    {"recording_folder",Path.GetFullPath(folder)},{"raw_receipt_file",raw==null?null:"acquisition_anchor.bin"},{"optional_error",error},
                    {"pc_reference","PC wall clock at host epoch, advanced with Stopwatch; no network correction"},
                    {"limitation","Software receiver-enable/source-row timing; not analog conversion latency. Clock precision is identified by each row's pc_mapping. Two endpoint PC mapping assumes linear device clock between stopped observations."},
                    {"pc_clock",NpHostClock.Receipt},{"clock_observations",clock},{"cycle_clock_observations",cycleClock},{"rows",mappedRows},
                    {"recording_start",RecordingStart(mappedRows,epoch)}};
                report["cycle_clock_error"]=cycleError;
                report["clock_query_journal"]="acquisition_clock_queries.jsonl";report["clock_query_journal_error"]=queryJournalError;
                report["usb_timing_evidence"]=stream==null?null:NpUsbTimingEvidence.FileName;report["usb_timing_error"]=streamError??(stream==null?null:stream.Error);
                report["usb_counter_lifetime_token"]=stream==null?null:stream.Token.ToString("X16");
                if(anchor!=null)report["anchor"]=new Dictionary<string,object>{{"flags",anchor.Flags},{"generation",anchor.Generation},{"before_ms",anchor.BeforeMs},{"after_ms",anchor.AfterMs},
                    {"before_cycles",anchor.BeforeCycles},{"after_cycles",anchor.AfterCycles},{"counter_hz",anchor.CounterHz},{"source_hz",anchor.SourceHz},{"sample_hz",anchor.SampleHz},
                    {"exported_source_origin",anchor.Origin},{"source_valid",anchor.SourceValid},{"first_source_bracket_valid",anchor.FirstSourceBracketValid},{"epoch_quantization_us",1000}};
                File.WriteAllText(Path.Combine(folder,"first_sample_timestamps.json"),new JavaScriptSerializer().Serialize(report));
#if NP_FIRST_SYNC_ENABLE
                if(NpFirstSyncUsbUtc.TryWrite(folder,identity,version,epoch,expectedFirstSyncAnchor??anchor,stream,mappedRows,SourceClockBounds))return;
#endif
                NpRecordingUtc.Write(folder,NpRecordingUtc.FromNpRows(mappedRows,version,identity,null,
                    anchor==null?(uint?)null:anchor.Generation,"first_sample_timestamps.json; validated acquisition anchor plus decoded source rows"));
            }catch { /* Optional timing must never change recording/export completion. */ }
        }
        List<Dictionary<string,object>> BuildRows(IEnumerable<NpFirstSampleRow> rows) {
            var result=new List<Dictionary<string,object>>();
            foreach(var row in rows) {
                var item=new Dictionary<string,object>{{"probe",row.Probe},{"band",row.Band},{"decoder_first_row_source_tick",row.SourceTick},
                    {"row_reference",version==1?"NP1 superframe start; AP channel serialization is not an analog conversion timestamp":"NP2 frame-0 source tick"},
                    {"note",row.Note},{"device_time_valid",false},{"pc_time_valid",false}};
                AddData(item,row,version);result.Add(item);
                if(anchor==null || !anchor.SourceValid || !row.SourceTick.HasValue || row.SampleHz!=anchor.SampleHz)continue;
                ulong total;try{total=checked(anchor.Origin+row.SourceTick.Value);}catch(OverflowException){continue;}
                decimal offset=total*1000000m/anchor.SourceHz;
                decimal lower=anchor.BeforeMs*1000m+offset,upper=(anchor.BeforeMs+(ulong)unchecked(anchor.AfterMs-anchor.BeforeMs))*1000m+offset+1000;
                item["device_time_valid"]=true;item["physical_source_tick"]=total;item["device_us_lower"]=Math.Floor(lower);item["device_us_upper"]=Math.Ceiling(upper);
                if(ApplyCycleBounds(item,total))continue;
                if(clock.Count<2)continue;
                ClockPoint first=clock[0],last=clock[clock.Count-1];uint span=unchecked(last.DeviceMs-first.DeviceMs),root=unchecked(anchor.BeforeMs-first.DeviceMs);
                if(span==0 || span>=0x80000000U || root>span)continue;
                double slope=(last.PcUs-first.PcUs)/(double)span;
                double position=root+(double)offset/1000.0;
                if(slope<900 || slope>1100 || position>span)continue;
                double uncertainty=Math.Max(first.UncertaintyUs,last.UncertaintyUs)+1000;
                item["pc_time_valid"]=true;item["pc_us_lower"]=checked((long)Math.Floor(first.PcUs+position*slope-uncertainty));
                item["pc_us_upper"]=checked((long)Math.Ceiling(first.PcUs+(position+unchecked(anchor.AfterMs-anchor.BeforeMs)+1)*slope+uncertainty));
                item["device_ms_to_pc_us_slope"]=slope;
                item["pc_mapping"]="hal_millisecond_affine_interval";
            }
            return result;
        }

        static void AddData(Dictionary<string,object> item,NpFirstSampleRow row,int version) {
            item["data_file"]=row.DataFile;item["channels"]=row.Channels;item["dat_rows"]=row.DataRows;
            item["rate_numerator"]=row.RateNumerator;item["rate_denominator"]=row.RateDenominator;
            item["source_hz"]=(ulong)row.SampleHz*(version==1?780U:1600U);item["segments"]=row.Segments;
            item["bound_scope"]="Conditional software source epoch; excludes unmeasured receiver acceptance and probe ADC/filter/serialization delay, clock nonlinearity and physical GPIO latency.";
        }

        internal static bool SdClockBounds(byte[] sector,uint session,int version,uint sampleHz,out ulong lo,out ulong hi,out uint uncertainty)
        {
                bool valid=(version==1 || version==2) && sector!=null && sector.Length==512 && System.Text.Encoding.ASCII.GetString(sector,0,8)==(version==1?"N1SDUTC2":"N2SDUTC2") &&
                    NpAcquisitionAnchor.U(sector,8)==2 && NpAcquisitionAnchor.U(sector,12)==session &&
                    NpAcquisitionAnchor.U(sector,508)==NpAcquisitionAnchor.Crc(sector,508) && NpAcquisitionAnchor.U(sector,72)==sampleHz &&
                    NpAcquisitionAnchor.U(sector,76)==1000;
                uint flags=valid?NpAcquisitionAnchor.U(sector,16):0;
                valid=valid && (flags&~(version==2?15U:7U))==0 && (flags&3)==3 && NpAcquisitionAnchor.U(sector,20)!=0;
                int reserved=version==2 && (flags&8U)!=0?112:80;
                if(valid && version==1){
                    try{
                        var cl=WildNpClosedLoop.NpClosedLoopCarrier.ReadV1UtcConfiguration(sector);
                        if(cl!=null){valid=cl.ApHz==sampleHz;reserved=508;}
                    }catch(InvalidDataException){valid=false;}
                }
                if(valid)for(int i=reserved;i<508;i++)if(sector[i]!=0){valid=false;break;}
                ulong utc=valid?BitConverter.ToUInt64(sector,32):0;
                uncertainty=valid?NpAcquisitionAnchor.U(sector,24):0;
                valid=valid && utc>=1577836800000000UL && utc<=4102444800000000UL && uncertainty>=1000 && uncertainty<=5000000;
                uint before=valid?NpAcquisitionAnchor.U(sector,version==1?64:48):0,after=valid?NpAcquisitionAnchor.U(sector,version==1?68:52):0;
                lo=valid?BitConverter.ToUInt64(sector,version==1?48:56):0;hi=valid?BitConverter.ToUInt64(sector,version==1?56:64):0;
                uint dt=valid?unchecked(before-NpAcquisitionAnchor.U(sector,28)):0,span=unchecked(after-before);
                valid=valid && dt<0x80000000U && span<0x80000000U && (ulong)dt+span<0x80000000UL &&
                    lo==utc+(ulong)dt*1000 && hi==lo+(ulong)span*1000;
                return valid;
        }
        // UTC1 is historical ARM timing. Only self-describing UTC2 attests exported source tick zero.
        internal static void SaveSd(string folder,byte[] sector,uint session,int version,uint sampleHz,IEnumerable<NpFirstSampleRow> rows
#if NP_FIRST_SYNC_ENABLE
            ,NpFirstSyncSdUtcContext firstSyncContext=null
#endif
            ) {
            try {
                // Preserve original evidence; only the opt-in adapter may
                // normalize its separately validated private clock copy.
                if(sector!=null && sector.Length==512)File.WriteAllBytes(Path.Combine(folder,"recorded_clock.bin"),sector);
                byte[] clockSector=sector;
#if NP_FIRST_SYNC_ENABLE
                var firstSync=NpFirstSyncSdUtc.Read(sector,session,version,sampleHz,firstSyncContext);
                if(firstSync!=null&&firstSync.ClockSector!=null)clockSector=firstSync.ClockSector;
#endif
                ulong lo,hi;uint uncertainty;bool valid=SdClockBounds(clockSector,session,version,sampleHz,out lo,out hi,out uncertainty);
                var output=new List<Dictionary<string,object>>();
                foreach(var row in rows){var item=new Dictionary<string,object>{{"probe",row.Probe},{"band",row.Band},{"decoder_first_row_source_tick",row.SourceTick},
                        {"pc_time_valid",valid && row.SourceTick.HasValue && row.SampleHz==sampleHz},{"note",row.Note}};AddData(item,row,version);output.Add(item);
                    if(!(bool)item["pc_time_valid"])continue;
                    decimal offset=row.SourceTick.Value*1000000m/(sampleHz*(version==1?780m:1600m));
                    item["pc_us_lower"]=Math.Floor(lo+offset-uncertainty);item["pc_us_upper"]=Math.Ceiling(hi+offset+uncertainty+1000);
                }
                File.WriteAllText(Path.Combine(folder,"first_sample_timestamps.json"),new JavaScriptSerializer().Serialize(new {schema=1,session_id=session,logger_version=version,
                    reference="Recorded PC clock mapping; network correction separate",raw_receipt_file=sector!=null && sector.Length==512?"recorded_clock.bin":null,source=valid?"SDUTC2 exported source origin":"unavailable; legacy or invalid optional timestamp",
                    limitation="Software source-row timing, not analog conversion latency; nominal source rate, millisecond epoch",rows=output,
                    recording_start=RecordingStart(output,null)}));
#if NP_FIRST_SYNC_ENABLE
                if(firstSync!=null){NpFirstSyncSdUtc.Write(folder,firstSync,output,session,version,sampleHz,valid,lo,hi,uncertainty,firstSyncContext);return;}
#endif
                NpRecordingUtc.Write(folder,NpRecordingUtc.FromNpRows(output,version,null,session,null,
                    valid?"recorded_clock.bin (legacy UTC2 source origin) plus decoded source rows":"Legacy/invalid clock evidence; NP start UTC unavailable"));
            }catch { }
        }
    }
}
