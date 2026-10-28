using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using USB512_interface;

internal static partial class NpV2SdDatExportTests
{
    private static void ImuSdCountRounding(string root)
    {
        foreach(int physical in new[]{3750,-3750,6250,-6250,2000000,-2000000}){
            var snapshot=new byte[164];Put(snapshot,32,3);Put(snapshot,12,1000);Put(snapshot,40,1);Put(snapshot,44,1);Put(snapshot,52,1000);Put(snapshot,56,1000);
            Put(snapshot,68,1000000);Put(snapshot,80,unchecked((uint)physical));Put(snapshot,92,450);
            string path=Path.Combine(root,"existing_sd_rounding_"+physical+".dat");uint valid;
            using(var output=File.Create(path))CE32_console.NpCe64Export.SensorAnalog(new[]{snapshot},1000,1,null,output,new StringWriter(),null,out valid);
            short actual=BitConverter.ToInt16(File.ReadAllBytes(path),8);
            if(Math.Abs(physical)==2000000)Check(actual==short.MinValue&&valid==2,"Existing SD out-of-range group policy changed");
            else Check(actual==(short)Math.Round(physical*164.0/10000.0,MidpointRounding.AwayFromZero)&&valid==3,"Existing SD exact-half gyro rounding regression");
        }
    }
    private static void AddImu(string path,string fault)
    {
        using(var image=new FileStream(path,FileMode.Open,FileAccess.ReadWrite)){
            var block=new byte[46592];
            for(uint sequence=0;sequence<8;sequence++){
                long at=Payload*512L+sequence*(long)block.Length;
                image.Position=at;Check(image.Read(block,0,block.Length)==block.Length,"IMU fixture truncated");
                uint flags=BitConverter.ToUInt32(block,52)|NpSdImuSnapshot.Flag;
                if(fault=="overlap")flags|=128U;
                if(fault=="raw_cl_overlap")flags|=1024U;
                Put(block,52,flags);
                int sensor=NpSdImuSnapshot.Offset;
                Put(block,sensor,NpSdImuSnapshot.Magic);Put(block,sensor+4,2);Put(block,sensor+8,1);Put(block,sensor+12,3);
                Put(block,sensor+16,1000+sequence*5);Put(block,sensor+20,sequence+1);Put(block,sensor+24,sequence+1);
                Put(block,sensor+32,1000+sequence*5);Put(block,sensor+36,1000+sequence*5);Put(block,sensor+44,sequence*128);
                Put(block,sensor+48,1000000+sequence);Put(block,sensor+52,unchecked((uint)-500000));Put(block,sensor+60,12500);
                Put(block,sensor+72,42000);Put(block,sensor+96,0xd8);
                Put(block,508,NpSdImuSnapshot.Crc(block,sensor,NpSdImuSnapshot.Bytes));
                if(fault=="sensor_crc"&&sequence==4)block[500]^=1;
                if(fault=="neural_identity"&&sequence==4)block[92]^=1;
                if(fault=="absent")Array.Clear(block,sensor,NpSdImuSnapshot.RecordBytes);
                image.Position=at;image.Write(block,0,block.Length);
            }
        }
    }
    private static void ImuSnapshots(string root)
    {
        foreach(uint mask in new uint[]{3U,7U}){
            uint d8=mask==7?719U:uint.MaxValue;
            string baselineImage=Path.Combine(root,"imu_baseline_"+mask+".img"),baseline=Path.Combine(root,"imu_baseline_"+mask);
            MakeImage(baselineImage,20000,null,0,mask,d8);
            using(var card=File.OpenRead(baselineImage)){
                var result=NpV2SdDatExport.Export(card,Start,baseline);
                Check(!result.SensorSnapshotsAdvertised&&!File.Exists(Path.Combine(baseline,"sensors.csv")),"Legacy recording gained fabricated sensors");
            }
            foreach(string fault in new[]{"", "sensor_crc", "absent", "neural_identity", "overlap", "raw_cl_overlap"}){
                if(fault=="raw_cl_overlap"&&mask!=7)continue;
                string image=Path.Combine(root,"imu_"+mask+"_"+fault+".img"),output=Path.Combine(root,"imu_"+mask+"_"+fault);
                File.Copy(baselineImage,image);AddImu(image,fault);
                bool mustRefuse=fault=="neural_identity"||fault=="overlap"||fault=="raw_cl_overlap",refused=false;
                NpV2SdDatExport.Result result=null;
                try{using(var card=File.OpenRead(image))result=NpV2SdDatExport.Export(card,Start,output);}
                catch(InvalidDataException){refused=true;}
                Check(refused==mustRefuse,"IMU neural/geometry acceptance changed: "+mask+"/"+fault);
                if(mustRefuse){Check(Directory.GetFiles(output,"*.dat").Length==0,"Invalid neural export was published");continue;}
                SameDat(baseline,output,mask==7?3:2);
                Check(result.SensorSnapshotsAdvertised,"SNS2 flag not reported");
                long expected=fault=="sensor_crc"?7:fault=="absent"?0:8;
                Check(result.SensorSnapshots==expected&&result.DistinctImuObservations==expected,"Sparse sensor count mismatch");
                Check((result.OptionalImuError!=null)==(fault=="sensor_crc"),"Optional CRC error not isolated");
                Check(File.Exists(Path.Combine(output,"sensors.csv"))==(expected!=0),"Missing snapshot generated synthetic samples");
                Check(File.ReadAllText(Path.Combine(output,"np2_sd_export_status.txt")).StartsWith("Export complete: True"),"IMU affected independent neural receipt");
                Check(result.Rows.All(n=>n>0),"IMU removed neural rows");
                ulong rows=(result.ElapsedSourceTicks*1250UL+result.SampleHz*1600UL-1UL)/(result.SampleHz*1600UL);
                byte[] analog=File.ReadAllBytes(Path.Combine(output,"analogin.dat")),validity=File.ReadAllBytes(Path.Combine(output,"analogin_validity.dat"));
                Check(result.ImuAnalogGridComplete&&(ulong)result.ImuAnalogGridRows==rows&&(ulong)analog.Length==rows*32UL&&(ulong)validity.Length==rows,"NP-duration held-grid geometry changed");
                Check(File.ReadAllText(Path.Combine(output,"analogin_encoding.txt")).Contains("NP offset and UTC unknown"),"Legacy origin falsely promoted to a sample lock");
                if(fault=="absent")Check(validity.All(v=>v==0)&&BitConverter.ToInt16(analog,2)==short.MinValue,"Absent sensor data fabricated measured rows");
                else Check(BitConverter.ToInt16(analog,2)==4096&&BitConverter.ToInt16(analog,8)==205&&BitConverter.ToInt16(analog,14)==140,"SD/USB/WILD sensor count gain mismatch");
                if(fault==""||fault=="absent"){
                    string bridge=output+"_bridge";
                    using(var card=File.OpenRead(image))Bridge(card,Start,Payload+8*91,bridge,()=>false,null);
                    SameDat(output,bridge,mask==7?3:2);
                    Check(File.ReadAllBytes(Path.Combine(bridge,"analogin.dat")).SequenceEqual(analog),"bridge altered the held grid");
                    var metadata=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(bridge,"recording_settings.json")));
                    var misc=(Dictionary<string,object>)metadata["misc_output"];var streams=(Dictionary<string,object>)metadata["streams"];
                    Check((bool)misc["present"]&&Convert.ToInt32(misc["sample_rate_hz"])==1250&&Convert.ToInt64(misc["rows"])==(long)rows,"bridge omitted actual auxiliary-file provenance");
                    Check((bool)streams["misc"]&&(bool)streams["imu"]==(fault!="absent")&&Convert.ToString(misc["origin_evidence"]).Contains("Unsynchronized"),"bridge fabricated IMU availability/origin");
                }
            }
        }
        ImuSdCountRounding(root);
        Console.WriteLine("PASS {0} focused SNS2 SD export checks: exact two/three-probe DAT parity, raw-D8 coexistence, sparse/all-zero snapshots, optional CRC isolation, SD count rounding, immutable identity and overlapping-extension rejection; no hardware.",checks);
    }
}
