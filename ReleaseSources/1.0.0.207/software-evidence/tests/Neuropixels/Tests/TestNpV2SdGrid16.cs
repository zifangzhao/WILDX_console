using System;
using System.IO;
using System.Linq;
using System.Text;
using USB512_interface;

internal static partial class NpV2SdDatExportTests {
    const string Grid16Uid="001A0004434250062039314E";
    const ulong Grid16Token=0x1020304050607080UL;
    static byte[] Grid16Clock(uint hz,byte mask,int[] phases,out byte[] companion,out NpFirstSyncReceipt receipt){
        byte[] r=NpFirstSyncWireTests.Fixture(2,false,hz,mask);Put(r,12,(2U<<8)|47U);Put(r,16,7);r[50]=1;
        for(int p=0;p<3;p++)if((mask&(1<<p))!=0){int at=64+p*20;PutQ(r,at,(ulong)((1600-phases[p])%1600));Buffer.BlockCopy(BitConverter.GetBytes((ushort)phases[p]),0,r,at+12,2);}
        PutQ(r,52,BitConverter.ToUInt64(r,64));Array.Clear(r,132,192);r[132]=1;r[133]=5;r[134]=64;
        for(int at=136;at<=148;at+=4)Put(r,at,UInt32.MaxValue);PutQ(r,152,0);Put(r,160,1250);Put(r,164,1);PutQ(r,168,0x8000000000000000UL);
        Put(r,324,NpFirstSyncWire.Crc(r,324));receipt=NpFirstSyncWire.Parse(r);
        var clock=new byte[512];Encoding.ASCII.GetBytes("N2SDUTC2").CopyTo(clock,0);Put(clock,8,2);Put(clock,12,7);Put(clock,72,hz);Put(clock,76,1000);Seal(clock);
        clock=NpFirstSyncSd.BindSector(clock,r,2,Grid16Uid,Grid16Token);
        var anchor=new byte[64];Put(anchor,0,0x5241504e);Put(anchor,4,1);Put(anchor,8,57);Put(anchor,12,(2U<<8)|31U);Put(anchor,16,9);
        Put(anchor,20,1000);Put(anchor,24,1000);Put(anchor,28,128000000);Put(anchor,32,128000004);Put(anchor,36,128000000);
        Put(anchor,40,hz*1600);Put(anchor,52,hz);Put(anchor,56,1000);Put(anchor,60,NpFirstSyncWire.Crc(anchor,60));
        companion=NpFineClockWire.CompanionFineCounter64(clock,anchor,2,7,hz,Grid16Uid,Grid16Token);return clock;
    }
    static void MakeGrid16(string source,string target,uint baseVersion,uint hz,byte mask,int[] phases,string fault=null){
        using(var input=File.OpenRead(source))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.ReadWrite)){
            var d=SectorAt(input,Start+3);var f=SectorAt(input,Start+4);uint payload=BitConverter.ToUInt32(d,60),k=NpV2SdGrid.K(mask,hz),stride=364+k;
            var config=mask==3?null:new byte[2048];if(config!=null){input.Position=(Start+8L)*512;Check(input.Read(config,0,2048)==2048,"Configuration truncated");}
            Put(d,8,16);Put(d,64,payload+2*stride);Put(d,68,(uint)RoundUp(baseVersion==11?payload+2*stride:RoundUp(payload+2*stride,BitConverter.ToUInt32(d,128)),16384));
            Encoding.ASCII.GetBytes("N2GRP016").CopyTo(d,208);
            foreach(var x in new[]{Tuple.Create(216,1U),Tuple.Create(220,baseVersion),Tuple.Create(224,0U),Tuple.Create(228,k),Tuple.Create(232,stride),Tuple.Create(236,4U),Tuple.Create(240,1U),Tuple.Create(244,2U),Tuple.Create(248,1250U)})Put(d,x.Item1,x.Item2);
            Buffer.BlockCopy(d,8,f,8,92);Buffer.BlockCopy(d,208,f,208,48);Put(f,116,payload+2*stride);PutQ(f,256,2*stride);PutQ(f,264,2*k);
            Put(f,272,0);Put(f,284,2);Put(f,372,2*k-2);PutQ(f,376,UInt64.MaxValue);PutQ(f,384,8);
            if(fault=="first_source"){phases=(int[])phases.Clone();phases[0]=(phases[0]+1)%1600;}
            byte[] companion;NpFirstSyncReceipt receipt;byte[] clock=Grid16Clock(hz,mask,phases,out companion,out receipt);
            var binding=NpImuGridWriter.PhaseBinding(Grid16Uid,Grid16Token,receipt);
            var phase=new NpImuGridPhaseRecord{Uid=binding.Uid,Session=binding.Session,CaptureGeneration=binding.CaptureGeneration,SourceEpoch=binding.SourceEpoch,CounterDomain=binding.CounterDomain,Token=binding.Token,
                FirstAcceptedSlot=0,LowerNumerator=0,UpperNumerator=32,Denominator=1,MaximumJitter=64,Flags=3};
            NpImuGridPhase.Pack(phase).CopyTo(f,288);
            if(fault=="phase_crc")f[368]^=1;if(fault=="phase_absent")Array.Clear(f,288,84);
            if(fault=="context_crc")companion[508]^=1;if(fault=="context_absent")Array.Clear(companion,0,512);
            if(fault=="grid_count")Put(f,284,3);if(fault=="grid_flags")Put(f,272,8);if(fault=="grid_fault")Put(f,272,4);
            if(fault=="all_pad"){Put(f,284,0);Put(f,372,2*k);PutQ(f,384,0);}
            if(fault!=null&&fault.StartsWith("schema2")){
                Put(d,216,2);Put(f,216,2);PutQ(f,392,fault=="schema2_known"?3UL:0UL);Put(f,400,1);
                if(fault=="schema2_bad_pending")Put(f,400,2);if(fault=="schema2_bad_reserved")f[408]=1;
            }
            if(fault=="physical_count")PutQ(f,256,2*stride-1);if(fault=="aux_count")PutQ(f,264,2*k-1);
            if(fault=="stride")Put(d,232,stride+1);if(fault=="reserved")d[256]=1;if(fault=="footer_reserved")f[392]=1;
            if(fault=="limit")Put(d,64,UInt32.MaxValue);if(fault=="outer_crc")d[508]^=1;
            if(fault!="outer_crc")Seal(d);Seal(f);
            if(fault=="footer_crc")f[508]^=1;
            output.SetLength((payload+2L*stride)*512);WriteAt(output,Start+3,d);WriteAt(output,Start+4,f);WriteAt(output,Start+5,clock);WriteAt(output,Start+6,companion);
            if(config!=null)WriteAt(output,Start+8,config);
            output.Position=payload*512L;
            for(uint g=0;g<2;g++){
                for(uint n=0;n<4;n++){uint seq=g*4+n;input.Position=payload*512L+seq*46592L;var block=new byte[46592];Check(input.Read(block,0,block.Length)==block.Length,"NN fixture truncated");
                    if(fault=="nn"&&seq==4)block[92]^=1;output.Write(block,0,block.Length);}
                for(uint n=0;n<k;n++){
                    byte[] sector;
                    if(n!=0||fault=="all_pad")sector=NpV2GridSector.PackSd16Empty(9,g*4+3);
                    else {var rows=Enumerable.Range(0,4).Select(i=>new[]{1000000,-500000,0,12500,0,0,42000,0,0}).ToArray();
                        sector=NpV2GridSector.Pack(9,g*4+3,NpImuGridWire.Pack(new NpImuGridBatch{CaptureGeneration=9,FirstSlot=g*4,SlotCount=4,ObservedMask=15,AgValid=15,MagValid=15,Rows=rows}));}
                    if(g==0&&n==0){if(fault=="aux_crc")sector[28]^=1;if(fault=="inner_crc")sector[76]^=1;if(fault=="parent"){PutQ(sector,16,99);Put(sector,28,Crc(sector,28));}}
                    output.Write(sector,0,512);
                }
            }
            CatalogEntry(output,f);
            if(fault=="aux_truncated")output.SetLength(output.Length-128);
        }
    }
    sealed class Grid16IoCard:Stream {
        readonly Stream source;readonly string output,target;bool injected;
        internal Grid16IoCard(Stream source,string output,string target){this.source=source;this.output=output;this.target=target;}
        public override int Read(byte[] b,int at,int count){if(!injected&&Directory.Exists(output)){Directory.CreateDirectory(Path.Combine(output,target));injected=true;}return source.Read(b,at,count);}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}
        public override long Length{get{return source.Length;}}public override long Position{get{return source.Position;}set{source.Position=value;}}
        public override long Seek(long x,SeekOrigin o){return source.Seek(x,o);}public override void Flush(){}public override void SetLength(long n){throw new NotSupportedException();}public override void Write(byte[] b,int a,int n){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(disposing)source.Dispose();base.Dispose(disposing);}
    }
    static NpV2SdDatExport.Result ExportGrid16(string image,string output,NpCalibrationSet calibration=null){
        using(var card=new CapacityImage(File.OpenRead(image),65536L*512))return calibration==null?NpV2SdDatExport.Export(card,Start,output):NpV2SdDatExport.ExportCalibrated(card,Start,output,calibration);
    }
    static void NativeGrid16(string root,string native){
        var d=File.ReadAllBytes(Path.Combine(native,"descriptor.bin"));var f=File.ReadAllBytes(Path.Combine(native,"footer.bin"));
        NpV2SdDatExport.ValidateMetadata(d,f,8192,65536L*512);checks++;
        byte[] raw=NpV2GridSector.ReadSd16Data(File.ReadAllBytes(Path.Combine(native,"data.bin")),12,7);
        Check(NpImuGridWire.Parse(raw).SlotCount==12,"Native12-slot wrapper parity");
        Check(NpV2GridSector.ReadSd16Data(File.ReadAllBytes(Path.Combine(native,"empty.bin")),12,7).Length==0,"Native SD-only empty wrapper parity");
        var phase=NpImuGridPhase.Parse(File.ReadAllBytes(Path.Combine(native,"phase.bin")));Check(phase.Flags==0&&phase.CaptureGeneration==12,"Unknown native phase was qualified");
    }
    static void Grid16(string root,string native){
        NativeGrid16(root,native);
        GridAppend(root);
        foreach(uint hz in new[]{7500U,15000U,20000U,30000U})foreach(byte mask in new byte[]{1,3,7}){
            string name="g16_"+hz+"_"+mask,legacy=Path.Combine(root,name+".img"),baseline=Path.Combine(root,name+"_old"),image=Path.Combine(root,name+"_new.img"),output=Path.Combine(root,name);
            MakeImage(legacy,hz,null,0,mask,mask==7?719U:UInt32.MaxValue);AddImu(legacy,null);NpV2SdDatExport.Result before;using(var card=File.OpenRead(legacy))before=NpV2SdDatExport.Export(card,Start,baseline);
            MakeGrid16(legacy,image,11,hz,mask,before.Phases);var result=ExportGrid16(image,output);SameDat(baseline,output,before.Rows.Length);
            Check(result.ScheduledImuGrid&&result.RecordingComplete&&result.CommittedBlocks==8&&result.CommittedPhysicalSectors==2*(364+NpV2SdGrid.K(mask,hz)),"SD16 NP/physical count confusion");
            using(var card=new CapacityImage(File.OpenRead(image),65536L*512)){
                var catalog=Catalog(card);Check(catalog.Count==1&&(bool)RecordValue(catalog[0],"V2",true)&&(bool)RecordValue(catalog[0],"Downloadable",true)&&
                    (ulong)RecordValue(catalog[0],"PhysicalSectors")==result.CommittedPhysicalSectors,"SD16 catalog classification/physical extent lost");
            }
            Check(result.ImuAnalogGridNpLocked&&result.ImuGridSourceWindowVerified&&result.ImuAnalogGridRows==8,"Bound SD16 retained grid phase/window unavailable");
            Check(new FileInfo(Path.Combine(output,"analogin.dat")).Length==8*32&&new FileInfo(Path.Combine(output,"analogin_validity.dat")).Length==8,"Two analog writers or wrong held-grid extent");
            Check(File.Exists(Path.Combine(output,"sensors.csv"))&&result.SensorSnapshots==8,"Native sensor export lost");
            Check(File.ReadAllBytes(Path.Combine(output,"sensors.csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(baseline,"sensors.csv")))&&
                File.ReadAllBytes(Path.Combine(output,"imu_sd_records.sns2")).SequenceEqual(File.ReadAllBytes(Path.Combine(baseline,"imu_sd_records.sns2"))),"SD16 changed native sensor identities/physical values");
            Check(!File.Exists(Path.Combine(output,"imu_fine_timing_status.json")),"Fine snapshot rebuild overwrote scheduled grid");
            Check(File.ReadAllBytes(image).Skip((int)((Start+3)*512)).Take(512).SequenceEqual(File.ReadAllBytes(Path.Combine(output,"descriptor.bin"))),"Original descriptor was normalized");
            Check(!Directory.GetFiles(output,"*.raw").Any()&&!Directory.GetFiles(output,"*.lane8").Any(),"Neural raw retention introduced");
            if(hz==20000&&mask==3){
                foreach(string fault in new[]{"phase_crc","phase_absent","context_crc","context_absent","grid_count","grid_flags","grid_fault","aux_crc","inner_crc","parent","aux_truncated","first_source","all_pad"}){
                    string bad=image+fault,badOut=output+fault;MakeGrid16(legacy,bad,11,hz,mask,before.Phases,fault);var r=ExportGrid16(bad,badOut);SameDat(baseline,badOut,2);
                    Check(r.RecordingComplete&&!r.ImuAnalogGridNpLocked&&(r.OptionalImuError!=null||fault=="first_source"),"Optional grid fault gated NN or stayed qualified: "+fault);
                }
                foreach(string fault in new[]{"physical_count","aux_count","stride","reserved","footer_reserved","limit","outer_crc","footer_crc","nn"}){
                    string bad=image+fault,badOut=output+fault;MakeGrid16(legacy,bad,11,hz,mask,before.Phases,fault);bool rejected=false;
                    try{ExportGrid16(bad,badOut);}catch(InvalidDataException){rejected=true;}Check(rejected,"NN/geometry corruption accepted: "+fault);
                }
                string gain=Path.Combine(root,"unity_gain.csv");File.WriteAllLines(gain,new[]{"12345"}.Concat(Enumerable.Range(0,384).Select(i=>i+",1")));
                var calibration=new NpCalibrationSet();calibration.Set(1,new NpAdcCalibration(gain,null,"12345"));string calibrated=output+"cal";
                ExportGrid16(image,calibrated,calibration);SameDat(output,calibrated,2);Check(File.Exists(Path.Combine(calibrated,NpCalibrationSet.ManifestName)),"Calibration provenance lost");
                string oldOrigin=output+"old_origin";using(var card=new CapacityImage(File.OpenRead(image),65536L*512)){
                    var r=NpV2SdDatExport.ExportWithImuOrigin(card,Start,oldOrigin,new NpSdImuGridOrigin(0,0,1,1,"stale old DTO",UInt64.MaxValue));
                    Check(r.RecordingComplete&&r.ImuAnalogGridNpLocked,"Old HAL-ms DTO gated SD16 NN or replaced scheduled origin");}
                SameDat(output,oldOrigin,2);
                foreach(string tail in new[]{"schema2_known","schema2_unknown","schema2_bad_pending","schema2_bad_reserved"}){
                    string tailImage=image+tail,tailOut=output+tail;MakeGrid16(legacy,tailImage,11,hz,mask,before.Phases,tail);var r=ExportGrid16(tailImage,tailOut);SameDat(output,tailOut,2);
                    Check(r.RecordingComplete&&r.ImuAnalogGridNpLocked&&r.ImuGridSourceWindowVerified&&r.ImuGridMetadataSchema==2,"Optional tail accounting changed retained phase/NN");
                    bool valid=!tail.StartsWith("schema2_bad");Check(r.ImuGridTailStatsValid==valid,"Tail field validity was guessed");
                    if(valid)Check(r.ImuGridKnownUnsavedRows==(tail=="schema2_known"?3UL:0UL)&&r.ImuGridPendingRecordRowsUnknown==true,"Known unsaved lower bound / pending unknown conflated");
                    else Check(r.ImuGridKnownUnsavedRows==null&&r.ImuGridPendingRecordRowsUnknown==null&&r.ImuGridTailError!=null,"Invalid optional tail was reported as zero");
                }
                string bridge=output+"bridge";using(var card=new CapacityImage(File.OpenRead(image),65536L*512))Bridge(card,Start,BitConverter.ToUInt32(File.ReadAllBytes(Path.Combine(output,"footer.bin")),116),bridge,null,null);
                SameDat(output,bridge,2);Check(File.ReadAllText(Path.Combine(bridge,"recording_settings.json")).Contains("scheduled"),"Bridge metadata lost scheduled-grid semantics");
                byte[] analog=File.ReadAllBytes(Path.Combine(bridge,"analogin.dat"));var inputs=new[]{File.ReadAllBytes(Path.Combine(bridge,"neuropixels_probe1_ap.dat")),File.ReadAllBytes(Path.Combine(bridge,"neuropixels_probe2_ap.dat"))};
                CE32_console.NpSdDatMerge.Export(bridge,true,3,(int)hz,()=>false);
                byte[] merged=File.ReadAllBytes(Path.Combine(bridge,"neuropixels_ap.dat"));int rows=inputs.Min(b=>b.Length/768);
                Check(merged.Length==rows*1536&&Enumerable.Range(0,rows).All(row=>Enumerable.Range(0,2).All(p=>merged.Skip(row*1536+p*768).Take(768).SequenceEqual(inputs[p].Skip(row*768).Take(768)))),"SD16 merged physical probe order changed");
                Check(analog.SequenceEqual(File.ReadAllBytes(Path.Combine(bridge,"analogin.dat"))),"DAT merge rewrote scheduled IMU");
                foreach(string target in new[]{"analogin.dat","imu_grid_sectors.bin","imu_grid_status.txt"}){
                    string badOut=output+"io_"+target;NpV2SdDatExport.Result r;
                    using(var card=new Grid16IoCard(new CapacityImage(File.OpenRead(image),65536L*512),badOut,target))r=NpV2SdDatExport.Export(card,Start,badOut);
                    SameDat(baseline,badOut,2);Check(r.RecordingComplete&&!r.ImuAnalogGridNpLocked&&r.OptionalImuError!=null,"Optional grid output IO gated NN or stayed qualified: "+target);
                }
            }
        }
        foreach(uint v in new[]{12U,13U}){
            string legacy=Path.Combine(root,"g16au"+v+".img"),baseline=legacy+"base",image=legacy+"new",output=legacy+"out";MakeImage(legacy,20000,null,49152,7,719,v==13);
            NpV2SdDatExport.Result before;using(var card=new CapacityImage(File.OpenRead(legacy),131072L*512))before=NpV2SdDatExport.Export(card,Start,baseline);
            MakeGrid16(legacy,image,v,20000,7,before.Phases);using(var card=new CapacityImage(File.OpenRead(image),131072L*512))NpV2SdDatExport.Export(card,Start,output);SameDat(baseline,output,3);
        }
        Grid16ClosedLoop(root,native);
        Console.WriteLine("PASS {0} SD16 fixed-group/native-golden/NN parity/optional isolation/profile/calibration/old-AU checks; software only",checks);
    }
    static void Grid16ClosedLoop(string root,string nativePath){
        byte[] native=File.ReadAllBytes(Path.Combine(nativePath,"closed_loop_metadata.bin"));
        string legacy=Path.Combine(root,"g16_cl.img"),baseline=legacy+"base",image=legacy+"new",output=legacy+"out";
        MakeImage(legacy,30000,null,0,7);NpV2SdDatExport.Result before;using(var card=File.OpenRead(legacy))before=NpV2SdDatExport.Export(card,Start,baseline);
        MakeGrid16(legacy,image,11,30000,7,before.Phases);
        using(var card=new FileStream(image,FileMode.Open,FileAccess.ReadWrite)){
            var d=SectorAt(card,Start+3);var f=SectorAt(card,Start+4);var config=new byte[2048];card.Position=(Start+8L)*512;Check(card.Read(config,0,2048)==2048,"CL configuration truncated");
            Buffer.BlockCopy(native,1888,config,1888,160);Put(config,1908,7);Put(config,2044,Crc(config.Skip(1888).Take(156).ToArray(),156));
            Put(d,76,Crc(config,2048));Put(d,96,143);Put(d,132,BitConverter.ToUInt32(config,1904));Put(d,136,11);Put(d,140,0x314d4c43);
            Buffer.BlockCopy(d,8,f,8,92);Buffer.BlockCopy(native,2048,f,144,64);Put(f,160,7);Put(f,204,Crc(f.Skip(144).Take(60).ToArray(),60));Seal(d);Seal(f);
            WriteAt(card,Start+3,d);WriteAt(card,Start+4,f);WriteAt(card,Start+8,config);
            for(uint seq=0;seq<8;seq++){
                long at=Payload*512L+(seq*91+seq/4)*512L;card.Position=at;var h=new byte[512];Check(card.Read(h,0,512)==512,"CL NN header truncated");
                Put(h,52,BitConverter.ToUInt32(h,52)|1024);Put(h,252,BitConverter.ToUInt32(d,132));
                if(seq==0){Buffer.BlockCopy(native,2296,h,184,64);Put(h,192,7);Put(h,244,Crc(h.Skip(184).Take(60).ToArray(),60));}
                card.Position=at;card.Write(h,0,512);
            }
        }
        var result=ExportGrid16(image,output);SameDat(baseline,output,3);
        Check(result.RecordingComplete&&File.ReadAllLines(Path.Combine(output,"closed_loop_events.csv")).Length==2,"SD16 direct CL configuration/events lost");
        Check(File.ReadAllText(Path.Combine(output,"closed_loop_status.txt")).Contains("Dropped: 1"),"SD16 CL summary lost");
    }
}
