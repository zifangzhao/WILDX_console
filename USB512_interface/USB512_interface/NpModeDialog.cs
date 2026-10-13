using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using WildNpConfiguration;
namespace USB512_interface {
 public partial class Form1 {
  // Null preserves the firmware's default.  A non-null object represents the
  // user's intent, not a promise that the device can attain that exact mode.
  private NpCaptureRequest npRequestedMode;
  private NpCaptureSession npCaptureSession;
  // Preferences apply only when the accepted profile makes them meaningful:
  // one probe is already a single file and 30 kHz is already native AP rate.
  private bool npMergeProbeDat=true;
  private bool npUpsampleAp15To20=true;
  // Enabled only through camera capability controls; default capture is NP-only.
  private bool npCameraEnabled=false;
  private volatile int npActiveProbeCount=1;
  private int npPreviewMask=1;
  private readonly Dictionary<string,Dictionary<byte,NpConfigRequest>> npPortConfigurations=new Dictionary<string,Dictionary<byte,NpConfigRequest>>(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string,Dictionary<int,string>> npV2HostConfigurations=new Dictionary<string,Dictionary<int,string>>(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string,HashSet<int>> npV2ApplyConfigurations=new Dictionary<string,HashSet<int>>(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string,Np2ConfigurationPreferences.Device> npV2PersistedConfigurations=LoadNpV2PersistedConfigurations();
  private static Dictionary<string,Np2ConfigurationPreferences.Device> LoadNpV2PersistedConfigurations(){
   try{return Np2ConfigurationPreferences.Load(Properties.Settings.Default.Np2ConfigurationIntents,NpConfigurationDialog.NormalizeNp2Request);}
   catch(Exception){return new Dictionary<string,Np2ConfigurationPreferences.Device>(StringComparer.OrdinalIgnoreCase);}
  }
  private void NpV2EnsureConfigurationState(string path){
   string key=NpV2FirmwareForm.Uid(path)??path;
   if(npV2HostConfigurations.ContainsKey(key))return;
   var requests=new Dictionary<int,string>();var apply=new HashSet<int>();Np2ConfigurationPreferences.Device saved;
   if(npV2PersistedConfigurations.TryGetValue(key,out saved)){
    foreach(var request in saved.Requests)requests.Add(request.Key,request.Value);
    foreach(int port in saved.Apply)if(requests.ContainsKey(port))apply.Add(port);
   }
   npV2HostConfigurations.Add(key,requests);npV2ApplyConfigurations.Add(key,apply);
  }
  private void NpV2SaveConfigurationState(string path){
   string key=NpV2FirmwareForm.Uid(path)??path;NpV2EnsureConfigurationState(path);
   var saved=new Np2ConfigurationPreferences.Device();foreach(var request in npV2HostConfigurations[key])saved.Requests.Add(request.Key,request.Value);
   foreach(int port in npV2ApplyConfigurations[key])if(saved.Requests.ContainsKey(port))saved.Apply.Add(port);
   npV2PersistedConfigurations[key]=saved;
   try{Properties.Settings.Default.Np2ConfigurationIntents=Np2ConfigurationPreferences.Save(npV2PersistedConfigurations);Properties.Settings.Default.Save();}
   catch(Exception ex){MessageBox.Show(this,"The NP2 map is retained for this session only: "+ex.Message,"NP2 IMRO / channel map",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
  }
  private Dictionary<int,string> NpV2RequestsForDevice(string path){
   string key=NpV2FirmwareForm.Uid(path)??path;
   NpV2EnsureConfigurationState(path);return npV2HostConfigurations[key];
  }
  private HashSet<int> NpV2ApplyRequestsForDevice(string path,bool create=false){
   if(String.IsNullOrEmpty(path))return null;
   string key=NpV2FirmwareForm.Uid(path)??path;
   NpV2EnsureConfigurationState(path);return npV2ApplyConfigurations[key];
  }
  private Dictionary<byte,NpConfigRequest> NpRequestsForDevice(string path,bool create=false){
   if(String.IsNullOrEmpty(path))return null;
   string key=NpV1Management.UidFromPath(path)??path;
   Dictionary<byte,NpConfigRequest> requests;
   if(!npPortConfigurations.TryGetValue(key,out requests) && create){requests=new Dictionary<byte,NpConfigRequest>();npPortConfigurations.Add(key,requests);}
   return requests;
  }
  private bool npRecordMiscSignals=true;
  private NpUsbRecordingProfile ReadNpRecordingProfile() {
   if(selectedDeviceKind==DeviceKind.NpV2)using(var device=NpV2UsbDevice.Open(devicePath)) {
    var service=device.GetServiceStatus();var capture=device.GetCaptureStatus();var source=device.GetSourceStatus();var caps=device.GetProfileCapabilities();
    if(!service.Valid || service.Busy!=0 || !caps.Valid)throw new System.IO.IOException("Wait for an idle device with recording capabilities.");
    return new NpUsbRecordingProfile {
     AvailableMask=(int)caps.AvailableProbeMask,SelectedMask=(int)caps.SelectedProbeMask,SelectedHz=(int)caps.SampleHz,
     Rates=Array.ConvertAll(caps.SupportedRates,hz=>(int)hz),
     Status=NpV2ProfileText(service,capture,source,caps)+"\r\n"+NpV2UsbTopology.Inspect(devicePath).Warning,
     Validate=(mask,hz)=>{
      if(mask==0 || !caps.SupportsProbeMask((uint)mask))return "Select an advertised connector combination.";
      try{NpV2ProfileCapabilities.RequireUsbBandwidth((uint)mask,(uint)hz);return null;}catch(System.IO.IOException ex){return ex.Message;}
     },
     Warning=(mask,hz)=>NpV2ProfileCapabilities.NeedsMultiProbe30KQualificationWarning((uint)mask,(uint)hz)?
      "Multi-probe 30 kHz USB recording is experimental. Continue with this profile?":null
    };
   }
   using(var device=XeNpWinUsbDevice.OpenForControl(devicePath)) {
    var status=device.GetStatus();if(status.Streaming)throw new System.IO.IOException("Stop recording before changing settings.");
    var caps=device.GetModeCapabilities();
    if(caps==null)return new NpUsbRecordingProfile {
     AvailableMask=0,SelectedMask=0,SelectedHz=status.ApSampleRateHz,Rates=new[]{status.ApSampleRateHz},
     Status="The firmware supplies a fixed recording profile. Physical connector identity is not advertised. 384 AP + 384 LFP channels per probe.",
     Validate=(mask,hz)=>mask!=0 || hz!=status.ApSampleRateHz?"Use the fixed firmware profile.":null,Warning=(mask,hz)=>null
    };
    var rates=new List<int>();foreach(int hz in new[]{7500,15000,20000,30000})if((caps.Rates[0]|caps.Rates[1]|caps.Rates[2])!=0 && ((caps.Rates[0]|caps.Rates[1]|caps.Rates[2])&NpCaptureMode.RateBit(hz))!=0)rates.Add(hz);
    if(!caps.Selectable){rates.Clear();rates.Add(caps.ApHz);}
    return new NpUsbRecordingProfile {
     AvailableMask=caps.Selectable?caps.AvailableMask:caps.ActiveMask,SelectedMask=caps.Selectable?caps.AvailableMask:caps.ActiveMask,SelectedHz=caps.ApHz,Rates=rates.ToArray(),
     Status="Device: Neuropixels V1\r\nAvailable: "+NpCaptureRequest.Ports(caps.AvailableMask)+"; active: "+NpCaptureRequest.Ports(caps.ActiveMask)+" at "+caps.ApHz+" Hz\r\n384 AP + 384 LFP channels per probe. The firmware resolves the requested rate; the accepted rate is saved with the recording.\r\nMisc signals retain device timestamps; analogin.dat uses a held-value 1,250 Hz grid.",
     Validate=(mask,hz)=>{
      if(mask==0 || (mask&~caps.AvailableMask)!=0)return "Select available connectors.";
      if(!caps.Selectable && (mask!=caps.ActiveMask || hz!=caps.ApHz))return "This firmware uses a fixed recording profile.";
      try {string resolution;new NpCaptureRequest(mask,hz).ResolveBest(caps,out resolution);return null;}catch(Exception ex){return ex.Message;}
     },
     Warning=(mask,hz)=>{
      string resolution;var resolved=new NpCaptureRequest(mask,hz).ResolveBest(caps,out resolution);
      string warning=resolved.ApHz!=hz?resolution+". Continue with the resolved profile?":null;
      if(resolved.Experimental)warning=(warning==null?"":warning+"\r\n")+"This V1 profile is experimental. Continue?";
      return warning;
     }
    };
   }
  }
  private void ShowNpProbeConfiguration(IWin32Window owner) {
   if(selectedDeviceKind==DeviceKind.NpV2) {
    int available;using(var device=NpV2UsbDevice.Open(devicePath))available=(int)device.GetProfileCapabilities().AvailableProbeMask;
    using(var editor=new NpConfigurationDialog(null,np2:true,availableMask:available,retainedRequests:NpV2RequestsForDevice(devicePath),applyConnectors:NpV2ApplyRequestsForDevice(devicePath,true),
     receipt:async(port,json)=>{using(var device=NpV2UsbDevice.Open(devicePath)){
      var reply=await new Np2ConfigClient(new Np2ConfigurationUsb(device)).StatusAsync((byte)(port-1),System.Threading.CancellationToken.None);
      return NpConfigurationDialog.Receipt(reply,json==null?null:NpConfigurationDialog.PrepareV2Request(json,port));}}))
     if(editor.ShowDialog(owner)==DialogResult.OK)NpV2SaveConfigurationState(devicePath);
   } else {
    string result=NpConfigurationUsb.Show(owner,devicePath,null,NpRequestsForDevice(devicePath,true));
    if(result!=null){npRequestedConfiguration=result;npConfigurationDevicePath=devicePath;}
   }
  }
  private void NpShowModeSettings() {
   bool np2=selectedDeviceKind==DeviceKind.NpV2;
   using(var dialog=new NpRecordingDialog(np2,np2?(int)npV2RequestedProbeMask:npRequestedMode==null?0:npRequestedMode.ProbeMask,
    np2?(int)npV2RequestedSampleHz:npRequestedMode==null?30000:npRequestedMode.RequestedApHz,
    npMergeProbeDat,npUpsampleAp15To20,npRecordMiscSignals,ReadNpRecordingProfile,ShowNpProbeConfiguration)) {
    if(dialog.ShowDialog(this)==DialogResult.OK) {
     if(np2){npV2RequestedProbeMask=(uint)dialog.ProbeMask;npV2RequestedSampleHz=(uint)dialog.SampleHz;}
     else {var profile=ReadNpRecordingProfile();int mask=dialog.ProbeMask==0?profile.SelectedMask:dialog.ProbeMask;npRequestedMode=mask==0?null:new NpCaptureRequest(mask,dialog.SampleHz);}
     npMergeProbeDat=dialog.Merge;npUpsampleAp15To20=dialog.Upsample;npRecordMiscSignals=dialog.Misc;
    }
    UpdateStartEnabled();
   }
  }
 }
}
