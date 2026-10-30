using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface {
    internal sealed class NpRtcUsbHistory {
        internal bool Enabled;
        internal byte MissingMask=7;
        internal int PagesBeforeRoot;
        internal string Error,Warning;
        internal NpFirstSyncUsbFineRoot SourceRoot;
        internal NpRtcStartBinding Binding;
        internal byte[] Raw;
        internal NpRtcStartBounds Bounds;
        internal bool Bound {get{return Raw!=null&&Binding!=null;}}
        internal bool Qualified {get{return Bound&&Bounds!=null&&Bounds.Qualified;}}
    }
    // Optional post-record replay. Cached status flags are never qualification.
    internal static class NpRtcUsbArchive {
        internal const string RawFile="usb_rtc_start.bin",StatusFile="usb_rtc_start_status.json",RowKind="first_sync_rtc_page";
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("USB RTC evidence: "+why);}
        static byte[] Hex(object value){
            string text=Convert.ToString(value,Inv);Need(text!=null&&text.Length==216,"108-byte raw page required");
            var raw=new byte[108];for(int i=0;i<raw.Length;i++)raw[i]=Byte.Parse(text.Substring(i*2,2),NumberStyles.HexNumber,Inv);return raw;
        }
        internal static NpRtcStartBinding Bind(NpFirstSyncUsbFineRoot root){
            Need(root!=null,"completed fine source root required");var info=NpFirstSyncUsb.ParseInfo(root.Info,root.Uid,(int)root.Family);
            Need(info.Ready&&info.HardwareRtc&&info.FineCounter64&&info.Token==root.Token,"confirmed RTC/continuous64 selection required");
            return NpRtcStartWire.BindUsb(root.Receipt,root.Anchor,(int)root.Family,root.Uid,root.Token,info.Features);
        }
        internal static void Save(string folder,byte[] raw,NpRtcStartBinding binding){
            var stamp=NpRtcStartWire.Parse(raw,binding);var bounds=NpRtcStartWire.Map(raw,binding);
            using(var output=new FileStream(Path.Combine(folder,RawFile),FileMode.CreateNew,FileAccess.Write,FileShare.Read))output.Write(raw,0,raw.Length);
            File.WriteAllText(Path.Combine(folder,StatusFile),new JavaScriptSerializer().Serialize(new {schema=1,bound=true,raw_file=RawFile,
                device_uid=stamp.Uid,token_hex=stamp.Token.ToString("X16",Inv),capture_generation=stamp.Generation,source_epoch=stamp.SourceEpoch,
                counter_domain=stamp.Domain,counter_hz=stamp.FineHz,receipt_crc32=stamp.ReceiptCrc.ToString("X8",Inv),
                local_rtc_mapping_qualified=bounds.Qualified,local_rtc_mapping_reason=bounds.Reason.ToString(),
                local_utc_lower_us=bounds.Qualified?(long?)bounds.LowerUs:null,local_utc_upper_us=bounds.Qualified?(long?)bounds.UpperUs:null,
                pc_calibration_uncertainty_us=stamp.PcUncertaintyUs,scope="Device RTC at final NP STARTBYTE; PC alignment and network precision separate. Raw journal replay is authoritative."}));
        }
        internal static NpRtcUsbHistory Read(string folder){
            string path=Path.Combine(folder,NpUsbTimingEvidence.FileName);if(!File.Exists(path))return null;
            var result=new NpRtcUsbHistory();NpRtcStartAssembler assembled=null;
            try{
                var source=NpFirstSyncUsbEvidence.Read(folder);Need(source!=null,"source journal unavailable");result.Enabled=source.RtcEnabled;result.Warning=source.Warning;
                if(!result.Enabled){result.Error="Hardware RTC was not negotiated.";return result;}
                Need(source.Qualified&&source.FineQualified,source.Error??source.FineError??"Complete source proof/root unavailable");
                var root=source.FineRoot;var binding=Bind(root);result.SourceRoot=root;
                assembled=new NpRtcStartAssembler(binding);bool rootSeen=false;
                var json=new JavaScriptSerializer();
                using(var input=new StreamReader(path)){
                    string line;while((line=input.ReadLine())!=null){
                        Need(line.Length<32768,"oversized journal row");
                        if(input.EndOfStream&&!line.TrimEnd().EndsWith("}",StringComparison.Ordinal)){result.Warning="Truncated final journal row; verified prefix only.";break;}
                        var row=json.Deserialize<Dictionary<string,object>>(line);string kind=Convert.ToString(row["kind"],Inv);
                        if(kind=="first_sync_source_root"){
                            var raw=Hex(row["raw_hex"]);ulong sequence=UInt64.Parse(Convert.ToString(row["container_sequence"],Inv),Inv);
                            if(sequence==root.Sequence&&raw.SequenceEqual(root.Envelope))rootSeen=true;
                        }else if(kind==RowKind){
                            Need(Convert.ToInt64(row["receive_qpc"],Inv)>0,"RTC page QPC unavailable");
                            if(!rootSeen){result.PagesBeforeRoot++;continue;}
                            var raw=Hex(row["raw_hex"]);ulong sequence=UInt64.Parse(Convert.ToString(row["container_sequence"],Inv),Inv);
                            Need(sequence>root.Sequence,"RTC page must follow the final source root");
                            Need(assembled.Feed(raw,sequence),assembled.Error??"RTC page assembly failed");result.MissingMask=assembled.MissingMask;
                        }
                    }
                }
                Need(rootSeen&&assembled.Complete,"RTC pages unavailable/incomplete; missing mask "+assembled.MissingMask);
                byte[] body=assembled.Raw;string cache=Path.Combine(folder,RawFile);
                if(File.Exists(cache))Need(File.ReadAllBytes(cache).SequenceEqual(body),"immutable RTC body cache disagrees with raw pages");
                result.SourceRoot=root;result.Binding=binding;result.Raw=body;result.Bounds=NpRtcStartWire.Map(body,binding);
            }catch(Exception error){result.Error=error.Message;result.Raw=null;result.Binding=null;result.Bounds=null;}
            if(assembled!=null)result.MissingMask=assembled.MissingMask;return result;
        }
    }
}
