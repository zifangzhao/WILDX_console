// Actual compiled exporter; sparse read-only card fixtures only. No WinUSB/HW.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

internal static class NpV1PowerCutRecoveryTests
{
    const long Start=8192;
    const uint Session=1792272185;
    static Type exporter;static string root;static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static uint Crc(byte[] b,int at,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[at+i];for(int bit=0;bit<8;bit++)c=(c>>1)^(0xedb88320U&(0U-(c&1)));}return ~c;}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static void Magic(byte[] b,int at,string value){System.Text.Encoding.ASCII.GetBytes(value).CopyTo(b,at);}
    static void Seal(byte[] b,int at){Put(b,at+508,Crc(b,at,508));}
    static void Word(byte[] b,int at,int pin,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<pin);}

    internal sealed class SparseCard:Stream
    {
        internal readonly byte[] Header=new byte[512],Footer;
        internal readonly Dictionary<int,byte[]> Chunks=new Dictionary<int,byte[]>();
        internal readonly long DataStart,ReservedEnd;
        internal long Size,DataBytes,MaxDataChunk=-1;
        internal int MaxReadBytes=int.MaxValue;
        readonly List<Tuple<long,long>> reads=new List<Tuple<long,long>>();
        long position;
        internal SparseCard(int valid,int limit=8,uint reservation=8192,uint mask=3,uint hz=15000){
            DataStart=(Start+reservation)*512;ReservedEnd=Start+reservation+limit*128L;Size=ReservedEnd*512;
            Magic(Header,0,"NPSDLOG1");Put(Header,8,6);Put(Header,12,Session);Put(Header,16,mask);Put(Header,20,1);
            Put(Header,24,hz);Put(Header,28,hz/12);Put(Header,32,15360);Put(Header,36,128);Put(Header,40,(uint)Start);
            Put(Header,44,(uint)ReservedEnd);Put(Header,48,reservation);Put(Header,80,2);Put(Header,96,61632);Seal(Header,0);
            Footer=(byte[])Header.Clone();Magic(Footer,0,"NPSDEND1");Put(Footer,60,1);Put(Footer,68,0);Seal(Footer,0);
            for(int i=0;i<valid;i++)Chunks.Add(i,MakeChunk(i));
        }
        byte[] MakeChunk(int chunk){
            var b=new byte[65536];var chronological=new byte[61440];
            uint mask=BitConverter.ToUInt32(Header,16);int count=Count(mask),selected=0,index=chunk%count;
            for(int p=0;p<3;p++)if((mask&(1U<<p))!=0 && index--==0){selected=p+1;break;}
            for(int f=0;f<1024;f++){
                uint counter=(uint)((chunk/count)*1024+f);int at=f*60;
                Word(chronological,at,0,0,counter%13==0?0x330U:0xcfU);
                Word(chronological,at,7,2,0x11);Word(chronological,at,7,3,counter>>10);Word(chronological,at,7,4,counter&1023);
            }
            for(int i=0;i<15360;i++)for(int lane=0;lane<4;lane++)b[lane*15360+i]=chronological[i*4+lane];
            const int sensor=61440;Magic(b,sensor,"NPSENS01");Put(b,sensor+8,(uint)chunk);Put(b,sensor+12,(uint)(chunk*10+10));
            Put(b,sensor+16,140);Put(b,sensor+20,0x31534e53);Put(b,sensor+24,1);Put(b,sensor+36,(uint)(chunk*10+10));Put(b,sensor+160,Crc(b,sensor,160));
            Buffer.BlockCopy(Header,0,b,61632,512);Magic(b,61632,"NPSDBLK1");Put(b,61632+52,(uint)chunk);
            Put(b,61632+56,(uint)(DataStart/512+chunk*128L));Put(b,61632+88,(uint)selected);Put(b,61632+92,(uint)(chunk/count));Put(b,61632+100,0x11);Seal(b,61632);
            for(int row=0;row<16;row++){int at=65024+row*32;var seq=BitConverter.GetBytes((ushort)(chunk*16+row));b[at+22]=seq[0];b[at+23]=seq[1];Put(b,at+28,(uint)chunk);}
            return b;
        }
        internal void Descriptor(int chunk,int at,uint value){Put(Chunks[chunk],61632+at,value);Seal(Chunks[chunk],61632);}
        internal void BadSync(int chunk,int frame){int offset=frame*60+5;Chunks[chunk][offset%4*15360+offset/4]^=1;}
        internal void FinalizeFooter(uint chunks){Put(Footer,60,3);Put(Footer,68,chunks);Seal(Footer,0);}
        internal void IncompleteFooter(uint chunks){Put(Footer,60,4);Put(Footer,64,2);Put(Footer,68,chunks);Seal(Footer,0);}
        static void Copy(byte[] source,long sourceStart,byte[] dest,int offset,long at,int count){
            long first=Math.Max(at,sourceStart),last=Math.Min(at+count,sourceStart+source.Length);
            if(last>first)Buffer.BlockCopy(source,(int)(first-sourceStart),dest,offset+(int)(first-at),(int)(last-first));
        }
        public override int Read(byte[] b,int offset,int count){
            if(position<0 || position>Size)throw new IOException("Outside sparse source");
            int n=(int)Math.Min(Math.Min(count,Size-position),MaxReadBytes);if(n==0)return 0;
            Array.Clear(b,offset,n);Copy(Header,(Start+3)*512,b,offset,position,n);Copy(Footer,(Start+4)*512,b,offset,position,n);
            foreach(var chunk in Chunks)Copy(chunk.Value,DataStart+chunk.Key*65536L,b,offset,position,n);
            if(position+n>DataStart){
                long first=Math.Max(position,DataStart),last=position+n;
                foreach(var previous in reads)if(first<previous.Item2 && previous.Item1<last)throw new Exception("Data reread: recovery must be one-pass");
                reads.Add(Tuple.Create(first,last));DataBytes+=last-first;MaxDataChunk=Math.Max(MaxDataChunk,(last-DataStart-1)/65536);
            }
            position+=n;return n;
        }
        public override long Length{get{return Size;}}
        public override long Position{get{return position;}set{if(value<0)throw new ArgumentOutOfRangeException();position=value;}}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}
        public override long Seek(long value,SeekOrigin origin){Position=(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:Size)+value;return position;}
        public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException("Read-only card");}
        public override void Write(byte[] b,int at,int count){throw new Exception("Source modification attempted");}
    }

    static string Output(string name){return Path.Combine(root,name);}
    static int Count(uint mask){return (int)((mask&1)+((mask>>1)&1)+((mask>>2)&1));}
    static void Export(SparseCard card,string output,bool recovery=true,long? end=null,Func<bool> cancel=null,Action<int> progress=null){
        MethodInfo method=exporter.GetMethod(recovery?"ExportPowerInterruptedPrefix":"Export",BindingFlags.Static|BindingFlags.NonPublic);
        Check(method!=null,"Actual production export entrypoint missing");
        try{method.Invoke(null,new object[]{card,Start,end??card.ReservedEnd,output,cancel,progress});}
        catch(TargetInvocationException ex){throw ex.InnerException;}
    }
    static void Throws<T>(Action action,string reason)where T:Exception{try{action();}catch(T){Check(true,reason);return;}throw new Exception("Expected "+typeof(T).Name+": "+reason);}
    static Dictionary<string,string> Status(string folder){return File.ReadAllLines(Path.Combine(folder,"capture_status.txt")).Where(line=>line.Contains("=")).Select(line=>line.Split(new[]{'='},2)).ToDictionary(parts=>parts[0],parts=>parts[1]);}
    static void Prefix(SparseCard card,string folder,int chunks){
        var status=Status(folder);
        Check(status["complete"]=="False" && status["recovered_prefix_chunks"]==chunks.ToString(),"Recovery is incomplete with exact derived count");
        Check(status["committed_chunks"]=="0" && status["firmware_committed"]=="False" && status["lossless"]=="False","No fabricated footer/commit/lossless claim");
        Check(File.ReadAllBytes(Path.Combine(folder,"descriptor.bin")).SequenceEqual(card.Header) && File.ReadAllBytes(Path.Combine(folder,"footer.bin")).SequenceEqual(card.Footer),"Original metadata preserved byte-for-byte");
        Check(!File.Exists(Path.Combine(folder,"ephys.bin")) && new FileInfo(Path.Combine(folder,"analogin.dat")).Length==chunks*512L,"No neural raw copy; analog ends at verified whole chunk");
        uint mask=BitConverter.ToUInt32(card.Header,16);
        for(int p=1;p<=3;p++){
            Check(!File.Exists(Path.Combine(folder,"np"+p+".lane8")),"Neural lane8 copy created");
            if((mask&(1U<<(p-1)))==0){Check(!File.Exists(Path.Combine(folder,"np"+p+"_ap.dat")),"Unselected probe output created");continue;}
            Check(new FileInfo(Path.Combine(folder,"np"+p+"_ap.dat")).Length==long.Parse(status["np"+p+"_ap_rows"])*768,"No partially emitted AP chunk");
        }
    }
    static void Boundary(string name,Action<SparseCard> damage){
        var card=new SparseCard(6);damage(card);string folder=Output(name);Export(card,folder);Prefix(card,folder,2);
        Check(card.MaxDataChunk==2 && card.DataBytes==3*65536L,"Stop at first boundary; never scan later chunks/extents");
    }
    static int Main(string[] args){
        try{
            if(args.Length!=2 && !(args.Length==3 && (args[2]=="--catalog-chain-only" || args[2]=="--all-np-modes-only")))throw new ArgumentException("checked_WILDX_EXE fresh_test_output [--catalog-chain-only|--all-np-modes-only]");
            root=Path.GetFullPath(args[1]);if(Directory.Exists(root) || File.Exists(root))throw new IOException("Fresh test output required");Directory.CreateDirectory(root);
            var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));exporter=assembly.GetType("CE32_console.NpCe64Export",true);
            if(args.Length==3 && args[2]=="--all-np-modes-only"){
                int modes=0;
                for(uint mask=1;mask<=7;mask++)foreach(uint hz in new uint[]{7500,15000,20000,30000}){
                    if(mask==7 && (hz==20000 || hz==30000))continue;
                    var c=new SparseCard(8,16,8192,mask,hz);string path=Output("mask"+mask+"_hz"+hz);Export(c,path);Prefix(c,path,8);
                    Check(c.DataBytes==9*65536L,"Supported mode must stop at first absent chunk in one pass");
                    var damaged=new SparseCard(8,16,8192,mask,hz);int at=Count(mask);damaged.Descriptor(at,12,Session+1);path=Output("bad_mask"+mask+"_hz"+hz);Export(damaged,path);Prefix(damaged,path,at);
                    Check(damaged.DataBytes==(at+1)*65536L,"Wrong-session boundary must not expose later chunks");modes++;
                }
                Check(modes==26,"All supported v6 topology/rate combinations covered");
                Console.WriteLine("PASS "+checks+" actual-exporter recovery checks across "+modes+" V1 neural topology/rate combinations; sparse source only, not hardware qualification");return 0;
            }
            if(args.Length==3){
                var read=assembly.GetType("CE32_console.NpCatalogRecovery",true).GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic);
                Func<SparseCard,object[]> recover=c=>((System.Collections.IEnumerable)read.Invoke(null,new object[]{c,null,null,false})).Cast<object>().Where(r=>(bool)r.GetType().GetProperty("Downloadable",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r,null)).ToArray();
                Func<object,string,object> field=(r,n)=>r.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r);
                var interrupted=new SparseCard(2);var entries=recover(interrupted);
                Check(entries.Length==1 && (uint)field(entries[0],"Session")==Session && (uint)field(entries[0],"Chunks")==0,"Missing catalog reconstructed original zero-footer recording");
                Check(interrupted.DataBytes==0,"Listing recovery scanned payload");
                string recovered=Output("catalog_to_prefix");Export(interrupted,recovered,true,(long)field(entries[0],"End"));Prefix(interrupted,recovered,2);
                Check(interrupted.DataBytes==3*65536L,"Catalog-to-export data must be single-pass");
                var finalized=new SparseCard(8);finalized.FinalizeFooter(8);entries=recover(finalized);
                Check(entries.Length==1 && (uint)field(entries[0],"Chunks")==8 && finalized.DataBytes==0,"Completed recording reconstruction changed footer count or scanned payload");
                string normal=Output("catalog_to_normal");Export(finalized,normal,false,(long)field(entries[0],"End"));
                Check(Status(normal)["complete"]=="True" && finalized.DataBytes==8*65536L,"Reconstructed completed recording did not pass normal strict export");
                var tornCatalogFooter=new SparseCard(2);tornCatalogFooter.Footer[508]^=1;entries=recover(tornCatalogFooter);
                Check(entries.Length==0 && tornCatalogFooter.DataBytes==0,"Torn footer became downloadable or triggered payload scan");
                Console.WriteLine("PASS "+checks+" catalog-to-strict-export software integration checks; missing catalog, interrupted/complete recording and torn footer; no hardware");return 0;
            }
            var card=new SparseCard(2);string folder=Output("initial_footer");Export(card,folder);Prefix(card,folder,2);
            Check(card.DataBytes==3*65536L,"Valid data plus first absent descriptor read exactly once");
            Boundary("torn_descriptor",c=>c.Chunks[2][61632+508]^=1);
            Boundary("wrong_session",c=>c.Descriptor(2,12,Session+1));
            Boundary("sequence_gap",c=>c.Descriptor(2,52,3));
            Boundary("wrong_lba",c=>c.Descriptor(2,56,(uint)(c.DataStart/512+1)));
            Boundary("wrong_probe",c=>c.Descriptor(2,88,2));
            Boundary("wrong_model",c=>c.Descriptor(2,100,0x99));
            Boundary("changed_model",c=>c.Descriptor(2,100,0x22));
            Boundary("bad_payload",c=>c.BadSync(2,100));
            card=new SparseCard(2);card.BadSync(0,1023);folder=Output("bad_seam_tail");Export(card,folder);Prefix(card,folder,0);
            Check(Status(folder)["prefix_boundary"].Contains("partial frame"),"Deferred seam failure trims its source chunk");
            card=new SparseCard(0);folder=Output("zero_prefix");Export(card,folder);Prefix(card,folder,0);Check(card.MaxDataChunk==0,"Zero prefix does not search for later valid chunks");
            card=new SparseCard(8);folder=Output("catalog_bound");Export(card,folder,true,card.DataStart/512+2*128);Prefix(card,folder,2);Check(card.DataBytes==2*65536L,"Catalog extent is a hard upper bound");
            card=new SparseCard(8);card.Size=card.DataStart+2*65536+17;folder=Output("stream_bound");Export(card,folder);Prefix(card,folder,2);Check(card.DataBytes==2*65536L,"Partial source tail is not read past whole-chunk length bound");
            card=new SparseCard(2,8,16384);folder=Output("large_reservation");Export(card,folder);Prefix(card,folder,2);Check(card.DataBytes==3*65536L,"Header reservation chooses data start, no scan through metadata reservation");
            card=new SparseCard(2,2);long oversized=card.ReservedEnd+128;folder=Output("header_bound");var bounded=card;
            Throws<InvalidDataException>(()=>Export(bounded,folder,true,oversized),"Catalog cannot override smaller header reservation extent");Check(card.DataBytes==0,"Inconsistent reservation rejected before payload");
            card=new SparseCard(2);card.Header[500]^=1;folder=Output("torn_header");var torn=card;Throws<InvalidDataException>(()=>Export(torn,folder),"Torn header CRC rejected");Check(card.DataBytes==0,"No scan after torn header");
            card=new SparseCard(2);Put(card.Footer,12,Session+1);Seal(card.Footer,0);folder=Output("wrong_footer_session");var mismatch=card;Throws<InvalidDataException>(()=>Export(mismatch,folder),"Footer session must match");Check(card.DataBytes==0,"No scan after mismatched footer");
            card=new SparseCard(2);Array.Clear(card.Footer,0,512);folder=Output("absent_footer");var absent=card;Throws<InvalidDataException>(()=>Export(absent,folder),"All-zero/torn footer is not a valid initial footer");
            card=new SparseCard(2);Put(card.Footer,60,0);Seal(card.Footer,0);folder=Output("invalid_footer_state");var invalidState=card;Throws<InvalidDataException>(()=>Export(invalidState,folder),"Zero/idle state is not a stored recording footer");Check(card.DataBytes==0,"No scan for invalid footer state");
            card=new SparseCard(2);folder=Output("pre_cancel");var pre=card;Throws<OperationCanceledException>(()=>Export(pre,folder,true,null,()=>true),"Pre-cancelled recovery");Check(card.DataBytes==0 && !Directory.Exists(folder),"Pre-cancel creates no outputs or source reads");
            card=new SparseCard(6);folder=Output("cancel_between_chunks");bool cancelled=false;var cancelCard=card;
            Throws<OperationCanceledException>(()=>Export(cancelCard,folder,true,null,()=>cancelled,p=>cancelled=true),"Cancellation between native chunks");Prefix(card,folder,1);Check(card.DataBytes==65536,"Cancellation does not read next chunk");
            card=new SparseCard(6);card.MaxReadBytes=4096;folder=Output("cancel_partial_read");var partial=card;
            Throws<OperationCanceledException>(()=>Export(partial,folder,true,null,()=>partial.DataBytes>=4096),"Cancellation during short source reads");Prefix(card,folder,0);Check(card.DataBytes==4096,"Partial input chunk never reaches native output");
            card=new SparseCard(2);folder=Output("normal_initial");Export(card,folder,false);Check(card.DataBytes==0 && Status(folder)["complete"]=="False","Normal initial-footer export stays metadata-only");
            card=new SparseCard(8);card.FinalizeFooter(8);folder=Output("normal_complete");Export(card,folder,false);Check(card.DataBytes==8*65536L && Status(folder)["complete"]=="True","Normal finalized export stays complete and one-pass");
            card=new SparseCard(8);card.FinalizeFooter(8);folder=Output("reject_finalized");var final=card;Throws<InvalidDataException>(()=>Export(final,folder),"Explicit recovery is not a finalized-export replacement");Check(card.DataBytes==0,"Finalized record not scanned by recovery entry");
            Console.WriteLine("PASS "+checks+" actual-assembly power-cut prefix checks; sparse/memory source only, no hardware");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
