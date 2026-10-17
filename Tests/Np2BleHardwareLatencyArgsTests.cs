using System;
using System.Diagnostics;
using System.IO;

// Extracts the runner's summary/admission and production clock timing helpers. No desktop assembly,
// watcher, GATT, or hardware is loaded.
internal static class Np2BleHardwareLatencyArgsTests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}

    static int Main(string[] args)
    {
        string folder=null;
        try {
            if(args.Length!=2)throw new ArgumentException("<Np2BleHardware.cs> <csc.exe>");
            string source=File.ReadAllText(args[0]);
            int start=source.IndexOf("static long PercentileMilliseconds",StringComparison.Ordinal);
            int end=source.IndexOf("static async Task RunLatency",start,StringComparison.Ordinal);
            Need(start>=0 && end>start,"Latency percentile helpers missing.");
            string helpers=source.Substring(start,end-start);
            int action=source.IndexOf("if (action == \"latency\")",StringComparison.Ordinal);
            int run=source.IndexOf("await RunLatency(connection,client,cancel)",action,StringComparison.Ordinal);
            int readonlyReturn=source.IndexOf("latency action completed read-only",run,StringComparison.Ordinal);
            int method=source.IndexOf("static async Task RunLatency",StringComparison.Ordinal);
            int methodEnd=source.IndexOf("static async Task RunConnected",method,StringComparison.Ordinal);
            string latencyMethod=source.Substring(method,methodEnd-method);
            Need(source.Contains("action != \"latency\"") && source.Contains("status|observed-status|latency|sync|reconnect"),"Latency action is not advertised and validated.");
            Need(action>=0 && run>action && readonlyReturn>run,"Latency action is not routed to read-only measurement.");
            Need(latencyMethod.Contains("new long[LatencyStatusQueries]") && latencyMethod.Contains("new long[LatencyCapsQueries]") && latencyMethod.Contains("SendV2(connection,0,0,0") && latencyMethod.Contains("CapsAsync"),"Latency action lacks serialized STATUS/CAPS measurements.");
            Need(latencyMethod.Contains("Get(status,\"State\"))!=1U") && latencyMethod.Contains("latency.status-" ) && latencyMethod.Contains("LogCaps(port,caps)") && latencyMethod.Contains("latency.caps-"),"Measured STATUS/CAPS samples are not Idle-guarded and individually logged.");
            Need(source.Contains("const int LatencyClockQueries = 8;") && latencyMethod.Contains("new long[LatencyClockQueries]"),"Clock sampling is not bounded to eight reads.");
            Need(latencyMethod.Contains("InvokeStatic(clockType,\"Request\",0U,id,0U,0UL,0U,0U)") && latencyMethod.Contains("InvokeStatic(clockType,\"Parse\",reply,0U,id)") && latencyMethod.Contains("Invoke(connection,\"ExchangeAsync\",(byte)0xb9,request,cancel)"),"Clock measurement bypasses fresh op0 requests, production exchange or receipt parser.");
            Need(latencyMethod.Contains("uint id=NextId()") && latencyMethod.Contains("ClockProcessingMicroseconds(") && latencyMethod.Contains("midpoint.Invoke(null,timing)") && latencyMethod.Contains("Get(connection,\"Epoch\"))!=epoch"),"Clock timing lacks fresh identity, production MidpointUtc or epoch/monotonic admission.");
            Need(latencyMethod.Contains("elapsed.Stop();") && latencyMethod.Contains("elapsed_us=") && latencyMethod.Contains("device_processing_us=") && latencyMethod.Contains("unsynchronized") && latencyMethod.Contains("timing_only=true"),"Clock measurements are not timed/labeled independently of synchronization.");
            Need(source.Substring(action,run-action).Contains("Get(lastStatus,\"State\"))!=1U"),"Latency action does not reject a non-Idle initial STATUS.");
            Need(!latencyMethod.Contains("StageAsync") && !latencyMethod.Contains("Initialize") && !latencyMethod.Contains("Synchronize") && !latencyMethod.Contains("\"Request\",1") && !latencyMethod.Contains("RescueReset") && !latencyMethod.Contains("CapturePreview") && !latencyMethod.Contains("ConnectAsync") && !latencyMethod.Contains("catch("),"Latency action routes a mutation, reconnect or retry.");
            string clock=File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"..","USB512_interface","USB512_interface","Ble","NpV2OnlineClock.cs"));
            int timingStart=clock.IndexOf("internal static long MidpointUtc",StringComparison.Ordinal);
            int timingEnd=clock.IndexOf("internal static async Task<NpV2ClockResult> Synchronize",timingStart,StringComparison.Ordinal);
            Need(timingStart>=0 && timingEnd>timingStart,"Production clock timing helper missing.");
            string timing=clock.Substring(timingStart,timingEnd-timingStart);
            folder=Path.Combine(Path.GetTempPath(),"Np2BleLatencyArgs_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string program=Path.Combine(folder,"LatencyHarness.cs"),exe=Path.Combine(folder,"LatencyHarness.exe");
            File.WriteAllText(program,Harness(helpers,timing));
            var compile=new ProcessStartInfo(args[1],"/nologo /target:exe /r:System.Core.dll /out:"+Quote(exe)+" "+Quote(program)){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(compile)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted latency helpers failed to compile: "+output);}
            var runProcess=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(runProcess)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted latency helpers failed: "+output);Console.Write(output);}
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        finally { if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){} }
    }

    static string Harness(string helpers,string timing)
    {
        return @"using System;
using System.IO;
using System.Linq;
static class Harness {
"+helpers+timing+@"
 static void Need(bool value,string message){if(!value)throw new Exception(message);}
 static void Refuse(Action action){try{action();}catch(IOException){return;}throw new Exception(""invalid clock sample admitted"");}
 static int Main(){try{
  long[] samples=Enumerable.Range(1,20).Select(x=>(long)x).ToArray();
  Need(PercentileMilliseconds(samples,1,2)==10,""median rank wrong"");
  Need(PercentileMilliseconds(samples,95,100)==19,""p95 rank wrong"");
  Need(LatencySummary(samples)==""count=20; median_ms=10; p95_ms=19; max_ms=20"",""summary wrong"");
  bool rejected=false;try{PercentileMilliseconds(new long[0],1,2);}catch(ArgumentException){rejected=true;}Need(rejected,""empty samples accepted"");
  Need(ClockProcessingMicroseconds(100,105,1,false,0,false,0,false,0)==5000,""fresh unsynchronized read rejected"");
  Need(ClockProcessingMicroseconds(100,105,1,false,7,true,99,false,7)==5000,""expired mapping cannot retain generation"");
  Need(ClockProcessingMicroseconds(100,105,1,true,7,true,99,true,7)==5000,""stable valid generation rejected"");
  Need(ClockProcessingMicroseconds(UInt32.MaxValue-2,2,1,false,0,true,UInt32.MaxValue-3,false,0)==5000,""monotonic tick wrap rejected"");
  Refuse(()=>ClockProcessingMicroseconds(100,105,3,true,7,false,0,false,0));
  Refuse(()=>ClockProcessingMicroseconds(100,105,1,true,0,false,0,false,0));
  Refuse(()=>ClockProcessingMicroseconds(100,99,1,false,0,false,0,false,0));
  Refuse(()=>ClockProcessingMicroseconds(100,105,1,true,7,true,101,true,7));
  Refuse(()=>ClockProcessingMicroseconds(100,105,1,true,8,true,99,true,7));
  Refuse(()=>ClockProcessingMicroseconds(100,105,1,false,7,true,99,true,7));
  long uncertainty;
  Need(MidpointUtc(0,80000,100,105,out uncertainty)==42500 && uncertainty==38500,""actual midpoint timing changed"");
  Refuse(()=>{long error;MidpointUtc(0,-1,100,100,out error);});
  Refuse(()=>{long error;MidpointUtc(0,5000001,100,100,out error);});
  Refuse(()=>{long error;MidpointUtc(0,1000,100,104,out error);});
  Console.WriteLine(""PASS extracted latency statistics, clock validity/generation/wrap/timing and eight-op0 read-only routing; no radio or calibration used"");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}";
    }
}
