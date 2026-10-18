using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace USB512_interface {
 // Only the dialog/hardware boundaries are replaced. The complete production
 // catalog, identity matcher and Offer implementation are compiled unchanged.
 static class MessageBox {
  internal static int Calls;internal static string Title;internal static MessageBoxDefaultButton Default;
  internal static DialogResult Result=DialogResult.No;
  internal static DialogResult Show(IWin32Window owner,string text,string title,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton selected){Calls++;Title=title;Default=selected;return Result;}
 }
 sealed class NpSystemInfoControl:IDisposable {internal NpSystemInfoControl(string path){throw new Exception("Hardware accessed");}internal byte[] Read(byte type,byte request,ushort value,int length){throw new Exception("Hardware accessed");}public void Dispose(){}}
 static class NpV1Management {internal static string UidFromPath(string path){throw new Exception("Hardware accessed");}internal static void ParseSplitStatus(byte[] data,string uid){throw new Exception("Hardware accessed");}}
 static class NpV2FirmwareForm {internal static string Uid(string path){throw new Exception("Hardware accessed");}}
 sealed class NpV2ServiceStatus {internal bool Valid;internal uint Uid0,UidXor;internal static NpV2ServiceStatus Parse(byte[] data){throw new Exception("Hardware accessed");}}

 static class NpFirmwareNoticeTests {
  static int checks,device;
  static void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
  static NpFirmwareBuild Copy(NpFirmwareBuild b){return new NpFirmwareBuild{imageId=b.imageId,baseAddress=b.baseAddress,bytes=b.bytes,codeCrc32=b.codeCrc32,buildIdHex=b.buildIdHex,manifestFlags=b.manifestFlags,minimumLoaderAbi=b.minimumLoaderAbi,generation=b.generation};}
  static NpFirmwareIdentity Current(NpFirmwareRelease r){return new NpFirmwareIdentity{Family=r.family,Board=r.boardIds[0],Flags=0x11f,App=Copy(r.appIdentity),Usb=Copy(r.usbIdentity),AppCrc32=r.appCrc32,UsbCrc32=r.usbCrc32};}
  static void Silent(NpFirmwareRelease r,NpFirmwareIdentity identity,string why,string key=null){int before=MessageBox.Calls;Check(!NpFirmwareNotice.Offer(null,key??"device"+(++device),r,identity),why+" opened management");Check(MessageBox.Calls==before,why+" showed update prompt");}
  static void Family(NpFirmwareRelease r){
   if(r.boardIds.Length==0)r.boardIds=new uint[]{0x00020001}; // Matcher fixture only; shipping manifest untouched.
   Silent(r,Current(r),"Exact current APP/USB");Silent(r,null,"Missing firmware identity");
   var value=Current(r);value.App=null;value.AppCrc32=null;value.Flags&=~5U;Silent(r,value,"APP not reported");
   value=Current(r);value.Usb=null;value.UsbCrc32=null;value.Flags&=~10U;Silent(r,value,"USB not reported");
   value=Current(r);value.App.buildIdHex=new string('F',64);Silent(r,value,"Unknown/newer private APP with same CRC");
   value=Current(r);value.Usb.codeCrc32="DEADBEEF";Silent(r,value,"Unknown/newer USB");
   value=Current(r);value.Board++;Silent(r,value,"Different board");value=Current(r);value.Family=3;Silent(r,value,"Different family");
   string key="later-identified-"+r.family;Silent(r,null,"Unidentified connection",key);
   var oldApp=Copy(r.appIdentity);oldApp.codeCrc32="12345678";
   var oldUsb=Copy(r.usbIdentity);oldUsb.codeCrc32="87654321";
   r.previousApps=new[]{oldApp};r.previousUsb=new[]{oldUsb};
   value=Current(r);value.App=Copy(oldApp);value.Flags&=~4U;Silent(r,value,"Unverified old APP");
   value=Current(r);value.Usb=Copy(oldUsb);value.Flags&=~8U;Silent(r,value,"Unverified old USB");
   value=Current(r);value.App=Copy(oldApp);value.App.buildIdHex=new string('E',64);Silent(r,value,"CRC-only old APP match");
   value=Current(r);value.App=Copy(oldApp);int before=MessageBox.Calls;
   Check(!NpFirmwareNotice.Offer(null,key,r,value),"Decline opened management");Check(MessageBox.Calls==before+1,"Confirmed old APP did not prompt after identity became known");
   Check(MessageBox.Title=="Firmware update available"&&MessageBox.Default==MessageBoxDefaultButton.Button2,"Wrong/default-Yes update dialog");
   Silent(r,value,"Repeat notice in same session",key);
   value=Current(r);value.Usb=Copy(oldUsb);MessageBox.Result=DialogResult.Yes;before=MessageBox.Calls;
   Check(NpFirmwareNotice.Offer(null,"old-usb-"+r.family,r,value),"Confirmed old USB acceptance ignored");Check(MessageBox.Calls==before+1,"Confirmed old USB did not prompt");MessageBox.Result=DialogResult.No;
   Silent(r,Current(r),"Current after previous-build tests");
  }
  static int Main(string[] args){try{
   var catalog=NpFirmwareCatalog.Load(Path.GetFullPath(args[0]));foreach(var release in catalog.Releases)Family(release);
   Silent(null,null,"Missing bundle");Silent(catalog.For(1),Current(catalog.For(1)),"Empty device key","");
   Console.WriteLine("PASS "+checks+" actual-source automatic firmware notice checks: latest/missing/partial/private/unknown identities stay silent; only verified predecessors prompt, once per session; no hardware or firmware writes.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
