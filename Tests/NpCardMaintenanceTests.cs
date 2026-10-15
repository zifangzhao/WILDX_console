using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;

static class NpCardMaintenanceTests {
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;static int checks;
    static Type T(string name){return app.GetType("CE32_console."+name,true);}
    static object Call(string name,string method,params object[] args){try{return T(name).GetMethods(All).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
    static void Reject(Action action,string why){bool rejected=false;try{action();}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,why);}
    static uint U(byte[] b,int offset){return BitConverter.ToUInt32(b,offset);}
    static void Put(byte[] b,int offset,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,offset,4);}
    static void Seal(byte[] b){Put(b,508,(uint)Call("NpDownloadStream","Crc",b,0,508));}
    sealed class Card:Stream {
        internal readonly Dictionary<long,byte[]> Sectors=new Dictionary<long,byte[]>();
        internal readonly List<long> Writes=new List<long>();
        internal int FailWrite,FailFlush,CorruptWrite,Flushes;long position,length;
        internal Card(long sectors){length=sectors*512;}
        internal byte[] Sector(long lba){byte[] value;return Sectors.TryGetValue(lba,out value)?(byte[])value.Clone():new byte[512];}
        internal void Seed(long lba,byte[] value){Sectors[lba]=(byte[])value.Clone();}
        public override int Read(byte[] b,int offset,int count){int read=0;while(count>0&&position<length){int at=(int)(position%512),n=(int)Math.Min(Math.Min(count,512-at),length-position);Buffer.BlockCopy(Sector(position/512),at,b,offset,n);position+=n;offset+=n;count-=n;read+=n;}return read;}
        public override void Write(byte[] b,int offset,int count){if(position%512!=0||count!=512)throw new Exception("Non-sector write");Writes.Add(position/512);if(Writes.Count==FailWrite)throw new IOException("Injected write failure");byte[] value=new byte[512];Buffer.BlockCopy(b,offset,value,0,512);if(Writes.Count==CorruptWrite)value[200]^=1;Seed(position/512,value);position+=512;}
        public override void Flush(){if(++Flushes==FailFlush)throw new IOException("Injected flush failure");}
        public override bool CanRead{get{return true;}}public override bool CanWrite{get{return true;}}public override bool CanSeek{get{return true;}}
        public override long Length{get{return length;}}public override long Position{get{return position;}set{position=value;}}
        public override long Seek(long offset,SeekOrigin origin){return Position=offset+(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?Position:Length);}
        public override void SetLength(long value){throw new NotSupportedException();}
    }
    static Card Make(int count,string tag="NP_CE64_1",bool failed=false) {
        uint capacity=(uint)(count*2+8)*8192;var card=new Card(capacity);byte[] id=new byte[512];
        Encoding.ASCII.GetBytes("CE_FILESYSTEM_V1").CopyTo(id,0);Encoding.ASCII.GetBytes(tag).CopyTo(id,32);card.Seed(16,id);
        uint previous=8192;
        for(int i=0;i<count;i++) {
            uint start=(previous+8191)/8192*8192,payload=(start+8192+16383)/16384*16384,end=payload+8*91,reserved=payload+16384;
            byte[] page=card.Sector(128+i/63);Put(page,0,(uint)(i%63+1));Put(page,4+4*(i%63),end);Put(page,256+4*(i%63),end);card.Seed(128+i/63,page);
            byte[] h=new byte[512];Encoding.ASCII.GetBytes("N2SDLOG1").CopyTo(h,0);
            uint[] fields={11,(uint)i+1,20000,32000000,3,63,4,6,46592,61440,91,4,start,payload,payload+((reserved-payload)/364)*364,reserved,capacity,0};
            for(int n=0;n<fields.Length;n++)Put(h,8+n*4,fields[n]);Put(h,96,15);Put(h,100,1);Seal(h);card.Seed(start+3,h);
            var f=(byte[])h.Clone();Encoding.ASCII.GetBytes("N2SDEND1").CopyTo(f,0);Put(f,100,failed&&i==count-1?4U:3U);Put(f,104,failed&&i==count-1?1U:0U);Put(f,108,8);Put(f,112,2);Put(f,116,end);Put(f,120,8*61440);Seal(f);card.Seed(start+4,f);
            card.Seed(payload,Enumerable.Repeat((byte)(i+1),512).ToArray());previous=end;
        }
        card.Seed(17,Enumerable.Repeat((byte)42,512).ToArray());return card;
    }
    static int Count(Card card){return ((IList)Call("NpDownloadCatalog","Read",card)).Count;}
    static Card V1(bool media) {
        var card=Make(2);uint previous=8192;
        for(int i=0;i<2;i++) {
            uint start=(previous+8191)/8192*8192,payload=(start+8192+16383)/16384*16384,end=payload+8*128;
            byte[] page=card.Sector(128);Put(page,4+4*i,end);Put(page,256+4*i,end);card.Seed(128,page);
            byte[] h=new byte[512];Encoding.ASCII.GetBytes("NPSDLOG1").CopyTo(h,0);
            Put(h,8,media?8U:6U);Put(h,12,(uint)i+1);Put(h,16,media?0U:1U);Put(h,20,1);Put(h,24,media?0U:30000U);Put(h,28,media?0U:2500U);Put(h,32,15360);Put(h,36,128);Put(h,40,start);Put(h,44,payload+16384);Put(h,48,payload-start);Put(h,80,2);Put(h,96,61632);Seal(h);card.Seed(start+3,h);
            var f=(byte[])h.Clone();Encoding.ASCII.GetBytes("NPSDEND1").CopyTo(f,0);Put(f,60,3);Put(f,68,8);Seal(f);card.Seed(start+4,f);previous=end;
        }
        return card;
    }
    static object Latest(Card card){return Call("NpCardFormat","Latest",card);}
    static void Delete(Card card,object latest){Call("NpCardFormat","DeleteLast",card,card.Length,card.Sector(16),latest);}
    static void Deletion() {
        foreach(string tag in new[]{"NP_CE64_1","NP_PSSI_6","NP_PSSI_7"})foreach(int count in new[]{1,2,62,63,64,126,127})using(var card=Make(count,tag)) {
            Check(Count(card)==count,"Invalid fixture catalog");var before=card.Sectors.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());var latest=Latest(card);Delete(card,latest);
            Check(Count(card)==count-1,"Wrong remaining record count");long page=128+(count-1)/63;
            Check(card.Writes.SequenceEqual(new long[]{113,112,page,112}),"Wrong journal publication order");
            foreach(var sector in before.Where(p=>p.Key!=page&&p.Key!=113&&p.Key!=112))Check(card.Sector(sector.Key).SequenceEqual(sector.Value),"Earlier recording/settings/payload changed");
            Check(U(card.Sector(page),0)==(count-1)%63&&U(card.Sector(page),4+4*((count-1)%63))==0&&U(card.Sector(page),256+4*((count-1)%63))==0,"Tail mirror/count not removed");
            Check(card.Sector(112).All(b=>b==0),"Journal not cleared");
            if(count>1){Delete(card,Latest(card));Check(Count(card)==count-2,"Repeated deletion failed across page boundary");}
        }
        using(var card=Make(2,failed:true)){var rows=(IList)Call("NpDownloadCatalog","Read",card);Check(!(bool)rows[1].GetType().GetProperty("Downloadable",All).GetValue(rows[1],null),"Failed fixture unexpectedly downloadable");Delete(card,Latest(card));Check(Count(card)==1,"Cannot remove failed tail");}
        foreach(bool media in new[]{false,true})using(var card=V1(media)){Check(Count(card)==2,"V1 catalog fixture invalid");Delete(card,Latest(card));Check(Count(card)==1,"V1 neural/media tail not removed");}
        foreach(string stale in new[]{"directory","descriptor","footer","journal","identity","capacity","null"})using(var card=Make(2)) {
            object latest=Latest(card);byte[] identity=card.Sector(16);long bytes=card.Length;
            if(stale=="capacity")bytes-=512;
            else if(stale=="null")latest=null;
            else {long lba=stale=="directory"?128:stale=="descriptor"?24579:stale=="footer"?24580:stale=="journal"?112:16;byte[] b=card.Sector(lba);b[200]^=1;if(stale=="descriptor"||stale=="footer")Seal(b);card.Seed(lba,b);}
            object expected=latest;Reject(()=>Call("NpCardFormat","DeleteLast",card,bytes,identity,expected),"Unsafe/stale delete accepted: "+stale);Check(card.Writes.Count==0,"Refused delete wrote to card");
        }
        using(var card=Make(0)){Reject(()=>Latest(card),"Empty card accepted");Check(card.Writes.Count==0,"Empty card changed");}
        using(var card=Make(1,"UNKNOWN00")){Reject(()=>Latest(card),"Unknown card accepted");Check(card.Writes.Count==0,"Unknown card changed");}
    }
    static void Interruptions() {
        foreach(string fault in new[]{"write","flush","readback"})for(int phase=1;phase<=4;phase++)using(var card=Make(64)) {
            object latest=Latest(card);var before=card.Sectors.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
            if(fault=="write")card.FailWrite=phase;else if(fault=="flush")card.FailFlush=phase;else card.CorruptWrite=phase;
            Reject(()=>Delete(card,latest),"Injected failure ignored");Check(card.Writes.Count==phase,"Failed mutation retried/continued");
            foreach(var sector in before.Where(p=>p.Key!=129&&p.Key!=112&&p.Key!=113))Check(card.Sector(sector.Key).SequenceEqual(sector.Value),"Failure changed payload/older recording");
            byte[] journal=card.Sector(112);
            if(journal.Any(b=>b!=0))Reject(()=>Count(card),"Pending/torn journal silently accepted");
            else Check(Count(card)==(phase==4&&fault=="flush"?63:64),"Failed write unexpectedly changed count");
        }
    }
    static IEnumerable<Control> Descendants(Control owner){foreach(Control child in owner.Controls){yield return child;foreach(var next in Descendants(child))yield return next;}}
    static void Ui() {
        var type=T("NpDownloadForm");using(var form=(Form)Activator.CreateInstance(type,All,null,new object[]{null,false},null)) {
            var controls=Descendants(form).ToArray();Check(controls.Any(c=>c.Name=="quickFormatNpSd"),"Format missing from Data Download");Check(controls.Any(c=>c.Name=="deleteLastNpRecording"),"Delete missing from Data Download");
            var sources=(ComboBox)type.GetField("sources",All).GetValue(form);var sourceType=type.GetNestedType("Source",All);
            foreach(string kind in new[]{"usb","fixed","card"}) {
                object source=Activator.CreateInstance(sourceType,true);sourceType.GetField("Label",All).SetValue(source,kind);sourceType.GetField("Path",All).SetValue(source,kind);sourceType.GetField("Usb",All).SetValue(source,kind=="usb");sourceType.GetField("MediaType",All).SetValue(source,kind=="card"?"Removable Media":"Fixed hard disk media");sources.Items.Add(source);sources.SelectedIndex=sources.Items.Count-1;
                foreach(string name in new[]{"quickFormatNpSd","deleteLastNpRecording"})Check(controls.Single(c=>c.Name==name).Enabled==(kind=="card"),"Wrong maintenance eligibility: "+kind);
            }
            type.GetField("maintenanceActive",All).SetValue(form,true);type.GetMethod("SetBusy",All).Invoke(form,new object[]{true});type.GetMethod("SetUsbBlocked",All).Invoke(form,new object[]{true});
            Check(!((Button)type.GetField("cancel",All).GetValue(form)).Enabled,"Maintenance became cancellable during USB state refresh");
            Check(!controls.Single(c=>c.Name=="deleteLastNpRecording").Enabled&&!controls.Single(c=>c.Name=="quickFormatNpSd").Enabled,"Maintenance controls enabled while busy");
        }
        using(var form=(Form)Activator.CreateInstance(app.GetType("USB512_interface.Form1",true))) {
            var tabs=Descendants(form).OfType<TabControl>().Single(c=>c.TabPages.Cast<TabPage>().Any(p=>p.Name=="downloadPage"));
            var download=tabs.TabPages.Cast<TabPage>().Single(p=>p.Name=="downloadPage");Check(Descendants(download).Any(c=>c.Name=="quickFormatNpSd"),"Advanced stole Format from Data Download");
        }
    }
    [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(args[0]);Deletion();Interruptions();Ui();Console.WriteLine("PASS "+checks+" actual-app card maintenance checks; synthetic cards only, no enumeration or hardware writes.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
