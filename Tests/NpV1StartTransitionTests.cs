using System;
using System.IO;
using USB512_interface;
using Native=CE32_console.NpDownloadNative;

// Uses the existing native-boundary fake and the actual management source.
// Every permitted mode command is checked: no APP update or recorder START.
static class NpV1StartTransitionTests {
 static int checks;
 static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
 static void Reject(Action call,string reason){bool failed=false;try{call();}catch(Exception){failed=true;}Check(failed,reason);Check(Native.Modes.Count==0&&Native.Operations.Count==0,"Failure mutated device/firmware");}
 static void Main(){
  foreach(uint role in new[]{2U,3U}) {
   Native.Reset(role);string selected=Native.Path(false,Native.Uid);
   string path=NpV1Management.EnsureStreaming(selected);
   Check(path==selected&&Native.Role==2&&Native.Pending==0,"Start did not pin and prepare streaming on the same UID");
   Check(Native.Modes.Count==(role==3?1:0)&&(role==2||Native.Modes[0]==2),"Unexpected mode command");
   Check(Native.Operations.Count==0&&Native.BeginCalls==0&&!Native.Transaction,"Start preparation performed a firmware update");
   Check(Native.Opened.TrueForAll(p=>p==selected),"Another logger was substituted");
  }
  Native.Reset(3);Native.Busy=1;Reject(()=>NpV1Management.EnsureStreaming(Native.Path(false,Native.Uid)),"Busy logger accepted");
  Native.Reset(3);Native.Pending=1;Reject(()=>NpV1Management.EnsureStreaming(Native.Path(false,Native.Uid)),"Pending transition accepted");
  Native.Reset(3);Native.WrongUid=true;Reject(()=>NpV1Management.EnsureStreaming(Native.Path(false,Native.Uid)),"Wrong-UID receipt accepted");
  Native.Reset(3);Native.StatusError=121;Reject(()=>NpV1Management.EnsureStreaming(Native.Path(false,Native.Uid)),"Timeout accepted");
  Native.Reset(3);Native.Paths.Clear();Native.Paths.Add(Native.Path(false,"001E002A5942500B20313355"));Reject(()=>NpV1Management.EnsureStreaming(Native.Path(false,Native.Uid)),"Missing selected board substituted");
  Native.Reset(3);string legacy="usb#vid_1209&pid_b386#legacy-capture#{fixture}";
  Check(NpV1Management.EnsureStreaming(legacy)==legacy&&Native.Opened.Count==0&&Native.Modes.Count==0,"Legacy identity lost direct capture support");
  Console.WriteLine("PASS "+checks+" actual-source V1 Start transition checks; streaming/download/legacy modes, pinned UID, busy/pending/error refusal and no firmware-update operations. No hardware.");
 }
}
