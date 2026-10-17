using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CE32_console;
using USB512_interface;

namespace CE32_console {
 internal static class NpDownloadNative {internal static readonly List<string> Paths=new List<string>();internal static IList<string> EnumerateNpPaths(){return Paths;}}
}
namespace USB512_interface {
 internal static class NpV1Management {internal static string UidFromPath(string path){return (path??"").Contains("#npv1-")?"V1":null;}}
 internal sealed class NpSystemInfoControl:IDisposable {
  internal const string Uid="001A0020434250062039314E";
  internal static uint Mode,Busy,Pending;internal static bool WrongUid,Legacy,CancelAfterSwitch;internal static int Switches,Reads;internal static string ReturnedPath;
  internal static string Path(string uid=Uid){return @"\\?\usb#vid_1209&pid_b386#"+uid+"#{test}";}
  internal static void Reset(uint mode){Mode=mode;Busy=Pending=0;WrongUid=Legacy=CancelAfterSwitch=false;Switches=Reads=0;ReturnedPath=null;NpDownloadNative.Paths.Clear();NpDownloadNative.Paths.Add(Path());}
  internal NpSystemInfoControl(string path){if(!NpDownloadNative.Paths.Contains(path))throw new IOException("Missing selected path");}
  static void Put(byte[] b,int p,uint value){BitConverter.GetBytes(value).CopyTo(b,p);}
  internal byte[] Read(byte type,byte request,ushort value,int count){
   if(type!=0xc0||value!=0||count!=32)throw new Exception("Unexpected transfer");Reads++;
   var b=new byte[32];
   if(request==0x6e){if(Legacy)throw new Win32Exception(31);Put(b,0,0x3253504e);Put(b,4,4);Put(b,8,Mode);Put(b,12,15);Put(b,16,Busy);Put(b,20,Pending);Put(b,24,Convert.ToUInt32(Uid.Substring(0,8),16)^(WrongUid?1U:0));Put(b,28,Convert.ToUInt32(Uid.Substring(8,8),16)^Convert.ToUInt32(Uid.Substring(16,8),16));}
   else if(request==0x60&&Legacy){Put(b,0,0x3144504e);Put(b,4,1);Put(b,16,Busy);Put(b,28,1);}else throw new Exception("Unexpected request");return b;
  }
  public void Dispose(){}
 }
 internal sealed class NpV2FirmwareForm {
ACTUAL_V2_METHODS
  static Task<string> RunHelper(string uid,string operation,Action<string> append){
   if(uid!=NpSystemInfoControl.Uid||operation!="--download"||NpSystemInfoControl.Mode!=2||NpSystemInfoControl.Busy!=0||NpSystemInfoControl.Pending!=0)throw new Exception("Unsafe helper command");
   NpSystemInfoControl.Switches++;NpSystemInfoControl.Mode=3;
   if(NpSystemInfoControl.ReturnedPath!=null){NpDownloadNative.Paths.Clear();NpDownloadNative.Paths.Add(NpSystemInfoControl.ReturnedPath);}
   return Task.FromResult("ack");
  }
 }
ACTUAL_SERVICE_STATUS
}
static class NpV2DownloadModeTests {
 static int checks;
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static async Task Refused(Func<Task<string>> action,string why){bool refused=false;try{await action();}catch(Exception ex)when(ex is IOException||ex is OperationCanceledException){refused=true;}Need(refused,why);Need(NpSystemInfoControl.Switches==0,"Refused preparation mutated a device");}
 static async Task Tests(){
  foreach(uint mode in new[]{2U,3U}){NpSystemInfoControl.Reset(mode);string path=await NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path());Need(path==NpSystemInfoControl.Path()&&NpSystemInfoControl.Mode==3,"V2 mode not verified on selected UID");Need(NpSystemInfoControl.Switches==(mode==2?1:0),"V2 mode request replayed or unnecessary");}
  NpSystemInfoControl.Reset(2);NpSystemInfoControl.Busy=1;await Refused(()=>NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path()),"Active recorder interrupted");
  NpSystemInfoControl.Reset(2);NpSystemInfoControl.Pending=3;await Refused(()=>NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path()),"Pending mode interrupted");
  NpSystemInfoControl.Reset(2);NpSystemInfoControl.WrongUid=true;await Refused(()=>NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path()),"Wrong service UID accepted");
  NpSystemInfoControl.Reset(2);NpDownloadNative.Paths.Add(NpSystemInfoControl.Path().Replace("{test}","{duplicate}"));await Refused(()=>NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path()),"Duplicate selected UID accepted");
  NpSystemInfoControl.Reset(2);NpDownloadNative.Paths.Clear();NpDownloadNative.Paths.Add(NpSystemInfoControl.Path("001A0020434250062039314F"));await Refused(()=>NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path()),"Neighbor substituted for absent board");
  NpSystemInfoControl.Reset(2);await Refused(()=>NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path(),()=>true),"Cancelled preparation sent a mode command");
  NpSystemInfoControl.Reset(3);NpSystemInfoControl.Legacy=true;Need(await NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path())==NpSystemInfoControl.Path()&&NpSystemInfoControl.Switches==0,"Standalone download firmware rejected or switched");
  NpSystemInfoControl.Reset(2);NpSystemInfoControl.ReturnedPath=NpSystemInfoControl.Path().Replace("{test}","{returned}");Need(await NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path())==NpSystemInfoControl.ReturnedPath,"Returned path lost after re-enumeration");
  NpSystemInfoControl.Reset(2);bool cancelled=false;try{await NpV2FirmwareForm.EnsureDownload(NpSystemInfoControl.Path(),()=>NpSystemInfoControl.Switches!=0);}catch(OperationCanceledException){cancelled=true;}Need(cancelled&&NpSystemInfoControl.Switches==1,"Cancel after acknowledged switch replayed or proceeded");
  Console.WriteLine("PASS "+checks+" actual-source V2 automatic-download mode checks; fake EP0/helper, no hardware.");
 }
 static int Main(){try{Tests().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
