using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;

// Actual-assembly parser, sparse read-only RAM sectors; no USB/BLE/UI calls.
// Fixture builders encode the frozen np_store_v2.h wire records, not a parser.
static class NpV1StoreV2Tests {
    const uint Capacity=1048576;const ulong Epoch=0x0102030405060708UL;
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Type parser,catalog,format;static int checks;
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static object Get(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static ulong N(object value,string name){return Convert.ToUInt64(Get(value,name));}
    static int Result(object value){return Convert.ToInt32(Get(value,"Result"));}
    static object CallOn(Type type,string name,params object[] args){try{return type.GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(null,args);}catch(TargetInvocationException ex){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}}
    static object Call(string name,params object[] args){return CallOn(parser,name,args);}
    static bool Downloadable(object record){return (bool)record.GetType().GetProperty("Downloadable",All).GetValue(record,null);}
    static Exception Failure(Action action){try{action();}catch(Exception ex){return ex;}throw new Exception("Invalid store accepted");}
    static void P(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static void P64(byte[] b,int at,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,8);}
    static void Magic(byte[] b,int at,string value){Encoding.ASCII.GetBytes(value).CopyTo(b,at);}
    static void Seal(byte[] b){uint c=uint.MaxValue;for(int i=0;i<508;i++){c^=b[i];for(int bit=0;bit<8;bit++)c=(c>>1)^((c&1)!=0?0xedb88320U:0);}P(b,508,~c);}
    static byte[] Identity(ulong epoch=Epoch){var b=new byte[512];Magic(b,0,"NP_FILESYSTEM_V2");Magic(b,32,"NP_CE64_2");P(b,48,2);P(b,52,Capacity);P64(b,56,epoch);P(b,64,8192);P(b,68,4032);Seal(b);return b;}
    static byte[] Checkpoint(ulong generation=5,uint count=2,uint frontier=24576,ulong epoch=Epoch){
        var b=new byte[512];Magic(b,0,"NPS2CP01");P(b,8,2);P(b,12,Capacity);P64(b,16,epoch);P64(b,24,generation);P(b,32,frontier);P(b,36,count);
        if(count!=0){P(b,40,frontier-8192);P(b,44,frontier);P(b,48,102);P(b,52,1);}Seal(b);return b;
    }
    static byte[] Record(uint index,ulong generation=5,uint state=1,ulong epoch=Epoch){
        uint start=8192+index*8192;var b=new byte[512];Magic(b,0,"NPS2REC1");P(b,8,2);P(b,12,Capacity);P64(b,16,epoch);P64(b,24,generation);P(b,32,index);P(b,36,start);P(b,40,start+8192);P(b,44,start);P(b,48,101+index);P(b,52,state);Seal(b);return b;
    }
    sealed class Card:Stream {
        internal readonly Dictionary<long,byte[]> Sectors=new Dictionary<long,byte[]>();
        internal readonly HashSet<long> IoFaults=new HashSet<long>(),ShortSectors=new HashSet<long>();
        internal readonly List<long> Reads=new List<long>();
        internal int Writes,Fragment;internal bool Seekable=true,AllowRecordingHeaders,Writable;internal Action<long> Observed;
        long position;internal long SectorCount=Capacity;
        internal void Pair(long lba,byte[] bytes){Sectors[lba]=(byte[])bytes.Clone();Sectors[lba+1]=(byte[])bytes.Clone();}
        internal Card(){Sectors[16]=Identity();Sectors[24]=Identity();Pair(112,Checkpoint());Pair(128,Record(0,2,2));Pair(130,Record(1));}
        public override int Read(byte[] b,int offset,int count){
            if(count==0)return 0;long lba=position/512;Reads.Add(lba);if(Observed!=null)Observed(lba);
            if(lba>=8192&&(!AllowRecordingHeaders||!Sectors.ContainsKey(lba)))throw new Exception("Parser read payload or guessed later free space");
            if(IoFaults.Contains(lba))throw new IOException("Injected metadata I/O fault at "+lba);
            if(position>=Length)return 0;int at=(int)(position%512),n=Math.Min(count,512-at);
            if(ShortSectors.Contains(lba)){if(at>=128)return 0;n=Math.Min(n,128-at);}
            if(Fragment!=0)n=Math.Min(n,Fragment);byte[] sector;
            if(Sectors.TryGetValue(lba,out sector))Buffer.BlockCopy(sector,at,b,offset,n);else Array.Clear(b,offset,n);
            position+=n;return n;
        }
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return Seekable;}}public override bool CanWrite{get{return Writable;}}
        public override long Length{get{return SectorCount*512;}}public override long Position{get{return position;}set{position=value;}}
        public override long Seek(long value,SeekOrigin origin){return Position=(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:Length)+value;}
        public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException();}
        public override void Write(byte[] b,int offset,int count){Writes++;throw new Exception("Parser attempted a card write");}
    }
    static object Mount(Card card,Func<bool> cancelled=null){return Call("Mount",card,cancelled);}
    static object Read(Card card,Func<bool> cancelled=null){return Call("Read",card,cancelled);}
    static object Entry(Card card,object store,uint index,Func<bool> cancelled=null){return Call("ReadRecord",card,store,index,cancelled);}
    static IList Entries(object store){return (IList)Get(store,"Entries");}
    static void Allocation(object store,uint count=2,uint frontier=24576,ulong generation=5){
        Need((bool)Get(store,"Ready")&&N(store,"Epoch")==Epoch&&N(store,"Generation")==generation,"Mounted epoch/generation lost");
        Need(N(store,"Count")==count&&N(store,"Frontier")==frontier&&N(store,"Capacity")==Capacity,"Records changed allocation authority");
    }
    static void MountCases(){
        using(var card=new Card()){
            object store=Mount(card);Allocation(store);Need(Entries(store).Count==0,"Mount depended on directory enumeration");
            Need(card.Reads.SequenceEqual(new long[]{16,24,112,113,114,115})&&card.Writes==0,"Mount read records/payload or wrote metadata");
            Need(((byte[])Get(store,"Identity")).SequenceEqual(card.Sectors[16])&&((byte[])Get(store,"Checkpoint")).SequenceEqual(card.Sectors[112]),"Original selected metadata was not retained");
        }
        foreach(long sector in new long[]{16,24,112,113})using(var card=new Card()){
            card.Sectors[sector][508]^=1;object store=Mount(card);Allocation(store);
            Need(N(store,sector<100?"IdentityLba":"CheckpointLba")==((sector==16)?24UL:sector==24?16UL:sector==112?113UL:112UL),"Corrupt copy did not fall back");
        }
        foreach(bool shortRead in new[]{false,true})using(var card=new Card()){
            if(shortRead){card.ShortSectors.Add(16);card.ShortSectors.Add(112);}else{card.IoFaults.Add(16);card.IoFaults.Add(112);}
            Allocation(Mount(card));Need(card.Writes==0,"Metadata fallback wrote to source");
        }
        using(var card=new Card()){card.Fragment=37;Allocation(Mount(card));Need(card.Reads.Count>4,"Short positive sector reads not exercised");}
        foreach(bool checkpoint in new[]{false,true})using(var card=new Card()){
            long lba=checkpoint?113:24;card.Sectors[lba][400]^=1;Seal(card.Sectors[lba]);
            Need(Failure(()=>Mount(card)) is InvalidDataException,"Conflicting equal-generation/identity copies accepted");Need(card.Writes==0,"Conflict repaired metadata");
        }
        using(var card=new Card()){card.Sectors[24]=Identity(Epoch+1);Need(Failure(()=>Mount(card)) is InvalidDataException,"Different valid identity epochs were adopted");}
        using(var card=new Card()){card.Sectors[112]=Checkpoint(99,2,24576,Epoch-1);object store=Mount(card);Allocation(store);Need(N(store,"CheckpointLba")==113,"Old checkpoint epoch was not ignored");}
        using(var card=new Card()){card.Pair(112,Checkpoint(5,2,24576,Epoch-1));Need(Failure(()=>Mount(card)) is InvalidDataException,"No matching epoch checkpoint accepted");Need(card.Reads.All(lba=>lba<128),"Missing allocation checkpoint caused directory scan");}
        using(var card=new Card()){card.Sectors[113]=Checkpoint(6,2,32768);Allocation(Mount(card),2,32768,6);}
        using(var card=new Card()){card.Sectors[112]=Checkpoint(4,1,16384);Allocation(Mount(card));}
        using(var card=new Card()){card.Sectors[112]=Checkpoint(ulong.MaxValue-1);card.Sectors[113]=Checkpoint(ulong.MaxValue);Allocation(Mount(card),2,24576,ulong.MaxValue);}
        foreach(int kind in new[]{0,1,2,3})using(var card=new Card()){
            card.Sectors[113]=kind==0?Checkpoint(7):kind==1?Checkpoint(6,2,16384):kind==2?Checkpoint(6,1,24576):Checkpoint(6,4,24576);
            Need(Failure(()=>Mount(card)) is InvalidDataException,"Checkpoint generation/frontier/count conflict accepted: "+kind);
        }
        foreach(int offset in new[]{8,12,32,36,40,44,48,52})using(var card=new Card()){
            byte[] b=Checkpoint();P(b,offset,offset==36?4033U:offset==48?0U:offset==52?2U:BitConverter.ToUInt32(b,offset)+1);Seal(b);card.Pair(112,b);
            Need(Failure(()=>Mount(card)) is InvalidDataException,"Invalid checkpoint field accepted: "+offset);
        }
        foreach(int kind in new[]{0,1,2,3,4})using(var card=new Card()){
            byte[] b=Checkpoint(1,0,8192);if(kind!=0){int at=new[]{0,40,44,48,52}[kind];P(b,at,1);Seal(b);}card.Pair(112,b);
            if(kind==0){object store=Read(card);Allocation(store,0,8192,1);Need(Entries(store).Count==0&&card.Reads.Count==6,"Empty store scanned stale descriptors");}
            else Need(Failure(()=>Mount(card)) is InvalidDataException,"Nonempty fields in zero-count checkpoint accepted");
        }
        foreach(int kind in new[]{0,1,2,3,4,5})using(var card=new Card()){
            byte[] b=Identity();if(kind==0)Magic(b,0,"CE_FILESYSTEM_V1");else if(kind==1)Magic(b,32,"NP_PSSI_7");else if(kind==2)P(b,48,3);else if(kind==3)P(b,52,Capacity-1);else if(kind==4)P64(b,56,0);else P(b,68,4033);Seal(b);card.Sectors[16]=b;card.Sectors[24]=(byte[])b.Clone();
            Need(Failure(()=>Mount(card)) is InvalidDataException,"Legacy/V2/unsupported identity adopted: "+kind);Need(card.Reads.SequenceEqual(new long[]{16,24}),"Unsupported identity probed catalog/checkpoints");
        }
        using(var card=new Card()){card.IoFaults.UnionWith(new long[]{16,24});Need(Failure(()=>Mount(card)) is InvalidDataException,"Both unreadable identities mounted");}
        using(var card=new Card()){card.Seekable=false;Need(Failure(()=>Mount(card)) is IOException&&card.Reads.Count==0,"Unseekable source admitted");}
        using(var card=new Card()){card.SectorCount++;Need(Failure(()=>Mount(card)) is InvalidDataException,"Changed physical capacity accepted");}
    }
    static void RecordCases(){
        using(var card=new Card()){
            object store=Read(card);Allocation(store);IList entries=Entries(store);Need(entries.Count==2&&entries.Cast<object>().All(r=>Result(r)==0),"Valid raw records missing");
            Need(N(entries[0],"Start")==8192&&N(entries[0],"ReservedEnd")==16384&&N(entries[0],"CommittedEnd")==8192&&N(entries[0],"Session")==101&&N(entries[0],"State")==2,"Raw record fields mapped to payload-footer semantics");
            Need(((byte[])Get(entries[1],"Raw")).SequenceEqual(card.Sectors[130]),"Selected raw record was not retained");Need(card.Reads.All(lba=>lba<8192)&&card.Writes==0,"Parser scanned payload or wrote source");
        }
        using(var card=new Card()){
            card.Sectors.Remove(128);card.Sectors.Remove(129);object store=Read(card);Allocation(store);Need(Result(Entries(store)[0])==-3&&Result(Entries(store)[1])==0,"Missing first record hid allocation/later record");Need(Get(Entries(store)[0],"ValidationError")!=null,"Missing record not reported");
        }
        using(var card=new Card()){
            card.Sectors[128][508]^=1;object store=Read(card);Need(Result(Entries(store)[0])==0,"One corrupt record copy prevented fallback");
        }
        using(var card=new Card()){
            card.IoFaults.UnionWith(new long[]{128,129});object store=Read(card);Allocation(store);Need(Result(Entries(store)[0])==-3&&Result(Entries(store)[1])==0,"I/O-failed record copies blocked independent allocation/later record");
        }
        using(var card=new Card()){card.Sectors[128]=Record(0,99,2,Epoch-1);object store=Read(card);Need(N(Entries(store)[0],"Generation")==2,"Stale record epoch selected");}
        using(var card=new Card()){card.Pair(128,Record(0,99,2,Epoch-1));object store=Read(card);Allocation(store);Need(Result(Entries(store)[0])==-3,"Old epoch record appeared current");}
        using(var card=new Card()){card.Sectors[129][400]^=1;Seal(card.Sectors[129]);object store=Read(card);Allocation(store);Need(Result(Entries(store)[0])==-1,"Equal-generation raw record conflict accepted");}
        using(var card=new Card()){
            byte[] older=Record(1,1),newer=Record(1,6,2);P(newer,44,20480);Seal(newer);card.Sectors[130]=older;card.Sectors[131]=newer;
            object store=Read(card);Need(Result(Entries(store)[1])==0&&N(Entries(store)[1],"Generation")==6&&N(Entries(store)[1],"State")==2,"Allowed nonadjacent/ahead record publication rejected");
        }
        foreach(int kind in new[]{0,1,2,3,4,5})using(var card=new Card()){
            byte[] older=Record(0,2),newer=Record(0,9);P(older,40,24576);P(newer,40,24576);
            if(kind==0){P(newer,36,16384);P(newer,44,16384);}else if(kind==1)P(newer,48,999);else if(kind==2)P(newer,40,16384);else if(kind==3){P(older,44,12288);P(newer,44,8192);}else P(older,52,kind==4?2U:3U);
            Seal(older);Seal(newer);card.Sectors[128]=older;card.Sectors[129]=newer;object store=Read(card);Allocation(store);
            Need(Result(Entries(store)[0])==-1&&Result(Entries(store)[1])==0,"Record mutation/retreat/terminal conflict accepted: "+kind);
        }
        foreach(int offset in new[]{8,12,32,36,40,44,48,52})using(var card=new Card()){
            byte[] bad=Record(0);P(bad,offset,offset==32?1U:offset==44?0U:offset==48?0U:offset==52?4U:BitConverter.ToUInt32(bad,offset)+1);Seal(bad);card.Pair(128,bad);
            object store=Read(card);Allocation(store);Need(Result(Entries(store)[0])==-3,"Bad record geometry/index/state accepted: "+offset);
        }
        using(var card=new Card()){
            object store=Mount(card);int reads=card.Reads.Count;Need(Result(Entry(card,store,2))==-1&&card.Reads.Count==reads,"Out-of-count index read guessed records");
        }
        using(var card=new Card()){
            byte[] cp=Checkpoint(7,4032,Capacity),lastRecord=Record(0);P(lastRecord,32,4031);Seal(lastRecord);card.Pair(112,cp);card.Pair(8190,lastRecord);
            object store=Mount(card);object last=Entry(card,store,4031);Need(Result(last)==0&&card.Reads.Contains(8190)&&card.Reads.Contains(8191),"Maximum record slot was not bounded to metadata area");
            int reads=card.Reads.Count;Need(Result(Entry(card,store,4032))==-1&&card.Reads.Count==reads,"Record index4032 escaped metadata area");
        }
        using(var card=new Card()){
            var unmounted=Activator.CreateInstance(parser.GetNestedType("Store",All),true);
            unmounted.GetType().GetField("Count",All).SetValue(unmounted,1U);
            Need(Result(Entry(card,unmounted,0))==-1&&card.Reads.Count==0,"Unready state authorized record reads");
        }
    }
    static void Cancellation(){
        using(var card=new Card()){Need(Failure(()=>Mount(card,()=>true)) is OperationCanceledException&&card.Reads.Count==0,"Pre-cancelled mount read source");}
        using(var card=new Card()){Need(Failure(()=>Mount(card,()=>card.Reads.Count!=0)) is OperationCanceledException&&card.Reads.Count==1,"Mount cancellation swallowed by copy fallback");}
        using(var card=new Card()){bool cancelled=false;card.Observed=lba=>{if(lba==128)cancelled=true;};Need(Failure(()=>Read(card,()=>cancelled)) is OperationCanceledException,"Record cancellation became missing/corrupt record");Need(!card.Reads.Contains(129)&&card.Writes==0,"Cancellation read next copy or wrote source");}
    }
    static byte[] RecordAt(uint index,uint start,uint end,uint committed,uint session,uint state=2){
        byte[] b=Record(index);P(b,36,start);P(b,40,end);P(b,44,committed);P(b,48,session);P(b,52,state);Seal(b);return b;
    }
    static void Payload(Card card,uint start,uint session,uint version=6,uint chunks=8,uint state=3,uint fault=0,uint requestedEnd=131072){
        var h=new byte[512];Magic(h,0,"NPSDLOG1");P(h,8,version);P(h,12,session);P(h,16,version>=7?0U:3U);P(h,20,1);P(h,24,version>=7?0U:20000U);P(h,28,version>=7?0U:20000U/12);
        P(h,32,15360);P(h,36,128);P(h,40,start);P(h,44,requestedEnd);P(h,48,(start+8192)%16384==0?8192U:16384U);P(h,80,2);P(h,96,61632);
        if(version>=7){P(h,112,40);if(version==8){P(h,116,200000);P(h,120,1);}}
        Magic(h,464,"NPS2BND1");P64(h,472,Epoch);P(h,480,start==8192?0U:1U);Seal(h);
        byte[] f=(byte[])h.Clone();Magic(f,0,"NPSDEND1");P(f,60,state);P(f,64,fault);P(f,68,chunks);Seal(f);card.Sectors[start+3]=h;card.Sectors[start+4]=f;
    }
    static Card DispatchCard(uint firstVersion=6,uint secondVersion=6){
        var card=new Card{AllowRecordingHeaders=true};byte[] cp=Checkpoint(5,2,65536);P(cp,40,32768);P(cp,48,202);P(cp,52,0);Seal(cp);card.Pair(112,cp);
        card.Pair(128,RecordAt(0,8192,32768,17408,201));card.Pair(130,RecordAt(1,32768,65536,50176,202));
        Payload(card,8192,201,firstVersion);Payload(card,32768,202,secondVersion);return card;
    }
    static IList Catalog(Card card,bool available=true){return (IList)CallOn(catalog,available?"ReadAvailable":"Read",card);}
    static void MetadataOnly(Card card){
        Need(card.Writes==0,"Catalog dispatch wrote source");
        Need(card.Reads.All(lba=>lba<8192||lba==8195||lba==8196||lba==32771||lba==32772),"Catalog dispatch read payload, guessed extent or later free space");
    }
    static void GoodCatalog(IList records){
        Need(records.Count==2&&records.Cast<object>().All(Downloadable),"Filesystem2 valid records not independently downloadable");
        Need(N(records[0],"Start")==8192&&N(records[1],"Start")==32768,"Filesystem2 starts guessed from adjacent committed ends");
        Need(N(records[0],"End")==17408&&N(records[1],"End")==50176,"Complete payload footer end not exposed to strict exporter");
        Need(N(records[0],"CatalogEnd")==32768&&N(records[1],"CatalogEnd")==65536,"Conservative reserved grants were reclaimed/shrunk");
        Need(N(records[0],"Session")==201&&N(records[1],"Session")==202&&N(records[1],"Chunks")==8&&N(records[1],"State")==3&&N(records[1],"Fault")==0,"Footer counts/state/session were fabricated or mapped from filesystem records");
        Need(((string)Get(records[0],"RecoveryNotice")).Contains("no tail reclaimed")&&((string)Get(records[1],"RecoveryNotice")).Contains("unused tail"),"Read-only allocation warning missing");
    }
    static void DispatchCases(){
        foreach(uint version in new uint[]{6,7,8})foreach(bool available in new[]{false,true})using(var card=DispatchCard(6,version)){
            IList records=Catalog(card,available);GoodCatalog(records);Need(N(records[1],"FormatVersion")==version,"Filesystem and payload format versions conflated");MetadataOnly(card);
        }
        foreach(int kind in new[]{0,1,2,3})foreach(bool available in new[]{false,true})using(var card=DispatchCard()){
            if(kind==0){card.Sectors.Remove(128);card.Sectors.Remove(129);}else if(kind==1){card.Sectors[129][400]^=1;Seal(card.Sectors[129]);}
            else if(kind==2){card.Pair(128,Record(0,99,2,Epoch-1));}else card.IoFaults.UnionWith(new long[]{128,129});
            IList records=Catalog(card,available);Need(records.Count==2&&!Downloadable(records[0])&&Downloadable(records[1]),"Missing/conflicting first directory entry hid intact record: "+kind);
            Need(N(records[0],"Start")==0&&N(records[1],"Start")==32768,"Missing record supplied guessed coordinates");MetadataOnly(card);Allocation(Mount(card),2,65536);
        }
        foreach(int kind in new[]{0,1,2,3,4,5,6,7})using(var card=DispatchCard()){
            byte[] h=card.Sectors[32771],f=card.Sectors[32772];
            if(kind==0)h[508]^=1;else if(kind==1)f[508]^=1;
            else if(kind==2){P(h,12,999);P(f,12,999);Seal(h);Seal(f);}else if(kind==3){P(f,12,999);Seal(f);}
            else if(kind==4){P(f,68,136);Seal(f);}else if(kind==5){Magic(h,0,"N2SDLOG1");Magic(f,0,"N2SDEND1");Seal(h);Seal(f);}
            else if(kind==6){P(h,36,129);P(f,36,129);Seal(h);Seal(f);}else{P(f,60,0);Seal(f);}
            IList records=Catalog(card);Need(records.Count==2&&Downloadable(records[0])&&!Downloadable(records[1]),"Bad/cross-format payload metadata accepted: "+kind);
            Need(N(records[1],"Start")==32768&&N(records[1],"CatalogEnd")==65536,"Unavailable payload lost proven grant");MetadataOnly(card);
        }
        foreach(bool shortRead in new[]{false,true})using(var card=DispatchCard()){
            if(shortRead)card.ShortSectors.Add(32771);else card.IoFaults.Add(32771);IList records=Catalog(card);
            Need(Downloadable(records[0])&&!Downloadable(records[1]),"Metadata I/O/short read hid independent records");MetadataOnly(card);
        }
        foreach(bool io in new[]{false,true})using(var card=DispatchCard()){
            if(io)card.IoFaults.Add(16);else card.Sectors[16][508]^=1;GoodCatalog(Catalog(card));MetadataOnly(card);
        }
        using(var card=DispatchCard()){card.Sectors[24]=Identity(Epoch+1);Need(Failure(()=>Catalog(card)) is InvalidDataException,"Dispatch silently chose conflicting valid filesystem identities");Need(card.Reads.All(lba=>lba<112),"Identity conflict read checkpoint/payload");}
        using(var card=DispatchCard()){card.Sectors[112][508]^=1;card.Sectors[113][508]^=1;Need(Failure(()=>Catalog(card)) is InvalidDataException,"Dispatch inferred allocation without checkpoint");Need(card.Reads.All(lba=>lba<128),"Bad allocation checkpoint scanned directory/payload");}
        using(var card=DispatchCard()){card.Pair(112,Checkpoint(1,0,8192));Need(Catalog(card).Count==0&&card.Reads.All(lba=>lba<128),"Empty filesystem2 scanned stale record/header sectors");}
        foreach(uint version in new uint[]{6,7,8})foreach(int kind in new[]{0,1,2,3})using(var card=DispatchCard(6,version)){
            byte[] cp=Checkpoint(6,2,73728);P(cp,40,32768);P(cp,48,kind==1?999U:202U);P(cp,52,kind==2?0U:1U);Seal(cp);card.Pair(112,cp);
            card.Pair(130,RecordAt(1,32768,65536,32768,202,kind==3?2U:1U));Payload(card,32768,202,version,0,1);
            IList records=Catalog(card);object tail=records[1];long bound=kind==0?73728:65536;
            Need(Downloadable(tail)&&N(tail,"End")==((ulong)bound)&&N(tail,"CatalogEnd")==((ulong)bound),"Interrupted grant extension was missed or admitted without exact last OPEN binding: "+kind);
            Need(N(tail,"Chunks")==0&&N(tail,"State")==1&&N(tail,"Fault")==0&&N(tail,"CommittedEnd")==49152,"Incomplete footer was fabricated/finalized");
            Need(((string)Get(tail,"RecoveryNotice")).Contains(kind==0?"matching checkpoint grant":"unused tail"),"Interrupted grant notice missing");
            Need(N(Entries(Read(card))[1],"ReservedEnd")==65536,"Read-only expanded bound mutated raw filesystem grant");MetadataOnly(card);
        }
        using(var card=DispatchCard()){
            byte[] legacy=new byte[512];Magic(legacy,0,"CE_FILESYSTEM_V1");Magic(legacy,32,"NP_CE64_1");card.Sectors[16]=legacy;card.Sectors[112]=new byte[512];
            byte[] page=new byte[512];P(page,0,2);P(page,4,32768);P(page,8,65536);P(page,256,32768);P(page,260,65536);card.Sectors[128]=page;
            IList records=Catalog(card);Need(records.Count==2&&records.Cast<object>().All(Downloadable),"Recognized legacy primary lost precedence over stale filesystem2 copy24");
            Need(N(records[0],"End")==32768&&N(records[1],"End")==65536&&!card.Reads.Contains(24),"Healthy legacy path changed bounds or probed filesystem2 fallback");MetadataOnly(card);
        }
        foreach(string tag in new[]{"NP_PSSI_6","NP_PSSI_7"})using(var card=new Card()){
            byte[] legacy=new byte[512];Magic(legacy,0,"CE_FILESYSTEM_V1");Magic(legacy,32,tag);card.Sectors[16]=legacy;card.Sectors[112]=new byte[512];card.Sectors[128]=new byte[512];
            Need(Catalog(card).Count==0&&!card.Reads.Contains(24)&&card.Writes==0,"Normal V2 identity was routed through V1 filesystem2");
        }
        using(var card=DispatchCard()){
            card.Writable=true;byte[] identity=card.Sectors[16];
            Need(!(bool)CallOn(catalog,"IsNpIdentity",identity)&&!(bool)CallOn(catalog,"IsCatalogIdentity",identity)&&Convert.ToInt32(CallOn(catalog,"ClassifyIdentity",identity))==0,"Read-only dispatch relaxed legacy writer identity policy");
            Need(Failure(()=>CallOn(format,"QuickFormat",card,card.Length,identity)) is IOException&&card.Writes==0,"Filesystem2 reached legacy QuickFormat writer");
            Need(Failure(()=>CallOn(format,"DeleteLast",card,card.Length,identity,null)) is IOException&&card.Writes==0,"Filesystem2 reached legacy DeleteLast writer");
        }
    }
    static int Main(string[] args){try{
        if(args.Length!=1)throw new ArgumentException("Usage: NpV1StoreV2Tests <actual-source-library.dll-or-WILDX_console.exe>");
        Assembly assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));parser=assembly.GetType("CE32_console.NpV1StoreV2",true);catalog=assembly.GetType("CE32_console.NpDownloadCatalog",true);format=assembly.GetType("CE32_console.NpCardFormat",true);
        MountCases();RecordCases();Cancellation();DispatchCases();
        Console.WriteLine("PASS "+checks+" actual-assembly V1 filesystem2 parser/catalog dispatch checks; sparse read-only metadata, real payload metadata validators, unchanged legacy writer refusal; no payload decode, allocation writes or hardware.");return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
