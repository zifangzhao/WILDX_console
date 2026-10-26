using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;
using USB512_interface;

// Only the radio is replaced. Wire framing, request admission, protocol,
// sample conversion, calibration and the form are production source.
namespace USB512_interface {
 internal sealed partial class NpBleConnection {
  internal int Epoch=1;internal bool Connected=true,PreviewLowerPriorityBusy;
  internal readonly SemaphoreSlim transaction=new SemaphoreSlim(1,1);
  internal readonly List<byte[]> Requests=new List<byte[]>();
  internal Action<byte[]> OnRequest;
  internal event Action<int,byte[]> PreviewReceived;
  internal void Emit(byte[] body,int epoch=1){if(PreviewReceived!=null)PreviewReceived(epoch,body);}
  Task WriteFrame(byte[] frame,int epoch,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();if(epoch!=Epoch||!Connected)throw new IOException("Connection changed");
   var body=new byte[64];Buffer.BlockCopy(frame,2,body,0,64);Requests.Add(body);if(OnRequest!=null)OnRequest(body);return Task.FromResult(true);
  }
 }
}
static class NpV1CompactPreviewTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static int checks;
 static void Need(bool b,string why){checks++;if(!b)throw new Exception(why);}
 static void Reject(Action action,string why){try{action();}catch(IOException){checks++;return;}catch(ArgumentException){checks++;return;}throw new Exception(why);}
 static object Get(object target,string name){return target.GetType().GetField(name,All).GetValue(target);}
 static void Set(object target,string name,object value){target.GetType().GetField(name,All).SetValue(target,value);}
 static object Call(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,All).Invoke(target,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static void Put(byte[] b,int at,uint n){BitConverter.GetBytes(n).CopyTo(b,at);}
 static void U16(byte[] b,int at,uint n){BitConverter.GetBytes((ushort)n).CopyTo(b,at);}
 static void Seal(byte[] b){Put(b,220,NpSdControl.Crc(b,220));}
 static uint Offset(uint ch,uint band,uint phase){uint adc=ch/24*2+(ch&1),word=adc<30?adc%5:adc-30;return phase+(band==0?(ch%24/2+1)*60:ch%24/2*780)+word*10;}
 static byte[] Packet(uint hz=20000,uint band=0,uint phase=37,uint[] channels=null,ulong cycles=0){
  if(channels==null)channels=new uint[]{0,127,360,383};
  uint period=band==0?780U:9360U,origin=Offset(channels[0],band,phase)%period;
  ulong first=cycles*period+origin;var b=new byte[224];
  uint[] words={NpV1PreviewBurst.Magic,91,7,hz,(uint)first,(uint)(first>>32),(128U<<16)|16,1U|(5U<<8)|(band<<12)|(channels[0]<<16)};
  for(int i=0;i<words.Length;i++)Put(b,4*i,words[i]);
  for(int c=0;c<channels.Length;c++){
   U16(b,160+c*2,channels[c]);U16(b,168+c*2,c==0?0x7fffU:0xffffU);
   Put(b,176+c*4,(Offset(channels[c],band,phase)%period+period-origin)%period);
   for(int n=0;n<16;n++)U16(b,32+c*32+n*2,(uint)((c*271+n*67)&1023));
  }
  Put(b,192,band==0?((hz+499)/500)*780:((hz+5999)/6000)*9360);Put(b,200,(uint)channels.Length);Put(b,204,1);Seal(b);return b;
 }
 static byte[] Legacy(uint hz,uint band,uint phase,uint channel){
  var b=new byte[224];uint first=Offset(channel,band,phase),adc=channel/24*2+(channel&1),line=adc<30?adc/5+1:7;
  uint[] words={NpV1PreviewBurst.Magic,91,7,hz,first,0,(160U<<16)|32767,1U|(2U<<8)|(band<<12)|(channel<<16)};
  for(int i=0;i<words.Length;i++)Put(b,4*i,words[i]);
  for(int n=0;n<16;n++)for(int bit=0;bit<10;bit++)if(((n*67)&(1<<(9-bit)))!=0)b[32+n*10+bit]|=(byte)(1<<((int)line));
  Put(b,192,band==0?((hz+499)/500)*780:((hz+5999)/6000)*9360);return b;
 }
 static byte[] Frame(byte cmd,byte[] body,bool ascii=false){var b=new byte[body.Length+(ascii?3:4)];b[0]=ascii?(byte)0x3c:(byte)0xad;b[1]=cmd;body.CopyTo(b,2);b[b.Length-1]=ascii?(byte)0x3e:(byte)0xda;if(!ascii)b[b.Length-2]=0xda;return b;}
 static byte[] Slice(byte[] data,int at,int n){var b=new byte[n];Buffer.BlockCopy(data,at,b,0,n);return b;}
 static byte[] Join(byte[] a,byte[] b){var c=new byte[a.Length+b.Length];a.CopyTo(c,0);b.CopyTo(c,a.Length);return c;}
 static void Protocol(){
  foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(uint band in new uint[]{0,1})foreach(uint phase in new uint[]{0,37,779})foreach(uint firstChannel in new uint[]{0,127,360,383}){
   var channels=new uint[]{firstChannel,(firstChannel+1)%384,(firstChannel+128)%384,(firstChannel+255)%384};
   var b=Packet(hz,band,phase,channels,0x1000000);var burst=new NpV1PreviewBurst(91);Need(burst.Accept(b),"Compact rejected");var values=burst.DecodeCompact(phase);
   Need(values.Length==4,"Channel count lost");
   for(int c=0;c<4;c++)for(int n=0;n<16;n++)Need(c==0&&n==15?Double.IsNaN(values[c][n]):values[c][n]==((c*271+n*67)&1023),"ADC or validity changed");
   Reject(()=>burst.DecodeCompact(phase+1),"Wrong phase admitted");
   var legacy=new NpV1PreviewBurst(91);Need(legacy.Accept(Legacy(hz,band,phase,firstChannel)),"Legacy rejected");var raw=legacy.DecodeRolling(phase);
   for(int n=0;n<16;n++)Need(n==15?Double.IsNaN(raw[n]):raw[n]==n*67,"Legacy decoding changed");
  }
  for(int count=1;count<=4;count++){
   var channels=new uint[count];for(int i=0;i<count;i++)channels[i]=(uint)i;
   foreach(uint kind in new uint[]{5,3,4}){
    var r=NpV1PreviewBurst.CompactRequest(91,kind,1,channels,37,7,0);
    Need(NpSdGeometry.U(r,8)==kind&&NpSdGeometry.U(r,36)==count&&NpSdGeometry.U(r,60)==NpSdControl.Crc(r,60),"Compact lease request changed");
    for(int i=count;i<4;i++)Need(NpSdGeometry.U(r,36+i*4)==0,"Unused channel request not zero");
   }
   var burst=new NpV1PreviewBurst(91);Need(burst.Accept(Packet(channels:channels))&&burst.Channels.Length==count,"Count not supported");
  }
  Reject(()=>NpV1PreviewBurst.CompactRequest(91,5,1,new uint[]{1,1},0,7,0),"Duplicate requested channels");
  Reject(()=>NpV1PreviewBurst.CompactRequest(91,5,1,new uint[]{384},0,7,0),"Out of range channel");
  Reject(()=>NpV1PreviewBurst.CompactRequest(91,2,1,new uint[]{1},0,7,0),"Compact legacy kind");
  foreach(var mutate in new Action<byte[]>[]{b=>b[40]^=1,b=>Put(b,200,0),b=>Put(b,200,5),b=>Put(b,204,2),b=>Put(b,208,1),b=>Put(b,212,1),b=>Put(b,216,1),b=>Put(b,24,(128U<<16)|15),b=>Put(b,192,1),b=>U16(b,160,1),b=>U16(b,162,0),b=>U16(b,34,1024),b=>Put(b,176,1),b=>Put(b,180,780),b=>{for(int i=168;i<176;i++)b[i]=0;}}){
   var b=Packet();mutate(b);if(b[40]==Packet()[40])Seal(b);Reject(()=>new NpV1PreviewBurst(91).Accept(b),"Invalid compact payload accepted");
  }
  var wrong=Packet();Put(wrong,180,1);Seal(wrong);var accepted=new NpV1PreviewBurst(91);Need(accepted.Accept(wrong),"Framing rejected phase-bound check");Reject(()=>accepted.DecodeCompact(37),"Wrong per-channel offset admitted");
  var overflow=Packet();Put(overflow,16,uint.MaxValue);Put(overflow,20,uint.MaxValue);Seal(overflow);Reject(()=>new NpV1PreviewBurst(91).Accept(overflow),"Overflowing channel time admitted");
  foreach(int at in new[]{64,162,170,180}){var b=Packet(channels:new uint[]{0});b[at]=1;Seal(b);Reject(()=>new NpV1PreviewBurst(91).Accept(b),"Nonzero unused channel accepted");}
  Need(!new NpV1PreviewBurst(92).Accept(Packet()),"Stale request admitted");
 }
 static void Framing(){
  var control=Frame(0xb9,NpSdControl.Request(0,92));
  foreach(bool ascii in new[]{false,true}){
   var frame=Frame(0xba,Packet(),ascii);
   for(int split=1;split<frame.Length;split++){
    var wire=new NpBleWire();Need(wire.Feed(Slice(frame,0,split)).Count==0,"Partial packet emitted");
    var rows=wire.Feed(Join(Slice(frame,split,frame.Length-split),control));Need(rows.Count==2&&rows[0].Item1==0xba&&rows[1].Item1==0xb9,"Fragmented packet/control lost");
   }
   for(int at=2;at<226;at++)for(int bit=0;bit<8;bit++){var bad=(byte[])frame.Clone();bad[at]^=(byte)(1<<bit);var rows=new NpBleWire().Feed(Join(bad,control));Need(rows.Count==1&&rows[0].Item1==0xb9,"Corrupt preview accepted or blocked control");}
   for(int cut=35;cut<frame.Length;cut++){var rows=new NpBleWire().Feed(Join(Slice(frame,0,cut),control));Need(rows.Count==1&&rows[0].Item1==0xb9,"Truncated preview blocked control");}
  }
 }
 static void FirmwareVectors(string path){
  var data=File.ReadAllBytes(path);Need(data.Length==448,"Firmware fixture length changed");
  for(int band=0;band<2;band++){
   var body=Slice(data,band*224,224);var burst=new NpV1PreviewBurst(NpSdGeometry.U(body,4));
   Need(burst.Accept(body)&&burst.Band==band&&burst.Hz==7500&&burst.Probe==1,"Actual firmware packet rejected");
   var samples=burst.DecodeCompact(17);var channels=new uint[]{0,24,25,383};
   for(int c=0;c<4;c++)for(int row=0;row<16;row++){
    uint ch=channels[c],adc=ch/24*2+(ch&1),slot=ch%24/2;
    ulong tick=burst.FirstTick+burst.TickOffsets[c]+(ulong)row*burst.Stride,shifted=tick+4096UL*9360-17;
    Need(burst.Channels[c]==ch&&samples[c][row]==((adc*12+slot+(shifted/780%23)*17)&1023),"Actual firmware sample/time decode mismatch");
   }
  }
 }
 static NpV1BlePreviewForm Arm(NpBleConnection radio,bool compact,Func<bool> busy){
  var status=new NpSdStatus{State=1,Flags=128|(compact?NpV1BleProtocol.CompactPreview:0),Mask=3,Hz=20000};
  var form=new NpV1BlePreviewForm(radio,()=>status,busy,null);
  if(compact){((NumericUpDown)Get(form,"traceCount")).Value=4;var boxes=(NumericUpDown[])Get(form,"extraChannels");boxes[0].Value=127;boxes[1].Value=360;boxes[2].Value=383;}
  Set(form,"activeSelection",(int)Get(form,"selectionVersion"));
  Set(form,"activeId",91U);Set(form,"activeProbe",1U);Set(form,"activeChannel",0U);Set(form,"activeBand",1U);Set(form,"phase",37U);Set(form,"session",7U);Set(form,"hz",20000U);
  Set(form,"activeCompact",compact);var channels=compact?new uint[]{0,127,360,383}:new uint[]{0};Set(form,"activeChannels",channels);Call(form,"SetPlotChannels",(object)channels);Call(form,"ClearPlots",20000.0*780);
  return form;
 }
 static void Finish(Task task,int timeout=12000){var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&watch.ElapsedMilliseconds<timeout){Application.DoEvents();Thread.Sleep(1);}Need(task.IsCompleted,"Preview task did not finish");task.GetAwaiter().GetResult();}
 static void Word(byte[] data,int at,int line,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)data[at+word*10+bit]|=(byte)(1<<line);}
 static List<byte[]> OriginPackets(byte[] request){
  var packets=new List<byte[]>();
  var raw=new byte[1560];for(int f=0;f<13;f++){int at=37+f*60;Word(raw,at,0,0,f==0?0x330U:0xcfU);Word(raw,at,7,2,0x11);Word(raw,at,7,3,(uint)(900+f)>>10);Word(raw,at,7,4,(uint)(900+f)&1023);}
  for(int at=0;at<raw.Length;at+=160){var body=new byte[224];uint[] w={NpV1PreviewBurst.Magic,NpSdGeometry.U(request,12),7,20000,0,0,(1560U<<16)|(uint)at,1U|(1U<<12)};for(int i=0;i<w.Length;i++)Put(body,i*4,w[i]);Array.Copy(raw,at,body,32,Math.Min(160,raw.Length-at));Put(body,192,37440);packets.Add(body);}
  return packets;
 }
 static void Origin(NpBleConnection radio,byte[] request){foreach(var body in OriginPackets(request))radio.Emit(body);}
 static void OriginLeaseAndCache(){
  var state=new NpSdStatus{State=1,Mask=3,Hz=20000,Flags=128|4|NpV1BleProtocol.CompactPreview};var radio=new NpBleConnection();
  using(var form=new NpV1BlePreviewForm(radio,()=>state,()=>false,null)){
   int origins=0;List<byte[]> pending=null;radio.OnRequest=request=>{
    Need(NpSdGeometry.U(request,32)==1,"Origin lease requested AP");uint kind=NpSdGeometry.U(request,8);
    if(kind==0){origins++;pending=OriginPackets(request);radio.Emit(pending[0]);}
    else if(kind==4&&NpSdGeometry.U(request,24)==0){Need(NpSdGeometry.U(request,28)==7,"Origin renewal guessed session");foreach(var body in pending.GetRange(1,pending.Count-1))radio.Emit(body);pending=null;}
    else if(kind==5){Need(NpSdGeometry.U(request,24)==37&&NpSdGeometry.U(request,28)==7,"Cached phase/session changed");var body=Packet(band:1,channels:new uint[]{NpSdGeometry.U(request,20)});Put(body,4,NpSdGeometry.U(request,12));Seal(body);radio.Emit(body);}
   };
   Finish((Task)Call(form,"Tick"));Need(origins==1&&radio.Requests.Exists(x=>NpSdGeometry.U(x,8)==4),"Partial origin was not renewed");Need((int)Get(Get(form,"plot"),"count")==16,"Renewed origin did not start LFP");
   ((NumericUpDown)Get(form,"channel")).Value=1;Finish((Task)Call(form,"Tick"));Finish((Task)Call(form,"Tick"));Need(origins==1&&(int)Get(Get(form,"plot"),"count")==16,"Channel change did not reuse validated origin");
  }
  radio=new NpBleConnection();using(var form=new NpV1BlePreviewForm(radio,()=>state,()=>false,null)){
   radio.OnRequest=request=>{if(NpSdGeometry.U(request,8)==0){radio.Emit(OriginPackets(request)[0]);throw new IOException("Simulated write outcome unknown");}};
   Finish((Task)Call(form,"Tick"));Need(((CheckBox)Get(form,"enabled")).Checked&&(bool)Get(form,"retryPending"),"Partial write disabled Preview");
   Need(radio.Requests.Count==2&&NpSdGeometry.U(radio.Requests[1],8)==3&&NpSdGeometry.U(radio.Requests[1],12)==NpSdGeometry.U(radio.Requests[0],12)&&NpSdGeometry.U(radio.Requests[1],24)==0&&NpSdGeometry.U(radio.Requests[1],28)==7&&NpSdGeometry.U(radio.Requests[1],32)==1,"Partial origin lease was not stopped by its exact tuple");
  }
 }
 static void LfpStartupAndRetry(){
  var radio=new NpBleConnection();var state=new NpSdStatus{State=1,Mask=3,Hz=20000,Flags=128|4|NpV1BleProtocol.CompactPreview};using(var form=new NpV1BlePreviewForm(radio,()=>state,()=>false,null)){
   Need(Get(form,"band") is Label&&((Label)Get(form,"band")).Text=="LFP","V1 BLE offers AP preview");Need(((NumericUpDown)Get(form,"traceCount")).Value==1,"Initial preview uses excess BLE bandwidth");
   radio.OnRequest=request=>{Need(NpSdGeometry.U(request,32)==1,"V1 preview requested AP");uint kind=NpSdGeometry.U(request,8);if(kind==0)Origin(radio,request);if(kind==5){var body=Packet(band:1,channels:new uint[]{0});Put(body,4,NpSdGeometry.U(request,12));Seal(body);radio.Emit(body);}};
   Finish((Task)Call(form,"Tick"));Need((int)Get(Get(form,"plot"),"count")==16,"Two-probe20k LFP startup has no waveform");Need(radio.Requests.Count==2&&NpSdGeometry.U(radio.Requests[0],8)==0&&NpSdGeometry.U(radio.Requests[1],8)==5,"Unexpected recording/control command");
   Thread.Sleep(5100);var retry=(Task)Call(form,"Tick");while(radio.Requests.Count<3){Application.DoEvents();Thread.Sleep(1);}form.Dispose();Finish(retry);Need(((CheckBox)Get(form,"enabled")).Checked,"Closing during a retry unchecked Preview");
  }
  radio=new NpBleConnection();using(var form=new NpV1BlePreviewForm(radio,()=>state,()=>false,null)){
   Finish((Task)Call(form,"Tick"));Need(((CheckBox)Get(form,"enabled")).Checked&&(bool)Get(form,"retryPending"),"Origin timeout disabled Preview");Need(((Label)Get(form,"info")).Text.Contains("Retrying"),"Retry state is hidden");int sent=radio.Requests.Count;Finish((Task)Call(form,"Tick"));Need(radio.Requests.Count==sent,"Retry flooded BLE controls");
   radio.OnRequest=request=>{Need(NpSdGeometry.U(request,32)==1,"Retry requested AP");uint kind=NpSdGeometry.U(request,8);if(kind==0)Origin(radio,request);if(kind==5){var body=Packet(band:1,channels:new uint[]{0});Put(body,4,NpSdGeometry.U(request,12));Seal(body);radio.Emit(body);}};Thread.Sleep(1100);Finish((Task)Call(form,"Tick"));Need(((CheckBox)Get(form,"enabled")).Checked&&(int)Get(Get(form,"plot"),"count")==16,"LFP did not recover after startup timeout");
  }
 }
 static void Display(string output){
  foreach(bool compact in new[]{false,true}){
   var radio=new NpBleConnection();using(var form=Arm(radio,compact,()=>true)){
    Need(((NumericUpDown)Get(form,"traceCount")).Enabled==compact,"Legacy capabilities ignored");
    radio.Emit(compact?Packet(band:1):Legacy(20000,1,37,0));((Task)Call(form,"Tick")).GetAwaiter().GetResult();
    var plots=(Array)Get(form,"plots");
    for(int c=0;c<(compact?4:1);c++){
     var plot=plots.GetValue(c);Need((int)Get(plot,"count")==16,"Control-busy UI did not drain preview");
     Need((ulong)Get(plot,"displayEnd")== (ulong)Get(plots.GetValue(0),"displayEnd"),"Channels do not share a source-time axis");
     var values=(double[])Get(plot,"values");var ticks=(ulong[])Get(plot,"ticks");
     for(int n=0;n<16;n++){Need(c==0&&n==15?Double.IsNaN(values[n]):values[n]==512-((c*271+n*67)&1023),"Displayed nominal sample changed");Need(ticks[n]==37UL+(compact?NpSdGeometry.U(Packet(band:1),176+c*4):0U)+(ulong)n*37440,"Displayed channel timestamp changed");}
    }
    Need(radio.Requests.Count==0,"Preview display issued control traffic");
    ((NumericUpDown)Get(form,"gainBox")).Value=2;((NumericUpDown)Get(form,"windowSeconds")).Value=1;
    foreach(var plot in plots)Need((double)Get(plot,"gain")==2&&(double)Get(plot,"windowSeconds")==1,"Shared gain/window not applied");
    ((NumericUpDown)Get(form,"channel")).Value=5;Need(((CheckBox)Get(form,"enabled")).Checked,"Channel selection unchecked preview");
    foreach(var plot in plots)Need((int)Get(plot,"count")==0,"Selection retained stale data");
    Set(form,"activeId",0U);
   }
  }
  var source=new NpBleConnection();var request=NpV1PreviewBurst.CompactRequest(91,5,1,new uint[]{0,127,360,383},37,7,0);
  source.PreviewLowerPriorityBusy=true;Need(!source.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult()&&source.Requests.Count==0,"Preview bypassed IMU priority");
  source.PreviewLowerPriorityBusy=false;source.transaction.Wait();try{Need(!source.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult(),"Preview bypassed controls");}finally{source.transaction.Release();}
  Need(source.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult()&&source.Requests.Count==1,"Compact request admission failed");
  var deferred=new NpBleConnection{PreviewLowerPriorityBusy=true};using(var form=Arm(deferred,true,()=>false)){
   Set(form,"activeId",0U);((Task)Call(form,"Tick")).GetAwaiter().GetResult();
   Need(((CheckBox)Get(form,"enabled")).Checked&&deferred.Requests.Count==0&&(uint)Get(form,"activeId")==0,"Busy transport disabled preview or created a lease");
  }
  using(var form=Arm(new NpBleConnection(),true,()=>true)){
   Set(form,"activeId",0U);form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;
   form.Show();Application.DoEvents();((System.Windows.Forms.Timer)Get(form,"timer")).Stop();
   foreach(var size in new[]{new Size(850,550),new Size(700,450)}){
    form.ClientSize=size;form.PerformLayout();Application.DoEvents();
    var gain=(Control)Get(form,"gainBox");Need(gain.Visible&&gain.Parent.ClientRectangle.Contains(gain.Bounds),"Preview selector clipped");
    var plots=(Array)Get(form,"plots");foreach(Control plot in plots)Need(plot.Visible&&plot.Height>=60,"Trace clipped");
   }
   form.ClientSize=new Size(850,650);form.PerformLayout();Application.DoEvents();
   foreach(Control plot in (Array)Get(form,"plots")){Call(plot,"Clear",500.0);var wave=new double[400];for(int n=0;n<wave.Length;n++)wave[n]=30*Math.Sin(n*0.15);Call(plot,"Append",wave,0UL,1UL);}
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(output,"v1-four-channel-preview.png"));}
   form.Hide();
  }
 }
 [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();string output=args.Length==0?".":args[0];Directory.CreateDirectory(output);Protocol();Framing();FirmwareVectors(args[1]);OriginLeaseAndCache();LfpStartupAndRetry();Display(output);Console.WriteLine("PASS "+checks+" compact/legacy decode, actual firmware AP/LFP packets, CRC, fragmented/truncated framing, control priority, source offsets, four traces, selection and layout checks; fake radio, no hardware or app build.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
