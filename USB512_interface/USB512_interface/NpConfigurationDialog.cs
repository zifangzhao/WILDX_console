using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using WildNpConfiguration;

namespace USB512_interface {
 // Physical shank/site overview of the retained host request. The shared
 // configurator remains responsible for validating and editing physical routing.
 internal sealed class NpConfigurationMap : Control {
  internal sealed class Channel {internal int Number,Shank,Site;internal bool Active;internal string Detail;}
  internal Channel[] Channels {get;private set;}
  readonly ToolTip tip=new ToolTip();
  int shanks=1,sitesPerShank=960;bool np2,staggered;
  Channel[,] assigned;
  string request,caption="No channel map loaded";int hover=-1;
  internal NpConfigurationMap(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);BackColor=WildXTheme.Surface;}
  internal void SetRequest(string json){
   if(request==json)return;request=json;Channels=null;assigned=null;hover=-1;tip.SetToolTip(this,"");caption="No channel map loaded";
   if(json!=null)try {
    var document=new JavaScriptSerializer {MaxJsonLength=1024*1024}.Deserialize<Dictionary<string,object>>(json);
    string profile=(string)document["Profile"];np2=profile=="Np2Single" || profile=="Np2Multi";
    if(!np2 && profile!="Np1" && profile!="Nric1384")throw new FormatException();
    shanks=profile=="Np2Multi"?4:1;sitesPerShank=np2?1280:profile=="Np1"?960:384;
    object revision;document.TryGetValue("ModelRevision",out revision);staggered=profile=="Np1" || Equals(revision,"NP2006");
    var rows=document["Channels"] as IList;if(rows==null || rows.Count!=384)throw new FormatException();
    var channels=new Channel[384];
    foreach(Dictionary<string,object> row in rows){
     int number=Convert.ToInt32(row["Channel"]),shank=Convert.ToInt32(row["Shank"]),site=row["Site"]==null?-1:Convert.ToInt32(row["Site"]);
     if(number<0 || number>=384 || channels[number]!=null || shank<0 || shank>=shanks || site< -1 || site>=sitesPerShank)throw new FormatException();
     bool active=site>=0 && !Convert.ToBoolean(row["Standby"]);
     channels[number]=new Channel {Number=number,Shank=shank,Site=site,Active=active,
      Detail="Channel "+(number+1)+" (IMRO "+number+") · Shank "+shank+" · "+(site<0?"Unassigned":"Site "+site+" · Bank "+site/384)+
       "\r\n"+(active?"Selected":"Standby")+" · AP gain "+row["ApGain"]+" · LFP gain "+row["LfpGain"]+" · "+row["Reference"]};
    }
    var physical=new Channel[shanks,sitesPerShank];foreach(var channel in channels)if(channel.Site>=0){
     if(physical[channel.Shank,channel.Site]!=null)throw new FormatException();physical[channel.Shank,channel.Site]=channel;
    }
    Channels=channels;assigned=physical;
    caption=(np2?"NP2":profile=="Np1"?"NP1":"NRIC1384")+" · "+channels.Count(c=>c.Active)+" / 384 selected";
   }catch(Exception ex)when(ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is NullReferenceException){caption="Channel map unavailable";}
   AccessibleDescription=caption;Invalidate();
  }
  // Rotate the physical probe sideways so three complete probe maps fit in
  // stacked cards. Longitudinal electrode positions follow the shared geometry;
  // shaft width is enlarged so both columns and NP1 stagger remain visible.
  int MapLeft {get{return TextRenderer.MeasureText("S3",Font).Width+16;}}
  int MapRight {get{return Math.Max(MapLeft+40,ClientSize.Width-45);}}
  int MapTop {get{return Font.Height*2+12;}}
  float ShankStep {get{return Math.Max(12,(ClientSize.Height-MapTop-8)/(float)shanks);}}
  float ShankCenter(int shank){return MapTop+(shank+.5f)*ShankStep;}
  float ShaftWidth {get{return Math.Min(30,ShankStep*.65f);}}
  internal RectangleF SiteBounds(int shank,int site){
   var geometry=new NpPreviewSite(-1,site,shank,np2);
   float transverse=geometry.XUm-shank*250;
   if(np2 && staggered && (site/2)%2==0)transverse+=16;
   float rowPitch=np2?15:20;
   float step=(MapRight-MapLeft)/(float)(sitesPerShank/2-1);
   return new RectangleF(MapLeft+geometry.YUm/rowPitch*step,ShankCenter(shank)-ShaftWidth/2+4+transverse/70* (ShaftWidth-4),
    Math.Max(.7f,step*.75f),Math.Max(1,12f/70*(ShaftWidth-4)));
  }
  internal RectangleF ChannelBounds(int number){var ch=Channels[number];return ch.Site<0?RectangleF.Empty:SiteBounds(ch.Shank,ch.Site);}
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);TextRenderer.DrawText(e.Graphics,caption,Font,new Point(0,0),WildXTheme.Muted);
   if(Channels==null)return;
   using(var selected=new SolidBrush(WildXTheme.Accent))using(var inactive=new SolidBrush(Color.FromArgb(168,181,190)))
   using(var substrate=new SolidBrush(WildXTheme.SurfaceSoft))using(var line=new Pen(WildXTheme.Border))using(var reserved=new SolidBrush(Color.FromArgb(181,126,51))){
    int length=MapRight-MapLeft;
    for(int bank=0;bank*384<sitesPerShank;bank++){
     int left=MapLeft+bank*384*length/sitesPerShank,right=MapLeft+Math.Min((bank+1)*384,sitesPerShank)*length/sitesPerShank;
     TextRenderer.DrawText(e.Graphics,"B"+bank,Font,new Rectangle(left,Font.Height+4,right-left,Font.Height),WildXTheme.Muted,TextFormatFlags.HorizontalCenter);
     e.Graphics.DrawLine(line,left,MapTop-2,left,ClientSize.Height-2);
    }
    var baseBounds=new RectangleF(MapRight+8,MapTop,22,ShankStep*shanks);
    e.Graphics.FillRectangle(inactive,baseBounds);
    for(int shank=0;shank<shanks;shank++){
     float center=ShankCenter(shank),half=ShaftWidth/2;
     var outline=new[]{new PointF(MapLeft-14,center),new PointF(MapLeft-3,center-half),new PointF(MapRight+12,center-half),new PointF(MapRight+12,center+half),new PointF(MapLeft-3,center+half)};
     e.Graphics.FillPolygon(substrate,outline);e.Graphics.DrawPolygon(line,outline);
     TextRenderer.DrawText(e.Graphics,"S"+shank,Font,new Point(0,(int)center-Font.Height/2),WildXTheme.Muted);
     // Paint unselected sites first so subpixel overview rows cannot hide a
     // selected site. Exact channel/site identities remain available on hover.
     for(int pass=0;pass<2;pass++)for(int site=0;site<sitesPerShank;site++){
      var ch=assigned[shank,site];bool active=ch!=null && ch.Active;if(active!=(pass==1))continue;
      bool reference=!np2 && sitesPerShank==960 && (site==191 || site==575 || site==959);
      e.Graphics.FillRectangle(active?selected:reference?reserved:inactive,SiteBounds(shank,site));
     }
    }
   }
  }
  protected override void OnMouseMove(MouseEventArgs e){
   base.OnMouseMove(e);int next=-1;string detail="";
   if(Channels!=null && e.X>=MapLeft-2 && e.X<=MapRight+2)for(int shank=0;shank<shanks;shank++){
    if(Math.Abs(e.Y-ShankCenter(shank))>ShaftWidth/2+2)continue;
    int row=Math.Max(0,Math.Min(sitesPerShank/2-1,(int)Math.Round((e.X-MapLeft)*(sitesPerShank/2.0-1)/(MapRight-MapLeft))));
    int site=row*2;var first=SiteBounds(shank,site);var second=SiteBounds(shank,site+1);
    if(Math.Abs(e.Y-(second.Top+second.Height/2))<Math.Abs(e.Y-(first.Top+first.Height/2)))site++;
    next=shank*sitesPerShank+site;var ch=assigned[shank,site];detail=ch==null?"Shank "+shank+" · Site "+site+" · Bank "+site/384+" · Not selected":ch.Detail;break;
   }
   if(next==hover)return;hover=next;tip.SetToolTip(this,detail);
  }
  protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
 }

 // The shared .NET 8 editor runs separately from the .NET Framework recorder.
 // The editor remains file-only; device staging is a separate explicit action.
 internal sealed class NpConfigurationDialog : Form {
  internal string RequestedJson {get;private set;}
  private readonly FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,AutoSize=true,Padding=new Padding(16,10,16,2)};
  private readonly Panel scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(16,12,16,0)};
  private readonly WildXCard[] cards=new WildXCard[3];
  private readonly Label[] summaries=new Label[3];
  private readonly NpConfigurationMap[] maps=new NpConfigurationMap[3];
  private readonly CheckBox[] applyOnCapture=new CheckBox[3];
  private bool busy,refreshing;
  private readonly bool np2;
  private readonly int availableMask;
  private readonly Dictionary<int,string> requests;
  private readonly ISet<int> applyConnectors;
  private readonly HashSet<int> pendingApplyConnectors;
  private readonly Dictionary<byte,NpConfigRequest> v1Requests;
  private readonly Func<int,string,Task<string>> readReceipt;
  internal static string HelperPath {get{return Path.Combine(Path.GetDirectoryName(typeof(NpConfigurationDialog).Assembly.Location),"NPConfigurator","NPConfigurator.exe");}}
  internal NpConfigurationDialog(string current,string devicePath=null,bool np2=false,int availableMask=7,IDictionary<int,string> retainedRequests=null,ISet<int> applyConnectors=null,
   IDictionary<byte,NpConfigRequest> retainedV1Requests=null,Func<int,string,Task<string>> receipt=null) {
   if((availableMask&7)==0)throw new ArgumentOutOfRangeException("availableMask");
   this.np2=np2;this.availableMask=availableMask;requests=new Dictionary<int,string>(retainedRequests??new Dictionary<int,string>());this.applyConnectors=applyConnectors;pendingApplyConnectors=new HashSet<int>(applyConnectors??new HashSet<int>());
   v1Requests=new Dictionary<byte,NpConfigRequest>(retainedV1Requests??new Dictionary<byte,NpConfigRequest>());readReceipt=receipt;
   if(!np2)foreach(var request in v1Requests){requests[request.Key+1]=request.Value.Json;pendingApplyConnectors.Add(request.Key+1);}
   RequestedJson=current;Text="Probe configuration";Icon=NpPreviewTheme.ApplicationIcon;
   ClientSize=new Size(960,790);MinimumSize=new Size(800,520);StartPosition=FormStartPosition.CenterParent;
   var stack=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
   stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(stack);
   for(int p=1;p<=3;p++) {
    int port=p,index=p-1;bool available=(availableMask&(1<<index))!=0;
    var card=new WildXCard {Dock=DockStyle.Fill,Height=227,Padding=new Padding(12),Margin=new Padding(0,0,0,10),Name="probe"+port,Tag=port};cards[index]=card;
    var layout=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,132));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    summaries[index]=WildXTheme.Label("",true);summaries[index].Dock=DockStyle.Fill;layout.Controls.Add(summaries[index],0,0);
    maps[index]=new NpConfigurationMap {Dock=DockStyle.Fill,Margin=Padding.Empty,AccessibleName="Probe "+port+" channel selection map"};layout.Controls.Add(maps[index],0,1);
    var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Padding.Empty,Enabled=available};
    var load=new Button {Text="Load IMRO / JSON…",AutoSize=true};
    load.Click+=async delegate {
     using(var file=new OpenFileDialog {Title="Probe "+port+" configuration",Filter="IMRO or configuration|*.imro;*.npconfig.json;*.json|IMRO|*.imro|NP configuration|*.json"})
      if(file.ShowDialog(this)==DialogResult.OK)try{await ImportFile(file.FileName,port);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Probe "+port,MessageBoxButtons.OK,MessageBoxIcon.Error);}
    };
    var edit=new Button {Text="Edit channel map…",AutoSize=true};edit.Click+=async delegate {await Edit(port);};
    var clear=new Button {Text="Clear",AutoSize=true};clear.Click+=delegate {requests.Remove(port);v1Requests.Remove((byte)index);pendingApplyConnectors.Remove(port);RequestedJson=null;RefreshStatus();};
    var apply=new CheckBox {Text="Apply before recording",AutoSize=true,Margin=new Padding(8,8,0,0)};applyOnCapture[index]=apply;
    apply.CheckedChanged+=delegate {if(refreshing)return;if(apply.Checked)pendingApplyConnectors.Add(port);else pendingApplyConnectors.Remove(port);RefreshStatus();};
    buttons.Controls.AddRange(new Control[]{load,edit,clear,apply});
    if(readReceipt!=null){var device=new Button {Text="Receipt…",AutoSize=true};device.Click+=async delegate {
     if(busy)return;SetBusy(true);
     try{string json;requests.TryGetValue(port,out json);string receiptText=await readReceipt(port,json);using(var report=new NpReportForm("Probe "+port+" receipt",receiptText))report.ShowDialog(this);}
     catch(Exception ex){MessageBox.Show(this,"Configuration not confirmed: "+ex.Message,"Probe "+port,MessageBoxButtons.OK,MessageBoxIcon.Error);}
     finally{SetBusy(false);}
    };buttons.Controls.Add(device);}
    layout.Controls.Add(buttons,0,2);card.Controls.Add(layout);layout.SizeChanged+=delegate {card.Height=layout.Height+card.Padding.Vertical;};stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));stack.Controls.Add(card,0,index);
   }
   var keep=new Button {Text="Save settings",AutoSize=true};var cancel=new Button {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
   keep.Click+=delegate {
    if(!np2 && retainedV1Requests!=null){retainedV1Requests.Clear();foreach(var request in v1Requests)retainedV1Requests.Add(request.Key,request.Value);}
    if(np2 && retainedRequests!=null){retainedRequests.Clear();foreach(var request in requests)retainedRequests.Add(request.Key,request.Value);}
    if(np2 && applyConnectors!=null){applyConnectors.Clear();foreach(int port in pendingApplyConnectors)applyConnectors.Add(port);}
    DialogResult=DialogResult.OK;Close();
   };
   actions.Controls.AddRange(new Control[]{keep,cancel});Controls.Add(scroll);Controls.Add(actions);CancelButton=cancel;
   WildXTheme.Apply(this);BackColor=scroll.BackColor=WildXTheme.Canvas;WildXTheme.Button(keep,true);RefreshStatus();
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
  }
  private void SetBusy(bool value){busy=value;scroll.Enabled=actions.Enabled=!value;}
  private void RefreshStatus() {
   refreshing=true;
   try {for(int index=0;index<3;index++){
    int port=index+1;string json;requests.TryGetValue(port,out json);bool available=(availableMask&(1<<index))!=0;
    summaries[index].Text="Probe "+port+" · "+(!available?"Unavailable":json==null?"No map loaded":pendingApplyConnectors.Contains(port)?"Apply before recording":"Map retained");
    maps[index].SetRequest(json);applyOnCapture[index].Checked=pendingApplyConnectors.Contains(port);
    applyOnCapture[index].Enabled=available && np2 && json!=null;
   }}finally{refreshing=false;}
  }
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
  private void RetainResult(string result,int port){
   if(np2){result=NormalizeNp2Request(result,port);requests[port]=result;if(applyConnectors!=null)pendingApplyConnectors.Add(port);}
   else {var prepared=PrepareV1Request(result,port);result=prepared.Json;v1Requests[prepared.Port]=prepared;requests[port]=result;pendingApplyConnectors.Add(port);}
   RequestedJson=result;
  }
  internal async Task ImportFile(string path,int port){
   RequireAvailable(port);if(busy)throw new InvalidOperationException("Finish the current configuration action first.");
   string folder=Path.Combine(Path.GetTempPath(),"WILDX_NPImport_"+Guid.NewGuid().ToString("N")),output=Path.Combine(folder,"request.json");
   SetBusy(true);
   try{
    var input=new FileInfo(path);if(!input.Exists || input.Length==0 || input.Length>1024*1024)throw new IOException("Missing or oversized NP configuration file.");
    Directory.CreateDirectory(folder);
    if(await Run("--import-imro "+Quote(path)+" "+Quote(output))!=0)throw new IOException("NP configuration import failed.");
    RetainResult(ReadResult(output),port);
   }finally{
    SetBusy(false);RefreshStatus();
    try{if(File.Exists(output))File.Delete(output);if(Directory.Exists(folder))Directory.Delete(folder,false);}catch(IOException){}
   }
  }
  private void RequireAvailable(int port){if(port<1 || port>3 || (availableMask&(1<<(port-1)))==0)throw new ArgumentOutOfRangeException("port","Probe connector is unavailable.");}
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
  private async Task Edit(int port) {
   RequireAvailable(port);if(busy)return;string request;requests.TryGetValue(port,out request);
   string folder=Path.Combine(Path.GetTempPath(),"USB512_NPConfig_"+Guid.NewGuid().ToString("N"));
   string input=Path.Combine(folder,"input.npconfig.json"),output=Path.Combine(folder,"result.npconfig.json");
   SetBusy(true);
   try {
    Directory.CreateDirectory(folder);
    if(request!=null)File.WriteAllText(input,request);
    int exit=await Run((np2?"--host-edit-np2 ":"--host-edit ")+Quote(request==null?"-":input)+" "+Quote(output)+" "+port);
    if(exit==2)return;
    if(exit!=0)throw new IOException("Configurator failed (exit "+exit+"). Requires the .NET 8 Desktop Runtime; the previous request was retained.");
    // Validate with the exact shared mapping/schema rules before adopting it.
    string result=ReadResult(output);
    if(await Run("--validate "+Quote(output))!=0)throw new IOException("Returned NP configuration failed shared validation.");
    RetainResult(result,port);
   } catch(Exception ex) {MessageBox.Show(this,ex.Message,"NP configuration",MessageBoxButtons.OK,MessageBoxIcon.Error);}
   finally {
    SetBusy(false);RefreshStatus();
    // Delete only this invocation's two known files; never recursively remove paths.
    try {if(File.Exists(input))File.Delete(input);if(File.Exists(output))File.Delete(output);
      if(Directory.Exists(folder))Directory.Delete(folder,false);}catch(IOException){}
   }
  }
 }
}
