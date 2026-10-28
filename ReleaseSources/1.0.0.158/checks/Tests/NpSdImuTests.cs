using System;
using System.IO;
using System.Linq;
using USB512_interface;

static class NpSdImuTests
{
    static int checks;
    static void Need(bool b,string why){checks++;if(!b)throw new Exception(why);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Seal(byte[] b){Put(b,508,NpSdImuSnapshot.Crc(b,396,112));}
    static byte[] Block(uint sequence=7,uint flags=0x800U|32U|40U){
        var b=new byte[46592];Put(b,52,flags);Buffer.BlockCopy(BitConverter.GetBytes((ulong)sequence),0,b,24,8);
        Buffer.BlockCopy(BitConverter.GetBytes((ulong)sequence*40960UL),0,b,32,8);
        int at=396;Put(b,at,0x32534e53U);Put(b,at+4,2);Put(b,at+8,3);Put(b,at+12,3);Put(b,at+16,1000);
        Put(b,at+20,sequence);Put(b,at+24,sequence);Put(b,at+32,995);Put(b,at+36,994);Put(b,at+44,sequence*128);
        Put(b,at+48,1000000);Put(b,at+52,unchecked((uint)-500000));Put(b,at+60,12500);Put(b,at+72,42000);Put(b,at+96,0xd8);Put(b,at+108,7);
        Seal(b);return b;
    }
    static void Refuse(Action a,string why){try{a();}catch(InvalidDataException){checks++;return;}throw new Exception("Accepted "+why);}
    static void Parser(){
        var b=Block();var s=NpSdImuSnapshot.Parse(b);
        Need(s.Ag[0]==1000000&&s.Ag[1]==-500000&&s.Ag[3]==12500&&s.Mag[0]==42000,"physical units/axes changed");
        Need(s.Raw.Length==116&&s.Raw.SequenceEqual(b.Skip(396).Take(116)),"original112B+CRC not preserved");
        var legacy=(byte[])b.Clone();Put(legacy,52,40);Need(NpSdImuSnapshot.Parse(legacy)==null,"legacy tail was reinterpreted");
        var absent=Block();Array.Clear(absent,396,116);Need(NpSdImuSnapshot.Parse(absent)==null,"zero tail did not mean absent");
        for(int i=396;i<512;i++){var corrupt=(byte[])b.Clone();corrupt[i]^=1;Refuse(()=>NpSdImuSnapshot.Parse(corrupt),"corrupted byte"+i);}
        foreach(uint extra in new uint[]{128U,512U,4096U,64U|1024U}){var bad=Block(flags:0x800U|40U|extra);Refuse(()=>NpSdImuSnapshot.Parse(bad),"overlapping or unknown extension");}
        var invalid=Block();Put(invalid,396+44,0x1000000);Seal(invalid);Refuse(()=>NpSdImuSnapshot.Parse(invalid),"oversized sensor clock");
        invalid=Block();Put(invalid,396+8,0);Seal(invalid);Refuse(()=>NpSdImuSnapshot.Parse(invalid),"enabled/valid mismatch");
        invalid=Block();Put(invalid,396+32,899);Seal(invalid);Refuse(()=>NpSdImuSnapshot.Parse(invalid),"stale valid AG");
        invalid=Block();Put(invalid,396+20,0);Seal(invalid);Refuse(()=>NpSdImuSnapshot.Parse(invalid),"zero valid AG sequence");
        foreach(uint flags in new uint[]{0x800U|40U,0x800U|40U|64U,0x800U|40U|1024U}){
            var original=Block(flags:flags);for(int n=184;n<384;n++)original[n]=(byte)n;
            var keep=(byte[])original.Clone();var copy=new byte[original.Length];NpSdImuSnapshot.NormalizeNeuralCopy(original,copy);
            Need(original.SequenceEqual(keep),"original block changed");
            Need(BitConverter.ToUInt32(copy,52)==(flags&~0x800U),"unknown/non-sensor flag removed");
            Need(copy.Skip(184).Take(200).SequenceEqual(original.Skip(184).Take(200)),"raw-D8/CL region changed");
            Need(copy.Skip(396).Take(116).All(x=>x==0)&&copy.Skip(512).SequenceEqual(original.Skip(512)),"private normalization changed neural bytes");
        }
        bool refused=false;try{NpSdImuSnapshot.NormalizeNeuralCopy(b,b);}catch(ArgumentException){refused=true;}Need(refused,"in-place normalization allowed");
    }
    static void Export(string folder){
        Directory.CreateDirectory(folder);var sentinel=Path.Combine(folder,"neural.dat");File.WriteAllBytes(sentinel,new byte[]{1,2,3});
        var export=new NpSdImuExport(folder);var b=Block();
        export.Feed(b);export.Feed(b);export.Feed(Block(9));var bad=Block(10);bad[500]^=1;export.Feed(bad);export.Complete();export.Dispose();
        Need(export.Samples==2&&export.Observations==3&&export.SequenceGaps==1,"sequence/dedup statistics incorrect");
        Need(export.Error!=null,"bad sensor CRC not reported");
        Need(File.ReadAllBytes(sentinel).SequenceEqual(new byte[]{1,2,3}),"neural output touched by IMU observer");
        var rows=File.ReadAllLines(Path.Combine(folder,"sensors.csv"));
        Need(rows.Length==4&&rows[0].Split(',').Length==37&&rows.Skip(1).All(r=>r.Split(',').Length==37),"CSV shape incorrect");
        Need(rows.Skip(1).All(r=>r.Split(',').Skip(30).All(c=>c=="")),"unrecorded diagnostic counters fabricated");
        Need(new FileInfo(Path.Combine(folder,"imu_sd_records.sns2")).Length==3*132,"exact raw record framing incorrect");
        Need(File.Exists(Path.Combine(folder,"imu_sd_first_bad_record.bin")),"bad optional evidence missing");
        var status=File.ReadAllText(Path.Combine(folder,"imu_sd_export_status.txt"));
        Need(status.Contains("accel200Hz_8g")&&status.Contains("mag100Hz")&&status.Contains("not measured retained rate")&&status.Contains("unavailable"),"profile/sampling limitation missing");
    }
    static byte[] Timed(uint sequence,uint published,uint ag,uint mag,uint valid=3){
        var b=Block(sequence);Put(b,412,published);Put(b,428,ag);Put(b,432,mag);Put(b,408,valid);Seal(b);return b;
    }
    static void Grid(string root){
        const uint hz=32000000;const ulong extent=640000;
        foreach(bool locked in new[]{true,false}){
            string folder=Path.Combine(root,locked?"locked_grid":"legacy_grid");Directory.CreateDirectory(folder);
            var first=Timed(1,1000,995,994);var next=Timed(2,1008,1005,1006);
            Put(next,444,2000000);Put(next,456,1250);Put(next,468,450);Seal(next);
            var export=new NpSdImuExport(folder);export.Feed(first);export.Feed(next);export.Feed(Timed(3,1012,1010,1010,0));
            export.Complete(hz,extent,locked?new NpSdImuGridOrigin(1000,250,hz,7,"fixture-only qualified final-generation NP-byte-zero release"):null);export.Dispose();
            byte[] analog=File.ReadAllBytes(Path.Combine(folder,"analogin.dat")),mask=File.ReadAllBytes(Path.Combine(folder,"analogin_validity.dat"));
            Need(export.GridComplete&&export.GridRows==25&&analog.Length==25*32&&mask.Length==25,"1250 Hz verified-duration grid geometry");
            if(locked){
                Need(Enumerable.Range(0,6).All(i=>mask[i]==0&&BitConverter.ToInt16(analog,i*32+2)==short.MinValue),"pre-origin sensor data filled locked prefix");
                Need(mask[6]==1&&BitConverter.ToInt16(analog,6*32+2)==8192&&BitConverter.ToInt16(analog,6*32+8)==21,"AG source-offset/gain/midpoint rounding");
                Need(mask[8]==3&&BitConverter.ToInt16(analog,8*32+14)==2,"mag source offset/300 nT count gain");
                Need(mask.Skip(15).All(v=>v==0),"invalid status did not end held measurements");
            }else Need(BitConverter.ToInt16(analog,2)==4096,"unsynchronized fallback lost its actual first AG source");
            string metadata=File.ReadAllText(Path.Combine(folder,"analogin_encoding.txt"));
            Need(metadata.Contains("np_byte_zero_sample_grid_locked="+locked)&&metadata.Contains("native_sensor_conversion_phase_locked=false"),"grid/native phase provenance conflated");
            Need(locked?metadata.Contains("first_retained_AG_source_offset_us=4750")&&metadata.Contains("final_capture_generation=7"):metadata.Contains("NP offset and UTC unknown"),"source-origin provenance missing");
        }
        string sparse=Path.Combine(root,"expiry_grid");Directory.CreateDirectory(sparse);
        var stale=new NpSdImuExport(sparse);stale.Feed(Timed(1,1000,1000,1000));stale.Complete(hz,8000000,new NpSdImuGridOrigin(1000,0,hz,9,"fixture-only release"));stale.Dispose();
        var valid=File.ReadAllBytes(Path.Combine(sparse,"analogin_validity.dat"));
        Need(valid.Length==313&&valid[125]==3&&valid[126]==2&&valid[250]==2&&valid[251]==0,"held AG100ms/MAG200ms expiry policy");
        string absent=Path.Combine(root,"absent_grid");Directory.CreateDirectory(absent);var missing=new NpSdImuExport(absent);var zero=Block();Array.Clear(zero,396,116);missing.Feed(zero);missing.Complete(hz,extent);missing.Dispose();
        Need(File.ReadAllBytes(Path.Combine(absent,"analogin_validity.dat")).All(v=>v==0)&&File.ReadAllBytes(Path.Combine(absent,"analogin.dat")).Where((v,i)=>i%2==1).All(v=>v==0x80),"absent native samples became measured zeros");
        string range=Path.Combine(root,"range_grid");Directory.CreateDirectory(range);var clipping=new NpSdImuExport(range);var tooBig=Timed(1,1000,1000,1000);Put(tooBig,444,int.MaxValue);Seal(tooBig);clipping.Feed(tooBig);clipping.Complete(hz,extent);clipping.Dispose();
        byte[] clipped=File.ReadAllBytes(Path.Combine(range,"analogin.dat"));Need(File.ReadAllBytes(Path.Combine(range,"analogin_validity.dat")).All(v=>v==2)&&Enumerable.Range(1,6).All(word=>BitConverter.ToInt16(clipped,word*2)==short.MinValue),"SD unrepresentable group policy differs from existing SD exporter");
        foreach(int physical in new[]{3750,-3750,6250,-6250}){
            string half=Path.Combine(root,"half_"+physical);Directory.CreateDirectory(half);var round=new NpSdImuExport(half);var sample=Timed(1,1000,1000,1000);Put(sample,456,unchecked((uint)physical));Seal(sample);round.Feed(sample);round.Complete(hz,extent);round.Dispose();
            Need(BitConverter.ToInt16(File.ReadAllBytes(Path.Combine(half,"analogin.dat")),8)==(short)Math.Round(physical*164.0/10000,MidpointRounding.AwayFromZero),"exact-half gyro rounding regression");
        }
        string io=Path.Combine(root,"optional_io");Directory.CreateDirectory(io);File.WriteAllBytes(Path.Combine(io,"neural.dat"),new byte[]{3,7,11});Directory.CreateDirectory(Path.Combine(io,"analogin.dat"));
        var failure=new NpSdImuExport(io);failure.Feed(Timed(1,1000,1000,1000));failure.Complete(hz,extent);failure.Dispose();
        Need(!failure.GridComplete&&failure.Error!=null&&File.ReadAllBytes(Path.Combine(io,"neural.dat")).SequenceEqual(new byte[]{3,7,11}),"auxiliary grid I/O failure affected independent neural output");
        string cancelled=Path.Combine(root,"cancelled_grid");Directory.CreateDirectory(cancelled);var cancel=new NpSdImuExport(cancelled);cancel.Feed(Timed(1,1000,1000,1000));bool refused=false;
        try{cancel.Complete(hz,64000000,null,()=>true);}catch(OperationCanceledException){refused=true;}finally{cancel.Dispose();}
        Need(refused&&!File.Exists(Path.Combine(cancelled,"analogin.dat")),"cancelled auxiliary output was published");
        bool invalidOrigin=false;try{new NpSdImuGridOrigin(0,0,hz,0,"unqualified");}catch(ArgumentException){invalidOrigin=true;}Need(invalidOrigin,"zero-generation release accepted");
    }
    static int Main(string[] args){try{Parser();Export(args[0]);Grid(args[0]);Console.WriteLine("PASS "+checks+" SD SNS2 parsing/CRC/geometry/dedup/optional-isolation/1250Hz-grid checks; no hardware.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
