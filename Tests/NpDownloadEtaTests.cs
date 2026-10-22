using System;
using System.Reflection;

// Calls the formatter in the built application; no form or USB device is opened.
static class NpDownloadEtaTests {
    static MethodInfo format;
    static int checks;
    static void Check(int percent,double elapsed,bool known,string expected) {
        string actual=(string)format.Invoke(null,new object[]{percent,elapsed,known});
        checks++;
        if(actual!=expected)throw new Exception(percent+"% at "+elapsed+" s: "+actual+" != "+expected);
    }
    static int Main(string[] args) {
        if(args.Length!=1)return 2;
        format=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0])).GetType("CE32_console.NpDownloadForm",true)
            .GetMethod("DownloadTimeRemaining",BindingFlags.Static|BindingFlags.NonPublic);
        Check(0,30,true,"Estimating time…");
        Check(20,2,true,"Estimating time…");
        Check(25,60,true,"~00:03:00 remaining");
        Check(50,60,true,"~00:01:00 remaining");
        Check(75,60,true,"~00:00:20 remaining");
        Check(99,100,true,"~00:00:02 remaining");
        Check(1,3600,true,"~99:00:00 remaining");
        Check(100,60,true,"Finalizing…");
        Check(101,60,true,"Finalizing…");
        Check(50,60,false,"Time remaining unknown (size unknown)");
        Check(-1,60,true,"Estimating time…");
        Check(50,Double.NaN,true,"Estimating time…");
        Check(50,Double.PositiveInfinity,true,"Estimating time…");
        Check(1,Double.MaxValue,true,"Estimating time…");
        Console.WriteLine("PASS "+checks+" built-application download ETA checks; no hardware access.");
        return 0;
    }
}
