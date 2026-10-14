using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using WildNpConfiguration;

namespace USB512_interface {
 internal sealed class NpGpioRtcDevice {
  internal string Port,Identity;
  public override string ToString(){return Port==null?"Off":Port+" · WILD GPIO RTC";}
  internal static List<NpGpioRtcDevice> Find() {
   var result=new List<NpGpioRtcDevice>();
   using(var search=new ManagementObjectSearcher("SELECT DeviceID, PNPDeviceID FROM Win32_SerialPort"))
   using(var rows=search.Get())foreach(ManagementObject row in rows)using(row) {
    string id=Convert.ToString(row["PNPDeviceID"]),port=Convert.ToString(row["DeviceID"]);
    if(id.IndexOf("VID_0481&PID_5741",StringComparison.OrdinalIgnoreCase)>=0 && !String.IsNullOrWhiteSpace(port))
     result.Add(new NpGpioRtcDevice {Port=port,Identity=id});
   }
   return result.OrderBy(d=>d.Port,StringComparer.OrdinalIgnoreCase).ToList();
  }
 }
 internal interface INpGpioRtcPort : IDisposable {
  void Open();int Read(byte[] buffer);void Write(byte[] buffer);void Close();
 }
 internal sealed class NpGpioRtcSerial : INpGpioRtcPort {
  readonly SerialPort port;
  internal NpGpioRtcSerial(string name) {port=new SerialPort(name,119200,Parity.None,8,StopBits.One) {
   Handshake=Handshake.None,ReadTimeout=100,WriteTimeout=500,DtrEnable=true,RtsEnable=true};}
  public void Open(){port.Open();}public int Read(byte[] b){return port.Read(b,0,b.Length);}
  public void Write(byte[] b){port.Write(b,0,b.Length);}public void Close(){port.Close();}public void Dispose(){port.Dispose();}
 }
 // Wire-compatible with Ephys_station_interface/WILD_GPIO_Logger_CSharp at af7ea59.
 // RTC events: 3C 8E channel seq:u32le device_ms:u64le 3E. No CRC is advertised.
 internal sealed class NpGpioRtcParser {
  readonly byte[] frame=new byte[16];int count,length;
  internal long InvalidFrames;internal int PendingBytes {get{return count;}}
  internal void Feed(byte value,Action<byte,uint,ulong> trigger,Action<uint,ulong> sync) {
   if(count==0){if(value==0x3c)frame[count++]=value;return;}
   frame[count++]=value;
   if(count==2){length=value==0x8e?16:value==0x91?15:value==0x92?7:0;
    if(length==0){InvalidFrames++;count=value==0x3c?1:0;}return;}
   if(count<length)return;
   count=value==0x3c?1:0;
   if(value!=0x3e){InvalidFrames++;return;}
   if(frame[1]==0x8e)trigger(frame[2],BitConverter.ToUInt32(frame,3),BitConverter.ToUInt64(frame,7));
   else if(frame[1]==0x91)sync(BitConverter.ToUInt32(frame,2),BitConverter.ToUInt64(frame,6));
   // Ignore unsolicited DFU acknowledgments; the console never sends DFU commands.
  }
 }
 internal sealed class NpGpioRtcClock {
  internal sealed class Sample {internal uint Sequence;internal long Sent,Received;internal ulong Device;internal double Offset;internal long Rtt;}
  readonly Dictionary<uint,long> pending=new Dictionary<uint,long>();readonly List<Sample> recent=new List<Sample>();
  uint next;internal Sample Best;internal long Replies,LastReply;
  internal uint Query(long now) {
   foreach(uint key in pending.Where(p=>now-p.Value>10000 || now<p.Value).Select(p=>p.Key).ToArray())pending.Remove(key);
   if(pending.Count>=64)pending.Remove(pending.OrderBy(p=>p.Value).First().Key);
   uint seq=next++;pending[seq]=now;return seq;
  }
  internal Sample Reply(uint seq,ulong device,long now) {
   long sent;if(!pending.TryGetValue(seq,out sent))return null;pending.Remove(seq);
   if(now<sent || now-sent>10000 || device>253402300799999UL)return null;
   var sample=new Sample {Sequence=seq,Sent=sent,Received=now,Device=device,Rtt=now-sent,Offset=sent+(now-sent)/2.0-device};
   recent.Add(sample);if(recent.Count>16)recent.RemoveAt(0);Best=recent.OrderBy(p=>p.Rtt).First();Replies++;LastReply=now;return sample;
  }
  internal DateTime? Map(ulong device) {
   if(Best==null)return null;double ms=Math.Round(device+Best.Offset);
   if(ms< -62135596800000.0 || ms>253402300799999.0)return null;
   return DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime;
  }
 }
 // Bounded per-channel memory: count events in the latest 1000 device-RTC milliseconds.
 // PC monotonic time advances the window between RTC observations, including silence.
 internal sealed class NpGpioEventRate {
  const ulong Window=1000;const ulong MaximumRtcMs=253402300799999UL;
  readonly SortedDictionary<byte,long[]> counts=new SortedDictionary<byte,long[]>();readonly ulong[] stamps=new ulong[1000];
  bool anchored;ulong latestDeviceMs;long latestHostMs;
  internal NpGpioEventRate(){foreach(byte id in NpGpioRtcLogger.DefaultChannelIds)counts.Add(id,new long[1000]);}
  internal void Observe(ulong deviceMs,long hostMs) {
   if(deviceMs>MaximumRtcMs)return;
   if(anchored && deviceMs<latestDeviceMs && latestDeviceMs-deviceMs>=Window) {
    foreach(var bins in counts.Values)Array.Clear(bins,0,bins.Length);anchored=false;
   }
   if(!anchored || deviceMs>=latestDeviceMs){latestDeviceMs=deviceMs;latestHostMs=hostMs;anchored=true;}
  }
  internal void Add(byte channel,ulong deviceMs,long hostMs) {
   if(deviceMs>MaximumRtcMs)return;Observe(deviceMs,hostMs);
   long[] channelBins;if(!counts.TryGetValue(channel,out channelBins)){channelBins=new long[1000];counts.Add(channel,channelBins);}
   int bin=(int)(deviceMs%Window);
   if(stamps[bin]!=deviceMs){foreach(var bins in counts.Values)bins[bin]=0;stamps[bin]=deviceMs;}
   channelBins[bin]++;
  }
  internal double[] Read(long hostMs,byte[] ids=null) {
   ids=ids??counts.Keys.ToArray();var hz=new double[ids.Length];if(!anchored)return hz;
   ulong now=latestDeviceMs+(ulong)Math.Max(0,hostMs-latestHostMs);
   for(int p=0;p<ids.Length;p++){long[] bins;if(!counts.TryGetValue(ids[p],out bins))continue;
    for(int bin=0;bin<1000;bin++)if(stamps[bin]<=now && now-stamps[bin]<Window)hz[p]+=bins[bin];}
   return hz; // One-second window: event count / 1 s.
  }
 }
 internal sealed class NpGpioRtcActivity {
  internal readonly byte[] Ids;internal readonly long[] Counts;internal readonly double[] Frequencies;
  internal NpGpioRtcActivity(byte[] ids,long[] counts,double[] frequencies){Ids=ids;Counts=counts;Frequencies=frequencies;}
 }
 internal sealed class NpGpioRtcLogger : IDisposable {
  internal static byte[] DefaultChannelIds {get{return new byte[]{0,1,2,3};}}
  sealed class Line {internal bool Sync;internal string Text;}
  readonly string folder;readonly NpGpioRtcDevice device;readonly INpGpioRtcPort port;
  readonly BlockingCollection<Line> lines=new BlockingCollection<Line>(16384);
  readonly ManualResetEvent stop=new ManualResetEvent(false);readonly object gate=new object();
  readonly NpGpioRtcParser parser=new NpGpioRtcParser();readonly NpGpioRtcClock clock=new NpGpioRtcClock();
  readonly NpGpioEventRate eventRate=new NpGpioEventRate();
  StreamWriter events,sync;Thread receiver,writer,probes;volatile bool running;bool disposed,haveSequence;
  uint lastSequence;long received,saved,rejected,gaps;DateTime started,finished;internal DateTime? NeuralFirstReceiveUtc;
  readonly SortedDictionary<byte,long> channelEvents=new SortedDictionary<byte,long>();byte lastChannel;ulong lastDeviceMs;
  string error;internal string Error {get{lock(gate)return error;}}
  internal long EventsSaved {get{return Interlocked.Read(ref saved);}}
  internal long[] ChannelCounts {get{lock(gate)return channelEvents.Values.ToArray();}}
  internal NpGpioRtcActivity GetActivity() {lock(gate){var ids=channelEvents.Keys.ToArray();
   return new NpGpioRtcActivity(ids,channelEvents.Values.ToArray(),eventRate.Read(MonotonicMs(),ids));}}
  internal bool IsLogging {get{return running;}}
  internal long SyncReplies {get{lock(gate)return clock.Replies;}}
  internal bool ClockSynchronized {get{lock(gate)return clock.Best!=null && Now()>=clock.LastReply && Now()-clock.LastReply<=10000;}}
  internal NpGpioRtcLogger(string directory,NpGpioRtcDevice selected,INpGpioRtcPort transport=null) {
   folder=directory;device=selected;port=transport??new NpGpioRtcSerial(selected.Port);
   foreach(byte id in DefaultChannelIds)channelEvents.Add(id,0);
  }
  static long Now(){return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();}
  static long MonotonicMs(){return (long)(Stopwatch.GetTimestamp()*(1000.0/Stopwatch.Frequency));}
  static string F(double number){return number.ToString("R",CultureInfo.InvariantCulture);}
  void Fail(string message){lock(gate){if(error==null)error=message;}running=false;stop.Set();}
  internal void Start() {
   started=DateTime.UtcNow;
   try {
    events=Open("gpio_rtc_events.txt");sync=Open("gpio_rtc_sync.csv");
    sync.WriteLine("kind,query_sequence,pc_send_unix_ms,pc_receive_unix_ms,device_rtc_ms,rtt_ms,offset_ms,selected_query,selected_offset_ms");sync.Flush();
    port.Open();running=true;SaveReceipt("logging");
    writer=Launch("GPIO RTC file writer",WriteLoop);receiver=Launch("GPIO RTC receiver",ReceiveLoop);probes=Launch("GPIO RTC clock queries",SyncLoop);
   }catch(Exception ex){Fail("GPIO/RTC could not start: "+ex.Message);Dispose();throw new IOException(Error,ex);}
  }
  StreamWriter Open(string name){return new StreamWriter(new FileStream(Path.Combine(folder,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));}
  static Thread Launch(string name,ThreadStart action){var t=new Thread(action){IsBackground=true,Name=name};t.Start();return t;}
  void Enqueue(bool isSync,string text) {
   if(!lines.TryAdd(new Line {Sync=isSync,Text=text})){Interlocked.Increment(ref rejected);Fail("GPIO/RTC file queue is full; event log is incomplete.");}
  }
  void ReceiveLoop() {
   var buffer=new byte[4096];
   try {while(running)try {int n=port.Read(buffer);for(int i=0;i<n;i++)parser.Feed(buffer[i],Trigger,Reply);}
    catch(TimeoutException){} }
   catch(Exception ex){if(running)Fail("GPIO/RTC receive failed: "+ex.Message);}
  }
  void Trigger(byte channel,uint sequence,ulong deviceMs) {
   DateTime receive=DateTime.UtcNow;DateTime? mapped;
   lock(gate){mapped=ClockSynchronized?clock.Map(deviceMs):null;long count;channelEvents.TryGetValue(channel,out count);channelEvents[channel]=count+1;
    eventRate.Add(channel,deviceMs,MonotonicMs());lastChannel=channel;lastDeviceMs=deviceMs;}
   if(haveSequence && sequence!=unchecked(lastSequence+1))Interlocked.Increment(ref gaps);
   haveSequence=true;lastSequence=sequence;Interlocked.Increment(ref received);
   Enqueue(false,String.Format(CultureInfo.InvariantCulture,"Time: {0}, DeviceMs: {1}, Sequence: {2}, Data: {3}, PcReceive: {4}",
    mapped.HasValue?mapped.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",CultureInfo.InvariantCulture):"UNSYNCED",deviceMs,sequence,channel,
    receive.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff",CultureInfo.InvariantCulture)));
  }
  void Reply(uint sequence,ulong deviceMs) {
   long now=Now();NpGpioRtcClock.Sample sample,best;
   lock(gate){sample=clock.Reply(sequence,deviceMs,now);best=clock.Best;if(sample!=null)eventRate.Observe(deviceMs,MonotonicMs());}
   Enqueue(true,"reply,"+sequence+","+(sample==null?"":sample.Sent.ToString(CultureInfo.InvariantCulture))+","+now+","+deviceMs+","+
    (sample==null?",":sample.Rtt+","+F(sample.Offset))+","+(best==null?",":best.Sequence+","+F(best.Offset)));
  }
  void SyncLoop() {
   try {int sent=0;while(running) {
    long now=Now();uint sequence;lock(gate){sequence=clock.Query(now);}
    byte[] query={0x3c,0x90,0,0,0,0,0x3e};Buffer.BlockCopy(BitConverter.GetBytes(sequence),0,query,2,4);
    Enqueue(true,"query,"+sequence+","+now+",,,,,,");port.Write(query);
    if(stop.WaitOne(++sent<20?50:1000))break;
   }}catch(Exception ex){if(running)Fail("GPIO/RTC clock query failed: "+ex.Message);}
  }
  void WriteLoop() {
   try {while(!lines.IsCompleted) {Line line;
    if(lines.TryTake(out line,100)){(line.Sync?sync:events).WriteLine(line.Text);if(!line.Sync)Interlocked.Increment(ref saved);}
    if(lines.Count==0){events.Flush();sync.Flush();}
   }events.Flush();sync.Flush();}
   catch(Exception ex){Fail("GPIO/RTC file write failed: "+ex.Message);}
  }
  void Join(Thread thread,string role) {if(thread!=null && !thread.Join(5000))Fail("GPIO/RTC "+role+" did not finish; log is incomplete.");}
  internal string Warning {
   get {
    if(Error!=null)return Error;
    if(Interlocked.Read(ref gaps)>0 || parser.InvalidFrames>0 || (finished!=DateTime.MinValue && parser.PendingBytes>0))return "GPIO/RTC event continuity warning: "+Interlocked.Read(ref gaps)+" sequence discontinuities, "+parser.InvalidFrames+" invalid frames, "+parser.PendingBytes+" pending frame bytes.";
    if(started!=DateTime.MinValue && (DateTime.UtcNow-started).TotalSeconds>3 && !ClockSynchronized)return "GPIO/RTC clock has no recent valid reply; events are saved as UNSYNCED.";
    return null;
   }
  }
  internal string WarningKey {get{return Error!=null?"gpio-runtime":Interlocked.Read(ref gaps)>0 || parser.InvalidFrames>0 || (finished!=DateTime.MinValue && parser.PendingBytes>0)?"gpio-continuity":"gpio-unsynced";}}
  internal string Describe(string state) {
   lock(gate)return device.Port+" · "+state+Environment.NewLine+
    "Events: "+EventsSaved.ToString("N0")+" | Clock: "+(!ClockSynchronized?"unsynced":"RTT "+clock.Best.Rtt+" ms")+Environment.NewLine+
    (received==0?"Waiting for events":"Last: CH"+lastChannel+" · RTC "+lastDeviceMs+" ms")+(Error==null?"":Environment.NewLine+Error);
  }
  void SaveReceipt(string state) {
   NpGpioRtcClock.Sample best;lock(gate){best=clock.Best;}
   var activity=GetActivity();
   var receipt=new {schema_version=1,format="wild.gpio_rtc",state=state,port=device.Port,pnp_device_id=device.Identity,baud=119200,
    started_utc=started.ToString("O"),stopped_utc=finished==DateTime.MinValue?null:finished.ToString("O"),
    neural_first_receive_utc=NeuralFirstReceiveUtc.HasValue?NeuralFirstReceiveUtc.Value.ToString("O"):null,
    events_file="gpio_rtc_events.txt",clock_queries_file="gpio_rtc_sync.csv",events_received=Interlocked.Read(ref received),events_saved=EventsSaved,
    channel_ids=activity.Ids.Select(id=>(int)id).ToArray(),channel_event_counts=activity.Counts,
    last_channel=haveSequence?(byte?)lastChannel:null,last_device_rtc_ms=haveSequence?(ulong?)lastDeviceMs:null,
    recent_channel_event_rate_hz=activity.Frequencies,event_rate_window_ms=1000,
    event_rate_timebase="received events in device RTC window; PC monotonic extrapolation between RTC observations; not waveform frequency",
    rejected_records=Interlocked.Read(ref rejected),sequence_discontinuities=Interlocked.Read(ref gaps),invalid_frames=parser.InvalidFrames,pending_frame_bytes=parser.PendingBytes,
    valid_clock_replies=SyncReplies,clock_synchronized=ClockSynchronized,best_rtt_ms=best==null?(long?)null:best.Rtt,selected_offset_ms=best==null?(double?)null:best.Offset,
    error=Error,complete=state=="stopped" && Error==null && received==saved && gaps==0 && parser.InvalidFrames==0 && parser.PendingBytes==0,
    time_mapping="RTC milliseconds mapped to PC by the lowest-RTT offset among the latest 16 replies; raw query observations retained. Not synchronized to the neural ADC clock."};
   NpMetadataFile.WriteText(Path.Combine(folder,"gpio_rtc_session.json"),new JavaScriptSerializer().Serialize(receipt)+Environment.NewLine);
  }
  public void Dispose() {
   if(disposed)return;disposed=true;running=false;stop.Set();
   try{port.Close();}catch(Exception ex){Fail("GPIO/RTC close failed: "+ex.Message);}
   Join(receiver,"receiver");Join(probes,"clock worker");
   // Producers finish before CompleteAdding; the writer drains all accepted records.
   lines.CompleteAdding();Join(writer,"file writer");
   try{if(events!=null)events.Dispose();if(sync!=null)sync.Dispose();}catch(Exception ex){Fail("GPIO/RTC flush failed: "+ex.Message);}
   try{port.Dispose();}catch(Exception ex){Fail("GPIO/RTC port disposal failed: "+ex.Message);}
   finished=DateTime.UtcNow;try{SaveReceipt(Error==null?"stopped":"error");}catch(Exception ex){Fail("GPIO/RTC session metadata failed: "+ex.Message);}
   // Avoid disposing synchronization objects if a broken serial driver left a worker alive.
   if((receiver==null || !receiver.IsAlive) && (probes==null || !probes.IsAlive) && (writer==null || !writer.IsAlive)){lines.Dispose();stop.Dispose();}
  }
 }
}
