using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WildNpConfiguration;

// Actual host preparation files and the two START/presence methods are compiled
// against in-memory transports. No WinUSB, BLE watcher or device is constructed.
namespace WildNpConfiguration {
 public interface INpConfigTransport {Task<byte[]> ExchangeAsync(byte[] q,CancellationToken c);}
 public class NpConfigRequest {public byte Port;}
 public class Np2ConfigRequest {public int Port;}
 public class Status {public uint GlobalState=0,Received=0,Error=0,TransferState=0;}
 public class NpConfigClient {public NpConfigClient(object wire){} public Task<Status> StatusAsync(int p,CancellationToken c){return Task.FromResult(new Status());}}
}
namespace CE32_console {class NpSdStatus {internal uint Mask=3,Hz=20000,Fault,State;}}
namespace USB512_interface {
 class TextCell {internal string Text="";}
 class Np2ConfigClient {internal Np2ConfigClient(object wire){} internal Task<Status> StatusAsync(int p,CancellationToken c){return Task.FromResult(new Status());}}
 class Np2ConfigurationUsb {internal Np2ConfigurationUsb(object device){} internal static void StageForCapture(object d,Np2ConfigRequest r){}}
 class NpConfigurationUsb {internal static void StageForCapture(object d,IList<NpConfigRequest> r,int m){}}
 class NpConfigurationDialog {internal static Np2ConfigRequest PrepareV2Request(string s,int p){return new Np2ConfigRequest{Port=p-1};}}
 class NpV2FirmwareForm {internal static string Uid(string path){return Wire.Uid;}}
 class NpV1Management {
  internal class Status {internal int Role=2,Busy;internal bool Recovery;}
  internal static string UidFromPath(string path){return Wire.Uid;}
  internal static string EnsureStreaming(string path){return path;}
  internal static string WaitForStreaming(string uid){return "v1";}
 }
 class NpCaptureMode {internal int ProbeMask=3,ApHz=20000;}
 class NpModeCapabilities {
  internal int ActiveMask=3,ApHz=20000,PresenceRequestedMask,PresenceState,PresenceFaultMask,PresentMask=3,AbsentMask;
  internal bool Selectable=true,SupportsPresence=true,PresenceReady=true;
  internal NpCaptureMode Active {get{return new NpCaptureMode{ProbeMask=ActiveMask,ApHz=ApHz};}}
  internal NpModeCapabilities ForPresentProbes(){return this;}
 }
 class NpCaptureRequest {
  internal int ProbeMask,RequestedApHz;
  internal NpCaptureRequest(int m,int h){ProbeMask=m;RequestedApHz=h;}
  internal NpCaptureMode ResolveBest(NpModeCapabilities caps,out string resolution){resolution="fixture";return new NpCaptureMode{ProbeMask=ProbeMask,ApHz=RequestedApHz};}
  internal string Describe(){return "fixture";}internal static string Ports(int mask){return mask.ToString();}
 }
 class XeNpStatus {internal bool Streaming;}
 class XeNpWinUsbDevice:IDisposable {
  string devicePath="v1";
  internal static XeNpWinUsbDevice OpenForControl(string path){return new XeNpWinUsbDevice{devicePath=path};}
  internal NpV1Management.Status ReadV1ServiceStatus(){return new NpV1Management.Status();}
  internal XeNpStatus GetStatus(){return new XeNpStatus();}
  internal NpModeCapabilities GetModeCapabilities(){return Wire.V1Caps;}
  internal byte[] ExchangeProbePreparation(byte[] q,CancellationToken c){return Wire.Exchange(q,c).GetAwaiter().GetResult();}
  internal byte[] ExchangeNpConfiguration(byte[] q,CancellationToken c){throw new Exception("Unexpected map transfer");}
  internal void StopPreparation(){Wire.Stop();}
  void VendorOut(byte command,ushort value,ushort index){Wire.Commands.Add(command);if(command==0x75)Wire.V1Caps.PresenceRequestedMask=value;}
ACTUAL_V1_SELECT
  public void Dispose(){}
 }
 class NpV2ProfileCapabilities {
  internal uint SelectedProbeMask=3,SampleHz=20000;
  internal static void RequireUsbBandwidth(uint mask,uint hz){}
 }
 class NpV2ServiceStatus {
  internal uint Mode=2,Busy;
  internal void RequireUid(string uid){if(uid!=Wire.Uid)throw new IOException("Wrong UID");}
  internal void RequireDeviceUid(string uid,string pathUid){RequireUid(uid);if(uid!=pathUid)throw new IOException("Wrong path");}
 }
 class NpV2CaptureStatus {internal uint State,SourceState,Format=3,ProbeMask=3,SampleHz=20000;}
 class NpV2UsbDevice:IDisposable {
  internal static NpV2UsbDevice Open(string path){return new NpV2UsbDevice();}
  internal NpV2ServiceStatus GetServiceStatus(){return new NpV2ServiceStatus();}
  internal NpV2CaptureStatus GetCaptureStatus(){return Wire.V2Capture;}
  internal NpV2ProfileCapabilities GetProfileCapabilities(){return new NpV2ProfileCapabilities();}
  internal NpV2ProfileCapabilities ConfigureProfile(uint m,uint h){return new NpV2ProfileCapabilities{SelectedProbeMask=m,SampleHz=h};}
  internal byte[] ExchangeProbePreparation(byte[] q,CancellationToken c){return Wire.Exchange(q,c).GetAwaiter().GetResult();}
  internal void Cancel(){Wire.Stop();}
  void VendorOut(byte command,ushort value,ushort index){Wire.Commands.Add(command);}
ACTUAL_V2_START
  public void Dispose(){}
 }
 class NpRecordingDialog:IDisposable {
  internal int ProbeMask=3,SampleHz=20000;internal bool Merge,Upsample,Misc;
  internal NpRecordingDialog(bool np2,int mask,int hz,bool merge,bool up,bool misc,Func<NpUsbRecordingProfile> read,Action<IWin32Window> configure){}
  internal DialogResult ShowDialog(object owner){Wire.OnDialog?.Invoke();return Wire.DialogDecision;}
  public void Dispose(){}
 }
 class NpUsbRecordingProfile {internal int SelectedMask=3;}
 public partial class Form1 {
  enum DeviceKind {XeNp,NpV2}
  DeviceKind selectedDeviceKind=DeviceKind.NpV2;
  string devicePath="v2";bool IsDisposed,Disposing,running;
  int systemInfoActive,deviceRefreshActive;
  readonly TextCell lblRecInfo=new TextCell();
  bool NpControlsAvailable {get{return !npSettingsApplying;}}
  uint npV2RequestedProbeMask=3,npV2RequestedSampleHz=20000;
  NpCaptureRequest npRequestedMode=new NpCaptureRequest(3,20000);
  bool npMergeProbeDat,npUpsampleAp15To20,npRecordMiscSignals;
  void UpdateStartEnabled(){}
  Dictionary<byte,NpConfigRequest> NpRequestsForDevice(string path){return new Dictionary<byte,NpConfigRequest>();}
  HashSet<int> NpV2ApplyRequestsForDevice(string path){return new HashSet<int>();}
  Dictionary<int,string> NpV2RequestsForDevice(string path){return new Dictionary<int,string>();}
  string NpV2EnsureIdleStreamMode(){return devicePath;}
  NpUsbRecordingProfile ReadNpRecordingProfile(){return new NpUsbRecordingProfile();}
  void ShowNpProbeConfiguration(IWin32Window owner){}
ACTUAL_SETTINGS_DIALOG
  internal static async Task VerifyColdSettings(){
   var form=new Form1();Wire.Reset();Wire.DialogDecision=DialogResult.OK;
   Wire.Exchange=(q,c)=>{throw new Exception("Default settings launched optional preparation");};
   await form.NpShowModeSettings();
   Wire.Need(!form.HasUsbProbePreparation&&form.lblRecInfo.Text=="Settings saved.","Default settings did not preserve cold Start intent");
   form.npPreparationTimer.Dispose();
  }
  internal static async Task Verify(){
   var form=new Form1();Wire.Reset();
   form.systemInfoActive=1;Wire.OnDialog=()=>Wire.Need(form.npSettingsApplying&&form.systemInfoActive==0,"Settings dialog opened without exclusive retired-reader lease");
   var retire=Task.Run(async()=>{await Task.Delay(30);Interlocked.Exchange(ref form.systemInfoActive,0);});
   await form.NpShowModeSettings();await retire;Wire.Need(!form.npSettingsApplying,"Settings dialog did not release lease");
   var expected=Wire.Receipt(2);form.npUsbPreparation=expected;form.npUsbPreparationKind=DeviceKind.NpV2;form.npUsbPreparationPath=form.devicePath;
   Wire.Exchange=(q,c)=>{var r=Wire.Reply(q,2);Wire.Put(r,16,3);Wire.Seal(r);return Task.FromResult(r);};
   await form.ObserveUsbProbePreparation();Wire.Need(!form.lblRecInfo.Text.Contains("Probes ready"),"USB nonzero Result retained readiness text");
   form.ClearUsbProbePreparation();Wire.Reset();
   form.npUsbPreparationAttempt=new NpProbePreparation.Attempt{Uid=Wire.Uid,Mode=1,Mask=3,Hz=20000};form.npUsbPreparationKind=DeviceKind.NpV2;form.npUsbPreparationPath=form.devicePath;
   form.npSettingsApplying=true;await form.StopUsbProbePreparation();Wire.Need(form.npPreparationStopRequested&&Wire.Stops==0,"Stop raced current USB owner");
   form.npSettingsApplying=false;Wire.FailStop=true;await form.StopUsbProbePreparation();
   Wire.Need(Wire.Stops==1&&form.npUsbPreparationAttempt.StopIssued,"Uncertain explicit USB Stop lost no-replay marker");
   Wire.FailStop=false;await form.StopUsbProbePreparation();Wire.Need(Wire.Stops==1&&!form.HasUsbProbePreparation,"Explicit USB Stop was replayed after a lost ACK");
   form.npPreparationTimer.Dispose();
  }
 }
 internal sealed partial class NpBleForm {
  sealed class FakeConnection {
   internal bool Connected=true;internal int Epoch=7;
   internal Task<byte[]> ExchangeAsync(byte command,byte[] q,CancellationToken c){return Wire.Exchange(q,c);}
  }
  sealed class ConfigTransport {internal ConfigTransport(object c){}}
  readonly FakeConnection connection=new FakeConnection();readonly TextCell configurationStatus=new TextCell();
  int recorderVersion=2;ulong connectedAddress=123;bool V2=true,stopRequested;string bleDeviceUid=Wire.Uid;
  void RequireStartNotCanceled(){if(stopRequested)throw new OperationCanceledException();}
  void Append(string text){}
  uint Next(){return 1;}
  Task<CE32_console.NpSdStatus> Send(byte[] request){Wire.Stop();return Task.FromResult(new CE32_console.NpSdStatus());}
  Task<CE32_console.NpSdStatus> ReadStatus(){return Task.FromResult(new CE32_console.NpSdStatus{State=1});}
  bool StopConfirmed(CE32_console.NpSdStatus status){return true;}
  internal static async Task Verify(){
   var form=new NpBleForm();Wire.Reset();int calls=0;
   Wire.Exchange=(q,c)=>{if(++calls==2)throw new TimeoutException();return Task.FromResult(Wire.Reply(q,0));};
   await form.PrepareBleProbes(new CE32_console.NpSdStatus());
   Wire.Need(form.UnknownBlePreparation&&form.probePreparationAttempt.Uid==Wire.Uid,"BLE lost PREPARE ACK lost ownership context");
   Wire.FailStop=true;bool failed=false;try{await form.StopUnknownBlePreparation(new CE32_console.NpSdStatus());}catch(TimeoutException){failed=true;}
   Wire.Need(failed&&Wire.Stops==1&&form.probePreparationAttempt.StopIssued,"BLE uncertain Stop lost no-replay marker");
   Wire.FailStop=false;Wire.Exchange=(q,c)=>Task.FromResult(Wire.Reply(q,0));await form.StopUnknownBlePreparation(new CE32_console.NpSdStatus());
   Wire.Need(Wire.Stops==1&&!form.UnknownBlePreparation,"BLE explicit Stop replayed after lost ACK");
   form.probePreparation=Wire.Receipt(1);form.probePreparationEpoch=7;form.probePreparationVersion=2;form.probePreparationPeer=123;
   Wire.Exchange=(q,c)=>{var r=Wire.Reply(q,2);Wire.Put(r,16,3);Wire.Seal(r);return Task.FromResult(r);};
   await form.ObserveProbePreparation(new CE32_console.NpSdStatus{State=1});
   Wire.Need(!form.configurationStatus.Text.Contains("Probes ready"),"BLE nonzero Result claimed readiness");
  }
 }
 static class NpV2SdControl {internal static uint NextId(CE32_console.NpSdStatus s){return 1;}internal static byte[] Request(uint op,uint id){return new byte[64];}}
 static class NpSdControl {internal static uint NextId(CE32_console.NpSdStatus s){return 1;}internal static byte[] Request(uint op,uint id){return new byte[64];}}
 static class Wire {
  internal const string Uid="001D00065942500B20313354";
  internal static int Checks,Stops;internal static bool FailStop;internal static Action OnDialog;
  internal static DialogResult DialogDecision;
  internal static readonly List<byte> Commands=new List<byte>();
  internal static NpModeCapabilities V1Caps;internal static NpV2CaptureStatus V2Capture;
  internal static Func<byte[],CancellationToken,Task<byte[]>> Exchange;
  internal static void Need(bool value,string text){Checks++;if(!value)throw new Exception(text);}
  internal static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
  internal static void Seal(byte[] b){Put(b,60,NpProbePreparation.Crc(b,60));}
  internal static byte[] Reply(byte[] q,uint state){var b=new byte[64];Put(b,0,NpProbePreparation.ReplyMagic);Put(b,4,1);Buffer.BlockCopy(q,8,b,8,8);Put(b,20,state);Put(b,24,7);Put(b,28,1);Put(b,32,3);Put(b,36,20000);for(int i=0;i<3;i++)Put(b,48+4*i,Convert.ToUInt32(Uid.Substring(8*i,8),16));Seal(b);return b;}
  internal static NpProbePreparation.Receipt Receipt(uint state){var q=NpProbePreparation.Request(0,1);return NpProbePreparation.Parse(Reply(q,state),q);}
  internal static void Stop(){Stops++;if(FailStop)throw new TimeoutException("Lost Stop ACK");}
  internal static void Reset(){Stops=0;FailStop=false;OnDialog=null;DialogDecision=DialogResult.Cancel;Commands.Clear();V1Caps=new NpModeCapabilities();V2Capture=new NpV2CaptureStatus();Exchange=(q,c)=>Task.FromResult(Reply(q,0));}
  [STAThread]static int Main(){try{Reset();
   using(var d=XeNpWinUsbDevice.OpenForControl("v1")){NpCaptureMode m;string resolution,reconnect;
#if NP_PREPARATION_ENABLE
    Exchange=(q,c)=>Task.FromResult(Reply(q,2));d.SelectCaptureMode(new NpCaptureRequest(3,20000),out m,out resolution,out reconnect,true);
    Need(!Commands.Contains(0x75),"Matching fresh V1 preparation was destroyed by presence preflight");
    Exchange=(q,c)=>{var r=Reply(q,2);Put(r,16,3);Seal(r);return Task.FromResult(r);};d.SelectCaptureMode(new NpCaptureRequest(3,20000),out m,out resolution,out reconnect,true);
    Need(Commands.Contains(0x75),"Nonzero V1 preparation Result bypassed preflight");
#endif
    Commands.Clear();Exchange=(q,c)=>{throw new Exception("Cold V1 unexpectedly discovered optional preparation");};
    d.SelectCaptureMode(new NpCaptureRequest(3,20000),out m,out resolution,out reconnect);
    Need(Commands.Contains(0x75),"Cold V1 no longer follows original presence preflight");
#if !NP_PREPARATION_ENABLE
    Commands.Clear();d.SelectCaptureMode(new NpCaptureRequest(3,20000),out m,out resolution,out reconnect,true);
    Need(Commands.Contains(0x75),"Default V1 preparation hint bypassed cold preflight");
#endif
   }
   Reset();V2Capture.State=4;V2Capture.SourceState=7;
   using(var d=NpV2UsbDevice.Open("v2")){
#if NP_PREPARATION_ENABLE
    Exchange=(q,c)=>Task.FromResult(Reply(q,2));d.Start(0);Need(Commands.Count==1&&Commands[0]==0x70,"Fresh matching V2 preparation was rejected by legacy one-shot guard");
    Commands.Clear();Exchange=(q,c)=>{throw new TimeoutException();};bool failed=false;try{d.Start(0);}catch(IOException){failed=true;}Need(failed&&Commands.Count==0,"Unconfirmed V2 preparation bypassed source guard");
#else
    Exchange=(q,c)=>{throw new Exception("Default V2 queried optional preparation");};bool failed=false;
    try{d.Start(0);}catch(IOException){failed=true;}
    Need(failed&&Commands.Count==0,"Default V2 bypassed its cold source guard");
#endif
    V2Capture.State=V2Capture.SourceState=0;Exchange=(q,c)=>{throw new Exception("Cold V2 unexpectedly discovered optional preparation");};
    d.Start(0);Need(Commands.Count==1,"Legacy cold V2 Start was gated by optional preparation query");
   }
#if NP_PREPARATION_ENABLE
   Form1.Verify().GetAwaiter().GetResult();NpBleForm.Verify().GetAwaiter().GetResult();
#else
   Form1.VerifyColdSettings().GetAwaiter().GetResult();
#endif
   Console.WriteLine("PASS "+Checks+" actual-source preparation host checks; no hardware.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
