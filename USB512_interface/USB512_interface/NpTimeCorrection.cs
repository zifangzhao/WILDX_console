using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // Optional evidence writer, called by decoder workers, never by USB read/rearm.
    internal sealed class NpTimeJournal : IDisposable
    {
        readonly object sync=new object();
        readonly string folder,session=Guid.NewGuid().ToString("N");
        readonly long originTick,originUtc;
        readonly double anchorBracketUs;
        readonly string clockApi;
        readonly Dictionary<string,NpTimeCorrection.Observation> latest=new Dictionary<string,NpTimeCorrection.Observation>();
        readonly Dictionary<string,long> written=new Dictionary<string,long>();
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        StreamWriter writer;bool disposed;
        internal string Error;
        [DllImport("kernel32.dll")]static extern void GetSystemTimePreciseAsFileTime(out long value);
        internal NpTimeJournal(string folder){
            this.folder=Path.GetFullPath(folder);originTick=NpHostClock.OriginTicks;originUtc=NpHostClock.OriginUtcUs;
            anchorBracketUs=NpHostClock.AnchorHalfWidthUs;clockApi=NpHostClock.Api;
        }
        internal void Observe(string file,int channels,int rateNumerator,int rateDenominator,long rows,long receiveTicks,DateTime receiveUtc)
        {
            if(rows<=0||receiveTicks<=0||receiveUtc.Kind!=DateTimeKind.Utc)return;
            lock(sync){
                if(disposed||Error!=null)return;
                try{
                    string path=Path.GetFullPath(file),prefix=folder.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Timing stream leaves its recording folder.");
                    string relative=path.Substring(prefix.Length).Replace('\\','/');
                    var point=new NpTimeCorrection.Observation{schema_version=1,session=session,host_epoch=NpHostClock.Epoch,stream=relative,channels=channels,
                        rate_numerator=rateNumerator,rate_denominator=rateDenominator,sample_index=rows-1,
                        host_monotonic_us=(receiveTicks-originTick)*1000000.0/Stopwatch.Frequency,
                        host_utc_us=originUtc+(receiveTicks-originTick)*1000000.0/Stopwatch.Frequency,
                        observed_wall_utc_us=(receiveUtc.Ticks-621355968000000000L)/10.0,receive_ticks=receiveTicks,reference_clock="pc_monotonic_anchor",pc_clock_api=clockApi,pc_anchor_bracket_us=anchorBracketUs,kind="usb_arrival"};
                    NpTimeCorrection.Observation previous;
                    if(latest.TryGetValue(relative,out previous)&&(point.sample_index<=previous.sample_index||point.host_monotonic_us<previous.host_monotonic_us))return;
                    latest[relative]=point;long last;
                    if(!written.TryGetValue(relative,out last)||receiveTicks-last>=Stopwatch.Frequency/4){Write(point);written[relative]=receiveTicks;}
                }catch(Exception ex){Error=ex.Message;}
            }
        }
        void Write(NpTimeCorrection.Observation point)
        {
            if(writer==null)writer=new StreamWriter(new FileStream(Path.Combine(folder,NpTimeCorrection.InputName),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536),new UTF8Encoding(false));
            writer.WriteLine(json.Serialize(point));
        }
        public void Dispose()
        {
            lock(sync){
                if(disposed)return;disposed=true;
                try{if(writer!=null)foreach(var point in latest.Values)Write(point);}catch(Exception ex){Error=ex.Message;}
                finally{try{if(writer!=null)writer.Dispose();}catch(Exception ex){Error=ex.Message;}}
            }
        }
        internal void Finish()
        {
            Dispose();
            NpTimeCorrection.Automatic(folder,Error);
        }
    }

    internal static class NpTimeCorrection
    {
        internal const string InputName="time_observations.jsonl",ReportName="timestamp_calibration.json";
        const double MinimumDriftSpanSeconds=60,MaximumExtrapolationSeconds=2;
        internal sealed class Observation
        {
            public int schema_version,channels,rate_numerator,rate_denominator;
            public string session,host_epoch,stream,kind,reference_clock,pc_clock_api;
            public long sample_index,receive_ticks;
            public double? reference_uncertainty_us=null;
            public double host_monotonic_us,host_utc_us,observed_wall_utc_us;
            public double pc_anchor_bracket_us;
        }
        internal sealed class Model
        {
            public string session,kind,status,reference_clock,pc_clock_api,host_epoch,epoch_basis;
            public double? wall_offset_first_us,wall_offset_last_us,max_adjacent_wall_change_us;
            public double pc_anchor_bracket_us;
            public double? reference_uncertainty_us;
            public long first_sample,last_sample;
            public int observations,used,rejected;
            public double nominal_us_per_sample,us_per_sample,reference_utc_us,drift_ppm,rms_residual_us,max_residual_us,statistical_95_us,span_seconds;
            public bool drift_estimated,within_assumed_5ppm;
            internal double At(long sample){return reference_utc_us+(sample-first_sample)*us_per_sample;}
        }
        // Descriptors come from the decoder/exporter, not inferred from filenames or nominal analog timing.
#pragma warning disable 0649
        internal sealed class AnchorSegment {public long row_offset,row_count;public ulong first_source_tick;}
        internal sealed class SampleAnchor {
            public string data_file,band;public int probe,channels;public long dat_rows;
            public uint rate_numerator,rate_denominator;public ulong source_hz;
            public bool pc_time_valid;public double? pc_us_lower,pc_us_upper;
            public ulong? decoder_first_row_source_tick;public AnchorSegment[] segments;
        }
        sealed class AnchorReport {public int schema;public string host_epoch;public SampleAnchor[] rows;}
#pragma warning restore 0649
        static SampleAnchor[] ReadAnchors(string folder,out string error,out string sourceHash,out string hostEpoch) {
            error=null;sourceHash=null;hostEpoch=null;string path=Path.Combine(folder,"first_sample_timestamps.json");
            if(!File.Exists(path))return new SampleAnchor[0];
            try {
                if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Oversized first-row metadata.");
                sourceHash=Hash(path);var report=Json.Deserialize<AnchorReport>(File.ReadAllText(path));
                if(report==null || report.schema!=1 || report.rows==null)throw new InvalidDataException("Unsupported first-row metadata.");
                hostEpoch=report.host_epoch;return report.rows.Where(a=>a!=null && !String.IsNullOrWhiteSpace(a.data_file)).ToArray();
            }catch(Exception ex){error=ex.Message;return new SampleAnchor[0];}
        }
        static bool ValidAnchor(SampleAnchor a,long rows,int channels,int numerator,int denominator) {
            if(!a.pc_time_valid || !a.pc_us_lower.HasValue || !a.pc_us_upper.HasValue ||
                !Finite(a.pc_us_lower.Value) || !Finite(a.pc_us_upper.Value) || a.pc_us_lower<1577836800000000 || a.pc_us_upper>4102444800000000 ||
                a.pc_us_upper<a.pc_us_lower || a.pc_us_upper-a.pc_us_lower>10000000 || a.dat_rows!=rows || a.channels!=channels ||
                a.rate_numerator!=numerator || a.rate_denominator!=denominator || a.source_hz==0 || !a.decoder_first_row_source_tick.HasValue || a.segments==null)return false;
            long next=0;double lastTick=-1;
            foreach(var s in a.segments) {
                if(s==null || s.row_offset!=next || s.row_count<0 || s.row_count>rows-next || s.first_source_tick<lastTick)return false;
                next+=s.row_count;lastTick=s.first_source_tick+s.row_count*(double)a.source_hz*denominator/numerator;
            }
            return next==rows && a.segments.Length>0 && a.segments[0].first_source_tick==a.decoder_first_row_source_tick;
        }
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
        static bool Finite(double x){return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
        static double Median(IEnumerable<double> values){var a=values.OrderBy(x=>x).ToArray();return a.Length%2==0?(a[a.Length/2-1]+a[a.Length/2])/2:a[a.Length/2];}
        static string Hash(string path){using(var input=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","");}
        static void Validate(Observation p)
        {
            if(p==null||p.schema_version!=1||String.IsNullOrWhiteSpace(p.session)||String.IsNullOrWhiteSpace(p.stream)||
               p.channels<1||p.channels>1152||p.rate_numerator<1||p.rate_denominator<1||p.sample_index<0||
               !Finite(p.host_monotonic_us)||!Finite(p.host_utc_us)||!Finite(p.observed_wall_utc_us)||p.host_utc_us<=0||!Finite(p.pc_anchor_bracket_us)||p.pc_anchor_bracket_us<0||
               p.reference_uncertainty_us.HasValue&&(!Finite(p.reference_uncertainty_us.Value)||p.reference_uncertainty_us.Value<0)||
               (p.kind!="usb_arrival"&&p.kind!="reference"))throw new InvalidDataException("Invalid timing observation.");
        }
        static bool Anchored(Observation p){return p.reference_clock=="pc_monotonic_anchor"&&!String.IsNullOrWhiteSpace(p.host_epoch);}
        static List<List<Observation>> Epochs(IList<Observation> points)
        {
            var result=new List<List<Observation>>();List<Observation> group=null;Observation last=null;double wallOffset=0,anchorOffset=0;
            foreach(var p in points){
                Validate(p);
                bool same=last!=null&&p.session==last.session&&p.host_epoch==last.host_epoch&&p.kind==last.kind&&p.reference_clock==last.reference_clock&&
                    p.channels==last.channels&&p.rate_numerator==last.rate_numerator&&p.rate_denominator==last.rate_denominator;
                if(same&&p.host_monotonic_us==last.host_monotonic_us&&p.host_utc_us==last.host_utc_us&&p.sample_index>=last.sample_index)continue; // Final decoder flush reuses a receive observation.
                double offset=p.kind=="reference"?0:p.observed_wall_utc_us-p.host_utc_us;
                // This coordinate is QPC advanced from one immutable PC anchor.
                // Live wall-clock slewing/steps are diagnostics, not sample gaps.
                bool clockBreak=Anchored(p)?Math.Abs(p.host_utc_us-p.host_monotonic_us-anchorOffset)>2:Math.Abs(offset-wallOffset)>20000;
                bool split=!same||p.sample_index<=last.sample_index||p.host_monotonic_us<=last.host_monotonic_us||clockBreak;
                if(split){group=new List<Observation>();result.Add(group);wallOffset=offset;anchorOffset=p.host_utc_us-p.host_monotonic_us;}
                group.Add(p);last=p;
            }
            return result;
        }
        internal static Model Fit(IList<Observation> points,double latencyUs)
        {
            if(!Finite(latencyUs)||latencyUs<0)throw new ArgumentOutOfRangeException("latencyUs");
            var first=points[0];var m=new Model{session=first.session,host_epoch=first.host_epoch,epoch_basis=Anchored(first)?"anchored_qpc":"legacy_wall_offset",kind=first.kind,reference_clock=first.reference_clock,pc_clock_api=first.pc_clock_api,pc_anchor_bracket_us=points.Max(p=>p.pc_anchor_bracket_us),first_sample=first.sample_index,last_sample=points[points.Count-1].sample_index,observations=points.Count,status="unavailable"};
            if(first.kind=="usb_arrival") {
                m.wall_offset_first_us=first.observed_wall_utc_us-first.host_utc_us;
                m.wall_offset_last_us=points[points.Count-1].observed_wall_utc_us-points[points.Count-1].host_utc_us;
                m.max_adjacent_wall_change_us=points.Skip(1).Select((p,i)=>Math.Abs((p.observed_wall_utc_us-p.host_utc_us)-(points[i].observed_wall_utc_us-points[i].host_utc_us))).DefaultIfEmpty(0).Max();
            }
            if(points.All(p=>p.reference_uncertainty_us.HasValue))m.reference_uncertainty_us=points.Max(p=>p.reference_uncertainty_us.Value);
            m.nominal_us_per_sample=1000000.0*first.rate_denominator/first.rate_numerator;
            m.span_seconds=(m.last_sample-m.first_sample)*m.nominal_us_per_sample/1e6;
            if(points.Count<3||m.span_seconds<1)return m;
            if(points.Any(p=>p.rate_numerator!=first.rate_numerator||p.rate_denominator!=first.rate_denominator||p.channels!=first.channels))throw new InvalidDataException("Recording rate/channel layout changed inside a timing epoch.");
            m.drift_estimated=m.span_seconds>=MinimumDriftSpanSeconds;
            double baseUtc=first.host_utc_us,intercept=0,slope=m.nominal_us_per_sample;
            var keep=Enumerable.Repeat(true,points.Count).ToArray();
            for(int pass=0;pass<4;pass++){
                double meanX=0,meanY=0;int count=0;
                for(int i=0;i<points.Count;i++)if(keep[i]){meanX+=points[i].sample_index-m.first_sample;meanY+=points[i].host_utc_us-baseUtc;count++;}
                if(count<3)return m;meanX/=count;meanY/=count;double xx=0,xy=0;
                for(int i=0;i<points.Count;i++)if(keep[i]){double x=points[i].sample_index-m.first_sample-meanX;xx+=x*x;xy+=x*(points[i].host_utc_us-baseUtc-meanY);}
                slope=m.drift_estimated?xy/xx:m.nominal_us_per_sample;intercept=meanY-slope*meanX;
                if(!Finite(slope)||slope<=0||Math.Abs(slope/m.nominal_us_per_sample-1)>0.001){m.status="clock_rate_or_epoch_conflict";return m;}
                var residuals=points.Select(p=>(p.host_utc_us-baseUtc)-(intercept+slope*(p.sample_index-m.first_sample))).ToArray();
                double median=Median(residuals.Where((x,i)=>keep[i])),mad=Median(residuals.Where((x,i)=>keep[i]).Select(x=>Math.Abs(x-median))),gate=Math.Max(100,6*1.4826*mad);
                if(pass==3)break;
                bool changed=false;for(int i=0;i<keep.Length;i++)if(keep[i]&&Math.Abs(residuals[i]-median)>gate){keep[i]=false;changed=true;}
                if(!changed)break;
            }
            double squares=0,xxUsed=0,meanUsed=0;
            for(int i=0;i<points.Count;i++)if(keep[i]){m.used++;meanUsed+=points[i].sample_index-m.first_sample;}
            if(m.used<3)return m;meanUsed/=m.used;
            for(int i=0;i<points.Count;i++)if(keep[i]){
                double x=points[i].sample_index-m.first_sample,residual=points[i].host_utc_us-baseUtc-intercept-slope*x;
                squares+=residual*residual;xxUsed+=(x-meanUsed)*(x-meanUsed);m.max_residual_us=Math.Max(m.max_residual_us,Math.Abs(residual));
            }
            m.rejected=m.observations-m.used;m.rms_residual_us=Math.Sqrt(squares/m.used);
            m.statistical_95_us=1.96*Math.Sqrt(squares/Math.Max(1,m.used-2))*Math.Sqrt(1.0/m.used+(m.drift_estimated&&xxUsed>0?Math.Max(meanUsed*meanUsed,Math.Pow(m.last_sample-m.first_sample-meanUsed,2))/xxUsed:0));
            m.us_per_sample=slope;m.reference_utc_us=baseUtc+intercept-(first.kind=="usb_arrival"?latencyUs:0);
            m.drift_ppm=(slope/m.nominal_us_per_sample-1)*1e6;m.within_assumed_5ppm=Math.Abs(m.drift_ppm)<=5;
            m.status=m.drift_estimated?"affine_estimate":"nominal_rate_short_span";return m;
        }
        static string Contained(string folder,string relative)
        {
            if(Path.IsPathRooted(relative))throw new InvalidDataException("Timing file path must be relative.");
            string path=Path.GetFullPath(Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Timing file leaves the selected recording folder.");
            for(string p=Path.GetDirectoryName(path);p!=null&&p.Length>=folder.Length;p=Path.GetDirectoryName(p))if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked recording folders are not followed.");
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked sample files are not followed.");return path;
        }
        static object[] DescribeModels(Model[] models) {
            return models.Select(m=>m.kind=="digital_source_anchor"?(object)new {
                m.session,m.kind,m.status,m.reference_clock,m.first_sample,m.last_sample,m.nominal_us_per_sample,m.us_per_sample,
                m.reference_utc_us,m.drift_estimated,m.drift_ppm,software_epoch_half_width_us=m.reference_uncertainty_us,
                rate_basis=m.drift_estimated?"shared AP arrival-rate fit":"nominal; recording-wide drift not measured"
            }:m).ToArray();
        }
        static object WriteTimes(string folder,string relative,IList<Observation> points,SampleAnchor[] anchors,string anchorEpoch,double? sharedApRatio,double latencyUs,bool replace)
        {
            string dat=Contained(folder,relative);var first=points.Count>0?points[0]:new Observation {channels=anchors[0].channels,rate_numerator=(int)anchors[0].rate_numerator,rate_denominator=(int)anchors[0].rate_denominator};
            if(first.channels<1 || first.channels>1152 || first.rate_numerator<1 || first.rate_denominator<1)throw new InvalidDataException("Invalid DAT timing layout.");
            long rowBytes=checked(first.channels*2L),length=new FileInfo(dat).Length,rows=length/rowBytes;
            if(length%rowBytes!=0)throw new InvalidDataException("Sample file has an incomplete channel row: "+relative);
            var models=Epochs(points).Select(p=>Fit(p,latencyUs)).ToArray();var good=models.Where(m=>m.us_per_sample>0).ToArray();
            var arrivalModels=models;string basis="usb_arrival_estimate",anchorStatus="unavailable";object probeOffsets=null;
            var qualified=anchors.Where(a=>ValidAnchor(a,rows,first.channels,first.rate_numerator,first.rate_denominator)).OrderBy(a=>a.probe).ToArray();
            bool sameEpoch=points.Count==0 || String.IsNullOrEmpty(anchorEpoch) || points.All(p=>p.host_epoch==anchorEpoch);
            // A discontinuous observation epoch cannot justify one global drift fit.
            if(qualified.Length==anchors.Length && qualified.Length>0 && sameEpoch && (points.Count==0 || models.Length==1 && (good.Length==1 || models[0].status=="unavailable"))) {
                var origin=qualified[0];double midpoint=(origin.pc_us_lower.Value+origin.pc_us_upper.Value)/2;
                double nominal=1e6*first.rate_denominator/first.rate_numerator;
                double ratio=sharedApRatio??1,period=nominal*ratio;bool drift=sharedApRatio.HasValue;
                // Keep sample rate and origin separate: receive latency is not subtracted from the firmware anchor.
                good=origin.segments.Where(s=>s.row_count>0).Select(s=>new Model {
                    session=anchorEpoch??"recorded_sd_clock",kind="digital_source_anchor",status=drift?"source_anchor_with_pc_rate":"source_anchor_nominal_rate",
                    reference_clock="pc_monotonic_anchor",first_sample=s.row_offset,last_sample=s.row_offset+s.row_count-1,
                    nominal_us_per_sample=nominal,us_per_sample=period,drift_estimated=drift,drift_ppm=(ratio-1)*1e6,
                    reference_utc_us=midpoint+(s.first_source_tick-origin.decoder_first_row_source_tick.Value)*1e6/origin.source_hz*ratio,
                    reference_uncertainty_us=(origin.pc_us_upper.Value-origin.pc_us_lower.Value)/2
                }).ToArray();
                models=good;basis="digital_source_anchor";anchorStatus="applied";
                probeOffsets=qualified.Select(a=>new {probe=a.probe,band=a.band,channel_start=Array.IndexOf(qualified,a)*384,
                    offset_from_reference_probe_us=((a.pc_us_lower.Value+a.pc_us_upper.Value)/2-midpoint)*ratio,
                    first_row_pc_us_lower=a.pc_us_lower,first_row_pc_us_upper=a.pc_us_upper}).ToArray();
            } else if(anchors.Length>0)anchorStatus="not_applied: invalid layout, missing origin, differing host epoch, or discontinuous arrival epochs";
            string target=dat+".timestamps.calibrated.dat";
            if(good.Length==0)return new{stream=relative,status="unavailable",reason="No qualified source anchor or sufficient arrival observations.",rows,models,anchor_status=anchorStatus};
            if(points.Any(p=>p.channels!=first.channels||p.rate_numerator!=first.rate_numerator||p.rate_denominator!=first.rate_denominator))throw new InvalidDataException("Sample file has conflicting rate/channel metadata.");
            for(int i=1;i<good.Length;i++)if(good[i].first_sample<=good[i-1].last_sample)throw new InvalidDataException("Reset/overlapping sample indices require separate recording folders.");
            if(!replace&&File.Exists(target))throw new IOException("Calibrated timestamps already exist; use ReplaceDerived to regenerate them.");
            string temporary=target+"."+Guid.NewGuid().ToString("N")+".partial";long missing=0;
            try{
                using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536)){
                    var values=new double[8192];var bytes=new byte[values.Length*8];int used=0,index=0;
                    for(long row=0;row<rows;row++){
                        while(index+1<good.Length&&row>good[index].last_sample)index++;
                        Model m=good[index];bool covered=row>=m.first_sample&&row<=m.last_sample;
                        if(basis!="digital_source_anchor" && index==0&&row<m.first_sample)covered=(m.first_sample-row)*m.nominal_us_per_sample<=MaximumExtrapolationSeconds*1e6;
                        if(basis!="digital_source_anchor" && index==good.Length-1&&row>m.last_sample)covered=(row-m.last_sample)*m.nominal_us_per_sample<=MaximumExtrapolationSeconds*1e6;
                        values[used++]=covered?m.At(row)/1e6:Double.NaN;if(!covered)missing++;
                        if(used==values.Length){Buffer.BlockCopy(values,0,bytes,0,used*8);file.Write(bytes,0,used*8);used=0;}
                    }
                    if(used!=0){Buffer.BlockCopy(values,0,bytes,0,used*8);file.Write(bytes,0,used*8);}file.Flush(true);
                }
                if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
            return new{stream=relative,status="written",file=relative+".timestamps.calibrated.dat",rows,bytes=checked(rows*8),unmapped_rows=missing,
                timestamp_basis=basis,anchor_status=anchorStatus,reference_probe=qualified.Length==0?(int?)null:qualified[0].probe,probe_offsets=probeOffsets,
                models=DescribeModels(models),arrival_fit_models=arrivalModels,uncertainty_scope="Source-anchor intervals are conditional software-epoch intervals, not physical ADC/GPIO accuracy or a bound on rate drift over the full recording."};
        }
        internal static string Correct(string recordingFolder,double latencyMicroseconds,bool replaceDerived)
        {
            if(!Finite(latencyMicroseconds)||latencyMicroseconds<0)throw new ArgumentOutOfRangeException("latencyMicroseconds");
            string folder=Path.GetFullPath(recordingFolder);if(!Directory.Exists(folder))throw new DirectoryNotFoundException(folder);
            string input=Path.Combine(folder,InputName),report=Path.Combine(folder,ReportName);
            var outputs=new List<object>();string status="unavailable",reason=null,sourceHash=null,anchorError,anchorHash,anchorEpoch;
            var anchors=ReadAnchors(folder,out anchorError,out anchorHash,out anchorEpoch);var observations=new List<Observation>();
            if(!File.Exists(input) && anchors.Length==0)reason="No saved sample/arrival observations or decoded source anchor. Historical analog row zero is not an AP timing anchor.";
            if(File.Exists(input)){
                sourceHash=Hash(input);
                foreach(string line in File.ReadLines(input)){
                    if(String.IsNullOrWhiteSpace(line))continue;if(line.Length>65536)throw new InvalidDataException("Oversized timing observation.");
                    var p=Json.Deserialize<Observation>(line);Validate(p);observations.Add(p);
                }
                if(Hash(input)!=sourceHash)throw new IOException("Timing journal changed during correction; stop the recording first.");
            }
            var apRatios=new List<double>();
            foreach(var group in observations.GroupBy(p=>p.stream,StringComparer.OrdinalIgnoreCase)) {
                var a=anchors.FirstOrDefault(x=>x.band=="ap" && String.Equals(x.data_file,group.Key,StringComparison.OrdinalIgnoreCase));
                if(a==null || !a.pc_time_valid || !String.IsNullOrEmpty(anchorEpoch) && group.Any(p=>p.host_epoch!=anchorEpoch))continue;
                var first=group.First();try {
                    long length=new FileInfo(Contained(folder,group.Key)).Length;
                    if(length%(first.channels*2L)!=0 || !ValidAnchor(a,length/(first.channels*2L),first.channels,first.rate_numerator,first.rate_denominator))continue;
                }catch{continue;}
                var epochs=Epochs(group.ToList());if(epochs.Count!=1)continue;
                var rate=Fit(epochs[0],0);if(rate.us_per_sample>0 && rate.drift_estimated)apRatios.Add(rate.us_per_sample/rate.nominal_us_per_sample);
            }
            double? sharedApRatio=apRatios.Count>0?(double?)Median(apRatios):null;
            foreach(string stream in observations.Select(p=>p.stream).Concat(anchors.Select(a=>a.data_file)).Distinct(StringComparer.OrdinalIgnoreCase))try {
                outputs.Add(WriteTimes(folder,stream,observations.Where(p=>String.Equals(p.stream,stream,StringComparison.OrdinalIgnoreCase)).ToList(),
                    anchors.Where(a=>String.Equals(a.data_file,stream,StringComparison.OrdinalIgnoreCase)).ToArray(),anchorEpoch,sharedApRatio,latencyMicroseconds,replaceDerived));
            }catch(Exception ex){SaveReport(report,Json.Serialize(new{schema_version=1,status="failed",input_sha256=sourceHash,failed_stream=stream,error=ex.Message,completed_streams=outputs,original_data_changed=false}));throw;}
            status=outputs.Count==0?"unavailable":"processed";
            var result=new{schema_version=1,status,reason,input=InputName,input_sha256=sourceHash,latency_correction_us=latencyMicroseconds,
                first_sample_metadata_sha256=anchorHash,optional_anchor_error=anchorError,
                timestamp_format="headerless little-endian float64 Unix UTC seconds, one value per complete DAT row; NaN means unmapped",
                reference="Shared PC QPC/UTC epoch; digital source anchor where qualified, otherwise labeled arrival estimate. Network correction is separate. Merged timestamps reference the lowest physical port; apply each probe_offsets entry for other channel groups.",
                limitations="USB arrival estimates retain unknown fixed transport/firmware latency unless supplied. Residual/confidence figures describe the fit, not absolute ADC/GPIO synchronization accuracy. The 5 ppm crystal value is an assumption, not a measured accuracy bound. No sample data or original timestamp files were modified.",
                short_span_policy="Below 60 seconds: nominal sample rate, fitted offset only; no measured drift.",maximum_edge_extrapolation_seconds=MaximumExtrapolationSeconds,streams=outputs};
            string text=Json.Serialize(result);SaveReport(report,text);
            return text;
        }
        static void SaveReport(string report,string text)
        {
            string temporary=report+"."+Guid.NewGuid().ToString("N")+".partial";
            try{File.WriteAllText(temporary,text,new UTF8Encoding(false));if(File.Exists(report))File.Replace(temporary,report,null);else File.Move(temporary,report);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static void AfterMerge(string folder) {
            try {
                string path=Path.Combine(folder,"first_sample_timestamps.json");if(!File.Exists(path))return;
                var document=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
                var anchors=Json.ConvertToType<SampleAnchor[]>(document["rows"]);
                var merge=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"dat_merge.json")));
                var bands=Json.ConvertToType<Dictionary<string,object>[]>(merge["bands"]);
                var updated=new List<Dictionary<string,object>>();
                // Retain first-row bounds and all other source metadata exactly; only the DAT row mapping changes.
                var originals=Json.ConvertToType<Dictionary<string,object>[]>(document["rows"]);
                for(int i=0;i<anchors.Length;i++) {
                    var a=anchors[i];var item=originals[i];
                    var band=bands.SingleOrDefault(b=>Json.ConvertToType<string[]>(b["input_files"]).Contains(a.data_file));
                    if(band==null){updated.Add(item);continue;}
                    int input=Array.IndexOf(Json.ConvertToType<string[]>(band["input_files"]),a.data_file);
                    var segments=new List<AnchorSegment>();
                    foreach(var part in Json.ConvertToType<Dictionary<string,object>[]>(band["segments"])) {
                        long start=Json.ConvertToType<long[]>(part["input_row_offsets"])[input],count=Convert.ToInt64(part["merged_rows"]);
                        if(count==0)continue;
                        var old=a.segments==null?null:a.segments.SingleOrDefault(s=>start>=s.row_offset && start+count<=s.row_offset+s.row_count);
                        if(old==null){item["pc_time_valid"]=false;segments.Clear();break;}
                        ulong ticks=checked((ulong)(start-old.row_offset)*a.source_hz*a.rate_denominator);
                        if(a.rate_numerator==0 || ticks%a.rate_numerator!=0)throw new InvalidDataException("Merged row is not on an exact source tick.");
                        segments.Add(new AnchorSegment {row_offset=Convert.ToInt64(part["merged_row_offset"]),row_count=count,first_source_tick=old.first_source_tick+ticks/a.rate_numerator});
                    }
                    item["data_file"]=band["file"];item["channels"]=band["channels"];item["dat_rows"]=band["rows"];item["segments"]=segments;
                    // Existing SD merge starts at each first complete row; reject future trims unless the origin is updated explicitly.
                    if(segments.Count>0 && segments[0].first_source_tick!=a.decoder_first_row_source_tick)item["pc_time_valid"]=false;
                    updated.Add(item);
                }
                if(!File.Exists(Path.Combine(folder,"first_sample_timestamps.before_merge.json")))File.Copy(path,Path.Combine(folder,"first_sample_timestamps.before_merge.json"));
                object startValue;
                if(document.TryGetValue("recording_start",out startValue)&&startValue!=null) {
                    var start=Json.ConvertToType<Dictionary<string,object>>(startValue);object probe;
                    if(start.TryGetValue("reference_probe",out probe)&&probe!=null) {
                        var row=updated.SingleOrDefault(r=>Convert.ToInt32(r["probe"])==Convert.ToInt32(probe)&&Convert.ToString(r["band"])=="ap");
                        if(row!=null) {
                            start["data_file"]=row["data_file"];start["pc_time_valid"]=row["pc_time_valid"];
                            if(!(bool)row["pc_time_valid"]) {start["pc_unix_seconds"]=start["pc_lower_unix_seconds"]=start["pc_upper_unix_seconds"]=null;
                                start["unavailable_reason"]="Merged layout no longer attests the original first retained row.";}
                            document["recording_start"]=start;
                        }
                    }
                }
                document["rows"]=updated;SaveReport(path,Json.Serialize(document));
                NpRecordingUtc.RefreshFromFirstSampleReport(folder);Automatic(folder);
            }catch(Exception ex){try{File.WriteAllText(Path.Combine(folder,"timestamp_calibration_error.txt"),ex.Message);}catch{}}
        }
        internal static void Automatic(string folder,string journalError=null)
        {
            try{if(journalError!=null)throw new IOException(journalError);Correct(folder,0,false);}
            catch(Exception ex){try{File.WriteAllText(Path.Combine(folder,"timestamp_calibration_error.txt"),ex.Message);}catch{}Trace.WriteLine("Timestamp calibration: "+ex.Message);}
        }
    }
}
