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
  private int NpDefaultProbeMask() {
   if(npRequestedMode!=null)return npRequestedMode.ProbeMask;
   try {using(var device=XeNpWinUsbDevice.OpenForControl(devicePath)) {
    if(device.GetStatus().Streaming)return 0;
    NpModeCapabilities caps=device.GetModeCapabilities();
    return caps==null?0:caps.AvailableMask;
   }} catch {return 0;}
  }
  private void NpShowModeSettings() {
   if(selectedDeviceKind==DeviceKind.NpV2) {NpV2ShowModeSettings();return;}
   using(var dialog=new Form {Text="NP USB capture setup",ClientSize=new Size(560,414),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false}) {
    int oldMask=NpDefaultProbeMask();
    int oldRate=npRequestedMode==null?30000:npRequestedMode.RequestedApHz;
    var port1=new CheckBox {Text="Probe 1",Location=new Point(16,39),AutoSize=true,Checked=(oldMask&1)!=0};
    var port2=new CheckBox {Text="Probe 2",Location=new Point(190,39),AutoSize=true,Checked=(oldMask&2)!=0};
    var port3=new CheckBox {Text="Probe 3",Location=new Point(372,39),AutoSize=true,Checked=(oldMask&4)!=0};
    var rate=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(16,93),Size=new Size(110,24)};
    foreach(int hz in new[]{7500,15000,20000,30000})rate.Items.Add(hz);
    rate.SelectedItem=NpCaptureMode.RateBit(oldRate)!=0?oldRate:30000;
    var rateLabel=new Label {Text="Hz AP per selected probe",Location=new Point(136,97),AutoSize=true};
    var note=new Label {Location=new Point(16,130),Size=new Size(520,112),Text="Each probe has 384 AP + 384 LFP channels. The console resolves your request from the attached firmware's capabilities; accepted ports and rate are saved in status.\r\n\r\nNative 20 kHz supports any single port or probes 1+2 when advertised. This clock profile is unqualified. Other pairs and three probes resolve to supported rates.\r\n\r\n15-to-20 kHz AP upsampling changes host output, not acquisition rate. Two probes at 30 kHz remains experimental."};
    var merge=new CheckBox {Text="Merge active probes into one AP DAT and one LFP DAT",Location=new Point(16,250),Size=new Size(500,24),Checked=npMergeProbeDat};
    var upsample=new CheckBox {Text="Enable 15 kHz → 20 kHz AP output if 15 kHz is accepted (no effect at native 20/30 kHz)",Location=new Point(16,278),Size=new Size(530,24),Checked=npUpsampleAp15To20};
    Action updateOptions=delegate {
     int mask=(port1.Checked?1:0)|(port2.Checked?2:0)|(port3.Checked?4:0);
     merge.Enabled=NpCaptureMode.Count(mask)>1;
     // Keep the requested resampling preference visible and checked at 30 kHz;
     // NpCaptureSession applies it only to an acknowledged 15 kHz profile.
     upsample.Enabled=true;
    };
    port1.CheckedChanged+=delegate {updateOptions();};port2.CheckedChanged+=delegate {updateOptions();};port3.CheckedChanged+=delegate {updateOptions();};rate.SelectedIndexChanged+=delegate {updateOptions();};updateOptions();
    var config=new Button {Text="Configure connected probe…",Location=new Point(16,318),Size=new Size(205,30)};
    config.Click+=delegate {
     string result=NpConfigurationUsb.Show(dialog,devicePath,null,NpRequestsForDevice(devicePath,true));
     if(result!=null) {npRequestedConfiguration=result;npConfigurationDevicePath=devicePath;}
    };
    var ok=new Button {Text="Use request",Location=new Point(318,372),Width=115};
    var cancel=new Button {Text="Cancel",Location=new Point(443,372),DialogResult=DialogResult.Cancel};
    ok.Click+=delegate {
     int mask=(port1.Checked?1:0)|(port2.Checked?2:0)|(port3.Checked?4:0);
     int requestedRate=(int)rate.SelectedItem;
     if(requestedRate==20000 && mask!=0 && !NpCaptureMode.Valid(mask,requestedRate) && MessageBox.Show(dialog,"Native 20 kHz supports one probe or probes 1+2 when advertised by the attached firmware. This selection will resolve to another advertised rate. Continue?","NP USB capture setup",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
     if(NpCaptureMode.Count(mask)==2 && requestedRate==30000 && MessageBox.Show(dialog,"2 × 30 kHz has not passed sustained real-probe USB qualification. Continue with this best-effort request? Firmware may select a lower profile.","Experimental mode",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
     npRequestedMode=mask==0?null:new NpCaptureRequest(mask,requestedRate);
     npMergeProbeDat=merge.Checked;
     npUpsampleAp15To20=upsample.Checked;
     dialog.DialogResult=DialogResult.OK;
    };
    dialog.Controls.AddRange(new Control[]{new Label {Text="Requested probes",Location=new Point(16,12),AutoSize=true},port1,port2,port3,new Label {Text="Requested sampling rate",Location=new Point(16,70),AutoSize=true},rate,rateLabel,note,merge,upsample,config,ok,cancel});
    WildXTheme.Apply(dialog);WildXTheme.Button(ok,true);note.ForeColor=WildXTheme.Muted;
    dialog.AcceptButton=ok;dialog.CancelButton=cancel;dialog.ShowDialog(this);UpdateStartEnabled();
   }
  }
 }
}
