using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    // RX only admits bounded raw packets. Decoding/painting belongs to the consumer.
    internal sealed class NpBlePreviewStream : IDisposable
    {
        readonly NpBleConnection connection;
        readonly uint id,probe,channel,session,hz,kind;
        readonly int epoch;
        readonly Queue<NpV2PreviewBurst> pending=new Queue<NpV2PreviewBurst>();
        readonly object gate=new object();
        bool disposed;Exception error;
        internal int Dropped {get;private set;}
        internal readonly Stopwatch Silence=Stopwatch.StartNew();
        internal NpBlePreviewStream(NpBleConnection source,uint request,uint selectedProbe,uint selectedChannel,uint expectedSession,uint expectedHz)
            : this(source,request,selectedProbe,selectedChannel,expectedSession,expectedHz,2) { }
        internal NpBlePreviewStream(NpBleConnection source,uint request,uint selectedProbe,uint selectedChannel,uint expectedSession,uint expectedHz,uint expectedKind)
        {
            connection=source;epoch=source.Epoch;id=request;probe=selectedProbe;channel=selectedChannel;session=expectedSession;hz=expectedHz;kind=expectedKind;
            connection.PreviewReceived+=Receive;
        }
        internal void Receive(int generation,byte[] body)
        {
            lock(gate){
                if(disposed || generation!=epoch)return;
                try{
                    var burst=new NpV2PreviewBurst(id);if(!burst.Accept(body))return;
                    if(burst.Kind!=kind || burst.Probe!=probe || burst.Channel!=channel || burst.Session!=session || burst.Hz!=hz)
                        throw new IOException("Preview session/rate/selection changed; reopen preview.");
                    if(pending.Count==8){pending.Dequeue();Dropped++;}
                    pending.Enqueue(burst);Silence.Restart();
                }catch(Exception ex){error=ex;}
            }
        }
        internal List<NpV2PreviewBurst> Drain()
        {
            lock(gate){if(error!=null)throw error;var result=new List<NpV2PreviewBurst>(pending);pending.Clear();return result;}
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;pending.Clear();}connection.PreviewReceived-=Receive;}
    }

    internal sealed class NpBlePreviewForm : Form
    {
        const int MaxWindowSeconds=10;
        const int LeaseRetireMilliseconds=2250; // Firmware lease2s, plus host scheduling margin.
        readonly NpBleConnection connection;
        readonly Func<NpSdStatus> status;
        readonly Func<bool> controlsBusy;
        readonly NpCalibrationSet factoryCalibration;
        internal Func<string> RecordingInfo;
        readonly Label recordingInfo=new Label{Dock=DockStyle.Top,Height=24,Padding=new Padding(10,3,10,0),AutoEllipsis=true,Visible=false};
        readonly NumericUpDown probe=new NumericUpDown{Minimum=1,Maximum=2,Value=1,Width=55};
        readonly NumericUpDown channel=new NumericUpDown{Minimum=0,Maximum=383,Width=65};
        readonly CheckBox enabled=new CheckBox{Text="Preview",AutoSize=true,Checked=true};
        readonly NumericUpDown windowSeconds=new NumericUpDown{Minimum=0.1M,Maximum=MaxWindowSeconds,DecimalPlaces=1,Increment=0.1M,Value=4,Width=65,AccessibleName="BLE preview display window in seconds"};
        readonly NumericUpDown gainBox=new NumericUpDown{Minimum=0.1M,Maximum=100,DecimalPlaces=1,Increment=0.1M,Value=1,Width=65,AccessibleName="Preview display gain",AccessibleDescription="Display only; does not change probe gain or saved data."};
        readonly NpHighPassControl highPass=new NpHighPassControl();
        readonly Label info=new Label{AutoSize=true,MaximumSize=new Size(800,0)};
        readonly Plot plot=new Plot{Dock=DockStyle.Fill};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=16};
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly Stopwatch heartbeat=Stopwatch.StartNew(),retry=Stopwatch.StartNew();
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly int epoch;
        NpBlePreviewStream stream;
        int phase;uint session,hz,activeProbe,activeChannel,request,previewHz;
        bool busy,closed,legacyPreview;
        int selectionGeneration;
        long rearmAfter;
        ulong previousEnd;long gaps,skipped;int packets;
        static int sequence=Environment.TickCount;
        static uint Next(){uint value=unchecked((uint)Interlocked.Increment(ref sequence));return value==0?Next():value;}
        internal NpBlePreviewForm(NpBleConnection source,Func<NpSdStatus> readStatus,Func<bool> ownerBusy)
            : this(source,readStatus,ownerBusy,null) { }
        internal NpBlePreviewForm(NpBleConnection source,Func<NpSdStatus> readStatus,Func<bool> ownerBusy,NpCalibrationSet calibration)
        {
            connection=source;epoch=source.Epoch;status=readStatus;controlsBusy=ownerBusy;
            factoryCalibration=calibration==null?null:calibration.Copy();
            Text="WILD-X — BLE Preview";Icon=NpPreviewTheme.ApplicationIcon;ClientSize=new Size(850,550);MinimumSize=new Size(700,450);
            StartPosition=FormStartPosition.CenterParent;
            var controls=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(10)};
            highPass.SampleRate=0;highPass.CutoffBox.Text="100";
            highPass.Changed+=delegate{plot.SetHighPass(highPass.CutoffHz);};
            windowSeconds.ValueChanged+=delegate{plot.SetWindowSeconds((double)windowSeconds.Value);};
            gainBox.ValueChanged+=delegate{plot.SetGain((double)gainBox.Value);};
            controls.Controls.AddRange(new Control[]{new Label{Text="Probe",AutoSize=true},probe,new Label{Text="Channel",AutoSize=true},channel,enabled,new Label{Text="Window (s)",AutoSize=true},windowSeconds,new Label{Text="Gain (×)",AutoSize=true},gainBox,highPass,info});
            Controls.Add(plot);Controls.Add(controls);Controls.Add(recordingInfo);WildXTheme.Apply(this);info.ForeColor=recordingInfo.ForeColor=WildXTheme.Muted;
            probe.ValueChanged+=delegate{RetirePreview();};channel.ValueChanged+=delegate{RetirePreview();};
            enabled.CheckedChanged+=delegate{RetirePreview();};
            timer.Tick+=async delegate{await Tick();};Shown+=delegate{timer.Start();};
        }
        bool Current(uint rate)
        {
            var s=status();return !closed && enabled.Checked && connection.Connected && connection.Epoch==epoch &&
                NpV2SdControl.CanPreview(s) && (s.Mask!=1 || probe.Value==1) && (rate==0 || s.Hz==rate);
        }
        void ResetDisplayRate(){previewHz=0;plot.SetSampleRate(0);highPass.SampleRate=0;}
        void DropStream(){var old=stream;stream=null;session=hz=previewHz=0;previousEnd=0;if(old!=null)old.Dispose();}
        void RetirePreview(){
            selectionGeneration++;
            if(stream!=null || busy)rearmAfter=Math.Max(rearmAfter,clock.ElapsedMilliseconds+LeaseRetireMilliseconds);
            DropStream();ResetDisplayRate();retry.Restart();
            info.Text=enabled.Checked?"Waiting to preview the selected channel…":"Paused";
        }
        bool SelectionCurrent(int generation,uint rate){return generation==selectionGeneration && Current(rate) && probe.Value==activeProbe && channel.Value==activeChannel;}
        void RefreshRecordingInfo(){
            try{
                string text=RecordingInfo==null?null:RecordingInfo();
                recordingInfo.Text=String.IsNullOrWhiteSpace(text)?String.Empty:text;
                recordingInfo.Visible=recordingInfo.Text.Length!=0;
            }catch(Exception ex){recordingInfo.Text="Recording information unavailable: "+ex.Message;recordingInfo.Visible=true;}
        }
        protected override void OnFormClosing(FormClosingEventArgs e){closed=true;lifetime.Cancel();DropStream();base.OnFormClosing(e);}
        protected override void Dispose(bool disposing)
        {
            if(disposing && !closed){closed=true;lifetime.Cancel();DropStream();}
            if(disposing){timer.Stop();timer.Dispose();lifetime.Dispose();}
            base.Dispose(disposing);
        }
        async Task Tick()
        {
            if(closed)return;
            RefreshRecordingInfo(); // Cached PC state only; never waits for BLE/control ownership.
            if(!Current(hz)){if(stream!=null || busy || session!=0)RetirePreview();info.Text=enabled.Checked?"Waiting for recording…":"Paused";return;}
            // Already-received display data never waits for a status/time RPC
            // or an in-flight preview write. Only new wire work yields to controls.
            try{DrainPreview();}
            catch(NpV2PreviewUnavailableException ex){
                // Only a matching compact request's explicit unsupported reply
                // permits one legacy mode switch during this connection epoch.
                if(ex.Reason==1 && !legacyPreview){
                    var old=stream;stream=null;if(old!=null)old.Dispose();legacyPreview=true;previousEnd=0;
                    ResetDisplayRate();retry.Restart();info.Text="Compact preview unsupported; waiting to request legacy preview…";return;
                }
                enabled.Checked=false;info.Text=ex.Message;return;
            }
            catch(Exception ex){enabled.Checked=false;info.Text=ex.Message;return;}
            if(busy || controlsBusy())return;
            busy=true;
            int generation=selectionGeneration;
            try{
                var token=lifetime.Token;
                if(stream==null){
                    if(clock.ElapsedMilliseconds<rearmAfter){info.Text="Waiting for the previous preview lease to expire…";return;}
                    if(retry.ElapsedMilliseconds<(legacyPreview?550:1000))return;
                    if(session==0){
                        activeProbe=(uint)probe.Value;activeChannel=(uint)channel.Value;hz=status().Hz;
                        info.Text="Reading probe framing…";
                        var calibration=await connection.CapturePreview(Next(),activeProbe,activeChannel,hz,token);
                        if(!SelectionCurrent(generation,hz))return;
                        int capturedPhase=await Task.Run(()=>calibration.FindPhase(),token);
                        if(!SelectionCurrent(generation,hz))return;
                        phase=capturedPhase;session=calibration.Session;
                        await Task.Delay(550,token); // Firmware preview admission interval after calibration.
                    }
                    if(!SelectionCurrent(generation,hz))return;
                    if(controlsBusy()){DropStream();return;}
                    plot.Clear((int)activeChannel);gaps=skipped=0;packets=0;
                    var profile=factoryCalibration==null?null:factoryCalibration.Get((int)activeProbe);
                    plot.CorrectSample=profile==null?null:(Func<int,short,short>)((ch,value)=>profile.Correct(value,ch));
                    uint kind=legacyPreview?2U:5U;
                    request=Next();stream=new NpBlePreviewStream(connection,request,activeProbe,activeChannel,session,hz,kind);
                    bool sent=await connection.TryWritePreview(NpV2PreviewBurst.Request(request,kind,activeProbe,activeChannel,(uint)phase,session),epoch,token);
                    if(!SelectionCurrent(generation,hz))return;
                    if(!sent){DropStream();retry.Restart();return;}
                    if(stream==null)return;
                    heartbeat.Restart();
                }
                if(heartbeat.ElapsedMilliseconds>=750){
                    bool sent=await connection.TryWritePreview(NpV2PreviewBurst.Request(request,4,activeProbe,activeChannel,(uint)phase,session),epoch,token);
                    if(!SelectionCurrent(generation,hz))return;
                    if(sent)heartbeat.Restart();
                }
            }catch(OperationCanceledException){if(!closed && generation==selectionGeneration)enabled.Checked=false;}
            catch(Exception ex){if(!closed && generation==selectionGeneration){enabled.Checked=false;info.Text=ex.Message;}}
            finally{
                // An already-submitted old request can finish after selection
                // changes. Start the quiet lease window only after it retires.
                if(generation!=selectionGeneration)rearmAfter=Math.Max(rearmAfter,clock.ElapsedMilliseconds+LeaseRetireMilliseconds);
                busy=false;
            }
        }
        void DrainPreview()
        {
            if(stream==null)return;
            // At most eight disposable bursts: bounded PC-only work,
            // avoiding a thread-pool round trip on every display timer tick.
            foreach(var frame in stream.Drain()){
                if(previousEnd!=0 && frame.FirstTick<previousEnd)continue;
                var decoded=frame.DecodeRolling(phase);
                if(!Current(hz) || probe.Value!=activeProbe || channel.Value!=activeChannel){DropStream();return;}
                if(previewHz!=frame.PreviewHz){
                    previewHz=frame.PreviewHz;plot.SetSampleRate(previewHz);highPass.SampleRate=previewHz;plot.SetHighPass(highPass.CutoffHz);
                }
                ulong stride=frame.SourceStep;
                if(previousEnd!=0 && frame.FirstTick!=previousEnd)gaps++;
                for(int n=0;n<frame.Rows;n++)if((frame.ValidRows&(1UL<<n))==0)skipped++;
                previousEnd=frame.FirstTick+(ulong)frame.Rows*stride;packets++;
                plot.Append(decoded,frame.FirstTick,stride,frame.ValidRows);
            }
            plot.Invalidate(); // Reveal buffered originals between BLE bursts, even while controls own the radio.
            info.Text=previewHz+" samples/s grid · "+(legacyPreview?"legacy 16-row transport (gaps expected)":"compact 64-row transport")+
                " · subsampled, aliasing possible · gaps "+gaps+" · skipped "+skipped+" · host drops "+stream.Dropped+" · packets "+packets;
            if(stream.Silence.ElapsedMilliseconds>2500)throw new NpV2PreviewUnavailableException(128);
        }
        // BLE wire stays raw. Convert only the disposable PC display samples.
        sealed class Plot : Control
        {
            const int Capacity=500*MaxWindowSeconds; // Current wire profiles are bounded at 500 samples/s.
            readonly short[] values=new short[Capacity];readonly ulong[] ticks=new ulong[Capacity];
            readonly NpDisplayHighPass highPass=new NpDisplayHighPass(1);
            readonly short[] displayRow=new short[1];
            internal Func<int,short,short> CorrectSample;
            int count,write,selectedChannel;uint sampleHz;ulong stride,playbackOrigin;long playbackStarted;double windowSeconds=4,gain=1;
            const double ReferenceCounts=256; // Match the USB NP2 display at 1×.
            internal Plot(){DoubleBuffered=true;BackColor=WildXTheme.Dark;ForeColor=Color.LightGreen;}
            internal void SetGain(double value){if(Double.IsNaN(value)||Double.IsInfinity(value)||value<0.1||value>100)throw new ArgumentOutOfRangeException("value");gain=value;Invalidate();}
            internal float ValueY(double value){return (float)(Height/2.0-value*Math.Max(1,Height-60)*gain/(2*ReferenceCounts));}
            internal void SetWindowSeconds(double seconds){
                if(Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds<0.1 || seconds>MaxWindowSeconds)throw new ArgumentOutOfRangeException("seconds");
                windowSeconds=seconds;Invalidate(); // View only; retain history and the BLE lease.
            }
            internal void SetSampleRate(uint rate){sampleHz=rate;Clear(selectedChannel);}
            internal void SetHighPass(double cutoff){if(sampleHz!=0)highPass.Configure(sampleHz,cutoff);Clear(selectedChannel);}
            internal void Clear(int channel){count=write=0;selectedChannel=channel;highPass.Reset();Invalidate();}
            internal void Append(ushort[,] data,ulong first,ulong step,ulong valid)
            {
                // One received burst supplies the display's initial lead. Pace its
                // original samples at the declared Fs; never interpolate a gap.
                if(count==0){playbackOrigin=first;playbackStarted=Stopwatch.GetTimestamp();}
                stride=step;for(int i=0;i<data.GetLength(0);i++){
                    if((valid&(1UL<<i))==0)continue;ticks[write]=first+(ulong)i*step;
                    short value=NpV2SampleDecoder.NominalCount(data[i,data.GetLength(1)==1?0:selectedChannel/128]);
                    displayRow[0]=CorrectSample==null?value:CorrectSample(selectedChannel,value);
                    highPass.Apply(displayRow,0,displayRow,0,checked((long)(ticks[write]/step)));
                    values[write]=displayRow[0];write=(write+1)%Capacity;if(count<Capacity)count++;
                }Invalidate();
            }
            internal int VisibleSamples(long now){
                if(count==0 || stride==0 || sampleHz==0)return 0;
                double elapsedSamples=Math.Max(0,(now-playbackStarted)/(double)Stopwatch.Frequency*sampleHz);
                int visible=count,start=(write-count+Capacity)%Capacity;
                while(visible>0 && (ticks[(start+visible-1)%Capacity]-playbackOrigin)/(double)stride>elapsedSamples)visible--;
                return visible;
            }
            double WindowTicks {get{return Math.Max(1,Math.Round(windowSeconds*sampleHz))*stride;}}
            internal float XForTick(ulong tick){return (float)(15+(tick%WindowTicks)/WindowTicks*Math.Max(1,Width-30));}
            static void DrawSegment(Graphics graphics,Pen pen,List<PointF> segment){
                if(segment.Count>1)graphics.DrawLines(pen,segment.ToArray());
                else if(segment.Count==1)graphics.FillRectangle(Brushes.LightGreen,segment[0].X,segment[0].Y,1,1);
                segment.Clear();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.DrawString("Ch "+selectedChannel+(CorrectSample==null?" · nominal counts ±":" · calibrated counts ±")+(ReferenceCounts/gain).ToString("0.##")+" · "+gain.ToString("0.#")+"×",Font,Brushes.LightGray,10,5);
                using(var zero=new Pen(Color.DimGray)){zero.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;e.Graphics.DrawLine(zero,15,Height/2,Math.Max(15,Width-15),Height/2);}
                int visible=VisibleSamples(Stopwatch.GetTimestamp());if(visible==0)return;
                ulong latest=ticks[(write-count+visible-1+Capacity)%Capacity];double window=WindowTicks;
                int first=FirstVisible(latest,window),shown=visible-first,start=(write-count+first+Capacity)%Capacity;
                var segment=new List<PointF>();ulong prev=0;float previousX=0;
                var saved=e.Graphics.Save();e.Graphics.SetClip(new Rectangle(15,30,Math.Max(1,Width-30),Math.Max(1,Height-60)));
                try{
                using(var pen=new Pen(Color.LightGreen)){
                    for(int i=0;i<shown;i++){
                        int at=(start+i)%Capacity;ulong tick=ticks[at];if(latest-tick>window)continue;
                        // Absolute source ticks keep old samples fixed on screen.
                        // Break at missing data AND sweep wrap; never invent a join.
                        float x=XForTick(tick);
                        if(segment.Count>0 && (tick-prev!=stride || x<previousX))DrawSegment(e.Graphics,pen,segment);
                        segment.Add(new PointF(x,ValueY(values[at])));prev=tick;previousX=x;
                    }
                    DrawSegment(e.Graphics,pen,segment);
                    using(var cursor=new Pen(Color.FromArgb(100,Color.LightGreen))){
                        float x=XForTick(latest+stride);e.Graphics.DrawLine(cursor,x,30,x,Height-30);
                    }
                }
                }finally{e.Graphics.Restore(saved);}
            }
            int FirstVisible(ulong latest,double window){
                int first=0,start=(write-count+Capacity)%Capacity;
                while(first<count && latest-ticks[(start+first)%Capacity]>window)first++;
                return first;
            }
        }
    }
}
