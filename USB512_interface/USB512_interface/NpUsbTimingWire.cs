using System;
using System.Globalization;
using System.IO;

namespace USB512_interface {
    internal sealed class NpUsbTimingInfo {
        internal uint Flags,Caps,Generation,Offset;
        internal ulong Token;
        internal string Uid;
        internal bool Ready {get{return (Flags&7)==7&&(Caps&3)==3&&Token!=0;}}
    }
    internal sealed class NpUsbTimingRecord {
        internal byte[] Raw,Body;
        internal ushort Kind;
        internal ulong Token,Sequence;
        internal uint Generation,Id,Flags,Domain,RootDomain,RxMs,SealMs,RxCycles,SealCycles,Hz;
        internal string Uid;
        internal bool Valid {get{return (Flags&1)!=0;}}
        internal bool Fine {get{return (Flags&2)!=0;}}
        internal bool Prearm {get{return (Flags&4)!=0;}}
    }
    // Shared optional R2 body; never used to infer an arrival-latency bound.
    internal static class NpUsbTimingWire {
        internal const int Bytes=108;
        internal const uint Info=0x4954504e,Negotiate=0x4e54504e,Request=0x5154504e,Envelope=0x4254504e,Clock=0x4d54504e;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ushort H(byte[] b,int at){return BitConverter.ToUInt16(b,at);}
        static ulong L(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        static void Put64(byte[] b,int at,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,8);}
        static bool Zero(byte[] b,int at,int count){for(int i=at;i<at+count;i++)if(b[i]!=0)return false;return true;}
        static void Require(bool valid,string error){if(!valid)throw new InvalidDataException("USB timing: "+error);}
        static string Uid(byte[] b,int at){return U(b,at).ToString("X8")+U(b,at+4).ToString("X8")+U(b,at+8).ToString("X8");}
        static void PutUid(byte[] b,int at,string uid){Require(uid!=null&&uid.Length==24&&uid!=new string('0',24)&&uid!=new string('F',24),"exact MCU UID required");
            for(int i=0;i<3;i++)Put(b,at+4*i,UInt32.Parse(uid.Substring(i*8,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture));}
        static void Crc(byte[] b){Put(b,b.Length-4,NpAcquisitionAnchor.Crc(b,b.Length-4));}
        internal static byte[] Enable(string uid,ulong token,bool enable=true) {
            Require(enable?token!=0:token==0,"invalid negotiation token");var b=new byte[64];Put(b,0,Negotiate);Put(b,4,1U|(enable?0x10000U:0));Put64(b,8,token);PutUid(b,16,uid);Crc(b);return b;
        }
        internal static byte[] Query(uint id,ulong token,uint generation) {
            Require(id!=0&&token!=0,"request ID/token required");var b=new byte[64];Put(b,0,Request);Put(b,4,0x10001);Put(b,8,id);Put64(b,16,token);Put(b,24,generation);Crc(b);return b;
        }
        internal static NpUsbTimingInfo ParseInfo(byte[] b,string uid,int family) {
            Require(b!=null&&b.Length==64&&U(b,0)==Info&&H(b,4)==1&&U(b,60)==NpAcquisitionAnchor.Crc(b,60),"invalid capability reply");
            Require((family==1||family==2)&&(H(b,6)&~7)==0&&(U(b,36)&~7U)==0&&U(b,28)==1000&&U(b,40)==Bytes&&U(b,44)==1&&U(b,52)==1&&U(b,56)==0,"unsupported capability layout");
            Require(U(b,48)==(family==1?192U:396U)&&String.Equals(Uid(b,16),uid,StringComparison.OrdinalIgnoreCase),"capability UID/slot mismatch");
            Require(((H(b,6)&2)!=0)==(L(b,8)!=0),"invalid enabled/token state");
            return new NpUsbTimingInfo{Flags=H(b,6),Caps=U(b,36),Token=L(b,8),Uid=Uid(b,16),Generation=U(b,32),Offset=U(b,48)};
        }
        internal static NpUsbTimingRecord Parse(byte[] b,string uid,int family,ulong token,ulong sequence) {
            Require(b!=null&&b.Length==Bytes&&U(b,0)==Envelope&&H(b,4)==1&&U(b,104)==NpAcquisitionAnchor.Crc(b,104),"invalid envelope/version/CRC");
            Require((family==1||family==2)&&H(b,6)>=1&&H(b,6)<=3&&token!=0&&L(b,8)==token&&L(b,32)==sequence&&String.Equals(Uid(b,20),uid,StringComparison.OrdinalIgnoreCase),"envelope identity/token/sequence mismatch");
            var r=new NpUsbTimingRecord {Raw=(byte[])b.Clone(),Body=new byte[64],Kind=H(b,6),Token=L(b,8),Generation=U(b,16),Uid=Uid(b,20),Sequence=L(b,32)};
            Buffer.BlockCopy(b,40,r.Body,0,64);var body=r.Body;
            Require(U(body,60)==NpAcquisitionAnchor.Crc(body,60),"invalid body CRC");
            if(r.Kind==1) {
                Require(U(body,0)==Clock&&U(body,4)==1&&U(body,8)!=0&&(U(body,12)&~0xff1fU)==0&&(U(body,12)>>8)==family,"invalid clock type/flags/ID");
                r.Id=U(body,8);r.Flags=U(body,12);r.Domain=U(body,16);r.RxMs=U(body,24);r.SealMs=U(body,28);r.RxCycles=U(body,32);r.SealCycles=U(body,36);r.Hz=U(body,40);r.RootDomain=U(body,44);
                Require(U(body,20)==r.Generation&&Uid(body,48)==r.Uid&&r.Prearm==(sequence==UInt64.MaxValue)&&unchecked(r.SealMs-r.RxMs)<0x80000000U,"clock generation/UID/phase mismatch");
                Require(r.Fine?r.Domain!=0&&r.Hz>0:r.Domain==0&&r.RxCycles==0&&r.SealCycles==0&&r.RootDomain==0&&r.Hz==1000,"mixed or invalid counter coordinates");
            } else if(r.Kind==2) {
                var anchor=NpAcquisitionAnchor.Parse(body,U(body,8),family);Require(anchor.Generation==r.Generation&&sequence!=UInt64.MaxValue,"source generation mismatch");
            } else {
                var clock=NpCycleClockReply.Parse(body,U(body,8),family);Require(clock.Generation==r.Generation&&clock.Uid==r.Uid&&sequence!=UInt64.MaxValue,"startup counter identity mismatch");
            }
            return r;
        }
        internal static bool IsAbsent(byte[] b,int offset){return b!=null&&offset>=0&&b.Length-offset>=Bytes&&U(b,offset)==0;}
    }
}
