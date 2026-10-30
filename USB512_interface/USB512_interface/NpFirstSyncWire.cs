using System;
using System.IO;
using System.Text;

namespace USB512_interface {
    internal sealed class NpFirstSyncProbe {
        internal ulong FirstTick;
        internal uint Counter,Flags;
        internal ushort Phase,Period;
    }
    internal sealed class NpFirstSyncAux {
        internal byte Id,Flags,CounterBits,StampKind;
        internal uint ReleaseMs,ReleaseCycles,FirstMs,FirstCycles,RateNumerator,RateDenominator,OffsetDenominator;
        internal ulong Counter;
        internal long OffsetNumerator;
        internal bool SampleLocked {get{return (Flags&16)!=0;}}
    }
    internal sealed class NpFirstSyncReceipt {
        internal uint Flags,Family,Session,Generation,SourceEpoch,Domain,SourceHz,ApHz,CounterHz,ProfileCrc;
        internal byte Mask,Reference,AuxMask,Policy;
        internal ulong ReferenceTick;
        internal uint DiscoveryMs,DiscoveryCycles;
        internal NpFirstSyncProbe[] Probes;
        internal NpFirstSyncAux[] Aux;
        internal byte[] Raw;
    }
    internal sealed class NpFirstSyncBinding {
        internal uint Family,Session,Generation,SourceEpoch,Domain,CounterHz,ProfileCrc,Mask,ApHz;
    }
    // Optional producer evidence. This parser cannot establish framing or a
    // physical sample lock from UTC fitting, USB delivery or reset/preparation.
    internal static class NpFirstSyncWire {
        internal const int Bytes=328,SdOffset=160;
        internal const uint Final=1,AllPorts=2,Discovery=4,Profile=8,AuxGated=16,SourceClock=32;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ushort H(byte[] b,int at){return BitConverter.ToUInt16(b,at);}
        static ulong L(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static bool Zero(byte[] b,int at,int count){for(int i=at;i<at+count;i++)if(b[i]!=0)return false;return true;}
        static void Require(bool ok,string error){if(!ok)throw new InvalidDataException("NP first-sync: "+error);}
        static ulong Gcd(ulong a,ulong b){while(b!=0){ulong t=a%b;a=b;b=t;}return a;}
        static bool Rate(uint hz){return hz==7500 || hz==15000 || hz==20000 || hz==30000;}
        internal static uint Crc(byte[] b,int count){
            uint c=UInt32.MaxValue;for(int i=0;i<count;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((c&1)!=0?0xedb88320U:0U);}return ~c;
        }
        internal static bool GridStep(uint sourceHz,uint rateNumerator,uint rateDenominator,out ulong numerator,out uint denominator){
            numerator=0;denominator=0;if(sourceHz==0 || rateNumerator==0 || rateDenominator==0)return false;
            ulong n=(ulong)sourceHz*rateDenominator,g=Gcd(n,rateNumerator);numerator=n/g;denominator=(uint)(rateNumerator/g);return true;
        }
        internal static NpFirstSyncReceipt Parse(byte[] b){
            Require(b!=null && b.Length==Bytes,"size");
            Require(Encoding.ASCII.GetString(b,0,8)=="NPSTART1" && H(b,8)==1 && H(b,10)==Bytes && U(b,324)==Crc(b,324),"tag/version/length/CRC");
            uint flags=U(b,12),family=(flags>>8)&255,hz=U(b,36),mask=b[48];int reference=1;
            Require((flags&~0xff3fU)==0 && (family==1 || family==2) && Rate(hz) && U(b,32)==hz*(family==1?780U:1600U),"family/source profile");
            Require(U(b,16)!=0 && U(b,20)!=0 && U(b,24)!=0 && mask!=0 && (mask&~7U)==0 && (b[50]&~15)==0 && b[51]==(b[50]!=0?1:0),"session/generations/port policy");
            Require((U(b,28)==0)==(U(b,40)==0) && U(b,40)<=1000000000 && ((flags&Profile)!=0 || U(b,44)==UInt32.MaxValue),"counter domain/profile proof");
            while((mask&(1U<<(reference-1)))==0)reference++;
            Require(b[49]==reference && Zero(b,60,4),"deterministic reference/reserved");
            var probes=new NpFirstSyncProbe[3];bool all=true;
            for(int p=0;p<3;p++){
                int at=64+p*20;uint pf=U(b,at+16),required=family==1?31U:27U;ushort period=(ushort)(family==1?780:1600);
                var probe=new NpFirstSyncProbe{FirstTick=L(b,at),Counter=U(b,at+8),Phase=H(b,at+12),Period=H(b,at+14),Flags=pf};probes[p]=probe;
                if((mask&(1U<<p))==0){Require(Zero(b,at,20),"unselected probe record");continue;}
                Require(probe.Period==period && (pf==1 || pf==required),"probe proof flags/period");
                if(pf==1){
                    Require(probe.FirstTick==UInt64.MaxValue && probe.Counter==UInt32.MaxValue && probe.Phase==UInt16.MaxValue,"unknown probe must use sentinels");all=false;
                }else{
                    Require((flags&Final)!=0 && probe.FirstTick!=UInt64.MaxValue && probe.Phase<period &&
                        (probe.FirstTick%period+probe.Phase)%period==0 &&
                        (family==1?probe.Counter<=0xfffff:probe.Counter==UInt32.MaxValue),"final-generation native position/counter/phase");
                }
            }
            Require(((flags&AllPorts)!=0)==all && L(b,52)==probes[reference-1].FirstTick,"reference/all-port proof");
            if((flags&Discovery)!=0)Require(all && U(b,28)!=0,"discovery domain/proof");
            else Require(U(b,124)==UInt32.MaxValue && U(b,128)==UInt32.MaxValue,"unknown discovery must use sentinels");
            var aux=new NpFirstSyncAux[4];bool gated=true;
            for(int a=0;a<4;a++){
                int at=132+a*48;byte af=b[at+1];uint rn=U(b,at+28),rd=U(b,at+32),od=U(b,at+44);ulong on=L(b,at+36);
                var stream=new NpFirstSyncAux{Id=b[at],Flags=af,CounterBits=b[at+2],StampKind=b[at+3],ReleaseMs=U(b,at+4),ReleaseCycles=U(b,at+8),
                    FirstMs=U(b,at+12),FirstCycles=U(b,at+16),Counter=L(b,at+20),RateNumerator=rn,RateDenominator=rd,
                    OffsetNumerator=unchecked((long)on),OffsetDenominator=od};aux[a]=stream;
                if((b[50]&(1<<a))==0){Require(Zero(b,at,48),"disabled auxiliary record");continue;}
                Require(stream.Id==a+1 && (af&1)!=0 && (af&~127)==0 && (rn==0)==(rd==0) && (rn==0 || Gcd(rn,rd)==1),"auxiliary ID/flags/rate");
                if((af&2)!=0)Require(all && U(b,28)!=0,"release domain/proof");
                else Require(stream.ReleaseMs==UInt32.MaxValue && stream.ReleaseCycles==UInt32.MaxValue,"unknown release must use sentinels");
                if((af&4)!=0)Require(all && stream.CounterBits>=1 && stream.CounterBits<=64 &&
                    (stream.CounterBits==64 || stream.Counter<(1UL<<stream.CounterBits)),"native auxiliary counter");
                else Require(stream.CounterBits==0 && stream.Counter==UInt64.MaxValue,"unknown auxiliary counter");
                if((af&64)!=0)Require((af&4)!=0 && U(b,28)!=0 && (stream.StampKind==1 || stream.StampKind==2),"first-source point kind/domain");
                else Require(stream.StampKind==0 && stream.FirstMs==UInt32.MaxValue && stream.FirstCycles==UInt32.MaxValue,"unknown first-source point");
                if((af&8)!=0)Require((af&4)!=0 && od!=0 && on<=Int64.MaxValue && Gcd(on,od)==1,"NP-relative offset must be nonnegative and normalized");
                else Require(on==0x8000000000000000UL && od==0,"unknown offset must use sentinel/zero denominator");
                Require((af&32)==0 || (af&8)!=0,"pre-origin exclusion requires a source offset");
                if((af&32)==0)gated=false;
                Require((af&16)==0 || ((af&(4|8|32))==(4|8|32) && rn!=0 && on==0 && od==1 &&
                    ((af&64)==0 || stream.StampKind!=2)),"sample-lock claim lacks zero-phase source evidence");
            }
            Require(((flags&AuxGated)!=0)==(b[50]!=0 && gated),"auxiliary exclusion summary");
            return new NpFirstSyncReceipt{Raw=(byte[])b.Clone(),Flags=flags,Family=family,Session=U(b,16),Generation=U(b,20),SourceEpoch=U(b,24),Domain=U(b,28),
                SourceHz=U(b,32),ApHz=hz,CounterHz=U(b,40),ProfileCrc=U(b,44),Mask=b[48],Reference=b[49],AuxMask=b[50],Policy=b[51],ReferenceTick=L(b,52),
                DiscoveryMs=U(b,124),DiscoveryCycles=U(b,128),Probes=probes,Aux=aux};
        }
        // Does not replace validation of the older UTC2 clock or CL area80..159.
        internal static NpFirstSyncReceipt ParseSdExtension(byte[] sector,int family,uint session,uint apHz){
            Require(sector!=null && sector.Length==512 && (family==1 || family==2),"SD sector size/family");
            Require(Encoding.ASCII.GetString(sector,0,8)==(family==1?"N1SDUTC2":"N2SDUTC2") && U(sector,8)==2 && U(sector,12)==session &&
                U(sector,72)==apHz && U(sector,508)==Crc(sector,508),"SD outer identity/seal");
            if(Zero(sector,SdOffset,508-SdOffset))return null;
            Require(Zero(sector,SdOffset+Bytes,508-SdOffset-Bytes),"SD extension tail reserved");
            var raw=new byte[Bytes];Buffer.BlockCopy(sector,SdOffset,raw,0,Bytes);var r=Parse(raw);
            Require(r.Family==family && r.Session==session && r.ApHz==apHz,"SD receipt identity/profile");return r;
        }
        // Called once on a finalized receipt; capture owners cache permission and
        // invalidate it on rearm, source loss or any expected-binding change.
        internal static bool GateMatch(NpFirstSyncReceipt r,NpFirstSyncBinding e){
            return r!=null && e!=null && (r.Flags&(Final|AllPorts))==(Final|AllPorts) &&
                r.Family==e.Family && r.Session==e.Session && r.Generation==e.Generation && r.SourceEpoch==e.SourceEpoch &&
                r.Domain==e.Domain && r.CounterHz==e.CounterHz && r.ProfileCrc==e.ProfileCrc && r.Mask==e.Mask && r.ApHz==e.ApHz;
        }
        // Cached producer root only, not a PC clock exchange. Domain comes
        // from the strictly validated NPSTART receipt; NPAR has no domain field.
        internal static NpFirstSyncReceipt ValidateSourceAnchor(byte[] anchor,byte[] rawReceipt){
            var r=Parse(rawReceipt);
            Require(anchor!=null&&anchor.Length==64 && (r.Flags&(Final|AllPorts|SourceClock))==(Final|AllPorts|SourceClock) &&
                r.Domain!=0&&r.CounterHz>=1000000&&r.CounterHz<=1000000000,"qualified source anchor receipt");
            Require(U(anchor,0)==0x5241504e&&U(anchor,4)==1&&U(anchor,8)!=0&&U(anchor,12)==((r.Family<<8)|31U)&&
                U(anchor,16)==r.Generation&&U(anchor,36)==r.CounterHz&&U(anchor,40)==r.SourceHz&&U(anchor,52)==r.ApHz&&
                U(anchor,56)==1000&&U(anchor,60)==Crc(anchor,60),"source anchor identity/profile/CRC");
            Require(unchecked(U(anchor,24)-U(anchor,20))<=1&&unchecked(U(anchor,32)-U(anchor,28))<=r.CounterHz/10000,
                "first-source anchor bracket width");
            Require(r.ReferenceTick!=UInt64.MaxValue&&L(anchor,44)<=UInt64.MaxValue-r.ReferenceTick,"source anchor origin/reference overflow");
            return r;
        }
    }
}
