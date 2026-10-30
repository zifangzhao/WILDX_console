using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace USB512_interface {
 public partial class Form1 {
  ComboBox gpioRtcDevices;Button gpioRtcRefresh;Label gpioRtcStatus;CheckBox gpioRtcEnabled;
  TextBox gpioRtcInfo;string gpioRtcLastInfo;
  sealed class GpioChannelRow {
   internal Label Id,Led,Count,Frequency;internal long Events;internal DateTime PulseUntil;
   internal Label[] Cells {get{return new[]{Id,Led,Count,Frequency};}}
  }
  readonly SortedDictionary<byte,GpioChannelRow> gpioRows=new SortedDictionary<byte,GpioChannelRow>();
  TableLayoutPanel gpioChannelTable;Panel gpioChannelViewport;
  NpGpioRtcLogger gpioRtcLogger;bool refreshingGpioRtc;
  string gpioRtcRecordingFolder;
  readonly HashSet<string> recordingWarnings=new HashSet<string>(StringComparer.Ordinal);
  internal Action<string> recordingWarningPresenter=null;
  internal Func<string,string,bool> gpioRtcDevicePresent=(port,id)=>NpGpioRtcDevice.Find().Any(d=>
   String.Equals(d.Port,port,StringComparison.OrdinalIgnoreCase) && String.Equals(d.Identity,id,StringComparison.OrdinalIgnoreCase));
  Control BuildGpioRtcControls() {
   var row=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
   row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,88));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,105));
   gpioRtcEnabled=new CheckBox {Name="gpioRtcEnabled",Text="GPIO / RTC logging",AutoSize=true,Anchor=AnchorStyles.Left,Margin=Padding.Empty,
    Checked=Properties.Settings.Default.GpioRtcEnabled};row.Controls.Add(gpioRtcEnabled,0,0);
   gpioRtcDevices=new ComboBox {Name="gpioRtcDevices",Anchor=AnchorStyles.Left|AnchorStyles.Right,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="GPIO RTC logger",Margin=Padding.Empty};
   gpioRtcDevices.Items.Add(new NpGpioRtcDevice());gpioRtcDevices.SelectedIndex=0;
   gpioRtcDevices.SelectedIndexChanged+=delegate {
    if(refreshingGpioRtc)return;var selected=gpioRtcDevices.SelectedItem as NpGpioRtcDevice;
    gpioRtcLastInfo=null;ResetGpioChannelDisplay();RefreshGpioRtcInfo();
    try{Properties.Settings.Default.GpioRtcDevice=selected==null || selected.Port==null?"off":selected.Identity;Properties.Settings.Default.Save();}
    catch(Exception ex){gpioRtcStatus.Text="Save failed";WarnRecordingOnce("gpio-settings","GPIO/RTC selection could not be saved: "+ex.Message);}
   };
   row.Controls.Add(gpioRtcDevices,1,0);gpioRtcRefresh=new Button {Text="Refresh",Dock=DockStyle.Fill};WildXTheme.Button(gpioRtcRefresh);
   gpioRtcRefresh.Margin=new Padding(6,0,8,0);
   gpioRtcRefresh.Click+=delegate {RefreshGpioRtcDevices();};row.Controls.Add(gpioRtcRefresh,2,0);
   gpioRtcStatus=new Label {Name="gpioRtcStatus",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=WildXTheme.Muted};row.Controls.Add(gpioRtcStatus,3,0);
   gpioRtcEnabled.CheckedChanged+=delegate {
    try{Properties.Settings.Default.GpioRtcEnabled=gpioRtcEnabled.Checked;Properties.Settings.Default.Save();}
    catch(Exception ex){WarnRecordingOnce("gpio-settings","GPIO/RTC setting could not be saved: "+ex.Message);}
    if(bleWorkspace!=null && bleWorkspace.HostGpioLogging.Checked!=gpioRtcEnabled.Checked)bleWorkspace.HostGpioLogging.Checked=gpioRtcEnabled.Checked;
    RefreshGpioRtcInfo();UpdateStartEnabled();
   };
   return row;
  }
  Control BuildGpioRtcInfo() {
   gpioRtcInfo=new TextBox {Name="gpioRtcInfo",Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,Margin=Padding.Empty,
    ScrollBars=ScrollBars.Vertical,BackColor=WildXTheme.Surface,ForeColor=WildXTheme.Ink,Text="Off"};
   var content=Stack(48,0);content.Controls.Add(gpioRtcInfo,0,0);
   gpioChannelViewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};
   gpioChannelTable=new TableLayoutPanel {Name="gpioChannelTable",Dock=DockStyle.Top,ColumnCount=4,RowCount=1,Margin=Padding.Empty,BackColor=WildXTheme.Border};
   foreach(int width in new[]{20,20,30,30})gpioChannelTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
   string[] headers={"ID","Status","Count","Freq (Hz)"};
   for(int c=0;c<headers.Length;c++) {
    var header=GpioTableCell(headers[c],c==0?ContentAlignment.MiddleLeft:c==1?ContentAlignment.MiddleCenter:ContentAlignment.MiddleRight);
    header.Font=WildXTheme.Strong;header.BackColor=WildXTheme.SurfaceSoft;gpioChannelTable.Controls.Add(header,c,0);
   }
   gpioChannelViewport.Controls.Add(gpioChannelTable);content.Controls.Add(gpioChannelViewport,0,1);
   SetGpioChannelRows(NpGpioRtcLogger.DefaultChannelIds,true);
   var activityTimer=new Timer {Interval=100};components.Add(activityTimer);
   activityTimer.Tick+=delegate {bool logging=gpioRtcLogger!=null && gpioRtcLogger.IsLogging;
    var activity=gpioRtcLogger==null?null:gpioRtcLogger.GetActivity();
    if(activity!=null)SetGpioChannelRows(activity.Ids,false);
    UpdateGpioPortActivity(activity==null?null:activity.Counts,logging,DateTime.UtcNow);
    if(logging)UpdateGpioPortFrequency(activity.Frequencies);
   };activityTimer.Start();
   var card=Card("GPIO / RTC",null,content,true);card.Name="gpioRtcCard";return card;
  }
  static Label GpioTableCell(string text,ContentAlignment alignment) {
   return new Label {Text=text,Dock=DockStyle.Fill,TextAlign=alignment,Margin=new Padding(0,0,1,1),
    Padding=new Padding(4,0,4,0),BackColor=WildXTheme.Surface,ForeColor=WildXTheme.Ink,UseMnemonic=false};
  }
  void SetGpioChannelRows(byte[] ids,bool replace) {
   if(!replace && ids.All(id=>gpioRows.ContainsKey(id)))return;
   gpioChannelTable.SuspendLayout();
   if(replace)foreach(byte id in gpioRows.Keys.Where(id=>!ids.Contains(id)).ToArray()) {
    foreach(var cell in gpioRows[id].Cells){gpioChannelTable.Controls.Remove(cell);cell.Dispose();}gpioRows.Remove(id);
   }
   foreach(byte id in ids)if(!gpioRows.ContainsKey(id)) {
    var row=new GpioChannelRow {Id=GpioTableCell(id.ToString(),ContentAlignment.MiddleLeft),Led=GpioTableCell("",ContentAlignment.MiddleCenter),
     Count=GpioTableCell("0",ContentAlignment.MiddleRight),Frequency=GpioTableCell("0.0",ContentAlignment.MiddleRight)};
    row.Id.Name="gpioChannelId"+id;row.Led.Name="gpioChannelLed"+id;row.Count.Name="gpioChannelCount"+id;row.Frequency.Name="gpioChannelRate"+id;
    row.Led.ForeColor=WildXTheme.Border;
    row.Led.Paint+=delegate(object sender,PaintEventArgs e){var led=(Label)sender;
     int diameter=Math.Max(4,Math.Min(led.Font.Height,Math.Min(led.ClientSize.Width,led.ClientSize.Height)-6));
     e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
     using(var brush=new SolidBrush(led.ForeColor))e.Graphics.FillEllipse(brush,(led.Width-diameter)/2F,(led.Height-diameter)/2F,diameter,diameter);
    };
    row.Led.AccessibleName="GPIO channel "+id+" activity";row.Count.AccessibleName="GPIO channel "+id+" events";
    row.Frequency.AccessibleName="GPIO channel "+id+" event frequency in Hz";
    gpioRows.Add(id,row);foreach(var cell in row.Cells)gpioChannelTable.Controls.Add(cell);
   }
   float scale=DeviceDpi/96F;int headerHeight=(int)Math.Ceiling(24*scale),rowHeight=(int)Math.Ceiling(28*scale);
   gpioChannelTable.RowCount=gpioRows.Count+1;gpioChannelTable.RowStyles.Clear();gpioChannelTable.RowStyles.Add(new RowStyle(SizeType.Absolute,headerHeight));
   int r=1;foreach(var row in gpioRows.Values) {
    gpioChannelTable.RowStyles.Add(new RowStyle(SizeType.Absolute,rowHeight));var cells=row.Cells;
    for(int c=0;c<cells.Length;c++)gpioChannelTable.SetCellPosition(cells[c],new TableLayoutPanelCellPosition(c,r));r++;
   }
   gpioChannelTable.Height=headerHeight+rowHeight*gpioRows.Count;
   gpioChannelViewport.AutoScrollMinSize=new Size(0,gpioChannelTable.Height);gpioChannelTable.ResumeLayout(true);
  }
  void ResetGpioChannelDisplay() {
   if(gpioChannelTable==null)return;SetGpioChannelRows(NpGpioRtcLogger.DefaultChannelIds,true);
   UpdateGpioPortActivity(new long[gpioRows.Count],false,DateTime.UtcNow);
  }
  // The wire reports trigger events, not input levels. LEDs pulse for 350 ms.
  void UpdateGpioPortActivity(long[] counts,bool logging,DateTime now) {
   if(!logging)UpdateGpioPortFrequency(null);
   int p=0;foreach(var row in gpioRows.Values) {
    if(counts!=null && p<counts.Length && counts[p]!=row.Events) {
     row.Events=counts[p];row.PulseUntil=logging?now.AddMilliseconds(350):DateTime.MinValue;
     row.Count.Text=counts[p].ToString("N0");
    }
    bool active=logging && now<row.PulseUntil;
    row.Led.ForeColor=active?WildXTheme.Accent:WildXTheme.Border;
    row.Led.AccessibleDescription=active?"Recent event":"No recent event";p++;
   }
  }
  void UpdateGpioPortFrequency(double[] hz) {
   int p=0;foreach(var row in gpioRows.Values){row.Frequency.Text=(hz!=null && p<hz.Length?hz[p]:0).ToString("F1");p++;}
  }
  void RefreshGpioRtcInfo() {
   if(gpioRtcInfo==null)return;
   var selected=gpioRtcDevices.SelectedItem as NpGpioRtcDevice;
   gpioRtcInfo.Text=gpioRtcLogger!=null?gpioRtcLogger.Describe(gpioRtcStatus.Text):gpioRtcLastInfo??
    (gpioRtcEnabled!=null && !gpioRtcEnabled.Checked || selected==null || selected.Port==null?"Off":selected.Port+" · "+(selected.Port=="Unavailable"?"Unavailable":"Ready"));
  }
  void RefreshGpioRtcDevices() {
   if(running || gpioRtcLogger!=null)return;
   refreshingGpioRtc=true;
   try {
    string preferred=Properties.Settings.Default.GpioRtcDevice;
    var found=NpGpioRtcDevice.Find();gpioRtcDevices.Items.Clear();gpioRtcDevices.Items.Add(new NpGpioRtcDevice());
    foreach(var item in found)gpioRtcDevices.Items.Add(item);
    var match=found.FirstOrDefault(d=>String.Equals(d.Identity,preferred,StringComparison.OrdinalIgnoreCase));
    if(match==null && String.IsNullOrEmpty(preferred) && found.Count==1)match=found[0];
    if(match==null && !String.IsNullOrEmpty(preferred) && preferred!="off") {
     // Retain the configured identity instead of silently logging a different board.
     match=new NpGpioRtcDevice {Identity=preferred,Port="Unavailable"};gpioRtcDevices.Items.Add(match);
    }
    gpioRtcDevices.SelectedItem=match??gpioRtcDevices.Items[0];gpioRtcStatus.Text=match==null?"":match.Port=="Unavailable"?"Unavailable":"Ready";
   }catch(Exception ex){gpioRtcStatus.Text="Scan failed";WarnRecordingOnce("gpio-scan","GPIO/RTC device detection failed: "+ex.Message);}
   finally{refreshingGpioRtc=false;gpioRtcLastInfo=null;ResetGpioChannelDisplay();RefreshGpioRtcInfo();}
  }
  void StartGpioRtcRecording() {
   StartGpioRtcForRecording(runFolder,npRecordingSettings);
  }
  void StartGpioRtcForRecording(string folder,NpRecordingSettings settings) {
   gpioRtcRecordingFolder=folder;
   recordingWarnings.Clear();
   if(gpioRtcLogger!=null) {
    // NP recovery owns new neural files; the independent RTC stream retains
    // its open COM port, clock observations, counts and original output files.
    try{SetGpioRtcContextForRecording(folder,settings);}catch(Exception ex){WarnRecordingOnce("gpio-link","GPIO/RTC continues, but its recording link could not be saved: "+ex.Message);}
    RefreshGpioRtcInfo();return;
   }
   gpioRtcLastInfo=null;
   ResetGpioChannelDisplay();
   if(gpioRtcEnabled==null || !gpioRtcEnabled.Checked){
    if(settings!=null)settings.Context("gpio_rtc",NpRecordingSettings.Fields("requested",false));
    gpioRtcStatus.Text="Off";RefreshGpioRtcInfo();return;
   }
   gpioRtcEnabled.Enabled=false;
   gpioRtcDevices.Enabled=gpioRtcRefresh.Enabled=false;
   var selected=gpioRtcDevices.SelectedItem as NpGpioRtcDevice;
   if(settings!=null)settings.Context("gpio_rtc",NpRecordingSettings.Fields("requested",true,"port",selected==null?null:selected.Port));
   try {
    if(selected==null || selected.Port==null)throw new IOException("GPIO/RTC logging is enabled. Select a GPIO/RTC device in USB Mode.");
    SetGpioRtcContextForRecording(folder,settings);
    if(!gpioRtcDevicePresent(selected.Port,selected.Identity))throw new IOException("The selected GPIO/RTC board is unavailable. Refresh and select its COM port.");
    gpioRtcLogger=new NpGpioRtcLogger(folder,selected);gpioRtcLogger.Start();gpioRtcStatus.Text="Logging";
   }catch(Exception ex) {
    if(gpioRtcLogger!=null){gpioRtcLogger.Dispose();gpioRtcLogger=null;}
    gpioRtcStatus.Text="Failed";
    gpioRtcLastInfo=(selected==null?"GPIO/RTC":selected.Port)+" · Failed"+Environment.NewLine+ex.Message;
    try {File.WriteAllText(Path.Combine(folder,"gpio_rtc_start_error.txt"),DateTime.UtcNow.ToString("O")+Environment.NewLine+ex+Environment.NewLine);}
    catch(Exception logError){ex=new IOException(ex.Message+"; could not save GPIO/RTC failure: "+logError.Message,ex);}
    WarnRecordingOnce("gpio-start",ex.Message+"\r\n\r\nNeural recording will continue without GPIO/RTC logging.");
   }
   RefreshGpioRtcInfo();
  }
  string GpioRtcFileReferenceForRecording(string folder,string name) {
   string target=Path.GetFullPath(Path.Combine(gpioRtcLogger==null?folder:gpioRtcLogger.DirectoryPath,name));
   var source=new Uri(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar);
   var relative=source.MakeRelativeUri(new Uri(target));
   return relative.IsAbsoluteUri?target:Uri.UnescapeDataString(relative.ToString()).Replace('/',Path.DirectorySeparatorChar);
  }
  void SetGpioRtcContextForRecording(string folder,NpRecordingSettings settings) {
   var selected=gpioRtcDevices.SelectedItem as NpGpioRtcDevice;
   if(selected==null || selected.Port==null)return;
   var context=NpRecordingSettings.Fields("requested",true,"port",selected.Port,"pnp_device_id",selected.Identity,
    "session_file",GpioRtcFileReferenceForRecording(folder,"gpio_rtc_session.json"),"events_file",GpioRtcFileReferenceForRecording(folder,"gpio_rtc_events.txt"),
    "clock_queries_file",GpioRtcFileReferenceForRecording(folder,"gpio_rtc_sync.csv"),"continuous_across_neural_interruptions",true,
    "host_epoch",NpHostClock.Epoch,"clock_reference","pc_wall_clock; network correction separate");
   if(settings!=null)settings.Context("gpio_rtc",context);
   if(gpioRtcLogger!=null && !String.Equals(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
    Path.GetFullPath(gpioRtcLogger.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
    NpMetadataFile.WriteText(Path.Combine(folder,"gpio_rtc_link.json"),
     new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(context)+Environment.NewLine);
  }
  void RememberGpioRtcNeuralStart() {
   // BLE uses its own receipt folder. A previous USB recording's GUI summary
   // must not become the first-receive evidence of that unrelated session.
   if(gpioRtcRecordingFolder!=null && !String.Equals(gpioRtcRecordingFolder,runFolder,StringComparison.OrdinalIgnoreCase))return;
   if(gpioRtcLogger!=null && recordingSummaryStartUtc!=DateTime.MinValue &&
    (!gpioRtcLogger.NeuralFirstReceiveUtc.HasValue || recordingSummaryStartUtc<gpioRtcLogger.NeuralFirstReceiveUtc.Value))
    gpioRtcLogger.NeuralFirstReceiveUtc=recordingSummaryStartUtc;
  }
  void FinishGpioRtcCapture() {
   RememberGpioRtcNeuralStart();
   if(selectedDeviceKind!=DeviceKind.XeNp && selectedDeviceKind!=DeviceKind.NpV2)StopGpioRtcRecording();
   else {
    RefreshGpioRtcInfo();
    // Null releases the logger/file owner after explicit Stop. The NP owner
    // has now published its final canonical time, which may follow GPIO Stop.
    if(gpioRtcLogger==null)NpGpioTimeCorrection.QueueCompleted(runFolder);
   }
  }
  void PollGpioRtcRecording() {
   if(gpioRtcLogger==null)return;
   gpioRtcStatus.Text=gpioRtcLogger.Error!=null?"Error":!gpioRtcLogger.ClockSynchronized?"Syncing…":"Logging";
   RefreshGpioRtcInfo();
   string warning=gpioRtcLogger.Warning;
   if(warning!=null)WarnRecordingOnce(gpioRtcLogger.WarningKey,warning+"\r\n\r\nNeural recording continues. See the GPIO/RTC files in the recording folder.");
  }
  void StopGpioRtcRecording() {
   var logger=gpioRtcLogger;
   if(logger!=null) {
    RememberGpioRtcNeuralStart();
    logger.Dispose();gpioRtcStatus.Text=logger.Error==null?"Stopped":"Error";
    gpioRtcLastInfo=logger.Describe(gpioRtcStatus.Text);
    var activity=logger.GetActivity();SetGpioChannelRows(activity.Ids,false);UpdateGpioPortActivity(activity.Counts,false,DateTime.UtcNow);
    if(logger.Warning!=null)WarnRecordingOnce(logger.WarningKey,logger.Warning+"\r\n\r\nSee gpio_rtc_session.json in the recording folder.");
    gpioRtcLogger=null;
    // The first NP folder may already have finalized before a later restart.
    if(captureThread==null || !String.Equals(logger.DirectoryPath,runFolder,StringComparison.OrdinalIgnoreCase))
     try{NpRecordingSettings.RefreshBundle(logger.DirectoryPath);}
     catch(Exception ex){WarnRecordingOnce("gpio-bundle","GPIO/RTC stopped, but its recording index could not be updated: "+ex.Message);}
   }
   gpioRtcDevices.Enabled=gpioRtcRefresh.Enabled=true;if(gpioRtcEnabled!=null)gpioRtcEnabled.Enabled=true;
   RefreshGpioRtcInfo();
  }
  void ObserveNpImu(NpImuDisplay display,bool stopped) {
   imuTelemetry.SetNpSample(display,stopped);if(display==null)return;
   var messages=new List<string>();
   if(display.ImuError!=0 && recordingWarnings.Add("imu"))messages.Add("IMU error "+display.ImuError+". IMU data may be unavailable or invalid.");
   if(display.Overflow!=0 && recordingWarnings.Add("imu-overflow"))messages.Add("IMU queue overflow. Sensor samples have been lost.");
   if(display.Error!=null && recordingWarnings.Add("sensor-monitor"))messages.Add("Sensor monitoring error: "+display.Error);
   if(messages.Count>0)PresentRecordingWarning(String.Join("\r\n",messages)+"\r\n\r\nCheck the sensor status and capture report."+(stopped?" Recording has stopped.":" Neural recording continues unless a capture error is reported."));
  }
  void WarnRecordingOnce(string key,string text){if(recordingWarnings.Add(key))PresentRecordingWarning(text);}
  void PresentRecordingWarning(string text) {
   string folder=gpioRtcRecordingFolder??runFolder;
   if(!String.IsNullOrEmpty(folder))try{File.AppendAllText(Path.Combine(folder,"recording_warnings.txt"),DateTime.UtcNow.ToString("O")+" "+text+Environment.NewLine);}
    catch(Exception ex){text+="\r\nWarning log could not be saved: "+ex.Message;}
   // Post the modal dialog after the Start/Stop callback; USB workers keep running.
   string warning=text;PostToUi(delegate {
    if(recordingWarningPresenter!=null)recordingWarningPresenter(warning);
    else MessageBox.Show(this,warning,"Recording warning",MessageBoxButtons.OK,MessageBoxIcon.Warning);
   });
  }
 }
}
