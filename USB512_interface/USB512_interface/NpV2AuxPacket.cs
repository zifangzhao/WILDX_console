using System;
using System.IO;
using System.Text;

namespace USB512_interface {
    // One immutable completion-owned packet. Neither the caller's USB buffer
    // nor arrays returned to a consumer can mutate a later consumer's view.
    internal sealed class NpV2AuxPacket {
        readonly byte[] header,payload;
        internal readonly uint Type,Sequence,PublishedMs,PayloadBytes,Offset,Total,Rate,Flags,Error,Format;
        internal readonly ulong Source;
        internal NpV2AuxPacket(byte[] h,byte[] p){
            header=(byte[])h.Clone();payload=p;
            Type=U(h,12);Sequence=U(h,16);PublishedMs=U(h,20);Source=Q(h,24);
            PayloadBytes=U(h,32);Offset=U(h,36);Total=U(h,40);Rate=U(h,44);
            Flags=U(h,48);Error=U(h,52);Format=U(h,56);
        }
        internal byte[] Header {get{return (byte[])header.Clone();}}
        internal byte[] Payload {get{return (byte[])payload.Clone();}}
        internal static uint U(byte[] b,int i){return (uint)b[i]|(uint)b[i+1]<<8|(uint)b[i+2]<<16|(uint)b[i+3]<<24;}
        internal static ulong Q(byte[] b,int i){return U(b,i)|(ulong)U(b,i+4)<<32;}
        internal static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("V2 AUX: "+why);}
        internal static int CheckHeader(byte[] h,bool gridSelected){
            Need(Encoding.ASCII.GetString(h,0,8)=="NPAVUSB1"&&U(h,8)==1&&U(h,60)==0,"header magic/version/reserved");
            uint type=U(h,12),n=U(h,32),offset=U(h,36),total=U(h,40),flags=U(h,48),format=U(h,56);
            Need(n<=32768&&offset<=total&&n<=total-offset,"bounded payload/fragment extent");
            Need(type>=1&&type<=7&&format==(type==4?0:type),"record type/format");
            if(type==1)Need(n>0&&total==160000&&U(h,44)>0&&(flags&~3U)==0&&
                ((flags&1)!=0)==(offset==0)&&((flags&2)!=0)==(offset+n==total),"camera fragment");
            else if(type==2)Need(n>0&&n<=4096&&(n&1)==0&&(Q(h,24)&1)==0&&offset==0&&total==n&&flags==3&&U(h,44)>0,"ADC fragment");
            else if(type==3)Need(n==140&&offset==0&&total==n&&flags==3&&U(h,44)==20,"legacy IMU extent/rate");
            else if(type==4)Need(n==0&&offset==0&&total==0&&flags==3&&Q(h,24)==0&&U(h,44)==0,"legacy terminal");
            else {
                Need(gridSelected,"grid record without explicit selection");
                Need(U(h,20)==0&&offset==0&&total==n&&flags==3,"grid envelope");
                Need(type==5?n>=48&&n<=480&&U(h,44)==1250:type==6?n==84&&U(h,44)==0:n==80&&U(h,44)==0,"grid extent/rate");
            }
            return (int)n;
        }
        internal void CheckBody(){
            if(Type==5)Need(NpImuGridWire.Parse(payload).FirstSlot==Source,"grid first ordinal mismatch");
            else if(Type==6)Need(NpImuGridPhase.Parse(payload).FirstAcceptedSlot==Source,"phase first ordinal mismatch");
            else if(Type==7)Need(NpV2AuxTerminal.Parse(payload).FinalExclusive==Source,"terminal final ordinal mismatch");
            // Existing sensor bodies are optional metadata: framing does not
            // turn their own version/CRC errors into a neural stream error.
        }
    }

    // Pure ordered byte framer, not a USB reader or clock/session proof. One
    // owner feeds it and isolates errors from the independent EP81 recorder.
    internal sealed class NpV2AuxParser {
        readonly bool gridSelected;readonly byte[] header=new byte[64];
        byte[] payload;int headerUsed,payloadUsed;bool failed,feeding,finished;
        internal NpV2AuxParser(bool gridSelected=false){this.gridSelected=gridSelected;}
        internal void Feed(byte[] input,int offset,int count,Action<NpV2AuxPacket> consume){
            if(input==null||offset<0||count<0||offset>input.Length-count)throw new ArgumentOutOfRangeException("input");
            if(consume==null)throw new ArgumentNullException("consume");
            if(failed||finished||feeding)throw new InvalidOperationException("AUX parser is failed, finished or already feeding.");
            feeding=true;bool success=false;
            try {
                while(count!=0){
                    if(headerUsed!=64){int n=Math.Min(64-headerUsed,count);Buffer.BlockCopy(input,offset,header,headerUsed,n);offset+=n;count-=n;headerUsed+=n;if(headerUsed!=64)continue;payload=new byte[NpV2AuxPacket.CheckHeader(header,gridSelected)];}
                    int copy=Math.Min(payload.Length-payloadUsed,count);Buffer.BlockCopy(input,offset,payload,payloadUsed,copy);offset+=copy;count-=copy;payloadUsed+=copy;
                    if(payloadUsed==payload.Length){var packet=new NpV2AuxPacket(header,payload);headerUsed=payloadUsed=0;payload=null;consume(packet);}
                }
                success=true;
            } finally {feeding=false;if(!success)failed=true;}
        }
        internal void Complete(){
            if(failed||finished||feeding)throw new InvalidOperationException("AUX parser is failed, finished or already feeding.");
            finished=true;if(headerUsed!=0||payload!=null){failed=true;throw new InvalidDataException("V2 AUX: truncated header/payload; tail unavailable.");}
        }
    }

    // USB design-target terminal; no producer parity or runtime selection is
    // implied by this pure reader. Original bytes remain in the packet.
    internal sealed class NpV2AuxTerminal {
        readonly byte[] uid;
        internal readonly uint Session,Generation,SourceEpoch,Domain,QueueDropped,SchedulerMissed,Flags;
        internal readonly ulong Token,FinalExclusive;
        internal bool FinalKnown {get{return (Flags&1)!=0;}}
        internal bool CountersAreLowerBounds {get{return (Flags&2)!=0;}}
        internal bool GridError {get{return (Flags&4)!=0;}}
        NpV2AuxTerminal(byte[] b){uid=new byte[12];Buffer.BlockCopy(b,16,uid,0,12);Session=U(b,28);Generation=U(b,32);SourceEpoch=U(b,36);Domain=U(b,40);Token=Q(b,44);FinalExclusive=Q(b,52);QueueDropped=U(b,60);SchedulerMissed=U(b,64);Flags=U(b,68);}
        static uint U(byte[] b,int i){return NpV2AuxPacket.U(b,i);}static ulong Q(byte[] b,int i){return NpV2AuxPacket.Q(b,i);}
        static void Need(bool ok,string why){NpV2AuxPacket.Need(ok,"grid terminal "+why);}
        internal static uint Crc(byte[] b){uint c=UInt32.MaxValue;for(int i=0;i<76;i++){c^=b[i];for(int k=0;k<8;k++)c=(c>>1)^(0xedb88320U&unchecked(0U-(c&1)));}return ~c;}
        internal static NpV2AuxTerminal Parse(byte[] b){
            Need(b!=null&&b.Length==80,"extent");Need(Encoding.ASCII.GetString(b,0,8)=="NPGEND01"&&U(b,8)==1&&U(b,12)==80&&U(b,72)==0&&U(b,76)==Crc(b),"magic/version/size/reserved/CRC");
            var t=new NpV2AuxTerminal(b);int any=0,notFf=0;foreach(byte n in t.uid){any|=n;notFf|=n^255;}
            Need(any!=0&&notFf!=0&&t.Session!=0&&t.Generation!=0&&t.SourceEpoch!=0&&t.Domain!=0&&t.Token!=0&&(t.Flags&~7U)==0,"identity/flags");
            Need(t.FinalKnown?t.FinalExclusive!=UInt64.MaxValue:t.FinalExclusive==UInt64.MaxValue,"unknown final sentinel");
            Need(t.CountersAreLowerBounds==(t.QueueDropped==UInt32.MaxValue||t.SchedulerMissed==UInt32.MaxValue),"counter saturation flag");return t;
        }
        internal static NpV2AuxTerminal Read(byte[] b,NpImuGridPhaseBinding expected){
            var t=Parse(b);Need(expected!=null&&expected.Uid!=null&&expected.Uid.Length==12&&t.Session==expected.Session&&t.Generation==expected.CaptureGeneration&&t.SourceEpoch==expected.SourceEpoch&&t.Domain==expected.CounterDomain&&t.Token==expected.Token,"independent context");
            for(int i=0;i<12;i++)Need(t.uid[i]==expected.Uid[i],"independent UID");return t;
        }
    }
}
