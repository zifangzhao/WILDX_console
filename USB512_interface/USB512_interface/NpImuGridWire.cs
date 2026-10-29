using System;
using System.IO;

namespace USB512_interface {
    internal sealed class NpImuGridBatch {
        internal ulong FirstSlot;
        internal uint CaptureGeneration;
        internal ushort SlotCount,ObservedMask,AgValid,MagValid,AgRepeat,MagRepeat,ErrorMask;
        // Compact ascending observed-bit order; AX/AY/AZ microG, GX/GY/GZ
        // milli-dps, MX/MY/MZ nanoT. Missing slots have no physical row.
        internal int[][] Rows=new int[0][];
    }
    // Pure NSG1 RAW codec. No carrier, sequence/UTC, clock I/O, scheduling,
    // T0/precision inference or changes to existing SNS1/recording behavior.
    internal static class NpImuGridWire {
        internal const uint Magic=0x3147534e;
        internal const int HeaderBytes=48,RowBytes=36,MaxSlots=12,MaxBytes=480;
        static ushort H(byte[] b,int at){return (ushort)(b[at]|b[at+1]<<8);}
        static uint U(byte[] b,int at){return (uint)b[at]|(uint)b[at+1]<<8|(uint)b[at+2]<<16|(uint)b[at+3]<<24;}
        static ulong L(byte[] b,int at){return U(b,at)|(ulong)U(b,at+4)<<32;}
        static void Put16(byte[] b,int at,ushort n){b[at]=(byte)n;b[at+1]=(byte)(n>>8);}
        static void Put32(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(8*i));}
        static void Put64(byte[] b,int at,ulong n){Put32(b,at,(uint)n);Put32(b,at+4,(uint)(n>>32));}
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("IMU grid: "+why);}
        static int Count(ushort mask){int n=0;while(mask!=0){n+=mask&1;mask>>=1;}return n;}
        static void Header(NpImuGridBatch b){
            Need(b!=null&&b.CaptureGeneration!=0&&b.SlotCount>=1&&b.SlotCount<=MaxSlots,"generation/slot count");
            Need(b.FirstSlot<=UInt64.MaxValue-b.SlotCount,"slot range overflow");
            uint allowed=(1U<<b.SlotCount)-1U;
            Need((b.ObservedMask&~allowed)==0&&((b.AgValid|b.MagValid|b.AgRepeat|b.MagRepeat|b.ErrorMask)&~b.ObservedMask)==0&&
                (b.AgRepeat&~b.AgValid)==0&&(b.MagRepeat&~b.MagValid)==0,"observation/validity/repeat/error masks");
        }
        internal static uint Crc(byte[] raw){
            Need(raw!=null&&raw.Length>=HeaderBytes&&raw.Length<=MaxBytes,"CRC frame extent");
            uint crc=UInt32.MaxValue;
            for(int i=0;i<raw.Length;i++)if(i<44||i>=48){crc^=raw[i];for(int k=0;k<8;k++)crc=(crc>>1)^(0xedb88320U&unchecked(0U-(crc&1)));}
            return ~crc;
        }
        internal static byte[] Pack(NpImuGridBatch b){
            Header(b);int rows=Count(b.ObservedMask);Need(b.Rows!=null&&b.Rows.Length==rows,"compact row count");
            for(int r=0;r<rows;r++)Need(b.Rows[r]!=null&&b.Rows[r].Length==9,"nine signed physical values per row");
            var raw=new byte[HeaderBytes+rows*RowBytes];Put32(raw,0,Magic);raw[4]=1;raw[5]=1;Put16(raw,6,48);Put64(raw,8,b.FirstSlot);
            Put16(raw,16,b.SlotCount);Put16(raw,18,36);Put16(raw,20,b.ObservedMask);Put16(raw,22,b.AgValid);Put16(raw,24,b.MagValid);
            Put16(raw,26,b.AgRepeat);Put16(raw,28,b.MagRepeat);Put16(raw,30,b.ErrorMask);Put32(raw,32,(uint)(rows*RowBytes));Put32(raw,40,b.CaptureGeneration);
            for(int r=0;r<rows;r++)for(int c=0;c<9;c++)Put32(raw,48+r*36+c*4,unchecked((uint)b.Rows[r][c]));
            Put32(raw,44,Crc(raw));return raw;
        }
        internal static NpImuGridBatch Parse(byte[] raw){
            Need(raw!=null&&raw.Length>=HeaderBytes&&raw.Length<=MaxBytes,"frame extent");
            Need(U(raw,0)==Magic&&raw[4]==1&&raw[5]==1&&H(raw,6)==48&&H(raw,18)==36&&U(raw,36)==0,"tag/version/encoding/geometry/flags");
            var b=new NpImuGridBatch{FirstSlot=L(raw,8),CaptureGeneration=U(raw,40),SlotCount=H(raw,16),ObservedMask=H(raw,20),
                AgValid=H(raw,22),MagValid=H(raw,24),AgRepeat=H(raw,26),MagRepeat=H(raw,28),ErrorMask=H(raw,30)};
            Header(b);int rows=Count(b.ObservedMask);
            Need(U(raw,32)==rows*RowBytes&&raw.Length==HeaderBytes+rows*RowBytes&&U(raw,44)==Crc(raw),"payload extent/CRC");
            b.Rows=new int[rows][];
            for(int r=0;r<rows;r++){b.Rows[r]=new int[9];for(int c=0;c<9;c++)b.Rows[r][c]=unchecked((int)U(raw,48+r*36+c*4));}
            return b;
        }
    }
}
