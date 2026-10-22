using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace CE32_console {static class NpSdControl {internal static uint Crc(byte[] bytes,int count){throw new Exception("Wire parser is outside this mismatch fixture");}}}
namespace USB512_interface {
 static class MessageBox {internal static int Calls;internal static string Body,Title;internal static DialogResult Result=DialogResult.No;internal static DialogResult Show(IWin32Window owner,string text,string title,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton selected){Calls++;Body=text;Title=title;if(selected!=MessageBoxDefaultButton.Button2)throw new Exception("Default update choice must decline");return Result;}}
 sealed class NpSystemInfoControl:IDisposable{internal NpSystemInfoControl(string path){throw new Exception("Hardware accessed");}internal byte[] Read(byte type,byte request,ushort value,int length){throw new Exception("Hardware accessed");}public void Dispose(){}}
 static class NpV1Management{internal static string UidFromPath(string path){throw new Exception("Hardware accessed");}internal static void ParseSplitStatus(byte[] bytes,string uid){throw new Exception("Hardware accessed");}}
 static class NpV2FirmwareForm{internal static string Uid(string path){throw new Exception("Hardware accessed");}}
 sealed class NpV2ServiceStatus{internal bool Valid;internal uint Uid0,UidXor;internal static NpV2ServiceStatus Parse(byte[] bytes){throw new Exception("Hardware accessed");}}
 static class NpFirmwareMismatchTests {
  static int checks,number;static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  static NpFirmwareBuild Copy(NpFirmwareBuild b){return new NpFirmwareBuild{imageId=b.imageId,baseAddress=b.baseAddress,bytes=b.bytes,codeCrc32=b.codeCrc32,buildIdHex=b.buildIdHex,manifestFlags=b.manifestFlags,minimumLoaderAbi=b.minimumLoaderAbi};}
  static NpFirmwareIdentity Identity(NpFirmwareRelease r){return new NpFirmwareIdentity{Family=r.family,Board=r.boardIds.FirstOrDefault(),Uid="001D00065942500B"+(++number).ToString("X8"),Flags=0x10f,App=Copy(r.appIdentity),Usb=Copy(r.usbIdentity),AppCrc32=r.appCrc32,UsbCrc32=r.usbCrc32};}
  static void Silent(NpFirmwareRelease r,NpFirmwareIdentity value,string why){int before=MessageBox.Calls;Need(!NpFirmwareNotice.Offer(null,"usb",r,value)&&MessageBox.Calls==before,why);}
  static void Prompt(NpFirmwareRelease r,NpFirmwareIdentity value,string why){int before=MessageBox.Calls;Need(!NpFirmwareNotice.Offer(null,"usb",r,value)&&MessageBox.Calls==before+1,why);Need(MessageBox.Body.Contains("Open firmware management?")&&MessageBox.Body.Contains("only after you select and confirm"),"Prompt performs/implies automatic flash");}
  static int Main(string[] args){try{
   var catalog=NpFirmwareCatalog.Load(Path.GetFullPath(args[0]));
   foreach(var release in catalog.Releases){
    var value=Identity(release);Silent(release,value,"Current image pair prompted");Silent(release,null,"Missing identity prompted");
    value=Identity(release);value.App=null;value.AppCrc32=null;Silent(release,value,"Unknown APP was treated as a mismatch");
    value=Identity(release);value.Usb=null;value.UsbCrc32=null;Silent(release,value,"Unknown USB was treated as a mismatch");
    value=Identity(release);value.Family=3;value.App.buildIdHex=new string('E',64);Silent(release,value,"Wrong family prompted");
    value=Identity(release);value.App.buildIdHex=new string('E',64);Prompt(release,value,"Different reported APP did not prompt");Need(MessageBox.Body.Contains("Different from bundle: APP."),"Unknown build falsely called newer");
    int before=MessageBox.Calls;Need(!NpFirmwareNotice.Offer(null,"ble",release,value)&&MessageBox.Calls==before,"Same UID/build prompted again across transports");
    value.Usb.buildIdHex=new string('D',64);Prompt(release,value,"Newly changed build did not prompt");
    value=Identity(release);value.Usb.codeCrc32=value.UsbCrc32="11223344";value.Flags&=~12U;MessageBox.Result=DialogResult.Yes;before=MessageBox.Calls;
    Need(NpFirmwareNotice.Offer(null,"usb",release,value)&&MessageBox.Calls==before+1,"Reported USB mismatch acceptance ignored");Need(MessageBox.Body.Contains("APP-only update does not update the USB"),"USB mismatch suggests APP-only update");MessageBox.Result=DialogResult.No;
    if(release.family==1){value=Identity(release);value.Board++;value.App.buildIdHex=new string('E',64);Silent(release,value,"Unsupported V1 board prompted");}
    else{Need(release.boardIds.Length==0,"Invented V2 board ID");Need(release.full!=null&&File.Exists(catalog.CheckedPath(release.full)),"Checked full V2 bundle missing");}
   }
   Silent(null,null,"Missing bundle prompted");Console.WriteLine("PASS "+checks+" actual-source mismatch/prompt/bundle checks; no hardware or firmware writes.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
