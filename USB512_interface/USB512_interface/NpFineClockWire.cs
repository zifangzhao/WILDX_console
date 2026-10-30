using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace USB512_interface {
    internal sealed class NpFineClockReply {
        internal ushort Version;
        internal uint RequestedHoldUs;
        internal uint Flags,Domain,Hz,Generation,SourceEpoch,RxMs,RxCounter,TxMs,TxCounter,UartBaud;
        internal bool Qualified {get{return (Flags&1)!=0;}}
    }
    internal sealed class NpFineSdCompanion {
        internal NpFirstSyncSdBound Bound;
        internal byte[] Anchor;
        internal uint Features {get;set;}
        internal bool FineAux {get{return Features==NpFineClockWire.SdCompanionFineAux || Features==NpFineClockWire.SdCompanionFineCounter64;}}
        internal bool FineCounter64 {get{return Features==NpFineClockWire.SdCompanionFineCounter64;}}
    }
    // Serialization cannot establish actual counter continuity, UART latency
    // or precision. Those flags must come from the owning producer's evidence.
    internal static class NpFineClockWire {
        internal const uint RequestMagic=0x4654504e,ReplyMagic=0x4652504e;
        internal const uint ClockQualified=1,CaptureBound=2,UartBaudBound=4;
        internal const uint MaximumDeferredHoldUs=4000;
        // Per-record format declaration only, including when no auxiliary
        // sample arrives. It does not attest release, sample lock or phase.
        internal const uint SdCompanionLegacy=1,SdCompanionFineAux=9,SdCompanionFineCounter64=25;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ulong L(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static ushort H(byte[] b,int at){return BitConverter.ToUInt16(b,at);}
        static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
        static bool Zero(byte[] b,int at,int bytes){for(int i=at;i<at+bytes;i++)if(b[i]!=0)return false;return true;}
        static void Need(bool ok,string reason){if(!ok)throw new InvalidDataException("NP fine clock: "+reason);}
        static uint[] Uid(string uid){
            Need(uid!=null&&uid.Length==24,"exact UID");var words=new uint[3];
            for(int i=0;i<3;i++)Need(UInt32.TryParse(uid.Substring(i*8,8),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out words[i]),"UID");
            Need((words[0]|words[1]|words[2])!=0&&(words[0]&words[1]&words[2])!=UInt32.MaxValue,"UID");return words;
        }
        static string ReadUid(byte[] b,int at){return U(b,at).ToString("X8")+U(b,at+4).ToString("X8")+U(b,at+8).ToString("X8");}
        static void PutUid(byte[] b,int at,string uid){var words=Uid(uid);for(int i=0;i<3;i++)Put(b,at+4*i,words[i]);}
        static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
        internal static void ValidateRequest(byte[] b,string uid){
            Uid(uid);Need(b!=null&&b.Length==64,"request size");
            ushort version=H(b,4);
            Need(U(b,0)==RequestMagic&&(version==1||version==2)&&H(b,6)==0&&U(b,8)!=0&&L(b,24)!=0&&
                String.Equals(ReadUid(b,12),uid,StringComparison.OrdinalIgnoreCase)&&
                (version==1?Zero(b,32,28):U(b,32)<=MaximumDeferredHoldUs&&Zero(b,36,24))&&
                U(b,60)==NpFirstSyncWire.Crc(b,60),"request shape/ID/UID/nonce/hold/CRC");
        }
        internal static byte[] Request(string uid,uint id,ulong nonce){
            var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,1);Put(b,8,id);PutUid(b,12,uid);Put64(b,24,nonce);Seal(b);ValidateRequest(b,uid);return b;
        }
        // An explicit opt-in, not a timing guarantee. Only measured Rx/Tx
        // counters may constrain the clock fit; the requested hold never does.
        internal static byte[] RequestDeferred(string uid,uint id,ulong nonce,uint minimumHoldUs){
            var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,2);Put(b,8,id);PutUid(b,12,uid);Put64(b,24,nonce);
            Put(b,32,minimumHoldUs);Seal(b);ValidateRequest(b,uid);return b;
        }
        internal static bool IsRequest(byte[] b){
            if(b==null||b.Length!=64||U(b,0)!=RequestMagic)return false;
            try{ValidateRequest(b,ReadUid(b,12));return true;}catch(InvalidDataException){return false;}
        }
        internal static NpFineClockReply ParseReply(byte[] b,byte[] request,int family){
            Need(request!=null&&request.Length==64,"request context");ValidateRequest(request,ReadUid(request,12));
            Need(b!=null&&b.Length==80&&(family==1||family==2),"reply size/family");
            ushort version=H(request,4);uint hold=version==2?U(request,32):0;
            Need(U(b,0)==ReplyMagic&&H(b,4)==version&&(version==1?Zero(b,68,8):U(b,68)==hold&&Zero(b,72,4))&&
                U(b,76)==NpFirstSyncWire.Crc(b,76),"reply version/hold/reserved/CRC");
            for(int i=8;i<32;i++)Need(b[i]==request[i],"reply ID/UID/nonce echo");
            uint flags=H(b,6);Need((flags&~0x307U)==0&&(flags>>8)==family,"reply flags/family");
            var r=new NpFineClockReply {Version=version,RequestedHoldUs=hold,Flags=flags&7,Domain=U(b,32),Hz=U(b,36),Generation=U(b,40),SourceEpoch=U(b,44),
                RxMs=U(b,48),RxCounter=U(b,52),TxMs=U(b,56),TxCounter=U(b,60),UartBaud=U(b,64)};
            if(!r.Qualified){Need(r.Flags==0&&Zero(b,32,36),"unqualified reply must carry no clock/capture/UART evidence");return r;}
            Need(r.Domain!=0&&r.Hz>=1000000&&r.Hz<=1000000000,"clock domain/frequency");
            Need((r.Flags&CaptureBound)!=0?r.Generation!=0&&r.SourceEpoch!=0:r.Generation==0&&r.SourceEpoch==0,"capture context");
            Need((r.Flags&UartBaudBound)!=0?r.UartBaud>=1200&&r.UartBaud<=12000000:r.UartBaud==0,"UART bound");
            uint ms=unchecked(r.TxMs-r.RxMs),cycles=unchecked(r.TxCounter-r.RxCounter);
            Need(ms<=1000&&cycles<=r.Hz,"clock span/wrap");
            ulong coarse=(ulong)ms*r.Hz,fine=(ulong)cycles*1000;
            Need((coarse>fine?coarse-fine:fine-coarse)<=(ulong)r.Hz*2,"coarse/fine clock disagreement");return r;
        }
        internal static byte[] Reply(byte[] request,int family,NpFineClockReply r){
            Need(request!=null&&request.Length==64&&r!=null&&(r.Flags&~7U)==0&&(family==1||family==2),"reply inputs");
            ValidateRequest(request,ReadUid(request,12));ushort version=H(request,4);
            var b=new byte[80];Put(b,0,ReplyMagic);Put(b,4,(uint)version|((r.Flags|((uint)family<<8))<<16));Buffer.BlockCopy(request,8,b,8,24);
            uint[] words={r.Domain,r.Hz,r.Generation,r.SourceEpoch,r.RxMs,r.RxCounter,r.TxMs,r.TxCounter,r.UartBaud};
            for(int i=0;i<words.Length;i++)Put(b,32+i*4,words[i]);if(version==2)Put(b,68,U(request,32));
            Seal(b);ParseReply(b,request,family);return b;
        }
        static void Anchor(byte[] a,NpFirstSyncSdBound bound,int family){
            Need(a!=null&&a.Length==64,"NPAR size");var r=bound.Receipt;
            Need((r.Flags&(NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts|NpFirstSyncWire.SourceClock))==
                (NpFirstSyncWire.Final|NpFirstSyncWire.AllPorts|NpFirstSyncWire.SourceClock)&&r.Domain!=0&&r.CounterHz>=1000000&&r.CounterHz<=1000000000,"qualified NP source clock");
            Need(U(a,0)==0x5241504e&&U(a,4)==1&&U(a,12)==(((uint)family<<8)|31U)&&U(a,16)==r.Generation&&
                U(a,36)==r.CounterHz&&U(a,40)==r.SourceHz&&U(a,52)==r.ApHz&&U(a,56)==1000&&U(a,60)==NpFirstSyncWire.Crc(a,60),"NPAR shape/context/CRC");
            Need(unchecked(U(a,24)-U(a,20))<=1&&unchecked(U(a,32)-U(a,28))<=r.CounterHz/10000,"first-source bracket width");
            Need(r.ReferenceTick!=UInt64.MaxValue&&L(a,44)<=UInt64.MaxValue-r.ReferenceTick,"NPAR origin/reference overflow");
        }
        internal static NpFineSdCompanion ReadCompanion(byte[] b,byte[] clock,int family,uint session,uint apHz,string expectedUid=null,ulong expectedToken=0){
            Need(b!=null&&b.Length==512,"companion size");if(Zero(b,0,512))return null;
            var bound=NpFirstSyncSd.ReadSector(clock,family,session,apHz,expectedUid,expectedToken);Need(bound!=null,"bound SD clock receipt required");var r=bound.Receipt;
            uint features=U(b,68);
            Need(Encoding.ASCII.GetString(b,0,8)=="NPFCLK1\0"&&U(b,8)==1&&U(b,12)==family&&ReadUid(b,16)==bound.Uid&&
                U(b,28)==session&&U(b,32)==r.Generation&&U(b,36)==r.SourceEpoch&&U(b,40)==r.ProfileCrc&&U(b,44)==r.Mask&&
                U(b,48)==apHz&&U(b,52)==r.SourceHz&&L(b,56)==bound.Token&&U(b,64)==U(r.Raw,324)&&(features==SdCompanionLegacy||features==SdCompanionFineAux||features==SdCompanionFineCounter64)&&
                Zero(b,136,372)&&U(b,508)==NpFirstSyncWire.Crc(b,508),"companion identity/binding/reserved/CRC");
            var anchor=new byte[64];Buffer.BlockCopy(b,72,anchor,0,64);Anchor(anchor,bound,family);return new NpFineSdCompanion {Bound=bound,Anchor=anchor,Features=features};
        }
        internal static byte[] Companion(byte[] clock,byte[] anchor,int family,uint session,uint apHz,string expectedUid=null,ulong expectedToken=0){
            return CompanionWithFeatures(clock,anchor,family,session,apHz,expectedUid,expectedToken,SdCompanionLegacy);
        }
        // Producers may use this only for an accepted per-record NPSD v2
        // recordFeatures9 association; no persistent next-record latch.
        internal static byte[] CompanionFineAux(byte[] clock,byte[] anchor,int family,uint session,uint apHz,string expectedUid=null,ulong expectedToken=0){
            return CompanionWithFeatures(clock,anchor,family,session,apHz,expectedUid,expectedToken,SdCompanionFineAux);
        }
        // Explicit continuous64 producer attestation: the raw NPAR BEFORE
        // counter's high word is zero. Old features1/9 never imply this proof.
        internal static byte[] CompanionFineCounter64(byte[] clock,byte[] anchor,int family,uint session,uint apHz,string expectedUid=null,ulong expectedToken=0){
            return CompanionWithFeatures(clock,anchor,family,session,apHz,expectedUid,expectedToken,SdCompanionFineCounter64);
        }
        static byte[] CompanionWithFeatures(byte[] clock,byte[] anchor,int family,uint session,uint apHz,string expectedUid,ulong expectedToken,uint features){
            var bound=NpFirstSyncSd.ReadSector(clock,family,session,apHz,expectedUid,expectedToken);Need(bound!=null,"bound SD clock receipt required");Anchor(anchor,bound,family);var r=bound.Receipt;
            var b=new byte[512];Encoding.ASCII.GetBytes("NPFCLK1\0").CopyTo(b,0);Put(b,8,1);Put(b,12,(uint)family);PutUid(b,16,bound.Uid);
            uint[] words={session,r.Generation,r.SourceEpoch,r.ProfileCrc,r.Mask,apHz,r.SourceHz};for(int i=0;i<words.Length;i++)Put(b,28+i*4,words[i]);
            Put64(b,56,bound.Token);Put(b,64,U(r.Raw,324));Put(b,68,features);Buffer.BlockCopy(anchor,0,b,72,64);Seal(b);
            ReadCompanion(b,clock,family,session,apHz,expectedUid,expectedToken);return b;
        }
    }
}
