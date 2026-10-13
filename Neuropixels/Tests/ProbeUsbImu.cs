using System;
using System.IO;
using System.Reflection;
using System.Threading;

// Explicit USB read-only HIL. Does not start capture, reset, or configure probes.
class ProbeUsbImu {
 const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static object Call(Type t,object o,string name,params object[] args){try{return t.GetMethod(name,F).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static uint Word(object o,string name){var property=o.GetType().GetProperty(name,F);return property==null?(uint)o.GetType().GetField(name,F).GetValue(o):(uint)property.GetValue(o,null);}
 static void Main(string[] args) {
  if(args.Length!=4)throw new ArgumentException("ProbeUsbImu <GUI.exe> <UID> <fresh folder> preflight|observe");
  string folder=Path.GetFullPath(args[2]);if(Directory.Exists(folder))throw new IOException("Fresh output required");Directory.CreateDirectory(folder);
  Assembly gui=Assembly.LoadFrom(Path.GetFullPath(args[0]));Type deviceType=gui.GetType("USB512_interface.NpV2UsbDevice",true),uidType=gui.GetType("USB512_interface.NpV2FirmwareForm",true);
  string path=null;foreach(string candidate in (System.Collections.Generic.List<string>)Call(deviceType,null,"EnumerateDevicePaths"))
   if(String.Equals((string)Call(uidType,null,"Uid",candidate),args[1],StringComparison.OrdinalIgnoreCase))path=candidate;
  if(path==null)throw new IOException("Exact selected USB UID absent");
  object device=Call(deviceType,null,"Open",path);
  try {
   object service=Call(deviceType,device,"GetServiceStatus"),capture=Call(deviceType,device,"GetCaptureStatus"),source=Call(deviceType,device,"GetSourceStatus");
   Console.WriteLine("UID="+args[1]+" mode="+Word(service,"Mode")+" busy="+Word(service,"Busy")+" capture="+Word(capture,"State")+" source="+Word(source,"State"));
   if(Word(service,"Mode")!=2 || Word(service,"Busy")!=0 || Word(capture,"State")!=0 || Word(source,"State")!=0)throw new IOException("Idle exact USB-stream service required");
   if(args[3]=="preflight")return;
   if(args[3]!="observe")throw new ArgumentException("Unknown HIL mode");
   object monitor=Activator.CreateInstance(gui.GetType("USB512_interface.NpUsbSensorMonitor",true),F,null,new object[]{device,folder,args[1],true,null},null);
   // Explicit idle-only diagnostic owns every EP0 request. Production monitor
   // has no thread/handle and consumes verified bulk snapshots instead.
   try {for(int i=0;i<40;i++) {
    service=Call(deviceType,device,"GetServiceStatus");capture=Call(deviceType,device,"GetCaptureStatus");
    if(Word(service,"Busy")!=0 || Word(capture,"State")!=0)throw new IOException("Idle status changed during observation");
    byte[] snapshot=(byte[])Call(deviceType,device,"GetSensorSnapshot");
    Call(monitor.GetType(),monitor,"FeedSnapshot",snapshot,0);
    if(i==0 || i==20 || i==39)Call(monitor.GetType(),monitor,"ObserveIdleClock",device);
    Thread.Sleep(200);
   }}finally{((IDisposable)monitor).Dispose();}
   Console.WriteLine(File.ReadAllText(Path.Combine(folder,"imu_observation_status.txt")));
   byte[] raw=File.ReadAllBytes(Path.Combine(folder,"imu_snapshots.bin"));int valid=0,changes=0;uint last=0,first=0,lastTime=0;
   for(int o=0;o+140<=raw.Length;o+=140) {
    uint seq=BitConverter.ToUInt32(raw,o+20),tick=BitConverter.ToUInt32(raw,o+32);
    if((BitConverter.ToUInt32(raw,o+12)&1)==0)continue;valid++;if(first==0)first=tick;lastTime=tick;if(seq!=last)changes++;last=seq;
   }
   Console.WriteLine("IMU snapshots="+(raw.Length/140)+" valid_AG="+valid+" distinct="+changes+" device_span_ms="+unchecked(lastTime-first));
   string status=File.ReadAllText(Path.Combine(folder,"imu_observation_status.txt"));Console.WriteLine(status);
   if(valid<30 || changes<30 || unchecked(lastTime-first)<2000 || !status.Contains("error=none"))throw new IOException("Fresh progressing IMU observations not confirmed");
   Console.WriteLine("PASS live idle IMU snapshots + held DAT + immutable timing sidecar; not neural/physical-sync qualification");
  } finally {((IDisposable)device).Dispose();}
 }
}
