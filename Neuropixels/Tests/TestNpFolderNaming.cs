using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

// Exercises the delivered GUI's naming/time code. No source/device discovery,
// USB, BLE scan, firmware update or installer execution.
static class NpFolderNamingTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static Assembly app;static Type naming,catalog,recordType;static int checks;
 static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
 static object Call(Type type,string name,params object[] args){try{return type.GetMethod(name,All).Invoke(null,args);}catch(TargetInvocationException ex){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}}
 static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
 static void Put64(byte[] b,int at,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,8);}
 static uint Crc(byte[] b){uint c=0xffffffff;for(int i=0;i<508;i++){c^=b[i];for(int n=0;n<8;n++)c=c>>1^((c&1)!=0?0xedb88320:0);}return ~c;}
 static void Seal(byte[] b){Put(b,508,Crc(b));}
 static readonly ulong Anchor=1791897600123456UL;
 static byte[] Clock(bool v2){var b=new byte[512];Encoding.ASCII.GetBytes(v2?"N2SDUTC1":"N1SDUTC1").CopyTo(b,0);Put(b,8,1);Put(b,12,42);Put(b,16,3);Put(b,20,1);Put(b,24,1000);Put(b,28,0xfffffff0);Put64(b,32,Anchor);Put(b,v2?48:64,10);Put(b,v2?52:68,11);Put64(b,v2?56:48,Anchor+26000);Put64(b,v2?64:56,Anchor+27000);Put(b,72,15000);Put(b,76,1000);Seal(b);return b;}
 static object Record(bool v2,bool media=false){var r=Activator.CreateInstance(recordType,true);foreach(var entry in new[]{Tuple.Create("Start",(object)0L),Tuple.Create("Session",(object)42U),Tuple.Create("FormatVersion",(object)(v2?12U:media?8U:6U)),Tuple.Create("ApHz",(object)(media?0U:15000U))})recordType.GetField(entry.Item1,All).SetValue(r,entry.Item2);return r;}
 static DateTime? Time(byte[] clock,bool v2,byte[] footer=null){var card=new byte[4096];clock.CopyTo(card,5*512);if(footer!=null)footer.CopyTo(card,4*512);using(var s=new MemoryStream(card))return (DateTime?)Call(catalog,"RecordingStartTime",s,Record(v2,footer!=null));}
 static DateTime Expected(ulong us){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddTicks((long)us*10).ToLocalTime();}
 static void Times(){
  foreach(bool v2 in new[]{false,true}) {
   var clock=Clock(v2);Check(Time(clock,v2)==Expected(Anchor+26000),"Stored START bracket/tick wrap was not used");
   for(int error=0;error<11;error++) {
    var bad=Clock(v2);
    switch(error){case 0:Put(bad,12,43);break;case 1:Put(bad,16,1);break;case 2:Put(bad,20,0);break;case 3:Put(bad,24,0);break;case 4:Put(bad,72,30000);break;case 5:Put(bad,76,0);break;case 6:Put64(bad,32,0);break;case 7:Put64(bad,v2?56:48,Anchor);break;case 8:Put(bad,v2?48:64,0x7fffffff);break;case 9:Put(bad,v2?52:68,9);break;case 10:Put(bad,16,11);break;}
    Seal(bad);Check(Time(bad,v2)==null,"Invalid stored timestamp was presented as a recording date: "+error);
   }
   clock[32]^=1;Check(Time(clock,v2)==null,"Bad clock CRC accepted");Check(Time(new byte[512],v2)==null,"Absent date replaced with current date");
  }
  var padding=Clock(true);padding[80]=1;Seal(padding);Check(Time(padding,true)==null,"V2 reserved padding accepted");
  var footer=new byte[512];Encoding.ASCII.GetBytes("NPSDEND1").CopyTo(footer,0);Put(footer,12,42);Encoding.ASCII.GetBytes("NPMEDX1\0").CopyTo(footer,128);Put(footer,136,1);Put(footer,140,1);Put(footer,144,36);Put(footer,152,10);Put(footer,156,1);Put64(footer,160,Anchor);Put(footer,288,1000);Seal(footer);
  Check(Time(new byte[512],false,footer)==Expected(Anchor+26000),"Media did not use recorded START clock");
  Put(footer,156,0);Seal(footer);Check(Time(new byte[512],false,footer)==null,"Unanchored media received a date");
  var legacy=Clock(false);Put(legacy,72,0);Seal(legacy);Check(Time(legacy,false,footer)==Expected(Anchor+26000),"Media legacy START receipt fallback failed");
 }
 static string Name(string label,DateTime? time,string fallback){return (string)Call(naming,"Name",label,time,fallback);}
 static void Names(string root){
  var date=new DateTime(2026,10,13,14,30,25,123);
  string name=Name("RatID_sleep",date,"default");Check(name=="RatID_sleep_wild_20261013_143025.123","Yoshi's format mismatch");
  Check(Name("  RatID_sleep  ",date,"default")==name,"Whitespace not trimmed");Check(Name(" ",null,"NP_42_LBA8192")=="NP_42_LBA8192","Blank changed defaults");
  Check(Name("RatID_sleep",null,"NP_42_LBA8192")=="RatID_sleep_wild_time_unknown_NP_42_LBA8192","Missing date invented");
  foreach(string invalid in new[]{"..","rat/ID","rat\\ID","rat:ID","rat\"ID","rat.",new string('a',81)}){bool rejected=false;try{Name(invalid,date,"default");}catch(ArgumentException){rejected=true;}Check(rejected,"Unsafe label accepted: "+invalid);}
  string first=(string)Call(naming,"CreateFresh",root,name);File.WriteAllText(Path.Combine(first,"keep.dat"),"KEEP");
  string next=(string)Call(naming,"CreateFresh",root,name);Check(next==first+"_2" && File.ReadAllText(Path.Combine(first,"keep.dat"))=="KEEP","Collision overwrote existing recording");
  File.WriteAllText(Path.Combine(root,"occupied"),"KEEP");using(var reserve=(IDisposable)Call(naming,"Reserve",root,"occupied"))Check((string)reserve.GetType().GetField("Path",All).GetValue(reserve)==Path.Combine(root,"occupied_2"),"Existing file collision ignored");
  using(var a=(IDisposable)Call(naming,"Reserve",root,"claim"))using(var b=(IDisposable)Call(naming,"Reserve",root,"claim"))Check(a.GetType().GetField("Path",All).GetValue(a).ToString()!=b.GetType().GetField("Path",All).GetValue(b).ToString(),"Simultaneous exporters claimed the same destination");
  var paths=Task.WhenAll(Enumerable.Range(0,20).Select(i=>Task.Run(()=>(string)Call(naming,"CreateFresh",root,"parallel")))).Result;
  Check(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()==20,"Concurrent output collision");Check(Directory.GetFiles(root,"*.wildx-reservation").Length==0,"Claims leaked after close");
 }
 static void Ui(string root){
  var type=app.GetType("USB512_interface.Form1",true);
  using(var form=(Form)Activator.CreateInstance(type)) {
   form.Load-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),form,type.GetMethod("Form1_Load",All));
   form.Location=new Point(-32000,-32000);form.StartPosition=FormStartPosition.Manual;form.Show();Application.DoEvents();
   var label=(TextBox)type.GetField("npSessionLabel",All).GetValue(form);label.Text="RatID_sleep";
   var download=type.GetField("downloadWorkspace",All).GetValue(form);var field=download.GetType().GetProperty("SessionLabel",All);
   Check((string)field.GetValue(download,null)==label.Text,"USB label did not reach downloads");field.SetValue(download,"Mouse02_awake",null);Check(label.Text=="Mouse02_awake","Download label did not reach USB");
   Check(label.Width>200 && label.Height>=20 && label.Parent.ClientRectangle.Contains(label.Bounds),"USB session field clipped");
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"usb-folder-name.png"));}
   var tabs=(TabControl)type.GetField("workspaceTabs",All).GetValue(form);tabs.SelectedIndex=2;Application.DoEvents();
   var downloadLabel=(TextBox)download.GetType().GetField("sessionLabel",All).GetValue(download);
   Check(downloadLabel.Width>200 && downloadLabel.Parent.ClientRectangle.Contains(downloadLabel.Bounds),"Download session field clipped");
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"download-folder-name.png"));}
   form.Close();
  }
  using(var fast=(Form)Activator.CreateInstance(app.GetType("USB512_interface.NpLoggerForm",true),All,null,new object[]{null,"RatID_sleep"},null)) {
   var events=(System.ComponentModel.EventHandlerList)typeof(System.ComponentModel.Component).GetProperty("Events",All).GetValue(fast,null);
   var shown=typeof(Form).GetField("EVENT_SHOWN",All).GetValue(null);events.RemoveHandler(shown,events[shown]);
   fast.Location=new Point(-32000,-32000);fast.StartPosition=FormStartPosition.Manual;fast.Show();Application.DoEvents();
   var label=(TextBox)fast.GetType().GetField("sessionLabel",All).GetValue(fast);
   Check(label.Width>200 && label.Parent.ClientRectangle.Contains(label.Bounds),"Fast archive session field clipped");
   using(var bitmap=new Bitmap(fast.Width,fast.Height)){fast.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"archive-folder-name.png"));}fast.Close();
  }
 }
 [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));naming=app.GetType("USB512_interface.NpFolderNaming",true);catalog=app.GetType("CE32_console.NpDownloadCatalog",true);recordType=app.GetType("CE32_console.NpDownloadRecord",true);string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);Names(Path.Combine(root,"names"));Times();Ui(root);Console.WriteLine("PASS "+checks+" delivered-app naming/time/UI checks; stored V1/V2/media START, invalid/missing clocks, collision/concurrency/no-overwrite, synced labels; no device discovery or hardware accessed.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
