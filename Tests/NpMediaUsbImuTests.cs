using System;
using System.Diagnostics;
using System.IO;

// Compile the actual optional sink, sensor writer, packet parser and recorder.
// Only local synthetic packets/files are used; no form or hardware is opened.
internal static class NpMediaUsbImuTests
{
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    static string Quote(string s){return "\""+s.Replace("\"","\\\"")+"\"";}
    static string Class(string source,string name){
        int start=source.IndexOf(name,StringComparison.Ordinal);Need(start>=0,"Missing actual class: "+name);
        int open=source.IndexOf('{',start),depth=0;
        for(int i=open;i<source.Length;i++){if(source[i]=='{')depth++;if(source[i]=='}'&&--depth==0)return source.Substring(start,i-start+1);}
        throw new Exception("Unterminated actual class: "+name);
    }
    static int Main(string[] args){try{
        Need(args.Length==3 || args.Length==4,"Pass owning repository, cached csc.exe, fresh output directory; optional failed snapshot folder");
        string repo=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Need(!Directory.Exists(output),"Fresh test output required");Directory.CreateDirectory(output);
        string source=Path.Combine(repo,"USB512_interface","USB512_interface"),form=File.ReadAllText(Path.Combine(source,"NpMediaUsbForm.cs"));
        Need(form.Contains("imu=new NpMediaUsbImu(LastFolder)")&&form.Contains("NpMediaUsbImu.Attach(parser,imu)")&&form.Contains("imu.Dispose()"),"Camera saving still discards sensors or does not flush the optional sink");
        string program=Path.Combine(output,"ActualMediaImu.cs"),exe=Path.Combine(output,"ActualMediaImu.exe");
        File.WriteAllText(program,"using System;using System.IO;using System.Linq;using System.Globalization;using System.Collections.Generic;namespace USB512_interface {\n"+
            Class(form,"internal sealed class NpMediaUsbImu")+"\n"+
            Class(File.ReadAllText(Path.Combine(source,"XeNpWinUsb.cs")),"internal sealed class XeNpPacketParser")+"\n"+
            Class(File.ReadAllText(Path.Combine(source,"NpCaptureModes.cs")),"internal sealed class NpCameraRecording")+"\n}\n"+Harness);
        var compile=new ProcessStartInfo(args[1],"/nologo /r:System.Core.dll /out:"+Quote(exe)+" "+Quote(program)+" "+Quote(Path.Combine(source,"NpUsbSensors.cs"))){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var p=Process.Start(compile)){string text=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();p.WaitForExit();Need(p.ExitCode==0,"Actual-source compile failed: "+text);}
        using(var p=Process.Start(new ProcessStartInfo(exe,Quote(output)+(args.Length==4?" "+Quote(Path.GetFullPath(args[3])):"")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})){string text=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();p.WaitForExit();Console.Write(text);Need(p.ExitCode==0,"Actual-source IMU checks failed");}
        return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}}

    const string Harness=@"
class MediaImuChecks {
 static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Put(byte[] b,int o,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,o,4);}
 static void Magic(byte[] b,int o,string text){for(int i=0;i<text.Length;i++)b[o+i]=(byte)text[i];}
 static byte[] Sensor(ushort wire,uint record,uint seq,uint tick,uint observed,bool corrupt=false,uint valid=3){
  var b=new byte[512];b[0]=0x12;b[1]=0xa5;b[2]=5;b[3]=8;Buffer.BlockCopy(BitConverter.GetBytes(wire),0,b,4,2);int o=6;
  Magic(b,o,""NPSENS01"");Put(b,o+8,record);Put(b,o+12,observed);Put(b,o+16,140);
  Put(b,o+20,0x31534e53);Put(b,o+24,1);Put(b,o+28,1);Put(b,o+32,valid);Put(b,o+36,observed);
  Put(b,o+40,seq);Put(b,o+44,seq);Put(b,o+52,tick);Put(b,o+56,tick);
  Put(b,o+68,seq==1?1000000U:2000000U);Put(b,o+80,12345);Put(b,o+92,25000);Put(b,o+128,7);
  Magic(b,o+164,""NPUSB001"");Put(b,o+172,1);Put(b,o+176,9);Put(b,o+184,record+2);
  Put(b,o+160,USB512_interface.NpUsbSensors.Crc(b,o,160));Put(b,o+502,USB512_interface.NpUsbSensors.Crc(b,o,502));
  if(corrupt)b[o+70]^=1;return b;
 }
 static byte[] Camera(byte[] frame){using(var stream=new MemoryStream()){
  ushort seq=0;for(int at=0;at<frame.Length;){int n=Math.Min(506,frame.Length-at);byte flags=(byte)((at==0?1:0)|(at+n==frame.Length?2:0));
   var packet=new byte[n+6];packet[0]=0x12;packet[1]=0xa5;packet[2]=3;packet[3]=flags;Buffer.BlockCopy(BitConverter.GetBytes(seq++),0,packet,4,2);Buffer.BlockCopy(frame,at,packet,6,n);stream.Write(packet,0,packet.Length);at+=n;
  }return stream.ToArray();}}
 static Dictionary<string,string> Status(string folder){return File.ReadAllLines(Path.Combine(folder,""imu_capture_status.txt"")).Where(s=>s.Contains(""="")).Select(s=>s.Split(new[]{'='},2)).ToDictionary(s=>s[0],s=>s[1]);}
 static void LateSource(string root){
  string folder=Path.Combine(root,""late_source"");Directory.CreateDirectory(folder);
  byte[][] packets={Sensor(0,1,1,1000,1000),Sensor(1,2,1,1000,1020,false,0),Sensor(2,3,2,1010,1030),Sensor(3,4,2,1010,1035),Sensor(4,5,3,1040,1060)};
  byte[] original=packets.SelectMany(b=>b).ToArray();
  using(var sink=new USB512_interface.NpMediaUsbImu(folder)){foreach(byte[] b in packets)sink.Feed(b,6,506);Check(sink.Error==null,""Queued valid source behind status watermark failed"");}
  var status=Status(folder);byte[] analog=File.ReadAllBytes(Path.Combine(folder,""analogin.dat""));
  Check(status[""error""]==""none""&&status[""records""]==""5""&&status[""distinct_imu_samples""]==""3"",""Late native source observations lost or mislabeled"");
  Check(analog.Length==75*32&&status[""derived_1250_rows""]==""75"",""Late source rewound or expanded derived grid"");
  Check(Enumerable.Range(0,25).All(i=>BitConverter.ToInt16(analog,i*32+2)==1000)&&Enumerable.Range(25,25).All(i=>BitConverter.ToInt16(analog,i*32+2)==short.MinValue)&&Enumerable.Range(50,25).All(i=>BitConverter.ToInt16(analog,i*32+2)==2000),""Late source re-enabled invalid held values before a fresh source caught up"");
  Check(Enumerable.Range(1,74).All(i=>unchecked((int)(BitConverter.ToUInt32(analog,i*32+28)-BitConverter.ToUInt32(analog,(i-1)*32+28)))>=0),""Derived grid PC-time rows rewound"");
  Check(File.ReadAllBytes(Path.Combine(folder,""imu_snapshots.bin"")).SequenceEqual(packets.SelectMany(b=>b.Skip(26).Take(140)))&&packets.SelectMany(b=>b).SequenceEqual(original),""Late source native snapshots/ticks changed"");
 }
 static void ClockGuard(string root,string name,uint first,uint next,bool fail){
  string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);bool rejected=false;
  using(var sensor=new USB512_interface.NpUsbSensors(folder)){
   byte[] a=Sensor(0,1,1,first,first),b=Sensor(1,2,2,next,next);sensor.Feed(a,6,506);
   try{sensor.Feed(b,6,506);}catch(IOException e){if(!e.Message.Contains(""source clock discontinuity""))throw;rejected=true;}
  }
  Check(rejected==fail,name+"": true source discontinuity policy changed"");
  if(!fail)Check(new FileInfo(Path.Combine(folder,""analogin.dat"")).Length==39*32,""Legitimate uint source-clock wrap changed"");
 }
 static void Replay(string root,string prefix){
  string rawPath=Path.Combine(prefix,""imu_snapshots.bin""),csvPath=Path.Combine(prefix,""imu_snapshots.csv"");
  byte[] raw=File.ReadAllBytes(rawPath);string csv=File.ReadAllText(csvPath);string[] lines=File.ReadAllLines(csvPath);
  Check(raw.Length>0&&raw.Length%140==0&&lines.Length==raw.Length/140+1,""Failed-prefix geometry mismatch"");
  string folder=Path.Combine(root,""failed_prefix_replay"");Directory.CreateDirectory(folder);uint? firstValid=null;uint lastObserved=0;
  using(var sink=new USB512_interface.NpMediaUsbImu(folder)){
   for(int i=0;i<raw.Length/140;i++){
    string[] row=lines[i+1].Split(',');uint record=uint.Parse(row[1]),observed=uint.Parse(row[2]),seq=uint.Parse(row[3]),tick=uint.Parse(row[4]);
    if(!firstValid.HasValue&&(uint.Parse(row[5])&1)!=0)firstValid=tick;lastObserved=observed;
    byte[] b=Sensor((ushort)i,record,seq,tick,observed);Buffer.BlockCopy(raw,i*140,b,26,140);Put(b,6+184,uint.Parse(row[6]));
    Put(b,6+160,USB512_interface.NpUsbSensors.Crc(b,6,160));Put(b,6+502,USB512_interface.NpUsbSensors.Crc(b,6,502));sink.Feed(b,6,506);
   }
   Check(sink.Error==null,""Exact failed snapshot prefix still stops the optional writer: ""+sink.Error);
  }
  var status=Status(folder);long rows=new FileInfo(Path.Combine(folder,""analogin.dat"")).Length/32;
  Check(firstValid.HasValue&&rows==(long)(((ulong)unchecked(lastObserved-firstValid.Value)*1000+799)/800)&&status[""records""]==(raw.Length/140).ToString()&&status[""rejected_packets""]==""0""&&status[""error""]==""none"",""Failed-prefix replay has unbounded output or missing observations"");
  Check(File.ReadAllBytes(Path.Combine(folder,""imu_snapshots.bin"")).SequenceEqual(raw)&&File.ReadAllLines(Path.Combine(folder,""imu_snapshots.csv"")).SequenceEqual(lines),""Replay altered native snapshot bytes/ticks/validity"");
  Check(File.ReadAllBytes(rawPath).SequenceEqual(raw)&&File.ReadAllText(csvPath)==csv,""Original failed snapshot files changed"");
  Console.WriteLine(""PASS offline failed-prefix replay: snapshots=""+raw.Length/140+""; held1250 rows=""+rows+""; originals unchanged"");
 }
 static void Scenario(string root,string name,int kind){
  string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
  if(kind==3)File.WriteAllBytes(Path.Combine(folder,""imu_snapshots.bin""),new byte[0]);
  var sink=new USB512_interface.NpMediaUsbImu(folder);var camera=new USB512_interface.NpCameraRecording(folder);var parser=new USB512_interface.XeNpPacketParser();
  USB512_interface.NpMediaUsbImu.Attach(parser,sink);parser.OnCamera=(b,o,n,f,s)=>camera.Write(b,o,n,f,s);
  byte[] frame=Enumerable.Range(0,160000).Select(i=>(byte)(i*7)).ToArray(),cam=Camera(frame);
  byte[] first=Sensor(0,1,1,1000,1000),bad=Sensor(1,2,2,1025,1025,true),last=Sensor(2,3,11,1050,1075);
  byte[] bytes=kind==1?cam:kind==2?Sensor(0,1,1,1000,1000,true).Concat(cam).ToArray():kind==4?Sensor(0,1,1,1000,1000,false,0).Concat(cam).ToArray():first.Concat(cam.Take(4096)).Concat(bad).Concat(cam.Skip(4096)).Concat(last).ToArray();
  byte[] original=(byte[])bytes.Clone();int at=0;
  while(at<bytes.Length){int n=Math.Min(1373,bytes.Length-at);var part=new byte[n];Buffer.BlockCopy(bytes,at,part,0,n);parser.Feed(part,n,(Action<byte[],int,int>)delegate{throw new Exception(""Unexpected neural routing"");});at+=n;}
  parser.Complete();camera.Dispose();long before=new FileInfo(Path.Combine(folder,""analogin.dat"")).Length;sink.Dispose();sink.Dispose();
  Check(camera.Complete&&camera.Frames==1&&File.ReadAllBytes(Path.Combine(folder,""misc.dat"")).SequenceEqual(frame),name+"": camera bytes/index damaged by optional sensors"");
  Check(bytes.SequenceEqual(original),name+"": native input changed"");
  var status=Status(folder);
  if(kind==0){
   Check(status[""records""]==""2""&&status[""distinct_imu_samples""]==""2""&&status[""availability""]==""available""&&status[""error""]==""none"",""Mixed availability/native counts"");
   Check(status[""received_packets""]==""3""&&status[""rejected_packets""]==""1""&&status[""record_sequence_gaps""]==""1""&&status[""imu_sequence_gaps""]==""9"",""Optional rejection/sequence losses hidden"");
   Check(File.ReadAllBytes(Path.Combine(folder,""imu_snapshots.bin"")).SequenceEqual(first.Skip(26).Take(140).Concat(last.Skip(26).Take(140))),""Native140 snapshot bytes/ticks changed or bad CRC saved"");
   string[] csv=File.ReadAllLines(Path.Combine(folder,""imu_snapshots.csv""));Check(csv.Length==3&&csv[1].StartsWith(""0,1,1000,1,1000,3,3"")&&csv[2].StartsWith(""1,3,1075,11,1050,3,5""),""Native source/observation ticks or queue loss changed"");
   byte[] analog=File.ReadAllBytes(Path.Combine(folder,""analogin.dat""));Check(analog.Length==94*32&&analog.Length>before&&status[""derived_1250_rows""]==""94"",""Held1250 grid or dispose flush incorrect"");
   Check(BitConverter.ToInt16(analog,2)==1000&&BitConverter.ToInt16(analog,63*32+2)==2000&&BitConverter.ToInt16(analog,14)==25,""Canonical analog scaling/hold changed"");
  }else if(kind==1)Check(status[""availability""]==""not_received""&&status[""records""]==""0""&&status[""error""]==""none"",""Absent sensors mislabeled as camera failure"");
  else if(kind==2)Check(status[""availability""]==""invalid""&&status[""rejected_packets""]==""1""&&status[""records""]==""0""&&status[""error""]==""none"",""Bad optional CRC stopped camera or was not reported"");
  else if(kind==3)Check(status[""availability""]==""file_error""&&status[""error""]!=""none"",""Sidecar file error hidden"");
  else Check(status[""availability""]==""invalid""&&status[""records""]==""1""&&status[""distinct_imu_samples""]==""0""&&status[""derived_1250_rows""]==""0"",""Not-ready IMU fabricated data"");
  using(var reopen=new FileStream(Path.Combine(folder,""analogin.dat""),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(reopen.Length%32==0,name+"": sidecar handle leaked"");
 }
 static int Main(string[] args){try{Scenario(args[0],""mixed"",0);Scenario(args[0],""absent"",1);Scenario(args[0],""bad_crc"",2);Scenario(args[0],""file_error"",3);Scenario(args[0],""not_ready"",4);LateSource(args[0]);ClockGuard(args[0],""source_reset"",1000,900,true);ClockGuard(args[0],""source_large_jump"",1000,61001,true);ClockGuard(args[0],""source_uint_wrap"",uint.MaxValue-20,10,false);if(args.Length==2)Replay(args[0],args[1]);Console.WriteLine(""PASS ""+checks+"" actual-source USB camera/IMU routing, exact native/ticks, causal held grid, optional errors and flush checks; no hardware"");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}";
}
