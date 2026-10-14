using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace USB512_interface {
 public partial class Form1 {
  ComboBox gpioRtcDevices;Button gpioRtcRefresh;Label gpioRtcStatus;
  TextBox gpioRtcInfo;string gpioRtcLastInfo;
  NpGpioRtcLogger gpioRtcLogger;bool refreshingGpioRtc;
  readonly HashSet<string> recordingWarnings=new HashSet<string>(StringComparer.Ordinal);
  internal Action<string> recordingWarningPresenter=null;
  internal Func<string,string,bool> gpioRtcDevicePresent=(port,id)=>NpGpioRtcDevice.Find().Any(d=>
   String.Equals(d.Port,port,StringComparison.OrdinalIgnoreCase) && String.Equals(d.Identity,id,StringComparison.OrdinalIgnoreCase));
  Control BuildGpioRtcControls() {
   var row=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
   row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,88));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,105));
   row.Controls.Add(new Label {Text="GPIO / RTC",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=WildXTheme.Muted},0,0);
   gpioRtcDevices=new ComboBox {Name="gpioRtcDevices",Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="GPIO RTC logger"};
   gpioRtcDevices.Items.Add(new NpGpioRtcDevice());gpioRtcDevices.SelectedIndex=0;
   gpioRtcDevices.SelectedIndexChanged+=delegate {
    if(refreshingGpioRtc)return;var selected=gpioRtcDevices.SelectedItem as NpGpioRtcDevice;
    gpioRtcLastInfo=null;RefreshGpioRtcInfo();
    try{Properties.Settings.Default.GpioRtcDevice=selected==null || selected.Port==null?"off":selected.Identity;Properties.Settings.Default.Save();}
    catch(Exception ex){gpioRtcStatus.Text="Save failed";WarnRecordingOnce("gpio-settings","GPIO/RTC selection could not be saved: "+ex.Message);}
   };
   row.Controls.Add(gpioRtcDevices,1,0);gpioRtcRefresh=new Button {Text="Refresh",Dock=DockStyle.Fill};WildXTheme.Button(gpioRtcRefresh);
   gpioRtcRefresh.Click+=delegate {RefreshGpioRtcDevices();};row.Controls.Add(gpioRtcRefresh,2,0);
   gpioRtcStatus=new Label {Name="gpioRtcStatus",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=WildXTheme.Muted};row.Controls.Add(gpioRtcStatus,3,0);
   return row;
  }
  Control BuildGpioRtcInfo() {
   gpioRtcInfo=new TextBox {Name="gpioRtcInfo",Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,
    ScrollBars=ScrollBars.Vertical,BackColor=WildXTheme.Surface,ForeColor=WildXTheme.Ink,Text="Off"};
   return Card("GPIO / RTC",null,gpioRtcInfo);
  }
  void RefreshGpioRtcInfo() {
   if(gpioRtcInfo==null)return;
   var selected=gpioRtcDevices.SelectedItem as NpGpioRtcDevice;
   gpioRtcInfo.Text=gpioRtcLogger!=null?gpioRtcLogger.Describe(gpioRtcStatus.Text):gpioRtcLastInfo??
    (selected==null || selected.Port==null?"Off":selected.Port+" · "+(selected.Port=="Unavailable"?"Unavailable":"Ready"));
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
   finally{refreshingGpioRtc=false;gpioRtcLastInfo=null;RefreshGpioRtcInfo();}
  }
  void StartGpioRtcRecording() {
   recordingWarnings.Clear();
   gpioRtcLastInfo=null;
   gpioRtcDevices.Enabled=gpioRtcRefresh.Enabled=false;
   var selected=gpioRtcDevices.SelectedItem as NpGpioRtcDevice;
   if(selected==null || selected.Port==null){gpioRtcStatus.Text="";RefreshGpioRtcInfo();return;}
   if(npRecordingSettings!=null)npRecordingSettings.Context("gpio_rtc",NpRecordingSettings.Fields(
    "requested",true,"port",selected.Port,"pnp_device_id",selected.Identity,"session_file","gpio_rtc_session.json",
    "events_file","gpio_rtc_events.txt","clock_queries_file","gpio_rtc_sync.csv"));
   try {
    if(!gpioRtcDevicePresent(selected.Port,selected.Identity))throw new IOException("The selected GPIO/RTC board is unavailable. Refresh and select its COM port.");
    gpioRtcLogger=new NpGpioRtcLogger(runFolder,selected);gpioRtcLogger.Start();gpioRtcStatus.Text="Logging";
   }catch(Exception ex) {
    if(gpioRtcLogger!=null){gpioRtcLogger.Dispose();gpioRtcLogger=null;}
    gpioRtcStatus.Text="Failed";
    gpioRtcLastInfo=selected.Port+" · Failed"+Environment.NewLine+ex.Message;
    try {File.WriteAllText(Path.Combine(runFolder,"gpio_rtc_start_error.txt"),DateTime.UtcNow.ToString("O")+Environment.NewLine+ex+Environment.NewLine);}
    catch(Exception logError){ex=new IOException(ex.Message+"; could not save GPIO/RTC failure: "+logError.Message,ex);}
    WarnRecordingOnce("gpio-start",ex.Message+"\r\n\r\nNeural recording will continue without GPIO/RTC logging.");
   }
   RefreshGpioRtcInfo();
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
    logger.NeuralFirstReceiveUtc=recordingSummaryStartUtc==DateTime.MinValue?(DateTime?)null:recordingSummaryStartUtc;
    logger.Dispose();gpioRtcStatus.Text=logger.Error==null?"Stopped":"Error";
    gpioRtcLastInfo=logger.Describe(gpioRtcStatus.Text);
    if(logger.Warning!=null)WarnRecordingOnce(logger.WarningKey,logger.Warning+"\r\n\r\nSee gpio_rtc_session.json in the recording folder.");
    gpioRtcLogger=null;
   }
   gpioRtcDevices.Enabled=gpioRtcRefresh.Enabled=true;
   RefreshGpioRtcInfo();
  }
  void ObserveNpImu(NpImuDisplay display,bool stopped) {
   imuTelemetry.SetNpSample(display,stopped);if(display==null)return;
   var messages=new List<string>();
   if(display.ImuError!=0 && recordingWarnings.Add("imu"))messages.Add("IMU error "+display.ImuError+". IMU data may be unavailable or invalid.");
   if(display.EnvironmentError!=0 && recordingWarnings.Add("environment"))messages.Add("Environment sensor error "+display.EnvironmentError+".");
   if(display.Overflow!=0 && recordingWarnings.Add("imu-overflow"))messages.Add("IMU queue overflow. Sensor samples have been lost.");
   if(display.Error!=null && recordingWarnings.Add("sensor-monitor"))messages.Add("Sensor monitoring error: "+display.Error);
   if(messages.Count>0)PresentRecordingWarning(String.Join("\r\n",messages)+"\r\n\r\nCheck the sensor status and capture report."+(stopped?" Recording has stopped.":" Neural recording continues unless a capture error is reported."));
  }
  void WarnRecordingOnce(string key,string text){if(recordingWarnings.Add(key))PresentRecordingWarning(text);}
  void PresentRecordingWarning(string text) {
   if(!String.IsNullOrEmpty(runFolder))try{File.AppendAllText(Path.Combine(runFolder,"recording_warnings.txt"),DateTime.UtcNow.ToString("O")+" "+text+Environment.NewLine);}
    catch(Exception ex){text+="\r\nWarning log could not be saved: "+ex.Message;}
   // Post the modal dialog after the Start/Stop callback; USB workers keep running.
   string warning=text;PostToUi(delegate {
    if(recordingWarningPresenter!=null)recordingWarningPresenter(warning);
    else MessageBox.Show(this,warning,"Recording warning",MessageBoxButtons.OK,MessageBoxIcon.Warning);
   });
  }
 }
}
