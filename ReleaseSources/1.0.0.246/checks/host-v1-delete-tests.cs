using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;

static class NpV1DeleteLastTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    const string Uid="001D00065942500B20313354";
    static Assembly app;static Type store,usb,ble,status,wire,connection;static int checks;
    static object Call(object target,string name,params object[] args){try{return (target as Type??target.GetType()).GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(target is Type?null:target,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object Field(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static void P(byte[] b,int at,uint value){BitConverter.GetBytes(value).CopyTo(b,at);}
    static uint Crc(byte[] b,int length){uint c=uint.MaxValue;for(int i=0;i<length;i++){c^=b[i];for(int n=0;n<8;n++)c=(c>>1)^((c&1)!=0?0xedb88320U:0);}return ~c;}
    static void Need(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    static void Reject(Action action,string why){try{action();}catch(Exception ex) when(ex is IOException || ex is InvalidDataException){checks++;return;}throw new Exception("Accepted "+why);}
    sealed class Card:Stream{
        internal uint Capacity;internal Dictionary<uint,byte[]> Sectors=new Dictionary<uint,byte[]>();internal int Writes;long position;
        internal Card Clone(){return new Card{Capacity=Capacity,Sectors=Sectors.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone())};}
        public override int Read(byte[] b,int at,int count){uint lba=(uint)(position/512);if(lba>=8192)throw new Exception("Read-only recovery scanned recording payload");byte[] raw;int n=Math.Min(count,512-(int)(position%512));if(Sectors.TryGetValue(lba,out raw))Buffer.BlockCopy(raw,(int)(position%512),b,at,n);else Array.Clear(b,at,n);position+=n;return n;}
        public override void Write(byte[] b,int at,int count){Writes++;throw new Exception("PC recovery wrote to the card");}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}
        public override long Length{get{return (long)Capacity*512;}}public override long Position{get{return position;}set{position=value;}}
        public override long Seek(long at,SeekOrigin origin){return Position=(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:Length)+at;}
        public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException();}
    }
    static void MountVectors(string path){
        var images=new List<Card>();
        using(var r=new BinaryReader(File.OpenRead(path))){Need(new string(r.ReadChars(8))=="NPS2CUT1"&&r.ReadUInt32()==1,"Native vector version");uint capacity=r.ReadUInt32(),count=r.ReadUInt32(),sectors=r.ReadUInt32();Need(count==7&&sectors==12,"Native vector dimensions");for(uint n=0;n<count;n++){Need(r.ReadUInt32()==n,"Native boundary order");var card=new Card{Capacity=capacity};for(int i=0;i<sectors;i++){uint lba=r.ReadUInt32();card.Sectors.Add(lba,r.ReadBytes(512));}images.Add(card);}Need(r.BaseStream.Position==r.BaseStream.Length,"Trailing native vector bytes");}
        uint oldCount=U(images[0].Sectors[112],36),oldFrontier=U(images[0].Sectors[112],32),newFrontier=U(images[0].Sectors[112],40);
        for(int n=0;n<images.Count;n++){
            var c=images[n];var parsed=Call(store,"Read",c,null);
            Need((uint)Field(parsed,"Count")==oldCount-(n==0?0U:1U),"Wrong PC count at boundary "+n);
            Need((uint)Field(parsed,"Frontier")== (n==0?oldFrontier:newFrontier),"Wrong PC frontier at boundary "+n);
            Need((bool)Field(parsed,"DeleteRecoveryPending")== (n>0&&n<6),"Wrong pending label at boundary "+n);
            Need(c.Writes==0,"PC decoder wrote metadata");
            foreach(uint sector in new uint[]{128,129,130,131})Need(c.Sectors[sector].SequenceEqual(images[0].Sectors[sector]),"Earlier descriptor changed");
        }
        uint[] writes={114,115,112,113,114,115};
        for(int n=1;n<images.Count;n++)for(int prefix=0;prefix<=512;prefix++)using(var c=images[n-1].Clone()){
            uint sector=writes[n-1];Buffer.BlockCopy(images[n].Sectors[sector],0,c.Sectors[sector],0,prefix);
            Func<byte[],bool> intent=b=>System.Text.Encoding.ASCII.GetString(b,0,8)=="NPS2DL01"&&U(b,508)==Crc(b,508);
            bool pending=intent(c.Sectors[114])||intent(c.Sectors[115]);
            var parsed=Call(store,"Read",c,null);uint expected=n==1&&!pending?oldCount:oldCount-1;
            Need((uint)Field(parsed,"Count")==expected && (uint)Field(parsed,"Frontier")== (expected==oldCount?oldFrontier:newFrontier),"Unsafe PC view after torn write "+n+"/"+prefix);
            Need((bool)Field(parsed,"DeleteRecoveryPending")==pending&&c.Writes==0,"Torn journal view lost its read-only/pending state");
        }
        using(var c=images[3].Clone()){P(c.Sectors[113],24,U(c.Sectors[113],24)+10);P(c.Sectors[113],508,Crc(c.Sectors[113],508));Reject(()=>Call(store,"Read",c,null),"later appended checkpoint");Need(c.Writes==0,"Stale intent changed metadata");}
        foreach(int at in new[]{16,20,120+8,120+12,152+12,184+16,216,224,232,236})using(var c=images[2].Clone()){
            c.Sectors[114][at]^=1;P(c.Sectors[114],508,Crc(c.Sectors[114],508));c.Sectors[115]=(byte[])c.Sectors[114].Clone();Reject(()=>Call(store,"Read",c,null),"semantic-invalid resealed intent "+at);Need(c.Writes==0,"Bad journal wrote metadata");
        }
        using(var c=images[2].Clone()){c.Sectors[114][32]^=1;P(c.Sectors[114],508,Crc(c.Sectors[114],508));Reject(()=>Call(store,"Read",c,null),"conflicting valid intents");}
        using(var c=images[2].Clone()){c.Sectors[114][508]^=1;Need((bool)Field(Call(store,"Read",c,null),"DeleteRecoveryPending"),"One surviving intent did not project");}
    }
    static byte[] Service(){var b=new byte[32];P(b,0,0x3153504e);P(b,4,258);P(b,8,3);P(b,12,0x4000f);P(b,24,0x001d0006);P(b,28,0x5942500b^0x20313354);return b;}
    static void StaleGrant(string path){
        using(var r=new BinaryReader(File.OpenRead(path)))using(var c=new Card()){
            Need(new string(r.ReadChars(8))=="NPS2CUT1"&&r.ReadUInt32()==1,"Stale-grant vector header");c.Capacity=r.ReadUInt32();Need(r.ReadUInt32()==1&&r.ReadUInt32()==12&&r.ReadUInt32()==0,"Stale-grant dimensions");
            for(int n=0;n<12;n++){uint lba=r.ReadUInt32();c.Sectors.Add(lba,r.ReadBytes(512));}
            Need(U(c.Sectors[132],40)<Math.Max(U(c.Sectors[114],64),U(c.Sectors[114],96)),"Native vector lacks stale descriptor grant");
            var parsed=Call(store,"Read",c,null);
            Need((uint)Field(parsed,"Count")==2&&(uint)Field(parsed,"Frontier")==24576&&(ulong)Field(parsed,"Generation")==8&&(bool)Field(parsed,"DeleteRecoveryPending"),"Authoritative checkpoint grant was replaced by stale descriptor bounds");
            Need(c.Writes==0,"Stale-grant view wrote metadata");
        }
    }
    static byte[] Reply(uint op,uint phase,uint nonce,uint flags=3){var b=new byte[128];P(b,0,0x3155444e);P(b,4,1);P(b,8,op);P(b,12,phase);P(b,16,nonce);P(b,24,1);P(b,28,flags);P(b,32,262144);P(b,36,3);P(b,40,40960);P(b,44,2);P(b,48,24576);P(b,52,40960);for(int i=0;i<4;i++)P(b,64+4*i,(uint)(100+i));P(b,80,0x001d0006);P(b,84,0x5942500b);P(b,88,0x20313354);P(b,92,120000000);P(b,124,Crc(b,124));return b;}
    sealed class StatusFactory{
        internal object Value;
        public Task<T> Get<T>(){return Task.FromResult((T)Value);}
        internal Delegate Callback(){return Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(typeof(Task<>).MakeGenericType(status)),this,GetType().GetMethod("Get").MakeGenericMethod(status));}
    }
    static object Status(uint flags=0x14000011){var s=Activator.CreateInstance(status,true);status.GetField("State",All).SetValue(s,0U);status.GetField("Capacity",All).SetValue(s,262144U);status.GetField("Flags",All).SetValue(s,flags);return s;}
    static void BleCases(){
        Need((bool)Call(usb,"Supported",Service(),Uid),"V1 A7 capability rejected");
        foreach(uint flags in new uint[]{3,7,11}){var r=Call(usb,"Parse",Reply(1,3,1,flags),Uid);Need((bool)r.GetType().GetProperty("CanDelete",All).GetValue(r,null),"Stopped V1 complete/incomplete/failed rejected");}
        Need((bool)Call(ble,"Supported",Status()),"V1 transactional BLE unavailable");Need(!(bool)Call(ble,"Supported",Status(17)),"Legacy BLE offered new command");
        var factory=new StatusFactory{Value=Status()};byte[] current=Reply(0,0,0);int inspect=0,commits=0;bool lost=false;
        Func<byte[],Task<byte[]>> exchange=q=>{
            if(q.Length==8&&U(q,0)==1){inspect++;current=Reply(1,3,U(q,4),7);}
            else if(q.Length==128){commits++;Need(U(q,8)==2&&U(q,16)==2&&U(q,96)==1&&U(q,124)==Crc(q,124),"BLE confirmation wire differs from USB");if(lost)throw new IOException("lost acknowledgement");current=Reply(2,3,2,1);P(current,36,2);P(current,40,24576);P(current,96,1);P(current,124,Crc(current,124));}
            return Task.FromResult((byte[])current.Clone());
        };
        Func<object> manager=()=>Activator.CreateInstance(ble,All,null,new object[]{Uid,exchange,factory.Callback(),(Action)(()=>{})},null);
        object client=manager();Task t=(Task)Call(client,"Inspect");t.GetAwaiter().GetResult();object confirmed=t.GetType().GetProperty("Result").GetValue(t,null);
        ((Task)Call(client,"Delete",confirmed)).GetAwaiter().GetResult();Need(inspect==1&&commits==1,"BLE transaction replayed");
        current=Reply(0,0,0);inspect=commits=0;lost=true;client=manager();t=(Task)Call(client,"Inspect");t.GetAwaiter().GetResult();confirmed=t.GetType().GetProperty("Result").GetValue(t,null);Reject(()=>((Task)Call(client,"Delete",confirmed)).GetAwaiter().GetResult(),"lost BLE acknowledgement");Need(commits==1,"BLE lost ACK replayed mutation");
        factory.Value=Status(17);inspect=commits=0;client=manager();Reject(()=>((Task)Call(client,"Inspect")).GetAwaiter().GetResult(),"legacy BLE admission");Need(inspect==0&&commits==0,"Unsupported BLE firmware received command");
    }
    static void FrameCases(){
        var q=new byte[8];P(q,0,1);P(q,4,0x3e3cad);var response=Reply(1,3,0x3e3cad);P(response,64,0x3e3cdaad);P(response,124,Crc(response,124));
        var tx=(byte[])Call(wire,"Frame",(byte)0xa7,q);Need(tx.Length==11&&tx[1]==0xa7&&tx[10]==0x3e,"A7 inspection framing");
        var confirmed=Call(usb,"Parse",response,Uid);var commit=(byte[])Call(usb,"DeleteRequest",confirmed);tx=(byte[])Call(wire,"Frame",(byte)0xa7,commit);Need(tx.Length==131&&tx[130]==0x3e,"A7 confirmation framing");
        Need((bool)Call(connection,"Matches",(byte)0xa7,q,(byte)0xa7,response),"Actual BLE receipt matcher rejects A7");
        Need(!(bool)Call(connection,"Matches",(byte)0xa7,q,(byte)0xb9,response),"Cross-command receipt accepted");
        var stale=(byte[])response.Clone();P(stale,16,88);P(stale,124,Crc(stale,124));Need(!(bool)Call(connection,"Matches",(byte)0xa7,q,(byte)0xa7,stale),"Stale inspection nonce accepted");
        var done=Reply(2,3,U(commit,16),1);P(done,96,U(commit,96));P(done,124,Crc(done,124));Need((bool)Call(connection,"Matches",(byte)0xa7,commit,(byte)0xa7,done),"Confirmation receipt not correlated");P(done,96,99);P(done,124,Crc(done,124));Need(!(bool)Call(connection,"Matches",(byte)0xa7,commit,(byte)0xa7,done),"Foreign inspected nonce accepted");
        foreach(bool ad in new[]{false,true}){
            var frame=new byte[ad?132:131];frame[0]=ad?(byte)0xad:(byte)0x3c;frame[1]=0xa7;response.CopyTo(frame,2);if(ad){frame[130]=0xda;frame[131]=0xda;}else frame[130]=0x3e;
            for(int split=1;split<=frame.Length;split++){
                object parser=Activator.CreateInstance(wire,true);var received=new List<object>();
                for(int at=0;at<frame.Length;at+=split){byte[] piece=frame.Skip(at).Take(split).ToArray();foreach(object item in (System.Collections.IEnumerable)Call(parser,"Feed",piece))received.Add(item);}
                Need(received.Count==1,"A7 body delimiters or arbitrary notification fragments lost framing");
                Need(((byte[])received[0].GetType().GetProperty("Item2").GetValue(received[0],null)).SequenceEqual(response),"A7 fragmented payload changed");
            }
        }
        var legacy=new byte[64];tx=(byte[])Call(wire,"Frame",(byte)0xb9,legacy);Need(tx.Length==67&&tx[66]==0x3e,"Legacy B9 framing changed");
    }
    static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));store=app.GetType("CE32_console.NpV1StoreV2",true);usb=app.GetType("USB512_interface.NpUsbDeleteLast",true);ble=app.GetType("USB512_interface.NpBleDeleteLast",true);status=app.GetType("CE32_console.NpSdStatus",true);wire=app.GetType("USB512_interface.NpBleWire",true);connection=app.GetType("USB512_interface.NpBleConnection",true);MountVectors(args[1]);if(args.Length>2)StaleGrant(args[2]);BleCases();FrameCases();Console.WriteLine("PASS "+checks+" actual-assembly V1 delete USB/BLE admission/framing/correlation, one-shot confirmation, native crash-boundary PC projection and unchanged earlier descriptors; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
