using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace USB512_interface {
    // Immutable raw, revalidated source context. This is a device-relative
    // counter association, not a PC/UTC or physical sensor-phase claim.
    internal sealed class NpFirstSyncUsbFineRoot {
        internal readonly string Uid;
        internal readonly ulong Token,Sequence;
        internal readonly uint Family,Generation;
        readonly byte[] info,receipt,envelope,anchor;
        internal byte[] Info {get{return (byte[])info.Clone();}}
        internal byte[] Receipt {get{return (byte[])receipt.Clone();}}
        internal byte[] Envelope {get{return (byte[])envelope.Clone();}}
        internal byte[] Anchor {get{return (byte[])anchor.Clone();}}
        internal NpFirstSyncUsbFineRoot(byte[] rawInfo,NpFirstSyncUsbAssembler assembled,byte[] rawEnvelope,ulong sequence){
            if(assembled==null || !assembled.FineReady || assembled.FineSequence!=sequence)throw new InvalidDataException("Fine-AUX source context is incomplete.");
            var confirmed=NpFirstSyncUsb.ParseInfo(rawInfo,assembled.Uid,(int)assembled.Family);
            if(!confirmed.Ready || !confirmed.FineAux || confirmed.Token!=assembled.Token || confirmed.FineCounter64!=assembled.FineCounter64)throw new InvalidDataException("Fine-AUX INFO binding mismatch.");
            anchor=NpFirstSyncUsb.ParseAnchor(rawEnvelope,assembled.Receipt.Raw,assembled.Uid,assembled.Token,assembled.Generation,sequence);
            if(!anchor.SequenceEqual(assembled.FineAnchor))throw new InvalidDataException("Fine-AUX source root mismatch.");
            Uid=assembled.Uid;Token=assembled.Token;Family=assembled.Family;Generation=assembled.Generation;Sequence=sequence;
            info=(byte[])rawInfo.Clone();receipt=(byte[])assembled.Receipt.Raw.Clone();envelope=(byte[])rawEnvelope.Clone();
        }
    }
    internal sealed class NpFirstSyncUsbHistory {
        internal string Uid,Error,Warning;
        internal ulong Token;
        internal uint Generation;
        internal byte MissingMask=127;
        internal int PagesBeforeContext;
        internal NpFirstSyncReceipt Receipt;
        internal bool FineEnabled,RtcEnabled;
        internal string FineError;
        internal NpFirstSyncUsbFineRoot FineRoot;
        internal bool FineQualified {get{return Qualified && FineRoot!=null;}}
        internal bool Qualified {get{return Receipt!=null;}}
        // Transport/source association is not a physical clock or UTC result.
    }
    internal static class NpFirstSyncUsbEvidence {
        internal const string ReceiptFile="usb_first_sync_receipt.bin";
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static void Need(bool ok,string message){if(!ok)throw new InvalidDataException("First-sync USB evidence: "+message);}
        static string S(Dictionary<string,object> d,string key){return Convert.ToString(d[key],Inv);}
        static byte[] Hex(string value,int count){
            Need(value!=null && value.Length==count*2,"raw byte length");var b=new byte[count];
            for(int i=0;i<count;i++)b[i]=Byte.Parse(value.Substring(i*2,2),NumberStyles.HexNumber,Inv);return b;
        }
        internal static void CheckSource(NpFirstSyncReceipt receipt,byte[] source,byte[] cycles,string uid,int family,uint generation){
            Need(receipt!=null && receipt.Family==family && receipt.Generation==generation && generation!=0 && source!=null,"missing source/capture association");
            Need((receipt.Flags&(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts))==(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts),"final all-selected-port framing proof unavailable");
            var a=NpAcquisitionAnchor.Parse(source,BitConverter.ToUInt32(source,8),family);
            Need(a.SourceValid && a.Generation==generation && a.SourceHz==receipt.SourceHz && a.SampleHz==receipt.ApHz,"source anchor/rate disagreement");
            if((a.Flags&2)!=0 && receipt.CounterHz!=0)Need(a.CounterHz==receipt.CounterHz,"source counter frequency disagreement");
            if(cycles!=null){
                var p=NpCycleClockReply.Parse(cycles,BitConverter.ToUInt32(cycles,8),family);
                Need(p.Uid==uid && p.Generation==generation,"source counter UID/generation disagreement");
                if((p.Flags&1)!=0 && receipt.Domain!=0)Need(p.Domain==receipt.Domain && p.CounterHz==receipt.CounterHz,"source counter domain disagreement");
            }
        }
        // Replay raw evidence. Never trust a cached qualification flag or assign
        // page arrival time to its earlier NP source position. No file is changed.
        internal static NpFirstSyncUsbHistory Read(string folder){
            string path=Path.Combine(folder,NpUsbTimingEvidence.FileName);if(!File.Exists(path))return null;
            var result=new NpFirstSyncUsbHistory();NpFirstSyncUsbAssembler assembler=null;NpFirstSyncUsbFineRoot fineRoot=null;
            try{
                var legacy=NpUsbTimingEvidence.Read(folder);Need(legacy!=null,"legacy timing context missing");
                result.Uid=legacy.Uid;result.Token=legacy.Token;result.Generation=legacy.Generation;result.Warning=legacy.Error;
                var json=new JavaScriptSerializer();bool established=false,pageContext=false;
                using(var input=new StreamReader(path)){
                    string line=input.ReadLine();Need(line!=null && line.Length<32768,"metadata missing/oversized");var meta=json.Deserialize<Dictionary<string,object>>(line);
                    object optional;if(!meta.TryGetValue("first_sync_transport",out optional) || optional==null){result.Error="First-sync v2 was not negotiated.";return result;}
                    var transport=(Dictionary<string,object>)optional;Need(Convert.ToInt32(transport["version"],Inv)==2,"negotiation version");
                    var initial=NpFirstSyncUsb.ParseInfo(Hex(S(transport,"initial_info_hex"),64),legacy.Uid,legacy.Family);
                    Need((initial.Flags&5)==5 && (initial.Caps&3)==3,"initial v2 layout unavailable");bool enabled;
                    var negotiation=Hex(S(transport,"negotiation_hex"),64);
                    ulong token=NpFirstSyncUsb.ParseNegotiation(negotiation,legacy.Uid,initial.Token,out enabled);
                    var confirmed=NpFirstSyncUsb.ParseInfo(Hex(S(transport,"confirmed_info_hex"),64),legacy.Uid,legacy.Family);
                    Need(enabled && token==legacy.Token && confirmed.Ready && confirmed.Token==token &&
                        BitConverter.ToUInt32(negotiation,28)==initial.Features && confirmed.Features==initial.Features,"unconfirmed/mismatched v2 token/features");
                    result.FineEnabled=confirmed.FineAux;result.RtcEnabled=confirmed.HardwareRtc;
                    while((line=input.ReadLine())!=null){
                        Need(line.Length<32768,"oversized journal row");
                        if(input.EndOfStream && !line.TrimEnd().EndsWith("}",StringComparison.Ordinal)){result.Warning="Truncated final journal row; verified prefix only.";break;}
                        var row=json.Deserialize<Dictionary<string,object>>(line);string kind=S(row,"kind");
                        if(kind=="record"){
                            bool accepted=S(row,"outcome")=="accepted";NpUsbTimingRecord r;
                            try{r=NpUsbTimingWire.Parse(Hex(S(row,"raw_hex"),108),legacy.Uid,legacy.Family,token,UInt64.Parse(S(row,"container_sequence"),Inv));}
                            catch(Exception){if(accepted)throw;continue;}
                            if(r.Kind==1)continue;
                            if(established)Need(r.Generation==result.Generation,"source generation changed");
                            if(accepted && r.Generation==legacy.Generation && r.Generation!=0){
                                established=true;result.Generation=r.Generation;if(assembler==null)assembler=new NpFirstSyncUsbAssembler(confirmed,token,r.Generation);
                            }
                        }else if(kind=="first_sync_page"){
                            Need(Convert.ToInt64(row["receive_qpc"],Inv)>0,"page QPC missing");
                            if(!established&&confirmed.HardwareRtc){
                                var raw=Hex(S(row,"raw_hex"),108);ulong seq=UInt64.Parse(S(row,"container_sequence"),Inv);
                                result.Generation=NpFirstSyncUsb.CandidatePageGeneration(raw,legacy.Uid,token,seq);
                                assembler=new NpFirstSyncUsbAssembler(confirmed,token,result.Generation);established=true;pageContext=true;
                            }
                            if(!established){result.PagesBeforeContext++;continue;}
                            Need(assembler.Add(Hex(S(row,"raw_hex"),108),UInt64.Parse(S(row,"container_sequence"),Inv)),assembler.Error??"page assembly failed");
                            result.MissingMask=assembler.MissingMask;
                        }else if(kind=="first_sync_source_root"){
                            // Optional root failure cannot erase valid base pages. Reparse
                            // exact bytes/sequence; cached outcome/qualification is not proof.
                            if(result.FineError!=null)continue;
                            try{
                                Need(Convert.ToInt64(row["receive_qpc"],Inv)>0,"source-root QPC missing");
                                Need(result.FineEnabled && established && assembler!=null,"source root without negotiated active context");
                                var raw=Hex(S(row,"raw_hex"),108);ulong sequence=UInt64.Parse(S(row,"container_sequence"),Inv);
                                Need(assembler.FeedAnchor(raw,sequence),assembler.FineError??"source-root assembly failed");
                                if(fineRoot==null)fineRoot=new NpFirstSyncUsbFineRoot(Hex(S(transport,"confirmed_info_hex"),64),assembler,raw,sequence);
                            }catch(Exception ex){result.FineError=ex.Message;}
                        }else if(kind=="close"){
                            object invalid;if(row.TryGetValue("first_sync_invalid",out invalid))Need(!Convert.ToBoolean(invalid,Inv),"live first-sync association invalidated");
                        }
                    }
                }
                Need(established && assembler!=null,"no accepted active source generation");result.MissingMask=assembler.MissingMask;
                Need(assembler.Complete,"first-sync pages incomplete; missing mask "+result.MissingMask);
                if(result.RtcEnabled&&(pageContext||legacy.Anchor==null)){
                    Need(fineRoot!=null&&result.FineError==null,"feature15 requires the strict final kind5 source root");
                    Need(assembler.Receipt.Session==result.Generation,"USB final session/generation mismatch");
                    CheckSource(assembler.Receipt,fineRoot.Anchor,legacy.StartCycles,legacy.Uid,legacy.Family,result.Generation);
                    if(legacy.Anchor!=null)CheckSource(assembler.Receipt,legacy.Anchor,legacy.StartCycles,legacy.Uid,legacy.Family,result.Generation);
                }else CheckSource(assembler.Receipt,legacy.Anchor,legacy.StartCycles,legacy.Uid,legacy.Family,legacy.Generation);
                string body=Path.Combine(folder,ReceiptFile);if(File.Exists(body))Need(File.ReadAllBytes(body).SequenceEqual(assembler.Receipt.Raw),"immutable body cache disagrees with raw pages");
                result.Receipt=assembler.Receipt;
                if(result.FineError==null)result.FineRoot=fineRoot;
                if(result.FineEnabled && result.FineRoot==null && result.FineError==null)result.FineError="Fine-AUX source root was not observed.";
            }catch(Exception ex){result.Error=ex.Message;result.Receipt=null;if(assembler!=null)result.MissingMask=assembler.MissingMask;}
            return result;
        }
    }
}
