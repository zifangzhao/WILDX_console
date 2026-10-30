using System;
using System.IO;
using System.Collections.Generic;
using USB512_interface;

internal static class NpFirstSyncWireTests {
    static int checks;
    static BinaryWriter vectors;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Put16(byte[] b,int at,ushort n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,2);}
    static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
    static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
    static NpFirstSyncBinding Binding(NpFirstSyncReceipt r){return new NpFirstSyncBinding{Family=r.Family,Session=r.Session,Generation=r.Generation,SourceEpoch=r.SourceEpoch,
        Domain=r.Domain,CounterHz=r.CounterHz,ProfileCrc=r.ProfileCrc,Mask=r.Mask,ApHz=r.ApHz};}
    static bool Accept(byte[] b){try{NpFirstSyncWire.Parse(b);return true;}catch(InvalidDataException){return false;}}
    static void Vector(byte[] b,bool expected,string label){
        bool accepted=Accept(b);Check(accepted==expected,label);
        bool gate=accepted && NpFirstSyncWire.GateMatch(NpFirstSyncWire.Parse(b),Binding(NpFirstSyncWire.Parse(b)));
        if(vectors!=null && b!=null && b.Length==NpFirstSyncWire.Bytes){vectors.Write((byte)(accepted?1:0));vectors.Write((byte)(gate?1:0));vectors.Write(b);}
    }
    static byte[] Changed(byte[] b,int at,uint value){var c=(byte[])b.Clone();Put(c,at,value);Seal(c);return c;}
    internal static byte[] Fixture(int family,bool pending=false,uint hz=20000,byte mask=0){
        var b=new byte[NpFirstSyncWire.Bytes];Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("NPSTART1"),0,b,0,8);
        Put16(b,8,1);Put16(b,10,(ushort)b.Length);Put(b,12,(uint)(family<<8)|(pending?41U:63U));
        Put(b,16,0x12345);Put(b,20,9);Put(b,24,11);Put(b,28,17);Put(b,32,hz*(family==1?780U:1600U));Put(b,36,hz);Put(b,40,128000000);Put(b,44,0x12345678);
        b[48]=mask!=0?mask:(byte)(family==1?3:7);b[49]=1;while((b[48]&(1<<(b[49]-1)))==0)b[49]++;b[50]=15;b[51]=1;
        uint period=family==1?780U:1600U;
        for(int p=0;p<3;p++)if((b[48]&(1<<p))!=0){
            int at=64+p*20;ulong tick=pending?UInt64.MaxValue:period+(uint)p*37;
            Put64(b,at,tick);Put(b,at+8,pending||family==2?UInt32.MaxValue:(uint)(52+p*13));
            Put16(b,at+12,pending?UInt16.MaxValue:(ushort)((period-tick%period)%period));Put16(b,at+14,(ushort)period);Put(b,at+16,pending?1U:(family==1?31U:27U));
        }
        Put64(b,52,BitConverter.ToUInt64(b,64+(b[49]-1)*20));Put(b,124,pending?UInt32.MaxValue:1000U);Put(b,128,pending?UInt32.MaxValue:128000000U);
        uint[] rates={200U,1250U,48000U,30U};
        for(int a=0;a<4;a++){
            int at=132+a*48;b[at]=(byte)(a+1);b[at+1]=pending?(byte)1:(byte)111;b[at+2]=pending?(byte)0:(byte)32;b[at+3]=pending?(byte)0:(byte)1;
            Put(b,at+4,pending?UInt32.MaxValue:1000U);Put(b,at+8,pending?UInt32.MaxValue:128001000U);
            Put(b,at+12,pending?UInt32.MaxValue:1001U);Put(b,at+16,pending?UInt32.MaxValue:128128000U);
            Put64(b,at+20,pending?UInt64.MaxValue:(ulong)(123+a));Put(b,at+28,rates[a]);Put(b,at+32,1);
            Put64(b,at+36,pending?0x8000000000000000UL:(ulong)((a+1)*7));Put(b,at+44,pending?0U:1U);
        }
        Seal(b);return b;
    }
    static void RejectBinding(NpFirstSyncReceipt r,Action<NpFirstSyncBinding> mutation,string label){var e=Binding(r);mutation(e);Check(!NpFirstSyncWire.GateMatch(r,e),label);}
    static void Run(string output){
        using(vectors=new BinaryWriter(File.Create(output))){
            var ready=Fixture(2);var pending=Fixture(2,true);var parsed=NpFirstSyncWire.Parse(ready);
            foreach(int family in new[]{1,2})foreach(uint hz in new[]{7500U,15000U,20000U,30000U})foreach(byte mask in new byte[]{1,2,3,4,5,6,7}){
                var b=Fixture(family,false,hz,mask);Vector(b,true,"supported profile/port set");Vector(Fixture(family,true,hz,mask),true,"pending profile/port set");
                ulong num;uint den;Check(NpFirstSyncWire.GridStep(hz*(family==1?780U:1600U),1250,1,out num,out den) && den==1 && num==hz*(family==1?780U:1600U)/1250,"1250Hz integer source grid");
            }
            Check(!NpFirstSyncWire.GateMatch(NpFirstSyncWire.Parse(pending),Binding(NpFirstSyncWire.Parse(pending))),"pending proof released gate");
            Check(NpFirstSyncWire.GateMatch(parsed,Binding(parsed)),"complete proof did not release exact binding");
            Check(parsed.Reference==1 && parsed.ReferenceTick==1600 && parsed.Probes[1].FirstTick==1637 && parsed.Aux[0].OffsetNumerator==7 && !parsed.Aux[0].SampleLocked,"port/aux offsets were hidden or called sample locked");
            RejectBinding(parsed,e=>e.Family=1,"stale family");RejectBinding(parsed,e=>e.Session++,"stale session");RejectBinding(parsed,e=>e.Generation++,"stale capture generation");
            RejectBinding(parsed,e=>e.SourceEpoch++,"stale source epoch");RejectBinding(parsed,e=>e.Domain++,"stale counter domain");RejectBinding(parsed,e=>e.CounterHz++,"stale counter rate");
            RejectBinding(parsed,e=>e.ProfileCrc++,"stale applied profile");RejectBinding(parsed,e=>e.Mask=3,"stale probe mask");RejectBinding(parsed,e=>e.ApHz=30000,"stale AP rate");
            Vector(null,false,"null");Vector(new byte[327],false,"short");Vector(new byte[329],false,"long");
            for(int i=0;i<ready.Length;i++){var bad=(byte[])ready.Clone();bad[i]^=1;Vector(bad,false,"single byte corruption "+i);}
            foreach(int at in new[]{0,4,8,12,32,36,40,48,60,80,84,104,132,164,172,180,272}){
                var bad=Changed(ready,at,0xffffffff);Vector(bad,false,"resealed invalid field "+at);
            }
            foreach(int at in new[]{16,20,24})Vector(Changed(ready,at,0),false,"zero session/generation "+at);
            Vector(Changed(pending,64,0),false,"unknown source tick zero");Vector(Changed(pending,132+40,0),false,"unknown offset zero");
            var badPhase=(byte[])pending.Clone();Put16(badPhase,76,0);Seal(badPhase);Vector(badPhase,false,"unknown phase zero");
            Vector(Changed(ready,12,0x23e),false,"warmup framing not final generation");
            Vector(Changed(ready,12,0x23d),false,"false all-port summary");Vector(Changed(ready,12,0x22f),false,"false aux gate summary");
            Vector(Changed(ready,28,0),false,"mixed counter domain");var noDomain=Changed(ready,28,0);noDomain=Changed(noDomain,40,0);Vector(noDomain,false,"fine evidence with no domain");
            var unknownDomain=Changed(pending,28,0);unknownDomain=Changed(unknownDomain,40,0);Vector(unknownDomain,true,"pending unknown counter domain");
            var noProfile=Changed(pending,12,0x221);Vector(noProfile,false,"unknown profile lacks sentinel");noProfile=Changed(noProfile,44,UInt32.MaxValue);Vector(noProfile,true,"unknown profile sentinel");
            var readyUnknown=Changed(ready,12,0x237);readyUnknown=Changed(readyUnknown,44,UInt32.MaxValue);Vector(readyUnknown,true,"ready with unknown provider profile");
            var unknownReceipt=NpFirstSyncWire.Parse(readyUnknown);Check(NpFirstSyncWire.GateMatch(unknownReceipt,Binding(unknownReceipt)),"unknown profile blocked proven final framing");
            RejectBinding(unknownReceipt,e=>e.ProfileCrc=7,"known expected profile cannot match unknown producer");
            var noAux=(byte[])ready.Clone();noAux[50]=0;noAux[51]=0;Array.Clear(noAux,132,192);Put(noAux,12,0x22f);Seal(noAux);Vector(noAux,true,"no auxiliary streams");
            noAux[132]=1;Seal(noAux);Vector(noAux,false,"disabled auxiliary bytes must stay zero");
            var extraProbe=Fixture(2,false,20000,1);Put(extraProbe,84,1);Seal(extraProbe);Vector(extraProbe,false,"unselected port bytes must stay zero");
            Vector(Changed(ready,64+8,0),false,"V2 invented native frame counter");Vector(Changed(Fixture(1),64+8,0x100000),false,"V1 counter exceeds20bits");
            var wrongRef=(byte[])ready.Clone();wrongRef[49]=2;Seal(wrongRef);Vector(wrongRef,false,"reference must be lowest selected");
            var negative=Changed(ready,132+40,0xffffffff);Vector(negative,false,"pre-origin negative accepted offset");
            var fraction=Changed(ready,132+36,2);fraction=Changed(fraction,132+44,2);Vector(fraction,false,"unreduced source offset");
            Vector(Changed(ready,132+44,0),false,"known offset zero denominator");
            var rate=Changed(ready,132+28,500);rate=Changed(rate,132+32,2);Vector(rate,false,"unreduced stream rate");
            var locked=(byte[])ready.Clone();locked[181]|=16;Seal(locked);Vector(locked,false,"positive offset cannot be sample0 lock");
            Put64(locked,180+36,0);Seal(locked);Vector(locked,true,"producer-attested zero-phase lock");
            locked[183]=2;Seal(locked);Vector(locked,false,"foreground acceptance cannot attest sample lock");
            ulong step;uint denominator;Check(NpFirstSyncWire.GridStep(10,3,1,out step,out denominator)&&step==10&&denominator==3,"future rational grid was truncated");
            Check(!NpFirstSyncWire.GridStep(10,0,1,out step,out denominator),"zero auxiliary rate accepted");
            Check(!NpFirstSyncWire.GridStep(0,1250,1,out step,out denominator),"zero source rate accepted");
            Check(NpFirstSyncWire.GridStep(UInt32.MaxValue,UInt32.MaxValue,UInt32.MaxValue,out step,out denominator)&&step==UInt32.MaxValue&&denominator==1,"grid multiplication overflow");
            for(int family=1;family<=2;family++){
                var sector=new byte[512];Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes(family==1?"N1SDUTC2":"N2SDUTC2"),0,sector,0,8);
                Put(sector,8,2);Put(sector,12,0x12345);Put(sector,72,20000);Seal(sector);
                Check(NpFirstSyncWire.ParseSdExtension(sector,family,0x12345,20000)==null,"zero legacy extension changed");
                Fixture(family).CopyTo(sector,160);Seal(sector);Check(NpFirstSyncWire.ParseSdExtension(sector,family,0x12345,20000).Family==family,"SD extension fit");
                sector[488]=1;Seal(sector);try{NpFirstSyncWire.ParseSdExtension(sector,family,0x12345,20000);throw new Exception("SD tail reserved accepted");}catch(InvalidDataException){checks++;}
                sector[488]=0;Seal(sector);sector[508]^=1;try{NpFirstSyncWire.ParseSdExtension(sector,family,0x12345,20000);throw new Exception("SD outer CRC accepted");}catch(InvalidDataException){checks++;}
                Seal(sector);try{NpFirstSyncWire.ParseSdExtension(sector,family,0x12346,20000);throw new Exception("SD session substitution accepted");}catch(InvalidDataException){checks++;}
                try{NpFirstSyncWire.ParseSdExtension(sector,family,0x12345,30000);throw new Exception("SD profile substitution accepted");}catch(InvalidDataException){checks++;}
            }
        }
        vectors=null;Console.WriteLine("PASS "+checks+" C# first-sync receipt/gate checks");
    }
    static void Main(string[] args){
        if(args.Length!=2)throw new ArgumentException("generate|reply path");
        if(args[0]=="generate"){Run(args[1]);return;}
        var bytes=File.ReadAllBytes(args[1]);Check(bytes.Length==2*NpFirstSyncWire.Bytes,"C golden output length");
        for(int family=1;family<=2;family++){
            var record=new byte[NpFirstSyncWire.Bytes];Buffer.BlockCopy(bytes,(family-1)*record.Length,record,0,record.Length);var expected=Fixture(family);
            for(int i=0;i<record.Length;i++)Check(record[i]==expected[i],"C/C# byte parity "+family+":"+i);
            Check(NpFirstSyncWire.Parse(record).Family==family,"C fixture family");
        }
        Console.WriteLine("PASS "+checks+" C-produced exact byte parity checks");
    }
}
