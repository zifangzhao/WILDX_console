using System;
using System.IO;
using System.Linq;
using USB512_interface;

internal static class NpV2GridSectorTests {
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Bad(Action action,string why){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static void Put16(byte[] b,int at,ushort n){b[at]=(byte)n;b[at+1]=(byte)(n>>8);}
    static void Put32(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(i*8));}
    static uint Crc(byte[] b,int length){uint c=UInt32.MaxValue;for(int i=0;i<length;i++){c^=b[i];for(int j=0;j<8;j++)c=(c&1)!=0?(c>>1)^0xedb88320:c>>1;}return ~c;}
    static void OuterSeal(byte[] b){Put32(b,28,Crc(b,28));}
    static void InnerSeal(byte[] b){int n=BitConverter.ToUInt16(b,24);var frame=b.Skip(32).Take(n).ToArray();Put32(frame,44,NpImuGridWire.Crc(frame));Buffer.BlockCopy(frame,0,b,32,n);}
    static NpImuGridBatch Batch(int count,ulong first=0){return new NpImuGridBatch{CaptureGeneration=42,FirstSlot=first,SlotCount=(ushort)count,
        ObservedMask=(ushort)((1<<count)-1),AgValid=(ushort)((1<<count)-1),MagValid=(ushort)((1<<count)-1),
        Rows=Enumerable.Range(0,count).Select(r=>Enumerable.Range(0,9).Select(c=>r*10+c).ToArray()).ToArray()};}
    static NpImuGridPhaseBinding Binding(){return new NpImuGridPhaseBinding{Uid=Enumerable.Range(1,12).Select(i=>(byte)i).ToArray(),Session=31,CaptureGeneration=42,SourceEpoch=51,CounterDomain=61,Token=0x1122334455667788};}
    static NpImuGridPhaseRecord Phase(){var b=Binding();return new NpImuGridPhaseRecord{Uid=b.Uid,Session=b.Session,CaptureGeneration=b.CaptureGeneration,SourceEpoch=b.SourceEpoch,CounterDomain=b.CounterDomain,Token=b.Token,
        FirstAcceptedSlot=0,LowerNumerator=1000,UpperNumerator=1007,Denominator=5,MaximumJitter=2,Flags=3};}
    static void PhaseTests(byte[] full,byte[] one){
        var phase=Phase();var raw=NpV2GridSector.PackPhase(42,7,phase,Binding());var original=(byte[])raw.Clone();
        Check(raw.Skip(32).Take(84).SequenceEqual(NpImuGridPhase.Pack(phase))&&BitConverter.ToUInt16(raw,24)==84,"Wrapped phase changed NSGP84 bytes");
        var read=NpV2GridSector.ReadPhase(raw,42,7,Binding());Check(read.FirstAcceptedSlot==0&&read.LowerNumerator==1000&&read.UpperNumerator==1007&&read.Denominator==5&&read.MaximumJitter==2&&read.Flags==3,"Phase interval/jitter/context changed");
        Check(raw.SequenceEqual(original),"Phase parse mutated borrowed carrier");
        // Explicitly dispatch phase followed by two same-parent row sectors.
        // The codec does not invent a session state or one phase per packet.
        var sequence=new[]{raw,full,one};var proof=NpV2GridSector.ReadPhase(sequence[0],42,7,Binding());var first=NpV2GridSector.Parse(sequence[1],42,7);var next=NpV2GridSector.Parse(sequence[2],42,7);
        Check(proof.FirstAcceptedSlot==first.FirstSlot&&next.FirstSlot==first.FirstSlot+first.SlotCount,"Phase + 12+1 row positions changed");
        Bad(()=>NpV2GridSector.Parse(raw,42,7),"NSGP misread as NSG row sector");Bad(()=>NpV2GridSector.ReadPhase(full,42,7,Binding()),"NSG rows misread as phase proof");
        Bad(()=>NpV2GridSector.ReadPhase(raw,42,8,Binding()),"Phase from wrong NP parent/order admitted");Bad(()=>NpV2GridSector.ReadPhase(raw,41,7,Binding()),"Wrong phase wrapper generation admitted");
        foreach(var change in new Action<NpImuGridPhaseBinding>[] {b=>b.Uid[0]^=1,b=>b.Session++,b=>b.CaptureGeneration++,b=>b.SourceEpoch++,b=>b.CounterDomain++,b=>b.Token++,b=>b.Uid=null,b=>b.Token=0}){
            var b=Binding();change(b);Bad(()=>NpV2GridSector.ReadPhase(raw,42,7,b),"Mismatched independent phase binding admitted");Bad(()=>NpV2GridSector.PackPhase(42,7,phase,b),"Phase pack ignored independent binding");
        }
        Bad(()=>NpV2GridSector.ReadPhase(raw,42,7,null),"Null expected phase context admitted");Bad(()=>NpV2GridSector.PackPhase(43,7,phase,Binding()),"Phase pack wrapper generation mismatch admitted");
        var gen=(byte[])raw.Clone();Put32(gen,12,43);OuterSeal(gen);Bad(()=>NpV2GridSector.ReadPhase(gen,43,7,Binding()),"Valid inner proof borrowed a different wrapper generation");
        for(int at=0;at<116;at++){var bad=(byte[])raw.Clone();bad[at]^=1;Bad(()=>NpV2GridSector.ReadPhase(bad,42,7,Binding()),"Phase header/body corruption admitted");}
        for(int at=116;at<512;at++){var bad=(byte[])raw.Clone();bad[at]=1;Bad(()=>NpV2GridSector.ReadPhase(bad,42,7,Binding()),"Nonzero phase sector padding admitted");}
        foreach(var edit in new Action<byte[]>[] {b=>Put16(b,24,83),b=>Put16(b,24,85),b=>Put16(b,26,1),b=>Put32(b,8,2)}){
            var bad=(byte[])raw.Clone();edit(bad);OuterSeal(bad);Bad(()=>NpV2GridSector.ReadPhase(bad,42,7,Binding()),"Resealed invalid phase envelope admitted");
        }
        // Reseal inner CRC to isolate context/semantic admission from integrity.
        foreach(int at in new[]{8,20,24,28,32,36,76}){var bad=(byte[])raw.Clone();bad[32+at]^=0x80;var body=bad.Skip(32).Take(84).ToArray();Put32(body,80,NpImuGridPhase.Crc(body));Buffer.BlockCopy(body,0,bad,32,84);Bad(()=>NpV2GridSector.ReadPhase(bad,42,7,Binding()),"CRC-valid wrong phase context/flags admitted");}
        var unknown=Phase();unknown.Flags=0;unknown.LowerNumerator=unknown.UpperNumerator=Int64.MinValue;unknown.Denominator=0;unknown.MaximumJitter=UInt32.MaxValue;
        var unknownRead=NpV2GridSector.ReadPhase(NpV2GridSector.PackPhase(42,7,unknown,Binding()),42,7,Binding());
        Check(unknownRead.Flags==0&&unknownRead.Denominator==0&&unknownRead.MaximumJitter==UInt32.MaxValue,"Explicit unknown phase/jitter sentinel became timing proof");
        raw[40]^=1;Check(read.Uid[0]==1,"Parsed phase UID aliases mutable sector");
        // No hidden ordering state: a caller can inspect a later archival
        // phase independently. Global ordering belongs to the session reader.
        Check(NpV2GridSector.Parse(one,42,7).FirstSlot==12&&NpV2GridSector.ReadPhase(original,42,7,Binding()).FirstAcceptedSlot==0,"Wrapper invented session ordering state");
    }
    static void Main(string[] args){
        var native=File.ReadAllBytes(args[0]);Check(native.Length==1024,"Native golden extent");
        var full=NpV2GridSector.Pack(42,7,NpImuGridWire.Pack(Batch(12)));var one=NpV2GridSector.Pack(42,7,NpImuGridWire.Pack(Batch(1,12)));
        Check(full.Concat(one).SequenceEqual(native),"Actual native encode versus managed 12+1 golden bytes");
        for(int i=0;i<2;i++){
            var raw=native.Skip(i*512).Take(512).ToArray();var original=(byte[])raw.Clone();var batch=NpV2GridSector.Parse(raw,42,7);
            Check(batch.FirstSlot==(i==0?0UL:12UL)&&batch.SlotCount==(i==0?12:1)&&batch.Rows.Length==batch.SlotCount,"Two sectors bound to same NP parent preserve slot positions");
            Check(raw.SequenceEqual(original)&&NpV2GridSector.Pack(42,7,NpImuGridWire.Pack(batch)).SequenceEqual(raw),"Exact parse/pack/borrowed input preservation");
        }
        foreach(int n in new[]{0,31,511,513})Bad(()=>NpV2GridSector.Parse(new byte[n],42,7),"Invalid sector extent accepted");
        Bad(()=>NpV2GridSector.Parse(null,42,7),"Null sector accepted");Bad(()=>NpV2GridSector.Parse(one,0,7),"Zero expected generation accepted");
        Bad(()=>NpV2GridSector.Parse(one,41,7),"Wrong generation accepted");Bad(()=>NpV2GridSector.Parse(one,42,8),"Wrong parent NP block accepted");
        for(int i=0;i<32;i++){var bad=(byte[])one.Clone();bad[i]^=1;Bad(()=>NpV2GridSector.Parse(bad,42,7),"Header byte corruption admitted");}
        foreach(var mutate in new Action<byte[]>[]{b=>b[0]=0,b=>Put32(b,8,2),b=>Put32(b,12,0),b=>Put16(b,26,1),b=>Put16(b,24,0),b=>Put16(b,24,47),b=>Put16(b,24,83),b=>Put16(b,24,85),b=>Put16(b,24,481),b=>Put16(b,24,UInt16.MaxValue)}){
            var bad=(byte[])one.Clone();mutate(bad);OuterSeal(bad);Bad(()=>NpV2GridSector.Parse(bad,42,7),"Resealed malformed outer header accepted");
        }
        var wrongInner=(byte[])one.Clone();Put32(wrongInner,12,43);OuterSeal(wrongInner);Bad(()=>NpV2GridSector.Parse(wrongInner,43,7),"Inner generation mismatch admitted");
        foreach(int at in new[]{32,76,80,115}){var bad=(byte[])one.Clone();bad[at]^=1;Bad(()=>NpV2GridSector.Parse(bad,42,7),"Inner tag/CRC/payload corruption admitted");}
        foreach(var mutate in new Action<byte[]>[]{b=>Put16(b,32+16,13),b=>Put16(b,32+20,2),b=>Put16(b,32+22,2),b=>Put16(b,32+24,2),
            b=>Put16(b,32+26,2),b=>Put16(b,32+28,2),b=>Put16(b,32+30,2),b=>Put32(b,32+36,1),b=>Put32(b,32+40,43)}){
            var bad=(byte[])one.Clone();mutate(bad);InnerSeal(bad);Bad(()=>NpV2GridSector.Parse(bad,42,7),"CRC-valid invalid NSG mask/generation/flags admitted");
        }
        for(int i=116;i<512;i++){var bad=(byte[])one.Clone();bad[i]=1;Bad(()=>NpV2GridSector.Parse(bad,42,7),"Nonzero sector padding admitted");}
        var sparse=new NpImuGridBatch{FirstSlot=99,SlotCount=12,CaptureGeneration=42,ObservedMask=0x801,AgValid=0x801,MagValid=1,AgRepeat=0x800,ErrorMask=1,
            Rows=new[]{new[]{Int32.MinValue,Int32.MaxValue,-1,0,1,1000000,-1000000,300,-300},new int[9]}};
        var sparseRaw=NpV2GridSector.Pack(42,UInt64.MaxValue,NpImuGridWire.Pack(sparse));var copy=NpV2GridSector.Parse(sparseRaw,42,UInt64.MaxValue);
        Check(copy.ObservedMask==0x801&&copy.AgRepeat==0x800&&copy.Rows[0][0]==Int32.MinValue&&copy.Rows[0][1]==Int32.MaxValue,"Sparse masks/signed physical units lost");
        sparseRaw[80]=0;Check(copy.Rows[0][0]==Int32.MinValue,"Parsed rows alias caller sector");
        var empty=NpV2GridSector.Pack(42,0,NpImuGridWire.Pack(new NpImuGridBatch{SlotCount=12,CaptureGeneration=42}));Check(NpV2GridSector.Parse(empty,42,0).ObservedMask==0,"Explicit unobserved slots rejected");
        Bad(()=>NpV2GridSector.Pack(0,7,NpImuGridWire.Pack(Batch(1))),"Pack zero generation accepted");Bad(()=>NpV2GridSector.Pack(43,7,NpImuGridWire.Pack(Batch(1))),"Pack inner generation mismatch accepted");
        Bad(()=>NpV2GridSector.Pack(42,7,null),"Pack null accepted");Bad(()=>NpV2GridSector.Pack(42,7,new byte[481]),"Pack oversize accepted");
        NpImuGridBatch optional;string error;var invalid=(byte[])one.Clone();invalid[511]=1;
        Check(!NpV2GridSector.TryParse(invalid,42,7,out optional,out error)&&optional==null&&!String.IsNullOrEmpty(error),"Optional grid error not isolated");
        Check(NpV2GridSector.TryParse(one,42,7,out optional,out error)&&optional.FirstSlot==12&&error==null,"A prior grid error poisoned later valid parsing");
        PhaseTests(full,one);
        Console.WriteLine("PASS "+checks+" managed NPGRID02 NSG/NSGP sector checks + exact actual-native NSG-only 12+1 golden bytes; no producer-phase parity or transport/timing inference");
    }
}
