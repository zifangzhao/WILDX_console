using System;
using System.IO;

namespace USB512_interface {
    internal class NpImuGridPhaseBinding {
        internal byte[] Uid;
        internal uint Session,CaptureGeneration,SourceEpoch,CounterDomain;
        internal ulong Token;
    }
    internal sealed class NpImuGridPhaseRecord : NpImuGridPhaseBinding {
        internal ulong FirstAcceptedSlot;
        internal long LowerNumerator,UpperNumerator;
        internal uint Denominator,MaximumJitter,Flags;
    }
    // Structural validation is not source qualification. Match/Read requires
    // independent exact context. No absolute/per-slot time or SAMPLE0_LOCK.
    internal static class NpImuGridPhase {
        internal const int Bytes=84;
        internal const uint Magic=0x5047534e,IntervalValid=1,JitterValid=2;
        static uint U(byte[] b,int at){return (uint)b[at]|(uint)b[at+1]<<8|(uint)b[at+2]<<16|(uint)b[at+3]<<24;}
        static ulong Q(byte[] b,int at){return U(b,at)|(ulong)U(b,at+4)<<32;}
        static void Put(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(8*i));}
        static void Put64(byte[] b,int at,ulong n){Put(b,at,(uint)n);Put(b,at+4,(uint)(n>>32));}
        static void Need(bool yes,string why){if(!yes)throw new InvalidDataException("IMU grid phase: "+why);}
        static bool Binding(NpImuGridPhaseBinding b){
            if(b==null||b.Uid==null||b.Uid.Length!=12||b.Session==0||b.CaptureGeneration==0||b.SourceEpoch==0||b.CounterDomain==0||b.Token==0)return false;
            int any=0,notFf=0;foreach(byte n in b.Uid){any|=n;notFf|=n^255;}return any!=0&&notFf!=0;
        }
        static void Validate(NpImuGridPhaseRecord p){
            Need(Binding(p)&&p.FirstAcceptedSlot!=UInt64.MaxValue&&(p.Flags&~3U)==0,"source identity/slot/flags");
            if((p.Flags&IntervalValid)!=0)Need(p.LowerNumerator>=0&&p.UpperNumerator>=p.LowerNumerator&&p.Denominator!=0,"interval");
            else Need(p.LowerNumerator==Int64.MinValue&&p.UpperNumerator==Int64.MinValue&&p.Denominator==0,"unknown interval sentinels");
            Need((p.Flags&JitterValid)!=0?p.MaximumJitter!=UInt32.MaxValue:p.MaximumJitter==UInt32.MaxValue,"jitter sentinel");
        }
        internal static uint Crc(byte[] b){
            Need(b!=null&&b.Length==Bytes,"CRC extent");uint c=UInt32.MaxValue;
            for(int i=0;i<80;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^(0xedb88320U&unchecked(0U-(c&1)));}return ~c;
        }
        internal static byte[] Pack(NpImuGridPhaseRecord p){
            Validate(p);var b=new byte[Bytes];Put(b,0,Magic);b[4]=1;b[6]=Bytes;Buffer.BlockCopy(p.Uid,0,b,8,12);
            Put(b,20,p.Session);Put(b,24,p.CaptureGeneration);Put(b,28,p.SourceEpoch);Put(b,32,p.CounterDomain);Put64(b,36,p.Token);
            Put64(b,44,p.FirstAcceptedSlot);Put64(b,52,unchecked((ulong)p.LowerNumerator));Put64(b,60,unchecked((ulong)p.UpperNumerator));
            Put(b,68,p.Denominator);Put(b,72,p.MaximumJitter);Put(b,76,p.Flags);Put(b,80,Crc(b));return b;
        }
        internal static NpImuGridPhaseRecord Parse(byte[] b){
            Need(b!=null&&b.Length==Bytes,"extent");
            Need(U(b,0)==Magic&&b[4]==1&&b[5]==0&&b[6]==Bytes&&b[7]==0&&U(b,80)==Crc(b),"magic/version/size/CRC");
            var uid=new byte[12];Buffer.BlockCopy(b,8,uid,0,12);
            var p=new NpImuGridPhaseRecord{Uid=uid,Session=U(b,20),CaptureGeneration=U(b,24),SourceEpoch=U(b,28),CounterDomain=U(b,32),
                Token=Q(b,36),FirstAcceptedSlot=Q(b,44),LowerNumerator=unchecked((long)Q(b,52)),UpperNumerator=unchecked((long)Q(b,60)),
                Denominator=U(b,68),MaximumJitter=U(b,72),Flags=U(b,76)};Validate(p);return p;
        }
        static bool Same(NpImuGridPhaseRecord p,NpImuGridPhaseBinding e){
            if(!Binding(e)||p.Session!=e.Session||p.CaptureGeneration!=e.CaptureGeneration||p.SourceEpoch!=e.SourceEpoch||p.CounterDomain!=e.CounterDomain||p.Token!=e.Token)return false;
            for(int i=0;i<12;i++)if(p.Uid[i]!=e.Uid[i])return false;return true;
        }
        internal static bool Match(byte[] b,NpImuGridPhaseBinding expected){
            if(!Binding(expected))return false;try{return Same(Parse(b),expected);}catch(InvalidDataException){return false;}
        }
        internal static NpImuGridPhaseRecord Read(byte[] b,NpImuGridPhaseBinding expected){
            var p=Parse(b);Need(Same(p,expected),"independent source context mismatch");return p;
        }
    }
}
