using System;
using System.Diagnostics;
using System.IO;

// Extracts only the record preview-policy helper. No desktop assembly,
// discovery, GATT, USB, or hardware is loaded.
internal static class Np2BleHardwarePreviewArgsTests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    static int Main(string[] args)
    {
        string folder=null;
        try {
            if(args.Length!=2)throw new ArgumentException("<Np2BleHardware.cs> <csc.exe>");
            string source=File.ReadAllText(args[0]);
            int start=source.IndexOf("static bool RecordPreview",StringComparison.Ordinal);
            int end=source.IndexOf("static async Task RunConnected",StringComparison.Ordinal);
            Need(start>=0 && end>start,"Record preview policy helper missing.");
            string helper=source.Substring(start,end-start);
            int record=source.IndexOf("if (action == \"record\")",StringComparison.Ordinal);
            int policy=source.IndexOf("requestPreview = RecordPreview(args)",record,StringComparison.Ordinal);
            int branch=source.IndexOf("if (requestPreview && state == 3 && applied && !previewLogged)",end,StringComparison.Ordinal);
            int capture=source.IndexOf("CapturePreview",branch,StringComparison.Ordinal);
            int applied=source.IndexOf("RequireApplied",end,StringComparison.Ordinal);
            int stop=source.IndexOf("if (state == 3 && !stopAttempted",branch,StringComparison.Ordinal);
            Need(source.Contains("args.Length < 6 || args.Length > 9") && policy>record,"Record tail arguments are not bounded before policy selection.");
            Need(source.Contains("if (args.Length >= 7) recordHz = Positive(args[6], \"sample rate\");") && source.Contains("if (args.Length >= 8) reservationSeconds = Positive(args[7], \"capture duration\");"),"Ninth no-preview tail does not retain explicit rate/reservation wiring.");
            Need(source.Contains("if (args.Length != 4) throw new ArgumentException(action + \" takes no configuration/rate arguments.\")"),"Non-record actions accept the preview flag.");
            Need(source.Contains("preview_policy=") && applied<branch && branch>end && capture>branch && stop>capture,"Preview policy changes configuration, applied-map, or stop wiring.");
            folder=Path.Combine(Path.GetTempPath(),"Np2BlePreviewArgs_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string program=Path.Combine(folder,"PreviewArgsHarness.cs"),exe=Path.Combine(folder,"PreviewArgsHarness.exe");
            File.WriteAllText(program,Harness(helper));
            var compile=new ProcessStartInfo(args[1],"/nologo /target:exe /out:"+Quote(exe)+" "+Quote(program)){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(compile)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted preview helper failed to compile: "+output);}
            var run=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(run)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted preview helper failed: "+output);Console.Write(output);}
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        finally { if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){} }
    }

    static string Harness(string helper)
    {
        return @"using System;
static class Harness {
"+helper+@"
 static void Need(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(string[] args){try{RecordPreview(args);}catch(ArgumentException){return;}throw new Exception(""accepted invalid preview arguments"");}
 static int Main(){try{Need(RecordPreview(new[]{""x"",""peer"",""out"",""record"",""p1"",""p2""}),""default preview was not enabled"");Need(RecordPreview(new[]{""x"",""peer"",""out"",""record"",""p1"",""p2"",""20000"",""10""}),""explicit rate/duration lost default preview"");Need(!RecordPreview(new[]{""x"",""peer"",""out"",""record"",""p1"",""p2"",""20000"",""10"",""no-preview""}),""final no-preview was not accepted"");Reject(new[]{""x"",""peer"",""out"",""record"",""p1"",""p2"",""20000"",""10"",""NO-PREVIEW""});Reject(new[]{""x"",""peer"",""out"",""record"",""p1"",""p2"",""20000"",""10"",""no-preview"",""extra""});Console.WriteLine(""PASS extracted record no-preview/default/invalid contract; no radio used"");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}";
    }
}
