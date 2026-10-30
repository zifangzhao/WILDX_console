using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface {
    internal sealed class NpUsbTimingObservation {
        internal NpUsbTimingRecord Record;internal long SendQpc,ReceiveQpc;internal int Segment;
    }
    internal sealed class NpUsbTimingHistory {
        internal string Uid,Epoch,Error;internal int Family;internal ulong Token;internal uint Generation;
        internal double QpcHz,OriginPcUs,HalfWidth;internal long OriginQpc;
        internal byte[] Anchor,StartCycles;
        internal readonly List<NpUsbTimingObservation> Points=new List<NpUsbTimingObservation>();
        internal double PcUs(long qpc){return OriginPcUs+(qpc-OriginQpc)*1000000.0/QpcHz;}
    }
    // Revalidate the retained wire bytes when generating derived timestamps.
    // The journal is evidence, not permission to trust arbitrary JSON fields.
    internal static class NpUsbTimingEvidence {
        internal const string FileName="usb_timing_evidence.jsonl";
        sealed class Attempt {internal long Qpc;internal bool Prearm;internal uint Generation;}
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static string S(Dictionary<string,object> d,string key){return Convert.ToString(d[key],Inv);}
        static long L(Dictionary<string,object> d,string key){return Convert.ToInt64(d[key],Inv);}
        static uint U(Dictionary<string,object> d,string key){return Convert.ToUInt32(d[key],Inv);}
        static byte[] Hex(string text,int bytes){if(text==null||text.Length!=bytes*2)throw new InvalidDataException("USB timing raw record length mismatch.");
            var b=new byte[bytes];for(int i=0;i<bytes;i++)b[i]=Byte.Parse(text.Substring(i*2,2),NumberStyles.HexNumber,Inv);return b;}
        static void Need(bool ok,string error){if(!ok)throw new InvalidDataException("USB timing evidence: "+error);}
        internal static NpUsbTimingHistory Read(string folder) {
            string path=Path.Combine(folder,FileName);if(!File.Exists(path))return null;
            var h=new NpUsbTimingHistory();var attempts=new Dictionary<uint,Attempt>();var used=new HashSet<uint>();uint lastId=0;
            var source=new Dictionary<ushort,byte[]>();bool conflict=false;var json=new JavaScriptSerializer();
            using(var input=new StreamReader(path)) {
                string line=input.ReadLine();Need(line!=null&&line.Length<32768,"missing metadata");var meta=json.Deserialize<Dictionary<string,object>>(line);
                Need(S(meta,"kind")=="metadata"&&U(meta,"schema")==2,"unsupported schema");
                h.Uid=S(meta,"device_uid");h.Family=Convert.ToInt32(meta["device_family"],Inv);h.Epoch=S(meta,"host_epoch");h.Token=UInt64.Parse(S(meta,"token_hex"),NumberStyles.HexNumber,Inv);
                var clock=(Dictionary<string,object>)meta["clock"];h.OriginQpc=L(clock,"origin_qpc");h.OriginPcUs=Convert.ToDouble(clock["origin_pc_unix_us"],Inv);
                h.QpcHz=Convert.ToDouble(clock["qpc_frequency"],Inv);h.HalfWidth=Convert.ToDouble(clock["anchor_half_width_us"],Inv);
                Need(h.Token!=0&&!String.IsNullOrWhiteSpace(h.Epoch)&&h.Epoch==S(clock,"host_epoch")&&h.Uid.Length==24&&h.Uid!=new string('0',24)&&!h.Uid.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase)&&h.QpcHz>0&&h.QpcHz<1e12&&h.HalfWidth>=0&&h.HalfWidth<5000000&&h.OriginPcUs>=1577836800000000&&h.OriginPcUs<=4102444800000000,"invalid host coordinate");
                var replies=new List<Tuple<NpUsbTimingRecord,long>>();
                while((line=input.ReadLine())!=null) {
                    Need(line.Length<32768,"oversized row");
                    if(input.EndOfStream&&!line.TrimEnd().EndsWith("}",StringComparison.Ordinal)){h.Error="Truncated final journal row; verified prefix only.";break;}
                    var d=json.Deserialize<Dictionary<string,object>>(line);string kind=S(d,"kind");
                    if(kind=="request") {
                        uint id=U(d,"request_id");bool pre=S(d,"phase")=="prearm";long qpc=L(d,"send_qpc");uint generation=U(d,"generation");
                        Need(id>lastId&&id!=0&&qpc>0,"reused/reordered request");lastId=id;
                        if(!pre) {var b=Hex(S(d,"raw_hex"),64);
                            Need(BitConverter.ToUInt32(b,0)==NpUsbTimingWire.Request&&BitConverter.ToUInt32(b,4)==0x10001&&BitConverter.ToUInt32(b,8)==id&&BitConverter.ToUInt32(b,12)==0&&
                                BitConverter.ToUInt64(b,16)==h.Token&&BitConverter.ToUInt32(b,24)==generation&&b.Skip(28).Take(32).All(x=>x==0)&&BitConverter.ToUInt32(b,60)==NpAcquisitionAnchor.Crc(b,60),"request bytes disagree");
                        }
                        attempts.Add(id,new Attempt{Qpc=qpc,Prearm=pre,Generation=generation});
                    } else if(kind=="record") {
                        if(S(d,"outcome")!="accepted")continue;
                        ulong sequence=UInt64.Parse(S(d,"container_sequence"),Inv);long qpc=L(d,"receive_qpc");
                        var r=NpUsbTimingWire.Parse(Hex(S(d,"raw_hex"),108),h.Uid,h.Family,h.Token,sequence);
                        if(r.Kind==1) {if(r.Valid)replies.Add(Tuple.Create(r,qpc));}
                        else {
                            if(h.Generation==0)h.Generation=r.Generation;
                            if(h.Generation!=r.Generation)continue;
                            byte[] old;if(source.TryGetValue(r.Kind,out old)&&!old.SequenceEqual(r.Body))conflict=true;
                            else source[r.Kind]=r.Body;
                        }
                    } else if(kind=="close"&&d.ContainsKey("error")&&d["error"]!=null)h.Error=S(d,"error");
                }
                foreach(var pair in replies) {
                    var r=pair.Item1;Attempt a;
                    if(!attempts.TryGetValue(r.Id,out a)||!used.Add(r.Id)||a.Prearm!=r.Prearm||a.Generation!=r.Generation||pair.Item2<a.Qpc)continue;
                    // A pre-arm generation transition requires a real same-token startup record.
                    if(!r.Prearm&&(h.Generation==0||r.Generation!=h.Generation))continue;
                    if(r.Prearm&&h.Generation==0)continue;
                    if(h.PcUs(pair.Item2)-h.PcUs(a.Qpc)>5000000)continue;
                    h.Points.Add(new NpUsbTimingObservation{Record=r,SendQpc=a.Qpc,ReceiveQpc=pair.Item2});
                }
                h.Points.Sort((a,b)=>a.SendQpc.CompareTo(b.SendQpc));
                uint? last=null;uint previousId=0;int segment=0;var good=new List<NpUsbTimingObservation>();
                foreach(var point in h.Points) {
                    if(last.HasValue&&unchecked(point.Record.SealMs-last.Value)>=0x80000000U)break;
                    if(previousId!=0&&point.Record.Id!=previousId+1)segment++;
                    point.Segment=segment;good.Add(point);last=point.Record.SealMs;previousId=point.Record.Id;
                }
                h.Points.Clear();h.Points.AddRange(good);
                if(!conflict){source.TryGetValue(2,out h.Anchor);source.TryGetValue(3,out h.StartCycles);}
                else h.Error="Conflicting immutable startup receipts; source origin unavailable.";
            }
            return h;
        }
    }
}
