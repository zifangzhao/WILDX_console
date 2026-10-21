using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Delivered-binary preview tests only. No main-form Load, device discovery,
// USB, BLE, firmware, recording or network filesystem access.
static class NpPreviewLoadTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static int checks;
 static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static object Get(object o,string name){return o.GetType().GetField(name,All).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,All).SetValue(o,value);}
 static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,All).Invoke(o,args);}
 static void Until(Func<bool> ready,string why){var timer=Stopwatch.StartNew();while(!ready()){if(timer.ElapsedMilliseconds>5000)throw new Exception(why);Application.DoEvents();Thread.Sleep(5);}}
 static byte[] Frame(int value){
  var raw=new byte[160000];
  for(int y=0;y<320;y++)for(int n=0;n<12;n++)raw[y*492+n]=0x55;
  for(int y=0;y<320;y++)for(int x=0;x<320;x+=2){int p=y*492+12+x/2*3;raw[p]=(byte)(0x80|(value>>3));raw[p+1]=(byte)(((value&7)<<5)|8|(value>>7));raw[p+2]=(byte)((value&127)<<1);}
  return raw;
 }
 static void Budgets(Assembly app,string typeName,int cadence){
  var method=app.GetType(typeName,true).GetMethod("PreviewIdleMilliseconds",All);
  foreach(long work in new long[]{0,1,5,10,20,50,100,500,1000,10000}){
   int idle=(int)method.Invoke(null,new object[]{work});
   Check(work+idle>=cadence,"Preview exceeded cadence: "+typeName);
   Check(idle>=3*work,"Slow preview can consume over a quarter of one CPU: "+typeName+" work="+work);
  }
  Check((int)method.Invoke(null,new object[]{Int64.MaxValue})>0,"Preview delay overflow");
 }
 static void Publish(Form camera,byte[] frame){
  object[] args={null};Check((bool)Call(camera,"TryAcquireRawFrameBuffer",args),"Preview raw pool exhausted");
  var raw=(byte[])args[0];Buffer.BlockCopy(frame,0,raw,0,raw.Length);Call(camera,"PublishFrame",raw);
 }
 static bool PoolRecovered(Form camera){
  var borrowed=new List<byte[]>();object[] args={null};
  while((bool)Call(camera,"TryAcquireRawFrameBuffer",args)){borrowed.Add((byte[])args[0]);args[0]=null;}
  foreach(var raw in borrowed)Call(camera,"ReturnRawFrameBuffer",raw);
  return borrowed.Count==3;
 }
 static void PublishUntilDecoded(Form camera,int value,long previous){
  byte[] frame=Frame(value);var timer=Stopwatch.StartNew();
  // A real camera keeps publishing; display-only throttling may skip a slot.
  while((long)Get(camera,"decodedFrames")<=previous){
   if(timer.ElapsedMilliseconds>5000)throw new Exception("Restoring did not resume images");
   Publish(camera,frame);Application.DoEvents();Thread.Sleep(50);
  }
 }
 [STAThread]static int Main(string[] args){try{
  var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Application.EnableVisualStyles();
  Budgets(app,"USB512_interface.NeuropixelsPreviewForm",50);Budgets(app,"USB512_interface.CameraPreviewForm",40);
  int exposureWrites=0;
  using(var camera=(Form)Activator.CreateInstance(app.GetType("USB512_interface.CameraPreviewForm",true),new object[]{new Action<byte,byte>((a,b)=>Interlocked.Increment(ref exposureWrites))})){
   camera.ShowInTaskbar=false;camera.StartPosition=FormStartPosition.Manual;camera.Location=new Point(-20000,-20000);
   Check(((Thread)Get(camera,"decodeThread")).Priority==ThreadPriority.BelowNormal,"Camera competes at recording priority");
   Call(camera,"UpdateSensorExposure",100);
   Set(camera,"lastExposureMeterTimestamp",Int64.MaxValue);Call(camera,"UpdateSensorExposure",900);
   Check((int)Get(camera,"exposureMean")==900,"Meter did not retain the latest native brightness");
   Check((int)Get(camera,"filteredExposureMean")==100,"Control filter ran faster than the original25Hz cadence");
   Set(camera,"lastExposureMeterTimestamp",0L);Set(camera,"filteredExposureInitialized",false);
   Publish(camera,Frame(512));Until(()=> (int)Get(camera,"exposureMean")==512,"Hidden camera stopped metering");
   Until(()=>PoolRecovered(camera),"Hidden frame buffer was retained");
   Check((long)Get(camera,"decodedFrames")==0,"Hidden camera still demosaics images");
   Check(exposureWrites>0,"Hidden camera stopped automatic sensor control");
   camera.Show();Application.DoEvents();Check((bool)Get(camera,"displayActive"),"Shown preview remained inactive");
   Publish(camera,Frame(400));Until(()=> (long)Get(camera,"decodedFrames")>0,"Visible camera did not decode");
   Until(()=>PoolRecovered(camera),"Visible frame buffer was retained");
   camera.Hide();long shown=(long)Get(camera,"decodedFrames");
   Publish(camera,Frame(256));Until(()=> (int)Get(camera,"exposureMean")==256,"Hide stopped metering");
   Until(()=>PoolRecovered(camera),"Hidden buffer did not return");
   Check((long)Get(camera,"decodedFrames")==shown,"Hiding camera did not stop image decode");
   camera.Show();camera.WindowState=FormWindowState.Minimized;Application.DoEvents();
   Check(!(bool)Get(camera,"displayActive"),"Minimized camera still active");
   Publish(camera,Frame(700));Until(()=> (int)Get(camera,"exposureMean")==700,"Minimization stopped exposure metering");
   Until(()=>PoolRecovered(camera),"Minimized buffer did not return");
   Check((long)Get(camera,"decodedFrames")==shown,"Minimized camera still demosaics images");
   camera.WindowState=FormWindowState.Normal;Application.DoEvents();
   Check((bool)Get(camera,"displayActive"),"Restored camera remained inactive");
   PublishUntilDecoded(camera,600,shown);
  }
  Console.WriteLine("PASS "+checks+" delivered preview load checks: cadence/duty bounds, hidden/minimized image bypass with active exposure metering/control, pooled ownership and restore. No hardware.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
