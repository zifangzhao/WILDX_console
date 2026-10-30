using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using USB512_interface;

// Dependency adapters invoke the unchanged actual shipping204 implementations;
// only NpFirmwareImage is compiled from the changed production source here.
namespace CE32_console { }
namespace USB512_interface {
 internal static class NpDownloadStream {
  internal static Func<byte[],int,int,uint> Actual;
  internal static uint Crc(byte[] b,int at,int count){return Actual(b,at,count);}
 }
 internal static class NpV1Management {
  internal const uint Capacity=0x15e000;
  internal static Action<byte[]> Actual;
  internal static void ValidateAppBytes(byte[] b){Actual(b);}
 }
}
static class NpFirmwareSparseImageTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static int checks;static string output;static Dictionary<uint,byte> original;
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static void Reject(Action a,string why){try{a();}catch(InvalidDataException){checks++;return;}catch(IOException){checks++;return;}throw new Exception("Accepted "+why);}
 static string Record(ushort offset,int type,byte[] data){var b=new List<byte>{(byte)data.Length,(byte)(offset>>8),(byte)offset,(byte)type};b.AddRange(data);b.Add((byte)-b.Sum(x=>(int)x));return ":"+BitConverter.ToString(b.ToArray()).Replace("-","");}
 static Dictionary<uint,byte> Read(string path){var m=new Dictionary<uint,byte>();uint upper=0;foreach(string line in File.ReadLines(path)){
  var b=Enumerable.Range(0,(line.Length-1)/2).Select(i=>Convert.ToByte(line.Substring(1+2*i,2),16)).ToArray();Need((b.Sum(x=>(int)x)&255)==0,"fixture HEX checksum");
  int count=b[0],type=b[3];if(type==4)upper=(uint)(b[4]*256+b[5])<<16;if(type==0)for(int i=0;i<count;i++)m.Add(upper+(uint)(b[1]*256+b[2]+i),b[4+i]);}return m;}
 static string Write(Dictionary<uint,byte> m,string name){string p=Path.Combine(output,name+".hex");using(var w=new StreamWriter(p,false,Encoding.ASCII)){
  var keys=m.Keys.OrderBy(x=>x).ToArray();uint upper=uint.MaxValue;int i=0;while(i<keys.Length){uint start=keys[i];if(start>>16!=upper){upper=start>>16;w.WriteLine(Record(0,4,new[]{(byte)(upper>>8),(byte)upper}));}
   var bytes=new List<byte>{m[start]};i++;while(i<keys.Length&&bytes.Count<32&&keys[i]==start+bytes.Count&&(keys[i]>>16)==upper)bytes.Add(m[keys[i++]]);w.WriteLine(Record((ushort)start,0,bytes.ToArray()));}w.WriteLine(Record(0,1,new byte[0]));}return p;}
 static NpFirmwareImage V2(Dictionary<uint,byte> m,string name){string p=Write(m,name);return NpFirmwareImage.LoadBundledV2(p,NpFirmwareImage.Sha(File.ReadAllBytes(p)));}
 static void Mutate(string name,Action<Dictionary<uint,byte>> mutation){var m=new Dictionary<uint,byte>(original);mutation(m);Reject(()=>V2(m,name),name);}
 static void U(Dictionary<uint,byte> m,uint at,uint v){for(int i=0;i<4;i++)m[at+(uint)i]=(byte)(v>>(i*8));}
 static void ShortUsb(Dictionary<uint,byte> m){const uint manifest=0x0817e000,start=0x08180000;foreach(uint k in m.Keys.Where(k=>k>=start).ToArray())m.Remove(k);
  var body=new byte[32];Buffer.BlockCopy(BitConverter.GetBytes(0x20010000U),0,body,0,4);Buffer.BlockCopy(BitConverter.GetBytes(start+9),0,body,4,4);for(int i=8;i<body.Length;i++)body[i]=(byte)(i+13);
  for(int i=0;i<body.Length;i++)m[start+(uint)i]=body[i];var h=Enumerable.Range(0,128).Select(i=>m[manifest+(uint)i]).ToArray();
  Buffer.BlockCopy(BitConverter.GetBytes((uint)body.Length),0,h,28,4);Buffer.BlockCopy(BitConverter.GetBytes(NpDownloadStream.Crc(body,0,body.Length)),0,h,32,4);Buffer.BlockCopy(body,0,h,36,8);
  Buffer.BlockCopy(BitConverter.GetBytes(NpDownloadStream.Crc(h,0,124)),0,h,124,4);for(int i=0;i<h.Length;i++)m[manifest+(uint)i]=h[i];}
 static void Overlay(NpFirmwareImage image,int length){var before=new byte[length];new Random(1701+length).NextBytes(before);var expected=image.ExpectedFlash(before);var supplied=original;
  for(int i=0;i<length;i++)Need(expected[i]==(supplied.ContainsKey(0x08000000U+(uint)i)?supplied[0x08000000U+(uint)i]:before[i]),"backup overlay byte"+i);
  Need(expected[0x74c0]==before[0x74c0],"sparse recovery tail changed");Need(expected.Skip(0x1c000).Take(8192).SequenceEqual(before.Skip(0x1c000).Take(8192)),"config changed");
  string path=Path.Combine(output,"pages-"+length+".hex");using(var held=image.WritePageHex(path,before,expected)){Need(held!=null,"no changed pages emitted");}
  var pages=Read(path);foreach(var pair in pages){int i=(int)(pair.Key-0x08000000);Need(i<0x200000&&!(i>=0x1c000&&i<0x1e000),"protected/secondbank page emitted");Need(pair.Value==expected[i],"emitted bytes differ");}
  Need(pages.ContainsKey(0x080074c0)&&pages[0x080074c0]==before[0x74c0],"boot-tail same-page backup lost");
  using(var unchanged=image.WritePageHex(Path.Combine(output,"no-change-"+length+".hex"),expected,expected)){Need(unchanged==null,"identical image wrote pages");}
 }
 static int Main(string[] args){try{
  var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));var crc=app.GetType("CE32_console.NpDownloadStream",true).GetMethod("Crc",All,null,new[]{typeof(byte[]),typeof(int),typeof(int)},null);
  NpDownloadStream.Actual=(b,a,n)=>(uint)crc.Invoke(null,new object[]{b,a,n});var management=app.GetType("USB512_interface.NpV1Management",true);
  Need((uint)management.GetField("Capacity",All).GetRawConstantValue()==NpV1Management.Capacity,"actual V1 capacity differs");var validate=management.GetMethod("ValidateAppBytes",All);
  NpV1Management.Actual=b=>{try{validate.Invoke(null,new object[]{b});}catch(TargetInvocationException ex){throw ex.InnerException;}};
  output=Path.GetFullPath(args[5]);Directory.CreateDirectory(output);var image=NpFirmwareImage.LoadBundledV2(args[1],args[2]);Need(image.Full,"shipping V2 not full");
  Need(NpFirmwareImage.Load(args[3]).Full,"shipping V1 full rejected");Need(!NpFirmwareImage.Load(args[4]).Full,"shipping V1 APP rejected");
  Reject(()=>NpFirmwareImage.Load(args[1]),"V2 through V1 parser");Reject(()=>NpFirmwareImage.LoadBundledV2(args[1],new string('0',64)),"wrong catalog SHA");
  original=Read(args[1]);Need(original.Keys.Where(k=>k<0x0801c000).Max()==0x080074bf,"shipping recovery extent changed");Overlay(image,0x200000);Overlay(image,0x400000);
  Mutate("recovery-internal-hole",m=>m.Remove(0x08000100));Mutate("recovery-vector-missing",m=>m.Remove(0x08000000));Mutate("reset-code-missing",m=>m.Remove((BitConverter.ToUInt32(Enumerable.Range(0,4).Select(i=>m[0x08000004U+(uint)i]).ToArray(),0)&~1U)));
  Mutate("reset-in-absent-tail",m=>U(m,0x08000004,0x080074c1));Mutate("truncated-reset-instruction",m=>{U(m,0x08000004,0x080074bf);m.Remove(0x080074bf);});
  Mutate("config-write",m=>m[0x0801c000]=1);Mutate("truncated-app-manifest",m=>m.Remove(0x0801e07f));Mutate("bad-app-crc",m=>m[0x08020100]^=1);
  Mutate("truncated-usb-manifest",m=>m.Remove(0x0817e07f));Mutate("bad-usb-crc",m=>m[0x08180100]^=1);
  var shortUsb=new Dictionary<uint,byte>(original);ShortUsb(shortUsb);Need(V2(shortUsb,"short-usb-no-legacy").Full,"absent legacy manifest treated as zero");
  var partial=new Dictionary<uint,byte>(shortUsb);partial[0x081c0000]=7;Reject(()=>V2(partial,"unknown-legacy-payload"),"unknown partial legacy payload");
  Console.WriteLine("PASS "+checks+" sparse-bundle/strict-mutant/2MiB+4MiB-overlay/page-preservation assertions; actual204 CRC and V1 validation; no Form/device/flash.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
