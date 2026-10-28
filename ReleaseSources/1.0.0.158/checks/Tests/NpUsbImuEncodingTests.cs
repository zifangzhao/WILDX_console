using System;
using System.IO;
using System.Linq;
using System.Reflection;
using USB512_interface;

static class NpUsbImuEncodingTests
{
    static int checks;
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static readonly MethodInfo ScaleMethod=typeof(NpMisc1250).GetMethod("Scale",BindingFlags.NonPublic|BindingFlags.Static);
    static short Scale(int physical,int numerator,int denominator){return (short)ScaleMethod.Invoke(null,new object[]{physical,numerator,denominator});}
    static short Reference(int value,int numerator,int denominator){
        double rounded=Math.Round(value*(double)numerator/denominator,MidpointRounding.AwayFromZero);
        return (short)Math.Max(short.MinValue,Math.Min(short.MaxValue,rounded));
    }
    static void Ratios()
    {
        Need(Scale(1000000,4096,1000000)==4096&&Scale(-1000000,4096,1000000)==-4096,"4096 counts/g gain");
        Need(Scale(8000000,4096,1000000)==32767&&Scale(-8000000,4096,1000000)==-32768,"8 g signed range/clipping");
        Need(Scale(122,4096,1000000)==0&&Scale(123,4096,1000000)==1&&Scale(-123,4096,1000000)==-1,"accel rounding boundary");
        Need(Scale(12500,164,10000)==205&&Scale(-12500,164,10000)==-205,"16.4 counts/dps gain");
        Need(Scale(1250,164,10000)==21&&Scale(-1250,164,10000)==-21,"gyro midpoint rounding");
        Need(Scale(3750,164,10000)==62&&Scale(-3750,164,10000)==-62&&Scale(6250,164,10000)==103&&Scale(-6250,164,10000)==-103,"exact-half gyro floating-point regression");
        Need(Scale(2000000,164,10000)==32767&&Scale(-2000000,164,10000)==-32768,"2000 dps signed range/clipping");
        Need(Scale(42000,1,300)==140&&Scale(-42000,1,300)==-140,"300 nT/count gain");
        Need(Scale(149,1,300)==0&&Scale(150,1,300)==1&&Scale(-150,1,300)==-1&&Scale(450,1,300)==2,"mag midpoint rounding");
        var random=new Random(20261028);
        foreach(var ratio in new[]{new[]{4096,1000000},new[]{164,10000},new[]{1,300}}){
            foreach(int value in new[]{int.MinValue,int.MaxValue,0,-1,1})Need(Scale(value,ratio[0],ratio[1])==Reference(value,ratio[0],ratio[1]),"int32 extrema overflow");
            for(int i=0;i<2000;i++){
                int bound=ratio[0]==4096?8000000:ratio[0]==164?2000000:10000000;
                int value=random.Next(-bound,bound);
                Need(Scale(value,ratio[0],ratio[1])==Reference(value,ratio[0],ratio[1]),"CE64 count reference mismatch");
            }
        }
    }
    static byte[] Snapshot(uint sequence,uint tick)
    {
        var b=new byte[140];Put(b,0,0x31534e53);Put(b,4,1);Put(b,8,1);Put(b,12,3);Put(b,16,tick);
        Put(b,20,sequence);Put(b,24,sequence);Put(b,32,tick);Put(b,36,tick);Put(b,44,sequence*128);
        Put(b,48,sequence*1000000);Put(b,52,unchecked((uint)-500000));Put(b,60,12500);Put(b,72,42000);return b;
    }
    static void Grid(string root)
    {
        string folder=Path.Combine(root,"grid");Directory.CreateDirectory(folder);
        byte[] first=Snapshot(1,1000),keep=(byte[])first.Clone(),second=Snapshot(2,1010);
        using(var sensors=new NpUsbSensors(folder)){
            sensors.FeedSnapshot(first,0,1000);sensors.FeedSnapshot(second,0,1010);
            var display=sensors.GetDisplay();Need(display.Values[0]==2&&display.Values[3]==12.5&&display.Values[6]==42,"physical display units changed");
            sensors.FeedSnapshot(second,0,1220);
            Need(sensors.Valid==0,"stale sensor validity changed");
        }
        Need(first.SequenceEqual(keep),"physical SNS1 source bytes changed");
        var data=File.ReadAllBytes(Path.Combine(folder,"analogin.dat"));
        Need(data.Length==275*32,"1250 Hz output-grid length changed");
        Need(BitConverter.ToInt16(data,2)==4096&&BitConverter.ToInt16(data,4)==-2048&&BitConverter.ToInt16(data,8)==205&&BitConverter.ToInt16(data,14)==140,"sensor word/gain mapping changed");
        Need(BitConverter.ToInt16(data,13*32+2)==8192,"source-time zero-order hold boundary changed");
        Need(BitConverter.ToInt16(data,274*32+2)==short.MinValue&&BitConverter.ToInt16(data,274*32+14)==short.MinValue,"stale sensor values became measured zeros");
        string metadata=File.ReadAllText(Path.Combine(folder,"analogin_encoding.txt"));
        foreach(string fact in new[]{"CE64-equivalent", "not recovered raw", "4096", "16.4", "300", "200 Hz", "not 1250 Hz sensor acquisition", "native_MAG_rate=not encoded"})Need(metadata.Contains(fact),"encoding/profile provenance missing: "+fact);
        string old=Path.Combine(root,"existing");Directory.CreateDirectory(old);string path=Path.Combine(old,"analogin.dat");File.WriteAllBytes(path,new byte[]{17,29,41});
        bool refused=false;try{using(var unused=new NpUsbSensors(old)){}}catch(IOException){refused=true;}
        Need(refused&&File.ReadAllBytes(path).SequenceEqual(new byte[]{17,29,41})&&!File.Exists(Path.Combine(old,"analogin_encoding.txt")),"old recording was overwritten/reinterpreted");
    }
    static int Main(string[] args){try{Directory.CreateDirectory(args[0]);Ratios();Grid(args[0]);Console.WriteLine("PASS "+checks+" actual-source USB IMU count/range/rounding/clipping/grid/provenance checks; no hardware.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
