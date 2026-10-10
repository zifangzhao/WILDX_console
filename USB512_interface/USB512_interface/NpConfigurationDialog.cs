using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
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
  internal static string HelperPath {get{return Path.Combine(Path.GetDirectoryName(typeof(NpConfigurationDialog).Assembly.Location),"NPConfigurator","NPConfigurator.exe");}}
  internal NpConfigurationDialog(string current,string devicePath=null) {
   RequestedJson=current;Text="NP configuration — editor and device staging";
   ClientSize=new Size(820,220);MinimumSize=Size;StartPosition=FormStartPosition.CenterParent;
   var edit=new Button {Text="Open configurator",AutoSize=true};
   var clear=new Button {Text="Clear request",AutoSize=true};
   var keep=new Button {Text="Keep request only",AutoSize=true};
   var cancel=new Button {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
   edit.Click+=async delegate {await Edit();};
   clear.Click+=delegate {RequestedJson=null;RefreshStatus();};
   keep.Click+=delegate {DialogResult=DialogResult.OK;Close();};
   actions.Controls.AddRange(new Control[]{edit,clear,keep,cancel});Controls.Add(status);Controls.Add(actions);
   if(!String.IsNullOrEmpty(devicePath)){
    var device=new Button{Text="Device staging / receipt…",AutoSize=true};
    device.Click+=delegate{RequestedJson=NpConfigurationUsb.Show(this,devicePath,RequestedJson);RefreshStatus();};actions.Controls.Add(device);
   }
   CancelButton=cancel;RefreshStatus();
   WildXTheme.Apply(this);WildXTheme.Button(keep,true);
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
  }
  private void RefreshStatus() {
   status.Text="Choose probe type, sites, gains and references in the shared NP configurator.\r\n"+
    "Use its Open / Save controls for .npconfig.json files.\r\n\r\n"+
    "Editing alone does not apply settings. Use Device staging / receipt with compatible firmware.\r\n"+
    "A retained request is saved alongside each recording; it does not change or filter the data.\r\n\r\n"+
    "Cancel / Clear request does not undo settings already staged on the device.\r\n"+
    (RequestedJson==null?"No requested configuration retained.":"Requested configuration retained. Query the device for staged/applied state.");
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
    int exit=await Run("--host-edit "+Quote(RequestedJson==null?"-":input)+" "+Quote(output));
    if(exit==2)return;
    if(exit!=0)throw new IOException("Configurator failed (exit "+exit+"). Requires the .NET 8 Desktop Runtime; the previous request was retained.");
    // Validate with the exact shared mapping/schema rules before adopting it.
    string result=ReadResult(output);
    if(await Run("--validate "+Quote(output))!=0)throw new IOException("Returned NP configuration failed shared validation.");
    RequestedJson=result;
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
