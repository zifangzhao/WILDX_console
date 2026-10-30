using System;
using System.Globalization;
using System.IO;

namespace USB512_interface {
    internal sealed class NpFirstSyncUsbInfo {
        internal string Uid;
        internal uint Family,Flags,Generation,Caps,Features,Offset;
        internal ulong Token;
        internal bool FineAux {get{return Features==3 || Features==7 || Features==15;}}
        internal bool FineCounter64 {get{return Features==7 || Features==15;}}
        internal bool HardwareRtc {get{return Features==15;}}
        internal bool Ready {get{return (Flags&7)==7 && (Caps&3)==3 && (Features==1 || Features==3 || Features==7 || Features==15) && Token!=0;}}
    }
    internal sealed class NpFirstSyncUsbPage {
        internal byte Index;
        internal uint Hash;
        internal ulong Sequence;
        internal byte[] Slice;
    }
    // Isolated v2 codec. Legacy NPTI/NPTN and R2 production paths are untouched.
    internal static class NpFirstSyncUsb {
        internal const ushort InfoIndex=0xffff;
        internal const int EnvelopeBytes=108,PageCount=7,SliceBytes=48;
        internal const uint Feature=1,FeatureFineAux=2,FeatureFineCounter64=4,FeatureRtcStart=8,InfoMagic=0x4954504e,NegotiateMagic=0x4e54504e,EnvelopeMagic=0x4254504e,PageMagic=0x4750534e;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ushort H(byte[] b,int at){return BitConverter.ToUInt16(b,at);}
        static ulong L(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
        static bool Zero(byte[] b,int at,int count){for(int i=at;i<at+count;i++)if(b[i]!=0)return false;return true;}
        static void Require(bool ok,string message){if(!ok)throw new InvalidDataException("NP first-sync USB: "+message);}
        internal static uint[] UidWords(string uid){
            Require(uid!=null && uid.Length==24,"exact UID required");var result=new uint[3];
            for(int i=0;i<3;i++)Require(UInt32.TryParse(uid.Substring(i*8,8),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out result[i]),"invalid UID");
            Require((result[0]|result[1]|result[2])!=0 && (result[0]&result[1]&result[2])!=UInt32.MaxValue,"invalid UID");return result;
        }
        static string ReadUid(byte[] b,int at){return U(b,at).ToString("X8")+U(b,at+4).ToString("X8")+U(b,at+8).ToString("X8");}
        static void MatchUid(byte[] b,int at,string uid){UidWords(uid);Require(String.Equals(ReadUid(b,at),uid,StringComparison.OrdinalIgnoreCase),"UID mismatch");}
        internal static byte[] Negotiate(string uid,ulong token,bool enable=true,ulong lastToken=0){
            return NegotiateFeatures(uid,token,enable,lastToken,Feature);
        }
        internal static byte[] NegotiateFineAux(string uid,ulong token,bool enable=true,ulong lastToken=0){
            return NegotiateFeatures(uid,token,enable,lastToken,Feature|FeatureFineAux);
        }
        // The producer must attest an actual continuous counter64 root whose
        // BEFORE high word is zero. Kind5 still carries the same raw low32 NPAR.
        internal static byte[] NegotiateFineCounter64(string uid,ulong token,bool enable=true,ulong lastToken=0){
            return NegotiateFeatures(uid,token,enable,lastToken,Feature|FeatureFineAux|FeatureFineCounter64);
        }
        internal static byte[] NegotiateRtcStart(string uid,ulong token,bool enable=true,ulong lastToken=0){
            return NegotiateFeatures(uid,token,enable,lastToken,Feature|FeatureFineAux|FeatureFineCounter64|FeatureRtcStart);
        }
        static byte[] NegotiateFeatures(string uid,ulong token,bool enable,ulong lastToken,uint features){
            var words=UidWords(uid);var b=new byte[64];Put(b,0,NegotiateMagic);Put(b,4,2U|(enable?0x10000U:0));Put64(b,8,token);
            for(int i=0;i<3;i++)Put(b,16+4*i,words[i]);Put(b,28,enable?features:0);Put(b,60,NpFirstSyncWire.Crc(b,60));
            bool parsed;ParseNegotiation(b,uid,lastToken,out parsed);return b;
        }
        internal static ulong ParseNegotiation(byte[] b,string uid,ulong lastToken,out bool enable){
            Require(b!=null && b.Length==64,"negotiation size");
            Require(U(b,0)==NegotiateMagic && H(b,4)==2 && H(b,6)<=1 && U(b,60)==NpFirstSyncWire.Crc(b,60) && Zero(b,32,28),"negotiation version/flags/CRC/reserved");
            MatchUid(b,16,uid);enable=H(b,6)!=0;ulong token=L(b,8);
            Require(enable?token!=0 && token!=lastToken && (U(b,28)==1 || U(b,28)==3 || U(b,28)==7 || U(b,28)==15):token==0 && U(b,28)==0,"feature mask/fresh token");return token;
        }
        // Query vendor80 C0IN64 value0/indexFFFF only while stopped/prepared.
        // ENABLED/token means v2 opt-in, not an existing legacy R2 token.
        // Features is the supported mask while disabled, then the selected
        // mask while enabled. FineAux alone never means a cold INFO is ready.
        internal static NpFirstSyncUsbInfo ParseInfo(byte[] b,string uid,int family){
            Require(b!=null && b.Length==64 && (family==1 || family==2),"INFO size/family");
            Require(U(b,0)==InfoMagic && H(b,4)==2 && U(b,60)==NpFirstSyncWire.Crc(b,60),"INFO tag/version/CRC");MatchUid(b,16,uid);
            uint flags=H(b,6),caps=U(b,36),features=U(b,56);ulong token=L(b,8);
            Require((flags&~7U)==0 && (flags&1)!=0 && (caps&~7U)==0 && (features==1 || features==3 || features==7 || features==15) && U(b,28)==1000 &&
                U(b,40)==EnvelopeBytes && U(b,44)==1 && U(b,48)==(family==1?192U:396U) && U(b,52)==1,"INFO layout/flags/features");
            Require(((flags&2)!=0)==(token!=0) && ((flags&2)==0 || (flags&7)==7 && (caps&3)==3),"INFO enabled-token/layout state");
            return new NpFirstSyncUsbInfo{Uid=ReadUid(b,16),Family=(uint)family,Flags=flags,Caps=caps,Features=features,Token=token,Generation=U(b,32),Offset=U(b,48)};
        }
        internal static void ValidateBody(byte[] b){
            Require(b!=null && b.Length==64 && U(b,0)==PageMagic && b[4]==1 && b[5]<PageCount && b[6]==PageCount && b[7]==0 &&
                U(b,60)==NpFirstSyncWire.Crc(b,60),"page body tag/version/index/count/reserved/CRC");
            Require(b[5]!=6 || Zero(b,52,8) && U(b,48)==U(b,8),"last-page padding/receipt CRC field");
        }
        internal static NpFirstSyncUsbPage ParsePage(byte[] b,string uid,ulong token,uint generation,ulong containingSequence){
            Require(b!=null && b.Length==EnvelopeBytes && token!=0 && generation!=0 && containingSequence!=UInt64.MaxValue,"page envelope size/context");
            Require(U(b,0)==EnvelopeMagic && H(b,4)==1 && H(b,6)==4 && L(b,8)==token && U(b,16)==generation &&
                L(b,32)==containingSequence && U(b,104)==NpFirstSyncWire.Crc(b,104),"page envelope identity/token/generation/sequence/CRC");MatchUid(b,20,uid);
            var body=new byte[64];Buffer.BlockCopy(b,40,body,0,64);ValidateBody(body);var slice=new byte[SliceBytes];Buffer.BlockCopy(body,12,slice,0,slice.Length);
            return new NpFirstSyncUsbPage{Index=body[5],Hash=U(body,8),Sequence=containingSequence,Slice=slice};
        }
        // A fresh feature15 stream can deliberately omit legacy kind2/3.
        // This is only a provisional page-set generation, not source proof:
        // the complete receipt and following kind5 must still bind it.
        internal static uint CandidatePageGeneration(byte[] b,string uid,ulong token,ulong sequence){
            Require(b!=null&&b.Length==EnvelopeBytes,"candidate page extent");uint generation=U(b,16);
            var page=ParsePage(b,uid,token,generation,sequence);Require(page.Index==0,"candidate context must begin at page0");return generation;
        }
        internal static byte[] Anchor(byte[] anchor,byte[] rawReceipt,string uid,ulong token,uint generation,ulong containingSequence){
            Require(token!=0&&generation!=0&&containingSequence!=UInt64.MaxValue,"anchor context");
            var receipt=NpFirstSyncWire.ValidateSourceAnchor(anchor,rawReceipt);Require(receipt.Generation==generation,"anchor generation");
            var b=new byte[EnvelopeBytes];Put(b,0,EnvelopeMagic);Put(b,4,0x50001);Put64(b,8,token);Put(b,16,generation);
            var words=UidWords(uid);for(int i=0;i<3;i++)Put(b,20+4*i,words[i]);Put64(b,32,containingSequence);Buffer.BlockCopy(anchor,0,b,40,64);Put(b,104,NpFirstSyncWire.Crc(b,104));return b;
        }
        internal static byte[] ParseAnchor(byte[] b,byte[] rawReceipt,string uid,ulong token,uint generation,ulong containingSequence){
            Require(b!=null&&b.Length==EnvelopeBytes&&token!=0&&generation!=0&&containingSequence!=UInt64.MaxValue,"anchor size/context");
            Require(U(b,0)==EnvelopeMagic&&H(b,4)==1&&H(b,6)==5&&L(b,8)==token&&U(b,16)==generation&&
                L(b,32)==containingSequence&&U(b,104)==NpFirstSyncWire.Crc(b,104),"anchor token/generation/sequence/CRC");MatchUid(b,20,uid);
            var anchor=new byte[64];Buffer.BlockCopy(b,40,anchor,0,64);var receipt=NpFirstSyncWire.ValidateSourceAnchor(anchor,rawReceipt);
            Require(receipt.Generation==generation,"anchor receipt generation");return anchor;
        }
    }
    // One bounded page set per active capture. Optional metadata failure never
    // changes neural state. The owning host records Error and leaves UTC unknown.
    internal sealed class NpFirstSyncUsbAssembler {
        readonly string uid;
        readonly uint family,generation;
        readonly ulong token;
        readonly bool fineAux,fineCounter64;
        readonly byte[][] slices=new byte[7][];
        readonly byte[] raw=new byte[NpFirstSyncWire.Bytes];
        uint seen,hash;
        bool haveSequence;
        ulong lastSequence;
        byte lastIndex;
        byte[] fineAnchor;
        ulong fineSequence;
        internal bool FineFailed {get;private set;}
        internal string FineError {get;private set;}
        internal string Uid {get{return uid;}}
        internal ulong Token {get{return token;}}
        internal uint Family {get{return family;}}
        internal uint Generation {get{return generation;}}
        internal ulong FineSequence {get{return fineSequence;}}
        internal bool FineCounter64 {get{return fineCounter64;}}
        internal byte[] FineAnchor {get{return fineAnchor==null?null:(byte[])fineAnchor.Clone();}}
        internal bool FineReady {get{return fineAux&&Complete&&!FineFailed&&fineAnchor!=null;}}
        internal bool Failed {get;private set;}
        internal string Error {get;private set;}
        internal NpFirstSyncReceipt Receipt {get;private set;}
        internal byte MissingMask {get{return (byte)(127U&~seen);}}
        internal bool Complete {get{return !Failed && Receipt!=null;}}
        internal NpFirstSyncUsbAssembler(NpFirstSyncUsbInfo info,ulong negotiatedToken,uint activeGeneration){
            if(info==null || !info.Ready || info.Token!=negotiatedToken || activeGeneration==0)throw new InvalidDataException("NP first-sync USB: matching v2 opt-in/active generation required");
            NpFirstSyncUsb.UidWords(info.Uid);uid=info.Uid;family=info.Family;token=info.Token;generation=activeGeneration;fineAux=info.FineAux;fineCounter64=info.FineCounter64;
            if(family!=1 && family!=2)throw new InvalidDataException("NP first-sync USB: family");
        }
        bool Reject(string message){Failed=true;Error=message;Receipt=null;return false;}
        static bool Same(byte[] a,byte[] b){if(a==null || b==null || a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        bool RejectFine(string message){FineFailed=true;FineError=message;return false;}
        internal bool FeedAnchor(byte[] envelope,ulong containingSequence){
            if(FineFailed)return false;
            if(!fineAux||!Complete)return RejectFine("Fine-AUX opt-in and a complete receipt must precede the source root");
            byte[] anchor;
            try{anchor=NpFirstSyncUsb.ParseAnchor(envelope,raw,uid,token,generation,containingSequence);}
            catch(InvalidDataException e){return RejectFine(e.Message);}
            if(fineAnchor!=null)return containingSequence==fineSequence&&Same(anchor,fineAnchor) || RejectFine("Replayed/conflicting source root");
            if(containingSequence<=lastSequence)return RejectFine("Source root must follow every receipt page");
            fineAnchor=anchor;fineSequence=containingSequence;return true;
        }
        internal bool Add(byte[] envelope,ulong containingSequence){
            if(Failed)return false;
            NpFirstSyncUsbPage page;
            try{page=NpFirstSyncUsb.ParsePage(envelope,uid,token,generation,containingSequence);}
            catch(InvalidDataException e){return Reject(e.Message);}
            if(seen!=0 && page.Hash!=hash)return Reject("Mixed receipt CRC");
            bool duplicate=(seen&(1U<<page.Index))!=0;
            if(duplicate && !Same(slices[page.Index],page.Slice))return Reject("Conflicting duplicate page");
            if(haveSequence && (page.Sequence<lastSequence || page.Sequence==lastSequence && (!duplicate || page.Index!=lastIndex)))return Reject("Replayed/conflicting containing sequence");
            if(haveSequence && page.Sequence==lastSequence)return true;
            haveSequence=true;lastSequence=page.Sequence;lastIndex=page.Index;hash=page.Hash;
            if(duplicate)return true;
            slices[page.Index]=page.Slice;seen|=1U<<page.Index;
            int count=page.Index==6?40:48;Buffer.BlockCopy(page.Slice,0,raw,page.Index*48,count);
            if(seen!=127)return true;
            try{
                var receipt=NpFirstSyncWire.Parse(raw);
                if(BitConverter.ToUInt32(raw,324)!=hash || receipt.Generation!=generation || receipt.Family!=family)return Reject("Assembled receipt generation/family/CRC mismatch");
                Receipt=receipt;return true;
            }catch(InvalidDataException e){return Reject(e.Message);}
        }
    }
}
