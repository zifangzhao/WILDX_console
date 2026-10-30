using System;
using System.IO;

namespace USB512_interface {
    internal sealed class NpSensorFine64Stamp {
        internal uint Mask,Domain,Generation,SourceEpoch,CounterHz,NativeAgeBoundaryMask;
        internal ulong PublicationCounter64;
        internal uint[] Counter=new uint[3];
        // Counter64 is the lower edge of the actual quantized read interval
        // [N,N+1]. Keep the one tick even at UInt64.MaxValue; do not add it in
        // unsigned64 arithmetic. Windows never replace/shift that interval.
        internal ulong[] Counter64=new ulong[3],WindowLower=new ulong[3],WindowUpper=new ulong[3];
    }
    // Explicit future continuity opt-in only, separate from legacy SIF28 v1.
    // A codec cannot qualify a producer's64-bit counter/ISR clock lifetime.
    internal static class NpSensorFine64Wire {
        internal const int Bytes=28,ReadQuantumTicks=1;
        internal const uint Tag=0x02464953,HeaderFlag=4096;
        static readonly uint[] AgeMs={100,200,3000};
        static readonly int[][] Offsets={new[]{88,92,504},new[]{384,388,392}};
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ulong Q(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
        static bool Zero(byte[] b,int at,int count){for(int i=at;i<at+count;i++)if(b[i]!=0)return false;return true;}
        static bool Binding(NpSensorFineBinding b,uint mask){
            if(b==null||mask==0||(mask&~7U)!=0||b.Domain==0||b.Generation==0||b.SourceEpoch==0||
                b.CounterHz<1000000||b.CounterHz>1000000000||(b.SensorValid&~7U)!=0||(mask&~b.SensorValid)!=0||
                b.Sequence==null||b.Sequence.Length!=3||b.SourceMs==null||b.SourceMs.Length!=3)return false;
            for(int i=0;i<3;i++)if((mask&(1U<<i))!=0&&b.Sequence[i]==0)return false;return true;
        }
        static void Context(byte[] p,int at,NpSensorFineBinding b){Put(p,at,b.Domain);Put(p,at+4,b.Generation);Put(p,at+8,b.SourceEpoch);Put(p,at+12,b.CounterHz);}
        static uint Crc(byte[] raw,NpSensorFineBinding b,uint mask){
            var proof=new byte[64];Buffer.BlockCopy(raw,0,proof,0,24);Context(proof,24,b);int n=40;
            for(int i=0;i<3;i++)if((mask&(1U<<i))!=0){Put(proof,n,b.Sequence[i]);Put(proof,n+4,b.SourceMs[i]);n+=8;}
            return NpFirstSyncWire.Crc(proof,n);
        }
        static bool Resolve(NpSensorFine64Stamp s,NpSensorFineBinding b){
            if(s==null||!Binding(b,s.Mask)||s.Counter==null||s.Counter.Length!=3)return false;
            for(int i=0;i<3;i++){
                if((s.Mask&(1U<<i))==0){if(s.Counter[i]!=0)return false;continue;}
                // Exact configured whole-tick policy (nominal ms rounded down).
                ulong age=(ulong)b.CounterHz*AgeMs[i]/1000;
                if(age>=0x100000000UL)return false;
                ulong pub=s.PublicationCounter64,low=pub>age?pub-age:0;
                ulong n=(pub&0xffffffff00000000UL)|s.Counter[i];
                if(n>pub){if(n<0x100000000UL)return false;n-=0x100000000UL;}
                if(n<low&&low-n>1)return false;
                if(n>=0x100000000UL){ulong prior=n-0x100000000UL;if(prior>=low||low-prior<=1)return false;}
                s.Counter64[i]=n;s.WindowLower[i]=low;s.WindowUpper[i]=pub;
                if(n<low||n==pub)s.NativeAgeBoundaryMask|=1U<<i;
            }
            s.Domain=b.Domain;s.Generation=b.Generation;s.SourceEpoch=b.SourceEpoch;s.CounterHz=b.CounterHz;return true;
        }
        static bool StampMatches(NpSensorFine64Stamp s,NpSensorFineBinding b){return s!=null&&b!=null&&s.Domain==b.Domain&&s.Generation==b.Generation&&s.SourceEpoch==b.SourceEpoch&&s.CounterHz==b.CounterHz;}
        internal static NpSensorFineState Parse(byte[] raw,NpSensorFineBinding expected,bool allow,out NpSensorFine64Stamp stamp){
            stamp=null;if(raw==null||raw.Length!=Bytes)return NpSensorFineState.Invalid;
            if(Zero(raw,0,Bytes))return NpSensorFineState.Absent;
            uint mask=(U(raw,0)>>28)&7;
            if(!allow||U(raw,0)!=(Tag|(mask<<28))||!Binding(expected,mask)||U(raw,24)!=Crc(raw,expected,mask))return NpSensorFineState.Invalid;
            var parsed=new NpSensorFine64Stamp {Mask=mask,PublicationCounter64=Q(raw,4)};for(int i=0;i<3;i++)parsed.Counter[i]=U(raw,12+i*4);
            if(!Resolve(parsed,expected))return NpSensorFineState.Invalid;stamp=parsed;return NpSensorFineState.Valid;
        }
        internal static byte[] Pack(NpSensorFine64Stamp stamp,NpSensorFineBinding native,bool allow){
            if(!allow||!StampMatches(stamp,native)||!Binding(native,stamp.Mask)||stamp.Counter==null||stamp.Counter.Length!=3)throw new InvalidDataException("Invalid explicit continuous-counter sensor context.");
            var raw=new byte[Bytes];Put(raw,0,Tag|(stamp.Mask<<28));Put64(raw,4,stamp.PublicationCounter64);for(int i=0;i<3;i++)Put(raw,12+i*4,stamp.Counter[i]);Put(raw,24,Crc(raw,native,stamp.Mask));
            NpSensorFine64Stamp parsed;if(Parse(raw,native,true,out parsed)!=NpSensorFineState.Valid)throw new InvalidDataException("Native sensor counter is outside its independent publication-age window.");return raw;
        }
        static bool Geometry(uint flags,bool sd){
            if((flags&32)==0)return false;
            return sd?(flags&~(511U|1024U|2048U|HeaderFlag))==0&&(flags&2048)!=0&&(flags&128)==0&&(flags&(64|1024))!=(64|1024):
                (flags&~(255U|512U|1024U|HeaderFlag))==0&&(flags&128)!=0&&(flags&64)==0;
        }
        static bool NativeCore(byte[] h,int at,NpSensorFineBinding b,uint mask,uint enabled,uint valid){
            if(!Binding(b,mask)||enabled>3||valid!=b.SensorValid||((valid&3)!=0&&(enabled&1)==0)||((valid&4)!=0&&(enabled&2)==0)||U(h,at+44)>=0x1000000)return false;
            for(int i=0;i<3;i++)if(U(h,at+20+i*4)!=b.Sequence[i]||U(h,at+32+i*4)!=b.SourceMs[i])return false;return true;
        }
        static uint HeaderCrc(byte[] h,NpSensorFineBinding b,bool sd){
            var proof=new byte[140];Context(proof,0,b);Buffer.BlockCopy(h,sd?396:256,proof,16,112);
            for(int i=0;i<3;i++)Put(proof,128+i*4,U(h,Offsets[sd?1:0][i]));return NpFirstSyncWire.Crc(proof,140);
        }
        static NpSensorFineState HeaderParse(byte[] h,NpSensorFineBinding b,bool allow,bool sd,out NpSensorFine64Stamp stamp){
            stamp=null;if(h==null||h.Length!=512)return NpSensorFineState.Invalid;
            int at=sd?396:256;uint flags=U(h,52),word=U(h,at+4),version=sd?4U:3U;
            if((flags&HeaderFlag)==0){if((word&255)==version)return NpSensorFineState.Invalid;foreach(int offset in Offsets[sd?1:0])if(U(h,offset)!=0)return NpSensorFineState.Invalid;return NpSensorFineState.Absent;}
            if(!allow||!Geometry(flags,sd))return NpSensorFineState.Invalid;
            bool absent=Zero(h,at,112)&&U(h,508)==0;foreach(int offset in Offsets[sd?1:0])absent&=U(h,offset)==0;
            if(absent)return NpSensorFineState.Absent;
            uint enabled=(word>>8)&3,valid=(word>>12)&7,mask=(word>>16)&7;
            if(U(h,at)!=(sd?0x32534e53U:0x31534e53U)||word!=(version|(enabled<<8)|(valid<<12)|(mask<<16))||
                !NativeCore(h,at,b,mask,enabled,valid)||U(h,508)!=HeaderCrc(h,b,sd))return NpSensorFineState.Invalid;
            var parsed=new NpSensorFine64Stamp {Mask=mask,PublicationCounter64=Q(h,at+8)};for(int i=0;i<3;i++)parsed.Counter[i]=U(h,Offsets[sd?1:0][i]);
            if(!Resolve(parsed,b))return NpSensorFineState.Invalid;stamp=parsed;return NpSensorFineState.Valid;
        }
        internal static NpSensorFineState UsbParse(byte[] h,NpSensorFineBinding b,bool allow,out NpSensorFine64Stamp stamp){return HeaderParse(h,b,allow,false,out stamp);}
        internal static NpSensorFineState SdParse(byte[] h,NpSensorFineBinding b,bool allow,out NpSensorFine64Stamp stamp){return HeaderParse(h,b,allow,true,out stamp);}
        static byte[] HeaderPack(byte[] source,NpSensorFine64Stamp stamp,NpSensorFineBinding b,bool allow,bool sd){
            if(source==null||source.Length!=512)throw new InvalidDataException("Private512B legacy header required.");
            int at=sd?396:256;uint flags=U(source,52),enabled=U(source,at+8),valid=U(source,at+12);
            if(!allow||!StampMatches(stamp,b)||(flags&HeaderFlag)!=0||!Geometry(flags,sd)||
                U(source,at)!=(sd?0x32534e53U:0x31534e53U)||U(source,at+4)!=(sd?2U:1U)||
                !NativeCore(source,at,b,stamp.Mask,enabled,valid)||
                (sd?U(source,508)!=NpFirstSyncWire.Crc(Slice(source,at,112),112):U(source,508)!=0))throw new InvalidDataException("Invalid original native sensor header/context.");
            foreach(int offset in Offsets[sd?1:0])if(U(source,offset)!=0)throw new InvalidDataException("Occupied native counter carrier.");
            // Validate raw-age aliases without mutating the caller's stamp.
            Pack(stamp,b,allow);var h=(byte[])source.Clone();Put(h,52,flags|HeaderFlag);
            Put(h,at+4,(sd?4U:3U)|(enabled<<8)|(valid<<12)|(stamp.Mask<<16));Put64(h,at+8,stamp.PublicationCounter64);
            for(int i=0;i<3;i++)Put(h,Offsets[sd?1:0][i],stamp.Counter[i]);Put(h,508,HeaderCrc(h,b,sd));return h;
        }
        static byte[] Slice(byte[] b,int at,int bytes){var copy=new byte[bytes];Buffer.BlockCopy(b,at,copy,0,bytes);return copy;}
        internal static byte[] UsbPack(byte[] source,NpSensorFine64Stamp stamp,NpSensorFineBinding b,bool allow){return HeaderPack(source,stamp,b,allow,false);}
        internal static byte[] SdPack(byte[] source,NpSensorFine64Stamp stamp,NpSensorFineBinding b,bool allow){return HeaderPack(source,stamp,b,allow,true);}
    }
}
