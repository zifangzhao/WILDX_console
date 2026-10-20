using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using CE32_console;

// File-only differential check against a frozen production decoder. No USB API.
static class NpV1DownloadPerformanceTests {
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Type oldType,oldLayout;static int checks;
    static void Need(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    static object Call(object o,string name,params object[] args){
        try{return o.GetType().GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o,args);}
        catch(TargetInvocationException e){throw e.InnerException;}
    }
    static object Old(int layout){return Activator.CreateInstance(oldType,All,null,new[]{Enum.ToObject(oldLayout,layout)},null);}
    static void UnpackCases(){
        var random=new Random(7621);
        foreach(int layout in new[]{1,2,3})for(int test=0;test<1024;test++){
            byte[] raw=new byte[64];random.NextBytes(raw);
            if(test<8)for(int i=0;i<raw.Length;i++)raw[i]=(byte)(1<<test);
            object old=Old(layout);var current=new NpCe64Decoder((NpCe64LaneLayout)layout);
            var list=(IList)oldType.GetField("pending",All).GetValue(old);foreach(byte b in raw)list.Add(b);
            Buffer.BlockCopy(raw,0,(byte[])typeof(NpCe64Decoder).GetField("pending",All).GetValue(current),0,raw.Length);
            Call(old,"ExtractAdcWords",3);Call(current,"ExtractAdcWords",3);
            Need(((ushort[])oldType.GetField("words",All).GetValue(old)).SequenceEqual((ushort[])typeof(NpCe64Decoder).GetField("words",All).GetValue(current)),"ADC unpack differs: layout="+layout+" fixture="+test);
        }
    }
    sealed class Outcome {
        public string Samples,Error;public long Ap,Lfp,Frames,FrameErrors,CounterErrors,Discarded,LfpErrors;public int Slot;
    }
    static Outcome Decode(byte[] data,bool baseline,int feedBytes){
        object decoder=baseline?Old(3):(object)new NpCe64Decoder(NpCe64LaneLayout.GpioV1Quad);var result=new Outcome();
        Call(decoder,"DeclareRecordingReset");Call(decoder,"DeclareFixedWord",(ushort)0x11);
        using(var hash=SHA256.Create()){
            byte[] bytes=new byte[768];
            Action<short[],int,short[],int> sample=(ap,n,lfp,l)=>{
                result.Ap+=n;result.Lfp+=l;
                foreach(var band in new[]{Tuple.Create(ap,n),Tuple.Create(lfp,l)})for(int row=0;row<band.Item2;row++){
                    Buffer.BlockCopy(band.Item1,row*768,bytes,0,768);hash.TransformBlock(bytes,0,768,bytes,0);
                }
            };
            try{for(int at=0;at<data.Length;at+=feedBytes)Call(decoder,"Feed",data,at,Math.Min(feedBytes,data.Length-at),sample);Call(decoder,"Complete");}
            catch(Exception e){result.Error=e.GetType().FullName+": "+e.Message;}
            hash.TransformFinalBlock(new byte[0],0,0);result.Samples=BitConverter.ToString(hash.Hash);
        }
        Func<string,long> get=name=>Convert.ToInt64(decoder.GetType().GetProperty(name,All).GetValue(decoder,null));
        result.Frames=get("FramesChecked");result.FrameErrors=get("FrameErrors");result.CounterErrors=get("CounterErrors");result.Discarded=get("StartupDiscardedBytes");result.LfpErrors=get("LfpPhaseErrors");result.Slot=(int)get("InitialLfpSlot");return result;
    }
    static double Speed(byte[][] data,bool baseline){
        var watch=Stopwatch.StartNew();
        foreach(var raw in data){object decoder=baseline?Old(3):(object)new NpCe64Decoder(NpCe64LaneLayout.GpioV1Quad);Call(decoder,"DeclareRecordingReset");Call(decoder,"DeclareFixedWord",(ushort)0x11);
            Action<short[],int,short[],int> sink=(a,n,b,m)=>{};
            for(int at=0;at<raw.Length;at+=61440)Call(decoder,"Feed",raw,at,Math.Min(61440,raw.Length-at),sink);Call(decoder,"Complete");
        }return watch.Elapsed.TotalSeconds;
    }
    static int Main(string[] args){
        if(args.Length!=3)return 2;
        Assembly old=Assembly.LoadFrom(Path.GetFullPath(args[0]));oldType=old.GetType("CE32_console.NpCe64Decoder",true);oldLayout=old.GetType("CE32_console.NpCe64LaneLayout",true);
        UnpackCases();var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
        var data=Directory.GetFiles(args[1],"np*.lane8").OrderBy(p=>p).Select(File.ReadAllBytes).ToArray();Need(data.Length==2,"Expected saved two-probe fixture");
        foreach(var raw in data){
            var expected=Decode(raw,true,61440);Need(expected.Error==null,"Baseline saved fixture failed");
            Need(json.Serialize(expected)==json.Serialize(Decode(raw,false,61440)),"Full saved sample/counter/status mismatch");
            byte[] prefix=raw.Take(65536).ToArray();
            foreach(int boundary in new[]{1,59,779,1561,61440})Need(json.Serialize(Decode(prefix,true,boundary))==json.Serialize(Decode(prefix,false,boundary)),"Feed boundary changed decoder result");
            foreach(int corrupt in new[]{10000,20000,30000}){byte[] bad=(byte[])prefix.Clone();bad[corrupt]^=0x81;var baseline=Decode(bad,true,779);Need(baseline.Error!=null,"Corruption did not reach strict rejection");Need(json.Serialize(baseline)==json.Serialize(Decode(bad,false,779)),"Corruption rejection/output prefix changed");}
            Need(json.Serialize(Decode(prefix.Take(900).ToArray(),true,127))==json.Serialize(Decode(prefix.Take(900).ToArray(),false,127)),"Short startup behavior changed");
        }
        double[] before=new double[3],after=new double[3];for(int n=0;n<3;n++){before[n]=Speed(data,true);after[n]=Speed(data,false);}Array.Sort(before);Array.Sort(after);
        var report=new{result="pass",checks,hardware_access=false,lane_bytes=data.Sum(b=>(long)b.Length),baseline_seconds=before,current_seconds=after,median_speedup=before[1]/after[1],baseline_MB_s=data.Sum(b=>(long)b.Length)/before[1]/1e6,current_MB_s=data.Sum(b=>(long)b.Length)/after[1]/1e6};
        File.WriteAllText(args[2],json.Serialize(report));Console.WriteLine(json.Serialize(report));return 0;
    }
}
