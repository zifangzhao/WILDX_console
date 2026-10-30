using System;
using System.IO;
using System.Text;

namespace WildNpClosedLoop
{
    public static class NpClosedLoopCarrier
    {
        public const uint V2Flag=0x400,V1Tag=0x434C0100;
        static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
        static bool Zero(byte[] b,int o,int n){for(int i=0;i<n;i++)if(b[o+i]!=0)return false;return true;}
        static void Bounds(byte[] b,int o,int n){if(b==null||o<0||n<0||o>b.Length-n)throw new InvalidDataException("Truncated closed-loop carrier.");}
        static byte[] Slice(byte[] b,int o,int n){Bounds(b,o,n);var r=new byte[n];Buffer.BlockCopy(b,o,r,0,n);return r;}
        static bool Magic(byte[] b,int o,string text){Bounds(b,o,text.Length);return Encoding.ASCII.GetString(b,o,text.Length)==text;}
        static void Put(byte[] b,int o,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,o,4);}
        static void Seal(byte[] b,string magic)
        {if(b==null||b.Length!=512||!Magic(b,0,magic)||U(b,508)!=NpClosedLoopEvent.Crc(b,508))throw new InvalidDataException("Invalid closed-loop SD metadata seal.");}
        static void Status(uint value){if((value&0xffffff00U)!=V1Tag||(value&0xc0U)!=0||(value&3)==3)throw new InvalidDataException("Invalid closed-loop carrier status.");}

        // Only the new explicitly advertised region is dynamic. Neural header,
        // payload, sequence, source-clock and existing CRC checks stay in their
        // existing verifier. An event CRC error is an auxiliary-log failure.
        public static bool IsV2DynamicByte(uint flags,int n){return (flags&V2Flag)!=0&&n>=184&&n<256;}
        public static void ValidateV2Structure(byte[] b,int o,uint flags)
        {
            Bounds(b,o,512);
            if((flags&V2Flag)==0||(flags&64)!=0||(flags&32)==0||!Zero(b,o+176,8)||
               ((flags&128)==0&&!Zero(b,o+256,140))||((flags&512)==0&&!Zero(b,o+396,108))||
               (!Zero(b,o+504,4)&&((flags&(4096U|128U|32U))!=(4096U|128U|32U)||(flags&(64U|2048U))!=0)))
                throw new InvalidDataException("Closed-loop metadata requires a copied V2 header with exact reserved-byte bounds.");
        }
        static NpClosedLoopEvent Event(byte[] b,int o,uint config,uint drops,uint session)
        {
            if(Zero(b,o,64))return null;
            var e=NpClosedLoopEvent.Parse(Slice(b,o,64));
            if(e.ConfigCrc!=config||e.Dropped>drops||(session!=0&&e.Session!=session))
                throw new InvalidDataException("Closed-loop event does not match its configuration/session/drops snapshot.");
            return e;
        }
        public static NpClosedLoopEvent ReadV2(byte[] b,int o,uint flags,uint session)
        {ValidateV2Structure(b,o,flags);return Event(b,o+184,U(b,o+252),U(b,o+248),session);}
        public static bool HasV1Descriptor(byte[] b,int o)
        {Bounds(b,o,512);return (U(b,o+460)&0xffffff00U)==V1Tag;}
        public static NpClosedLoopEvent ReadV1Descriptor(byte[] b,int o,uint session)
        {Bounds(b,o,512);Status(U(b,o+460));return Event(b,o+388,U(b,o+452),U(b,o+456),session);}
        public static NpClosedLoopControl ReadV1UtcConfiguration(byte[] b)
        {
            Bounds(b,0,512);if(!Magic(b,144,"NPCLCFG1"))return null;
            if(U(b,156)!=1||!Zero(b,160,348))throw new InvalidDataException("Unsupported V1 closed-loop clock/config extension.");
            var q=NpClosedLoopControl.Parse(Slice(b,80,64));
            if(q.Operation!=NpClosedLoopControl.Configure||q.Channels.Length==0||q.ConfigCrc!=U(b,152))
                throw new InvalidDataException("V1 closed-loop snapshot is not a confirmed enabled configuration.");return q;
        }
        public static NpClosedLoopControl ReadV1UsbConfiguration(byte[] b,int o,uint session,out NpClosedLoopEvent e)
        {
            Bounds(b,o,502);Status(U(b,o+436));if(!Zero(b,o+440,62))throw new InvalidDataException("Nonzero V1 carrier reserved bytes.");
            var q=NpClosedLoopControl.Parse(Slice(b,o+364,64));
            if(q.Operation!=NpClosedLoopControl.Configure||q.Channels.Length==0||q.ConfigCrc!=U(b,o+428))
                throw new InvalidDataException("Invalid V1 USB closed-loop configuration snapshot.");
            e=Event(b,o+300,q.ConfigCrc,U(b,o+432),session);return q;
        }
        public static bool HasV1Usb(byte[] b,int o,int count)
        {return b!=null&&o>=0&&count==506&&o<=b.Length-count&&(U(b,o+436)&0xffffff00U)==V1Tag;}
        public static uint ValidateV1UsbEnvelope(byte[] b,int o,int count)
        {
            Bounds(b,o,count);
            if(count!=506||!Magic(b,o,"NPSENS01")||!Magic(b,o+164,"NPUSB001")||U(b,o+172)!=1||
               U(b,o+502)!=NpClosedLoopEvent.Crc(Slice(b,o,502),502)||U(b,o+176)==0)
                throw new InvalidDataException("Invalid V1 event carrier envelope/session/CRC.");
            return U(b,o+176);
        }

        public sealed class V2Summary
        {
            public uint Session,ConfigCrc,Generation,Attempted,Published,Dropped,TailDropped,Rejected,LastSequence,Flags;
            public bool DeliveryUnknown{get{return (Flags&4)!=0;}}
        }
        public static V2Summary ReadV2Summary(byte[] b,int o,uint session,uint config)
        {
            Bounds(b,o,64);var p=Slice(b,o,64);
            if(!Magic(p,0,"NPCLEND1")||U(p,8)!=1||U(p,12)!=64||session==0||U(p,16)!=session||U(p,20)!=config||
               U(p,24)==0||(U(p,52)&1)==0||(U(p,52)&~7U)!=0||U(p,56)!=0||U(p,60)!=NpClosedLoopEvent.Crc(p,60)||
               U(p,40)>U(p,36)||U(p,44)>U(p,36)||U(p,48)>U(p,28)||
               (U(p,28)!=UInt32.MaxValue&&(ulong)U(p,32)+U(p,36)!=U(p,28)))
                throw new InvalidDataException("Invalid closed-loop final event summary.");
            return new V2Summary{Session=session,ConfigCrc=config,Generation=U(p,24),Attempted=U(p,28),Published=U(p,32),
                Dropped=U(p,36),TailDropped=U(p,40),Rejected=U(p,44),LastSequence=U(p,48),Flags=U(p,52)};
        }

        // Validate the new envelope first, then let the existing exact v11-v14
        // geometry validator inspect private copies. Never alter source sectors.
        public static V2Summary NormalizeV2Metadata(byte[] d,byte[] f,bool allowIncomplete,out byte[] baseD,out byte[] baseF)
        {
            Seal(d,"N2SDLOG1");Seal(f,"N2SDEND1");uint v=U(d,136),flags=U(d,96)&~128U;
            bool baseFlags=v==11?flags==15:v==12?flags==31:v==13?flags==47:v==14&&(flags==79||flags==95||flags==111);
            if(U(d,8)!=15||U(f,8)!=15||(U(d,96)&128)==0||!baseFlags||U(d,140)!=0x314d4c43||!Zero(d,144,364)||
               !Zero(f,140,4)||!Zero(f,208,300)||(v!=14&&!Zero(f,132,8)))
                throw new InvalidDataException("Unsupported closed-loop SD v15 envelope.");
            for(int n=8;n<100;n++)if(d[n]!=f[n])throw new InvalidDataException("Closed-loop SD descriptor/footer identity mismatch.");
            V2Summary summary=null;
            if(Zero(f,144,64)){
                if(!allowIncomplete||U(f,100)==3)throw new InvalidDataException("Closed-loop final summary is missing; final event losses are unknown.");
            }else summary=ReadV2Summary(f,144,U(d,12),U(d,132));
            baseD=(byte[])d.Clone();baseF=(byte[])f.Clone();
            foreach(var p in new[]{baseD,baseF}){Put(p,8,v);Put(p,96,flags);}
            Array.Clear(baseD,132,12);Array.Clear(baseF,144,64);
            Put(baseD,508,NpClosedLoopEvent.Crc(baseD,508));Put(baseF,508,NpClosedLoopEvent.Crc(baseF,508));
            return summary;
        }

        public static NpClosedLoopControl ReadV2Configuration(byte[] b,byte[] descriptor)
        {
            Bounds(b,0,2048);Bounds(descriptor,0,512);const int o=1888;
            if((U(descriptor,8)!=15&&U(descriptor,8)!=16)||(U(descriptor,96)&128)==0){
                if(!Zero(b,o,160))throw new InvalidDataException("Unadvertised closed-loop configuration extension.");return null;
            }
            var p=Slice(b,o,160);var q=NpClosedLoopControl.Parse(Slice(p,32,64));uint hz=U(p,24),flags=U(p,120);
            if(!Magic(p,0,"NPCLCFG1")||U(p,8)!=1||U(p,12)!=160||U(p,156)!=NpClosedLoopEvent.Crc(p,156)||
               U(p,16)!=q.ConfigCrc||q.ConfigCrc!=U(descriptor,132)||q.Operation!=1||q.Channels.Length==0||
               U(p,20)==0||U(p,20)!=U(descriptor,12)||hz!=q.ApHz||hz!=U(descriptor,16)||U(p,28)!=hz*1600U||
               U(p,96)!=1250||U(p,104)!=(22U*(hz/2500U)-2U)*1600U||
               ((flags&4)!=0?U(p,108)>=1600:U(p,108)!=0)||
               U(p,116)!=0x203||(flags&~15U)!=0||(flags&1)==0||((flags&2)!=0?U(p,100)==0:U(p,100)!=0)||
               U(p,124)!=1||U(p,128)!=(hz/1250U)*1600U||U(p,132)!=7||U(descriptor,24)!=7||!Zero(p,136,20)||
               (q.Channels.Length==1?(U(p,112)!=0||(flags&8)!=0):((flags&8)!=0?U(p,112)>=1600:U(p,112)!=0)))
                throw new InvalidDataException("Invalid closed-loop SPI configuration/timing provenance.");
            foreach(var channel in q.Channels)if(channel.Probe!=3||channel.Channel<256)
                throw new InvalidDataException("Unsupported closed-loop SPI tap; confirmed P3 lane 2 is required.");
            return q;
        }
    }
}
