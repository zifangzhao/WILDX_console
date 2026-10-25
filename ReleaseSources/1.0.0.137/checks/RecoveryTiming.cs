using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

static class RecoveryTiming {
    const BindingFlags A=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static int checks;
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),A,null,args,null);}
    static object Get(object o,string name){var f=o.GetType().GetField(name,A);return f!=null?f.GetValue(o):o.GetType().GetProperty(name,A).GetValue(o,null);}
    static object Call(object o,string name,params object[] args){try{return (o as Type??o.GetType()).GetMethods(A).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o is Type?null:o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Check(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    static void Word(byte[] b,int at,int pin,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<pin);}
    static byte[] Data(int rows,uint first){var b=new byte[rows*780];for(int f=0;f<rows*13;f++){uint c=(first+(uint)f)&0xfffff;int at=f*60;Word(b,at,0,0,f%13==0?0x330U:0xcfU);Word(b,at,7,2,0x11);Word(b,at,7,3,c>>10);Word(b,at,7,4,c&1023);}return b;}
    static void Feed(object probe,byte[] bytes){var free=(BlockingCollection<byte[]>)Get(probe,"free");for(int at=0;at<bytes.Length;at+=99840){Call(probe,"Feed",bytes,at,Math.Min(99840,bytes.Length-at));Call(probe,"PublishProducer");if(!System.Threading.SpinWait.SpinUntil(()=>free.Count==64,5000))throw new Exception("Synthetic producer did not drain");}}
    static int Main(string[] args){try {
        app=Assembly.LoadFrom(Path.GetFullPath(args[0]));string root=Path.GetFullPath(args[1]);if(Directory.Exists(root))throw new IOException("Fresh output required");Directory.CreateDirectory(root);
        var p=New("NpProbeRecording",root,1,Enum.ToObject(T("XeNpLaneLayout"),3));var decoder=Get(p,"Decoder");
        Call(p,"EnableBestEffort",root);Call(decoder,"DeclareRecordingReset");Call(decoder,"DeclareFixedWord",(ushort)0x11);
        byte[] before=Data(10000,0),after=Data(14000,130013);Feed(p,before);Feed(p,after);Call(p,"Complete");Call(p,"Flush");
        Check(Convert.ToInt64(Get(p,"HostDroppedLaneBytes"))==0,"Synthetic feed overfilled the unrelated host pool");
        Check(Convert.ToInt64(Get(p,"ApRows"))==24001,"Missing row compressed the AP timeline");
        Check(Convert.ToInt64(Get(decoder,"MissingApRows"))==1&&Convert.ToInt64(Get(decoder,"GapEvents"))==1,"Known counter gap not accounted exactly");
        Check(Convert.ToUInt32(Get(decoder,"FirstSuperframeCounter"))==0,"Recovery changed first-source origin");
        Check(Convert.ToUInt32(Get(decoder,"LastSuperframeCounter"))==312012,"Recovery changed last source counter");
        Check(!(bool)Get(p,"Lossless"),"Recovered recording incorrectly labeled lossless");
        ((IDisposable)p).Dispose();string dat=Path.Combine(root,"neuropixels_ap.dat");byte[] bytes=File.ReadAllBytes(dat);
        Check(bytes.Length==24001*768,"DAT row count wrong");Check(bytes.Skip(10000*768).Take(768).All(b=>b==0),"Missing AP sentinel not zero");
        string gaps=File.ReadAllText(Path.Combine(root,"neuropixels_probe1_gaps.csv"));Check(gaps.Contains("ap,10000,1,counter_bounded_recovery")&&gaps.Contains("gap_crossing"),"AP/LFP gap metadata missing");
        var journal=New("NpTimeJournal",root);long start=Stopwatch.GetTimestamp();DateTime utc=DateTime.UtcNow;
        foreach(int row in new[]{0,5000,15000,20000,24000})Call(journal,"Observe",dat,384,20000,1,(long)row+1,start+(long)(row*Stopwatch.Frequency/20000.0),utc.AddSeconds(row/20000.0));
        Call(journal,"Finish");Check(!File.Exists(Path.Combine(root,"timestamp_calibration_error.txt")),"Timestamp finalization failed");
        byte[] times=File.ReadAllBytes(Path.Combine(root,"neuropixels_ap.dat.timestamps.calibrated.dat"));Check(times.Length==24001*8,"Timestamp/DAT row counts differ");
        double left=BitConverter.ToDouble(times,9999*8),right=BitConverter.ToDouble(times,10001*8),zero=BitConverter.ToDouble(times,0);
        Check(!Double.IsNaN(left)&&!Double.IsNaN(right)&&Math.Abs((right-left)-0.0001)<0.000001,"Known gap compressed or shifted timestamp scale");
        Check(Math.Abs((right-zero)-0.50005)<0.000001,"Post-gap row no longer uses original row-zero scale");
        Console.WriteLine("PASS "+checks+" actual136 counter-gap/DAT/first-origin/20k timestamp checks; synthetic input, no USB/hardware; known missing samples remain explicit.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
