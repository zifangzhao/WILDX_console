using System;
using System.Diagnostics;
using System.IO;

// Extracts only the runner's argument/delay helpers. No desktop assembly,
// discovery, GATT, or hardware is loaded.
internal static class Np2BleHardwareReconnectArgsTests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    static int Main(string[] args)
    {
        string folder=null;
        try {
            if(args.Length!=2)throw new ArgumentException("<Np2BleHardware.cs> <csc.exe>");
            string source=File.ReadAllText(args[0]);
            int start=source.IndexOf("static uint ReconnectDelay",StringComparison.Ordinal);
            int end=source.IndexOf("static async Task RunConnected",StringComparison.Ordinal);
            Need(start>=0 && end>start,"Reconnect delay helpers missing.");
            string helpers=source.Substring(start,end-start);
            int reconnect=source.IndexOf("if (action == \"reconnect\")",end,StringComparison.Ordinal);
            int disconnect=source.IndexOf("Invoke(connection,\"Disconnect\")",reconnect,StringComparison.Ordinal);
            int wait=source.IndexOf("await WaitReconnectDelay(reconnectDelay,cancel)",reconnect,StringComparison.Ordinal);
            int connect=source.IndexOf("Invoke(connection,\"ConnectAsync\",address)",reconnect,StringComparison.Ordinal);
            Need(reconnect>=0 && disconnect>reconnect && wait>disconnect && connect>wait,"Reconnect delay is not routed between local disconnect and same-instance connect.");
            string reconnectPath=source.Substring(reconnect,source.IndexOf("if (action == \"rescue\")",reconnect,StringComparison.Ordinal)-reconnect);
            Need(reconnectPath.Contains("reconnect_delay_seconds=") && !reconnectPath.Contains("SendV2(") && !reconnectPath.Contains("StageAsync") && !reconnectPath.Contains("Initialize") && !reconnectPath.Contains("RescueReset"),"Reconnect delay path routes a mutation or omits logging.");
            folder=Path.Combine(Path.GetTempPath(),"Np2BleReconnectArgs_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string program=Path.Combine(folder,"ReconnectArgsHarness.cs"),exe=Path.Combine(folder,"ReconnectArgsHarness.exe");
            File.WriteAllText(program,Harness(helpers));
            var compile=new ProcessStartInfo(args[1],"/nologo /target:exe /r:System.Core.dll /out:"+Quote(exe)+" "+Quote(program)){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(compile)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted delay helpers failed to compile: "+output);}
            var run=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(run)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted delay helpers failed: "+output);Console.Write(output);}
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        finally { if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){} }
    }

    static string Harness(string helpers)
    {
        return @"using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
static class Harness {
"+helpers+@"
 static void Need(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(string value){try{ReconnectDelay(value);}catch(ArgumentException){return;}throw new Exception(""accepted invalid delay: ""+value);}
 static async Task Cancel(){using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(10);bool threw=false;try{await WaitReconnectDelay(30,cancel.Token);}catch(OperationCanceledException){threw=true;}Need(threw,""cancellation was not propagated to disconnected delay"");}}
 static int Main(){try{Need(ReconnectDelay(""0"")==0&&ReconnectDelay(""30"")==30,""delay bounds rejected"");Reject(""31"");Reject(""-1"");Reject(""x"");WaitReconnectDelay(0,CancellationToken.None).GetAwaiter().GetResult();Cancel().GetAwaiter().GetResult();Console.WriteLine(""PASS extracted reconnect delay argument/cancellation contract; no radio used"");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}";
    }
}
