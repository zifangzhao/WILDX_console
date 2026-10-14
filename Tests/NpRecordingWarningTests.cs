using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class NpRecordingWarningTests {
 const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 [STAThread]static int Main(string[] args){try {
  var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));var type=assembly.GetType("USB512_interface.Form1",true);var sampleType=assembly.GetType("USB512_interface.NpImuDisplay",true);
  string folder=Path.Combine(Path.GetFullPath(args[1]),"warnings_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  using(var form=(Form)Activator.CreateInstance(type)) {
   form.Load-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),form,type.GetMethod("Form1_Load",All));form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
   type.GetField("runFolder",All).SetValue(form,folder);int shown=0;type.GetField("recordingWarningPresenter",All).SetValue(form,new Action<string>(s=>shown++));
   var info=(TextBox)type.GetField("gpioRtcInfo",All).GetValue(form);Control infoCard=info;while(infoCard.Name!="gpioRtcCard")infoCard=infoCard.Parent;var split=(TableLayoutPanel)infoCard.Parent;
   Check(split.GetRow(infoCard)==1 && info.Text=="Off","GPIO information panel is missing below device health");
   var leds=(Label[])type.GetField("gpioPortLeds",All).GetValue(form);var counts=(Label[])type.GetField("gpioPortCounts",All).GetValue(form);
   var rates=(Label[])type.GetField("gpioPortRates",All).GetValue(form);
   Action<double[]> frequency=hz=>type.GetMethod("UpdateGpioPortFrequency",All).Invoke(form,new object[]{hz});
   Check(Array.TrueForAll(rates,l=>l.Text=="0.0 Hz"),"idle port frequency missing");
   Action<long[],bool,DateTime> activity=(n,on,now)=>type.GetMethod("UpdateGpioPortActivity",All).Invoke(form,new object[]{n,on,now});DateTime pulse=DateTime.UtcNow;
   activity(new long[]{1,0,3,0},true,pulse);Color active=leds[0].ForeColor,inactive=leds[1].ForeColor;
   Check(active!=inactive && leds[2].ForeColor==active && leds[3].ForeColor==inactive,"GPIO pulses did not map CH0/CH2 to ports 1/3");
   activity(new long[]{1,0,3,0},true,pulse.AddMilliseconds(349));Check(leds[0].ForeColor==active,"short GPIO event was invisible before 350 ms");
   activity(new long[]{1,0,3,0},true,pulse.AddMilliseconds(350));Check(leds[0].ForeColor==inactive && leds[2].ForeColor==inactive,"idle GPIO LEDs stayed lit as logic levels");
   activity(new long[]{1,1000,3,0},true,pulse.AddMilliseconds(400));Check(leds[1].ForeColor==active && counts[1].Text=="1,000" && leds[0].ForeColor==inactive,"GPIO event burst lost count or lit wrong port");
   frequency(new double[]{12,1000,30,0});Check(rates[0].Text=="12.0 Hz" && rates[1].Text=="1000.0 Hz" && rates[2].Text=="30.0 Hz" && rates[3].Text=="0.0 Hz","port frequency mapping/units incorrect");
   activity(new long[]{1,1000,3,0},false,pulse.AddMilliseconds(450));Check(leds[1].ForeColor==inactive && counts[1].Text=="1,000","Stop did not dim LEDs while retaining counts");
   Check(Array.TrueForAll(rates,l=>l.Text=="0.0 Hz"),"stopped logger still displayed live Hz");
   activity(new long[4],false,pulse.AddMilliseconds(500));Check(Array.TrueForAll(counts,l=>l.Text=="0"),"new GPIO recording retained old port counts");
   var sample=Activator.CreateInstance(sampleType);sampleType.GetField("Values",All).SetValue(sample,new double[9]);
   Action observe=()=>{type.GetMethod("ObserveNpImu",All).Invoke(form,new object[]{sample,false});Application.DoEvents();};
   observe();Check(shown==0,"healthy sensors produced warning");
   sampleType.GetField("ImuError",All).SetValue(sample,2U);sampleType.GetField("EnvironmentError",All).SetValue(sample,3U);sampleType.GetField("Overflow",All).SetValue(sample,1U);observe();
   Check(shown==1,"initial sensor errors were not grouped into one warning");
   for(int i=0;i<50;i++)observe();Check(shown==1,"repeated packet errors reopened warning");
   sampleType.GetField("ImuError",All).SetValue(sample,4U);observe();Check(shown==1,"increasing IMU error counter reopened warning");
   sampleType.GetField("ImuError",All).SetValue(sample,2U);observe();Check(shown==1,"recurring acknowledged error reopened popup");
   type.GetMethod("StartGpioRtcRecording",All).Invoke(form,null);observe();Check(shown==2,"new recording did not reset warning acknowledgments");
   type.GetMethod("StopGpioRtcRecording",All).Invoke(form,null);
   Check(File.ReadAllText(Path.Combine(folder,"recording_warnings.txt")).Contains("IMU error 2"),"warning was not stored with recording");
   sampleType.GetField("Error",All).SetValue(sample,"invalid optional snapshot");observe();observe();Check(shown==3,"optional monitoring error was silent or repeated");
   type.GetMethod("WarnRecordingOnce",All).Invoke(form,new object[]{"sensor-monitor","same final sensor error"});Application.DoEvents();Check(shown==3,"final result repeated an already shown monitor error");
   // Unavailable configured board must not block neural acquisition or substitute another COM port.
   var deviceType=assembly.GetType("USB512_interface.NpGpioRtcDevice",true);var device=Activator.CreateInstance(deviceType);
   deviceType.GetField("Port",All).SetValue(device,"COM77");deviceType.GetField("Identity",All).SetValue(device,"fixture-only");
   type.GetField("gpioRtcDevicePresent",All).SetValue(form,new Func<string,string,bool>((p,id)=>false));
   type.GetField("refreshingGpioRtc",All).SetValue(form,true);var combo=(ComboBox)type.GetField("gpioRtcDevices",All).GetValue(form);combo.Items.Add(device);combo.SelectedItem=device;type.GetField("refreshingGpioRtc",All).SetValue(form,false);
   type.GetField("running",All).SetValue(form,true);type.GetMethod("StartGpioRtcRecording",All).Invoke(form,null);Application.DoEvents();
   Check(shown==4 && (bool)type.GetField("running",All).GetValue(form) && type.GetField("gpioRtcLogger",All).GetValue(form)==null,"failed GPIO start canceled neural recording or failed to warn");
   Check(File.ReadAllText(Path.Combine(folder,"gpio_rtc_start_error.txt")).Contains("unavailable"),"failed GPIO start missing recording-folder evidence");
   Check(info.Text.Contains("COM77") && info.Text.Contains("Failed"),"GPIO start failure missing from GPIO information panel");
   type.GetMethod("StopGpioRtcRecording",All).Invoke(form,null);type.GetField("running",All).SetValue(form,false);
   // Optional per-probe outputs reference the shared recording-root GPIO files.
   var settingsType=assembly.GetType("USB512_interface.NpRecordingSettings",true);
   var settings=Activator.CreateInstance(settingsType,All,null,new object[]{folder,"","live_usb",false,(DateTime?)DateTime.UtcNow,"fixture","fixture"},null);
   settingsType.GetMethod("Actual",All).Invoke(settings,new object[]{20000,20000,3,false,true,"fixture"});
   settingsType.GetMethod("Context",All).Invoke(settings,new object[]{"gpio_rtc",new Dictionary<string,object>{{"session_file","gpio_rtc_session.json"},{"events_file","gpio_rtc_events.txt"},{"clock_queries_file","gpio_rtc_sync.csv"}}});
   File.WriteAllText(Path.Combine(folder,"gpio_rtc_events.txt"),"fixture event");File.WriteAllText(Path.Combine(folder,"gpio_rtc_sync.csv"),"fixture query");
   settingsType.GetMethod("Write",All).Invoke(settings,null);
   Check(!File.Exists(Path.Combine(folder,"probe1","gpio_rtc_events.txt")) && !File.Exists(Path.Combine(folder,"probe2","gpio_rtc_sync.csv")),"per-probe bundle copied a partial GPIO log");
   Check(File.ReadAllText(Path.Combine(folder,"probe2","recording_settings.json")).Contains("../gpio_rtc_events.txt"),"per-probe bundle lost shared GPIO reference");
   var kind=type.GetField("selectedDeviceKind",All);kind.SetValue(form,Enum.Parse(kind.FieldType,"XeNp"));
   type.GetMethod("FinishCaptureOnUi",All).Invoke(form,new object[]{"Capture error: simulated USB disconnect"});Application.DoEvents();
   Check(shown==5 && !(bool)type.GetField("running",All).GetValue(form),"capture failure did not stop and warn once");
   type.GetMethod("FinishCaptureOnUi",All).Invoke(form,new object[]{"Capture error: simulated USB disconnect"});Application.DoEvents();Check(shown==5,"repeated completion reopened capture warning");
   info.Text="COM77 · Logging\r\nEvents: 1,001 | Clock: RTT 2 ms\r\nLast: CH1 · RTC 100999 ms";
   foreach(int width in new[]{1044,1100,1450}) {
    form.ClientSize=new Size(width,920);Application.DoEvents();var network=form.Controls.Find("networkTimeCard",true)[0];
    Check(network.Parent==split && network.Top-network.Margin.Top==infoCard.Bottom+infoCard.Margin.Bottom && infoCard.Height>=280,"GPIO panel does not extend to network-time panel after resize");
    Check(info.ClientSize.Height>=info.Font.Height*3 && Array.TrueForAll(leds,l=>l.Parent.ClientRectangle.Contains(l.Bounds)) && Array.TrueForAll(counts,l=>l.Parent.ClientRectangle.Contains(l.Bounds)) && Array.TrueForAll(rates,l=>l.Parent.ClientRectangle.Contains(l.Bounds) && l.Height>=l.Font.Height),"GPIO text/LEDs/Hz clipped after resize");
   }
   form.ClientSize=new Size(1100,920);Application.DoEvents();
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(folder,"gpio-panel-1100.png"));}
   form.Close();
  }
  Console.WriteLine("PASS "+checks+" delivered-GUI warning/panel/port-LED checks; no discovery or hardware.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
