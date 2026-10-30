using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using USB512_interface;

internal static partial class NpV2SdDatExportTests {
    static void MakeGridAppend(string source,string target,uint[] order,uint capacity=12,string fault=null){
        using(var input=File.OpenRead(source))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.ReadWrite)){
            var d=SectorAt(input,Start+3);var f=SectorAt(input,Start+4);uint payload=BitConverter.ToUInt32(d,60),k=BitConverter.ToUInt32(d,228),stride=364+k;
            uint mask=BitConverter.ToUInt32(d,24),hz=BitConverter.ToUInt32(d,16),ticks=BitConverter.ToUInt32(d,44),n=(uint)order.Length*4;
            uint p=0,h=order.Length==0?0:order.Max()*4+4;foreach(uint g in order.OrderBy(x=>x))if(g*4==p)p+=4;
            Put(d,216,3);Put(d,224,1);Put(d,64,payload+capacity/4*stride);
            var config=mask==3?null:new byte[2048];if(config!=null){input.Position=(Start+8L)*512;Check(input.Read(config,0,2048)==2048,"Append config truncated");Put(config,44,capacity);Put(d,76,Crc(config,2048));}
            Buffer.BlockCopy(d,8,f,8,92);Buffer.BlockCopy(d,208,f,208,48);
            Put(f,100,p==n&&n==h?3U:4U);Put(f,104,p==n&&n==h?0U:6U);Put(f,108,n);Put(f,112,n/4);Put(f,116,payload+n/4*stride);PutQ(f,120,(ulong)n*ticks);
            PutQ(f,256,(ulong)n/4*stride);PutQ(f,264,(ulong)n/4*k);Put(f,284,n/4);Put(f,372,n/4*(k-1));PutQ(f,384,n);
            Put(f,408,p);Put(f,412,h);Put(f,416,h);Put(f,420,1);
            if(fault=="unknown_admitted"){Put(f,100,4);Put(f,104,6);Put(f,416,UInt32.MaxValue);Put(f,420,0);}
            if(fault=="bad_prefix")Put(f,408,p==0?4:p-4);if(fault=="bad_high")Put(f,412,h+4);
            if(fault=="bad_admitted")Put(f,416,h-1);if(fault=="bad_unknown"){Put(f,420,0);Put(f,416,0);}
            if(fault=="coverage_flags")Put(f,420,2);if(fault=="reserved")f[424]=1;
            if(fault=="complete_hole"){Put(f,100,3);Put(f,104,0);}
            Seal(d);Seal(f);output.SetLength((payload+n/4*(long)stride)*512);
            WriteAt(output,Start+3,d);WriteAt(output,Start+4,f);WriteAt(output,Start+5,SectorAt(input,Start+5));WriteAt(output,Start+6,SectorAt(input,Start+6));
            if(config!=null)WriteAt(output,Start+8,config);
            output.Position=payload*512L;
            for(int physical=0;physical<order.Length;physical++){
                uint group=order[physical];
                for(uint at=0;at<4;at++){
                    uint seq=group*4+at;input.Position=payload*512L+(seq%8/4*stride)*512L+(seq%4)*46592L;
                    var model=new byte[46592];Check(input.Read(model,0,model.Length)==model.Length,"Append NN source truncated");
                    byte[] block=seq<8?model:(byte[])Fixture.Invoke(null,new object[]{(ulong)seq,(ulong)seq*ticks,mask,mask,hz,mask==3,mask==7?719U:UInt32.MaxValue});
                    if(seq>=8){Put(block,52,BitConverter.ToUInt32(model,52));Buffer.BlockCopy(model,NpSdImuSnapshot.Offset,block,NpSdImuSnapshot.Offset,NpSdImuSnapshot.RecordBytes);}
                    PutQ(block,24,seq);PutQ(block,32,(ulong)seq*ticks);
                    if(fault=="backward_tick"&&physical==1&&at==0)PutQ(block,32,0);
                    if(fault=="nn_corrupt"&&physical==1&&at==0)block[512]^=1;
                    output.Write(block,0,block.Length);
                }
                for(uint a=0;a<k;a++){
                    byte[] aux=NpV2GridSector.PackSd16Empty(9,group*4+3);
                    if(a==0){ulong slot=(ulong)physical*4;if(fault=="aux_order"&&physical==1)slot=0;
                        var rows=Enumerable.Range(0,4).Select(i=>new[]{1000000+physical*20000,-500000,0,12500,0,0,42000,0,0}).ToArray();
                        aux=NpV2GridSector.Pack(9,group*4+3,NpImuGridWire.Pack(new NpImuGridBatch{CaptureGeneration=9,FirstSlot=slot,SlotCount=4,ObservedMask=15,AgValid=15,MagValid=15,Rows=rows}));}
                    if(fault=="aux_parent"&&physical==1){PutQ(aux,16,3);Put(aux,28,Crc(aux,28));}
                    output.Write(aux,0,512);
                }
            }
        }
    }
    static NpV2SdDatExport.Result ExportGridAppend(string image,string output,bool incomplete=false,Action<int> progress=null,Action<NpV2SdDatExport.ExportProgress> stages=null){
        using(var card=new CapacityImage(File.OpenRead(image),65536L*512)){
            if(incomplete&&stages==null)return CE32_console.NpCe64Export.ExportCalibrated(card,Start,0,output,null,progress,null);
            return stages!=null?NpV2SdDatExport.ExportCommittedWithStages(card,Start,output,0,null,progress,null,stages):NpV2SdDatExport.Export(card,Start,output);
        }
    }
    static void GridAppend(string root){
        int beforeChecks=checks;
        foreach(uint hz in new[]{7500U,15000U,20000U,30000U})foreach(byte mask in new byte[]{1,3,7}){
            string stem=Path.Combine(root,"append16_"+hz+"_"+mask),legacy=stem+".legacy",baseOut=stem+".base",grid=stem+".grid";
            MakeImage(legacy,hz,null,0,mask,mask==7?719U:UInt32.MaxValue);AddImu(legacy,null);
            NpV2SdDatExport.Result baseline;using(var card=File.OpenRead(legacy))baseline=NpV2SdDatExport.Export(card,Start,baseOut);
            MakeGrid16(legacy,grid,11,hz,mask,baseline.Phases);
            string ordered=stem+".ordered",reordered=stem+".reordered";MakeGridAppend(grid,ordered,new uint[]{0,1,2});MakeGridAppend(grid,reordered,new uint[]{0,2,1});
            var a=ExportGridAppend(ordered,ordered+"out");var stages=new List<NpV2SdDatExport.ExportProgress>();var percentages=new List<int>();
            var b=ExportGridAppend(reordered,reordered+"out",false,percentages.Add,stages.Add);SameDat(ordered+"out",reordered+"out",baseline.Rows.Length);
            Check(b.IndexedGridAppend&&b.RecordingComplete&&b.LogicalPrefixBlocks==12&&b.LogicalHighWaterBlocks==12&&b.AdmittedHighWaterKnown&&b.AdmittedHighWaterBlocks==12&&b.MissingStoredBlocks==0,"Indexed complete coverage");
            Check(b.ImuAnalogGridRows==12&&b.ImuAnalogGridNpLocked&&File.ReadAllBytes(Path.Combine(ordered+"out","analogin.dat")).SequenceEqual(File.ReadAllBytes(Path.Combine(reordered+"out","analogin.dat"))),"AUX was sorted with NN");
            Check(stages.First().Stage.StartsWith("Indexing")&&stages.Any(x=>x.Stage.StartsWith("Downloading"))&&stages.Last().Stage.StartsWith("Finalizing")&&percentages.Last()==100&&percentages.Take(percentages.Count-1).All(x=>x<100),"Stage/progress premature completion");
            Check(File.ReadAllLines(Path.Combine(reordered+"out","neural_storage_index.csv")).Skip(1).Select(x=>x.Split(',')[1]).SequenceEqual(new[]{"0","2","1"}),"Logical index does not retain original physical positions");
            if(hz!=20000||mask!=7)continue;
            foreach(string mode in new[]{"hole","prefix_missing","unknown_admitted"}){
                uint[] order=mode=="prefix_missing"?new uint[]{2}:new uint[]{0,2};string image=stem+mode,outPath=image+"out";MakeGridAppend(grid,image,order,12,mode=="unknown_admitted"?mode:null);
                var r=ExportGridAppend(image,outPath,true);Check(!r.RecordingComplete&&r.MissingStoredBlocks==(mode=="prefix_missing"?8U:4U)&&r.SourceGapCount==0&&r.DroppedBlocks==0,"Storage holes became capture loss or complete");
                Check(r.Segments.Count==order.Length&&r.Segments.Select(x=>x.FirstLogicalBlock).SequenceEqual(order.Select(x=>x*4))&&r.Segments.All(x=>x.StoredBlocks==4),"Original stored segment IDs lost");
                if(mode=="prefix_missing")Check(File.ReadAllText(Path.Combine(outPath,"np2_sd_export_status.txt")).Contains("first_source_tick="+(r.Segments[0].SourceStartTick+(1600-(r.Segments[0].SourceStartTick+(ulong)r.Phases[0])%1600)%1600)),"Missing prefix was reported with a fabricated zero-origin first row");
                for(int p=0;p<r.Rows.Length;p++){var expected=new List<byte>();foreach(uint g in order){string single=stem+mode+g+".single",singleOut=single+"out";if(p==0){MakeGridAppend(grid,single,new[]{g});ExportGridAppend(single,singleOut,true);}expected.AddRange(File.ReadAllBytes(Path.Combine(singleOut,"neuropixels_probe"+(p+1)+"_ap.dat")));}
                    Check(expected.SequenceEqual(File.ReadAllBytes(Path.Combine(outPath,"neuropixels_probe"+(p+1)+"_ap.dat"))),"Rows joined across a missing stored group");}
                Check(File.ReadAllText(Path.Combine(outPath,"first_sample_timestamps.json")).Contains("segments"),"DAT source segment metadata missing");
                var perProbe=Enumerable.Range(1,r.Rows.Length).Select(p=>File.ReadAllBytes(Path.Combine(outPath,"neuropixels_probe"+p+"_ap.dat"))).ToArray();
                CE32_console.NpSdDatMerge.Export(outPath,true,mask,(int)hz,()=>false);
                long common=r.Segments.Sum(x=>x.Rows.Min());Check(new FileInfo(Path.Combine(outPath,"neuropixels_ap.dat")).Length==common*768*r.Rows.Length,"Merged missing-group geometry");
                using(var expected=new MemoryStream()){
                    foreach(var part in r.Segments)for(long row=0;row<part.Rows.Min();row++)for(int p=0;p<r.Rows.Length;p++)
                        expected.Write(perProbe[p],checked((int)((part.RowOffsets[p]+row)*768)),768);
                    Check(expected.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(outPath,"neuropixels_ap.dat"))),"Merged rows crossed storage segments or changed physical probe order");
                }
                Check(File.ReadAllText(Path.Combine(outPath,"dat_merge.json")).Contains("source_start_tick"),"Merged logical segments lost");
                if(mode=="unknown_admitted")Check(!r.AdmittedHighWaterKnown&&r.AdmittedHighWaterBlocks==UInt32.MaxValue,"Unknown admitted suffix fabricated");
            }
            foreach(string fault in new[]{"bad_prefix","bad_high","bad_admitted","bad_unknown","coverage_flags","reserved","complete_hole","backward_tick"}){
                string image=stem+fault;MakeGridAppend(grid,image,new uint[]{0,2},12,fault);bool refused=false;
                try{ExportGridAppend(image,image+"out",true);}catch(InvalidDataException){refused=true;}Check(refused,"Invalid append admission: "+fault);
            }
            string duplicate=stem+"duplicate";MakeGridAppend(grid,duplicate,new uint[]{0,0,2});bool duplicateRefused=false;
            try{ExportGridAppend(duplicate,duplicate+"out",true);}catch(InvalidDataException){duplicateRefused=true;}Check(duplicateRefused,"Duplicate logical IDs accepted");
            foreach(string fault in new[]{"aux_order","aux_parent"}){string image=stem+fault;MakeGridAppend(grid,image,new uint[]{0,2,1},12,fault);var r=ExportGridAppend(image,image+"out");SameDat(ordered+"out",image+"out",3);Check(r.RecordingComplete&&!r.ImuAnalogGridNpLocked&&r.OptionalImuError!=null,"Optional AUX defect changed NN or stayed qualified");}
        }
        Console.WriteLine("PASS "+(checks-beforeChecks)+" SD16 schema3 logical-index/physical-AUX/partial-segment/merged-row/progress checks; software only");
    }
}
