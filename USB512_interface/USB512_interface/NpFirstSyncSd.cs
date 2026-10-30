using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace USB512_interface {
    internal sealed class NpFirstSyncSdRequest {
        internal ushort Version,Operation;
        internal uint Id;
        internal string Uid;
        internal ulong Token;
        internal byte[] Raw;
    }
    internal sealed class NpFirstSyncSdReply {
        internal ushort Version;
        internal uint Result,Supported,RecordFeatures,Session,Generation,SourceEpoch,ProfileCrc;
        // Association may still be pending; this never means recording/running.
        internal bool Associated {get{return Result==0 && (Version==1 && RecordFeatures==1 || Version==2 && RecordFeatures==9 || Version==3 && RecordFeatures==25);}}
        internal bool FineCounter64 {get{return Associated && Version==3;}}
    }
    internal sealed class NpFirstSyncSdBound {
        internal string Uid;
        internal ulong Token;
        internal NpFirstSyncReceipt Receipt;
    }
    // Isolated per-record opt-in codec; no controller, card I/O, state latch or
    // automatic replay/fallback. Legacy card/range/options admission stays intact.
    internal static class NpFirstSyncSd {
        internal const uint RequestMagic=0x4453504e,ReplyMagic=0x4153504e;
        internal const ushort Caps=0,Start=1,Status=2,Scheduled=3;
        internal const uint FeatureFirstSync=1,FeatureScheduled=2,FeatureFineClock=4,FeatureFineAux=8,FeatureFineCounter64=16;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ushort H(byte[] b,int at){return BitConverter.ToUInt16(b,at);}
        static ulong L(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
        static bool Zero(byte[] b,int at,int count){for(int i=at;i<at+count;i++)if(b[i]!=0)return false;return true;}
        static void Require(bool ok,string message){if(!ok)throw new InvalidDataException("NP first-sync SD: "+message);}
        static bool Hz(uint hz){return hz==7500 || hz==15000 || hz==20000 || hz==30000;}
        static uint[] UidWords(string uid){
            Require(uid!=null && uid.Length==24,"exact UID required");var words=new uint[3];
            for(int i=0;i<3;i++)Require(UInt32.TryParse(uid.Substring(i*8,8),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out words[i]),"UID");
            Require((words[0]|words[1]|words[2])!=0 && (words[0]&words[1]&words[2])!=UInt32.MaxValue,"UID");return words;
        }
        static string ReadUid(byte[] b,int at){return U(b,at).ToString("X8")+U(b,at+4).ToString("X8")+U(b,at+8).ToString("X8");}
        static void PutUid(byte[] b,int at,string uid){var words=UidWords(uid);for(int i=0;i<3;i++)Put(b,at+4*i,words[i]);}
        static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
        static NpFirstSyncSdRequest CheckRequest(byte[] b,int family,string uid,bool admit){
            Require(b!=null && b.Length==64 && (family==1 || family==2),"request size/family");UidWords(uid);
            Require(U(b,0)==RequestMagic && (H(b,4)==1 || H(b,4)==2 || H(b,4)==3) && U(b,8)!=0 && String.Equals(ReadUid(b,12),uid,StringComparison.OrdinalIgnoreCase) &&
                U(b,60)==NpFirstSyncWire.Crc(b,60),"request tag/version/ID/UID/CRC");
            ushort op=H(b,6);ulong token=L(b,24);Require(op<=Scheduled,"request operation");
            if(op==Caps)Require(token==0 && Zero(b,32,28),"CAPS token/payload");
            else{
                Require(token!=0,"nonzero record token");
                if(op==Status)Require(Zero(b,32,28),"STATUS payload");
                else Require(!(admit && family==1 && op==Scheduled) && U(b,32)!=0 && (U(b,32)&~7U)==0 && Hz(U(b,36)) &&
                    (family!=2 || Zero(b,52,8)),"START token/profile/reserved/family support");
            }
            return new NpFirstSyncSdRequest{Version=H(b,4),Operation=op,Id=U(b,8),Uid=ReadUid(b,12),Token=token,Raw=(byte[])b.Clone()};
        }
        internal static NpFirstSyncSdRequest ParseRequest(byte[] b,int family,string uid){return CheckRequest(b,family,uid,true);}
        internal static bool FreshStart(byte[] b,int family,string uid,ulong lastToken){var q=ParseRequest(b,family,uid);return (q.Operation==Start || q.Operation==Scheduled) && q.Token!=lastToken;}
        internal static bool CachedDuplicate(byte[] b,byte[] accepted,int family,string uid){
            var q=ParseRequest(b,family,uid);ParseRequest(accepted,family,uid);if(q.Operation!=Start && q.Operation!=Scheduled)return false;
            for(int i=0;i<64;i++)if(b[i]!=accepted[i])return false;return true;
        }
        internal static byte[] Query(ushort op,uint id,int family,string uid,ulong token=0){
            return QueryVersion(1,op,id,family,uid,token);
        }
        internal static byte[] QueryFineAux(ushort op,uint id,int family,string uid,ulong token=0){
            return QueryVersion(2,op,id,family,uid,token);
        }
        internal static byte[] QueryFineCounter64(ushort op,uint id,int family,string uid,ulong token=0){
            return QueryVersion(3,op,id,family,uid,token);
        }
        static byte[] QueryVersion(ushort version,ushort op,uint id,int family,string uid,ulong token){
            Require(op==Caps || op==Status,"use legacy START wrapper");var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,version|((uint)op<<16));Put(b,8,id);PutUid(b,12,uid);Put64(b,24,token);Seal(b);
            ParseRequest(b,family,uid);return b;
        }
        static void Legacy(byte[] b,int family){
            Require(b!=null && b.Length==64 && (family==1 || family==2),"legacy size/family");
            Require(U(b,0)==(family==1?0x31444c4eU:0x32444c4eU) && U(b,4)==(family==1?1U:2U) && U(b,12)!=0 && U(b,60)==NpFirstSyncWire.Crc(b,60) &&
                Zero(b,44,16) && U(b,16)!=0 && (U(b,16)&~7U)==0 && Hz(U(b,20)) &&
                (U(b,8)==2 || family==2 && U(b,8)==4) && (family!=2 || Zero(b,36,8)),"legacy frame/op/profile/reserved; V1 scheduled op6 is not wrapped");
        }
        internal static byte[] WrapLegacy(byte[] legacy,int family,string uid,ulong token,ulong lastToken=0){
            return WrapLegacyVersion(1,legacy,family,uid,token,lastToken);
        }
        internal static byte[] WrapLegacyFineAux(byte[] legacy,int family,string uid,ulong token,ulong lastToken=0){
            return WrapLegacyVersion(2,legacy,family,uid,token,lastToken);
        }
        internal static byte[] WrapLegacyFineCounter64(byte[] legacy,int family,string uid,ulong token,ulong lastToken=0){
            return WrapLegacyVersion(3,legacy,family,uid,token,lastToken);
        }
        static byte[] WrapLegacyVersion(ushort version,byte[] legacy,int family,string uid,ulong token,ulong lastToken){
            Legacy(legacy,family);var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,version|((U(legacy,8)==4?3U:1U)<<16));Put(b,8,U(legacy,12));PutUid(b,12,uid);Put64(b,24,token);
            Buffer.BlockCopy(legacy,16,b,32,28);Seal(b);Require(FreshStart(b,family,uid,lastToken),"new START token was reused");return b;
        }
        internal static byte[] Unwrap(byte[] request,int family,string uid){
            var q=ParseRequest(request,family,uid);Require(q.Operation==Start || q.Operation==Scheduled,"query has no legacy START");
            var b=new byte[64];Put(b,0,family==1?0x31444c4eU:0x32444c4eU);Put(b,4,family==1?1U:2U);Put(b,8,q.Operation==Scheduled?4U:2U);Put(b,12,q.Id);
            Buffer.BlockCopy(request,32,b,16,28);Seal(b);return b;
        }
        internal static NpFirstSyncSdReply ParseReply(byte[] b,byte[] request,int family){
            Require(request!=null && request.Length==64,"reply request context");var q=CheckRequest(request,family,ReadUid(request,12),false);
            Require(b!=null && b.Length==64 && U(b,0)==ReplyMagic && U(b,60)==NpFirstSyncWire.Crc(b,60),"reply tag/size/CRC");
            for(int i=4;i<32;i++)Require(b[i]==request[i],"reply operation/ID/UID/token mismatch");
            var r=new NpFirstSyncSdReply{Version=q.Version,Result=U(b,32),Supported=U(b,36),RecordFeatures=U(b,40),Session=U(b,44),Generation=U(b,48),SourceEpoch=U(b,52),ProfileCrc=U(b,56)};
            uint recordFeatures=q.Version==3?25U:q.Version==2?9U:1U,allowed=(family==1?5U:7U)|(q.Version>=2?FeatureFineAux|FeatureFineCounter64:0);
            Require(r.Result<=5 && (r.Supported&~allowed)==0 && ((r.Supported&6)==0 || (r.Supported&1)!=0) &&
                ((r.Supported&FeatureFineAux)==0 || (r.Supported&13)==13) && ((r.Supported&FeatureFineCounter64)==0 || (r.Supported&29)==29) &&
                (r.RecordFeatures&~recordFeatures)==0,"reply result/features");
            if(q.Version>=2 && r.Result==0)Require((r.Supported&(q.Version==3?29U:13U))==(q.Version==3?29U:13U),"fine AUX/continuous64 protocol support dependencies");
            if(r.Result!=0 || q.Operation==Caps)Require(r.RecordFeatures==0 && r.Session==0 && r.Generation==0 && r.SourceEpoch==0 && r.ProfileCrc==UInt32.MaxValue,"error/CAPS cannot expose record context");
            else Require(r.RecordFeatures==recordFeatures && (r.Supported&1)!=0 && (q.Operation!=Scheduled || family==2 && (r.Supported&2)!=0),"successful record association/features");
            return r;
        }
        internal static byte[] BuildReply(byte[] request,int family,NpFirstSyncSdReply r){
            Require(request!=null && request.Length==64 && r!=null,"reply arguments");var b=new byte[64];Put(b,0,ReplyMagic);Buffer.BlockCopy(request,4,b,4,28);
            Put(b,32,r.Result);Put(b,36,r.Supported);Put(b,40,r.RecordFeatures);Put(b,44,r.Session);Put(b,48,r.Generation);Put(b,52,r.SourceEpoch);Put(b,56,r.ProfileCrc);Seal(b);
            ParseReply(b,request,family);return b;
        }
        // Common UTC2 framing/clock only. Existing provider validates its opaque
        // CL/overflow area80..159; this codec preserves those bytes exactly.
        static void Base(byte[] b,int family,uint session,uint hz){
            Require(b!=null && b.Length==512 && (family==1 || family==2) && session!=0 && Hz(hz),"sector size/family/profile");
            Require(Encoding.ASCII.GetString(b,0,8)==(family==1?"N1SDUTC2":"N2SDUTC2") && U(b,8)==2 && U(b,12)==session && U(b,72)==hz && U(b,76)==1000 &&
                U(b,508)==NpFirstSyncWire.Crc(b,508),"legacy UTC2 identity/seal");
            uint flags=U(b,16);ulong utc=L(b,32);Require((flags&~(family==1?7U:15U))==0,"legacy UTC2 flags");
            if((flags&1)!=0)Require(U(b,20)!=0 && U(b,24)>=1000 && U(b,24)<=5000000 && utc>=1577836800000000UL && utc<=4102444800000000UL,"legacy UTC2 clock anchor");
            if((flags&3)!=3)return;
            uint before=U(b,family==1?64:48),after=U(b,family==1?68:52),delta=unchecked(before-U(b,28)),span=unchecked(after-before);
            ulong lo=L(b,family==1?48:56),hi=L(b,family==1?56:64);
            Require(delta<0x80000000 && span<0x80000000 && (ulong)delta+span<0x80000000UL && lo==utc+(ulong)delta*1000 && hi==lo+(ulong)span*1000,"legacy UTC2 source-zero bracket");
        }
        internal static byte[] BindSector(byte[] legacy,byte[] rawReceipt,int family,string uid,ulong token){
            var receipt=NpFirstSyncWire.Parse(rawReceipt);UidWords(uid);Require(token!=0 && receipt.Family==family,"bound token/family");Base(legacy,family,receipt.Session,receipt.ApHz);
            Require(Zero(legacy,160,348),"legacy extension must be entirely zero before binding");var output=(byte[])legacy.Clone();Buffer.BlockCopy(rawReceipt,0,output,160,NpFirstSyncWire.Bytes);
            PutUid(output,488,uid);Put64(output,500,token);Seal(output);return output;
        }
        // Offline callers omit expected identity and receive the recorded UID/
        // token for association. An unextended legacy sector returns null.
        internal static NpFirstSyncSdBound ReadSector(byte[] b,int family,uint session,uint hz,string expectedUid=null,ulong expectedToken=0){
            Base(b,family,session,hz);if(Zero(b,160,348))return null;
            var raw=new byte[NpFirstSyncWire.Bytes];Buffer.BlockCopy(b,160,raw,0,raw.Length);var receipt=NpFirstSyncWire.Parse(raw);string uid=ReadUid(b,488);UidWords(uid);ulong token=L(b,500);
            Require(token!=0 && receipt.Family==family && receipt.Session==session && receipt.ApHz==hz,"bound receipt identity/profile/token");
            if(expectedUid!=null){UidWords(expectedUid);Require(String.Equals(uid,expectedUid,StringComparison.OrdinalIgnoreCase),"bound UID mismatch");}
            Require(expectedToken==0 || token==expectedToken,"bound token mismatch");return new NpFirstSyncSdBound{Uid=uid,Token=token,Receipt=receipt};
        }
    }
}
