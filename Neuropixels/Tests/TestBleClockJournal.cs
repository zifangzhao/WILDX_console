using System;
using System.IO;
using System.Reflection;

// Exact built GUI, mocked clock receipts and local sidecars only; no BLE objects.
internal static class TestBleClockJournal {
 const BindingFlags Static=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static;
 const uint RequestMagic=0x3254504e,ReplyMagic=0x3252504e;
 static int checks;
 static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
 static uint Crc(byte[] b){uint c=~0U;for(int i=0;i<60;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((c&1)==0?0:0xedb88320U);}return c^~0U;}
 static byte[] Request(uint op=0,uint id=17){var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,1);Put(b,8,op);Put(b,12,id);Put(b,60,Crc(b));return b;}
 static byte[] Reply(uint rx,uint tx,uint gen=2,uint id=17,uint error=0){var b=new byte[64];Put(b,0,ReplyMagic);Put(b,4,1);Put(b,12,id);Put(b,16,error);Put(b,20,rx);Put(b,24,tx);Put(b,28,gen);Put(b,52,1000);Put(b,60,Crc(b));return b;}
 static void Main(string[] args) {
  if(args.Length!=2)throw new ArgumentException("Usage: TestBleClockJournal <WILDX_console.exe> <fresh-output-folder>");
  string output=Path.GetFullPath(args[1]);if(Directory.Exists(output))throw new IOException("Fresh test output required.");
  Directory.CreateDirectory(output);var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
  Type route=assembly.GetType("USB512_interface.NpBleConnection",true),journalType=assembly.GetType("USB512_interface.NpClockJournal",true);
  var admit=route.GetMethod("IsReadOnlyClockRequest",Static);var record=route.GetMethod("ReadOnlyClockObservation",Static);
  Func<byte,byte[],bool> allowed=(cmd,req)=>(bool)admit.Invoke(null,new object[]{cmd,req});
  Check(allowed(0xb9,Request()),"shared V1/V2 read-only clock admitted");Check(!allowed(0xb9,Request(1)),"clock commit excluded");
  Check(!allowed(0xb8,Request()),"configuration command excluded");Check(!allowed(0xb9,null),"null request excluded");
  var wrong=Request();Put(wrong,0,0x31444c4e);Put(wrong,60,Crc(wrong));Check(!allowed(0xb9,wrong),"NP status magic excluded");
  wrong=Request();wrong[60]^=1;Check(!allowed(0xb9,wrong),"corrupt request excluded");
  object journal=Activator.CreateInstance(journalType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,
   new object[]{output,"BLE-E7237BAB7355","connection-5-mock"},null);
  Func<byte,byte[],byte[],long,long,bool> log=(cmd,req,res,start,end)=>(bool)record.Invoke(null,new object[]{journal,cmd,req,res,start,end});
  var request=Request();var response=Reply(100,104);var original=(byte[])response.Clone();
  Check(log(0xb9,request,response,100000,124000),"valid receipt journaled");
  Check(log(0xb9,request,Reply(UInt32.MaxValue-1,2),200000,224000),"device RX/TX wrap handled");
  Check(!log(0xb9,Request(1),response,100000,124000),"mutation never journaled");
  Check(!log(0xb9,request,Reply(100,104,id:18),100000,124000),"stale transaction rejected");
  Check(!log(0xb9,request,Reply(100,104,error:2),100000,124000),"device error rejected");
  wrong=Reply(100,104);wrong[60]^=1;Check(!log(0xb9,request,wrong,100000,124000),"receipt CRC rejected");
  Check(!log(0xb9,request,Reply(100,104),124000,100000),"negative host interval rejected");
  Check(!log(0xb9,request,Reply(100,500),100000,124000),"impossible processing time rejected");
  for(int i=0;i<response.Length;i++)Check(response[i]==original[i],"raw receipt unchanged");
  ((IDisposable)journal).Dispose();string[] csv=File.ReadAllLines(Path.Combine(output,"clock_observations.csv"));
  Check(csv.Length==3,"only two valid read-only observations");string[] fields=csv[1].Split(',');
  Check(fields[0]=="BLE-E7237BAB7355" && fields[1]=="connection-5-mock","transport identity, not invented MCU UID");
  Check(fields[3]=="2" && fields[4]=="104" && fields[5]=="114000" && fields[7]=="11000","TX coordinate, processing-adjusted midpoint and uncertainty");
  Check(File.ReadAllText(Path.Combine(output,"clock_linear_fit.txt")).Contains("raw_data_changed=false"),"offline-only report");
  string invalid=Path.Combine(output,"unwritable");File.WriteAllText(invalid,"not a directory");
  journal=Activator.CreateInstance(journalType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,
   new object[]{invalid,"BLE-E7237BAB7355","connection-6-mock"},null);
  Check(!log(0xb9,request,response,100000,124000),"filesystem failure isolated");((IDisposable)journal).Dispose();
  Console.WriteLine("BLE read-only clock journal: PASS ("+checks+" checks); no BLE/native calls.");
 }
}
