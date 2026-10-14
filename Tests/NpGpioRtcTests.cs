using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using USB512_interface;

// File/protocol fixture only: never enumerates or opens a physical COM port.
namespace WildNpConfiguration {internal static class NpMetadataFile {internal static void WriteText(string path,string text){File.WriteAllText(path,text);}}}
static class NpGpioRtcTests {
 static int checks;
 static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static byte[] Event(uint seq,byte channel=1){byte[] b=new byte[16];b[0]=0x3c;b[1]=0x8e;b[2]=channel;Buffer.BlockCopy(BitConverter.GetBytes(seq),0,b,3,4);Buffer.BlockCopy(BitConverter.GetBytes(100000UL+seq),0,b,7,8);b[15]=0x3e;return b;}
 sealed class Port : INpGpioRtcPort {
  readonly BlockingCollection<byte[]> input=new BlockingCollection<byte[]>();internal bool Closed,Disposed,OpenFails,Reply=true;internal volatile bool ReadFails;
  internal readonly List<byte[]> Commands=new List<byte[]>();internal int Reads;
  public void Open(){if(OpenFails)throw new IOException("simulated busy COM");}
  internal void Feed(byte[] b){input.Add(b);}
  public int Read(byte[] b){if(ReadFails)throw new IOException("simulated disconnect");byte[] data;if(input.TryTake(out data,50)){Buffer.BlockCopy(data,0,b,0,data.Length);Interlocked.Increment(ref Reads);return data.Length;}if(Closed)throw new IOException("closed");throw new TimeoutException();}
  public void Write(byte[] b){lock(Commands)Commands.Add((byte[])b.Clone());if(!Reply)return;
   byte[] r=new byte[15];r[0]=0x3c;r[1]=0x91;Buffer.BlockCopy(b,2,r,2,4);Buffer.BlockCopy(BitConverter.GetBytes(100000UL),0,r,6,8);r[14]=0x3e;Feed(r);}
  public void Close(){Closed=true;}public void Dispose(){Disposed=true;}
 }
 static Dictionary<string,object> Receipt(string folder){return (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(folder,"gpio_rtc_session.json")));}
 static void Until(Func<bool> done){DateTime end=DateTime.UtcNow.AddSeconds(5);while(!done() && DateTime.UtcNow<end)Thread.Sleep(10);if(!done())throw new Exception("fixture timed out");}
 static int Main(string[] args){try {
  string root=Path.Combine(Path.GetFullPath(args[0]),"run_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var rate=new NpGpioEventRate();Check(rate.Read(0).All(f=>f==0),"idle GPIO frequency was nonzero");
  // One second of device events delivered together must retain its device rate.
  for(int i=0;i<100;i++)rate.Add(0,(ulong)(10000+i*10),500);
  rate.Add(1,10990,500);var hz=rate.Read(500);
  Check(hz.SequenceEqual(new double[]{100,1,0,0}),"USB batching changed per-port device-window rate");
  Check(rate.Read(510)[0]==99,"event was not removed at the exact one-second boundary");
  Check(rate.Read(1500).All(f=>f==0),"silent GPIO rate stayed nonzero");
  rate.Observe(12000,2000);Check(rate.Read(2000).All(f=>f==0),"clock observation did not advance idle window");
  rate.Add(2,500,3000);Check(rate.Read(3000).SequenceEqual(new double[]{0,0,1,0}),"RTC rollback did not reset the frequency window");
  rate.Add(9,600,3000);rate.Add(0,UInt64.MaxValue,3000);Check(rate.Read(3000).SequenceEqual(new double[]{0,0,1,0}),"invalid port/RTC corrupted frequency");
  hz=rate.Read(3000);hz[2]=999;Check(rate.Read(3000)[2]==1,"frequency snapshot mutated live data");
  rate=new NpGpioEventRate();for(int i=0;i<10000;i++)rate.Add(3,1000,4000);
  Check(rate.Read(4000)[3]==10000 && rate.Read(5000)[3]==0,"same-millisecond burst count or idle expiration failed");
  var parser=new NpGpioRtcParser();int events=0,replies=0;uint last=0;ulong ms=0;byte channel=0;
  Action<byte,uint,ulong> trigger=(c,s,t)=>{events++;last=s;ms=t;channel=c;};Action<uint,ulong> sync=(s,t)=>replies++;
  foreach(byte b in Event(UInt32.MaxValue,3))parser.Feed(b,trigger,sync);
  Check(events==1 && last==UInt32.MaxValue && ms==100000UL+UInt32.MaxValue && channel==3,"fragmented RTC event decoding changed");
  byte[] bad=Event(1);bad[15]=0;foreach(byte b in bad.Concat(Event(2)))parser.Feed(b,trigger,sync);
  Check(events==2 && last==2 && parser.InvalidFrames==1,"malformed frame did not recover");
  foreach(byte b in new byte[]{0x3c,0x92,1,0,0,0,0x3e})parser.Feed(b,trigger,sync);
  Check(events==2 && replies==0,"unsolicited DFU ACK was interpreted as data");
  var clock=new NpGpioRtcClock();Check(clock.Map(42)==null,"unsynchronized event got a PC timestamp");
  uint q=clock.Query(1000);clock.Reply(q,500,1020);Check(clock.Best.Offset==510 && clock.Map(500).Value==DateTimeOffset.FromUnixTimeMilliseconds(1010).UtcDateTime,"midpoint offset changed");
  q=clock.Query(2000);clock.Reply(q,1000,2002);Check(clock.Best.Rtt==2 && clock.Best.Offset==1001,"lowest RTT selection failed");
  Check(clock.Reply(q,0,2003)==null && clock.Reply(999,0,2003)==null,"duplicate/unknown clock reply accepted");
  q=clock.Query(3000);Check(clock.Reply(q,10,2999)==null,"host clock rollback accepted");
  q=clock.Query(4000);Check(clock.Reply(q,UInt64.MaxValue,4001)==null,"invalid RTC date accepted");
  for(int i=0;i<17;i++){q=clock.Query(5000+i*10);clock.Reply(q,(ulong)(1000+i*10),5004+i*10);}
  Check(clock.Best.Rtt==4,"clock fit did not retain only the latest 16 replies");
  string folder=Path.Combine(root,"first");Directory.CreateDirectory(folder);var port=new Port();
  using(var logger=new NpGpioRtcLogger(folder,new NpGpioRtcDevice{Port="COM77",Identity="fixture-board"},port)) {
   logger.Start();Until(()=>logger.SyncReplies>0);
   // Exercise split reads, a large burst, wraparound and draining at Stop.
   var first=Event(UInt32.MaxValue);port.Feed(first.Take(5).ToArray());port.Feed(first.Skip(5).ToArray());
   for(uint i=0;i<1000;i++)port.Feed(Event(i));
   Until(()=>port.Reads>=1003);logger.NeuralFirstReceiveUtc=DateTime.UtcNow;
   logger.Dispose();string panel=logger.Describe("Stopped");
   Check(panel.Contains("COM77 · Stopped") && logger.ChannelCounts.SequenceEqual(new long[]{0,1001,0,0}) && panel.Contains("RTC 100999 ms") && panel.Contains("RTT") && !logger.IsLogging,"GPIO panel lost final device/channel/clock/event information");
   var copiedCounts=logger.ChannelCounts;copiedCounts[1]=0;Check(logger.ChannelCounts[1]==1001,"UI snapshot could alter recorded port counts");
  }
  string[] lines=File.ReadAllLines(Path.Combine(folder,"gpio_rtc_events.txt"));var receipt=Receipt(folder);
  Check(lines.Length==1001 && lines[0].Contains("Sequence: 4294967295") && lines.Last().Contains("Sequence: 999"),"Stop did not drain every accepted event in order");
  Check(lines.All(l=>l.Contains("DeviceMs:") && l.Contains("PcReceive:") && !l.Contains("UNSYNCED")),"RTC/raw/PC timestamps missing");
  Check((bool)receipt["complete"] && Convert.ToInt64(receipt["events_saved"])==1001 && Convert.ToInt64(receipt["sequence_discontinuities"])==0,"wraparound counted as a gap or receipt incomplete");
  Check(((object[])receipt["recent_channel_event_rate_hz"]).Length==4 && Convert.ToInt32(receipt["event_rate_window_ms"])==1000 && ((string)receipt["event_rate_timebase"]).Contains("device RTC"),"frequency timing metadata not saved with GPIO session");
  Check((string)receipt["port"]=="COM77" && (string)receipt["pnp_device_id"]=="fixture-board" && receipt["neural_first_receive_utc"]!=null,"selected board or neural anchor not retained");
  Check(port.Closed && port.Disposed && port.Commands.Count>0 && port.Commands.All(b=>b.Length==7 && b[0]==0x3c && b[1]==0x90 && b[6]==0x3e),"wrong command or serial handle leaked");
  var csv=File.ReadAllLines(Path.Combine(folder,"gpio_rtc_sync.csv"));Check(csv.Any(l=>l.StartsWith("query,")) && csv.Any(l=>l.StartsWith("reply,")) && csv.All(l=>l.Split(',').Length==9),"raw clock query/reply observations missing or malformed CSV");
  string second=Path.Combine(root,"second");Directory.CreateDirectory(second);var noSync=new Port {Reply=false};
  using(var logger=new NpGpioRtcLogger(second,new NpGpioRtcDevice{Port="COM77",Identity="fixture-board"},noSync)) {
   logger.Start();noSync.Feed(Event(1));noSync.Feed(Event(3));Until(()=>logger.EventsSaved==2);
   Check(logger.Warning!=null,"sequence discontinuity was silent");
  }
  Check(File.ReadAllLines(Path.Combine(second,"gpio_rtc_events.txt")).All(l=>l.Contains("UNSYNCED")) && !(bool)Receipt(second)["complete"],"unsynced events or gap were hidden");
  Check(File.ReadAllLines(Path.Combine(folder,"gpio_rtc_events.txt")).Length==1001,"new recording appended into old log");
  string fail=Path.Combine(root,"failed");Directory.CreateDirectory(fail);var busy=new Port{OpenFails=true};
  try{using(var logger=new NpGpioRtcLogger(fail,new NpGpioRtcDevice{Port="COM77",Identity="fixture-board"},busy))logger.Start();throw new Exception("startup failure swallowed");}catch(IOException){}
  Check((string)Receipt(fail)["state"]=="error" && ((string)Receipt(fail)["error"]).Contains("simulated busy COM") && busy.Disposed,"failed start lost diagnostics or leaked port");
  string disconnect=Path.Combine(root,"disconnect");Directory.CreateDirectory(disconnect);var unplugged=new Port();
  using(var logger=new NpGpioRtcLogger(disconnect,new NpGpioRtcDevice{Port="COM77",Identity="fixture-board"},unplugged)) {
   logger.Start();Until(()=>logger.SyncReplies>0);unplugged.ReadFails=true;Until(()=>logger.Error!=null);
   Check(logger.Warning.Contains("simulated disconnect") && logger.WarningKey=="gpio-runtime","runtime disconnect was silent");
  }
  Check(!(bool)Receipt(disconnect)["complete"] && (string)Receipt(disconnect)["state"]=="error" && unplugged.Disposed,"disconnect left a successful receipt or open handle");
  string full=Path.Combine(root,"queue-full");Directory.CreateDirectory(full);
  using(var logger=new NpGpioRtcLogger(full,new NpGpioRtcDevice{Port="COM77",Identity="fixture-board"},new Port())) {
   // Isolate bounded-queue admission before starting a consumer; no timing race.
   var accept=typeof(NpGpioRtcLogger).GetMethod("Trigger",BindingFlags.Instance|BindingFlags.NonPublic);
   for(uint i=0;i<16385;i++)accept.Invoke(logger,new object[]{(byte)1,i,100000UL+i});
   Check(logger.Error!=null && logger.Error.Contains("queue is full"),"bounded queue overflow was silent");
  }
  Check(Convert.ToInt32(Receipt(full)["rejected_records"])==1 && !(bool)Receipt(full)["complete"],"queue overflow lost its incomplete evidence");
  Console.WriteLine("PASS "+checks+" GPIO RTC protocol/clock/file lifecycle checks; simulated serial only. "+root);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
