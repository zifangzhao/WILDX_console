using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CE32_console;
using USB512_interface;

internal static class NpV1SdOptionalSensorTests {
    const long Start=8192;const int Sensor=61440;static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Seal(byte[] b,int at){Put(b,at+508,NpUsbCrc(b,at,508));}
    static uint NpUsbCrc(byte[] b,int at,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[at+i];for(int bit=0;bit<8;bit++)c=(c>>1)^(0xedb88320U&(0U-(c&1)));}return ~c;}
    static void SensorCore(byte[] b,int chunk){
        int p=Sensor;uint seq=(uint)chunk+1,tick=(uint)(chunk*10+10);Put(b,p+28,3);Put(b,p+32,7);
        for(int i=0;i<3;i++){Put(b,p+40+i*4,seq);Put(b,p+52+i*4,tick);}Put(b,p+64,seq*128);
        for(int i=0;i<9;i++)Put(b,p+68+i*4,(uint)(i+1)*1000);Put(b,p+160,NpUsbCrc(b,p,160));
    }
    static void Word(byte[] b,int at,int pin,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<pin);}
    static NpV1PowerCutRecoveryTests.SparseCard Card(int version=6,int valid=8,bool complete=true){
        var card=new NpV1PowerCutRecoveryTests.SparseCard(valid,8,8192,version==5?7U:3U,15000);
        if(version!=6){Put(card.Header,8,(uint)version);Put(card.Footer,8,(uint)version);}
        if(version==4){Put(card.Header,20,2);Put(card.Footer,20,2);Put(card.Header,80,1);Put(card.Footer,80,1);Put(card.Header,96,0);Put(card.Footer,96,0);}
        foreach(var entry in card.Chunks){byte[] b=entry.Value;
            if(version==4){
                Array.Clear(b,0,65024);var native=new byte[30720];for(int f=0;f<512;f++){uint count=(uint)(entry.Key*512+f);int at=f*60;Word(native,at,0,0,count%13==0?0x330U:0xcfU);Word(native,at,7,2,0x11);Word(native,at,7,3,count>>10);Word(native,at,7,4,count&1023);}
                for(int i=0;i<7680;i++)for(int lane=0;lane<4;lane++)for(int p=0;p<2;p++)b[lane*15360+i*2+p]=native[i*4+lane];
                System.Text.Encoding.ASCII.GetBytes("NPSENS01").CopyTo(b,Sensor);Put(b,Sensor+8,(uint)entry.Key);Put(b,Sensor+12,(uint)(entry.Key*10+10));Put(b,Sensor+16,140);Put(b,Sensor+20,0x31534e53);Put(b,Sensor+24,1);Put(b,Sensor+36,(uint)(entry.Key*10+10));
            }else if(version==5){Put(b,61632+8,5);Seal(b,61632);}
            SensorCore(b,entry.Key);
        }
        Seal(card.Header,0);if(complete)card.FinalizeFooter((uint)valid);else Seal(card.Footer,0);return card;
    }
    static byte[] Fine(NpV1PowerCutRecoveryTests.SparseCard card,int chunk,bool known){
        byte[] fine=known?NpSensorFineWire.Pack(new NpSensorFineStamp {Mask=7,Domain=17,Generation=9,Counter=new uint[]{0,uint.MaxValue,123456}},new NpSensorFineBinding {Domain=17,Generation=9,SourceEpoch=11,CounterHz=128000000,SensorValid=7,
            Sequence=new[]{(uint)chunk+1,(uint)chunk+1,(uint)chunk+1},SourceMs=new[]{(uint)(chunk*10+10),(uint)(chunk*10+10),(uint)(chunk*10+10)}}):Enumerable.Range(0,28).Select(i=>(byte)(i*7+3)).ToArray();
        fine.CopyTo(card.Chunks[chunk],Sensor+164);return fine;
    }
    static Dictionary<string,string> Status(string folder,string name="capture_status.txt"){return File.ReadAllLines(Path.Combine(folder,name)).Where(l=>l.Contains("=")).Select(l=>l.Split(new[]{'='},2)).GroupBy(p=>p[0]).ToDictionary(g=>g.Key,g=>g.Last()[1]);}
    static Dictionary<string,byte[]> Neural(string folder){return Directory.GetFiles(folder,"*.dat").Where(p=>Path.GetFileName(p)=="analogin.dat"||System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"^np[1-3]_(ap|lfp)\.dat$")).ToDictionary(Path.GetFileName,File.ReadAllBytes);}
    static void Same(Dictionary<string,byte[]> baseline,string folder,string why){var now=Neural(folder);Need(now.Count==baseline.Count&&now.All(f=>baseline.ContainsKey(f.Key)&&f.Value.SequenceEqual(baseline[f.Key])),"NN/AP/LFP/legacy analog bytes changed: "+why);}
    sealed class OutputFault:Stream {
        readonly Stream source;readonly string folder,name;bool injected;
        internal OutputFault(Stream source,string folder,string name){this.source=source;this.folder=folder;this.name=name;}
        public override int Read(byte[] b,int o,int n){if(!injected&&Directory.Exists(folder)){Directory.CreateDirectory(Path.Combine(folder,name));injected=true;}return source.Read(b,o,n);}
        public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return true;}}public override long Length{get{return source.Length;}}public override long Position{get{return source.Position;}set{source.Position=value;}}
        public override long Seek(long n,SeekOrigin s){return source.Seek(n,s);}public override void Flush(){}public override void SetLength(long n){throw new Exception("Source change attempted");}public override void Write(byte[] b,int o,int n){throw new Exception("Source write attempted");}
    }
    static void Export(NpV1PowerCutRecoveryTests.SparseCard card,string folder,bool recovery=false,string outputFault=null,Func<bool> cancel=null){
        Stream source=outputFault==null?(Stream)card:new OutputFault(card,folder,outputFault);
        if(recovery)NpCe64Export.ExportPowerInterruptedPrefix(source,Start,card.ReservedEnd,folder,cancel,null);else NpCe64Export.Export(source,Start,card.ReservedEnd,folder,cancel,null);
    }
    static void Normal(string root,int version){
        Dictionary<string,byte[]> baseline=null;Dictionary<string,string> baseStatus=null;
        foreach(string fault in new[]{"baseline","known_fine","unknown_fine","core_crc","core_abi","core_sequence","sensor_io","fine_io","fine_encoding_io","status_io"}){
            string folder=Path.Combine(root,"v"+version+"_"+fault);using(var card=Card(version)){
                var fine=new Dictionary<int,byte[]>();if(fault.Contains("fine"))foreach(int chunk in new[]{1,4,7})fine[chunk]=Fine(card,chunk,fault=="known_fine");
                if(fault=="core_crc")card.Chunks[2][Sensor+160]^=1;if(fault=="core_abi"){Put(card.Chunks[2],Sensor+24,99);Put(card.Chunks[2],Sensor+160,NpUsbCrc(card.Chunks[2],Sensor,160));}
                if(fault=="core_sequence"){Put(card.Chunks[2],Sensor+8,33);Put(card.Chunks[2],Sensor+160,NpUsbCrc(card.Chunks[2],Sensor,160));}
                byte[][] original=card.Chunks.Values.Select(b=>(byte[])b.Clone()).ToArray();
                Export(card,folder,false,fault=="sensor_io"?"sensors.csv":fault=="fine_io"?"sensors_fine.bin":fault=="fine_encoding_io"?"sensors_fine_encoding.txt":fault=="status_io"?"imu_sd_export_status.txt":null);
                var status=Status(folder);Need(status["complete"]=="True","Optional sensor issue changed finalization: "+fault);
                if(baseline==null){baseline=Neural(folder);baseStatus=status;}else {Same(baseline,folder,"v"+version+"/"+fault);foreach(string key in baseStatus.Keys.Where(k=>k.EndsWith("_ap_rows")||k.EndsWith("_lfp_rows")||k.EndsWith("_frame_errors")||k.EndsWith("_counter_errors")))Need(status[key]==baseStatus[key],"NN row/check count changed: "+key);}
                Need(card.Chunks.Values.SelectMany(b=>b).SequenceEqual(original.SelectMany(b=>b)),"Source data changed");
                bool badCore=fault=="core_crc"||fault=="core_abi"||fault=="core_sequence";
                if(fault!="sensor_io")Need(new FileInfo(Path.Combine(folder,"sensors.bin")).Length==(badCore?7:8)*164,"Invalid core entered valid sensor output or later good core lost");
                if(fault=="known_fine"||fault=="unknown_fine"||fault=="fine_encoding_io"){
                    byte[] archive=File.ReadAllBytes(Path.Combine(folder,"sensors_fine.bin"));Need(archive.Length==3*32,"Sparse fine archive geometry");int index=0;foreach(var item in fine){Need(BitConverter.ToUInt32(archive,index*32)==item.Key&&archive.Skip(index*32+4).Take(28).SequenceEqual(item.Value),"Fine source chunk/original bytes changed");index++;}
                    var optional=Status(folder,"imu_sd_export_status.txt");Need(optional["fine_records_saved"]=="3"&&optional["fine_partial_record_bytes"]=="0"&&optional["invalid_sensor_records"]=="0","Raw unknown fine data was rejected or count wrong");
                    if(fault!="fine_encoding_io")Need(File.ReadAllText(Path.Combine(folder,"sensors_fine_encoding.txt")).Contains("fine_clock_qualified=false"),"Raw fine archive became qualified clock");
                }else if(fault!="fine_io")Need(!File.Exists(Path.Combine(folder,"sensors_fine.bin"))&&!File.Exists(Path.Combine(folder,"sensors_fine_encoding.txt")),"All-zero legacy sidecar generated a fine archive");
                if(badCore){var optional=Status(folder,"imu_sd_export_status.txt");Need(optional["invalid_sensor_records"]=="1"&&optional["first_bad_source_chunk"]=="2"&&optional["first_bad_raw_bytes"]=="192","Bad core quality report missing");Need(File.ReadAllBytes(Path.Combine(folder,"imu_sd_first_bad_record.bin")).SequenceEqual(original[2].Skip(Sensor).Take(192)),"First bad192B source record changed");}
                if(fault.EndsWith("_io"))Need(status["optional_imu_error"]!="none"&&long.Parse(status["sensor_metadata_io_errors"])>0,"Optional metadata IO failure hidden");
                Need(new FileInfo(Path.Combine(folder,"analogin.dat")).Length==8*512,"Legacy analogin replaced with sensor grid");
            }
        }
    }
    static void Strict(string root){
        foreach(string fault in new[]{"padding","descriptor_crc","descriptor_session","NN_sync","misc","cancel"}){
            string folder=Path.Combine(root,"strict_"+fault);using(var card=Card(5)){
                if(fault=="padding")card.Chunks[2][62144]=1;if(fault=="descriptor_crc")card.Chunks[2][61632+508]^=1;if(fault=="descriptor_session")card.Descriptor(2,12,123);if(fault=="NN_sync")card.BadSync(0,100);if(fault=="misc")card.Chunks[2][65024+22]^=1;
                bool rejected=false;try{Export(card,folder,false,null,fault=="cancel"?(Func<bool>)(()=>card.DataBytes>=65536):null);}catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is OperationCanceledException){Need((ex is OperationCanceledException)==(fault=="cancel"),"Cancellation exception was swallowed/changed");rejected=true;}
                Need(rejected,"Required NN/padding/descriptor/misc/cancel check weakened: "+fault);
            }
        }
        string strict=Path.Combine(root,"strict_sensor_record");Directory.CreateDirectory(strict);using(var card=Card())using(var bin=File.Create(Path.Combine(strict,"sensor.bin")))using(var csv=File.Create(Path.Combine(strict,"sensor.csv"))){card.Chunks[0][Sensor+160]^=1;bool rejected=false;try{NpCe64Export.SensorRecord(card.Chunks[0],Sensor,0,bin,csv);}catch(InvalidDataException){rejected=true;}Need(rejected&&bin.Length==0&&csv.Length==0,"Strict media SensorRecord behavior changed");}
    }
    static void Prefix(string root){
        string baseline=Path.Combine(root,"prefix_baseline");using(var card=Card(6,6,false)){card.BadSync(2,100);Export(card,baseline,true);}
        foreach(string fault in new[]{"lazy_rollback","bad_core"}){
            string folder=Path.Combine(root,"prefix_"+fault);using(var card=Card(6,6,false)){
                if(fault=="lazy_rollback"){Fine(card,2,false);card.BadSync(2,100);}else {Fine(card,1,false);card.Chunks[2][Sensor+160]^=1;}
                Export(card,folder,true);Dictionary<string,string> status=Status(folder),optional=Status(folder,"imu_sd_export_status.txt");
                if(fault=="lazy_rollback"){Same(Neural(baseline),folder,"optional lazy handle added after prefix checkpoint");Need(status["recovered_prefix_chunks"]=="2"&&optional["fine_records_saved"]=="0"&&new FileInfo(Path.Combine(folder,"sensors_fine.bin")).Length==0,"Lazy fine prefix rollback/count incorrect");}
                else Need(status["recovered_prefix_chunks"]=="6"&&optional["invalid_sensor_records"]=="1"&&optional["fine_records_saved"]=="1","Optional core failure truncated NN recovery prefix");
            }
        }
    }
    static int Main(string[] args){try{string root=Path.Combine(args[0],Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);foreach(int version in new[]{4,5,6})Normal(root,version);Strict(root);Prefix(root);Console.WriteLine("PASS "+checks+" actual V1 SD NN/legacy-analog byte+row parity, sensor parse/IO isolation, sparse raw fine association, strict NN/descriptor/padding/cancel and lazy-prefix rollback checks; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
