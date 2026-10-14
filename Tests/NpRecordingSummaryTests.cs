using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

// Delivered GUI with ordinary local files and simulated receive/Stop events.
// Main Load is removed: no discovery, USB, BLE, recording or firmware actions.
static class NpRecordingSummaryTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Type type;static int checks;
 static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static object Call(object form,string name,params object[] args){try{return type.GetMethod(name,All).Invoke(form,args);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 static void Set(object form,string name,object value){type.GetField(name,All).SetValue(form,value);}
 static void Kind(object form,string name){var f=type.GetField("selectedDeviceKind",All);f.SetValue(form,Enum.Parse(f.FieldType,name));}
 static string Text(object form){return ((Label)type.GetField("recordingSummary",All).GetValue(form)).Text;}
 [STAThread]static int Main(string[] args){try{
  type=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("USB512_interface.Form1",true);string root=Path.Combine(Path.GetFullPath(args[1]),"run_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string first=Path.Combine(root,"recording1"),second=Path.Combine(root,"recording2");Directory.CreateDirectory(first);Directory.CreateDirectory(second);Directory.CreateDirectory(Path.Combine(first,"probe2"));
  File.WriteAllBytes(Path.Combine(first,"neuropixels_ap.dat"),new byte[1024]);File.WriteAllBytes(Path.Combine(first,"probe2","neuropixels_lfp.dat"),new byte[512]);
  using(var form=(Form)Activator.CreateInstance(type)) {
   form.Load-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),form,type.GetMethod("Form1_Load",All));form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
   Check(Text(form).Contains("Elapsed 00:00:00")&&Text(form).Contains("Recorded size 0 B"),"Idle metrics missing");
   Kind(form,"XeNp");Set(form,"saveFolder",first);Set(form,"runFolder",first);Call(form,"ResetRecordingSummary",first);DateTime start=DateTime.UtcNow;
   Call(form,"RefreshRecordingSummary",start.AddMinutes(2));Check(Text(form).Contains("Elapsed 00:00:00"),"Preparation time counted as NP recording");Check(Text(form).Contains("Recorded size 1.50 KiB")&&Text(form).Contains("Disk free ")&&Text(form).Contains(" GB"),"Actual files or destination disk space missing");
   Check((long)Call(null,"RecordingFolderBytes",first)==1536,"Nested recording files omitted");
   Call(form,"NpFirstReceive",start,first);Call(form,"RefreshRecordingSummary",start.AddSeconds(65));Check(Text(form).Contains("Elapsed 00:01:05"),"V1 first-data callback did not start elapsed clock without auto recovery");
   Call(form,"ObserveRecordingSummary",start.AddSeconds(50),first);Call(form,"RefreshRecordingSummary",start.AddSeconds(65));Check(Text(form).Contains("Elapsed 00:01:05"),"Later progress restarted recording clock");
   Call(form,"ObserveRecordingSummary",start.AddHours(-1),second);Check((DateTime)type.GetField("recordingSummaryStartUtc",All).GetValue(form)==start,"Stale folder callback changed current clock");
   Call(form,"FreezeRecordingSummary",start.AddSeconds(65));Call(form,"RefreshRecordingSummary",start.AddHours(5));Check(Text(form).Contains("Elapsed 00:01:05"),"Stopped time kept increasing");
   using(var file=new FileStream(Path.Combine(first,"neuropixels_ap.dat"),FileMode.Append,FileAccess.Write,FileShare.Read)){file.Write(new byte[2048],0,2048);file.Flush();Thread.Sleep(1150);Application.DoEvents();Check(Text(form).Contains("Recorded size 3.50 KiB")&&Text(form).Contains("Elapsed 00:01:05"),"One-second UI timer did not refresh actual file growth independently of frozen time");}
   ((Label)type.GetField("lblRecInfo",All).GetValue(form)).Text="Stopped; metadata warning: retained sidecar";Check(Text(form).Contains("Recorded size 3.50 KiB"),"Status message overwrote persistent metrics");
   Kind(form,"NpV2");Set(form,"runFolder",second);Call(form,"ResetRecordingSummary",second);Check(Text(form).Contains("Elapsed 00:00:00")&&Text(form).Contains("Recorded size 0 B"),"New timestamped recording retained previous totals");
   Call(form,"NpV2ObserveReceive",start,start.AddSeconds(3),second);Call(form,"RefreshRecordingSummary",start.AddHours(25).AddSeconds(1));Check(Text(form).Contains("Elapsed 25:00:01"),"V2 clock absent or wrapped after 24 hours");
   Call(form,"RefreshRecordingSummary",start.AddSeconds(-1));Check(Text(form).Contains("Elapsed 00:00:00"),"Clock adjustment displayed negative recording time");
   Check(Call(null,"RecordingFolderBytes",Path.Combine(root,"missing"))==null&&(string)Call(null,"RecordingSize",(object)null)=="n/a","Unavailable file size fabricated as zero");
   Call(form,"ResetRecordingSummary",first);Call(form,"NpFirstReceive",start,first);Call(form,"FreezeRecordingSummary",start.AddSeconds(65));Call(form,"RefreshRecordingSummary",start.AddSeconds(65));
   foreach(int width in new[]{1100,1450}) {
    form.ClientSize=new Size(width,920);Application.DoEvents();var summary=(Label)type.GetField("recordingSummary",All).GetValue(form);var parent=(TableLayoutPanel)summary.Parent;Check(parent.ClientRectangle.Contains(summary.Bounds)&&summary.Height>=summary.GetPreferredSize(new Size(summary.Width,0)).Height,"Metrics clipped after resize");
    using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"recording-summary-"+width+".png"));}
   }
   form.Close();
  }
  Console.WriteLine("PASS "+checks+" delivered-GUI recording summary checks: V1/V2 first data, no preparation time, live file growth and disk space, Stop retention, fresh recordings, stale callbacks, >24h and resized rendering; no hardware. Images: "+root);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
