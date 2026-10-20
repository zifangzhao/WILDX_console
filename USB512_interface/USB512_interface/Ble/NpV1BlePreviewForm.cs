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
    internal sealed class NpV1BlePreviewForm : Form
    {
        const int MaxWindowSeconds=10;
        readonly NpBleConnection connection;
        readonly Func<NpSdStatus> status;
        readonly Func<bool> controlsBusy;
        readonly NpCalibrationSet calibration;
        readonly int epoch;
        internal Func<string> RecordingInfo;
        readonly Label recordingInfo=new Label{Dock=DockStyle.Top,Height=24,Padding=new Padding(10,3,10,0),AutoEllipsis=true,Visible=false};
        readonly NumericUpDown probe=new NumericUpDown{Minimum=1,Maximum=3,Value=1,Width=55};
        readonly NumericUpDown channel=new NumericUpDown{Minimum=0,Maximum=383,Width=65};
        readonly ComboBox band=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=75};
        readonly CheckBox enabled=new CheckBox{Text="Preview",AutoSize=true,Checked=true};
        readonly NumericUpDown windowSeconds=new NumericUpDown{Minimum=0.1M,Maximum=MaxWindowSeconds,DecimalPlaces=1,Increment=0.1M,Value=4,Width=65,AccessibleName="BLE preview display window in seconds"};
        readonly NumericUpDown gainBox=new NumericUpDown{Minimum=0.1M,Maximum=100,DecimalPlaces=1,Increment=0.1M,Value=1,Width=65,AccessibleName="Preview display gain",AccessibleDescription="Display only; does not change probe gain or saved data."};
        readonly Label info=new Label{AutoSize=true,MaximumSize=new Size(800,0)};
        readonly ToolTip diagnostics=new ToolTip();
        readonly Plot plot=new Plot{Dock=DockStyle.Fill};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=50};
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly Stopwatch renew=Stopwatch.StartNew(),silence=Stopwatch.StartNew();
        readonly Queue<NpV1PreviewBurst> pending=new Queue<NpV1PreviewBurst>();
        readonly object gate=new object();
        static int sequence=Environment.TickCount;
        bool busy,closed;uint activeId,activeProbe,activeChannel,activeBand,session,hz,phase;
        int selectionVersion,activeSelection;
        Exception error;int dropped,packets;ulong previousEnd;long gaps;
        static uint Next(){uint id=unchecked((uint)Interlocked.Increment(ref sequence));return id==0?Next():id;}
        internal static bool CanPreview(NpSdStatus s){return s!=null && s.State==1 && (s.Flags&(8|NpV1BleProtocol.Waiting|NpV1BleProtocol.Preparing))==0 && s.Fault==0 && s.Mask!=0 && (s.Flags&NpV1BleProtocol.Preview)!=0;}
        internal NpV1BlePreviewForm(NpBleConnection source,Func<NpSdStatus> readStatus,Func<bool> ownerBusy,NpCalibrationSet corrections)
        {
            connection=source;epoch=source.Epoch;status=readStatus;controlsBusy=ownerBusy;calibration=corrections==null?null:corrections.Copy();
            Text="WILD-X — V1 BLE Preview";Icon=NpPreviewTheme.ApplicationIcon;ClientSize=new Size(850,550);MinimumSize=new Size(700,450);StartPosition=FormStartPosition.CenterParent;
            band.Items.AddRange(new object[]{"AP","LFP"});band.SelectedIndex=0;
            var active=readStatus==null?null:readStatus();
            if(active!=null)for(int port=1;port<=3;port++)if((active.Mask&(1U<<(port-1)))!=0){probe.Value=port;break;}
            var controls=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(10)};
            controls.Controls.AddRange(new Control[]{new Label{Text="Probe",AutoSize=true},probe,new Label{Text="Channel",AutoSize=true},channel,band,enabled,new Label{Text="Window (s)",AutoSize=true},windowSeconds,new Label{Text="Gain (×)",AutoSize=true},gainBox,info});
            Controls.Add(plot);Controls.Add(controls);Controls.Add(recordingInfo);WildXTheme.Apply(this);info.ForeColor=recordingInfo.ForeColor=WildXTheme.Muted;
            probe.ValueChanged+=delegate{SelectionChanged();};channel.ValueChanged+=delegate{SelectionChanged();};band.SelectedIndexChanged+=delegate{SelectionChanged();};
            windowSeconds.ValueChanged+=delegate{plot.SetWindowSeconds((double)windowSeconds.Value);};
            gainBox.ValueChanged+=delegate{plot.SetGain((double)gainBox.Value);};
            connection.PreviewReceived+=Receive;timer.Tick+=async delegate{await Tick();};Shown+=delegate{timer.Start();};
        }
        void SelectionChanged(){selectionVersion++;plot.Clear(0);info.Text=enabled.Checked?"Switching preview…":"Paused";}
        void RefreshRecordingInfo(){try{string text=RecordingInfo==null?null:RecordingInfo();recordingInfo.Text=String.IsNullOrWhiteSpace(text)?String.Empty:text;recordingInfo.Visible=recordingInfo.Text.Length!=0;}catch(Exception ex){recordingInfo.Text="Recording information unavailable: "+ex.Message;recordingInfo.Visible=true;}}
        bool Current(){var s=status();return !closed && enabled.Checked && connection.Connected && connection.Epoch==epoch && CanPreview(s) && (s.Mask&(1U<<((int)probe.Value-1)))!=0 && (activeId==0 || s.Hz==hz);}
        void Receive(int generation,byte[] body)
        {
            lock(gate){
                if(closed || generation!=epoch || activeId==0)return;
                try{var frame=new NpV1PreviewBurst(activeId);if(!frame.Accept(body))return;
                    if(frame.Kind!=2 || frame.Probe!=activeProbe || frame.Channel!=activeChannel || frame.Band!=activeBand || frame.Session!=session || frame.Hz!=hz)throw new IOException("Preview session/selection changed; reopen preview.");
                    if(pending.Count==8){pending.Dequeue();dropped++;}pending.Enqueue(frame);silence.Restart();
                }catch(Exception ex){error=ex;}
            }
        }
        async Task<NpV1PreviewBurst> Snapshot(uint p,uint c,uint b,uint rate)
        {
            var burst=new NpV1PreviewBurst(Next());var done=new TaskCompletionSource<NpV1PreviewBurst>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<int,byte[]> receive=(generation,body)=>{
                if(generation!=epoch)return;
                try{if(burst.Accept(body)){
                    if(burst.Kind!=0 || burst.Probe!=p || burst.Channel!=c || burst.Band!=b || burst.Hz!=rate)throw new IOException("Preview calibration selection/rate changed.");
                    done.TrySetResult(burst);
                }}catch(Exception ex){done.TrySetException(ex);}
            };
            connection.PreviewReceived+=receive;
            try{
                if(!await connection.TryWritePreview(NpV1PreviewBurst.Request(burst.Id,0,p,c,0,0,b),epoch,lifetime.Token))throw new IOException("Controls busy; preview deferred.");
                if(await Task.WhenAny(done.Task,Task.Delay(5000,lifetime.Token))!=done.Task){lifetime.Token.ThrowIfCancellationRequested();throw new IOException("No complete origin snapshot; recording pressure may suppress preview.");}
                var result=await done.Task;if(!Current())throw new IOException("Preview connection/recording changed.");return result;
            }finally{connection.PreviewReceived-=receive;}
        }
        async Task StopPreview(bool waitForExpiry=false)
        {
            uint old;lock(gate){old=activeId;activeId=0;pending.Clear();}
            if(old==0)return;
            bool sent=false;
            try{sent=await connection.TryWritePreview(NpV1PreviewBurst.Request(old,3,activeProbe,activeChannel,phase,session,activeBand),epoch,CancellationToken.None);}catch{ /* The device's two-second preview lease expires independently. */ }
            // Preview writes have no device ACK. Before changing selection,
            // let the old lease expire even after a successful host write.
            if((waitForExpiry || !sent) && !closed)await Task.Delay(2100,lifetime.Token);
        }
        async Task Tick()
        {
            if(closed)return;RefreshRecordingInfo();if(busy)return;busy=true;int generation=selectionVersion;
            try{
                if(!Current()){await StopPreview();info.Text=enabled.Checked?"Waiting for an active probe recording…":"Paused";return;}
                if(activeId!=0 && activeSelection!=generation){await StopPreview(true);info.Text="Waiting for new preview selection…";return;}
                if(controlsBusy())return;
                if(activeId==0){
                    uint p=(uint)probe.Value,c=(uint)channel.Value,b=(uint)band.SelectedIndex,rate=status().Hz;
                    info.Text="Preparing preview…";var origin=await Snapshot(p,c,b,rate);uint ph=(uint)await Task.Run(()=>origin.FindPhase(),lifetime.Token);
                    if(generation!=selectionVersion || !Current() || controlsBusy() || probe.Value!=p || channel.Value!=c || band.SelectedIndex!=b)return;
                    lock(gate){activeProbe=p;activeChannel=c;activeBand=b;activeSelection=generation;phase=ph;session=origin.Session;hz=origin.Hz;error=null;pending.Clear();dropped=packets=0;previousEnd=0;gaps=0;activeId=Next();silence.Restart();}
                    if(!await connection.TryWritePreview(NpV1PreviewBurst.Request(activeId,2,p,c,ph,session,b),epoch,lifetime.Token))throw new IOException("Controls busy; preview deferred.");
                    plot.Clear(hz*780.0);renew.Restart();
                }
                if(generation!=selectionVersion)return;
                if(renew.ElapsedMilliseconds>=750 && await connection.TryWritePreview(NpV1PreviewBurst.Request(activeId,4,activeProbe,activeChannel,phase,session,activeBand),epoch,lifetime.Token))renew.Restart();
                if(generation!=selectionVersion)return;
                List<NpV1PreviewBurst> frames;int hostDrops;long idle;
                lock(gate){if(error!=null)throw error;frames=new List<NpV1PreviewBurst>(pending);pending.Clear();hostDrops=dropped;idle=silence.ElapsedMilliseconds;}
                foreach(var frame in frames){
                    if(previousEnd!=0 && frame.FirstTick<previousEnd)continue;
                    if(previousEnd!=0 && frame.FirstTick!=previousEnd)gaps++;
                    var samples=frame.DecodeRolling(phase);var profile=calibration==null?null:calibration.Get((int)activeProbe);
                    for(int i=0;i<samples.Length;i++)if(!Double.IsNaN(samples[i])){short nominal=(short)(512-samples[i]);samples[i]=profile==null?nominal:profile.Correct(nominal,(int)activeChannel,activeBand==1);}
                    plot.Append(samples,frame.FirstTick,frame.Stride);previousEnd=frame.FirstTick+16UL*frame.Stride;packets++;
                }
                info.Text=(activeBand==0?"AP":"LFP")+" preview · "+(hz*780.0/((activeBand==0?((hz+499)/500)*780:((hz+5999)/6000)*9360))).ToString("F1")+" samples/s"+(gaps!=0 || hostDrops!=0?" · Preview gaps":"");
                diagnostics.SetToolTip(info,"Subsampled preview · gaps "+gaps+" · host drops "+hostDrops+" · packets "+packets);
                if(idle>2500)throw new IOException("Preview expired. Recording continues independently; toggle Preview to retry.");
            }catch(OperationCanceledException){}
            catch(Exception ex){if(!closed && generation==selectionVersion){enabled.Checked=false;await StopPreview();info.Text=ex.Message;}}
            finally{busy=false;}
        }
        void ClosePreview(){if(closed)return;closed=true;lifetime.Cancel();timer.Stop();connection.PreviewReceived-=Receive;ObserveStop();}
        async void ObserveStop(){await StopPreview();}
        protected override void OnFormClosing(FormClosingEventArgs e){ClosePreview();base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){if(disposing){ClosePreview();timer.Dispose();diagnostics.Dispose();}base.Dispose(disposing);}
        sealed class Plot : Control
        {
            const int Capacity=500*MaxWindowSeconds;readonly double[] values=new double[Capacity];readonly ulong[] ticks=new ulong[Capacity];
            int count,write;double ticksPerSecond,windowSeconds=4,gain=1;ulong stride;
            const double ReferenceCounts=64; // Match the USB NP1 AP display at 1×.
            internal Plot(){DoubleBuffered=true;BackColor=WildXTheme.Dark;}
            internal void SetGain(double value){if(Double.IsNaN(value)||Double.IsInfinity(value)||value<0.1||value>100)throw new ArgumentOutOfRangeException("value");gain=value;Invalidate();}
            internal float ValueY(double value){return (float)(Height/2.0-value*Math.Max(1,Height-60)*gain/(2*ReferenceCounts));}
            internal void SetWindowSeconds(double seconds){if(Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds<0.1 || seconds>MaxWindowSeconds)throw new ArgumentOutOfRangeException("seconds");windowSeconds=seconds;Invalidate();}
            internal void Clear(double rate){count=write=0;ticksPerSecond=rate;Invalidate();}
            internal void Append(double[] data,ulong first,ulong step){stride=step;for(int i=0;i<data.Length;i++){values[write]=data[i];ticks[write]=first+(ulong)i*step;write=(write+1)%Capacity;if(count<Capacity)count++;}Invalidate();}
            protected override void OnPaint(PaintEventArgs e){
                base.OnPaint(e);
                e.Graphics.DrawString("Counts ±"+(ReferenceCounts/gain).ToString("0.##")+" · "+gain.ToString("0.#")+"× · "+windowSeconds.ToString("0.0")+" seconds",Font,Brushes.LightGray,10,5);
                using(var zero=new Pen(Color.DimGray)){zero.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;e.Graphics.DrawLine(zero,10,Height/2,Math.Max(10,Width-10),Height/2);}
                if(count<2 || ticksPerSecond<=0)return;
                int start=(write-count+Capacity)%Capacity;ulong latest=ticks[(write+Capacity-1)%Capacity];double window=ticksPerSecond*windowSeconds;
                PointF previous=new PointF();bool have=false;ulong previousTick=0;
                var saved=e.Graphics.Save();e.Graphics.SetClip(new Rectangle(10,30,Math.Max(1,Width-20),Math.Max(1,Height-60)));
                try{
                using(var pen=new Pen(Color.LightGreen))for(int i=0;i<count;i++){
                    int at=(start+i)%Capacity;double v=values[at];ulong tick=ticks[at];if(latest-tick>window)continue;if(Double.IsNaN(v)){have=false;continue;}
                    var point=new PointF((float)(10+(1-(latest-tick)/window)*Math.Max(1,Width-20)),ValueY(v));
                    if(have && tick-previousTick==stride)e.Graphics.DrawLine(pen,previous,point);previous=point;previousTick=tick;have=true;
                }
                }finally{e.Graphics.Restore(saved);}
            }
        }
    }
}
