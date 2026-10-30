using System;
using System.IO;
using System.Text;

namespace USB512_interface {
    // Pure candidate NPGRID02 envelope. The caller owns negotiation, parent
    // block continuity and error policy; parsing proves no clock or NP phase.
    internal static class NpV2GridSector {
        internal const int Bytes=512,HeaderBytes=32,MaxFrameBytes=480;
        static ushort H(byte[] b,int at){return (ushort)(b[at]|b[at+1]<<8);}
        static uint U(byte[] b,int at){return (uint)b[at]|(uint)b[at+1]<<8|(uint)b[at+2]<<16|(uint)b[at+3]<<24;}
        static ulong L(byte[] b,int at){return U(b,at)|(ulong)U(b,at+4)<<32;}
        static void Put16(byte[] b,int at,ushort n){b[at]=(byte)n;b[at+1]=(byte)(n>>8);}
        static void Put32(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(i*8));}
        static void Put64(byte[] b,int at,ulong n){Put32(b,at,(uint)n);Put32(b,at+4,(uint)(n>>32));}
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("V2 grid sector: "+why);}
        static uint HeaderCrc(byte[] b){
            uint crc=UInt32.MaxValue;
            for(int i=0;i<28;i++){crc^=b[i];for(int bit=0;bit<8;bit++)crc=(crc>>1)^(0xedb88320U&unchecked(0U-(crc&1)));}
            return ~crc;
        }
        static byte[] Wrap(uint generation,ulong precedingBlock,byte[] frame){
            Need(generation!=0&&frame!=null&&frame.Length<=MaxFrameBytes,"generation/frame extent");
            var sector=new byte[Bytes];Encoding.ASCII.GetBytes("NPGRID02").CopyTo(sector,0);
            Put32(sector,8,1);Put32(sector,12,generation);Put64(sector,16,precedingBlock);Put16(sector,24,(ushort)frame.Length);
            Put32(sector,28,HeaderCrc(sector));Buffer.BlockCopy(frame,0,sector,HeaderBytes,frame.Length);return sector;
        }
        static byte[] Payload(byte[] sector,uint generation,ulong precedingBlock){
            Need(sector!=null&&sector.Length==Bytes&&generation!=0,"sector extent/expected generation");
            Need(Encoding.ASCII.GetString(sector,0,8)=="NPGRID02"&&U(sector,8)==1&&U(sector,12)==generation&&
                L(sector,16)==precedingBlock&&H(sector,26)==0&&U(sector,28)==HeaderCrc(sector),"header/context/CRC");
            int count=H(sector,24);Need(count<=MaxFrameBytes,"inner frame extent");
            var frame=new byte[count];Buffer.BlockCopy(sector,HeaderBytes,frame,0,count);
            for(int i=HeaderBytes+count;i<Bytes;i++)Need(sector[i]==0,"nonzero padding");
            return frame;
        }
        internal static byte[] Pack(uint generation,ulong precedingBlock,byte[] frame){
            Need(generation!=0&&frame!=null&&frame.Length<=MaxFrameBytes,"generation/frame extent");
            Need(NpImuGridWire.Parse(frame).CaptureGeneration==generation,"inner generation mismatch");
            return Wrap(generation,precedingBlock,frame);
        }
        internal static NpImuGridBatch Parse(byte[] sector,uint generation,ulong precedingBlock){
            var batch=NpImuGridWire.Parse(Payload(sector,generation,precedingBlock));
            Need(batch.CaptureGeneration==generation,"inner generation mismatch");
            return batch;
        }
        // Only the declared SD16 fixed-stride layout permits empty positions.
        // A padding sector is not a missing sample or an ordinal advance.
        internal static byte[] ReadSd16Data(byte[] sector,uint generation,ulong precedingBlock){
            var raw=Payload(sector,generation,precedingBlock);
            if(raw.Length!=0)Need(NpImuGridWire.Parse(raw).CaptureGeneration==generation,"inner generation mismatch");
            return raw;
        }
        internal static byte[] PackSd16Empty(uint generation,ulong precedingBlock){return Wrap(generation,precedingBlock,new byte[0]);}
        // This validates one NSGP proof against independent expected context;
        // a transport consumer owns once-per-session and phase-before-row policy.
        internal static byte[] PackPhase(uint generation,ulong precedingBlock,NpImuGridPhaseRecord phase,NpImuGridPhaseBinding expected){
            var frame=NpImuGridPhase.Pack(phase);var proof=NpImuGridPhase.Read(frame,expected);
            Need(proof.CaptureGeneration==generation,"phase/wrapper generation mismatch");
            return Wrap(generation,precedingBlock,frame);
        }
        internal static NpImuGridPhaseRecord ReadPhase(byte[] sector,uint generation,ulong precedingBlock,NpImuGridPhaseBinding expected){
            var proof=NpImuGridPhase.Read(Payload(sector,generation,precedingBlock),expected);
            Need(proof.CaptureGeneration==generation,"phase/wrapper generation mismatch");return proof;
        }
        internal static bool TryParse(byte[] sector,uint generation,ulong precedingBlock,out NpImuGridBatch batch,out string error){
            batch=null;error=null;
            try{batch=Parse(sector,generation,precedingBlock);return true;}
            catch(InvalidDataException failure){error=failure.Message;return false;}
        }
    }
}
