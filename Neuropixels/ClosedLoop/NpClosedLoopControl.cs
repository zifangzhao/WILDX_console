using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WildNpClosedLoop
{
    public sealed class NpClosedLoopChannel
    {
        public byte Probe,Filter=6;
        public ushort Channel;
        public float Gain=5;
        public bool Rectify=true;
    }
    public sealed class NpClosedLoopControl
    {
        public const uint Caps=0,Configure=1,Status=2;
        public uint Operation,RequestId,ConfigCrc,ApHz,TrainingStartMs,TrainingDurationMs,RectifyMask;
        public NpClosedLoopChannel[] Channels;
        static uint U(byte[] b,int n){return BitConverter.ToUInt32(b,n);}
        static void Put(byte[] b,int n,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,n,4);}
        static bool Zero(byte[] b,int a,int n){for(int i=0;i<n;i++)if(b[a+i]!=0)return false;return true;}
        public static bool IsRequest(byte[] b){return b!=null&&b.Length==64&&U(b,0)==0x4343504eU;}
        public static bool MatchesReply(byte[] request,byte[] response)
        {
            try{var q=Parse(request);ParseReply(response,q.Operation,q.RequestId);return true;}
            catch(InvalidDataException){return false;}
        }
        public static byte[] Request(uint operation,uint id,uint apHz=0,uint startMs=0,uint durationMs=0,NpClosedLoopChannel[] channels=null)
        {
            var b=new byte[64];Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("NPCC"),0,b,0,4);Put(b,4,1);Put(b,8,operation);Put(b,12,id);
            channels=channels??new NpClosedLoopChannel[0];if(channels.Length>2)throw new ArgumentException("Select at most two detector channels.");
            Put(b,20,apHz);Put(b,24,startMs);Put(b,28,durationMs);Put(b,32,(uint)channels.Length);uint mask=0;
            for(int i=0;i<channels.Length;i++){
                var c=channels[i];if(c==null)throw new ArgumentNullException("channels");
                Put(b,36+i*8,(uint)c.Probe|((uint)c.Filter<<8)|((uint)c.Channel<<16));
                Buffer.BlockCopy(BitConverter.GetBytes(c.Gain),0,b,40+i*8,4);if(c.Rectify)mask|=1U<<i;
            }
            Put(b,52,mask);Put(b,60,NpClosedLoopEvent.Crc(b,60));Parse(b);return b;
        }
        public static NpClosedLoopControl Parse(byte[] b)
        {
            if(b==null||b.Length!=64||System.Text.Encoding.ASCII.GetString(b,0,4)!="NPCC"||U(b,4)!=1||U(b,8)>2||U(b,12)==0||U(b,60)!=NpClosedLoopEvent.Crc(b,60))
                throw new InvalidDataException("Invalid closed-loop control version, identity or CRC.");
            uint count=U(b,32);var q=new NpClosedLoopControl {Operation=U(b,8),RequestId=U(b,12),ConfigCrc=U(b,60),Channels=new NpClosedLoopChannel[0]};
            if(q.Operation!=Configure||count==0){if(!Zero(b,16,44))throw new InvalidDataException("Unexpected closed-loop query/disable payload.");return q;}
            q.ApHz=U(b,20);q.TrainingStartMs=U(b,24);q.TrainingDurationMs=U(b,28);q.RectifyMask=U(b,52);
            if(count>2||q.TrainingDurationMs==0||U(b,16)!=0||U(b,56)!=0||
               !(q.ApHz==7500||q.ApHz==15000||q.ApHz==20000||q.ApHz==30000)||(q.RectifyMask&~((1U<<(int)count)-1))!=0)
                throw new InvalidDataException("Unsupported closed-loop request settings.");
            q.Channels=new NpClosedLoopChannel[count];
            for(int i=0;i<2;i++){
                uint selector=U(b,36+i*8);float gain=BitConverter.ToSingle(b,40+i*8);
                if(i>=count){if(selector!=0||U(b,40+i*8)!=0)throw new InvalidDataException("Unexpected inactive detector.");continue;}
                var c=new NpClosedLoopChannel {Probe=(byte)selector,Filter=(byte)(selector>>8),Channel=(ushort)(selector>>16),Gain=gain,Rectify=(q.RectifyMask&(1U<<i))!=0};
                if(c.Probe<1||c.Probe>3||c.Filter>9||c.Channel>=384||Single.IsNaN(gain)||Single.IsInfinity(gain)||gain<=0)
                    throw new InvalidDataException("Invalid closed-loop channel or threshold gain.");
                q.Channels[i]=c;
            }
            return q;
        }
        public sealed class Reply
        {
            public uint Operation,RequestId,Result,Flags,ConfigCrc,Session,ApHz,SourceEpoch,MaxLanes,ProbeMask,FilterMask,ChannelGroups,Dropped;
            public bool Supported {get{return Result==0&&(Flags&1)!=0;}}
        }
        public static Reply ParseReply(byte[] b,uint operation,uint id)
        {
            if(b==null||b.Length!=64||System.Text.Encoding.ASCII.GetString(b,0,4)!="NPCR"||U(b,4)!=1||U(b,8)!=operation||U(b,12)!=id||id==0||operation>2||U(b,60)!=NpClosedLoopEvent.Crc(b,60))
                throw new InvalidDataException("Closed-loop response identity/version/CRC mismatch.");
            var r=new Reply {Operation=operation,RequestId=id,Result=U(b,16),Flags=U(b,20),ConfigCrc=U(b,24),Session=U(b,28),ApHz=U(b,32),SourceEpoch=U(b,36),
                MaxLanes=U(b,40),ProbeMask=U(b,44),FilterMask=U(b,48),ChannelGroups=U(b,52),Dropped=U(b,56)};
            if(r.Result>6||(r.Flags&~31U)!=0||r.MaxLanes>2||(r.ProbeMask&~7U)!=0||(r.FilterMask&~1023U)!=0||(r.ChannelGroups&~511U)!=0||
               ((r.Flags&8)!=0&&r.SourceEpoch==0)||((r.Flags&2)!=0&&(r.Flags&1)==0)||((r.Flags&4)!=0&&(r.Flags&2)==0)||((r.Flags&16)!=0&&(r.Flags&2)==0))
                throw new InvalidDataException("Invalid closed-loop response fields.");return r;
        }
        // Configure is sent once. A lost receipt is an unknown outcome, never
        // permission to repeat a mutating command or prevent neural recording.
        public static async Task<Reply> ExchangeAsync(Func<byte[],CancellationToken,Task<byte[]>> exchange,byte[] request,CancellationToken cancel)
        {
            if(exchange==null)throw new ArgumentNullException("exchange");var q=Parse(request);cancel.ThrowIfCancellationRequested();
            byte[] response=await exchange((byte[])request.Clone(),cancel);return ParseReply(response,q.Operation,q.RequestId);
        }
    }
}
