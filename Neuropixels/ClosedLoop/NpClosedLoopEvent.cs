using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WildNpClosedLoop
{
    // File/export parser only. It performs no device operations or callbacks.
    public sealed class NpClosedLoopEvent
    {
        public const int Bytes=64;
        public uint Session,Sequence,ConfigCrc,SourceEpoch,Dropped,Reason;
        public byte Kind,Lane,Probe,Flags;
        public ushort Channel;
        public ulong SourceTick,InputEndTick;
        public float Envelope,Threshold;
        static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
        public static uint Crc(byte[] b,int count)
        {
            if(b==null||count<0||count>b.Length)throw new ArgumentOutOfRangeException("count");
            uint c=uint.MaxValue;for(int i=0;i<count;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((c&1)!=0?0xedb88320U:0);}
            return ~c;
        }
        static bool Finite(float x){return !Single.IsNaN(x)&&!Single.IsInfinity(x);}
        public static NpClosedLoopEvent Parse(byte[] b)
        {
            if(b==null||b.Length!=Bytes||Encoding.ASCII.GetString(b,0,4)!="NPCL"||b[4]!=1||U(b,60)!=Crc(b,60))
                throw new InvalidDataException("Invalid closed-loop event version, length or CRC.");
            var e=new NpClosedLoopEvent {Kind=b[5],Lane=b[6],Flags=b[7],Session=U(b,8),Sequence=U(b,12),ConfigCrc=U(b,16),
                Probe=b[20],Channel=BitConverter.ToUInt16(b,22),SourceTick=BitConverter.ToUInt64(b,24),InputEndTick=BitConverter.ToUInt64(b,32),
                SourceEpoch=U(b,40),Envelope=BitConverter.ToSingle(b,44),Threshold=BitConverter.ToSingle(b,48),Dropped=U(b,52),Reason=U(b,56)};
            if(b[21]!=0||e.Kind<1||e.Kind>5||e.Lane>1||e.Probe<1||e.Probe>3||e.Channel>=384||e.Session==0||e.Sequence==0||
               (e.Flags&~1)!=0||e.SourceEpoch==0||e.InputEndTick<e.SourceTick||((e.Flags&1)==0&&(e.SourceTick!=0||e.InputEndTick!=0))||!Finite(e.Envelope)||!Finite(e.Threshold)||
               (e.Kind!=5&&((e.Flags&1)==0||e.Reason!=0))||(e.Kind==5&&(e.Reason<1||e.Reason>7)))
                throw new InvalidDataException("Unsupported closed-loop event fields.");
            return e;
        }
        public static string KindName(byte kind)
        {return kind==1?"training":kind==2?"ready":kind==3?"rise":kind==4?"fall":kind==5?"gap":"unknown";}

        // Native tick columns are authoritative. Clock metadata belongs to the
        // confirmed configuration; never infer UTC from host receipt time.
        public static long Export(Stream input,TextWriter csv,uint session,uint configCrc)
        {
            if(input==null||csv==null||!input.CanRead||session==0)throw new ArgumentException("A readable event stream and confirmed session are required.");
            csv.WriteLine("session,config_crc32,sequence,kind,lane,probe,channel,source_epoch,source_time_valid,source_tick,input_end_tick,envelope,threshold,cumulative_drops,reason,missing_records_before");
            var bytes=new byte[Bytes];uint previous=0,drops=0;long total=0;var ticks=new ulong[2];var seen=new bool[2];var epochs=new uint[2];
            while(true){int got=0,n;while(got<Bytes&&(n=input.Read(bytes,got,Bytes-got))>0)got+=n;
                if(got==0)return total;if(got!=Bytes)throw new EndOfStreamException("Truncated closed-loop event; export is incomplete.");
                var e=Parse(bytes);
                bool changed=epochs[e.Lane]!=0&&e.SourceEpoch!=epochs[e.Lane];
                if(e.Session!=session||e.ConfigCrc!=configCrc||e.Sequence<=previous||e.Dropped<drops||
                   e.SourceEpoch<epochs[e.Lane]||(changed&&e.Kind!=5)||
                   (e.Kind==5&&(e.Flags&1)==0&&epochs[e.Lane]!=0&&!changed)||
                   ((e.Flags&1)!=0&&seen[e.Lane]&&!changed&&e.SourceTick<ticks[e.Lane]))
                    throw new InvalidDataException("Closed-loop event session, config or sequence changed; export is incomplete.");
                uint missing=e.Sequence-previous-1;
                csv.WriteLine(String.Join(",",new[]{e.Session.ToString(CultureInfo.InvariantCulture),e.ConfigCrc.ToString("X8"),e.Sequence.ToString(CultureInfo.InvariantCulture),
                    KindName(e.Kind),e.Lane.ToString(CultureInfo.InvariantCulture),e.Probe.ToString(CultureInfo.InvariantCulture),e.Channel.ToString(CultureInfo.InvariantCulture),
                    e.SourceEpoch.ToString(CultureInfo.InvariantCulture),((e.Flags&1)!=0?"1":"0"),e.SourceTick.ToString(CultureInfo.InvariantCulture),e.InputEndTick.ToString(CultureInfo.InvariantCulture),
                    e.Envelope.ToString("R",CultureInfo.InvariantCulture),e.Threshold.ToString("R",CultureInfo.InvariantCulture),
                    e.Dropped.ToString(CultureInfo.InvariantCulture),e.Reason.ToString(CultureInfo.InvariantCulture),missing.ToString(CultureInfo.InvariantCulture)}));
                previous=e.Sequence;drops=e.Dropped;total++;if(changed)seen[e.Lane]=false;epochs[e.Lane]=e.SourceEpoch;
                if((e.Flags&1)!=0){ticks[e.Lane]=e.SourceTick;seen[e.Lane]=true;}
            }
        }
    }
}
