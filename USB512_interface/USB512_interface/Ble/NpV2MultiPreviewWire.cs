using System;
using System.Collections.Generic;
using System.IO;

namespace CE32_console
{
    // Preview-only extension. Native recording, legacy preview and control
    // framing keep their existing contracts.
    internal static class NpV2MultiPreview
    {
        internal const uint Rate=500,Rows=40,Filter=1;
        internal const int BodyBytes=224;
        internal struct Selection
        {
            internal readonly uint Probe,Channel,Phase;
            internal Selection(uint probe,uint channel,uint phase){
                if(probe<1||probe>2||channel>=384||phase>=1600)throw new ArgumentOutOfRangeException();
                Probe=probe;Channel=channel;Phase=phase;
            }
            internal ushort Descriptor {get{return (ushort)(Channel|((Probe-1)<<9));}}
            internal ushort WordOffset {get{uint c=Channel%128;return (ushort)((1600-Phase+(c%32)/2*100+4+((c/32)*2+(c&1))*12)%1600);}}
        }
        internal sealed class Capabilities
        {
            internal uint Session,SourceHz,HistorySlots;
        }
        internal sealed class Frame
        {
            internal uint Sequence,SourceHz,Session,WireCrc;
            internal ulong FirstTick,Step;
            internal ushort[] WordOffsets;
            internal short[,] Samples;
        }
        internal sealed class ChecksumException:IOException
        {internal ChecksumException():base("Preview packet checksum failed; requesting its original data."){} }
        internal sealed class ContinuityException:IOException
        {
            internal readonly uint Status;
            static string Reason(uint status){switch(status){
                case 1:return "Preview settings were rejected.";
                case 2:return "Preview is waiting for recording.";
                case 3:return "Recording changed; re-enable preview.";
                case 4:return "Preview source data was interrupted.";
                case 5:return "Preview ended with the recording.";
                case 6:return "Preview paused while recording was busy.";
                case 7:return "Preview recovery buffer filled while recording had priority.";
                case 8:return "Preview signal exceeded its display range.";
                default:return "Preview data could not be retained.";
            }}
            internal ContinuityException(uint status):base(Reason(status)+" Recording is independent."){Status=status;}
        }
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        static void Put16(byte[] b,int at,ushort value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,2);}
        static bool Zero(byte[] b,int from,int to){for(int i=from;i<to;i++)if(b[i]!=0)return false;return true;}
        internal static bool SourceRate(uint hz){return hz==7500||hz==15000||hz==20000||hz==30000;}
        internal static ushort Delay(uint hz){if(hz==7500)return 208;if(hz==15000)return 418;if(hz==20000)return 538;if(hz==30000)return 838;throw new ArgumentOutOfRangeException("hz");}
        internal static bool Matches(byte[] b,uint id){return b!=null&&b.Length==BodyBytes&&U(b,0)==NpV2PreviewBurst.Magic&&U(b,4)==id;}
        internal static bool LegacyUnsupported(byte[] b,uint id){return Matches(b,id)&&((U(b,28)>>8)&255)==129;}
        static byte[] Request(uint kind,uint id){if(id==0)throw new ArgumentException("Preview id required");var b=new byte[64];Put(b,0,NpV2PreviewBurst.Magic);Put(b,4,1);Put(b,8,kind);Put(b,12,id);return b;}
        static byte[] Finish(byte[] b){Put(b,60,NpSdControl.Crc(b,60));return b;}
        internal static byte[] CapabilityRequest(uint id){return Finish(Request(8,id));}
        internal static byte[] Configure(uint id,uint session,Selection[] selection){
            if(session==0||selection==null||selection.Length<1||selection.Length>2)throw new ArgumentException("One or two preview channels required");
            for(int i=0;i<selection.Length;i++)new Selection(selection[i].Probe,selection[i].Channel,selection[i].Phase);
            if(selection.Length==2&&selection[0].Descriptor==selection[1].Descriptor)throw new ArgumentException("Select different preview channels");
            var b=Request(6,id);Put(b,16,Rate);Put(b,20,(uint)selection.Length);Put(b,24,session);
            for(int i=0;i<selection.Length;i++){Put16(b,32+i*2,selection[i].Descriptor);Put16(b,40+i*2,(ushort)selection[i].Phase);}return Finish(b);
        }
        internal static byte[] Feedback(uint id,uint session,uint acknowledged,uint missing,bool stop=false){
            if(session==0)throw new ArgumentException("Preview session required");var b=Request(7,id);Put(b,16,session);Put(b,20,acknowledged);Put(b,24,stop?0:missing);Put(b,28,stop?1U:0);return Finish(b);
        }
        internal static bool ValidRequest(byte[] b){
            if(b==null||b.Length!=64||U(b,0)!=NpV2PreviewBurst.Magic||U(b,4)!=1||U(b,12)==0||U(b,60)!=NpSdControl.Crc(b,60))return false;
            uint kind=U(b,8);
            if(kind==8)return Zero(b,16,60);
            if(kind==7)return U(b,16)!=0&&U(b,28)<=1&&((U(b,28)&1)==0||U(b,24)==0)&&Zero(b,32,60);
            if(kind!=6||U(b,16)!=Rate||U(b,20)<1||U(b,20)>2||U(b,24)==0||U(b,28)!=0||!Zero(b,36,40)||!Zero(b,44,60))return false;
            int n=(int)U(b,20);
            for(int i=0;i<2;i++){
                ushort d=BitConverter.ToUInt16(b,32+i*2),phase=BitConverter.ToUInt16(b,40+i*2);
                if(i>=n){if(d!=0||phase!=0)return false;}
                else if((d&~1023)!=0||(d&511)>=384||phase>=1600)return false;
            }
            return n!=2||BitConverter.ToUInt16(b,32)!=BitConverter.ToUInt16(b,34);
        }
        static void CheckCrc(byte[] b){if(U(b,220)!=NpSdControl.Crc(b,220))throw new ChecksumException();}
        internal static Capabilities ParseCapabilities(byte[] b,uint id){
            if(!Matches(b,id))return null;CheckCrc(b);
            if(U(b,28)!=(8U<<8)||U(b,24)!=0||U(b,32)!=1||U(b,36)!=2||U(b,40)!=Rate||U(b,44)!=Rows||
               U(b,48)<12||U(b,48)>64||U(b,52)!=7||!Zero(b,60,220)||!SourceRate(U(b,12))||U(b,8)==0)
                throw new InvalidDataException("Invalid multi-channel preview capability");
            if(U(b,56)!=0)throw new ContinuityException(U(b,56));
            return new Capabilities{Session=U(b,8),SourceHz=U(b,12),HistorySlots=U(b,48)};
        }
        internal static Frame ParseFrame(byte[] b,uint id,uint session,uint hz,Selection[] selection){
            if(!Matches(b,id))return null;CheckCrc(b);
            if(selection==null||selection.Length<1||selection.Length>2||U(b,8)!=session||U(b,12)!=hz||!SourceRate(hz)||
               U(b,28)!=((6U<<8)|((uint)selection.Length<<16)))throw new InvalidDataException("Preview session/rate/selection changed");
            if(U(b,56)!=0)throw new ContinuityException(U(b,56));
            if(U(b,24)!=((160U<<16)|Rows)||U(b,32)==0||U(b,36)!=Rate||!Zero(b,44,48)||BitConverter.ToUInt16(b,52)!=Filter||BitConverter.ToUInt16(b,54)!=Delay(hz))
                throw new InvalidDataException("Invalid filtered preview metadata");
            ulong first=U(b,16)|((ulong)U(b,20)<<32),step=(ulong)(hz/Rate)*1600;
            if(first>ulong.MaxValue-Rows*step-1599)throw new InvalidDataException("Preview timestamp overflow");
            var offsets=new ushort[selection.Length];
            for(int i=0;i<2;i++){
                ushort desc=BitConverter.ToUInt16(b,40+i*2),offset=BitConverter.ToUInt16(b,48+i*2);
                if(i>=selection.Length){if(desc!=0||offset!=0)throw new InvalidDataException("Nonzero inactive preview channel");}
                else{if(desc!=selection[i].Descriptor||offset!=selection[i].WordOffset)throw new InvalidDataException("Preview channel alignment changed");offsets[i]=offset;}
            }
            var values=new short[Rows,selection.Length];
            for(int row=0;row<Rows;row++)for(int ch=0;ch<2;ch++){
                short value=BitConverter.ToInt16(b,60+(row*2+ch)*2);
                if(ch<selection.Length)values[row,ch]=value;else if(value!=0)throw new InvalidDataException("Nonzero inactive preview samples");
            }
            return new Frame{Sequence=U(b,32),SourceHz=hz,Session=session,FirstTick=first,Step=step,WordOffsets=offsets,Samples=values,WireCrc=U(b,220)};
        }
    }

    // Publish only contiguous, checked originals. A bad/missing packet requests
    // replay; neither timestamps nor waveform values are interpolated.
    internal sealed class NpV2MultiPreviewReceiver:IDisposable
    {
        readonly object gate=new object();readonly uint id,session,hz;readonly int epoch;
        readonly NpV2MultiPreview.Selection[] selection;
        readonly SortedDictionary<uint,NpV2MultiPreview.Frame> waiting=new SortedDictionary<uint,NpV2MultiPreview.Frame>();
        readonly Queue<NpV2MultiPreview.Frame> ready=new Queue<NpV2MultiPreview.Frame>();
        uint next=1;ulong nextTick;bool haveTick,disposed,checksumLoss;Exception error;
        internal long Packets {get;private set;}internal long Samples {get;private set;}internal long ChecksumFailures {get;private set;}
        internal NpV2MultiPreviewReceiver(uint id,uint session,uint hz,NpV2MultiPreview.Selection[] selection,int epoch){
            if(id==0||session==0||!NpV2MultiPreview.SourceRate(hz)||selection==null||selection.Length<1||selection.Length>2)throw new ArgumentException();
            this.id=id;this.session=session;this.hz=hz;this.selection=(NpV2MultiPreview.Selection[])selection.Clone();this.epoch=epoch;
        }
        internal void Receive(int generation,byte[] body){lock(gate){
            if(disposed||generation!=epoch||!NpV2MultiPreview.Matches(body,id))return;
            try{
                var frame=NpV2MultiPreview.ParseFrame(body,id,session,hz,selection);int distance=unchecked((int)(frame.Sequence-next));
                if(distance<0)return;
                if(distance>=64)throw new IOException("Preview replay window exceeded; recording is independent.");
                NpV2MultiPreview.Frame prior;
                if(waiting.TryGetValue(frame.Sequence,out prior)){if(prior.WireCrc!=frame.WireCrc)throw new InvalidDataException("Preview replay changed its data");return;}
                waiting.Add(frame.Sequence,frame);
                while(waiting.TryGetValue(next,out frame)){
                    if(haveTick&&frame.FirstTick!=nextTick)throw new InvalidDataException("Preview source timestamps are discontinuous");
                    if(ready.Count==128)throw new IOException("Preview consumer cannot keep up; recording is independent.");
                    waiting.Remove(next);nextTick=frame.FirstTick+NpV2MultiPreview.Rows*frame.Step;haveTick=true;
                    ready.Enqueue(frame);Packets++;Samples+=NpV2MultiPreview.Rows*(uint)selection.Length;next++;
                    if(next==0)throw new IOException("Preview sequence exhausted; reopen preview.");
                    checksumLoss=false;
                }
            }catch(NpV2MultiPreview.ChecksumException){ChecksumFailures++;checksumLoss=true;}
            catch(Exception ex){error=ex;}
        }}
        internal List<NpV2MultiPreview.Frame> Drain(){lock(gate){if(error!=null)throw error;var result=new List<NpV2MultiPreview.Frame>(ready);ready.Clear();return result;}}
        internal uint Acknowledged {get{lock(gate)return next-1;}}
        internal uint Expected {get{lock(gate)return next;}}
        internal uint Missing {get{lock(gate)return waiting.Count!=0||checksumLoss?next:0;}}
        internal byte[] Feedback(bool stop=false,bool stalled=false){lock(gate){return NpV2MultiPreview.Feedback(id,session,next-1,stalled?next:(waiting.Count!=0||checksumLoss?next:0),stop);}}
        public void Dispose(){lock(gate){disposed=true;waiting.Clear();ready.Clear();}}
    }
}
