using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class NpBleMediaTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static Assembly app;static int checks;static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static object Call(Type type,string name,params object[] args){try{return type.GetMethod(name,All).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
 static void Put(byte[] b,int p,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,p,4);}
 static void Q(byte[] b,int p,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,p,8);}
 static uint Crc(byte[] b,int p,int n){uint c=~0U;for(int i=0;i<n;i++){c^=b[p+i];for(int bit=0;bit<8;bit++)c=(c>>1)^(0xedb88320U&(0U-(c&1)));}return ~c;}
 static void Seal(byte[] b,int p){Put(b,p+508,Crc(b,p,508));}
 static byte[] Native(byte command,byte[] body){var f=new byte[body.Length+4];f[0]=0xad;f[1]=command;Buffer.BlockCopy(body,0,f,2,body.Length);f[f.Length-1]=f[f.Length-2]=0xda;return f;}
 static void Protocol(){
  var status=app.GetType("CE32_console.NpV1MediaStatus",true);var connection=app.GetType("USB512_interface.NpBleConnection",true);var wire=app.GetType("USB512_interface.NpBleWire",true);
  byte[] request=(byte[])Call(status,"Request");
  Need(Convert.ToUInt32(status.GetMethod("ProfileCommand",All).GetParameters()[2].DefaultValue)==40,"BLE default is not40fps");
  foreach(uint hz in new uint[]{16,25,40,50}) {
   var reply=(byte[])request.Clone();Put(reply,8,1);Put(reply,12,hz);Put(reply,16,200000);Put(reply,56,1);Put(reply,60,Crc(reply,0,60));
   Need((bool)Call(connection,"Matches",(byte)0xb9,request,(byte)0xb9,reply),"valid MEDIA1 reply rejected");
   reply[60]^=1;Need(!(bool)Call(connection,"Matches",(byte)0xb9,request,(byte)0xb9,reply),"bad media CRC accepted");reply[60]^=1;
   Put(reply,8,2);Put(reply,60,Crc(reply,0,60));Need(!(bool)Call(connection,"Matches",(byte)0xb9,request,(byte)0xb9,reply),"unsupported MEDIA1 ABI accepted");
  }
  byte[] parameters=new byte[512];for(int i=0;i<parameters.Length;i++)parameters[i]=(byte)(i%256);
  var stream=Native(0x90,parameters).Concat(Native(0xb9,request)).ToArray();
  for(int width=1;width<=67;width++) {
   var parser=Activator.CreateInstance(wire,true);var output=new List<Tuple<byte,byte[]>>();
   for(int at=0;at<stream.Length;at+=width)output.AddRange((List<Tuple<byte,byte[]>>)wire.GetMethod("Feed",All).Invoke(parser,new object[]{stream.Skip(at).Take(width).ToArray()}));
   Need(output.Count==2 && output[0].Item1==0x90 && output[0].Item2.SequenceEqual(parameters) && output[1].Item1==0xb9,"native parameters/control fragmentation or embedded markers broke framing");
  }
 }
 static MemoryStream Card(uint hz,bool badTiming=false,bool fault=false){
  const int start=8192,payload=16384,end=18432,frames=4;uint span=(uint)(frames*1000/hz),first=(uint)Math.Round(1000m/hz);
  var card=new byte[end*512];var h=new byte[512];Encoding.ASCII.GetBytes("NPSDLOG1").CopyTo(h,0);
  Put(h,8,7);Put(h,12,42);Put(h,32,15360);Put(h,36,128);Put(h,40,start);Put(h,44,end);Put(h,48,8192);Put(h,80,2);Put(h,96,61632);Put(h,112,hz);
  Encoding.ASCII.GetBytes("NPMEDX1\0").CopyTo(h,128);Put(h,136,1);Put(h,140,8);Put(h,168,hz);
  var f=(byte[])h.Clone();Encoding.ASCII.GetBytes("NPSDEND1").CopyTo(f,0);Put(f,60,3);Put(f,68,16);Put(f,140,15);
  Put(f,144,1000);Put(f,148,1000+span);Put(f,152,990);Put(f,156,7);Q(f,160,1700000000000000UL);
  Put(f,172,frames);Put(f,176,frames);Put(f,180,10);Put(f,184,13);Put(f,188,1000+first);Put(f,192,1000+span);Put(f,196,fault?1U:0U);
  Put(f,272,1000+span);Put(f,276,7);Q(f,280,1700000000010000UL+span*1000UL);Put(f,288,1000);Put(f,292,1000);
  Seal(h,0);Seal(f,0);h.CopyTo(card,(start+3)*512);f.CopyTo(card,(start+4)*512);
  for(int seq=0;seq<16;seq++) {
   var chunk=new byte[65536];const int o=61632;bool camera=seq<12;Encoding.ASCII.GetBytes(camera?"NPCAMBL1":"NPPADBL1").CopyTo(chunk,o);
   Put(chunk,o+8,7);Put(chunk,o+12,42);Put(chunk,o+16,(uint)seq);Put(chunk,o+20,(uint)(payload+seq*128));
   if(camera){int frame=seq/3,part=seq%3,offset=part*61440,count=Math.Min(61440,160000-offset);for(int n=0;n<count;n++)chunk[n]=(byte)(frame+1);
    uint tick=1000+(uint)Math.Round((frame+1)*1000m/hz);if(badTiming && frame==1)tick+=5;
    Put(chunk,o+24,(uint)(10+frame));Put(chunk,o+28,tick);Put(chunk,o+32,(uint)offset);Put(chunk,o+36,(uint)count);Put(chunk,o+40,160000);Put(chunk,o+44,hz);Put(chunk,o+48,320);Put(chunk,o+52,320);Put(chunk,o+56,part==0?1U:part==2?2U:0U);
   }
   Seal(chunk,o);chunk.CopyTo(card,payload*512+seq*65536);
  }
  return new MemoryStream(card,false);
 }
 static void Export(string root){
  var media=app.GetType("CE32_console.NpMediaExport",true);var json=new JavaScriptSerializer();
  foreach(uint hz in new uint[]{16,25,40,50})foreach(int failure in new[]{0,1,2}) {
   string folder=Path.Combine(root,"sd-"+hz+"-"+failure);
   using(var card=Card(hz,failure==1,failure==2))Call(media,"Export",card,8192L,18432L,folder,null,null);
   var report=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"recording.json")));
   Need(Convert.ToBoolean(report["capture_lossless_qualified"])==(failure==0),"rate-aware qualification failed: "+hz+" / "+failure+" / "+new JavaScriptSerializer().Serialize(report["qualification_reasons"]));
   Need(new FileInfo(Path.Combine(folder,"camera.raw")).Length==640000,"native camera bytes changed");
   Need(Convert.ToUInt32(report["camera_requested_or_effective_hz"])==hz,"saved camera rate changed");
  }
 }
 [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);Protocol();Export(args[1]);Console.WriteLine("PASS "+checks+" actual-assembly MEDIA1/native framing and16/25/40/50fps export/timing/fault checks; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
