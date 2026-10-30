using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using USB512_interface;

internal static class NpUsbOptionalIsolationTests {
 static int checks;
 static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
 static void Text(byte[] b,int at,string s){System.Text.Encoding.ASCII.GetBytes(s).CopyTo(b,at);}
 static byte[] Snapshot(uint seq,uint tick){var b=new byte[140];Put(b,0,0x31534e53);Put(b,4,1);Put(b,12,3);Put(b,16,tick);Put(b,20,seq);Put(b,24,seq);Put(b,32,tick);Put(b,36,tick);Put(b,44,seq*128);Put(b,48,seq*1000000);Put(b,60,12500);Put(b,72,42000);return b;}
 static byte[] Sensor(ushort wire,uint seq,uint tick){
  var b=new byte[512];b[0]=0x12;b[1]=0xa5;b[2]=5;b[3]=8;Buffer.BlockCopy(BitConverter.GetBytes(wire),0,b,4,2);int o=6;
  Text(b,o,"NPSENS01");Put(b,o+8,seq);Put(b,o+12,tick);Put(b,o+16,140);Snapshot(seq,tick).CopyTo(b,o+20);
  Put(b,o+160,NpUsbSensors.Crc(b,o,160));Text(b,o+164,"NPUSB001");Put(b,o+172,1);Put(b,o+176,17);Put(b,o+502,NpUsbSensors.Crc(b,o,502));return b;
 }
 static void Seal(byte[] b){Put(b,6+160,NpUsbSensors.Crc(b,6,160));Put(b,6+502,NpUsbSensors.Crc(b,6,502));}
 static byte[] Bad(byte[] good,string fault){var b=(byte[])good.Clone();
  if(fault=="inner_crc")b[6+160]^=1;
  else if(fault=="outer_crc")b[6+502]^=1;
  else if(fault=="tag"){b[6]^=1;Seal(b);}
  else if(fault=="version"){Put(b,6+24,99);Seal(b);}
  else if(fault=="fine_carrier_crc")b[6+192]^=1;
  else if(fault=="session"){Put(b,6+176,18);Seal(b);}
  else if(fault=="record_sequence"){Put(b,6+8,0);Seal(b);}
  return b;
 }
 static void Word(byte[] b,int at,int line,int word,uint value){for(int i=0;i<10;i++)if((value&(1U<<(9-i)))!=0)b[at+word*10+i]|=(byte)line;}
 static byte[] Neural(){var b=new byte[36*780];for(int f=0;f<b.Length/60;f++){
  int at=f*60;for(int t=0;t<50;t++)b[at+t]=(byte)(((f*11+t*3)&63)<<1);
  Word(b,at,1,0,f%13==0?0x330U:0xcfU);Word(b,at,128,2,0x11);Word(b,at,128,3,(uint)f>>10);Word(b,at,128,4,(uint)f&1023);
 }return b;}
 static byte[] Packet(byte[] b,int at,int count){var p=new byte[count+6];p[0]=0x12;p[1]=0xa5;p[2]=6;p[3]=4;Buffer.BlockCopy(BitConverter.GetBytes((ushort)count),0,p,4,2);Buffer.BlockCopy(b,at,p,6,count);return p;}
 static Dictionary<string,string> Status(string folder){return File.ReadAllLines(Path.Combine(folder,NpUsbSensors.OptionalStatusFile)).Where(l=>l.Contains("=")).ToDictionary(l=>l.Substring(0,l.IndexOf('=')),l=>l.Substring(l.IndexOf('=')+1));}
 sealed class Decoded {internal byte[] Bytes;internal long Ap,Lfp,Frames;}
 static Decoded V1(string folder,string fault){
  Directory.CreateDirectory(folder);byte[] neural=Neural(),first=Sensor(0,1,1000),middle=Sensor(1,2,1010),last=Sensor(2,3,1020);if(fault!="none")middle=Bad(middle,fault);
  byte[] original=(byte[])middle.Clone();var parser=new XeNpPacketParser();var decoder=new XeNpStreamDecoder(XeNpLaneLayout.Gpio);decoder.DeclareRecordingReset();
  var result=new Decoded();using(var output=new MemoryStream())using(var writer=new BinaryWriter(output))using(var sensors=new NpUsbSensors(folder,true,"fixture-UID")){
   parser.OnMisc=(b,o,n)=>sensors.FeedOptional(b,o,n);
   Action<byte[],int,int> receive=(b,o,n)=>decoder.Feed(b,o,n,(ap,na,lfp,nl)=>{result.Ap+=na;result.Lfp+=nl;if(na>0)foreach(short v in ap)writer.Write(v);if(nl>0)foreach(short v in lfp)writer.Write(v);});
   parser.Feed(first,first.Length,receive);
   for(int at=0;at<neural.Length;at+=506){byte[] p=Packet(neural,at,Math.Min(506,neural.Length-at));parser.Feed(p,p.Length,receive);if(at==506*9){parser.Feed(middle,middle.Length,receive);
     if(fault!="none")Need(sensors.GetDisplay().Error!=null&&sensors.Valid==0,"Bad optional V1 record did not visibly invalidate held sensor state");}}
   parser.Feed(last,last.Length,receive);parser.Complete();decoder.Complete();writer.Flush();result.Bytes=output.ToArray();result.Frames=decoder.FramesChecked;
   Need(decoder.FrameErrors==0&&decoder.CounterErrors==0&&parser.SequenceGaps==0&&result.Ap==36&&result.Lfp==3,"Optional V1 record changed strict NN sample/frame counts");
   Need(sensors.ImuSamples==(fault=="none"?3:2)&&sensors.OptionalRejectedRecords==(fault=="none"?0:1),"Invalid optional V1 record counted as native sample");
   Need(sensors.Valid==3&&sensors.GetDisplay().Error==null,"A valid V1 sensor record did not recover after rejection");
   if(fault!="none")Need(sensors.RecordSequenceGaps==1&&File.ReadAllBytes(Path.Combine(folder,NpUsbSensors.RejectedFileName)).SequenceEqual(middle.Skip(6)),"Rejected V1 raw record or exact sequence loss missing");
  }
  Need(middle.SequenceEqual(original),"V1 rejected source bytes modified");var status=Status(folder);Need(status["rejected_optional_records"]==(fault=="none"?"0":"1")&&status["consumer_disabled"]=="False","V1 optional quality state missing");
  if(fault!="none"){
   Need(status["last_error"]!="none"&&status["first_rejected_raw_bytes"]=="506","V1 error/raw provenance missing");
   byte[] grid=File.ReadAllBytes(Path.Combine(folder,"analogin.dat"));Need(grid.Length==25*32&&Enumerable.Range(0,25).All(i=>BitConverter.ToInt16(grid,i*32+2)==short.MinValue),"Bad optional timestamp extended old held sensor values");
  }
  return result;
 }
 static void StrictV1(string root){
  using(var sensors=new NpUsbSensors(null,false)){bool rejected=false;try{sensors.Feed(Bad(Sensor(0,1,1000),"outer_crc"),6,506);}catch(IOException){rejected=true;}Need(rejected,"Strict media/diagnostic Feed stopped rejecting bad optional CRC");}
  foreach(string fault in new[]{"NN_frame","transport_magic","cancellation"}){
   string folder=Path.Combine(root,fault);Directory.CreateDirectory(folder);using(var sensors=new NpUsbSensors(folder)){
    var parser=new XeNpPacketParser();var decoder=new XeNpStreamDecoder(XeNpLaneLayout.Gpio);decoder.DeclareRecordingReset();
    parser.OnMisc=(b,o,n)=>{if(fault=="cancellation")throw new OperationCanceledException();sensors.FeedOptional(b,o,n);};bool rejected=false;
    try{if(fault=="cancellation"){byte[] p=Sensor(0,1,1000);parser.Feed(p,p.Length,(Action<byte[],int,int>)((b,o,n)=>{}));}
     else {byte[] b=Neural();if(fault=="NN_frame")b[12*780]^=1;for(int at=0;at<b.Length;at+=506){byte[] p=Packet(b,at,Math.Min(506,b.Length-at));if(fault=="transport_magic"&&at==506*9)p[0]^=1;parser.Feed(p,p.Length,(x,o,n)=>decoder.Feed(x,o,n,(a,na,l,nl)=>{}));}}
    }catch(Exception ex) when(ex is IOException || ex is OperationCanceledException){Need((ex is OperationCanceledException)==(fault=="cancellation"),"Cancellation changed exception type");rejected=true;}
    Need(rejected,"Optional consumer hid NN/transport/cancellation failure: "+fault);
   }
  }
  string bounded=Path.Combine(root,"bounded_bad");Directory.CreateDirectory(bounded);using(var sensors=new NpUsbSensors(bounded)){
   byte[] first=Bad(Sensor(0,1,1000),"outer_crc"),second=Bad(Sensor(1,2,1010),"version");sensors.FeedOptional(first,6,506);sensors.FeedOptional(second,6,506);
   Need(sensors.OptionalRejectedRecords==2&&File.ReadAllBytes(Path.Combine(bounded,NpUsbSensors.RejectedFileName)).SequenceEqual(first.Skip(6)),"Repeated errors rewrote/unbounded the first raw rejection");
  }
  string fail=Path.Combine(root,"archive_failure");Directory.CreateDirectory(Path.Combine(fail,NpUsbSensors.RejectedFileName));using(var sensors=new NpUsbSensors(fail)){sensors.FeedOptional(Bad(Sensor(0,1,1000),"version"),6,506);Need(sensors.OptionalArchiveError!=null&&sensors.OptionalRejectedRecords==1,"Optional raw archive IO error escaped or hidden");sensors.FeedOptional(Sensor(1,2,1020),6,506);Need(sensors.ImuSamples==1,"Archive IO error prevented later valid sensor recovery");}
 }
 static Decoded V2(string folder,bool bad,bool save=true,bool badVersion=false){
  Directory.CreateDirectory(folder);var baseline=new NpV2WireVerifier();var decoder=new NpV2SampleDecoder((m,h)=>{},(p,row)=>{});var result=new Decoded();
  using(var output=new MemoryStream())using(var writer=new BinaryWriter(output))using(var monitor=new NpUsbSensorMonitor(null,folder,"fixture-UID",save,null)){
   decoder=new NpV2SampleDecoder((m,h)=>{},(p,row)=>{result.Ap++;writer.Write((short)p);foreach(short v in row)writer.Write(v);});byte[] rejected=null;
   for(int n=0;n<3;n++){
    byte[] block=NpOptionalV2Fixture.Create((ulong)n,(ulong)n*61440),sensor=Snapshot((uint)n+1,(uint)(1000+n*10));Put(block,52,BitConverter.ToUInt32(block,52)|128);sensor.CopyTo(block,256);
    if(bad&&n==1){Put(block,badVersion?260:256,badVersion?99U:0xdeadbeefU);rejected=block.Skip(256).Take(140).ToArray();}
    byte[] original=(byte[])block.Clone();baseline.Feed(block,0);monitor.FeedBlock(block,0);decoder.Feed(block,0,baseline);
    Need(block.SequenceEqual(original),"Optional V2 monitor modified NN/header bytes");
    if(bad&&n==1)Need(monitor.Error!=null&&monitor.LastDisplay.Valid==0&&monitor.LastDisplay.Error!=null,"Malformed V2 snapshot was not visible");
   }
   decoder.Complete();writer.Flush();result.Bytes=output.ToArray();result.Frames=(long)baseline.SyncPositionsChecked;
   Need(baseline.Blocks==3&&result.Ap>0&&monitor.Samples==(bad?2:3)&&monitor.Records==(bad?2:3),"V2 IMU rejection damaged NN counts or disabled later valid samples");
   // Display is throttled, so query the real consumer's latest state for recovery.
   var sensorField=typeof(NpUsbSensorMonitor).GetField("sensors",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var live=(NpUsbSensors)sensorField.GetValue(monitor);
   Need(live.Valid==3&&live.GetDisplay().Error==null,"V2 valid snapshot failed to recover after one malformed optional record");
   if(bad&&save)Need(File.ReadAllBytes(Path.Combine(folder,NpUsbSensors.RejectedFileName)).SequenceEqual(rejected),"First V2 bad SNS1 raw bytes not preserved");
  }
  if(bad&&save){var status=Status(folder);Need(status["rejected_optional_records"]=="1"&&status["consumer_disabled"]=="False"&&status["first_rejected_raw_bytes"]=="140","V2 optional quality/provenance missing");Need(new FileInfo(Path.Combine(folder,"imu_snapshots.bin")).Length==280,"Rejected V2 snapshot entered valid raw snapshot stream");}
  return result;
 }
 static void StrictV2(){
  foreach(string fault in new[]{"header","supersync","payload_crc","sensor_D8_overlap","sensor_requires_no_header_crc","future_reserved"}){
   byte[] b=NpOptionalV2Fixture.Create(0,0);if(fault=="header")b[16]^=1;if(fault=="supersync")Array.Clear(b,512,b.Length-512);
   if(fault=="payload_crc")Put(b,56,1);if(fault=="sensor_D8_overlap")Put(b,52,BitConverter.ToUInt32(b,52)|192);if(fault=="sensor_requires_no_header_crc")Put(b,52,(BitConverter.ToUInt32(b,52)|128)&~32U);if(fault=="future_reserved")b[396]=1;
   bool rejected=false;try{new NpV2WireVerifier().Feed(b,0);}catch(InvalidDataException){rejected=true;}Need(rejected,"Optional monitor weakened V2 NN/header contract: "+fault);
  }
 }
 static void AnalogOwnership(string root){
  byte[][] snapshots={Snapshot(1,1000),Snapshot(2,1010),Snapshot(3,1020)};
  string baseline=Path.Combine(root,"legacy_analog_baseline"),legacy=Path.Combine(root,"legacy_analog_monitor"),grid=Path.Combine(root,"grid_analog_monitor"),unknown=Path.Combine(root,"unresolved_analog_monitor");
  foreach(string p in new[]{baseline,legacy,grid,unknown})Directory.CreateDirectory(p);
  using(var saved=new NpUsbSensors(baseline,true,"fixture-UID"))foreach(byte[] b in snapshots)saved.FeedSnapshot(b,0,BitConverter.ToUInt32(b,16));
  using(var monitor=new NpUsbSensorMonitor(null,legacy,"fixture-UID",true,null,true)){
   Need(!File.Exists(Path.Combine(legacy,"analogin.dat"))&&File.Exists(Path.Combine(legacy,"imu_snapshots.bin")),"Deferred monitor did not keep native output independently of analog owner");
   monitor.ResolveAnalogOwner(false);foreach(byte[] b in snapshots)monitor.FeedSnapshot(b,0);
   bool late=false;try{monitor.ResolveAnalogOwner(true);}catch(InvalidOperationException){late=true;}Need(late,"Analog owner silently changed after native data");
  }
  byte[] actual=File.ReadAllBytes(Path.Combine(legacy,"analogin.dat")),expected=File.ReadAllBytes(Path.Combine(baseline,"analogin.dat"));
  // The legacy serializer preserves its separately sampled PC-origin word.
  // Compare values/counts and relative clock increments, not two wall-clock epochs.
  foreach(byte[] b in new[]{actual,expected}){uint first=BitConverter.ToUInt32(b,28);for(int at=28;at<b.Length;at+=32)Put(b,at,unchecked(BitConverter.ToUInt32(b,at)-first));}
  Need(actual.SequenceEqual(expected),"Deferred legacy owner changed actual1250Hz values/gain/sequence/grid or relative clock bytes");
  using(var monitor=new NpUsbSensorMonitor(null,grid,"001A0004434250062039314E",true,null,true)){
   monitor.ResolveAnalogOwner(true);bool changed=false;try{monitor.ResolveAnalogOwner(false);}catch(InvalidOperationException){changed=true;}Need(changed,"Grid owner could be replaced before arm");
   foreach(byte[] b in snapshots)monitor.FeedSnapshot(b,0);Need(monitor.Records==3&&monitor.Samples==3&&!File.Exists(Path.Combine(grid,"analogin.dat")),"Grid selection suppressed native IMU or opened competing root analog file");
   int fineQueries=0;monitor.ConfigureFine(()=>{fineQueries++;return null;});
   Need(typeof(NpUsbSensorMonitor).GetField("fine",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(monitor)!=null,"Fine-rebuild competition fixture did not construct its real observer");
   monitor.CompleteFine(100000,true);Need(fineQueries==0&&!File.Exists(Path.Combine(grid,"analogin.dat")),"Stopped fine rebuild competed with the selected root grid writer");
  }
  using(var writer=new NpImuGridWriter(grid,1,true)){writer.Feed(NpImuGridWire.Pack(new NpImuGridBatch{CaptureGeneration=1,SlotCount=1,ObservedMask=1,AgValid=1,MagValid=1,Rows=new[]{new int[9]}}));writer.Complete(0,32000000,0,false);}
  Need(new FileInfo(Path.Combine(grid,"analogin.dat")).Length==32&&new FileInfo(Path.Combine(grid,"imu_snapshots.bin")).Length==420,"One root scheduled writer did not coexist with retained native snapshots");
  using(var monitor=new NpUsbSensorMonitor(null,unknown,"fixture-UID",true,null,true)){
   foreach(byte[] b in snapshots)monitor.FeedSnapshot(b,0);Need(monitor.Records==3&&monitor.Error!=null&&!File.Exists(Path.Combine(unknown,"analogin.dat")),"Unresolved ownership invented a late legacy fallback or stopped native data");
  }
 }
 static int Main(string[] args){try{
  string root=Path.Combine(args[0],Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var v1=V1(Path.Combine(root,"v1_baseline"),"none");
  foreach(string fault in new[]{"inner_crc","outer_crc","tag","version","fine_carrier_crc","session","record_sequence"}){var changed=V1(Path.Combine(root,"v1_"+fault),fault);Need(changed.Bytes.SequenceEqual(v1.Bytes)&&changed.Ap==v1.Ap&&changed.Lfp==v1.Lfp&&changed.Frames==v1.Frames,"Adjacent valid V1 NN samples changed: "+fault);}
  StrictV1(root);Decoded v2=V2(Path.Combine(root,"v2_baseline"),false),bad=V2(Path.Combine(root,"v2_bad"),true),display=V2(Path.Combine(root,"v2_display"),true,false),version=V2(Path.Combine(root,"v2_version"),true,true,true);
  Need(v2.Bytes.SequenceEqual(bad.Bytes)&&v2.Ap==bad.Ap&&v2.Frames==bad.Frames&&display.Bytes.SequenceEqual(v2.Bytes)&&version.Bytes.SequenceEqual(v2.Bytes)&&version.Ap==v2.Ap,"Actual V2 AP values/counts changed by malformed optional SNS1");StrictV2();AnalogOwnership(root);
  Console.WriteLine("PASS "+checks+" actual V1/V2 parser/decoder NN byte/sample-count parity, recoverable IMU rejection, first-bad raw/status, strict validator/transport/header/NN/cancellation checks; no hardware.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
