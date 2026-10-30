using System;
using System.IO;

namespace USB512_interface {
    internal enum NpSensorFineState {Invalid=-1,Absent=0,Valid=1}
    internal sealed class NpSensorFineBinding {
        internal uint Domain,Generation,SourceEpoch,CounterHz,SensorValid;
        internal uint[] Sequence=new uint[3],SourceMs=new uint[3];
    }
    internal sealed class NpSensorFineStamp {
        internal uint Mask,Domain,Generation;
        internal uint[] Counter=new uint[3];
    }
    // Native observation/cache proof only. This codec performs no clock I/O,
    // unwrapping, phase inference or NN admission. Seal once against original
    // native identities, then copy with that exact snapshot/queue entry.
    internal static class NpSensorFineWire {
        internal const int Bytes=28;
        internal const uint Tag=0x01464953;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static void Put(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(8*i));}
        static bool Binding(NpSensorFineBinding b,uint mask){
            if(b==null||mask==0||(mask&~7U)!=0||b.Domain==0||b.Generation==0||b.SourceEpoch==0||
                b.CounterHz<1000000||b.CounterHz>1000000000||(b.SensorValid&~7U)!=0||(mask&~b.SensorValid)!=0||
                b.Sequence==null||b.Sequence.Length!=3||b.SourceMs==null||b.SourceMs.Length!=3)return false;
            for(int i=0;i<3;i++)if((mask&(1U<<i))!=0&&b.Sequence[i]==0)return false;return true;
        }
        static uint Crc(byte[] prefix,NpSensorFineBinding b,uint mask){
            var proof=new byte[56];Buffer.BlockCopy(prefix,0,proof,0,24);Put(proof,24,b.SourceEpoch);Put(proof,28,b.CounterHz);int n=32;
            for(int i=0;i<3;i++)if((mask&(1U<<i))!=0){Put(proof,n,b.Sequence[i]);Put(proof,n+4,b.SourceMs[i]);n+=8;}
            return NpFirstSyncWire.Crc(proof,n);
        }
        internal static byte[] Pack(NpSensorFineStamp stamp,NpSensorFineBinding native){
            if(stamp==null||!Binding(native,stamp.Mask)||stamp.Domain!=native.Domain||stamp.Generation!=native.Generation||
                stamp.Counter==null||stamp.Counter.Length!=3)throw new InvalidDataException("Invalid native sensor fine binding.");
            for(int i=0;i<3;i++)if((stamp.Mask&(1U<<i))==0&&stamp.Counter[i]!=0)throw new InvalidDataException("Inactive sensor fine counter must be zero.");
            var raw=new byte[Bytes];Put(raw,0,Tag|(stamp.Mask<<28));Put(raw,4,stamp.Domain);Put(raw,8,stamp.Generation);
            for(int i=0;i<3;i++)Put(raw,12+i*4,stamp.Counter[i]);Put(raw,24,Crc(raw,native,stamp.Mask));return raw;
        }
        // Malformed optional evidence never throws or changes NN state.
        internal static NpSensorFineState Parse(byte[] raw,NpSensorFineBinding expected,out NpSensorFineStamp stamp){
            stamp=null;if(raw==null||raw.Length!=Bytes)return NpSensorFineState.Invalid;
            bool zero=true;foreach(byte b in raw)zero&=b==0;if(zero)return NpSensorFineState.Absent;
            uint tag=U(raw,0),mask=(tag>>28)&7;
            if(tag!=(Tag|(mask<<28))||!Binding(expected,mask)||U(raw,4)!=expected.Domain||U(raw,8)!=expected.Generation)return NpSensorFineState.Invalid;
            var counters=new uint[3];for(int i=0;i<3;i++){counters[i]=U(raw,12+i*4);if((mask&(1U<<i))==0&&counters[i]!=0)return NpSensorFineState.Invalid;}
            if(U(raw,24)!=Crc(raw,expected,mask))return NpSensorFineState.Invalid;
            stamp=new NpSensorFineStamp {Mask=mask,Domain=expected.Domain,Generation=expected.Generation,Counter=counters};return NpSensorFineState.Valid;
        }
        internal const uint V2Flag=4096;
        static readonly int[][] V2Offsets={new[]{88,92,504},new[]{384,388,392}};
        static void Context(byte[] p,int at,NpSensorFineBinding b){Put(p,at,b.Domain);Put(p,at+4,b.Generation);Put(p,at+8,b.SourceEpoch);Put(p,at+12,b.CounterHz);}
        static bool Core(byte[] h,int at,NpSensorFineBinding b,uint mask){
            if(!Binding(b,mask)||U(h,at+12)!=b.SensorValid)return false;
            for(int i=0;i<3;i++)if(U(h,at+20+i*4)!=b.Sequence[i]||U(h,at+32+i*4)!=b.SourceMs[i])return false;return true;
        }
        static bool Geometry(uint flags,bool sd){
            if((flags&32)==0)return false;
            return sd?(flags&~(511U|1024U|2048U|V2Flag))==0&&(flags&2048)!=0&&(flags&128)==0&&(flags&(64|1024))!=(64|1024):
                (flags&~(255U|512U|1024U|V2Flag))==0&&(flags&128)!=0&&(flags&64)==0;
        }
        static uint V2Crc(byte[] h,NpSensorFineBinding b,uint mask,bool sd){
            var proof=new byte[sd?140:56];if(sd){Context(proof,0,b);Buffer.BlockCopy(h,384,proof,16,124);return NpFirstSyncWire.Crc(proof,140);}
            Put(proof,0,U(h,260));for(int i=0;i<3;i++)Put(proof,4+i*4,U(h,V2Offsets[0][i]));Context(proof,16,b);int n=32;
            for(int i=0;i<3;i++)if((mask&(1U<<i))!=0){Put(proof,n,U(h,276+i*4));Put(proof,n+4,U(h,288+i*4));n+=8;}return NpFirstSyncWire.Crc(proof,n);
        }
        static bool Zero(byte[] b,int at,int count){for(int i=0;i<count;i++)if(b[at+i]!=0)return false;return true;}
        static NpSensorFineState V2Parse(byte[] h,NpSensorFineBinding b,bool allow,bool sd,out NpSensorFineStamp stamp){
            stamp=null;if(h==null||h.Length!=512)return NpSensorFineState.Invalid;int at=sd?396:256;int[] offsets=V2Offsets[sd?1:0];uint flags=U(h,52),version=U(h,at+4);
            if((flags&V2Flag)==0){if((version&255)==(sd?3:2))return NpSensorFineState.Invalid;foreach(int o in offsets)if(U(h,o)!=0)return NpSensorFineState.Invalid;return NpSensorFineState.Absent;}
            if(!allow||!Geometry(flags,sd))return NpSensorFineState.Invalid;
            if(sd?Zero(h,384,128):U(h,256)==0)return NpSensorFineState.Absent;
            uint mask=(version>>8)&7;if(U(h,at)!=(sd?0x32534e53U:0x31534e53U)||version!=((sd?3U:2U)|(mask<<8))||!Core(h,at,b,mask))return NpSensorFineState.Invalid;
            var counter=new uint[3];for(int i=0;i<3;i++){counter[i]=U(h,offsets[i]);if((mask&(1U<<i))==0&&counter[i]!=0)return NpSensorFineState.Invalid;}
            if(U(h,508)!=V2Crc(h,b,mask,sd))return NpSensorFineState.Invalid;
            stamp=new NpSensorFineStamp {Mask=mask,Domain=b.Domain,Generation=b.Generation,Counter=counter};return NpSensorFineState.Valid;
        }
        internal static NpSensorFineState UsbParse(byte[] header,NpSensorFineBinding expected,bool allow,out NpSensorFineStamp stamp){return V2Parse(header,expected,allow,false,out stamp);}
        internal static NpSensorFineState SdParse(byte[] header,NpSensorFineBinding expected,bool allow,out NpSensorFineStamp stamp){return V2Parse(header,expected,allow,true,out stamp);}
        static byte[] V2Pack(byte[] source,NpSensorFineStamp stamp,NpSensorFineBinding b,bool allow,bool sd){
            if(source==null||source.Length!=512)throw new InvalidDataException("A private 512B V2 header source is required.");
            int at=sd?396:256;int[] offsets=V2Offsets[sd?1:0];uint flags=U(source,52);
            if(!allow||(flags&V2Flag)!=0||!Geometry(flags,sd)||stamp==null||!Core(source,at,b,stamp.Mask)||stamp.Domain!=b.Domain||stamp.Generation!=b.Generation||
                stamp.Counter==null||stamp.Counter.Length!=3||U(source,at)!=(sd?0x32534e53U:0x31534e53U)||U(source,at+4)!=(sd?2U:1U)||(!sd&&U(source,508)!=0))
                throw new InvalidDataException("Invalid opt-in V2 native sensor fine source/context.");
            for(int i=0;i<3;i++)if(U(source,offsets[i])!=0||((stamp.Mask&(1U<<i))==0&&stamp.Counter[i]!=0))throw new InvalidDataException("V2 fine counter carrier is occupied/inactive.");
            var h=(byte[])source.Clone();Put(h,52,flags|V2Flag);Put(h,at+4,(sd?3U:2U)|(stamp.Mask<<8));for(int i=0;i<3;i++)Put(h,offsets[i],stamp.Counter[i]);Put(h,508,V2Crc(h,b,stamp.Mask,sd));return h;
        }
        internal static byte[] UsbPack(byte[] source,NpSensorFineStamp stamp,NpSensorFineBinding native,bool allow){return V2Pack(source,stamp,native,allow,false);}
        internal static byte[] SdPack(byte[] source,NpSensorFineStamp stamp,NpSensorFineBinding native,bool allow){return V2Pack(source,stamp,native,allow,true);}
    }
}
