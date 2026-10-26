using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;
using USB512_interface;

// Only the radio is doubled. The form, protocol, filters, theme, calibration
// and nominal sample conversion are compiled from canonical production source.
namespace USB512_interface {
    internal sealed partial class NpBleConnection {
        internal int Epoch=1,Writes,Captures;
        internal bool Connected=true;
        internal bool ImuPending;
        internal bool PreviewLowerPriorityBusy {get{return ImuPending;}}
        internal readonly List<byte[]> Requests=new List<byte[]>();
        internal readonly List<uint[]> Calibrations=new List<uint[]>();
        internal TaskCompletionSource<bool> PendingWrite;
        internal Action<byte[]> OnWrite;
        internal Func<uint,uint,uint,uint,Task<NpV2PreviewBurst>> Calibration;
        internal readonly SemaphoreSlim transaction=new SemaphoreSlim(1,1);
        internal event Action<int,byte[]> PreviewReceived;
        internal void Emit(byte[] data,int generation=1){if(PreviewReceived!=null)PreviewReceived(generation,data);}
        Task WriteFrame(byte[] data,int generation,CancellationToken cancel){
            cancel.ThrowIfCancellationRequested();if(generation!=Epoch || !Connected)throw new IOException("Stale/disconnected test epoch");
            if(data.Length!=67 || data[0]!=0x3c || data[1]!=0xb9 || data[66]!=0x3e)throw new IOException("Invalid preview frame");
            var body=new byte[64];Buffer.BlockCopy(data,2,body,0,64);Writes++;Requests.Add(body);if(OnWrite!=null)OnWrite(body);return PendingWrite==null?Task.FromResult(true):PendingWrite.Task;
        }
        internal Task<NpV2PreviewBurst> CapturePreview(uint id,uint probe,uint channel,uint hz,CancellationToken cancel){
            Captures++;Calibrations.Add(new[]{id,probe,channel,hz});
            if(Calibration!=null)return Calibration(id,probe,channel,hz);
            throw new Exception("Unexpected radio calibration in display-only fixture");
        }
    }
}

static class NpBlePreviewDisplayTests {
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Get(object item,string field){return item.GetType().GetField(field,All).GetValue(item);}
    static void Set(object item,string field,object value){item.GetType().GetField(field,All).SetValue(item,value);}
    static object Call(object item,string method,params object[] args){return item.GetType().GetMethod(method,All).Invoke(item,args);}
    static void Tick(object form){((Task)Call(form,"Tick")).GetAwaiter().GetResult();}
    static void ElapsePlayback(object plot,double seconds){Set(plot,"playbackStarted",Stopwatch.GetTimestamp()-(long)(seconds*Stopwatch.Frequency));}
    static void Finish(Task task){
        var timeout=Stopwatch.StartNew();
        while(!task.IsCompleted && timeout.ElapsedMilliseconds<5000){Application.DoEvents();Thread.Sleep(1);}
        Need(task.IsCompleted,"Preview callback did not retire");task.GetAwaiter().GetResult();
    }
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static ulong First(uint channel){return (ulong)new NpV2PreviewPlan(1,(int)channel,0,1,20000,0).FirstTick;}
    static void Reject(Action action,string why){bool refused=false;try{action();}catch(IOException){refused=true;}Need(refused,why);}
    static byte[] Packet(uint hz,ulong first,uint valid=65535,uint probe=1,uint channel=0,uint id=91){
        var b=new byte[224];Put(b,0,NpV2PreviewBurst.Magic);Put(b,4,91);Put(b,8,7);Put(b,12,hz);
        Put(b,4,id);Put(b,16,(uint)first);Put(b,20,(uint)(first>>32));Put(b,24,(192U<<16)|valid);Put(b,28,probe|(2U<<8)|(channel<<16));
        for(int n=0;n<16;n++)b[32+n*12]=(byte)(7<<((int)(probe-1)*3)); // raw2048, nominal zero
        return b;
    }
    static void CompactCode(byte[] b,int row,int code){
        int word=0;for(int bit=0;bit<12;bit++)word|=((code>>(11-bit))&1)<<bit;
        b[48+row*2]=(byte)word;b[49+row*2]=(byte)(word>>8);
    }
    static byte[] Compact(uint hz,ulong first,ulong valid=ulong.MaxValue,uint probe=1,uint channel=0,uint id=91){
        var b=Packet(hz,first,64,probe,channel,id);Array.Clear(b,32,192);
        Put(b,28,probe|(69U<<8)|(channel<<16));Put(b,32,500);Put(b,36,64);Put(b,40,(uint)valid);Put(b,44,(uint)(valid>>32));
        for(int row=0;row<64;row++)CompactCode(b,row,2048);return b;
    }
    static byte[] Error(uint reason,uint id=91){var b=Compact(20000,4,id:id);Put(b,28,1|((128U|reason)<<8));return b;}
    static NpV2PreviewBurst Calibration(uint id,uint probe,uint channel,uint hz){
        var sync=new byte[]{0xd3,0x6a,0x59,0xc5,0x3e,0xa7,0x1d,0xe4,0xb2,0x4f,0x78,0x96};
        var raw=new byte[3200];
        for(int i=0;i<raw.Length;i++){
            int tick=i%100,frame=i%1600/100,value=0;
            if(tick==0)value=frame==0?7:0;
            else if(tick==2 || tick==3)for(int lane=0;lane<3;lane++){int bit=frame*6+lane*2+tick-2;value|=((sync[bit/8]>>(bit&7))&1)<<lane;}
            raw[i]=(byte)(value<<((int)(probe-1)*3));
        }
        var burst=new NpV2PreviewBurst(id);
        for(int offset=0;offset<raw.Length;offset+=192){
            var packet=Packet(hz,0,probe:probe,channel:channel,id:id);Array.Clear(packet,32,192);
            Put(packet,24,(3200U<<16)|(uint)offset);Put(packet,28,probe|(64U<<8)|(channel<<16));
            Buffer.BlockCopy(raw,offset,packet,32,Math.Min(192,raw.Length-offset));burst.Accept(packet);
        }
        return burst;
    }
    static byte[] Frame(byte command,byte[] body,bool legacy=true){
        var frame=new byte[body.Length+(legacy?4:3)];frame[0]=legacy?(byte)0xad:(byte)0x3c;frame[1]=command;
        Buffer.BlockCopy(body,0,frame,2,body.Length);frame[body.Length+2]=legacy?(byte)0xda:(byte)0x3e;if(legacy)frame[body.Length+3]=0xda;return frame;
    }
    static byte[] Join(byte[] a,byte[] b,int prefix=-1){if(prefix<0)prefix=a.Length;var result=new byte[prefix+b.Length];Buffer.BlockCopy(a,0,result,0,prefix);Buffer.BlockCopy(b,0,result,prefix,b.Length);return result;}
    static NpBlePreviewForm Arm(NpBleConnection connection,uint hz,uint selectedProbe=1,uint selectedChannel=0,bool compact=false,Func<bool> ownerBusy=null){
        var status=new NpSdStatus{State=3,Mask=3,Hz=hz,Flags=128};
        var form=new NpBlePreviewForm(connection,()=>status,ownerBusy??(()=>true));if(form.GetType().GetField("multiSupport",All)!=null)Set(form,"multiSupport",-1);
        ((NumericUpDown)Get(form,"probe")).Value=selectedProbe;
        ((NumericUpDown)Get(form,"channel")).Value=selectedChannel;
        ((CheckBox)Get(form,"enabled")).Checked=true;
        Set(form,"hz",hz);Set(form,"phase",0);Set(form,"session",7U);Set(form,"activeProbe",selectedProbe);
        Set(form,"activeChannel",selectedChannel);Set(form,"request",91U);Call(Get(form,"plot"),"Clear",(int)selectedChannel);
        Set(form,"calibrationStart",status.Start);Set(form,"calibrationCommand",status.LastId);Set(form,"calibrationMask",status.Mask);
        var mode=form.GetType().GetField("legacyPreview",All);if(mode!=null)mode.SetValue(form,!compact);
        // Reflection keeps the same fixture compilable with the frozen baseline form.
        object stream=compact?Activator.CreateInstance(typeof(NpBlePreviewStream),All,null,new object[]{connection,91U,selectedProbe,selectedChannel,7U,hz,5U},null):new NpBlePreviewStream(connection,91,selectedProbe,selectedChannel,7,hz);
        Set(form,"stream",stream);
        return form;
    }
    static void BusyDrain(bool baseline){
        var connection=new NpBleConnection();using(var form=Arm(connection,20000)){
            object plot=Get(form,"plot");connection.Emit(Packet(20000,4));Tick(form);
            if(baseline){Need((int)Get(plot,"count")==0,"Frozen baseline did not reproduce control-busy display stall");return;}
            Need((int)Get(plot,"count")==16,"Background control ownership froze received display packets");
            Set(form,"busy",true);connection.Emit(Packet(20000,4+16*64000UL));Tick(form);
            Need((int)Get(plot,"count")==32,"In-flight preview write froze the local display");
            Need((bool)Get(form,"busy"),"Display consumer released another callback's wire ownership");
            Need(connection.Writes==0 && connection.Captures==0,"Local display queued radio work ahead of controls");
            for(uint packet=2;packet<14;packet++)connection.Emit(Packet(20000,4+packet*16*64000UL));
            Tick(form);Need((int)Get(plot,"count")==160,"Bounded RX queue did not drain its latest eight packets");
            Need(((NpBlePreviewStream)Get(form,"stream")).Dropped==4,"Host packet loss was concealed");
            Need((long)Get(form,"gaps")==1,"A real dropped-packet interval was hidden");
            connection.Emit(Packet(20000,4+14*16*64000UL,65533));Tick(form);
            Need((int)Get(plot,"count")==175 && (long)Get(form,"skipped")==1,"Invalid rows were filled instead of skipped");
            connection.Emit(Packet(20000,4+15*16*64000UL),0);Tick(form);
            Need((int)Get(plot,"count")==175,"A stale connection epoch reached the display");
            object stream=Get(form,"stream");var seconds=(NumericUpDown)Get(form,"windowSeconds");
            Need(seconds.Minimum==0.1M && seconds.Maximum==10 && seconds.Value==4,"Viewing-window bounds/default changed");
            seconds.Value=0.5M;Need((double)Get(plot,"windowSeconds")==0.5,"Window selector did not update the plot");
            seconds.Value=10;Need((int)Get(plot,"count")==175 && ReferenceEquals(Get(form,"stream"),stream),"Viewing window cleared history or restarted preview");
            Need(connection.Writes==0,"Viewing-window changes emitted BLE commands");
            var gain=(NumericUpDown)Get(form,"gainBox");gain.Value=2;
            Need((double)Get(plot,"gain")==2 && (int)Get(plot,"count")==175 && ReferenceEquals(Get(form,"stream"),stream),"Gain change restarted preview or lost samples");
            Need(connection.Writes==0 && connection.Captures==0,"Display gain issued radio commands");
            Need(((System.Windows.Forms.Timer)Get(form,"timer")).Interval<=16,"Display still waits50ms for32ms packets");
            connection.Epoch++;Tick(form);Need(Get(form,"stream")==null,"Changed connection did not retire preview");
        }
    }
    static void Profiles(){
        foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(uint probe in new uint[]{1,2}){
            var connection=new NpBleConnection();using(var form=Arm(connection,hz,probe)){
                connection.Emit(Packet(hz,4,65535,probe));Tick(form);
                Need((int)Get(Get(form,"plot"),"count")==16,"Display rejected a supported rate/probe");
                Need(connection.Writes==0,"Rate/probe display competed with controls");
            }
        }
    }
    static void WindowAndGaps(){
        var connection=new NpBleConnection();using(var form=Arm(connection,20000)){
            var plot=(Control)Get(form,"plot");var row=new ushort[16,3];for(int n=0;n<16;n++)for(int lane=0;lane<3;lane++)row[n,lane]=2048;
            Call(plot,"SetSampleRate",500U);
            for(ulong block=0;block<320;block++)Call(plot,"Append",row,block*16,1UL,65535UL);
            Need((int)Get(plot,"count")==5000,"Ten-second display history is not bounded at500Hz");
            foreach(double seconds in new double[]{0.1,0.5,1,2,4,10}){
                Call(plot,"SetWindowSeconds",seconds);
                int first=(int)Call(plot,"FirstVisible",5119UL,seconds*500);
                Need(5000-first==Math.Min(5000,(int)Math.Round(seconds*500)+1),"Wrapped display selected the wrong timestamp window");
                Need((int)Get(plot,"count")==5000,"Changing viewing length discarded retained samples");
            }
            foreach(double invalid in new[]{0.0,-1,10.1,Double.NaN,Double.PositiveInfinity}){
                bool rejected=false;try{Call(plot,"SetWindowSeconds",invalid);}catch(TargetInvocationException ex){rejected=ex.InnerException is ArgumentOutOfRangeException;}
                Need(rejected,"Invalid viewing duration admitted");
            }
            Call(plot,"Clear",0);Call(plot,"SetWindowSeconds",0.1);plot.Dock=DockStyle.None;plot.Size=new Size(600,300);
            Call(plot,"Append",row,4UL,64000UL,65535UL);Call(plot,"Append",row,4+32*64000UL,64000UL,65535UL);
            ElapsePlayback(plot,1);
            using(var image=new Bitmap(600,300)){
                // Parent form stays hidden. Invoke its actual paint methods;
                // DrawToBitmap may skip an invisible parent's child entirely.
                using(var graphics=Graphics.FromImage(image)){
                    var paint=new PaintEventArgs(graphics,new Rectangle(0,0,600,300));
                    Call(plot,"OnPaintBackground",paint);Call(plot,"OnPaint",paint);
                }
                Need(image.GetPixel(150,150).ToArgb()==Color.LightGreen.ToArgb() && image.GetPixel(500,150).ToArgb()==Color.LightGreen.ToArgb(),"Actual plot failed to draw valid selected-channel samples");
                Need(image.GetPixel(300,150).ToArgb()!=Color.LightGreen.ToArgb(),"Actual plot connected across a missing packet");
            }
        }
    }
    static void CompactProtocol(){
        var request=NpV2PreviewBurst.Request(91,5,2,383,1599,7);
        Need(request.Length==64 && BitConverter.ToUInt32(request,8)==5 && BitConverter.ToUInt32(request,20)==383 && BitConverter.ToUInt32(request,24)==1599 && BitConverter.ToUInt32(request,28)==7,"Compact request selection/session changed");
        Need(BitConverter.ToUInt32(request,60)==NpSdControl.Crc(request,60),"Compact request CRC changed");
        foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(uint probe in new uint[]{1,2})foreach(uint channel in new uint[]{0,1,127,128,255,256,383}){
            var packet=Compact(hz,First(channel),probe:probe,channel:channel);var expected=new ushort[64];
            for(int row=0;row<64;row++){expected[row]=(ushort)((row*271+channel*13)&4095);CompactCode(packet,row,expected[row]);}
            CompactCode(packet,0,2048);expected[0]=2048;CompactCode(packet,63,1);expected[63]=1;
            Need(packet[48]==1 && packet[49]==0 && packet[174]==0 && packet[175]==8,"Compact words do not retain first/last wire-tick bit positions");
            var burst=new NpV2PreviewBurst(91);Need(burst.Accept(packet),"Complete compact packet rejected");
            Need(burst.Rows==64 && burst.PreviewHz==500 && burst.ValidRows==ulong.MaxValue && burst.SourceStep==(ulong)(hz/500)*1600,"Compact row/mask/native tick metadata changed");
            var decoded=burst.DecodeRolling(0);Need(decoded.GetLength(0)==64 && decoded.GetLength(1)==1,"Compact decoder fabricated other channels");
            for(int row=0;row<64;row++)Need(decoded[row,0]==expected[row],"Compact raw serial bits changed ADC code or exact channel");
            Reject(()=>burst.DecodeRolling(1),"Compact phase mismatch accepted");
        }
        foreach(var damage in new Action<byte[]>[]{
            b=>Put(b,28,1|(5U<<8)),b=>Put(b,24,(193U<<16)|64),b=>Put(b,24,(192U<<16)|63),
            b=>Put(b,32,0),b=>Put(b,32,250),b=>Put(b,32,1000),b=>Put(b,36,63),b=>Put(b,36,65),
            b=>{Put(b,40,0);Put(b,44,0);},b=>b[49]=16,b=>b[175]=128,b=>b[176]=1,b=>b[223]=1,
            b=>Put(b,8,0),b=>Put(b,12,10000),b=>Put(b,28,3|(69U<<8)),b=>Put(b,28,1|(69U<<8)|(384U<<16)),
            b=>{Put(b,16,uint.MaxValue);Put(b,20,uint.MaxValue);}}){
            var bad=Compact(20000,4);damage(bad);Reject(()=>new NpV2PreviewBurst(91).Accept(bad),"Malformed compact metadata/word/padding accepted");
        }
        Need(!new NpV2PreviewBurst(91).Accept(new byte[223]),"Short compact packet accepted");
        Need(!new NpV2PreviewBurst(91).Accept(Compact(20000,4,id:90)),"Stale compact request accepted");
        var upper=new NpV2PreviewBurst(91);upper.Accept(Compact(20000,4,1UL<<63));Need(upper.ValidRows==(1UL<<63),"Validity bit63 was truncated");
        Reject(()=>upper.Accept(Compact(20000,4)),"Repeated compact packet accepted");
    }
    static void LegacyDecode(){
        var sync=new byte[]{0xd3,0x6a,0x59,0xc5,0x3e,0xa7,0x1d,0xe4,0xb2,0x4f,0x78,0x96};
        foreach(uint kind in new uint[]{0,1}){
            var raw=new byte[kind==0?3200:3072];
            if(kind==0)for(int i=0;i<raw.Length;i++){
                int tick=i%100,frame=i%1600/100,value=0;
                if(tick==0)value=frame==0?7:0;else if(tick==2 || tick==3)for(int lane=0;lane<3;lane++){int bit=frame*6+lane*2+tick-2;value|=((sync[bit/8]>>(bit&7))&1)<<lane;}
                raw[i]=(byte)(value<<3);
            }
            else for(int row=0;row<256;row++)for(int bit=0;bit<12;bit++)raw[row*12+bit]=(byte)((((row*31&4095)>>(11-bit))&1)<<5);
            var burst=new NpV2PreviewBurst(91);
            for(int offset=0;offset<raw.Length;offset+=192){
                var packet=Packet(20000,kind==0?0:First(383),probe:2,channel:kind==0?0U:383U);Array.Clear(packet,32,192);
                Put(packet,24,((uint)raw.Length<<16)|(uint)offset);Put(packet,28,2|((kind|64U)<<8)|((kind==0?0U:383U)<<16));
                Buffer.BlockCopy(raw,offset,packet,32,Math.Min(192,raw.Length-offset));
                Need(burst.Accept(packet)==(offset+192>=raw.Length),"Legacy calibration/gathered fragments changed");
            }
            if(kind==0)Need(burst.FindPhase()==0,"Legacy calibration framing changed");
            else {var selected=burst.Decode(0);for(int row=0;row<256;row++)Need(selected[row]==(row*31&4095),"Legacy gathered exact channel decoder changed");}
        }
    }
    static void WireAndAdmission(){
        var control=new byte[64];Put(control,0,0x32524c4e);Put(control,4,2);Put(control,12,91);Put(control,60,NpSdControl.Crc(control,60));var receipt=Frame(0xb9,control);
        foreach(bool legacy in new[]{false,true}){
            var compact=Compact(20000,4);compact[50]=0xad;compact[51]=0x0b;var frame=Frame(0xba,compact,legacy);
            for(int split=1;split<frame.Length;split++){
                var wire=new NpBleWire();var prefix=new byte[split];Buffer.BlockCopy(frame,0,prefix,0,split);
                Need(wire.Feed(prefix).Count==0,"Partial compact frame reached consumer");
                var remainder=new byte[frame.Length-split];Buffer.BlockCopy(frame,split,remainder,0,remainder.Length);
                var parsed=wire.Feed(Join(remainder,receipt));
                Need(parsed.Count==2 && parsed[0].Item1==0xba && parsed[1].Item1==0xb9 && new NpV2PreviewBurst(91).Accept(parsed[0].Item2),"Fragmented compact body swallowed preview/control receipt");
            }
            foreach(int cut in new[]{86,202}){
                var parsed=new NpBleWire().Feed(Join(frame,receipt,cut));
                Need(parsed.Count==1 && parsed[0].Item1==0xb9,"Truncated compact raw/tail held a valid control receipt");
            }
            var old=Packet(20000,4);Put(old,28,1|(66U<<8));
            Need(new NpBleWire().Feed(Join(Frame(0xba,old,legacy),receipt)).Count==2,"Legacy six-bit framing changed");
        }
        var radio=new NpBleConnection();var request=NpV2PreviewBurst.Request(91,5,2,383,1599,7);
        Need(radio.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult() && radio.Writes==1,"Actual transport rejected V2compact kind5");
        radio.ImuPending=true;
        Need(!radio.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult() && radio.Writes==1,"Waveform request bypassed IMU priority");
        radio.ImuPending=false;
        radio.transaction.Wait();try{Need(!radio.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult() && radio.Writes==1,"Preview queued ahead of an active control");}finally{radio.transaction.Release();}
        foreach(var damage in new Action<byte[]>[]{b=>Put(b,8,6),b=>{Put(b,0,0x3156504e);Put(b,8,6);},b=>Put(b,4,2),b=>Put(b,12,0),b=>Put(b,0,0),b=>b[20]^=1}){
            var bad=(byte[])request.Clone();damage(bad);if(bad[20]==request[20])Put(bad,60,NpSdControl.Crc(bad,60));
            bool rejected=false;try{radio.TryWritePreview(bad,1,CancellationToken.None).GetAwaiter().GetResult();}catch(ArgumentException){rejected=true;}
            Need(rejected && radio.Writes==1,"Actual transport admitted invalid opcode/version/id/CRC or V1kind6");
        }
        var v1=NpV2PreviewBurst.Request(91,4,1,0,0,7);Put(v1,0,0x3156504e);Put(v1,60,NpSdControl.Crc(v1,60));
        Need(radio.TryWritePreview(v1,1,CancellationToken.None).GetAwaiter().GetResult() && radio.Writes==2,"V1control kind4 admission changed");
        Reject(()=>radio.TryWritePreview(request,2,CancellationToken.None).GetAwaiter().GetResult(),"Stale transport epoch wrote preview");
        radio.Connected=false;Reject(()=>radio.TryWritePreview(request,1,CancellationToken.None).GetAwaiter().GetResult(),"Disconnected transport wrote preview");
        using(var cancel=new CancellationTokenSource()){
            cancel.Cancel();bool stopped=false;try{radio.TryWritePreview(request,1,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}
            Need(stopped && radio.Writes==2,"Cancelled transport wrote preview");
        }
    }
    static void CompactDisplay(){
        foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(uint probe in new uint[]{1,2})foreach(uint channel in new uint[]{0,128,256,383}){
            var connection=new NpBleConnection();using(var form=Arm(connection,hz,probe,channel,true)){
                var plot=Get(form,"plot");var packet=Compact(hz,First(channel),probe:probe,channel:channel);CompactCode(packet,63,1);
                int correctedChannel=-1;bool wrongCorrection=false;
                Set(plot,"CorrectSample",(Func<int,short,short>)((ch,value)=>{correctedChannel=ch;if(ch!=channel)wrongCorrection=true;return value;}));
                connection.Emit(packet);Tick(form);
                Need((int)Get(plot,"count")==64 && (int)Get(plot,"selectedChannel")==channel,"Compact display did not keep the exact selected channel");
                Need(!wrongCorrection && correctedChannel==channel,"Compact calibration used a base channel or another lane");
                Need(((short[])Get(plot,"values"))[63]==2047,"Compact display raw-to-nominal conversion changed");
                Need((uint)Get(plot,"sampleHz")==500 && (ulong)Get(plot,"stride")==((ulong)hz/500)*1600,"Plot used native Hz or fixed16-row timing");
                Need(((NpHighPassControl)Get(form,"highPass")).SampleRate==500,"HPF sample rate did not come from preview packet");
                Need(((NumericUpDown)Get(form,"channel")).Maximum==383,"Exact channel selector lost lanes128..383");
                Need(((ToolTip)Get(form,"diagnostics")).GetToolTip((Label)Get(form,"info")).Contains("aliasing"),"Subsampling limitation is missing from preview details");
                Need(connection.Writes==0 && connection.Captures==0,"Compact display competed with controls");
            }
        }
        var radio=new NpBleConnection();using(var form=Arm(radio,20000,selectedChannel:256,compact:true)){
            object plot=Get(form,"plot");var hp=(NpHighPassControl)Get(form,"highPass");hp.EnabledBox.Checked=true;
            var packet=Compact(20000,4,ulong.MaxValue&~(1UL<<32)&~(1UL<<63),channel:256);
            for(int row=0;row<64;row++)CompactCode(packet,row,2048+row);
            radio.Emit(packet);Tick(form);
            Need((int)Get(plot,"count")==62 && (long)Get(form,"skipped")==2,"Compact validity mask lost high rows or fabricated missing data");
            var ticks=(ulong[])Get(plot,"ticks");Need(ticks[31]==4+31*64000UL && ticks[32]==4+33*64000UL,"Missing compact row was resampled/interpolated");
            Need(((short[])Get(plot,"values"))[32]==0,"HPF joined across a missing compact row");
            Need((ulong)Get(form,"previousEnd")==4+64*64000UL,"Compact packet end retained legacy16-row stride");
            hp.CutoffBox.Text="250";Need(hp.CutoffHz==0,"HPF accepted Nyquist cutoff for500-Hz preview");
            hp.CutoffBox.Text="249";Need(hp.CutoffHz==249,"HPF incorrectly used native/legacy sample rate");
            hp.EnabledBox.Checked=false;
            radio.Emit(Compact(20000,4+128*64000UL,channel:256));Tick(form);
            Need((long)Get(form,"gaps")==1,"A missing compact packet was joined");
        }
        radio=new NpBleConnection();using(var form=Arm(radio,20000,compact:true)){
            var plot=(Control)Get(form,"plot");Call(plot,"SetWindowSeconds",0.1);plot.Dock=DockStyle.None;plot.Size=new Size(600,300);
            radio.Emit(Compact(20000,4,ulong.MaxValue&~(1UL<<32)));Tick(form);
            ElapsePlayback(plot,1);
            using(var image=new Bitmap(600,300))using(var graphics=Graphics.FromImage(image)){
                var paint=new PaintEventArgs(graphics,new Rectangle(0,0,600,300));Call(plot,"OnPaintBackground",paint);Call(plot,"OnPaint",paint);
                Need(image.GetPixel(150,150).ToArgb()==Color.LightGreen.ToArgb() && image.GetPixel(500,150).ToArgb()==Color.LightGreen.ToArgb(),"Compact plot used the wrong timestamp viewing window");
                Need(image.GetPixel(379,150).ToArgb()!=Color.LightGreen.ToArgb(),"Compact sweep drew across invalid row32");
            }
        }
    }
    static void Compatibility(){
        var connection=new NpBleConnection();using(var form=Arm(connection,20000,selectedChannel:383,compact:true,ownerBusy:()=>false)){
            connection.Emit(Compact(20000,First(383),channel:383));Tick(form);connection.Emit(Error(1,id:90));Tick(form);
            Need(!(bool)Get(form,"legacyPreview") && Get(form,"stream")!=null,"Stale unsupported reply caused a fallback");
            connection.Emit(Error(1));var rejected=Stopwatch.StartNew();Tick(form);
            Need((bool)Get(form,"legacyPreview") && Get(form,"stream")==null && ((CheckBox)Get(form,"enabled")).Checked,"Explicit compact unsupported did not schedule legacy once");
            Need((uint)Get(form,"previewHz")==0 && (uint)Get(Get(form,"plot"),"sampleHz")==0 && (int)Get(Get(form,"plot"),"count")==0,"Mode switch retained the old display rate/history");
            Tick(form);Need(connection.Writes==0 && connection.Captures==0,"Fallback ignored500-ms preview admission or recalibrated the probe");
            Thread.Sleep(575);Tick(form);
            Need(connection.Writes==1 && rejected.ElapsedMilliseconds>=550 && connection.Captures==0,"Fallback did not issue one delayed request with retained calibration");
            var request=connection.Requests[0];uint id=BitConverter.ToUInt32(request,12);
            Need(BitConverter.ToUInt32(request,8)==2 && BitConverter.ToUInt32(request,20)==383 && BitConverter.ToUInt32(request,28)==7,"Legacy fallback lost exact selection/session");
            var legacy=Packet(20000,First(383),channel:383,id:id);for(int row=0;row<16;row++)legacy[32+row*12]=4;
            connection.Emit(legacy);Tick(form);
            Need((int)Get(Get(form,"plot"),"count")==16 && (uint)Get(form,"previewHz")==500 && (int)Get(Get(form,"plot"),"selectedChannel")==383 && ((short[])Get(Get(form,"plot"),"values"))[0]==0,"Explicit fallback failed to display only legacy channel383");
            connection.Emit(Error(1,id));Tick(form);Tick(form);
            Need(connection.Writes==1 && !((CheckBox)Get(form,"enabled")).Checked,"Legacy unsupported retried/fell back again");
        }
        foreach(uint reason in new uint[]{2,3,4,5}){
            connection=new NpBleConnection();using(var form=Arm(connection,20000,compact:true,ownerBusy:()=>false)){
                connection.Emit(Error(reason));Tick(form);Tick(form);
                Need(!(bool)Get(form,"legacyPreview") && connection.Writes==0 && connection.Captures==0 && ((CheckBox)Get(form,"enabled")).Checked && Get(form,"stream")==null,"Transient preview refusal disabled selection, replayed immediately or triggered legacy fallback");
                Need(!(bool)Call(Get(form,"calibrationPressureRetry"),"CanAttempt",((Stopwatch)Get(form,"clock")).ElapsedMilliseconds),"Rolling retry ignored backoff");
            }
        }
        connection=new NpBleConnection();using(var form=Arm(connection,20000,compact:true,ownerBusy:()=>false)){
            var bad=Compact(20000,4);Put(bad,32,250);connection.Emit(bad);Tick(form);
            Need(!(bool)Get(form,"legacyPreview") && connection.Writes==0 && !((CheckBox)Get(form,"enabled")).Checked,"Malformed compact metadata triggered fallback");
        }
        connection=new NpBleConnection();using(var form=Arm(connection,20000,compact:true,ownerBusy:()=>false)){
            var old=(NpBlePreviewStream)Get(form,"stream");old.Dispose();Set(form,"stream",null);
            Thread.Sleep(1050);Tick(form);
            Need(connection.Writes==1 && BitConverter.ToUInt32(connection.Requests[0],8)==5,"New preview did not request compact kind5");
            Thread.Sleep(2550);Tick(form);Tick(form);
            Need(!(bool)Get(form,"legacyPreview") && connection.Writes==1 && connection.Captures==0 && ((CheckBox)Get(form,"enabled")).Checked && Get(form,"stream")==null,"Silence disabled preview or caused immediate replay/fallback");
        }
    }
    static void SelectionSwitch(){
        var connection=new NpBleConnection{Calibration=(id,probe,channel,hz)=>Task.FromResult(Calibration(id,probe,channel,hz))};
        using(var form=Arm(connection,20000,compact:true,ownerBusy:()=>false)){
            var selected=(NumericUpDown)Get(form,"channel");var probe=(NumericUpDown)Get(form,"probe");
            var enabled=(CheckBox)Get(form,"enabled");var window=(NumericUpDown)Get(form,"windowSeconds");window.Value=0.5M;
            connection.Emit(Compact(20000,4));Tick(form);selected.Value=383;probe.Value=2;selected.Value=128;
            Need(enabled.Checked && Get(form,"stream")==null && (int)Get(Get(form,"plot"),"count")==0,"Selection unchecked Preview or retained old waveform");
            connection.Emit(Error(1));connection.Emit(Compact(20000,4));Tick(form);
            Need(enabled.Checked && !(bool)Get(form,"legacyPreview") && connection.Captures==0 && connection.Writes==0,"Retired packets changed mode or sent new preview early");
            Thread.Sleep(575);Tick(form);
            Need(connection.Captures==0 && enabled.Checked,"550-ms admission was mistaken for old2s lease expiry");
            var clock=(Stopwatch)Get(form,"clock");long deadline=(long)Get(form,"rearmAfter");
            Need(deadline-clock.ElapsedMilliseconds>1400,"Selection did not retain a conservative2s lease window");
            Thread.Sleep((int)Math.Max(0,deadline-clock.ElapsedMilliseconds)+25);
            Finish((Task)Call(form,"Tick"));
            Need(enabled.Checked && connection.Captures==1 && connection.Writes==1,"Latest selection failed to automatically rearm");
            var capture=connection.Calibrations[0];var request=connection.Requests[0];uint id=BitConverter.ToUInt32(request,12);
            Need(capture[1]==2 && capture[2]==128 && BitConverter.ToUInt32(request,8)==5 && BitConverter.ToUInt32(request,16)==2 && BitConverter.ToUInt32(request,20)==128,"Rapid channel/probe changes did not converge to latest selection");
            Need(window.Value==0.5M && (double)Get(Get(form,"plot"),"windowSeconds")==0.5,"Selection reset the viewing duration");
            connection.Emit(Compact(20000,4));connection.Emit(Compact(20000,First(128),probe:2,channel:128,id:id));Tick(form);
            Need((int)Get(Get(form,"plot"),"count")==64 && (int)Get(Get(form,"plot"),"selectedChannel")==128,"Rearmed waveform included retired-selection packets");
            Need(connection.Requests.TrueForAll(b=>BitConverter.ToUInt32(b,8)==5),"Channel switching issued recording or extra renewal commands");
            enabled.Checked=false;selected.Value=256;
            Need(!enabled.Checked,"Selection overrode an explicitly paused preview");
        }
    }
    static void InFlightSelection(){
        foreach(string stage in new[]{"calibration","start","renew"})foreach(bool fail in new[]{false,true}){
            var radio=new NpBleConnection();var calibration=new TaskCompletionSource<NpV2PreviewBurst>();
            using(var form=Arm(radio,20000,compact:true,ownerBusy:()=>false)){
                if(stage=="calibration" || stage=="start"){
                    ((NpBlePreviewStream)Get(form,"stream")).Dispose();Set(form,"stream",null);
                    if(stage=="calibration"){Set(form,"session",0U);radio.Calibration=(id,probe,channel,hz)=>calibration.Task;}
                }
                if(stage!="calibration")radio.PendingWrite=new TaskCompletionSource<bool>();
                Thread.Sleep(stage=="renew"?775:1025);
                var old=(Task)Call(form,"Tick");
                Need(!old.IsCompleted && (bool)Get(form,"busy"),"Fixture did not hold old "+stage+" in flight");
                var selected=(NumericUpDown)Get(form,"channel");selected.Value=127;((NumericUpDown)Get(form,"probe")).Value=2;selected.Value=383;
                Need(((CheckBox)Get(form,"enabled")).Checked && Get(form,"stream")==null,"In-flight selection unchecked Preview or retained subscription");
                int captures=radio.Captures,writes=radio.Writes;long initialDeadline=(long)Get(form,"rearmAfter");
                Tick(form);Need(radio.Captures==captures && radio.Writes==writes,"New request overtook old in-flight preview");
                Thread.Sleep(50);
                if(stage=="calibration"){
                    var input=radio.Calibrations[0];
                    if(fail)calibration.SetException(new IOException("Old calibration failed"));
                    else calibration.SetResult(Calibration(input[0],input[1],input[2],input[3]));
                }else if(fail)radio.PendingWrite.SetException(new OperationCanceledException("Old write canceled"));
                else radio.PendingWrite.SetResult(true);
                Finish(old);
                Need(((CheckBox)Get(form,"enabled")).Checked && !(bool)Get(form,"busy") && Get(form,"stream")==null && (uint)Get(form,"session")==0,"Stale "+stage+" completion disabled/repopulated latest selection");
                long deadline=(long)Get(form,"rearmAfter");var clock=(Stopwatch)Get(form,"clock");
                Need(deadline>initialDeadline && deadline-clock.ElapsedMilliseconds>2100,"Lease wait began before old "+stage+" finished");
                Tick(form);Need(radio.Captures==captures && radio.Writes==writes,"Old callback renewed or new selection bypassed lease guard");
                Need(radio.Requests.TrueForAll(b=>BitConverter.ToUInt32(b,8)==(stage=="renew"?4U:5U)),"Stale callback sent another preview or recording command");
            }
        }
    }
    static void AdmitCalibration(object form){
        // Age only the existing admission stopwatch in this radio-double
        // fixture. Production keeps its real timer and monotonic deadlines.
        var timer=(Stopwatch)Get(form,"retry");
        var elapsed=typeof(Stopwatch).GetField("elapsed",All)??typeof(Stopwatch).GetField("_elapsed",All);
        elapsed.SetValue(timer,(long)elapsed.GetValue(timer)+Stopwatch.Frequency*1100/1000);
    }
    static void ExpireCalibrationBackoff(object form){
        AdmitCalibration(form);Set(Get(form,"calibrationPressureRetry"),"next",0L);
    }
    static NpSdStatus PressureStatus(){return new NpSdStatus{State=3,Mask=3,Hz=15000,Flags=0x0030FF95,Start=8192,LastId=71};}
    static void CalibrationPressure(){
        var s=PressureStatus();bool ownerBusy=false;
        Need(s.Busy && NpV2SdControl.CanPreview(s),"Real recording-active flags must exercise production Busy semantics");
        foreach(uint flags in new uint[]{0x0030FF91U,0x0030FF95U,0x0030FF9DU}){
            s=PressureStatus();s.Flags=flags;
            var admission=new NpBleConnection{Calibration=(id,p,c,h)=>Task.FromException<NpV2PreviewBurst>(new NpV2PreviewUnavailableException(4))};
            using(var form=new NpBlePreviewForm(admission,()=>s,()=>false)){
            Set(form,"multiSupport",-1);
                AdmitCalibration(form);Finish((Task)Call(form,"Tick"));
                Need(admission.Captures==((flags&8U)==0?1:0),"Active recording must allow calibration; pending SD command must defer it");
            }
        }
        s=PressureStatus();
        var radio=new NpBleConnection{Calibration=(id,p,c,h)=>Task.FromException<NpV2PreviewBurst>(new NpV2PreviewUnavailableException(4))};
        using(var form=new NpBlePreviewForm(radio,()=>s,()=>ownerBusy)){
            Set(form,"multiSupport",-1);
            var enabled=(CheckBox)Get(form,"enabled");
            for(int attempt=0;attempt<8;attempt++){
                ExpireCalibrationBackoff(form);Finish((Task)Call(form,"Tick"));
                Need(radio.Captures==attempt+1 && radio.Writes==0,"Calibration pressure replayed controls or duplicated a retry");
                {
                    Need(enabled.Checked && (int)Get(form,"calibrationPressureRetries")==attempt+1,"Persistent pressure disabled Preview");
                    var policy=(NpV2PreviewRetry)Get(form,"calibrationPressureRetry");var clock=(Stopwatch)Get(form,"clock");
                    long remaining=(long)Get(policy,"next")-clock.ElapsedMilliseconds,delay=1000L<<Math.Min(attempt,3);
                    Need(remaining>delay-100 && remaining<=delay && !policy.CanAttempt(clock.ElapsedMilliseconds),"Calibration backoff is not bounded1/2/4/8 seconds");
                    Tick(form);Need(radio.Captures==attempt+1,"Timer retried calibration before its deadline");
                }
            }
            Need(enabled.Checked && ((Label)Get(form,"info")).Text.Contains("waiting for recording bandwidth"),"Persistent pressure lost waiting state");
            enabled.Checked=false;ExpireCalibrationBackoff(form);Tick(form);Need(radio.Captures==8,"Explicit pause kept retrying");
            for(int n=1;n<radio.Calibrations.Count;n++)Need(radio.Calibrations[n][0]!=radio.Calibrations[n-1][0],"Calibration reused a preview request ID");
        }
        s=PressureStatus();radio=new NpBleConnection();
        radio.Calibration=(id,p,c,h)=>radio.Captures==1?Task.FromException<NpV2PreviewBurst>(new NpV2PreviewUnavailableException(4)):Task.FromResult(Calibration(id,p,c,h));
        using(var form=new NpBlePreviewForm(radio,()=>s,()=>ownerBusy)){
            Set(form,"multiSupport",-1);
            AdmitCalibration(form);Finish((Task)Call(form,"Tick"));
            ownerBusy=true;ExpireCalibrationBackoff(form);Tick(form);
            Need(radio.Captures==1 && radio.Writes==0,"Pressure retry overtook recording/control ownership");
            ownerBusy=false;s.Chunks+=100;Finish((Task)Call(form,"Tick"));
            Need(radio.Captures==2 && radio.Writes==1 && ((CheckBox)Get(form,"enabled")).Checked,"Pressure relief did not start compact preview during the same recording");
            Need((int)Get(form,"calibrationPressureRetries")==0 && ((NpV2PreviewRetry)Get(form,"calibrationPressureRetry")).CanAttempt(0),"Successful calibration retained retry state");
            Need(radio.Calibrations[0][0]!=radio.Calibrations[1][0] && radio.Calibrations[1][1]==1 && radio.Calibrations[1][2]==0 && radio.Calibrations[1][3]==15000,"Retry changed selection/rate or reused its ID");
            var request=radio.Requests[0];uint id=BitConverter.ToUInt32(request,12);
            Need(BitConverter.ToUInt32(request,8)==5 && BitConverter.ToUInt32(request,28)==7,"Recovered calibration lost kind5 or its actual wire session");
            radio.Emit(Compact(15000,4,id:id));Tick(form);
            Need((int)Get(Get(form,"plot"),"count")==64 && (uint)Get(form,"previewHz")==500,"Actual handler did not display compact data after pressure relief");
        }
        var failures=new List<Exception>{new TimeoutException("uncertain calibration"),new IOException("uncertain transport"),new OperationCanceledException()};
        failures.Add(new NpV2PreviewUnavailableException(1));
        foreach(var failure in failures){
            s=PressureStatus();radio=new NpBleConnection{Calibration=(id,p,c,h)=>Task.FromException<NpV2PreviewBurst>(failure)};
            using(var form=new NpBlePreviewForm(radio,()=>s,()=>false)){
            Set(form,"multiSupport",-1);
                AdmitCalibration(form);Finish((Task)Call(form,"Tick"));ExpireCalibrationBackoff(form);Tick(form);
                Need(radio.Captures==1 && radio.Writes==0 && !((CheckBox)Get(form,"enabled")).Checked,"Non-pressure refusal/uncertainty retried calibration");
                Need(!(bool)Get(form,"legacyPreview"),"Calibration error triggered legacy fallback");
            }
        }
        foreach(string changed in new[]{"channel","probe","epoch","start","command","rate","stop","fault"}){
            s=PressureStatus();radio=new NpBleConnection();var pending=new TaskCompletionSource<NpV2PreviewBurst>();
            radio.Calibration=(id,p,c,h)=>pending.Task;
            using(var form=new NpBlePreviewForm(radio,()=>s,()=>false)){
            Set(form,"multiSupport",-1);
                AdmitCalibration(form);var task=(Task)Call(form,"Tick");Need(!task.IsCompleted,"Calibration fixture did not remain in flight");
                if(changed=="channel")((NumericUpDown)Get(form,"channel")).Value=128;
                else if(changed=="probe")((NumericUpDown)Get(form,"probe")).Value=2;
                else if(changed=="epoch")radio.Epoch++;
                else if(changed=="start")s.Start+=8192;
                else if(changed=="command")s.LastId++;
                else if(changed=="rate")s.Hz=20000;
                else if(changed=="stop")s.State=5;
                else s.Fault=5;
                pending.SetException(new NpV2PreviewUnavailableException(4));Finish(task);
                Need((int)Get(form,"calibrationPressureRetries")==0 && radio.Captures==1 && radio.Writes==0,"Stale context scheduled calibration pressure retry: "+changed);
                if(changed=="channel" || changed=="probe")Need(((CheckBox)Get(form,"enabled")).Checked,"Stale calibration refusal unchecked a new selection");
            }
        }
    }
    static void TripleRecordingRetry(){
        foreach(uint reason in new uint[]{2,3,4,5,128,129}){
            var s=new NpSdStatus{State=3,Mask=7,Hz=20000,Flags=128|4,Start=8192,LastId=71};
            bool ownerBusy=false;var radio=new NpBleConnection();
            radio.Calibration=(id,p,c,h)=>radio.Captures==1?
                Task.FromException<NpV2PreviewBurst>(new NpV2PreviewUnavailableException(reason)):
                Task.FromResult(Calibration(id,p,c,h));
            using(var form=new NpBlePreviewForm(radio,()=>s,()=>ownerBusy)){
            Set(form,"multiSupport",-1);
                AdmitCalibration(form);Finish((Task)Call(form,"Tick"));
                Need(((CheckBox)Get(form,"enabled")).Checked && radio.Captures==1 && radio.Writes==0,"Triple20 startup disabled preview on transient reason "+reason);
                Need((uint)Get(form,"hz")==20000 && (uint)Get(form,"calibrationMask")==7,"Retry lost triple20 recording context");
                Tick(form);Need(radio.Captures==1,"Retry ignored deadline");
                // Fake-radio deadline acceleration only; production always waits
                // its monotonic backoff and full lease-retirement deadline.
                ExpireCalibrationBackoff(form);Set(form,"rearmAfter",0L);ownerBusy=true;Tick(form);
                Need(radio.Captures==1,"Preview retry overtook controls");
                ownerBusy=false;Finish((Task)Call(form,"Tick"));
                Need(((CheckBox)Get(form,"enabled")).Checked && radio.Captures==2 && radio.Writes==1,"Triple20 preview did not resume after transient refusal");
                Need(radio.Calibrations[0][0]!=radio.Calibrations[1][0] && BitConverter.ToUInt32(radio.Requests[0],8)==5,"Retry reused request ID or changed transport without explicit unsupported");
                uint id=BitConverter.ToUInt32(radio.Requests[0],12);radio.Emit(Compact(20000,4,id:id));Tick(form);
                Need((int)Get(Get(form,"plot"),"count")==64,"Recovered triple20 preview never rendered data");
                radio.Emit(Error(4,id));Tick(form);
                Need(((CheckBox)Get(form,"enabled")).Checked && Get(form,"stream")==null,"Rolling pressure disabled triple20 preview");
                long deadline=(long)Get(form,"rearmAfter");
                Need(deadline-((Stopwatch)Get(form,"clock")).ElapsedMilliseconds>2100,"Rolling recovery did not wait for the old lease");
                ExpireCalibrationBackoff(form);Set(form,"rearmAfter",0L);s.Start+=8192;Tick(form);
                Need(radio.Captures==2 && radio.Writes==1,"Changed recording was replayed under old preview context");
            }
        }
    }
    static void RetryDuringLeaseWrite(){
        var radio=new NpBleConnection{PendingWrite=new TaskCompletionSource<bool>()};
        using(var form=Arm(radio,20000,compact:true,ownerBusy:()=>false)){
            var heartbeat=(Stopwatch)Get(form,"heartbeat");
            var elapsed=typeof(Stopwatch).GetField("elapsed",All)??typeof(Stopwatch).GetField("_elapsed",All);
            elapsed.SetValue(heartbeat,(long)elapsed.GetValue(heartbeat)+Stopwatch.Frequency);
            var pending=(Task)Call(form,"Tick");
            Need(!pending.IsCompleted && radio.Writes==1 && BitConverter.ToUInt32(radio.Requests[0],8)==4,"Lease-write fixture did not remain in flight");
            radio.Emit(Error(4));Tick(form);
            Need(((CheckBox)Get(form,"enabled")).Checked && Get(form,"stream")==null,"Refusal during lease write disabled Preview");
            Set(form,"rearmAfter",0L);radio.PendingWrite.SetResult(true);Finish(pending);
            Need((long)Get(form,"rearmAfter")-((Stopwatch)Get(form,"clock")).ElapsedMilliseconds>2100,"Recovery retirement was timed before old GATT write completed");
            radio.Emit(Compact(20000,4));Tick(form);
            Need(radio.Writes==1 && radio.Captures==0 && Get(form,"stream")==null,"Old lease completion/reply resumed or renewed a retired subscription");
        }
    }
    static void ControlsAfterPhase(){
        foreach(bool controlRace in new[]{false,true}){
            var s=new NpSdStatus{State=3,Mask=7,Hz=20000,Flags=128|4,Start=8192,LastId=71};
            bool ownerBusy=false;var radio=new NpBleConnection();
            radio.Calibration=(id,p,c,h)=>{
                if(controlRace)radio.transaction.Wait();else ownerBusy=true;
                return Task.FromResult(Calibration(id,p,c,h));
            };
            using(var form=new NpBlePreviewForm(radio,()=>s,()=>ownerBusy)){
            Set(form,"multiSupport",-1);
                AdmitCalibration(form);Finish((Task)Call(form,"Tick"));
                Need(((CheckBox)Get(form,"enabled")).Checked && radio.Captures==1 && radio.Writes==0 && Get(form,"stream")==null && (uint)Get(form,"session")==7,"Control ownership discarded a completed triple20 phase snapshot");
                ownerBusy=false;if(controlRace)radio.transaction.Release();AdmitCalibration(form);Finish((Task)Call(form,"Tick"));
                Need(radio.Captures==1 && radio.Writes==1 && BitConverter.ToUInt32(radio.Requests[0],8)==5,"Idle control handoff repeated phase calibration instead of starting preview");
            }
        }
    }
    static bool OwnVisible(Control control){
        // The form is deliberately never shown. Read the control's native
        // visibility without its hidden parent, then restore the same Z order.
        var parent=control.Parent;int at=parent.Controls.GetChildIndex(control);
        parent.Controls.Remove(control);bool result=control.Visible;
        parent.Controls.Add(control);parent.Controls.SetChildIndex(control,at);return result;
    }
    static Bitmap Render(Control plot){
        var image=new Bitmap(plot.Width,plot.Height);
        using(var graphics=Graphics.FromImage(image)){
            var paint=new PaintEventArgs(graphics,new Rectangle(Point.Empty,plot.Size));
            Call(plot,"OnPaintBackground",paint);Call(plot,"OnPaint",paint);
        }
        return image;
    }
    static void StableSweep(string output){
        var connection=new NpBleConnection();using(var form=Arm(connection,20000,compact:true)){
            var plot=(Control)Get(form,"plot");plot.Dock=DockStyle.None;plot.Size=new Size(600,300);
            Call(plot,"SetSampleRate",500U);Call(plot,"SetWindowSeconds",0.1);
            var rows=new ushort[16,1];for(int n=0;n<16;n++)rows[n,0]=2048;
            Call(plot,"Append",rows,4UL,64000UL,65535UL);
            ulong tick=4+10*64000UL;float before=(float)Call(plot,"XForTick",tick);
            Call(plot,"Append",rows,4+20*64000UL,64000UL,65535UL);
            ElapsePlayback(plot,1);
            Need(before==(float)Call(plot,"XForTick",tick),"New BLE packet shifted the whole existing waveform");
            Need(Math.Abs(before-129)<0.01,"Sweep scale is not the actual500-Hz source grid");
            Need(Math.Abs((float)Call(plot,"XForTick",4+63*64000UL)-(float)Call(plot,"XForTick",4+13*64000UL))<0.01,"Sweep period differs from the selected100-ms window");
            using(var image=Render(plot)){
                Need(image.GetPixel(129,150).ToArgb()==Color.LightGreen.ToArgb(),"Stable sweep lost an existing sample");
                Need(image.GetPixel(220,150).ToArgb()!=Color.LightGreen.ToArgb(),"Stable sweep fabricated samples in a source gap");
            }
            Call(plot,"Clear",0);
            var edge=new ushort[,]{{1920},{2176}};
            Call(plot,"Append",edge,4+49*64000UL,64000UL,3UL);
            ElapsePlayback(plot,1);
            using(var image=Render(plot))Need(image.GetPixel(300,150).ToArgb()!=Color.LightGreen.ToArgb(),"Sweep wrap drew a false diagonal through the waveform");
            Call(plot,"Clear",0);Call(plot,"SetWindowSeconds",1.0);
            var wave=new ushort[64,1];
            for(int packet=0;packet<8;packet++){
                if(packet==4)continue; // A real missing burst must stay visible.
                for(int row=0;row<64;row++)wave[row,0]=(ushort)(2048+120*Math.Sin((packet*64+row)*2*Math.PI*8/500));
                Call(plot,"Append",wave,4+(ulong)packet*64*64000UL,64000UL,ulong.MaxValue);
            }
            ElapsePlayback(plot,2);
            if(!String.IsNullOrEmpty(output)){Directory.CreateDirectory(output);using(var image=Render(plot))image.Save(Path.Combine(output,"ble-preview-fixed-sweep.png"));}
            Need(connection.Writes==0 && connection.Captures==0,"Sweep rendering added BLE or recording traffic");
        }
        foreach(bool compact in new[]{false,true}){
            var radio=new NpBleConnection();using(var form=Arm(radio,20000,compact:compact)){
                radio.Emit(compact?Compact(20000,4):Packet(20000,4));Tick(form);
                string text=((Label)Get(form,"info")).Text;
                string details=((ToolTip)Get(form,"diagnostics")).GetToolTip((Label)Get(form,"info"));
                Need(text.Contains("Preview") && text.Contains("samples/s") && details.Contains(compact?"Compact transport":"Legacy transport") && details.Contains("subsampled"),"Preview rate or transport details are missing");
            }
        }
    }
    static void PacedSweep(){
        foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(bool compact in new[]{false,true}){
            var radio=new NpBleConnection();using(var form=Arm(radio,hz,compact:compact)){
                var plot=(Control)Get(form,"plot");ulong step=(ulong)(hz/500)*1600;
                radio.Emit(compact?Compact(hz,4):Packet(hz,4));Tick(form);
                long start=(long)Get(plot,"playbackStarted");int rows=compact?64:16;
                Need((int)Call(plot,"VisibleSamples",start)==1,"BLE arrival exposed the entire burst at once");
                Need((int)Call(plot,"VisibleSamples",start+Stopwatch.Frequency*16/1000)==9,"Display did not release original samples at500Hz");
                Need((int)Call(plot,"VisibleSamples",start+Stopwatch.Frequency)==rows,"Paced display omitted buffered originals");
                var samples=(short[])((short[])Get(plot,"values")).Clone();var ticks=(ulong[])((ulong[])Get(plot,"ticks")).Clone();
                radio.Emit(compact?Compact(hz,4+(ulong)rows*step):Packet(hz,4+(ulong)rows*step));Tick(form);
                Need((long)Get(plot,"playbackStarted")==start,"Every BLE burst restarted the display clock");
                Need((int)Call(plot,"VisibleSamples",start+Stopwatch.Frequency*16/1000)==9,"Next packet jumped the playhead or revealed future samples");
                for(int row=0;row<rows;row++)Need(((short[])Get(plot,"values"))[row]==samples[row] && ((ulong[])Get(plot,"ticks"))[row]==ticks[row],"Pacing resampled or changed an original sample/timestamp");
                Need(plot.Handle!=IntPtr.Zero,"Offscreen plot handle unavailable");int paints=0;plot.Invalidated+=delegate{paints++;};Set(form,"busy",true);Tick(form);
                Need(paints>0 && (bool)Get(form,"busy") && radio.Writes==0 && radio.Captures==0,"Paced refresh stalled behind controls or generated radio work");
                Call(plot,"Clear",0);
                var sparse=new ushort[64,1];for(int i=0;i<64;i++)sparse[i,0]=2048;
                Call(plot,"Append",sparse,4UL,step,(1UL<<0)|(1UL<<32)|(1UL<<63));start=(long)Get(plot,"playbackStarted");
                Need((int)Call(plot,"VisibleSamples",start+Stopwatch.Frequency*32/1000)==1,"Pacing filled an invalid-row interval");
                Need((int)Call(plot,"VisibleSamples",start+Stopwatch.Frequency*64/1000)==2,"Pacing used arrival count instead of source timestamps");
                Need((int)Call(plot,"VisibleSamples",start+Stopwatch.Frequency)==3,"Sparse original samples disappeared");
            }
        }
    }
    static void RecordingHeader(){
        var radio=new NpBleConnection();using(var form=Arm(radio,20000,compact:true)){
            var header=(Label)Get(form,"recordingInfo");
            Need(!OwnVisible(header),"Missing recording-info provider left an empty strip");
            Need(header.Dock==DockStyle.Top && header.Height==24 && header.AutoEllipsis,"Recording info is not a compact top strip");
            Need(form.Controls.GetChildIndex(header)>form.Controls.GetChildIndex(((NumericUpDown)Get(form,"probe")).Parent),"Recording info is not docked above preview controls");
            string text="Recording | 00:00:12 | committed3MB | available100GB";int reads=0;
            form.RecordingInfo=()=>{reads++;return text;};Tick(form);
            Need(OwnVisible(header) && header.Text==text && reads==1,"Control-busy preview did not refresh cached recording info");
            Set(form,"busy",true);text="Recording | 00:00:13 | committed4MB | available99GB";Tick(form);
            Need(header.Text==text && reads==2 && (bool)Get(form,"busy"),"In-flight preview froze status or stole wire ownership");
            Set(form,"busy",false);((CheckBox)Get(form,"enabled")).Checked=false;
            text="Stopped | 00:00:14 | committed5MB | available99GB";Tick(form);
            Need(header.Text==text && reads==3 && OwnVisible(header),"Paused preview hid recording status behind the readiness gate");
            text="  ";Tick(form);Need(!OwnVisible(header) && header.Text.Length==0,"Whitespace recording info left a blank strip");
            text=String.Empty;Tick(form);Need(!OwnVisible(header),"Empty recording info left a blank strip");
            form.RecordingInfo=null;Tick(form);Need(!OwnVisible(header),"Removed recording-info provider left stale text visible");
            form.RecordingInfo=()=>{throw new IOException("Cached status unavailable");};Tick(form);
            Need(OwnVisible(header) && header.Text.Contains("Cached status unavailable"),"Recording-info failure escaped the display or hid its reason");
            Need(radio.Writes==0 && radio.Captures==0,"Recording-info display generated device traffic");
            Set(form,"closed",true);form.RecordingInfo=()=>{reads++;return text;};Tick(form);
            Need(reads==5,"Closed preview continued reading recording info");
        }
    }
    [STAThread]static int Main(string[] args){try{
        Application.EnableVisualStyles();bool baseline=args.Length!=0 && args[0]=="--baseline";
        BusyDrain(baseline);if(baseline){Console.WriteLine("PASS: frozen production form reproduces control-busy display stall; no radio opened.");return 0;}
        Profiles();WindowAndGaps();CompactProtocol();LegacyDecode();WireAndAdmission();CompactDisplay();Compatibility();SelectionSwitch();InFlightSelection();CalibrationPressure();TripleRecordingRetry();RetryDuringLeaseWrite();ControlsAfterPhase();RecordingHeader();StableSweep(args.Length==0?null:args[0]);PacedSweep();NpBleMultiPreviewDisplayTests.Run(args.Length==0?null:args[0]);Console.WriteLine("PASS "+checks+" actual-source BLE compact/legacy protocol/display/control/window/gap/compatibility/selection-lease/calibration-pressure/triple20-recovery/recording-header/fixed-paced-sweep checks; fake radio only, no discovery, device or recording.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
