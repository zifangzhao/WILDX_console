using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

static class NpPreviewWindowTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static Assembly app;static int checks;
 static object Get(object o,string name){return o.GetType().GetField(name,All).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,All).SetValue(o,value);}
 static void Call(object o,string name,params object[] args){o.GetType().GetMethods(All).Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(o,args);}
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static Form MainForm(string kind){
  var type=app.GetType("USB512_interface.Form1",true);var main=(Form)Activator.CreateInstance(type);
  main.Load-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),main,type.GetMethod("Form1_Load",All));
  foreach(IComponent c in ((IContainer)Get(main,"components")).Components)if(c is Timer)((Timer)c).Stop();
  ((Timer)Get(Get(main,"bleWorkspace"),"timer")).Stop();
  Set(main,"selectedDeviceKind",Enum.Parse(type.GetField("selectedDeviceKind",All).FieldType,kind));
  Set(main,"devicePath","offline-preview-fixture");Set(main,"saveFolder",Path.GetTempPath());
  main.StartPosition=FormStartPosition.Manual;main.Location=new Point(-20000,-20000);main.Show();Call(main,"UpdateStartEnabled");return main;
 }
 static void Independent(Form main,Form preview){Check(preview.Visible && preview.Owner==null && !preview.TopMost && !main.OwnedForms.Contains(preview),"Preview window: "+preview.Text+"; visible="+preview.Visible+"; owner="+(preview.Owner==null?"none":preview.Owner.Text)+"; topmost="+preview.TopMost+"; owned="+main.OwnedForms.Contains(preview));}
 [STAThread]static int Main(string[] args){try{
  app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Application.EnableVisualStyles();
  using(var main=MainForm("NpV2")){
   Call(main,"btnScope_Click",null,EventArgs.Empty);var neural=(Form)Get(main,"neuropixelsPreview");Independent(main,neural);
   neural.GetType().GetProperty("KeepAliveWhileRecording",All).SetValue(neural,true,null);neural.Close();
   Check(!neural.Visible && !neural.IsDisposed,"Closing the live preview destroyed capture resources");
   Call(main,"btnScope_Click",null,EventArgs.Empty);Independent(main,neural);
   Call(main,"PrepareCameraPreviewForCapture");Call(main,"btnPreview_Click",null,EventArgs.Empty);var camera=(Form)Get(main,"cameraPreview");Independent(main,camera);
   main.Activate();Check(main.Enabled,"Independent preview disabled the console");main.Close();
   Check(neural.IsDisposed && camera.IsDisposed,"Closing the console left independent previews running");
  }
  using(var main=MainForm("XeNp")){
   Call(main,"btnPreview_Click",null,EventArgs.Empty);var media=(Form)Get(main,"mediaPreview");Independent(main,media);
   Check(main.Enabled && (bool)Get(main,"firmwareManagementActive") && !((Button)Get(main,"btnStart")).Enabled,"Modeless media preview released the USB lease");
   var drain=new TaskCompletionSource<bool>();Set(media,"capture",drain.Task);main.Close();
   Check(!main.IsDisposed && !media.IsDisposed && (bool)Get(media,"stopping") && (bool)Get(main,"firmwareManagementActive"),"Console close bypassed media drain");
   drain.SetResult(true);var deadline=Stopwatch.StartNew();while(!media.IsDisposed && deadline.ElapsedMilliseconds<2000)Application.DoEvents();
   Check(media.IsDisposed && Get(main,"mediaPreview")==null && !(bool)Get(main,"firmwareManagementActive"),"Media completion did not release its UI/USB lease");
   main.Close();Check(main.IsDisposed,"Console could not close after media drain");
  }
  Console.WriteLine("PASS "+checks+" preview z-order/lifetime checks; modeled media task only, no discovery or hardware.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
