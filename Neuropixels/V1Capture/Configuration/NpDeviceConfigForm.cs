using System;using System.IO;using System.Diagnostics;using System.Drawing;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
namespace WildNpConfiguration {
 public sealed class NpDeviceConfigForm:Form {
  readonly Func<byte[],CancellationToken,Task<NpConfigReply>> stage;
  readonly Func<byte,CancellationToken,Task<NpConfigReply>> query;
  readonly string helper;
  readonly Label status=new Label{Dock=DockStyle.Fill,Padding=new Padding(12)};
  readonly FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=100,WrapContents=true};
  readonly ComboBox port=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=82};
  readonly int availableMask;
  readonly IDictionary<byte,NpConfigRequest> requests;
  byte[] payload;bool busy;string requested;
  string lastReceipt;
  public string RequestedJson {get{return requested;}}
  public NpDeviceConfigForm(string helperPath,Func<byte[],CancellationToken,Task<NpConfigReply>> upload,Func<byte,CancellationToken,Task<NpConfigReply>> read,string initial=null,int connectedMask=7,IDictionary<byte,NpConfigRequest> retainedRequests=null){
   helper=helperPath;stage=upload;query=read;requested=initial;availableMask=connectedMask<1||connectedMask>7?7:connectedMask;
   requests=retainedRequests??new Dictionary<byte,NpConfigRequest>();
   for(int i=1;i<=3;i++)if((availableMask&(1<<(i-1)))!=0)port.Items.Add(i);
   if(port.Items.Count==0)for(int i=1;i<=3;i++)port.Items.Add(i);
   port.SelectedIndex=0;
   Text="NP configuration — connected probe settings";ClientSize=new Size(800,360);StartPosition=FormStartPosition.CenterParent;
   var import=new Button{Text="Load IMRO / JSON…",AutoSize=true};
   var clear=new Button{Text="Clear host request",AutoSize=true};
   var edit=new Button{Text="Open NP configurator…",AutoSize=true};
   var send=new Button{Text="Stage on connected logger",AutoSize=true};
   var check=new Button{Text="Read device receipt",AutoSize=true};
   var close=new Button{Text="Close",AutoSize=true};
   var save=new Button{Text="Save request + receipt…",AutoSize=true};
   edit.Click+=async(s,e)=>await RunUi(Edit);
   import.Click+=async(s,e)=>await RunUi(Import);
   clear.Click+=(s,e)=>{requests.Remove((byte)(SelectedPort-1));SelectPort();status.Text+="\r\nThis does not undo settings already staged on the device.";};
   send.Click+=async(s,e)=>await RunUi(Stage);
   check.Click+=async(s,e)=>await RunUi(async()=>ShowReceipt(await query((byte)(SelectedPort-1),CancellationToken.None)));
   close.Click+=(s,e)=>Close();
   save.Click+=async(s,e)=>await RunUi(Save);
   actions.Controls.AddRange(new Control[]{new Label{Text="Connected probe",AutoSize=true,Padding=new Padding(0,6,0,0)},port,import,edit,send,check,save,clear,close});
   Controls.Add(status);Controls.Add(actions);
   status.Text="NP1 / NRIC only. Configuration commands require compatible V1 firmware.\r\nConnected probes: "+ConnectedPorts()+".\r\n\r\n"+
    "1. Edit the probe type, sites, gains and references.\r\n2. Stage the configuration while recording is stopped.\r\n3. It is applied during the next probe bring-up; then read the device receipt.\r\n\r\n"+
    "The connected-probe dropdown sets the port; IMRO has no connector. Other ports' requests are retained separately.\r\n"+
    "The host restages selected requests after the final recording mode/reset, before START, then verifies applied receipts before release.\r\n"+
    "NP1 base chains are read back; the shank chain is ACK-only. A receipt does not verify analog signals.";
   FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
   port.SelectedIndexChanged+=(s,e)=>SelectPort();
   if(retainedRequests!=null)SelectPort();
  }
  int SelectedPort {get{return port.SelectedItem==null?1:(int)port.SelectedItem;}}
  void SelectPort(){
   NpConfigRequest r;payload=null;requested=null;lastReceipt=null;
   if(requests.TryGetValue((byte)(SelectedPort-1),out r)){payload=r.Selection;requested=r.Json;}
   status.Text="P"+SelectedPort+" — "+(payload==null?"no retained host request":"request retained for next START; device application not yet confirmed")+"\r\n\r\n"+
    "Load IMRO / JSON for this port, or edit in the configurator. IMRO does not encode the connector.\r\nOther ports' requests are preserved.\r\n\r\n"+
    "The host restages selected requests after the final recording mode/reset, before START. Capture is released only after matching applied receipts.\r\n"+
    "Immediate staging requires stopped compatible firmware. NP1 base chains are read back; the shank chain is ACK-only. Analog signals are not verified.";
  }
  string ConnectedPorts(){string s="";foreach(object item in port.Items)s+=(s.Length==0?"":", ")+"P"+item;return s;}
  async Task RunUi(Func<Task> work){if(busy)return;busy=true;actions.Enabled=false;
   try{await work();}catch(Exception ex){status.Text="Configuration not confirmed: "+ex.Message+"\r\nRead the device receipt before retrying an interrupted upload.";}
   finally{busy=false;actions.Enabled=true;}}
  static string Quote(string s){if(s.Contains("\""))throw new ArgumentException("Invalid path");return "\""+s+"\"";}
  async Task<int> Helper(string args){
   if(!File.Exists(helper))throw new FileNotFoundException("Bundled NP configurator missing; install the complete package.",helper);
   using(var p=Process.Start(new ProcessStartInfo(helper,args){UseShellExecute=false,RedirectStandardError=true})){
    var error=p.StandardError.ReadToEndAsync();await Task.Run(()=>p.WaitForExit());string detail=await error;
    if(p.ExitCode!=0 && p.ExitCode!=2)throw new IOException("NP configurator: "+(String.IsNullOrWhiteSpace(detail)?"exit "+p.ExitCode:detail.Trim()));
    return p.ExitCode;}
  }
  async Task Prepare(string source,string dir){
   string normalized=Path.Combine(dir,"prepared.json"),binary=Path.Combine(dir,"selection.bin");
   await Helper("--prepare-v1 "+Quote(source)+" "+Quote(normalized)+" "+Quote(binary)+" "+SelectedPort);
   if(new FileInfo(normalized).Length>1024*1024)throw new IOException("Oversized configuration");
   var r=new NpConfigRequest(File.ReadAllText(normalized),File.ReadAllBytes(binary));
   if(r.Port!=SelectedPort-1)throw new IOException("Configurator returned a different connected port.");
   requests[r.Port]=r;SelectPort();
   status.Text+="\r\nID: "+BitConverter.ToString(payload,8,32).Replace("-","");
  }
  static void RemoveTemporary(string dir){
   foreach(string name in new[]{"in.json","out.json","prepared.json","selection.bin"}){string file=Path.Combine(dir,name);if(File.Exists(file))File.Delete(file);}
   Directory.Delete(dir,false);
  }
  async Task Import(){
   using(var choose=new OpenFileDialog{Filter="NP configuration or IMRO|*.npconfig.json;*.json;*.imro|IMRO|*.imro|NP configuration|*.json"}){
    if(choose.ShowDialog(this)!=DialogResult.OK)return;
    string dir=Path.Combine(Path.GetTempPath(),"WILD_NP_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
    try{await Prepare(choose.FileName,dir);}finally{RemoveTemporary(dir);}
   }
  }
  async Task Edit(){
   string dir=Path.Combine(Path.GetTempPath(),"WILD_NP_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
   try{
    string input=Path.Combine(dir,"in.json"),output=Path.Combine(dir,"out.json"),binary=Path.Combine(dir,"selection.bin");
    if(requested!=null)File.WriteAllText(input,requested);
    int rc=await Helper("--host-edit "+Quote(requested==null?"-":input)+" "+Quote(output)+" "+SelectedPort);if(rc==2)return;
    if(rc!=0)throw new IOException("Configurator failed (requires .NET 8 Desktop Runtime).");
    await Prepare(output,dir);
   }finally{
    RemoveTemporary(dir);
   }
  }
  Task Save(){
   if(requested==null)throw new InvalidOperationException("No configuration request to save.");
   using(var choose=new SaveFileDialog{Filter="NP configuration|*.npconfig.json",FileName="configuration.npconfig.json"}){
    if(choose.ShowDialog(this)==DialogResult.OK){File.WriteAllText(choose.FileName,requested);
     File.WriteAllText(choose.FileName+".receipt.txt",lastReceipt??"No device receipt. Request only; not confirmed applied.");}
   }return Task.CompletedTask;
  }
  async Task Stage(){
   if(payload==null)throw new InvalidOperationException("Open and accept a configuration first.");
   if(SelectedPort!=payload[5]+1)throw new InvalidOperationException("The request does not belong to this connected port.");
   status.Text="Staging a validated transaction. No partial upload will program the probe…";
   ShowReceipt(await stage(payload,CancellationToken.None));
  }
  void ShowReceipt(NpConfigReply r){
   if(r.Error!=0)throw new IOException("Firmware rejected command: "+r.Error);
   string[] names={"No staged configuration (firmware defaults)","Receiving","Staged — NOT applied yet","Applied — verification limits below","FAILED — configuration not verified"};
   status.Text="Probe "+SelectedPort+" — "+(r.PortState<names.Length?names[r.PortState]:"Unknown state")+"\r\nID: "+BitConverter.ToString(r.ConfigurationId).Replace("-","")+"\r\nPayload CRC: "+r.PayloadCrc.ToString("X8")+
    "\r\n"+((r.Verification&1)!=0?"NP1 base chains: readback matched. ":"")+((r.Verification&2)!=0?"NP1 shank: ACK only, not readback verified. ":"")+((r.Verification&4)!=0?"NRIC: shift-register ready only, not full readback. ":"")+
    "\r\n\r\nDevice settings are volatile; the host retains each port request and restages it before START after the final mode/reset.\r\nRecordings retain matching JSON and applied receipts; channel gains are not inferred from this receipt.";
   bool matching=payload!=null && payload[5]+1==SelectedPort && r.PayloadCrc==NpConfigClient.Crc(payload,payload.Length);
   if(matching)for(int i=0;i<32;i++)if(payload[8+i]!=r.ConfigurationId[i]){matching=false;break;}
   if(!matching)status.Text+="\r\nWARNING: receipt does not match the currently edited request.";
   lastReceipt=DateTime.UtcNow.ToString("o")+"\r\n"+status.Text;
  }
 }
}
