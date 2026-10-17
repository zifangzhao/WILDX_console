using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Exercise the delivered form/logger with an in-memory COM transport. Never
// call the acquisition Start handler, enumerate devices, or open real hardware.
static class NpGpioRtcContinuityTests {
 const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;static Type formType,loggerType,settingsType;static Assembly assembly;
 static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static object Get(object target,string name){return target.GetType().GetField(name,All).GetValue(target);}
 static void Set(object target,string name,object value){target.GetType().GetField(name,All).SetValue(target,value);}
 static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,All).Invoke(target,args);}
 static bool Logging(object logger){return (bool)loggerType.GetProperty("IsLogging",All).GetValue(logger,null);}
 static long Saved(object logger){return (long)loggerType.GetProperty("EventsSaved",All).GetValue(logger,null);}
 static Dictionary<string,object> Json(string path){return (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));}
 static void Until(Func<bool> done){DateTime limit=DateTime.UtcNow.AddSeconds(5);while(!done() && DateTime.UtcNow<limit)Thread.Sleep(10);Check(done(),"simulated GPIO event was not written");}
 sealed class Port : RealProxy {
  readonly BlockingCollection<byte[]> input=new BlockingCollection<byte[]>();internal int Opens,Closes,Disposals;
  internal Port():base(assembly.GetType("USB512_interface.INpGpioRtcPort",true)){}
  internal void Event(uint seq){var b=new byte[16];b[0]=0x3c;b[1]=0x8e;b[2]=1;
   Buffer.BlockCopy(BitConverter.GetBytes(seq),0,b,3,4);Buffer.BlockCopy(BitConverter.GetBytes(100000UL+seq),0,b,7,8);b[15]=0x3e;input.Add(b);}
  public override IMessage Invoke(IMessage message) {
   var call=(IMethodCallMessage)message;
   try {object result=null;
    switch(call.MethodName) {
     case "GetType":result=assembly.GetType("USB512_interface.INpGpioRtcPort",true);break;
     case "Open":Interlocked.Increment(ref Opens);break;
     case "Close":Interlocked.Increment(ref Closes);break;
     case "Dispose":Interlocked.Increment(ref Disposals);break;
     case "Read":byte[] bytes;if(!input.TryTake(out bytes,25))throw new TimeoutException();
      Buffer.BlockCopy(bytes,0,(byte[])call.Args[0],0,bytes.Length);result=bytes.Length;break;
     case "Write":var query=(byte[])call.Args[0];var reply=new byte[15];reply[0]=0x3c;reply[1]=0x91;
      Buffer.BlockCopy(query,2,reply,2,4);Buffer.BlockCopy(BitConverter.GetBytes(100000UL),0,reply,6,8);reply[14]=0x3e;input.Add(reply);break;
     default:throw new InvalidOperationException(call.MethodName);
    }
    return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
   }catch(Exception ex){return new ReturnMessage(ex,call);}
  }
 }
 static object Settings(string folder) {
  var settings=Activator.CreateInstance(settingsType,All,null,new object[]{folder,"","live_usb",false,(DateTime?)DateTime.UtcNow,"fixture","fixture"},null);
  Call(settings,"Actual",20000,20000,3,false,true,"fixture");return settings;
 }
 static object StartLogger(Form form,string folder,object device,out Port port) {
  Directory.CreateDirectory(folder);Set(form,"runFolder",folder);port=new Port();
  var logger=Activator.CreateInstance(loggerType,All,null,new object[]{folder,device,port.GetTransparentProxy()},null);
  Call(logger,"Start");Set(form,"gpioRtcLogger",logger);return logger;
 }
 static void References(string folder,string original,Dictionary<string,object> context) {
  foreach(string key in new[]{"session_file","events_file","clock_queries_file"}) {
   string path=Path.GetFullPath(Path.Combine(folder,(string)context[key]));
   Check(path==Path.Combine(original,Path.GetFileName(path)) && File.Exists(path),"GPIO link does not resolve from "+folder+": "+key);
  }
 }
 static void Run(string root,string kindName) {
  using(var form=(Form)Activator.CreateInstance(formType)) {
   form.Load-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),form,formType.GetMethod("Form1_Load",All));
   form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
   Set(form,"recordingWarningPresenter",new Action<string>(s=>{}));
   Set(form,"gpioRtcDevicePresent",new Func<string,string,bool>((p,id)=>{throw new Exception("continued GPIO tried device discovery");}));
   var kindField=formType.GetField("selectedDeviceKind",All);kindField.SetValue(form,Enum.Parse(kindField.FieldType,kindName));
   var deviceType=assembly.GetType("USB512_interface.NpGpioRtcDevice",true);var device=Activator.CreateInstance(deviceType);
   Set(device,"Port","COM77");Set(device,"Identity","memory-only");Set(form,"refreshingGpioRtc",true);
   var combo=(ComboBox)Get(form,"gpioRtcDevices");combo.Items.Add(device);combo.SelectedItem=device;Set(form,"refreshingGpioRtc",false);combo.Enabled=false;
   string first=Path.Combine(root,kindName,"first"),next=Path.Combine(root,kindName,"next");Directory.CreateDirectory(next);
   Port port;var logger=StartLogger(form,first,device,out port);var firstReceive=DateTime.UtcNow.AddSeconds(-10);
   Set(form,"recordingSummaryStartUtc",firstReceive);Set(form,"running",true);
   port.Event(1);Until(()=>Saved(logger)==1);
   Call(form,"FinishCaptureOnUi","Capture error: NP USB read timed out");Application.DoEvents();
   Check(Logging(logger) && port.Opens==1 && port.Closes==0 && ReferenceEquals(Get(form,"gpioRtcLogger"),logger),kindName+" failure closed GPIO");
   Check(((Button)Get(form,"btnStop")).Enabled && !combo.Enabled,"Stop unavailable or GPIO port selectable during interruption");
   Check(((Label)Get(form,"lblRecInfo")).Text.Contains("GPIO/RTC logging continues"),"continued GPIO status missing");
   port.Event(2);Until(()=>Saved(logger)==2);
   var settings=Settings(next);Set(form,"npRecordingSettings",settings);Set(form,"runFolder",next);
   Set(form,"recordingSummaryStartUtc",DateTime.MinValue);Call(form,"StartGpioRtcRecording");
   Check(ReferenceEquals(Get(form,"gpioRtcLogger"),logger) && port.Opens==1 && port.Closes==0,"NP restart reopened/reset GPIO");
   port.Event(3);Until(()=>Saved(logger)==3);Call(settings,"Write");
   Check(!File.Exists(Path.Combine(next,"gpio_rtc_events.txt")),"NP restart split the GPIO event log");
   References(next,first,Json(Path.Combine(next,"gpio_rtc_link.json")));
   References(next,first,(Dictionary<string,object>)Json(Path.Combine(next,"recording_settings.json"))["gpio_rtc"]);
   References(Path.Combine(next,"probe2"),first,(Dictionary<string,object>)Json(Path.Combine(next,"probe2","recording_settings.json"))["gpio_rtc"]);
   // A cross-volume absolute reference must remain absolute in per-probe metadata.
   Call(settings,"Context","gpio_rtc",new Dictionary<string,object>{{"events_file",@"Z:\continuing\gpio_rtc_events.txt"}});Call(settings,"Write");
   Check((string)((Dictionary<string,object>)Json(Path.Combine(next,"probe2","recording_settings.json"))["gpio_rtc"])["events_file"]==@"Z:\continuing\gpio_rtc_events.txt","absolute GPIO link was prefixed as relative");
   // Exercise actual recovery scheduling, then explicit Stop before pumping the
   // UI timer (which would otherwise start real USB acquisition).
   Set(form,"npSessionFolder",first);Set(form,"npRecoveryCancelled",false);Set(form,"npSessionDeviceUid","fixture");
   Set(form,"npSegmentStartUtc",DateTime.UtcNow.AddSeconds(-15));Set(form,"npV2RecoverySafe",true);
   Set(form,"recordingSummaryStartUtc",DateTime.UtcNow);Set(form,"running",true);
   Call(form,"FinishCaptureOnUi","Capture error: NP USB read timed out");
   var retry=(System.Windows.Forms.Timer)Get(form,"npRetryTimer");Check(retry.Enabled && Logging(logger),"reconnect wait stopped GPIO");
   port.Event(4);Until(()=>Saved(logger)==4);Call(form,"btnStop_Click",null,EventArgs.Empty);Application.DoEvents();
   Check(!retry.Enabled && (bool)Get(form,"npRecoveryCancelled") && Get(form,"gpioRtcLogger")==null && !Logging(logger),"Stop during reconnect failed to stop GPIO/recovery");
   Check(port.Closes==1 && port.Disposals==1 && !((Button)Get(form,"btnStop")).Enabled,"Stop leaked/reclosed port or remained enabled");
   var receipt=Json(Path.Combine(first,"gpio_rtc_session.json"));
   Check((bool)receipt["complete"] && Convert.ToInt64(receipt["events_saved"])==4 && Convert.ToInt64(receipt["sequence_discontinuities"])==0,"continuing log lost events or introduced sequence gap");
   Check(DateTime.Parse((string)receipt["neural_first_receive_utc"]).ToUniversalTime()==firstReceive,"later recording replaced first neural timestamp");
   Check(File.ReadAllLines(Path.Combine(first,"gpio_rtc_events.txt")).Select((s,i)=>s.Contains("Sequence: "+(i+1)+",")).All(x=>x),"GPIO event order reset across interruption");
   // Explicit Stop during active NP and app Close must also release GPIO.
   Set(form,"npSessionFolder",null);Set(form,"npRecordingSettings",null);Set(form,"recordingSummaryStartUtc",DateTime.MinValue);
   Port activePort;var active=StartLogger(form,Path.Combine(root,kindName,"active"),device,out activePort);Set(form,"running",true);
   Call(form,"btnStop_Click",null,EventArgs.Empty);
   Check(!Logging(active) && activePort.Closes==1 && Get(form,"gpioRtcLogger")==null,"active NP Stop did not stop GPIO");
   Call(form,"FinishCaptureOnUi","Stopped");Application.DoEvents();
   Port closePort;var closing=StartLogger(form,Path.Combine(root,kindName,"close"),device,out closePort);form.Close();
   Check(!Logging(closing) && closePort.Closes==1,"app Close leaked independent GPIO logger");
  }
 }
 [STAThread]static int Main(string[] args){try {
  assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));formType=assembly.GetType("USB512_interface.Form1",true);
  loggerType=assembly.GetType("USB512_interface.NpGpioRtcLogger",true);settingsType=assembly.GetType("USB512_interface.NpRecordingSettings",true);
  string root=Path.Combine(Path.GetFullPath(args[1]),"continuity_"+Guid.NewGuid().ToString("N"));Run(root,"XeNp");Run(root,"NpV2");
  Console.WriteLine("PASS "+checks+" delivered-GUI GPIO continuity checks for V1/V2; simulated COM only.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
