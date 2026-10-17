using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

static class NpBleRecordingTimeTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app;static int checks;
 static Type T(string name){return app.GetType(name,true);}
 static void Set(object obj,string field,object value){obj.GetType().GetField(field,All).SetValue(obj,value);}
 static object Call(object obj,string method,params object[] args){return obj.GetType().GetMethod(method,All).Invoke(obj,args);}
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static object Status(uint mask,uint hz,uint chunks,uint state){var s=Activator.CreateInstance(T("CE32_console.NpSdStatus"),true);Set(s,"Mask",mask);Set(s,"Hz",hz);Set(s,"Chunks",chunks);Set(s,"State",state);return s;}
 static uint Seconds(int version,object s){return (uint)T(version==1?"CE32_console.NpV1BleProtocol":"CE32_console.NpV2SdControl").GetMethod("RecordedSeconds",All).Invoke(null,new[]{s});}
 [STAThread]static int Main(string[] args){try{
  app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);
  foreach(var vector in new[]{new uint[]{1,1,20000,15625,61},new uint[]{1,3,20000,15625,30},new uint[]{1,7,15000,15625,27},new uint[]{2,1,20000,12500,48},new uint[]{2,3,20000,12500,24},new uint[]{2,7,20000,12500,16}}){
   int version=(int)vector[0];object s=Status(vector[1],vector[2],vector[3],version==1?1U:3U);
   Need(Seconds(version,s)==vector[4],"Wrong device-counter duration for V"+version+" mask"+vector[1]);
   object host=FormatterServices.GetUninitializedObject(T("USB512_interface.NpBleForm"));
   using(var time=new Label())using(var state=new Label())using(var firmware=new Label())using(var log=new TextBox()){
    Set(host,"recorderVersion",version);Set(host,"recordingTime",time);Set(host,"state",state);Set(host,"firmwareStatus",firmware);Set(host,"log",log);
    Call(host,"ShowStatus",s);string wanted="Recording time 00:"+(vector[4]/60).ToString("00")+":"+(vector[4]%60).ToString("00");
    Need(time.Text.StartsWith(wanted+"   |   Recorded size "),"Online recording metrics not displayed");
    string metrics=time.Text;Set(s,"State",version==1?3U:5U);Call(host,"ShowStatus",s);Need(time.Text==metrics,"Stopped metrics changed without new recording data");
    Set(s,"Chunks",0U);Call(host,"ShowStatus",s);Need(time.Text=="Recording time 00:00:00   |   Recorded size 0 B","New recording retained prior metrics");
   }
  }
  Need((string)T("USB512_interface.NpBleForm").GetMethod("RecordingTimeText",All).Invoke(null,new object[]{108123UL})=="Recording time 30:02:03","Duration wrapped after24hours");
  var sizeStatus=Status(1,20000,16,1);Set(sizeStatus,"Capacity",2097152U);
  var storage=T("USB512_interface.NpBleForm").GetMethod("RecordingStorageText",All);
  Need((string)storage.Invoke(null,new object[]{false,sizeStatus})=="   |   Recorded size 1.00 MiB   |   SD capacity 1.00 GiB","V1 size/capacity incorrect");
  Need(((string)storage.Invoke(null,new object[]{true,sizeStatus})).Contains("Recorded size 728.00 KiB"),"V2 block size incorrect");
  Set(sizeStatus,"Chunks",uint.MaxValue);Set(sizeStatus,"Capacity",uint.MaxValue);
  Need(((string)storage.Invoke(null,new object[]{false,sizeStatus})).Contains("256.00 TiB"),"V1 size overflowed32-bit counters");
  Need(Seconds(1,null)==0,"Unknown V1 status fabricated a duration");
  bool refused=false;try{Seconds(2,null);}catch(TargetInvocationException ex) when(ex.InnerException is IOException){refused=true;}Need(refused,"Unknown V2 status fabricated a duration");
  using(var form=(Form)Activator.CreateInstance(T("USB512_interface.NpBleForm"),true)){
   var status=Status(3,20000,15625,1);Set(form,"recorderVersion",1);Call(form,"ShowStatus",status);
   using(var host=new Form{ClientSize=new Size(1050,680),Font=form.Font,BackColor=form.BackColor,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
    foreach(Control child in form.Controls.Cast<Control>().ToArray())host.Controls.Add(child);host.Show();Application.DoEvents();
    var label=(Label)T("USB512_interface.NpBleForm").GetField("recordingTime",All).GetValue(form);
    Need(label.Visible&&label.Font.Bold&&label.Parent.ClientRectangle.Contains(label.Bounds),"Recording time is hidden, clipped or not prominent");
    using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(args[1],"ble-recording-time.png"));}
   }
  }
  Console.WriteLine("PASS "+checks+" actual-app BLE recording-time checks; synthetic status only, no hardware.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
