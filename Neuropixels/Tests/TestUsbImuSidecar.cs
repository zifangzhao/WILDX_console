using System;
using System.IO;
using System.Reflection;

// Actual built parser, synthetic snapshots and local output only. No device.
class TestUsbImuSidecar {
 static int checks;const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
 static void Main(string[] args) {
  if(args.Length!=2)throw new ArgumentException("TestUsbImuSidecar <GUI.exe> <fresh folder>");
  string folder=Path.GetFullPath(args[1]);if(Directory.Exists(folder))throw new IOException("Fresh folder required");
  Directory.CreateDirectory(folder);Assembly gui=Assembly.LoadFrom(Path.GetFullPath(args[0]));
  Type t=gui.GetType("USB512_interface.NpUsbSensors",true);
  object sensor=Activator.CreateInstance(t,F,null,new object[]{folder},null);
  byte[] b=new byte[140];Put(b,0,0x31534e53);Put(b,4,1);Put(b,12,3);Put(b,16,1000);
  Put(b,20,1);Put(b,24,1);Put(b,32,1000);Put(b,36,1000);
  Put(b,48,1000000);Put(b,60,12345);Put(b,72,25000);
  var feed=t.GetMethod("FeedSnapshot",F);feed.Invoke(sensor,new object[]{b,0,1000U});
  Put(b,16,1050);Put(b,20,11);Put(b,32,1050);Put(b,36,1050);
  feed.Invoke(sensor,new object[]{b,0,1050U});
  Check((long)t.GetField("ImuSamples",F).GetValue(sensor)==2,"native sequences counted distinctly");
  Check((long)t.GetField("ImuSequenceGaps",F).GetValue(sensor)==9,"snapshot skips remain visible");
  var before=(byte[])b.Clone();feed.Invoke(sensor,new object[]{b,0,1251U});
  Check((uint)t.GetField("Valid",F).GetValue(sensor)==0,"stale AG/MAG invalidated");
  for(int i=0;i<b.Length;i++)Check(b[i]==before[i],"raw snapshot immutable");
  object display=t.GetMethod("GetDisplay",F).Invoke(sensor,null);Type d=display.GetType();
  var values=(double[])d.GetField("Values",F).GetValue(display);
  Check(values[0]==1 && Math.Abs(values[3]-12.345)<1e-8 && values[6]==25,"g/deg/s/uT units");
  ((IDisposable)sensor).Dispose();byte[] dat=File.ReadAllBytes(Path.Combine(folder,"analogin.dat"));
  Check(dat.Length>0 && dat.Length%32==0,"CE64 16-word held grid");
  Check(BitConverter.ToInt16(dat,2)==1000 && BitConverter.ToInt16(dat,14)==25,"compatibility scaling");
  bool unavailable=false;for(int o=0;o<dat.Length;o+=32)if(BitConverter.ToInt16(dat,o+2)==short.MinValue)unavailable=true;
  Check(unavailable,"held data expires instead of looking fresh");
  string badFolder=Path.Combine(folder,"bad");Directory.CreateDirectory(badFolder);
  sensor=Activator.CreateInstance(t,F,null,new object[]{badFolder},null);b[0]^=1;
  bool rejected=false;try{feed.Invoke(sensor,new object[]{b,0,1300U});}catch(TargetInvocationException e){rejected=e.InnerException is IOException;}
  Check(rejected,"invalid snapshot rejected");((IDisposable)sensor).Dispose();
  Console.WriteLine("USB IMU sidecar: PASS ("+checks+" checks); synthetic only.");
 }
}
