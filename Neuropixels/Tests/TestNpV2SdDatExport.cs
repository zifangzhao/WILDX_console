using System;
using System.IO;
using System.Linq;
using System.Reflection;
using USB512_interface;

internal static class NpV2SdDatExportTests
{
    private const uint Start = 8192, Payload = 16384, ReservationEnd = 32768;
    private static int checks;
    private static MethodInfo ExternalBridge;
    private static MethodInfo ExternalCatalog;
    private static MethodInfo ExternalMetadata;
    private sealed class CapacityImage:Stream {
        readonly Stream source;readonly long capacity;
        internal CapacityImage(Stream bytes,long length){source=bytes;capacity=length;}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}
        public override long Length{get{return capacity;}}public override long Position{get{return source.Position;}set{source.Position=value;}}
        public override int Read(byte[] b,int at,int count){return source.Read(b,at,count);}
        public override long Seek(long offset,SeekOrigin origin){Position=offset+(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?Position:Length);return Position;}
        public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException();}public override void Write(byte[] b,int at,int count){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(disposing)source.Dispose();base.Dispose(disposing);}
    }
    private static void Bridge(Stream card, long start, long end, string output, Func<bool> cancel, Action<int> progress)
    {
        if(ExternalBridge==null){CE32_console.NpCe64Export.Export(card,start,end,output,cancel,progress);return;}
        try{ExternalBridge.Invoke(null,new object[]{card,start,end,output,cancel,progress});}
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}
    }
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static void Put(byte[] b, int at, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4); }
    private static void PutQ(byte[] b, int at, ulong value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 8); }
    private static uint Crc(byte[] b, int count) { uint c = uint.MaxValue; for (int n = 0; n < count; n++) { c ^= b[n]; for (int bit = 0; bit < 8; bit++) c = (c >> 1) ^ ((c & 1) == 0 ? 0 : 0xedb88320U); } return c ^ uint.MaxValue; }
    private static void Seal(byte[] b) { Put(b, 508, Crc(b, 508)); }
    private static readonly MethodInfo Fixture = typeof(NpV2UsbConsoleTests).GetMethod("MakeV3Block", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo Word = typeof(NpV2UsbConsoleTests).GetMethod("V3DataWord", BindingFlags.NonPublic | BindingFlags.Static);
    // A bounded real8-block image relocated by8GiB. Unrepresented prefix reads
    // refuse; there is no huge backing file, fabricated payload, or exporter substitute.
    private sealed class OffsetImage : Stream
    {
        private readonly MemoryStream bytes;
        private readonly long origin;
        internal OffsetImage(byte[] data, long origin) { bytes = new MemoryStream(data, false); this.origin = origin; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return checked(origin + bytes.Length); } }
        public override long Position {
            get { return checked(origin + bytes.Position); }
            set { if (value < origin || value > Length) throw new ArgumentOutOfRangeException(); bytes.Position = value - origin; }
        }
        public override int Read(byte[] buffer, int offset, int count) { return bytes.Read(buffer, offset, count); }
        public override long Seek(long offset, SeekOrigin from) {
            Position = checked(offset + (from == SeekOrigin.Begin ? 0 : from == SeekOrigin.Current ? Position :
                from == SeekOrigin.End ? Length : throw new ArgumentOutOfRangeException())); return Position;
        }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing) bytes.Dispose(); base.Dispose(disposing); }
    }
    private static void Metadata(byte[] d, byte[] f, uint start, long length)
    {
        try { (ExternalMetadata??typeof(NpV2SdDatExport).GetMethod("ValidateMetadata", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .Invoke(null, new object[] { d, f, start, length }); }
        catch (TargetInvocationException e) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException ?? e).Throw(); throw;
        }
    }
    private static void MakeImage(string path, uint hz, string fault,uint au=0,uint mask=3,uint rawD8Phase=uint.MaxValue,bool legacyAu=false)
    {
        const uint blocks = 8;
        uint ticks=mask==1?122880U:mask==3?61440U:40960U;
        uint payload=au==0?Payload:(uint)RoundUp(Payload,au);
        uint payloadEnd=au==0?Payload+((ReservationEnd-Payload)/364)*364:payload+blocks*91;
        uint reservationEnd=au==0?ReservationEnd:(uint)RoundUp(RoundUp(payloadEnd,au),16384);
        byte[] d = new byte[512], f = new byte[512], u = new byte[512];
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("N2SDLOG1"), 0, d, 0, 8);
        uint[] fields = { au==0?11U:legacyAu?13U:12U, 7, hz, hz * 1600, mask, mask==1?7U:mask==3?63U:511U, mask==7?6U:4U, mask==1?3U:mask==3?6U:9U, 46592, ticks, 91, 4, Start, payload,
            payloadEnd, reservationEnd, 501301248, 0 };
        if(au==0)fields[16]=65536; // Retain every original v11 fixture byte.
        for (int n = 0; n < fields.Length; n++) Put(d, 8 + n * 4, fields[n]);
        byte[] configuration=null;
        if(mask==1){
            configuration=new byte[2048];
            System.Text.Encoding.ASCII.GetBytes("N2SDCFG1").CopyTo(configuration,0);
            foreach(var field in new[]{Tuple.Create(8,1U),Tuple.Create(12,2U),Tuple.Create(16,hz),Tuple.Create(20,hz*1600),Tuple.Create(24,1U),Tuple.Create(28,7U),Tuple.Create(32,4U),Tuple.Create(36,3U),Tuple.Create(40,46592U),Tuple.Create(44,blocks)})Put(configuration,field.Item1,field.Item2);
            Buffer.BlockCopy(d,80,configuration,48,16);
            System.Text.Encoding.ASCII.GetBytes("N2IMRO1").CopyTo(configuration,64);configuration[72]=1;
            System.Text.Encoding.ASCII.GetBytes("N2B1").CopyTo(configuration,128);configuration[132]=4;
            for(int n=0;n<32;n++)configuration[136+n]=(byte)(n+1);
            if(fault=="config_planned"){Put(configuration,12,1);configuration[72]=0;Array.Clear(configuration,128,876);}
            if(fault=="config_kind")Put(configuration,12,3);
            if(fault=="config_reserved")configuration[1004]=1;
            if(fault=="config_unselected")configuration[72]=3;
            if(fault=="config_port")configuration[133]=1;
            if(fault=="config_request")configuration[132]=3;
            Put(d,76,Crc(configuration,configuration.Length));
            if(fault=="config_crc")configuration[128]^=1;
        }else if(mask==7){
            configuration=new byte[2048];
            System.Text.Encoding.ASCII.GetBytes("N2SDCFG1").CopyTo(configuration,0);
            foreach(var field in new[]{Tuple.Create(8,1U),Tuple.Create(12,3U),Tuple.Create(16,hz),Tuple.Create(20,hz*1600),Tuple.Create(24,7U),Tuple.Create(28,511U),Tuple.Create(32,6U),Tuple.Create(36,9U),Tuple.Create(40,46592U),Tuple.Create(44,blocks)})Put(configuration,field.Item1,field.Item2);
            Buffer.BlockCopy(d,80,configuration,48,16);
            System.Text.Encoding.ASCII.GetBytes("N2IMRO2").CopyTo(configuration,64);configuration[72]=7;
            for(int p=0;p<3;p++){
                for(int n=0;n<32;n++)configuration[128+p*36+n]=(byte)(p*32+n+1);
                Put(configuration,160+p*36,0x12340000U+(uint)p);
            }
            if(fault=="config_kind")Put(configuration,12,2);
            if(fault=="config_reserved")configuration[236]=1;
            if(fault=="config_unselected")configuration[72]=3;
            Put(d,76,Crc(configuration,configuration.Length));
            if(fault=="config_crc")configuration[128]^=1;
        }
        Put(d, 96, au==0?15U:legacyAu?47U:31U); Put(d, 100, 1);if(au!=0)Put(d,128,au); Seal(d);
        Buffer.BlockCopy(d, 0, f, 0, d.Length); Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("N2SDEND1"), 0, f, 0, 8);
        Put(f, 100, 3); Put(f, 104, 0); Put(f, 108, blocks); Put(f, 112, blocks / 4); Put(f, 116, payload + blocks * 91); PutQ(f, 120, blocks * (ulong)ticks); Seal(f);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("N2SDUTC1"), 0, u, 0, 8);
        Put(u, 8, 1); Put(u, 12, 7); Put(u, 16, 3); Put(u, 20, 9); Put(u, 24, 3000); Put(u, 28, uint.MaxValue - 2);
        PutQ(u, 32, 1700000000000000); Put(u, 48, 4); Put(u, 52, 5);
        PutQ(u, 56, 1700000000007000); PutQ(u, 64, 1700000000008000); Put(u, 72, hz); Put(u, 76, 1000); Seal(u);
        if (fault == "metadata") d[8] ^= 1;
        if (fault == "footer") { Put(f, 100, 4); Put(f, 104, 1); Seal(f); }
        if (fault == "card") { f[80] = 1; Seal(f); }
        if (fault == "bounds") { Put(d, 64, uint.MaxValue); Seal(d); }
        if (fault == "utc") { Put(u, 12, 8); Seal(u); }
        if (fault == "reserved") { Put(d, 104, 1); Seal(d); }
        using (var image = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite)) {
            image.SetLength((payload + blocks * 91L) * 512);
            image.Position = (Start + 3L) * 512; image.Write(d, 0, 512); image.Write(f, 0, 512); image.Write(u, 0, 512);
            if(configuration!=null){image.Position=(Start+8L)*512;image.Write(configuration,0,configuration.Length);}
            image.Position = payload * 512L;
            for (uint seq = 0; seq < blocks; seq++) {
                var block = (byte[])Fixture.Invoke(null, new object[] { (ulong)seq, (ulong)seq * ticks, mask, mask, hz, mask==3, rawD8Phase });
                if (seq == 4 && fault == "gap") PutQ(block, 24, 9);
                if (seq == 4 && fault == "tick") PutQ(block, 32, (ulong)seq * ticks + 1600);
                if (seq == 4 && fault == "profile") Put(block, 60, hz == 20000 ? 30000U : 20000U);
                if (seq == 4 && fault == "sync") {
                    if(mask==1){int bit=((1600-(int)(((ulong)seq*ticks+137)%1600))%1600)*3;block[512+bit/8]^=(byte)(1<<(bit%8));}
                    else if(mask==3)block[512 + 3 * 6 / 8] ^= 1 << ((3 * 6) % 8);
                    else block[512+(1600-(int)(((ulong)seq*ticks+137)%1600))%1600]^=1;
                }
                if (seq == 4 && fault == "header") block[92] ^= 1;
                image.Write(block, 0, block.Length);
            }
            if (fault == "truncated") image.SetLength(image.Length - 1);
        }
    }

    private static ulong RoundUp(ulong value,uint unit){return (value+unit-1)/unit*unit;}
    private static byte[] SectorAt(Stream card,long lba){card.Position=lba*512;var bytes=new byte[512];int read=card.Read(bytes,0,bytes.Length);Check(read==512,"Fixture sector truncated");return bytes;}
    private static void WriteAt(Stream card,long lba,byte[] bytes){card.Position=lba*512;card.Write(bytes,0,bytes.Length);}
    private static System.Collections.IList Catalog(Stream card,bool available=false){
        if(ExternalCatalog==null)return available?CE32_console.NpDownloadCatalog.ReadAvailable(card):CE32_console.NpDownloadCatalog.Read(card);
        try{var method=available?ExternalCatalog.DeclaringType.GetMethod("ReadAvailable",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic):ExternalCatalog;return (System.Collections.IList)method.Invoke(null,new object[]{card});}
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}
    }
    private static object RecordValue(object record,string name,bool property=false){
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        return property?record.GetType().GetProperty(name,flags).GetValue(record,null):record.GetType().GetField(name,flags).GetValue(record);
    }
    private static void RefuseMetadata(byte[] d,byte[] f,long length,string reason,uint start=Start){
        bool refused=false;try{Metadata(d,f,start,length);}catch(InvalidDataException){refused=true;}
        Check(refused,"Unsafe v11/v12/v13 metadata accepted: "+reason);
    }
    private static void CatalogEntry(Stream card,byte[] footer,uint end=0){
        var identity=new byte[512];System.Text.Encoding.ASCII.GetBytes("CE_FILESYSTEM_V1").CopyTo(identity,0);System.Text.Encoding.ASCII.GetBytes("NP_PSSI_7").CopyTo(identity,32);WriteAt(card,16,identity);
        if(end==0)end=BitConverter.ToUInt32(footer,116);
        var directory=new byte[512];Put(directory,0,1);Put(directory,4,end);Put(directory,256,end);WriteAt(card,128,directory);
    }
    private static void SameDat(string first,string second,int probes=2){
        for(int probe=1;probe<=probes;probe++){
            string name="neuropixels_probe"+probe+"_ap.dat";
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(first,name)))==Convert.ToBase64String(File.ReadAllBytes(Path.Combine(second,name))),"v11/v12 DAT bytes changed: "+name);
        }
    }
    private static void IncompleteOutput(string output,byte[] footer,bool empty,string reference,int probes=2){
        Check(File.Exists(Path.Combine(output,"descriptor.bin")) && File.ReadAllBytes(Path.Combine(output,"footer.bin")).SequenceEqual(footer),"Incomplete metadata not preserved exactly");
        Check(File.ReadAllText(Path.Combine(output,"np2_sd_export_status.txt")).Contains("Recording complete: False"),"Incomplete source reported complete");
        if(empty)Check(Directory.GetFiles(output,"*.dat").Length==0,"Metadata-only recording invented DAT samples");
        else SameDat(reference,output,probes);
    }
    private static void MetadataVersions(string root,string baseline)
    {
        MethodInfo oldExport=baseline==null?null:Assembly.LoadFile(Path.GetFullPath(baseline)).GetType("USB512_interface.NpV2SdDatExport",true).GetMethod("Export");
        foreach(uint hz in new uint[]{20000,30000}){
            string legacy=Path.Combine(root,"v11_"+hz+".img"),legacyOutput=Path.Combine(root,"v11_dat_"+hz);MakeImage(legacy,hz,"");
            using(var card=File.OpenRead(legacy))Bridge(card,Start,Payload+8*91,legacyOutput,()=>false,null);
            if(oldExport!=null){
                string oldOutput=Path.Combine(root,"baseline_v11_"+hz);
                using(var card=File.OpenRead(legacy))oldExport.Invoke(null,new object[]{card,Start,oldOutput,0L,null,null});
                SameDat(legacyOutput,oldOutput);
            }
            using(var card=new FileStream(legacy,FileMode.Open,FileAccess.ReadWrite)){
                var d=SectorAt(card,Start+3);var f=SectorAt(card,Start+4);CatalogEntry(card,f);
                var rows=Catalog(card);Check(rows.Count==1 && (uint)RecordValue(rows[0],"FormatVersion")==11 && (bool)RecordValue(rows[0],"V2",true) && (bool)RecordValue(rows[0],"Downloadable",true),"Actual catalog changed v11 admission");
                var bad=(byte[])d.Clone();Put(bad,128,1024);Seal(bad);RefuseMetadata(bad,f,card.Length,"v11 reserved descriptor tail");
                bad=(byte[])f.Clone();Put(bad,128,1024);Seal(bad);RefuseMetadata(d,bad,card.Length,"v11 reserved footer tail");
            }
            foreach(uint au in new uint[]{3072,86016}){
                string image=Path.Combine(root,"v12_"+hz+"_"+au+".img"),output=Path.Combine(root,"v12_dat_"+hz+"_"+au);MakeImage(image,hz,"",au);
                byte[] d,f;using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){
                    d=SectorAt(card,Start+3);f=SectorAt(card,Start+4);
                    Check(BitConverter.ToUInt32(d,60)==(au==3072?18432U:86016U) && BitConverter.ToUInt32(d,64)==BitConverter.ToUInt32(d,60)+728 && BitConverter.ToUInt32(d,68)==(au==3072?32768U:180224U),"Geometry differs from native v12 codec fixtures");
                    CatalogEntry(card,f);
                    var rows=Catalog(card);Check(rows.Count==1 && (uint)RecordValue(rows[0],"FormatVersion")==12 && (bool)RecordValue(rows[0],"V2",true) && (bool)RecordValue(rows[0],"Downloadable",true),"Actual catalog did not preserve/admit v12 as NP2");
                }
                using(var card=File.OpenRead(image))Bridge(card,Start,BitConverter.ToUInt32(f,116),output,()=>false,null);
                SameDat(legacyOutput,output);
                Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output,"descriptor.bin")))==Convert.ToBase64String(d) && Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output,"footer.bin")))==Convert.ToBase64String(f),"Export relabeled v12 metadata");
                long length=new FileInfo(image).Length;Metadata(d,f,Start,length);
                if(au==3072){
                    // Relocate the real payload near the 32-bit LBA/22-bit VSC
                    // command limit without allocating a two-terabyte image.
                    const uint shift=0xffff0000;uint highStart=Start+shift;
                    var highD=(byte[])d.Clone();var highF=(byte[])f.Clone();
                    foreach(int at in new[]{56,60,64,68})Put(highD,at,BitConverter.ToUInt32(highD,at)+shift);
                    Put(highD,72,uint.MaxValue);Seal(highD);Buffer.BlockCopy(highD,8,highF,8,92);Put(highF,116,BitConverter.ToUInt32(f,116)+shift);Seal(highF);
                    var bytes=File.ReadAllBytes(image);Buffer.BlockCopy(highD,0,bytes,(int)((Start+3)*512),512);Buffer.BlockCopy(highF,0,bytes,(int)((Start+4)*512),512);
                    string highOutput=Path.Combine(root,"high_v12_"+hz);
                    using(var card=new OffsetImage(bytes,(long)shift*512))Bridge(card,highStart,BitConverter.ToUInt32(highF,116),highOutput,()=>false,null);
                    SameDat(output,highOutput);
                    var overflow=(byte[])highD.Clone();Put(overflow,56,0xffffe000);Seal(overflow);RefuseMetadata(overflow,highF,(long)uint.MaxValue*512,"payload alignment crosses 32-bit LBA range",0xffffe000);
                }
                foreach(var field in new[]{Tuple.Create(8,13U),Tuple.Create(96,15U),Tuple.Create(96,63U),Tuple.Create(128,0U),Tuple.Create(128,1023U),Tuple.Create(128,1536U),Tuple.Create(132,1U),Tuple.Create(60,BitConverter.ToUInt32(d,60)+1),Tuple.Create(64,BitConverter.ToUInt32(d,64)+1),Tuple.Create(68,BitConverter.ToUInt32(d,68)+16384),Tuple.Create(72,BitConverter.ToUInt32(d,68)-1)}){
                    var bad=(byte[])d.Clone();Put(bad,field.Item1,field.Item2);Seal(bad);RefuseMetadata(bad,f,length,"descriptor field "+field.Item1);
                }
                foreach(int offset in new[]{80,128,132}){var bad=(byte[])f.Clone();bad[offset]^=1;Seal(bad);RefuseMetadata(d,bad,length,"footer identity/tail "+offset);}
                var corrupt=(byte[])d.Clone();corrupt[508]^=1;RefuseMetadata(corrupt,f,length,"CRC");
                RefuseMetadata(d,f,length-1,"truncated image");
                var excess=(byte[])f.Clone();Put(excess,108,12);Put(excess,112,3);Put(excess,116,BitConverter.ToUInt32(d,60)+1092);PutQ(excess,120,12*61440UL);Seal(excess);RefuseMetadata(d,excess,(long)BitConverter.ToUInt32(d,68)*512,"AU padding credited as recorded payload");
                foreach(uint state in new uint[]{1,4}){
                    var bad=(byte[])f.Clone();Put(bad,100,state);Put(bad,104,state==1?0x56455249U:5U);
                    if(state==1){Put(bad,108,0);Put(bad,112,0);Put(bad,116,BitConverter.ToUInt32(d,60));PutQ(bad,120,0);}
                    Seal(bad);RefuseMetadata(d,bad,length,"Failed/VERIFY_PENDING");
                    using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){WriteAt(card,Start+4,bad);var rows=Catalog(card);Check(rows.Count==1 && !(bool)RecordValue(rows[0],"Downloadable",true),"Actual catalog promoted incomplete v12");}
                    string partialOutput=Path.Combine(root,"incomplete_"+hz+"_"+au+"_"+state);
                    using(var card=File.OpenRead(image))Bridge(card,Start,BitConverter.ToUInt32(f,116),partialOutput,()=>false,null);
                    IncompleteOutput(partialOutput,bad,state==1,output);
                    if(state==4){
                        using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite))CatalogEntry(card,bad,BitConverter.ToUInt32(d,60));
                        string openOutput=Path.Combine(root,"open_tail_"+hz+"_"+au);
                        using(var card=new CapacityImage(File.OpenRead(image),(long)BitConverter.ToUInt32(d,72)*512)){
                            var rows=Catalog(card,true);Check(rows.Count==1&&(bool)RecordValue(rows[0],"Downloadable",true),"Valid open checkpoint not available");
                            long committedEnd=(long)RecordValue(rows[0],"End");Check(committedEnd==BitConverter.ToUInt32(bad,116),"Open checkpoint read fence is not its committed footer");
                            Bridge(card,Start,committedEnd,openOutput,()=>false,null);
                        }
                        IncompleteOutput(openOutput,bad,false,output);
                    }
                }
                using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){WriteAt(card,Start+4,f);CatalogEntry(card,f);}
                foreach(string fault in new[]{"gap","tick","profile","sync","header","utc","truncated"}){
                    string badImage=Path.Combine(root,"fault_"+hz+"_"+au+"_"+fault+".img"),badOutput=Path.Combine(root,"fault_dat_"+hz+"_"+au+"_"+fault);MakeImage(badImage,hz,fault,au);bool refused=false;
                    try{using(var card=File.OpenRead(badImage))Bridge(card,Start,BitConverter.ToUInt32(f,116),badOutput,()=>false,null);}catch(Exception e) when(e is IOException || e is InvalidDataException){refused=true;}
                    Check(refused,"v12 payload/UTC corruption accepted: "+fault);
                    Check(!Directory.Exists(badOutput) || Directory.GetFiles(badOutput,"*.dat").Length==0,"Corrupt v12 published DAT: "+fault);
                }
            }
        }
        Console.WriteLine("PASS {0} focused v11/v12 checks: actual catalog/bridge,20/30kHz,3072/86016-sector AUs, exact RU/padded reservation bounds, CRC/zero tails, incomplete committed-prefix export and unchanged DAT bytes{1}; no hardware or live-VSC qualification.",checks,oldExport==null?"":" against frozen v11 exporter");
    }

    private static byte[] FooterFor(byte[] descriptor,byte[] template,uint blocks,uint state=3,uint error=0)
    {
        var footer=(byte[])template.Clone();Buffer.BlockCopy(descriptor,8,footer,8,92);
        Put(footer,128,BitConverter.ToUInt32(descriptor,128));Put(footer,100,state);Put(footer,104,error);
        Put(footer,108,blocks);Put(footer,112,blocks/4);Put(footer,116,BitConverter.ToUInt32(descriptor,60)+blocks/4*364);
        PutQ(footer,120,(ulong)blocks*BitConverter.ToUInt32(descriptor,44));Seal(footer);return footer;
    }
    private static void LegacyAuMetadata(string root)
    {
        foreach(uint mask in new uint[]{1,3,7}){
            uint hz=mask==7?15000U:20000U;
            int probes=mask==1?1:mask==3?2:3;
            string tag="v13_"+mask,image=Path.Combine(root,tag+".img"),output=Path.Combine(root,tag+"_dat");
            MakeImage(image,hz,"",49152,mask,uint.MaxValue,true);
            byte[] d,f;long length;
            using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){
                d=SectorAt(card,Start+3);f=SectorAt(card,Start+4);
                Check(BitConverter.ToUInt32(d,8)==13 && BitConverter.ToUInt32(d,96)==47 &&
                    BitConverter.ToUInt32(d,60)==49152 && BitConverter.ToUInt32(d,64)==49880 &&
                    BitConverter.ToUInt32(d,68)==98304,"v13 24MiB unit geometry differs from native codec");
                // Real unused allocation exists and contains poison, not samples.
                card.SetLength((long)BitConverter.ToUInt32(d,68)*512);length=card.Length;
                var poison=new byte[512];for(int n=0;n<poison.Length;n++)poison[n]=0xa5;
                WriteAt(card,BitConverter.ToUInt32(f,116),poison);CatalogEntry(card,f);
                var rows=Catalog(card);
                Check(rows.Count==1 && (uint)RecordValue(rows[0],"FormatVersion")==13 &&
                    (bool)RecordValue(rows[0],"V2",true) && (bool)RecordValue(rows[0],"Downloadable",true),"Catalog did not classify complete v13 as NP2");
            }
            Metadata(d,f,Start,length);
            using(var card=File.OpenRead(image)){
                Bridge(card,Start,BitConverter.ToUInt32(f,116),output,()=>false,null);
            }
            for(int p=0;p<probes;p++)Check(new FileInfo(Path.Combine(output,"neuropixels_probe"+(p+1)+"_ap.dat")).Length==
                ((8L*BitConverter.ToUInt32(d,44)-(1600-137-p*311))/1600)*768,"v13 export credited unused AU padding as samples");
            foreach(uint au in new uint[]{0,2048}){
                string oldImage=Path.Combine(root,tag+"_old_"+au+".img"),oldOutput=Path.Combine(root,tag+"_old_dat_"+au);
                MakeImage(oldImage,hz,"",au,mask);
                using(var card=File.OpenRead(oldImage))Bridge(card,Start,0,oldOutput,()=>false,null);
                SameDat(oldOutput,output,probes);
            }
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output,"descriptor.bin")))==Convert.ToBase64String(d) &&
                Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output,"footer.bin")))==Convert.ToBase64String(f),"Export relabeled v13 seals");

            // Metadata-only admission of all nine units; these are not claims
            // of exported payload at the other offsets or allocation sizes.
            foreach(uint[] geometry in new[]{new uint[]{2048,16384,32768},new uint[]{4096,16384,32768},
                new uint[]{8192,16384,32768},new uint[]{16384,16384,32768},new uint[]{24576,24576,49152},
                new uint[]{32768,32768,65536},new uint[]{49152,49152,98304},new uint[]{65536,65536,131072},
                new uint[]{131072,131072,262144}}){
                var unit=(byte[])d.Clone();Put(unit,128,geometry[0]);Put(unit,60,geometry[1]);Put(unit,64,geometry[1]+728);Put(unit,68,geometry[2]);Seal(unit);
                Metadata(unit,FooterFor(unit,f,8),Start,(long)geometry[2]*512);
            }
            foreach(uint au in new uint[]{1,1023,1024,1536,3072,86016,262144}){
                var bad=(byte[])d.Clone();uint payload=(uint)RoundUp(Payload,au);
                Put(bad,128,au);Put(bad,60,payload);Put(bad,64,payload+728);Put(bad,68,(uint)RoundUp(RoundUp(payload+728,au),16384));Seal(bad);
                RefuseMetadata(bad,FooterFor(bad,f,8),(long)BitConverter.ToUInt32(bad,68)*512,"non-UHS unit with otherwise consistent geometry "+au);
            }
            foreach(var field in new[]{Tuple.Create(8,14U),Tuple.Create(96,15U),Tuple.Create(96,31U),Tuple.Create(96,63U),
                Tuple.Create(128,0U),Tuple.Create(132,1U),Tuple.Create(504,1U),Tuple.Create(60,49153U),
                Tuple.Create(64,49881U),Tuple.Create(68,114688U),Tuple.Create(72,98303U)}){
                var bad=(byte[])d.Clone();Put(bad,field.Item1,field.Item2);Seal(bad);
                RefuseMetadata(bad,FooterFor(bad,f,8),length,"v13 descriptor field "+field.Item1);
            }
            foreach(int at in new[]{128,132,504}){var bad=(byte[])f.Clone();bad[at]^=1;Seal(bad);RefuseMetadata(d,bad,length,"v13 footer unit/zero tail "+at);}
            var crc=(byte[])d.Clone();crc[508]^=1;RefuseMetadata(crc,f,length,"v13 descriptor CRC");
            crc=(byte[])f.Clone();crc[508]^=1;RefuseMetadata(d,crc,length,"v13 footer CRC");
            RefuseMetadata(d,f,(long)BitConverter.ToUInt32(f,116)*512-1,"v13 truncated committed payload");
            foreach(uint blocks in new uint[]{0,6,12})RefuseMetadata(d,FooterFor(d,f,blocks),length,"v13 Complete count versus exact limit/AU padding "+blocks);
            RefuseMetadata(d,FooterFor(d,f,8,3,1),length,"v13 Complete with error");

            byte[] shortFooter=FooterFor(d,f,4);Metadata(d,shortFooter,Start,length);
            string shortOutput=Path.Combine(root,tag+"_short_dat");
            using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){WriteAt(card,Start+4,shortFooter);CatalogEntry(card,shortFooter);}
            using(var card=File.OpenRead(image))Bridge(card,Start,BitConverter.ToUInt32(shortFooter,116),shortOutput,()=>false,null);
            for(int p=1;p<=probes;p++){
                string name="neuropixels_probe"+p+"_ap.dat";
                byte[] partial=File.ReadAllBytes(Path.Combine(shortOutput,name)),full=File.ReadAllBytes(Path.Combine(output,name));
                Check(partial.Length>0 && partial.Length<full.Length && Convert.ToBase64String(partial)==Convert.ToBase64String(full,0,partial.Length),"Valid short Complete changed the committed DAT prefix");
            }
            foreach(uint state in new uint[]{1,4}){
                var bad=FooterFor(d,f,state==1?0U:4U,state,state==1?0x56455249U:5U);
                RefuseMetadata(d,bad,length,"v13 Reserved/partial Failed");
                using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){
                    WriteAt(card,Start+4,bad);CatalogEntry(card,bad,BitConverter.ToUInt32(d,68));
                    var rows=Catalog(card);Check(rows.Count==1 && (bool)RecordValue(rows[0],"V2",true) &&
                        !(bool)RecordValue(rows[0],"Downloadable",true) && (long)RecordValue(rows[0],"End")==BitConverter.ToUInt32(d,68),"v13 incomplete allocation was promoted or its quarantine fence changed");
                }
                string partialOutput=Path.Combine(root,tag+"_incomplete_"+state);
                using(var card=File.OpenRead(image))Bridge(card,Start,BitConverter.ToUInt32(d,68),partialOutput,()=>false,null);
                IncompleteOutput(partialOutput,bad,state==1,shortOutput,probes);
            }
            using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){WriteAt(card,Start+4,f);CatalogEntry(card,f);}
            if(mask!=3){
                using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){
                    var config=new byte[2048];card.Position=(Start+8L)*512;Check(card.Read(config,0,config.Length)==config.Length,"v13 configuration truncated");
                    Put(config,44,4);var bad=(byte[])d.Clone();Put(bad,76,Crc(config,config.Length));Seal(bad);
                    var footer=FooterFor(bad,f,4);Metadata(bad,footer,Start,length);
                    WriteAt(card,Start+3,bad);WriteAt(card,Start+4,footer);card.Position=(Start+8L)*512;card.Write(config,0,config.Length);
                }
                string refusedOutput=Path.Combine(root,tag+"_short_plan");bool refused=false;
                try{using(var card=File.OpenRead(image))Bridge(card,Start,0,refusedOutput,()=>false,null);}catch(InvalidDataException){refused=true;}
                Check(refused && !Directory.Exists(refusedOutput),"v13 single/triple configuration plan did not exactly match descriptor limit");
            }
        }
        Console.WriteLine("PASS {0} focused v13 checks: all nine UHS units, mask1/3/7 catalog/bridge, v11/v12 DAT equivalence, exact plans/short Complete, padding/flags/unit/tail/CRC/count refusal and incomplete allocation quarantine; no hardware or transport qualification.",checks);
    }

    private static void Valid(string root, uint hz)
    {
        string image = Path.Combine(root, "card_" + hz + ".img"), output = Path.Combine(root, "dat_" + hz);
        MakeImage(image, hz, ""); NpV2SdDatExport.Result r;
        using (var card = File.OpenRead(image)) r = NpV2SdDatExport.Export(card, Start, output);
        Check(r.SampleHz == hz && r.CommittedBlocks == 8 && r.SourceTicks == 8 * 61440UL, "Export extent/rate");
        for (int p = 0; p < 2; p++) {
            byte[] bytes = File.ReadAllBytes(Path.Combine(output, "neuropixels_probe" + (p + 1) + "_ap.dat"));
            int first = (1600 - (137 + p * 311)) % 1600;
            Check(r.Rows[p] == (8 * 61440 - first) / 1600 && bytes.Length == r.Rows[p] * 768, "Exact rows across SD block boundaries");
            for (int row = 0; row < r.Rows[p]; row++) for (int frame = 0; frame < 16; frame++) for (int adc = 0; adc < 24; adc++) {
                int ch = (adc / 2) * 32 + (adc & 1) + frame * 2;
                short expected = (short)(2048 - Convert.ToUInt16(Word.Invoke(null, new object[] { first + row * 1600, frame, adc / 8, adc % 8, p })));
                Check(BitConverter.ToInt16(bytes, row * 768 + ch * 2) == expected, "Every signed exported channel/value/byte order must match independent serial reference");
            }
        }
        string report = File.ReadAllText(Path.Combine(output, "np2_sd_export_status.txt"));
        Check(report.StartsWith("Export complete: True") && report.Contains("No ADC payload CRC") && report.Contains("first_source_tick="), "Honest export receipt/boundary policy");
        bool refused = false; try { using (var card = File.OpenRead(image)) NpV2SdDatExport.Export(card, Start, output); } catch (IOException) { refused = true; }
        Check(refused, "Existing DAT files must never be overwritten");
        int progress = 0;
        string bridge = Path.Combine(root, "wild_dat_" + hz);
        using (var card = File.OpenRead(image)) Bridge(card, Start, Payload + 8 * 91,
            bridge, () => false, value => { Check(value >= 0 && value <= 100, "WILD progress bounds"); progress++; });
        Check(progress > 0, "Normal WILD SD export must use the NP2 companion decoder");
        for (int p = 1; p <= 2; p++) {
            string name = "neuropixels_probe" + p + "_ap.dat";
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output, name))) ==
                Convert.ToBase64String(File.ReadAllBytes(Path.Combine(bridge, name))), "WILD bridge DAT bytes must match standalone export exactly");
        }
        string cancel = Path.Combine(root, "cancel_" + hz); bool stopped = false;
        try { using (var card = File.OpenRead(image)) Bridge(card, Start, Payload + 8 * 91, cancel, () => true, null); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && File.ReadAllText(Path.Combine(cancel, "np2_sd_export_status.txt")).StartsWith("Export complete: False") &&
            Directory.GetFiles(cancel, "*.dat").Length == 0, "WILD cancellation must retain partial files, never a success receipt");
        bool catalogRefused = false;
        try { using (var card = File.OpenRead(image)) NpV2SdDatExport.Export(card, Start, Path.Combine(root, "bad_catalog_" + hz), Payload + 7 * 91); }
        catch (InvalidDataException) { catalogRefused = true; }
        Check(catalogRefused, "Catalog entry shorter than committed footer must be rejected");
    }

    private static void LargeOffset(string root, uint hz)
    {
        const uint shift = 0x01000000U, blocks = 262144U;
        uint start = Start + shift, payload = Payload + shift;
        byte[] data = File.ReadAllBytes(Path.Combine(root, "card_" + hz + ".img"));
        byte[] d = new byte[512], f = new byte[512];
        Buffer.BlockCopy(data, checked((int)((Start + 3) * 512)), d, 0, 512);
        Buffer.BlockCopy(data, checked((int)((Start + 4) * 512)), f, 0, 512);
        foreach (int at in new[] { 56, 60, 64, 68, 72 }) Put(d, at, BitConverter.ToUInt32(d, at) + shift);
        Seal(d); Buffer.BlockCopy(d, 8, f, 8, 92); Put(f, 116, payload + 8 * 91); Seal(f);
        Buffer.BlockCopy(d, 0, data, checked((int)((Start + 3) * 512)), 512);
        Buffer.BlockCopy(f, 0, data, checked((int)((Start + 4) * 512)), 512);
        string output = Path.Combine(root, "high_lba_" + hz);
        using (var card = new OffsetImage(data, (long)shift * 512))
        {
            Check(card.Length > uint.MaxValue, "Actual exporter high-offset stream is beyond4GiB");
            var r = NpV2SdDatExport.Export(card, start, output);
            Check(r.SampleHz == hz && r.CommittedBlocks == 8 && r.SourceTicks == 8 * 61440UL &&
                  card.Position == ((long)payload + 8 * 91) * 512, "Actual Export narrowed LBA/file position");
        }
        for (int p = 1; p <= 2; p++)
        {
            string name = "neuropixels_probe" + p + "_ap.dat";
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output, name))) ==
                  Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "dat_" + hz, name))),
                  "Relocated actual export must preserve every DAT byte");
        }

        // Exercise the real private metadata gate with large counts only. Do not
        // pretend these absent blocks or >4GiB DAT outputs have been exported.
        uint end = payload + blocks * 91;
        Put(d, 64, end); Put(d, 68, end); Put(d, 72, end + 16384); Seal(d);
        Buffer.BlockCopy(d, 8, f, 8, 92); Put(f, 108, blocks); Put(f, 112, blocks / 4);
        Put(f, 116, end); PutQ(f, 120, (ulong)blocks * 61440); Seal(f);
        long length = (long)end * 512;
        Metadata(d, f, start, length);
        Check(length > uint.MaxValue && BitConverter.ToUInt64(f, 120) > uint.MaxValue,
              "Large footer/file-length admission actually exceeds32 bits");
        foreach (long shortLength in new[] { length - 1, (long)unchecked((uint)length) })
        {
            bool refused = false; try { Metadata(d, f, start, shortLength); } catch (InvalidDataException) { refused = true; }
            Check(refused, "Large footer accepted a truncated/narrowed image length");
        }
        foreach (int at in new[] { 108, 112, 116, 120, 124 })
        {
            byte[] bad = (byte[])f.Clone(); Put(bad, at, BitConverter.ToUInt32(bad, at) ^ 1U); Seal(bad);
            bool refused = false; try { Metadata(d, bad, start, length); } catch (InvalidDataException) { refused = true; }
            Check(refused, "Resealed large footer accounting/high tick word accepted");
        }
        byte[] failed = (byte[])f.Clone(); Put(failed, 100, 4); Put(failed, 104, 1); Seal(failed);
        bool incomplete = false; try { Metadata(d, failed, start, length); } catch (InvalidDataException) { incomplete = true; }
        Check(incomplete, "Large-count Failed footer was promoted");
    }

    private static void RuntimeProfiles(string root)
    {
        // Call the maintained exporter and USB decoder, not a model exporter.
        // The independent scalar oracle checks every emitted ADC/channel bit.
        foreach(uint mask in new uint[]{1,3,7})foreach(uint hz in new uint[]{7500,15000,20000,30000}){
            uint ticks=mask==1?122880U:mask==3?61440U:40960U;
            int probes=mask==1?1:mask==3?2:3;
            foreach(uint au in new uint[]{0,86016}){
                string tag=mask+"_"+hz+"_"+au,image=Path.Combine(root,"runtime_"+tag+".img"),output=Path.Combine(root,"runtime_dat_"+tag);
                // Test ordinary aligned codec6 plus its existing USB-qualified
                // raw-phase/lookahead variant across real block boundaries.
                MakeImage(image,hz,"",au,mask,mask==7 && au!=0?719U:uint.MaxValue);
                NpV2SdDatExport.Result result;
                using(var card=File.OpenRead(image))result=NpV2SdDatExport.Export(card,Start,output);
                Check(result.ProbeMask==mask && result.SampleHz==hz && result.CommittedBlocks==8 &&
                      result.SourceTicks==8UL*ticks && result.Rows.Length==probes && result.Phases.Length==probes,"Closed runtime export profile/counts");
                Check(Directory.GetFiles(output,"*.dat").Length==probes,"Wrong number of classic per-probe DAT files");
                for(int p=0;p<probes;p++){
                    string name="neuropixels_probe"+(p+1)+"_ap.dat";
                    byte[] bytes=File.ReadAllBytes(Path.Combine(output,name));
                    int first=(1600-(137+p*311))%1600;
                    Check(result.Phases[p]==137+p*311 && result.Rows[p]==(8L*ticks-first)/1600 && bytes.LongLength==result.Rows[p]*768,"Exact runtime row extent/phase/length");
                    for(int row=0;row<result.Rows[p];row++)for(int frame=0;frame<16;frame++)for(int adc=0;adc<24;adc++){
                        int channel=(adc/2)*32+(adc&1)+frame*2;
                        short expected=(short)(2048-Convert.ToUInt16(Word.Invoke(null,new object[]{first+row*1600,frame,adc/8,adc%8,p})));
                        Check(BitConverter.ToInt16(bytes,row*768+channel*2)==expected,"Runtime signed DAT changed an ADC bit/channel/byte order");
                    }
                    Check(File.ReadAllText(Path.Combine(output,Path.ChangeExtension(name,".xml"))).Contains("physical geometry unavailable"),"Fingerprint provenance inferred physical electrode geometry");
                }
                string report=File.ReadAllText(Path.Combine(output,"np2_sd_export_status.txt"));
                Check(report.StartsWith("Export complete: True") && report.Contains("No ADC payload CRC") && report.Contains("first_source_tick="),"Runtime export claimed unchecked completeness/time");
                if(mask==1){
                    Check(report.Contains("N2IMRO1") && report.Contains("NOT applied-mapping proof") && !report.Contains("N2IMRO2"),"Single staged request provenance was relabeled");
                    byte[] archived=File.ReadAllBytes(Path.Combine(output,"configuration.bin"));
                    using(var card=File.OpenRead(image)){
                        var original=new byte[2048];card.Position=(Start+8L)*512;
                        Check(card.Read(original,0,original.Length)==original.Length && Convert.ToBase64String(original)==Convert.ToBase64String(archived) && Crc(archived,archived.Length)==BitConverter.ToUInt32(SectorAt(card,Start+3),76),"Single request archive/descriptor CRC changed");
                    }
                }else if(mask==7){
                    Check(report.Contains("N2IMRO2 kind3") && report.Contains("NOT full maps or applied-mapping proof") &&
                          report.Contains("Probe 3 staged configuration:") && report.Contains("AP step=1600/"+(hz*1600)+" seconds"),"Triple fingerprint/rational-time receipt changed");
                    byte[] archived=File.ReadAllBytes(Path.Combine(output,"configuration.bin"));
                    using(var card=File.OpenRead(image)){
                        var original=new byte[2048];card.Position=(Start+8L)*512;
                        Check(card.Read(original,0,original.Length)==original.Length && Convert.ToBase64String(original)==Convert.ToBase64String(archived),"Triple fingerprint archive changed bytes");
                        Check(Crc(archived,archived.Length)==BitConverter.ToUInt32(SectorAt(card,Start+3),76),"Triple archive lacks exact descriptor CRC proof");
                    }
                }
                if(mask!=7 && au!=0)SameDat(Path.Combine(root,"runtime_dat_"+mask+"_"+hz+"_0"),output,probes);

                // Re-seal BOTH records: tuple refusal must not merely depend on
                // a CRC or descriptor/footer mismatch. Nothing past metadata
                // may be read and no output directory may be created.
                foreach(var field in new[]{Tuple.Create(16,25000U),Tuple.Create(20,hz*1600+1),
                    Tuple.Create(24,mask==3?7U:3U),Tuple.Create(28,mask==3?511U:63U),Tuple.Create(32,mask==7?4U:6U),
                    Tuple.Create(36,mask==3?9U:6U),Tuple.Create(40,46591U),Tuple.Create(44,ticks+1),Tuple.Create(48,90U),Tuple.Create(52,3U)}){
                    byte[] data=File.ReadAllBytes(image);
                    using(var card=new MemoryStream(data,true)){
                        byte[] d=SectorAt(card,Start+3),f=SectorAt(card,Start+4);
                        Put(d,field.Item1,field.Item2);Seal(d);Buffer.BlockCopy(d,8,f,8,92);Seal(f);
                        WriteAt(card,Start+3,d);WriteAt(card,Start+4,f);
                        string refusedOutput=Path.Combine(root,"tuple_"+tag+"_"+field.Item1);bool refused=false;
                        try{NpV2SdDatExport.Export(card,Start,refusedOutput);}catch(InvalidDataException){refused=true;}
                        Check(refused && card.Position==(Start+5L)*512 && !Directory.Exists(refusedOutput),"Mixed runtime tuple read payload/configuration or published output");
                    }
                }
            }
        }
        // The new single format validates its stored configuration before
        // output. A planned default has no staged request or applied claim.
        string planned=Path.Combine(root,"single_planned.img"),plannedOutput=Path.Combine(root,"single_planned_dat");
        MakeImage(planned,15000,"config_planned",0,1);
        using(var card=File.OpenRead(planned))Check(NpV2SdDatExport.Export(card,Start,plannedOutput).Rows.Length==1,"Single planned kind1 refused");
        foreach(string fault in new[]{"config_crc","config_kind","config_reserved","config_unselected","config_port","config_request","footer","gap","tick","profile","sync","header","utc","truncated"}){
            string image=Path.Combine(root,"single_fault_"+fault+".img"),output=Path.Combine(root,"single_fault_dat_"+fault);
            MakeImage(image,30000,fault,0,1);bool refused=false;
            try{using(var card=File.OpenRead(image))NpV2SdDatExport.Export(card,Start,output);}catch(Exception e)when(e is IOException || e is InvalidDataException){refused=true;}
            Check(refused,"Unsafe single provenance/payload accepted: "+fault);
            Check(!Directory.Exists(output) || (Directory.GetFiles(output,"*.dat").Length==0 && File.ReadAllText(Path.Combine(output,"np2_sd_export_status.txt")).StartsWith("Export complete: False")),"Failed single export published DAT/success");
            if(fault.StartsWith("config_"))Check(!Directory.Exists(output),"Invalid single provenance created output");
        }
        foreach(string fault in new[]{"config_crc","config_kind","config_reserved","config_unselected", "metadata","footer","card","bounds","reserved","gap","tick","profile","sync","header","utc","truncated"}){
            string image=Path.Combine(root,"triple_fault_"+fault+".img"),output=Path.Combine(root,"triple_fault_dat_"+fault);
            MakeImage(image,15000,fault,0,7);bool refused=false;
            try{using(var card=File.OpenRead(image))NpV2SdDatExport.Export(card,Start,output);}catch(Exception e)when(e is IOException || e is InvalidDataException){refused=true;}
            Check(refused,"Unsafe triple metadata/provenance/payload accepted: "+fault);
            Check(!Directory.Exists(output) || (Directory.GetFiles(output,"*.dat").Length==0 &&
                File.ReadAllText(Path.Combine(output,"np2_sd_export_status.txt")).StartsWith("Export complete: False")),"Failed triple export published DAT/success");
            if(fault.StartsWith("config_"))Check(!Directory.Exists(output),"Invalid fingerprint provenance created output");
        }
        Console.WriteLine("PASS {0} focused runtime SD export checks: all twelve mask1/3/7 x7.5/15/20/30 v11/v12, every signed ADC bit/channel, packed3/6 and codec6 aligned/raw-D8, closed tuples before payload/output, N2IMRO1 single staged request and kind3 fingerprint provenance, fail-closed files; no hardware/throughput qualification.",checks);
    }

    private static int Main(string[] args)
    {
        try {
            bool runtimeOnly=args.Length>=2 && args[1]=="--runtime-only";
            bool sourceMetadataOnly=args.Length>=2 && args[1]=="--v12-only";
            if(args.Length>=2 && !runtimeOnly && !sourceMetadataOnly){
                var actual=Assembly.LoadFrom(Path.GetFullPath(args[1]));
                ExternalBridge=actual.GetType("CE32_console.NpCe64Export",true).GetMethod("Export",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                ExternalCatalog=actual.GetType("CE32_console.NpDownloadCatalog",true).GetMethod("Read",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                ExternalMetadata=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"NpV2SdDatExport.dll")).GetType("USB512_interface.NpV2SdDatExport",true).GetMethod("ValidateMetadata");
                if(ExternalBridge==null)throw new Exception("Actual console export bridge missing");
                Console.WriteLine("Testing actual Release bridge: "+Path.GetFullPath(args[1]));
            }
            string root = Path.Combine(args[0], "export_tests_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            if(runtimeOnly){RuntimeProfiles(root);return 0;}
            if(sourceMetadataOnly || (args.Length>=3 && args[2]=="--v12-only")){
                int baselineAt=sourceMetadataOnly?2:3;
                MetadataVersions(root,args.Length>baselineAt?args[baselineAt]:null);LegacyAuMetadata(root);return 0;
            }
            Valid(root, 20000); Valid(root, 30000);
            checks += NpV2UsbConsoleTests.LargeCounterTests();
            LargeOffset(root, 20000); LargeOffset(root, 30000);
            foreach (string fault in new[] { "metadata", "footer", "card", "bounds", "utc", "reserved", "gap", "tick", "profile", "sync", "header", "truncated" }) {
                string image = Path.Combine(root, fault + ".img"), output = Path.Combine(root, fault); MakeImage(image, 20000, fault);
                bool rejected = false; try { using (var card = File.OpenRead(image)) NpV2SdDatExport.Export(card, Start, output); } catch (Exception e) when (e is IOException || e is InvalidDataException) { rejected = true; }
                Check(rejected, "Corruption must be rejected: " + fault);
                if (Directory.Exists(output)) {
                    Check(File.ReadAllText(Path.Combine(output, "np2_sd_export_status.txt")).StartsWith("Export complete: False"), "No false-complete export receipt: " + fault);
                    Check(Directory.GetFiles(output, "*.dat").Length == 0, "Failed verification must retain only partial DAT files: " + fault);
                }
            }
            if(ExternalBridge!=null)MetadataVersions(root,null);
            LegacyAuMetadata(root);
            RuntimeProfiles(root);
            Console.WriteLine("PASS {0} SD-to-classic-DAT checks: every sample/channel/row, 7.5/15/20/30 kHz runtime profiles, torn metadata, incomplete footer, truncation, profile/tick/sync corruption, UTC wrap, immutable headers, no-overwrite/fail-closed output; seeded source32 continuation, actual high-LBA export, large-footer/length helpers (no full >4GiB DAT write)", checks); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
