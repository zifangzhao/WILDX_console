using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

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
  internal static string HelperPath {get{return Path.Combine(Path.GetDirectoryName(typeof(NpConfigurationDialog).Assembly.Location),"NPConfigurator","NPConfigurator.exe");}}
  internal NpConfigurationDialog(string current,string devicePath=null,bool np2=false,int availableMask=7,IDictionary<int,string> retainedRequests=null) {
   this.np2=np2;requests=new Dictionary<int,string>(retainedRequests??new Dictionary<int,string>());
   RequestedJson=current;Text="NP configuration — editor and device staging";
   ClientSize=new Size(820,220);MinimumSize=Size;StartPosition=FormStartPosition.CenterParent;
   var edit=new Button {Text="Open configurator",AutoSize=true};
   var clear=new Button {Text="Clear request",AutoSize=true};
   var keep=new Button {Text="Keep request only",AutoSize=true};
   var cancel=new Button {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
   edit.Click+=async delegate {await Edit();};
   clear.Click+=delegate {RequestedJson=null;RefreshStatus();};
   keep.Click+=delegate {
    if(np2 && retainedRequests!=null){retainedRequests.Clear();foreach(var request in requests)retainedRequests.Add(request.Key,request.Value);}
    DialogResult=DialogResult.OK;Close();
   };
   if(np2){
    Text="NP2 IMRO / channel map — host requests only";ClientSize=new Size(900,300);MinimumSize=Size;actions.Height=84;
    for(int p=1;p<=3;p++)if((availableMask&(1<<(p-1)))!=0)connector.Items.Add(p);
    if(connector.Items.Count==0)connector.Items.AddRange(new object[]{1,2,3});
    connector.SelectedIndex=0;
    connector.SelectedIndexChanged+=delegate {SelectConnector();};
    var load=new Button {Text="Load IMRO / JSON…",AutoSize=true};
    load.Click+=async delegate {using(var file=new OpenFileDialog {Filter="NP2 IMRO or configuration|*.imro;*.npconfig.json;*.json|IMRO|*.imro|NP configuration|*.json"}){
     if(file.ShowDialog(this)==DialogResult.OK)try{await ImportFile(file.FileName);}catch(Exception ex){MessageBox.Show(this,ex.Message,"NP2 IMRO import",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }};
    actions.Controls.AddRange(new Control[]{new Label {Text="Connector",AutoSize=true,Padding=new Padding(0,6,0,0)},connector,load});
    clear.Click+=delegate {requests.Remove(SelectedConnector);RefreshStatus();};
    edit.Text="Edit NP2 channel map…";keep.Text="Keep host requests";SelectConnector();
   }
   actions.Controls.AddRange(new Control[]{edit,clear,keep,cancel});Controls.Add(status);Controls.Add(actions);
   if(!np2 && !String.IsNullOrEmpty(devicePath)){
    var device=new Button{Text="Device staging / receipt…",AutoSize=true};
    device.Click+=delegate{RequestedJson=NpConfigurationUsb.Show(this,devicePath,RequestedJson);RefreshStatus();};actions.Controls.Add(device);
   }
   CancelButton=cancel;RefreshStatus();
   WildXTheme.Apply(this);WildXTheme.Button(keep,true);
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
  }
  private void RefreshStatus() {
   if(np2){
    status.Text="Connector "+SelectedConnector+" — "+(RequestedJson==null?"no host channel map loaded.":"NP2 host request retained.")+"\r\n\r\n"+
     "Load a single- or four-shank NP2 IMRO / JSON file, then edit or save it in the configurator. Each connector retains its own request.\r\n\r\n"+
     "HOST ONLY: current V2 firmware cannot upload/apply these channel maps. Loading does not change acquisition, routing, gains or BLE/SD recording.\r\n"+
     "USB recordings can retain these files as requested/unverified metadata, never as applied settings.\r\n\r\nCancel leaves the previous host requests unchanged.";
    return;
   }
   status.Text="Choose probe type, sites, gains and references in the shared NP configurator.\r\n"+
    "Use its Open / Save controls for .npconfig.json files.\r\n\r\n"+
    "Editing alone does not apply settings. Use Device staging / receipt with compatible firmware.\r\n"+
    "A retained request is saved alongside each recording; it does not change or filter the data.\r\n\r\n"+
    "Cancel / Clear request does not undo settings already staged on the device.\r\n"+
    (RequestedJson==null?"No requested configuration retained.":"Requested configuration retained. Query the device for staged/applied state.");
  }
  private int SelectedConnector {get{return connector.SelectedItem==null?1:(int)connector.SelectedItem;}}
  private void SelectConnector(){requests.TryGetValue(SelectedConnector,out var request);RequestedJson=request;RefreshStatus();}
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
  private void RetainResult(string result){
   if(np2){result=NormalizeNp2Request(result,SelectedConnector);requests[SelectedConnector]=result;}
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
    process.StartInfo=new ProcessStartInfo(HelperPath,arguments) {UseShellExecute=false,RedirectStandardError=true};
    process.Start();var error=process.StandardError.ReadToEndAsync();await Task.Run(()=>process.WaitForExit());string detail=await error;
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
    int exit=await Run((np2?"--host-edit-np2 ":"--host-edit ")+Quote(RequestedJson==null?"-":input)+" "+Quote(output)+(np2?" "+SelectedConnector:""));
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
