"""Compile the actual NP2 USB AUX-retirement finally/error fragment; no device access."""
import argparse
import subprocess
from pathlib import Path


def need(value, message):
    if not value:
        raise RuntimeError(message)


def block(text, start):
    open_at = text.index("{", start)
    depth = 0
    for at in range(open_at, len(text)):
        if text[at] == "{":
            depth += 1
        elif text[at] == "}" and not (depth := depth - 1):
            return text[open_at:at + 1]
    raise RuntimeError("unterminated source block")


def actual_cleanup(source):
    marker = "try { if(beforeDeviceDispose!=null)beforeDeviceDispose(device); }"
    at = source.index(marker)
    start = source.rfind("finally", 0, at)
    open_at = source.index("{", start)
    return source[start:open_at] + block(source, start)


def source_contract(source):
    need("Action<NpV2UsbDevice> beforeDeviceDispose" in source, "missing AUX retirement overload")
    need("mergeProbes,saveRaw,beforeDeviceDispose" in source, "new overload does not forward cleanup callback")
    need("return RunCore(devicePath, folder, 0U, cancelled, progress, decodedApRow, null);" in source,
         "existing decoded-row overload no longer preserves its null cleanup argument")
    need("return RunContinuousInFolder(devicePath, folder, cancelled, progress, null);" in source,
         "existing simple continuous overload no longer preserves its original forwarding")
    need("if(beforeDisposeError!=null)error=error==null?beforeDisposeError:new AggregateException" in source,
         "cleanup failure no longer aggregates rather than replaces recording failure")


def fixture(cleanup):
    return """using System;
class Device { public bool Disposed; public void Dispose(){Disposed=true;} }
class ActualAuxRetirement {
 static int checks;
 static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static void Actual(Action<Device> beforeDeviceDispose,Device device,Action retire,ref Exception beforeDisposeError) {
  try { retire(); }
  """ + cleanup + """
 }
 static Exception Aggregate(Exception error,Exception workerError,Exception beforeDisposeError) {
  error=error??workerError;
  if(beforeDisposeError!=null)error=error==null?beforeDisposeError:new AggregateException("Recording and before-device-dispose cleanup failed.",error,beforeDisposeError);
  return error;
 }
 static void Case(string name,Action<Device> callback,bool prior,bool cleanupError,bool retireThrows) {
  var device=new Device();int callbackCount=0;bool callbackBeforeDispose=false;string order="";Exception cleanup=null,error=null;
  Action retire=delegate{order+="R";if(retireThrows)throw new InvalidOperationException("retire");};
  Action<Device> actual=delegate(Device d){callbackCount++;callbackBeforeDispose=!d.Disposed;order+="C";callback(d);};
  try { Actual(actual,device,retire,ref cleanup); } catch(Exception ex) { error=ex; }
  error=Aggregate(prior?new InvalidOperationException("record"):error,null,cleanup); device.Dispose();order+="D";
  Need(callbackCount==1,name+": callback count");Need(callbackBeforeDispose,name+": callback after dispose");Need(device.Disposed,name+": simulated disposal missing");
  Need(order=="RCD",name+": callback was not after retire and before dispose");
  if(prior&&cleanupError){var both=error as AggregateException;Need(both!=null&&both.InnerExceptions.Count==2&&both.InnerExceptions[0].Message=="record"&&both.InnerExceptions[1].Message=="aux",name+": prior error replaced");}
  else if(prior)Need(error.Message=="record",name+": cleanup changed prior error");
  else if(cleanupError)Need(error.Message=="aux",name+": cleanup error missing");
  else if(retireThrows)Need(error!=null&&error.Message=="retire",name+": retirement error missing");
  else Need(error==null,name+": unexpected error");
 }
 static int Main(){try{
  Case("normal",delegate(Device d){},false,false,false);
  Case("setupfail",delegate(Device d){},false,false,true);
  Case("abortthrow",delegate(Device d){},false,false,true);
  Case("cleanup-prior",delegate(Device d){throw new InvalidOperationException("aux");},true,true,false);
  Case("cleanup-only",delegate(Device d){throw new InvalidOperationException("aux");},false,true,false);
  Console.WriteLine("PASS "+checks+" actual NP2 USB before-device-dispose callback: once, before disposal, setup/abort finally, and error aggregation; no hardware");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo", type=Path)
    parser.add_argument("csc", type=Path)
    parser.add_argument("output", type=Path, help="fresh output directory")
    args = parser.parse_args()
    need(not args.output.exists(), "output directory must be fresh")
    source_path = args.repo / "Neuropixels" / "USBConsoleV2" / "NpV2UsbConsole.cs"
    source = source_path.read_text(encoding="utf-8")
    source_contract(source)
    args.output.mkdir(parents=True)
    program = args.output / "ActualNp2AuxRetirement.cs"
    exe = args.output / "ActualNp2AuxRetirement.exe"
    program.write_text(fixture(actual_cleanup(source)), encoding="utf-8")
    compile_result = subprocess.run([str(args.csc), "/nologo", "/out:" + str(exe), str(program)],
                                    capture_output=True, text=True, check=False)
    need(compile_result.returncode == 0, "actual cleanup fragment compile failed: " + compile_result.stdout + compile_result.stderr)
    result = subprocess.run([str(exe)], capture_output=True, text=True, check=False)
    print(result.stdout, end="")
    need(result.returncode == 0, "actual cleanup fragment failed: " + result.stderr)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(error)
        raise SystemExit(1)
