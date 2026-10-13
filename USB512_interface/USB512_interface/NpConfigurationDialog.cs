using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using WildNpConfiguration;

namespace USB512_interface {
 // The shared .NET 8 editor runs separately from the .NET Framework recorder.
 // The editor remains file-only; device staging is a separate explicit action.
 internal sealed class NpConfigurationDialog : Form {
  internal string RequestedJson {get;private set;}
  private readonly Label status=new Label {Dock=DockStyle.Fill,Padding=new Padding(12)};
  private readonly FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=44};
  private bool busy;
  private readonly bool np2;
  private readonly ComboBox connector=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=82};
  private readonly Dictionary<int,string> requests;
  private readonly ISet<int> applyConnectors;
  private readonly HashSet<int> pendingApplyConnectors;
  private readonly CheckBox applyOnCapture=new CheckBox {Text="Apply before next recording",AutoSize=true};
  private readonly Dictionary<byte,NpConfigRequest> v1Requests;
  private readonly Func<int,string,Task<string>> readReceipt;
  private bool selectingConnector;
  internal static string HelperPath {get{return Path.Combine(Path.GetDirectoryName(typeof(NpConfigurationDialog).Assembly.Location),"NPConfigurator","NPConfigurator.exe");}}
  internal NpConfigurationDialog(string current,string devicePath=null,bool np2=false,int availableMask=7,IDictionary<int,string> retainedRequests=null,ISet<int> applyConnectors=null,
   IDictionary<byte,NpConfigRequest> retainedV1Requests=null,Func<int,string,Task<string>> receipt=null) {
   this.np2=np2;requests=new Dictionary<int,string>(retainedRequests??new Dictionary<int,string>());this.applyConnectors=applyConnectors;pendingApplyConnectors=new HashSet<int>(applyConnectors??new HashSet<int>());
   v1Requests=new Dictionary<byte,NpConfigRequest>(retainedV1Requests??new Dictionary<byte,NpConfigRequest>());readReceipt=receipt;
   if(!np2)foreach(var request in v1Requests){requests.Add(request.Key+1,request.Value.Json);pendingApplyConnectors.Add(request.Key+1);}
   RequestedJson=current;Text="Probe configuration";
   ClientSize=new Size(820,300);MinimumSize=Size;StartPosition=FormStartPosition.CenterParent;actions.Height=100;
   var edit=new Button {Text="Open configurator",AutoSize=true};
   var clear=new Button {Text="Clear request",AutoSize=true};
   var keep=new Button {Text="Save settings",AutoSize=true};
   var cancel=new Button {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
   edit.Click+=async delegate {await Edit();};
   keep.Click+=delegate {
    if(!np2 && retainedV1Requests!=null){retainedV1Requests.Clear();foreach(var request in v1Requests)retainedV1Requests.Add(request.Key,request.Value);}
    if(np2 && retainedRequests!=null){retainedRequests.Clear();foreach(var request in requests)retainedRequests.Add(request.Key,request.Value);}
    if(np2 && applyConnectors!=null){applyConnectors.Clear();foreach(int connector in pendingApplyConnectors)applyConnectors.Add(connector);}
    DialogResult=DialogResult.OK;Close();
   };
   {
    for(int p=1;p<=3;p++)if((availableMask&(1<<(p-1)))!=0)connector.Items.Add(p);
    if(connector.Items.Count==0)throw new ArgumentOutOfRangeException("availableMask");
    connector.SelectedIndex=0;
    connector.SelectedIndexChanged+=delegate {SelectConnector();};
    applyOnCapture.Enabled=np2;
    applyOnCapture.CheckedChanged+=delegate {if(!selectingConnector){if(applyOnCapture.Checked)pendingApplyConnectors.Add(SelectedConnector);else pendingApplyConnectors.Remove(SelectedConnector);RefreshStatus();}};
    var load=new Button {Text="Load IMRO / JSON…",AutoSize=true};
    load.Click+=async delegate {using(var file=new OpenFileDialog {Filter="IMRO or configuration|*.imro;*.npconfig.json;*.json|IMRO|*.imro|NP configuration|*.json"}){
     if(file.ShowDialog(this)==DialogResult.OK)try{await ImportFile(file.FileName);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Probe configuration",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }};
    actions.Controls.AddRange(new Control[]{new Label {Text="Connector",AutoSize=true,Padding=new Padding(0,6,0,0)},connector,load,applyOnCapture});
    clear.Click+=delegate {requests.Remove(SelectedConnector);v1Requests.Remove((byte)(SelectedConnector-1));pendingApplyConnectors.Remove(SelectedConnector);SelectConnector();};
    edit.Text="Edit channel map…";SelectConnector();
   }
   actions.Controls.AddRange(new Control[]{edit,clear,keep,cancel});Controls.Add(status);Controls.Add(actions);
   if(readReceipt!=null){
    var device=new Button{Text="Read device receipt",AutoSize=true};
    device.Click+=async delegate {
     if(busy)return;busy=true;actions.Enabled=false;
     try{status.Text=await readReceipt(SelectedConnector,RequestedJson);}catch(Exception ex){status.Text="Configuration not confirmed: "+ex.Message;}
     finally{busy=false;actions.Enabled=true;}
    };actions.Controls.Add(device);
   }
   CancelButton=cancel;RefreshStatus();
   WildXTheme.Apply(this);WildXTheme.Button(keep,true);
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
  }
  private void RefreshStatus() {
   status.Text="Probe "+SelectedConnector+" · "+(RequestedJson==null?"No channel map loaded":pendingApplyConnectors.Contains(SelectedConnector)?"Apply before next recording":"Host request retained")+"\r\n\r\n"+
    (np2?"NP2: load an IMRO / JSON map. Device application currently supports NP2013 four-shank maps.":"NP1: choose sites, gains and references using an IMRO / JSON map.")+"\r\n\r\n"+
    "Maps apply at the next recording after the final mode change. Matching device receipts confirm application.\r\n"+
    (np2?"NP2 verifies selected chains; degraded unused shanks are reported.":"NP1 base chains use readback; the shank chain is ACK only.")+" Analog signals are not verified.\r\n\r\n"+
    "Cancel keeps previous host requests. Clear does not undo device settings.";
  }
  private int SelectedConnector {get{return connector.SelectedItem==null?1:(int)connector.SelectedItem;}}
  private void SelectConnector(){requests.TryGetValue(SelectedConnector,out var request);RequestedJson=request;selectingConnector=true;applyOnCapture.Checked=pendingApplyConnectors.Contains(SelectedConnector);selectingConnector=false;RefreshStatus();}
  internal static string NormalizeNp2Request(string json,int port){
   if(port<1 || port>3)throw new ArgumentOutOfRangeException("port");
   var serializer=new JavaScriptSerializer {MaxJsonLength=1024*1024};
   var document=serializer.Deserialize<Dictionary<string,object>>(json);
   object profile;
   if(document==null || !document.TryGetValue("Profile",out profile) || ((string)profile!="Np2Single" && (string)profile!="Np2Multi"))
    throw new IOException("Choose an NP2 single- or four-shank IMRO/configuration, not NP1 or NRIC.");
   document["Connector"]=port;document["HardwareState"]="NotApplied";
   return serializer.Serialize(document);
  }
  internal static Np2ConfigRequest PrepareV2Request(string json,int connector){
   if(String.IsNullOrWhiteSpace(json))throw new IOException("No NP2 configuration request is retained for this connector.");
   if(connector<1||connector>3)throw new ArgumentOutOfRangeException("connector");
   string folder=Path.Combine(Path.GetTempPath(),"WILDX_NP2Prepare_"+Guid.NewGuid().ToString("N"));
   string input=Path.Combine(folder,"input.npconfig.json"),normalized=Path.Combine(folder,"normalized.npconfig.json"),selection=Path.Combine(folder,"selection.n2b1");
   try{
    Directory.CreateDirectory(folder);File.WriteAllText(input,NormalizeNp2Request(json,connector));
    if(Run("--prepare-v2 "+Quote(input)+" "+Quote(normalized)+" "+Quote(selection)+" "+connector).GetAwaiter().GetResult()!=0)
     throw new IOException("NP2 configuration compiler declined the request.");
    string prepared=NormalizeNp2Request(ReadResult(normalized),connector);RequireInitialNp2013Scope(prepared);
    return new Np2ConfigRequest(prepared,File.ReadAllBytes(selection));
   }finally{
    try{if(File.Exists(input))File.Delete(input);if(File.Exists(normalized))File.Delete(normalized);if(File.Exists(selection))File.Delete(selection);if(Directory.Exists(folder))Directory.Delete(folder,false);}catch(IOException){}
   }
  }
  internal static NpConfigRequest PrepareV1Request(string json,int connector){
   if(connector<1 || connector>3)throw new ArgumentOutOfRangeException("connector");
   string folder=Path.Combine(Path.GetTempPath(),"WILDX_NP1Prepare_"+Guid.NewGuid().ToString("N"));
   string input=Path.Combine(folder,"input.json"),normalized=Path.Combine(folder,"normalized.json"),selection=Path.Combine(folder,"selection.bin");
   try{
    Directory.CreateDirectory(folder);File.WriteAllText(input,json);
    if(Run("--prepare-v1 "+Quote(input)+" "+Quote(normalized)+" "+Quote(selection)+" "+connector).GetAwaiter().GetResult()!=0)
     throw new IOException("NP1 configuration compiler declined the request.");
    var result=new NpConfigRequest(ReadResult(normalized),File.ReadAllBytes(selection));
    if(result.Port!=connector-1)throw new IOException("Configuration returned a different connector.");
    return result;
   }finally{
    try{if(File.Exists(input))File.Delete(input);if(File.Exists(normalized))File.Delete(normalized);if(File.Exists(selection))File.Delete(selection);if(Directory.Exists(folder))Directory.Delete(folder,false);}catch(IOException){}
   }
  }
  internal static string Receipt(NpConfigReply reply,NpConfigRequest request){
   if(reply.Error!=0)throw new IOException("Firmware rejected configuration, code "+reply.Error);
   if(reply.PortState==3 && request!=null)NpConfigClient.RequireApplied(reply,request.Selection);
   return ReceiptText(reply.PortState,reply.PayloadCrc,reply.ConfigurationId)+"\r\nVerification: "+reply.Verification+
    " · "+((reply.Verification&4)!=0?"NRIC ready indication":(reply.Verification&3)==3?"NP1 base readback / shank ACK":"Required verification unconfirmed")+". Analog signals not verified."+(request==null?"\r\nNo host request to compare.":reply.PortState==3?"\r\nMatches the applied host request.":"\r\nApplication of this host request is unconfirmed.");
  }
  internal static string Receipt(Np2ConfigReply reply,Np2ConfigRequest request){
   if(reply.Error!=0)throw new IOException("Firmware rejected configuration, code "+reply.Error);
   if(reply.PortState==3 && request!=null)Np2ConfigClient.RequireApplied(reply,request);
   return ReceiptText(reply.PortState,reply.PayloadCrc,reply.ConfigurationId)+"\r\nVerification: "+reply.Verification+
    " · NP2 chain verification. Analog signals not verified."+(request==null?"\r\nNo host request to compare.":reply.PortState==3?"\r\nMatches the applied host request.":"\r\nApplication of this host request is unconfirmed.")+"\r\n"+Np2ConfigClient.AppliedWarning(reply);
  }
  static string ReceiptText(byte state,uint crc,byte[] id){
   string[] names={"No staged map","Receiving","Staged; application pending","Applied","Failed; application unconfirmed"};
   return "Device receipt · "+(state<names.Length?names[state]:"Unknown state "+state)+"\r\nID: "+BitConverter.ToString(id).Replace("-","")+"\r\nPayload CRC32: "+crc.ToString("X8");
  }
  private static void RequireInitialNp2013Scope(string json){
   var document=new JavaScriptSerializer {MaxJsonLength=1024*1024}.Deserialize<Dictionary<string,object>>(json);object profile,channels,revision;
   if(document==null || !document.TryGetValue("Profile",out profile) || (string)profile!="Np2Multi")throw new IOException("Initial V2 configuration supports NP2013 four-shank requests only.");
   if(document.TryGetValue("ModelRevision",out revision) && ((string)revision=="24" || (string)revision=="NP2010"))throw new IOException("Legacy gain-80 NP2010 requests cannot be applied as NP2013 gain-100 configuration.");
   if(!document.TryGetValue("Channels",out channels) || !(channels is System.Collections.IEnumerable))throw new IOException("NP2013 configuration has no channel assignments.");
  }
  private void RetainResult(string result){
    if(np2){result=NormalizeNp2Request(result,SelectedConnector);requests[SelectedConnector]=result;if(applyConnectors!=null){pendingApplyConnectors.Add(SelectedConnector);selectingConnector=true;applyOnCapture.Checked=true;selectingConnector=false;}}
    else {var prepared=PrepareV1Request(result,SelectedConnector);result=prepared.Json;v1Requests[prepared.Port]=prepared;requests[SelectedConnector]=result;pendingApplyConnectors.Add(SelectedConnector);selectingConnector=true;applyOnCapture.Checked=true;selectingConnector=false;}
   RequestedJson=result;
  }
  internal async Task ImportFile(string path){
   string folder=Path.Combine(Path.GetTempPath(),"WILDX_NP2Import_"+Guid.NewGuid().ToString("N")),output=Path.Combine(folder,"request.json");
   busy=true;actions.Enabled=false;
   try{
    var input=new FileInfo(path);if(!input.Exists || input.Length==0 || input.Length>1024*1024)throw new IOException("Missing or oversized NP configuration file.");
    Directory.CreateDirectory(folder);
    if(await Run("--import-imro "+Quote(path)+" "+Quote(output))!=0)throw new IOException("NP configuration import failed.");
    RetainResult(ReadResult(output));
   }finally{
    busy=false;actions.Enabled=true;RefreshStatus();
    try{if(File.Exists(output))File.Delete(output);if(Directory.Exists(folder))Directory.Delete(folder,false);}catch(IOException){}
   }
  }
  internal static string ReadResult(string path) {
   var info=new FileInfo(path);
   if(!info.Exists || info.Length==0 || info.Length>1024*1024)throw new IOException("Missing or oversized configurator result.");
   return File.ReadAllText(path);
  }
  private static string Quote(string value) {
   if(value.IndexOf('"')>=0)throw new ArgumentException("Invalid quoted file path.");
   return "\""+value+"\"";
  }
  internal static async Task<int> Run(string arguments) {
   if(!File.Exists(HelperPath))throw new FileNotFoundException("NP configurator is missing. Reinstall the complete USB console package.",HelperPath);
   using(var process=new Process()) {
    process.StartInfo=new ProcessStartInfo(HelperPath,arguments) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
    process.Start();var error=process.StandardError.ReadToEndAsync();await Task.Run(()=>process.WaitForExit()).ConfigureAwait(false);string detail=await error.ConfigureAwait(false);
    if(process.ExitCode!=0 && process.ExitCode!=2)throw new IOException("NP configurator: "+(String.IsNullOrWhiteSpace(detail)?"exit "+process.ExitCode:detail.Trim()));
    return process.ExitCode;
   }
  }
  private async Task Edit() {
   string folder=Path.Combine(Path.GetTempPath(),"USB512_NPConfig_"+Guid.NewGuid().ToString("N"));
   string input=Path.Combine(folder,"input.npconfig.json"),output=Path.Combine(folder,"result.npconfig.json");
   busy=true;actions.Enabled=false;
   try {
    Directory.CreateDirectory(folder);
    if(RequestedJson!=null)File.WriteAllText(input,RequestedJson);
    status.Text="Configurator is open. Finish or cancel that window to return.\r\nNo settings are being sent to hardware.";
    int exit=await Run((np2?"--host-edit-np2 ":"--host-edit ")+Quote(RequestedJson==null?"-":input)+" "+Quote(output)+" "+SelectedConnector);
    if(exit==2)return;
    if(exit!=0)throw new IOException("Configurator failed (exit "+exit+"). Requires the .NET 8 Desktop Runtime; the previous request was retained.");
    // Validate with the exact shared mapping/schema rules before adopting it.
    string result=ReadResult(output);
    if(await Run("--validate "+Quote(output))!=0)throw new IOException("Returned NP configuration failed shared validation.");
    RetainResult(result);
   } catch(Exception ex) {MessageBox.Show(this,ex.Message,"NP configuration",MessageBoxButtons.OK,MessageBoxIcon.Error);}
   finally {
    busy=false;actions.Enabled=true;RefreshStatus();
    // Delete only this invocation's two known files; never recursively remove paths.
    try {if(File.Exists(input))File.Delete(input);if(File.Exists(output))File.Delete(output);
      if(Directory.Exists(folder))Directory.Delete(folder,false);}catch(IOException){}
   }
  }
 }
}
