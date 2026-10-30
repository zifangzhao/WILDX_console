using System;
using System.Collections.Generic;
using System.IO;

namespace USB512_interface {
    // V1 carriers only. The NSG1 codec is independent of transport and T0.
    internal static class NpImuGridCarrier {
        internal const int SdOffset=62144,SdEnd=65024;
        static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
        internal static bool IsPhase(byte[] frame){return frame!=null&&frame.Length==84&&U(frame,0)==0x5047534eU;}
        internal static uint Crc(byte[] b,int o,int n){uint c=~0U;for(int i=0;i<n;i++){c^=b[o+i];for(int k=0;k<8;k++)c=(c>>1)^(0xedb88320U&unchecked(0U-(c&1)));}return ~c;}
        static void Need(bool b,string why){if(!b)throw new InvalidDataException("IMU grid carrier: "+why);}
        internal static bool IsUsb(byte[] b,int o,int n){if(b==null||o<0||n<8||b.Length-o<n)return false;const string m="NPGRID01";for(int i=0;i<8;i++)if(b[o+i]!=m[i])return false;return true;}
        internal static byte[] Usb(byte[] b,int o,int n,uint session,out uint sequence){
            Need(IsUsb(b,o,n)&&n==506,"USB extent/tag");
            Need(session!=0&&U(b,o+12)==session&&U(b,o+502)==Crc(b,o,502),"USB session/CRC");
            uint count=U(b,o+16);Need(count>=48&&count<=480,"USB inner extent");
            for(int i=20+(int)count;i<502;i++)Need(b[o+i]==0,"USB nonzero reserved padding");
            var frame=new byte[count];Buffer.BlockCopy(b,o+20,frame,0,(int)count);if(!IsPhase(frame))NpImuGridWire.Parse(frame);sequence=U(b,o+8);return frame;
        }
        internal static bool SdDeclared(byte[] h){
            Need(h!=null&&h.Length==512,"SD metadata extent");uint magic=U(h,104),version=U(h,108);
            if(magic==0&&version==0)return false;
            Need(U(h,8)==6&&magic==NpImuGridWire.Magic&&version==1,"SD declaration/version");return true;
        }
        internal static bool SdMetadata(byte[] h,byte[] f){
            bool selected=SdDeclared(h);Need(SdDeclared(f)==selected&&U(h,104)==U(f,104)&&U(h,108)==U(f,108)&&U(h,124)==0&&U(f,124)==0,"SD header/footer declaration");return selected;
        }
        // Global descriptor/padding integrity is neural framing, not an
        // optional inner-frame error. Caller checks the metadata sector CRC.
        internal static int SdSpan(byte[] h,byte[] descriptor){
            bool selected=SdDeclared(h);Need(SdDeclared(descriptor)==selected&&U(h,104)==U(descriptor,104)&&U(h,108)==U(descriptor,108),"SD block declaration");
            uint n=U(descriptor,124);Need(selected?n<=SdEnd-SdOffset:n==0,"SD occupied extent");return (int)n;
        }
        internal static void SdPadding(byte[] raw,int occupied){
            Need(raw!=null&&raw.Length==SdEnd&&occupied>=0&&occupied<=SdEnd-SdOffset,"SD raw extent");
            for(int i=SdOffset+occupied;i<SdEnd;i++)Need(raw[i]==0,"SD undeclared/nonzero padding");
        }
        internal static IEnumerable<byte[]> SdFrames(byte[] raw,int occupied){
            int used=0;while(used<occupied){Need(occupied-used>=48,"SD truncated inner header");bool phase=U(raw,SdOffset+used)==0x5047534eU;uint n=phase?36U:U(raw,SdOffset+used+32);Need(phase||n<=432&&n%36==0,"SD inner length");int bytes=48+(int)n;Need(bytes<=occupied-used,"SD truncated inner payload");var frame=new byte[bytes];Buffer.BlockCopy(raw,SdOffset+used,frame,0,bytes);if(!phase)NpImuGridWire.Parse(frame);used+=bytes;yield return frame;}
        }
    }
}
