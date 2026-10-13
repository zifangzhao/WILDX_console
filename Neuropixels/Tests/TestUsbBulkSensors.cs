using System;
using System.IO;
using System.Linq;
using System.Reflection;

// Actual assembly + bounded saved RAW prefixes. No GUI/native/device access.
class TestUsbBulkSensors {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app;static int checks;
 static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
 static Type T(string name){return app.GetType("USB512_interface."+name,true);}
 static object New(string name){return Activator.CreateInstance(T(name),true);}
 static object Call(object o,string name,params object[] values){try{return o.GetType().GetMethod(name,F).Invoke(o,values);}catch(TargetInvocationException e){throw e.InnerException;}}
 static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
 static byte[] Block(FileStream file){var b=new byte[46592];int n=0;while(n<b.Length){int read=file.Read(b,n,b.Length-n);if(read==0)throw new IOException("RAW prefix too short");n+=read;}return b;}
 static bool Reject(byte[] b){try{Call(New("NpV2WireVerifier"),"Feed",b,0);return false;}catch(InvalidDataException){return true;}}
 static object Decoder(Action<int,short[]> row){return Activator.CreateInstance(T("NpV2SampleDecoder"),F,null,new object[]{new Action<uint,uint>((mask,hz)=>{}),row,0},null);}
 static void Payload(string rawPath,string folder){
  Directory.CreateDirectory(folder);object baseline=New("NpV2WireVerifier"),extended=New("NpV2WireVerifier");
  ulong oldHash=14695981039346656037UL,newHash=oldHash;long oldRows=0,newRows=0;
  object oldDecoder=Decoder((p,row)=>{oldRows++;foreach(short n in row)unchecked{oldHash=(oldHash^(ushort)n)*1099511628211UL;}});
  object newDecoder=Decoder((p,row)=>{newRows++;foreach(short n in row)unchecked{newHash=(newHash^(ushort)n)*1099511628211UL;}});
  object monitor=Activator.CreateInstance(T("NpUsbSensorMonitor"),F,null,new object[]{null,folder,"fixture-UID",true,null},null);
  byte[] first=null;
  using(var raw=new FileStream(rawPath,FileMode.Open,FileAccess.Read,FileShare.Read))for(int i=0;i<3;i++){
   byte[] original=Block(raw),changed=(byte[])original.Clone();if(i==0)first=(byte[])original.Clone();
   Put(changed,52,BitConverter.ToUInt32(changed,52)|128U);
   if(i!=1){Put(changed,256,0x31534e53U);Put(changed,260,1);Put(changed,268,1);Put(changed,272,(uint)(1000+i*50));Put(changed,276,(uint)(1+i));Put(changed,288,(uint)(1000+i*50));Put(changed,304,1000000);}
   else{changed[260]=77;changed[395]=99;} // absent marker with stale opaque bytes
   Call(baseline,"Feed",original,0);Call(extended,"Feed",changed,0);
   Call(oldDecoder,"Feed",original,0,baseline);Call(newDecoder,"Feed",changed,0,extended);
   Call(monitor,"FeedBlock",changed,0);
   Check(original.Skip(512).SequenceEqual(changed.Skip(512)),"payload unchanged by metadata");
  }
  Call(oldDecoder,"Complete");Call(newDecoder,"Complete");((IDisposable)monitor).Dispose();
  Check(oldHash==newHash && oldRows==newRows && oldRows>0,"actual AP decoder rows/values unchanged");
  Check((long)T("NpUsbSensorMonitor").GetProperty("Records",F).GetValue(monitor,null)==2,"sparse snapshots counted, stale marker ignored");
  byte[] bad=(byte[])first.Clone();Put(bad,52,BitConverter.ToUInt32(bad,52)|128U);bad[396]=1;Check(Reject(bad),"reserved tail remains strict");
  bad=(byte[])first.Clone();bad[256]=1;Check(Reject(bad),"legacy reserved window remains strict");
  bad=(byte[])first.Clone();Put(bad,52,BitConverter.ToUInt32(bad,52)|192U);Check(Reject(bad),"D8 overlap rejected");
  bad=(byte[])first.Clone();Put(bad,52,(BitConverter.ToUInt32(bad,52)|128U)&~32U);Check(Reject(bad),"sensor extension requires no header CRC");
  string invalid=Path.Combine(folder,"invalid");Directory.CreateDirectory(invalid);
  monitor=Activator.CreateInstance(T("NpUsbSensorMonitor"),F,null,new object[]{null,invalid,"fixture-UID",true,null},null);
  bad=(byte[])first.Clone();Put(bad,52,BitConverter.ToUInt32(bad,52)|128U);Put(bad,256,0xDEADBEEFU);
  Check(!Reject(bad),"optional opaque sensor error does not invalidate AP/profile");Call(monitor,"FeedBlock",bad,0);
  Check(T("NpUsbSensorMonitor").GetField("Error",F).GetValue(monitor)!=null,"malformed optional snapshot reported");((IDisposable)monitor).Dispose();
 }
 static object Status(string name,uint[] words){object o=New(name);T(name).GetField("Words",F).SetValue(o,words);return o;}
 static void Diagnostics(){
  uint[] c=new uint[32],s=new uint[32];c[0]=0x3155324e;c[1]=1;c[3]=2;c[21]=3;c[23]=8;c[24]=7;c[26]=20000;
  s[0]=0x3153324e;s[1]=1;s[3]=8;s[21]=1U<<10;
  var method=T("NpV2RecordingDiagnostics").GetMethod("Explain",F);
  Func<Exception,string> explain=e=>(string)method.Invoke(null,new object[]{"setup",Status("NpV2CaptureStatus",c),Status("NpV2SourceStatus",s),e,"original detail"});
  string text=explain(null);Check(text.Contains("P2 base-even") && text.Contains("shift-register verification"),"old source8 identifies exact chain/probe");
  s[21]=0;s[3]=c[23]=5;s[4]=0x01060300U;s[7]=4;text=explain(null);
  Check(text.Contains("P2 (I2C3)") && text.Contains("EEPROM read") && text.Contains("NACK"),"new I2C fault identifies probe/step");
  s[3]=c[23]=2;s[4]=0xff010700U;s[7]=0;text=explain(null);Check(text.Contains("not identified") && text.Contains("clock"),"shared setup fault never blames probe");
  s[3]=c[23]=0;s[4]=0;c[12]=6;text=explain(null);Check(text.Contains("ring overwrite") && text.Contains("no individual probe"),"shared producer failure explained");
  c[12]=0;var error=new InvalidDataException("framing fault");error.Data["NP2PhysicalProbe"]=3;text=explain(error);
  Check(text.Contains("Probe: P3") && text.Contains("supersync"),"stream error uses physical one-based probe");
 }
 static void Main(string[] args){
  if(args.Length!=4)throw new ArgumentException("GUI.exe dualRAW tripleRAW fresh-folder");
  string folder=Path.GetFullPath(args[3]);if(Directory.Exists(folder))throw new IOException("Fresh output required");Directory.CreateDirectory(folder);
  app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Payload(args[1],Path.Combine(folder,"dual"));Payload(args[2],Path.Combine(folder,"triple"));Diagnostics();
  Console.WriteLine("PASS "+checks+" actual-assembly sparse metadata/AP invariants and fault-stage/probe diagnostics; offline only");
 }
}
