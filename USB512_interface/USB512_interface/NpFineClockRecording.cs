using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Replays original, explicitly archived evidence only. Notification ranges
    // are host-producer attestations: their bytes were not saved independently.
    // Neither a fit nor a small interval establishes radio/ADC phase accuracy.
    internal static class NpFineClockRecording {
        internal const string StatusFile="fine_clock_fit_status.json";
        const long MaximumFileBytes=8*1024*1024,MaximumTotalBytes=32*1024*1024;
        const decimal Wrap=4294967296m;
        sealed class Clock {
            internal string Epoch;internal long Frequency,OriginQpc,OriginUs;internal decimal HalfWidth;
            internal bool Same(Clock other){return Epoch==other.Epoch&&Frequency==other.Frequency&&OriginQpc==other.OriginQpc&&OriginUs==other.OriginUs&&HalfWidth==other.HalfWidth;}
        }
        sealed class Boundaries {
            internal long Send,Receive;internal int LastBytes,FirstReplyBytes;internal string Signature;
        }
        static void Need(bool value,string reason){if(!value)throw new InvalidDataException("Fine SD clock: "+reason);}
        static Dictionary<string,object> Obj(object value){var d=value as Dictionary<string,object>;Need(d!=null,"object required");return d;}
        static object At(Dictionary<string,object> d,string name){object v;Need(d.TryGetValue(name,out v)&&v!=null,"missing "+name);return v;}
        static string Text(Dictionary<string,object> d,string name){string s=At(d,name) as string;Need(s!=null,"text "+name);return s;}
        static decimal Number(object value){
            Need(value is int||value is long||value is uint||value is ulong||value is decimal||value is double,"numeric field required");
            return Convert.ToDecimal(value,CultureInfo.InvariantCulture);
        }
        static long Long(Dictionary<string,object> d,string name){decimal v=Number(At(d,name));Need(v==Decimal.Truncate(v)&&v>=Int64.MinValue&&v<=Int64.MaxValue,"integer "+name);return (long)v;}
        static bool Flag(Dictionary<string,object> d,string name){object v=At(d,name);Need(v is bool,"boolean "+name);return (bool)v;}
        static object[] Items(Dictionary<string,object> d,string name,int maximum){var a=At(d,name) as object[];Need(a!=null&&a.Length>0&&a.Length<=maximum,"array "+name);return a;}
        static byte[] Hex(string s,int bytes){Need(s.Length==bytes*2,"raw length");var b=new byte[bytes];for(int i=0;i<bytes;i++)Need(Byte.TryParse(s.Substring(i*2,2),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out b[i]),"raw hex");return b;}
        static string Hash(byte[] b){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","");}
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static NpFirstSyncBinding Binding(NpFirstSyncReceipt r){return new NpFirstSyncBinding {Family=r.Family,Session=r.Session,Generation=r.Generation,SourceEpoch=r.SourceEpoch,
            Domain=r.Domain,CounterHz=r.CounterHz,ProfileCrc=r.ProfileCrc,Mask=r.Mask,ApHz=r.ApHz};}
        static void Count(Dictionary<string,int> counts,string reason){int n;counts.TryGetValue(reason,out n);counts[reason]=n+1;}
        static Clock ReadClock(Dictionary<string,object> row){
            var d=Obj(At(row,"pc_clock"));var c=new Clock {Epoch=Text(d,"host_epoch"),Frequency=Long(d,"qpc_frequency"),OriginQpc=Long(d,"origin_qpc"),OriginUs=Long(d,"origin_pc_unix_us"),HalfWidth=Number(At(d,"anchor_half_width_us"))};
            Need(c.Epoch.Length>0&&c.Epoch.Length<=128&&Text(row,"host_epoch")==c.Epoch,"host epoch mismatch");
            Need(c.Frequency>0&&c.Frequency<=1000000000000L&&c.OriginQpc>=0&&c.OriginUs>=1577836800000000L&&c.OriginUs<=4102444800000000L&&c.HalfWidth>=0,"invalid PC anchor");
            return c;
        }
        static Boundaries ReadBoundaries(Dictionary<string,object> row){
            var d=Obj(At(row,"transport_boundaries"));Need(Flag(d,"reply_id_crc_correlated")&&Flag(d,"boundary_order_valid"),"uncorrelated transport");
            long full=Long(d,"full_exchange_start_qpc"),continuation=Long(d,"continuation_qpc"),send=Long(d,"last_gatt_fragment_send_qpc"),
                receive=Long(d,"reply_start_notification_qpc"),complete=Long(d,"reply_complete_notification_qpc"),start=Long(d,"reply_start_byte"),end=Long(d,"reply_end_byte");
            Need(full>0&&continuation>=complete&&complete>=receive&&receive>=send&&send>=full&&start>=0&&end>start&&(end-start==83||end-start==84),"transport boundary order/frame size");
            if(row.ContainsKey("host_send_qpc"))Need(Long(row,"host_send_qpc")==full,"full send mismatch");
            if(row.ContainsKey("host_receive_qpc"))Need(Long(row,"host_receive_qpc")==continuation,"continuation mismatch");
            var signature=new StringBuilder();signature.Append(full).Append('/').Append(continuation).Append('/').Append(start).Append('/').Append(end);
            int offset=0,lastBytes=0;long lastAfter=full,lastBefore=0;
            foreach(object item in Items(d,"write_fragments",4)){
                var w=Obj(item);long at=Long(w,"offset"),bytes=Long(w,"bytes"),before=Long(w,"before_write_qpc"),after=Long(w,"completed_qpc");
                Need(at==offset&&bytes>0&&bytes<=67-offset&&before>=lastAfter&&after>=before&&after<=continuation&&Flag(w,"success"),"incomplete/noncontiguous GATT writes");
                offset+=(int)bytes;lastBytes=(int)bytes;lastBefore=before;lastAfter=after;
                signature.Append('|').Append(at).Append(',').Append(bytes).Append(',').Append(before).Append(',').Append(after);
            }
            Need(offset==67&&lastBefore==send,"last write not complete request tail");
            long priorEnd=-1,priorQpc=0,priorNumber=-1;int firstBytes=0;long firstNumber=0,completeNumber=0;bool haveStart=false,haveEnd=false;
            foreach(object item in Items(d,"notification_segments",128)){
                var n=Obj(item);long a=Long(n,"start_byte"),b=Long(n,"end_byte"),q=Long(n,"callback_qpc"),number=Long(n,"number");
                Need(a>=0&&b>a&&q>0&&q<=continuation&&number>0&&(priorEnd<0||a==priorEnd&&q>=priorQpc&&number==priorNumber+1),"notification ledger order/range");
                if(start>=a&&start<b){Need(q==receive,"first callback mismatch");haveStart=true;firstNumber=number;firstBytes=(int)(Math.Min(b,end)-start);}
                if(end-1>=a&&end-1<b){Need(q==complete,"complete callback mismatch");haveEnd=true;completeNumber=number;}
                priorEnd=b;priorQpc=q;priorNumber=number;signature.Append('|').Append(number).Append(',').Append(a).Append(',').Append(b).Append(',').Append(q);
            }
            Need(haveStart&&haveEnd&&firstBytes>0,"accepted reply outside notification ledger");
            if(d.ContainsKey("reply_start_notification"))Need(Long(d,"reply_start_notification")==firstNumber,"first notification number mismatch");
            if(d.ContainsKey("reply_complete_notification"))Need(Long(d,"reply_complete_notification")==completeNumber,"complete notification number mismatch");
            return new Boundaries {Send=send,Receive=receive,LastBytes=lastBytes,FirstReplyBytes=firstBytes,Signature=signature.ToString()};
        }
        static decimal Unwrap(uint ms,uint counter,byte[] anchor,uint hz){
            int delta=unchecked((int)(ms-U(anchor,20)));Need(delta!=Int32.MinValue,"ambiguous HAL-ms wrap");
            decimal coarse=(decimal)delta*hz/1000m,raw=unchecked(counter-U(anchor,28));
            decimal turns=(coarse-raw)/Wrap,nearest=Decimal.Round(turns,0,MidpointRounding.AwayFromZero);
            Need(Math.Abs(turns-nearest)!=0.5m,"ambiguous counter wrap");decimal result=raw+nearest*Wrap;
            Need(Math.Abs(result-coarse)<=hz/500m,"NPAR/query coarse-fine disagreement");return result;
        }
        static byte[] ReadRaw(string folder,string name,List<object> files){
            string path=Path.Combine(folder,name);Need(new FileInfo(path).Length==512,"raw sector size: "+name);
            byte[] b=File.ReadAllBytes(path);files.Add(new {file=name,sha256=Hash(b),bytes=b.Length});return b;
        }
        internal static NpFirstSyncUtcBounds MapSd(string folder,NpFineSdCompanion companion,out string error){
            error=null;NpFirstSyncUtcBounds result=null;var files=new List<object>();var selected=new List<object>();var counts=new Dictionary<string,int>();
            var report=new Dictionary<string,object>{{"schema",1},{"source","original bound SD sectors and raw NPRF1/NPRF2 BLE exchange journals"},{"qualified",false},
                {"files",files},{"selected_points",selected},{"ignored",counts},{"notification_evidence","CRC/ID/UID/nonce checked raw bodies; accepted byte ranges and callback boundaries attested by host producer, no independent raw notification archive"},
                {"scope","PC clock only; conditional affine continuous-counter model. No network accuracy or physical ADC/IMU phase claim."}};
            try{
                Need(!String.IsNullOrWhiteSpace(folder)&&Directory.Exists(folder),"explicit archive folder missing");
                Need(companion!=null&&companion.Bound!=null&&companion.Bound.Receipt!=null,"bound fine SD companion missing");
                var expected=companion.Bound;var e=expected.Receipt;
                var actual=NpFineClockWire.ReadCompanion(ReadRaw(folder,"first_source_clock_sector6.bin",files),ReadRaw(folder,"recorded_clock.bin",files),(int)e.Family,e.Session,e.ApHz,expected.Uid,expected.Token);
                Need(actual!=null&&actual.Bound.Receipt.Raw.SequenceEqual(e.Raw)&&actual.Anchor.SequenceEqual(companion.Anchor),"archived companion changed");
                var bound=actual.Bound;var r=bound.Receipt;byte[] anchor=actual.Anchor;
                report["device_uid"]=bound.Uid;report["record_token_hex"]=bound.Token.ToString("X16");
                report["binding"]=new {family=r.Family,session=r.Session,capture_generation=r.Generation,source_epoch=r.SourceEpoch,domain=r.Domain,counter_hz=r.CounterHz,profile_crc32=r.ProfileCrc,probe_mask=r.Mask,ap_hz=r.ApHz};report["reference_tick"]=r.ReferenceTick;
                string[] paths=Directory.GetFiles(folder,"ble_clock_*.jsonl",SearchOption.TopDirectoryOnly).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
                Need(paths.Length>0&&paths.Length<=64,"missing or excessive archived BLE journals");
                var json=new JavaScriptSerializer {MaxJsonLength=262144};var points=new List<NpFineClockFit.Point>();var seen=new Dictionary<string,string>();var rawSeen=new Dictionary<string,string>();Clock clock=null;long total=0;
                foreach(string path in paths){
                    long size=new FileInfo(path).Length;Need(size<=MaximumFileBytes&&total<=MaximumTotalBytes-size,"journal size limit");total+=size;
                    byte[] bytes=File.ReadAllBytes(path);files.Add(new {file=Path.GetFileName(path),sha256=Hash(bytes),bytes=bytes.Length});
                    string text;try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){
                        int last=Array.LastIndexOf(bytes,(byte)'\n');Need(last>=0&&last<bytes.Length-1,"invalid UTF8 journal");
                        text=new UTF8Encoding(false,true).GetString(bytes,0,last+1);Count(counts,"truncated_last_line");
                    }
                    if(text.Length>0&&text[0]=='\ufeff')text=text.Substring(1);
                    string[] lines=text.Split('\n');for(int line=0;line<lines.Length;line++){
                        string value=lines[line].Trim();if(value.Length==0)continue;Dictionary<string,object> row;
                        try{Need(value.Length<=262144,"journal line size limit");row=Obj(json.DeserializeObject(value));}
                        catch(Exception ex) when(ex is ArgumentException||ex is InvalidDataException){
                            if(line==lines.Length-1&&!text.EndsWith("\n",StringComparison.Ordinal)){Count(counts,"truncated_last_line");continue;}
                            throw new InvalidDataException("Fine SD clock: malformed complete journal line",ex);
                        }
                        object kind,protocol,outcome;if(!row.TryGetValue("event",out kind)||!Equals(kind,"exchange_finished")||!row.TryGetValue("clock_protocol",out protocol)||
                            !(Equals(protocol,"NPRF1")||Equals(protocol,"NPRF2"))||!row.TryGetValue("outcome",out outcome)||!Equals(outcome,"reply_validated")){Count(counts,"other_event_or_protocol");continue;}
                        var context=Obj(At(row,"context"));ulong token=0;string tokenText=Text(context,"sd_record_token_hex");
                        Need(tokenText.Length==16&&UInt64.TryParse(tokenText,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out token),"context token shape");
                        if(token!=bound.Token){Count(counts,"other_record_token");continue;}
                        byte[] request=Hex(Text(row,"request_hex"),64),reply=Hex(Text(row,"reply_hex"),80);
                        string uid=U(request,12).ToString("X8")+U(request,16).ToString("X8")+U(request,20).ToString("X8");
                        if(uid!=bound.Uid){Count(counts,"other_uid");continue;}
                        NpFineClockWire.ValidateRequest(request,bound.Uid);var q=NpFineClockWire.ParseReply(reply,request,(int)r.Family);
                        Need(Equals(protocol,q.Version==2?"NPRF2":"NPRF1"),"protocol label/version mismatch");
                        // V1 archives predate this diagnostic field. For v2 it
                        // is explicit, but never a causal bound or a timestamp.
                        if(q.Version==2||row.ContainsKey("requested_hold_us"))Need(Long(row,"requested_hold_us")==q.RequestedHoldUs,"requested hold echo mismatch");
                        string key=U(request,8).ToString("X8")+":"+BitConverter.ToUInt64(request,24).ToString("X16"),rawSignature=BitConverter.ToString(request)+"/"+BitConverter.ToString(reply),priorRaw;
                        if(rawSeen.TryGetValue(key,out priorRaw))Need(priorRaw==rawSignature,"conflicting duplicate raw ID/nonce");else rawSeen.Add(key,rawSignature);
                        if(!q.Qualified||q.Domain!=r.Domain||q.Hz!=r.CounterHz||(q.Flags&NpFineClockWire.CaptureBound)!=0&&(q.Generation!=r.Generation||q.SourceEpoch!=r.SourceEpoch)){Count(counts,"other_or_unqualified_counter_context");continue;}
                        Need(Long(row,"connection_epoch")>=0,"connection epoch");var c=ReadClock(row);var b=ReadBoundaries(row);
                        string signature=rawSignature+"/"+c.Epoch+"/"+Long(row,"connection_epoch")+"/"+c.Frequency+"/"+c.OriginQpc+"/"+c.OriginUs+"/"+c.HalfWidth.ToString(CultureInfo.InvariantCulture)+"/"+b.Signature;
                        string previous;if(seen.TryGetValue(key,out previous)){Need(previous==signature,"conflicting duplicate ID/nonce");Count(counts,"identical_mirrored_point");continue;}
                        Need(points.Count<NpFineClockFit.MaximumPoints,"selected point limit exceeded");Need(clock==null||clock.Same(c),"conflicting selected host epoch/anchor");clock=c;seen.Add(key,signature);
                        decimal rx=Unwrap(q.RxMs,q.RxCounter,anchor,q.Hz),tx=Unwrap(q.TxMs,q.TxCounter,anchor,q.Hz);Need(tx>=rx,"unwrapped query counter order");
                        var p=new NpFineClockFit.Point {RxCounter=rx,TxCounter=tx,SendUs=((decimal)b.Send-c.OriginQpc)*1000000m/c.Frequency,ReceiveUs=((decimal)b.Receive-c.OriginQpc)*1000000m/c.Frequency};
                        if((q.Flags&NpFineClockWire.UartBaudBound)!=0){p.MinRequestCounter=(decimal)b.LastBytes*10*q.Hz/q.UartBaud;p.MinReplyCounter=(decimal)b.FirstReplyBytes*10*q.Hz/q.UartBaud;}
                        points.Add(p);selected.Add(new {file=Path.GetFileName(path),line=line+1,request_id=U(request,8),nonce_hex=BitConverter.ToUInt64(request,24).ToString("X16"),connection_epoch=Long(row,"connection_epoch"),
                            protocol_version=q.Version,requested_hold_us=q.RequestedHoldUs,rx_counter=rx,tx_counter=tx,last_send_qpc=b.Send,first_reply_qpc=b.Receive,request_min_counter=p.MinRequestCounter,reply_min_counter=p.MinReplyCounter,capture_bound=(q.Flags&NpFineClockWire.CaptureBound)!=0});
                    }
                }
                Need(clock!=null,"no matching qualified archived fine-clock points");
                decimal target=((decimal)BitConverter.ToUInt64(anchor,44)+r.ReferenceTick)*r.CounterHz/r.SourceHz;
                decimal targetHigh=target+unchecked(U(anchor,32)-U(anchor,28))+1m;
                var fit=NpFineClockFit.Fit(points,target,targetHigh,1000000m/r.CounterHz,clock.HalfWidth);
                report["pc_clock"]=new {host_epoch=clock.Epoch,qpc_frequency=clock.Frequency,origin_qpc=clock.OriginQpc,origin_pc_unix_us=clock.OriginUs,anchor_half_width_us=clock.HalfWidth};
                report["target_counter_lower"]=target;report["target_counter_upper"]=targetHigh;
                report["fit"]=new {qualified=fit.Qualified,error=fit.Error,points=fit.PointCount,lower_relative_us=fit.LowerUs,upper_relative_us=fit.UpperUs,
                    rate_lower_us_per_counter=fit.RateLowerUsPerCounter,rate_upper_us_per_counter=fit.RateUpperUsPerCounter,rate_bound_determines_precision=fit.RateBoundDeterminesPrecision,
                    extrapolated=fit.Extrapolated,extrapolation_before_counter=fit.ExtrapolationBeforeCounter,extrapolation_after_counter=fit.ExtrapolationAfterCounter,
                    bounds_source=fit.BoundsSource,assumption=fit.Assumption};
                Need(fit.Qualified&&fit.LowerUs.HasValue&&fit.UpperUs.HasValue,"causal fit unqualified: "+fit.Error);
                decimal lower=clock.OriginUs+fit.LowerUs.Value,upper=clock.OriginUs+fit.UpperUs.Value;
                Need(lower>=1577836800000000m&&upper<=4102444800000000m&&lower<=upper,"mapped PC interval outside supported epoch");
                result=new NpFirstSyncUtcBounds {DeviceIdentity=bound.Uid,HostEpoch=clock.Epoch,Binding=Binding(r),SourceHz=r.SourceHz,ReferenceTick=r.ReferenceTick,LowerUs=lower,UpperUs=upper,
                    SourceClockQualified=true,Kind=NpFirstSyncUtcBoundsKind.Cycle,Evidence=StatusFile+"; original SD UID/token/final-generation NPAR binding; raw NPRF1/NPRF2 CRC/ID/nonce; causal affine interval, shared PC anchor uncertainty once"};
                report["qualified"]=true;report["pc_lower_unix_us"]=lower;report["pc_upper_unix_us"]=upper;
            }catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is ArithmeticException||ex is InvalidOperationException){error=ex.Message;report["error"]=error;}
            try{if(!String.IsNullOrWhiteSpace(folder)&&Directory.Exists(folder))File.WriteAllText(Path.Combine(folder,StatusFile),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(false));}
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException){/* Diagnostics cannot alter timing qualification. */}
            return result;
        }
    }
}
