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
        const uint PreviewBand=1; // V1 BLE preview is LFP; saved AP data is independent.
        readonly NpBleConnection connection;
        readonly Func<NpSdStatus> status;
        readonly Func<bool> controlsBusy;
        readonly NpCalibrationSet calibration;
        readonly int epoch;
        internal Func<string> RecordingInfo;
        readonly Label recordingInfo=new Label{Dock=DockStyle.Top,Height=24,Padding=new Padding(10,3,10,0),AutoEllipsis=true,Visible=false};
        readonly NumericUpDown probe=new NumericUpDown{Minimum=1,Maximum=3,Value=1,Width=55};
        readonly NumericUpDown channel=new NumericUpDown{Minimum=0,Maximum=383,Width=65};
        readonly NumericUpDown traceCount=new NumericUpDown{Minimum=1,Maximum=4,Value=1,Width=45,AccessibleName="Number of preview channels"};
        readonly NumericUpDown[] extraChannels={new NumericUpDown{Maximum=383,Value=1,Width=65},new NumericUpDown{Maximum=383,Value=2,Width=65},new NumericUpDown{Maximum=383,Value=3,Width=65}};
        readonly Label band=new Label{Text="LFP",AutoSize=true};
        readonly CheckBox enabled=new CheckBox{Text="Preview",AutoSize=true,Checked=true};
        readonly NumericUpDown windowSeconds=new NumericUpDown{Minimum=0.1M,Maximum=MaxWindowSeconds,DecimalPlaces=1,Increment=0.1M,Value=4,Width=65,AccessibleName="BLE preview display window in seconds"};
        readonly NumericUpDown gainBox=new NumericUpDown{Minimum=0.1M,Maximum=100,DecimalPlaces=1,Increment=0.1M,Value=1,Width=65,AccessibleName="Preview display gain",AccessibleDescription="Display only; does not change probe gain or saved data."};
        readonly Label info=new Label{AutoSize=true,MaximumSize=new Size(800,0)};
        readonly ToolTip diagnostics=new ToolTip();
        readonly Plot plot=new Plot{Dock=DockStyle.Fill};
        readonly Plot[] plots;
        readonly TableLayoutPanel traces=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=50};
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly Stopwatch renew=Stopwatch.StartNew(),silence=Stopwatch.StartNew();
        readonly Queue<NpV1PreviewBurst> pending=new Queue<NpV1PreviewBurst>();
        readonly object gate=new object();
        static int sequence=Environment.TickCount;
        // Probe -> (device session, AP Hz, validated origin phase), scoped to this BLE epoch.
        readonly Dictionary<uint,Tuple<uint,uint,int>> phases=new Dictionary<uint,Tuple<uint,uint,int>>();
        bool busy,closed,retryPending,activeFromCache;uint activeId,activeProbe,activeChannel,activeBand,session,hz,phase;
        bool activeCompact;uint[] activeChannels;
        int selectionVersion,activeSelection;
        Exception error;int dropped,packets;ulong previousEnd;long gaps;
        static uint Next(){uint id=unchecked((uint)Interlocked.Increment(ref sequence));return id==0?Next():id;}
        internal static bool CanPreview(NpSdStatus s){return s!=null && s.State==1 && (s.Flags&(8|NpV1BleProtocol.Waiting|NpV1BleProtocol.Preparing))==0 && s.Fault==0 && s.Mask!=0 && (s.Flags&NpV1BleProtocol.Preview)!=0;}
        internal NpV1BlePreviewForm(NpBleConnection source,Func<NpSdStatus> readStatus,Func<bool> ownerBusy,NpCalibrationSet corrections)
        {
            connection=source;epoch=source.Epoch;status=readStatus;controlsBusy=ownerBusy;calibration=corrections==null?null:corrections.Copy();
            plots=new[]{plot,new Plot{Dock=DockStyle.Fill},new Plot{Dock=DockStyle.Fill},new Plot{Dock=DockStyle.Fill}};
            for(int i=0;i<plots.Length;i++){plots[i].Margin=Padding.Empty;traces.RowStyles.Add(new RowStyle(SizeType.Absolute,0));traces.Controls.Add(plots[i],0,i);}
            Text="WILD-X — V1 BLE Preview";Icon=NpPreviewTheme.ApplicationIcon;ClientSize=new Size(850,550);MinimumSize=new Size(700,450);StartPosition=FormStartPosition.CenterParent;

            var active=readStatus==null?null:readStatus();
            // Start with one LFP channel; additional traces are optional BLE traffic.
            if(active!=null)for(int port=1;port<=3;port++)if((active.Mask&(1U<<(port-1)))!=0){probe.Value=port;break;}
            var controls=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(10)};
            controls.Controls.AddRange(new Control[]{new Label{Text="Probe",AutoSize=true},probe,new Label{Text="Traces",AutoSize=true},traceCount,new Label{Text="Channels",AutoSize=true},channel,extraChannels[0],extraChannels[1],extraChannels[2],band,enabled,new Label{Text="Window (s)",AutoSize=true},windowSeconds,new Label{Text="Gain (×)",AutoSize=true},gainBox,info});
            controls.SetFlowBreak(enabled,true);
            Controls.Add(traces);Controls.Add(controls);Controls.Add(recordingInfo);WildXTheme.Apply(this);info.ForeColor=recordingInfo.ForeColor=WildXTheme.Muted;
            probe.ValueChanged+=delegate{SelectionChanged();};channel.ValueChanged+=delegate{SelectionChanged();};
            traceCount.ValueChanged+=delegate{UpdateChannelControls();SelectionChanged();};
            for(int i=0;i<extraChannels.Length;i++){extraChannels[i].AccessibleName="Preview channel "+(i+2);extraChannels[i].ValueChanged+=delegate{SelectionChanged();};}
            UpdateChannelControls();
            SetPlotChannels(SelectedChannels());
            windowSeconds.ValueChanged+=delegate{foreach(var trace in plots)trace.SetWindowSeconds((double)windowSeconds.Value);};
            gainBox.ValueChanged+=delegate{foreach(var trace in plots)trace.SetGain((double)gainBox.Value);};
            connection.PreviewReceived+=Receive;timer.Tick+=async delegate{await Tick();};Shown+=delegate{timer.Start();};
        }
        void SelectionChanged(){retryPending=false;selectionVersion++;ClearPlots(0);SetPlotChannels(SelectedChannels());info.Text=enabled.Checked?"Switching preview…":"Paused";}
        void ClearPlots(double rate){foreach(var trace in plots)trace.Clear(rate);}
        void SetPlotChannels(uint[] channels){for(int i=0;i<plots.Length;i++){bool visible=i<channels.Length;plots[i].Visible=visible;traces.RowStyles[i].SizeType=visible?SizeType.Percent:SizeType.Absolute;traces.RowStyles[i].Height=visible?100F/channels.Length:0;plots[i].ChannelLabel=visible?"Ch "+channels[i]+" · ":String.Empty;}}
        bool CompactSupported(){var s=status==null?null:status();return s!=null && (s.Flags&NpV1BleProtocol.CompactPreview)!=0;}
        void UpdateChannelControls(){bool compact=CompactSupported();traceCount.Enabled=compact;for(int i=0;i<extraChannels.Length;i++)extraChannels[i].Visible=compact && i+1<traceCount.Value;}
        uint[] SelectedChannels(){var channels=new uint[CompactSupported()?(int)traceCount.Value:1];channels[0]=(uint)channel.Value;for(int i=1;i<channels.Length;i++)channels[i]=(uint)extraChannels[i-1].Value;return channels;}
        static bool SameChannels(uint[] expected,ushort[] actual){if(expected==null||actual==null||expected.Length!=actual.Length)return false;for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])return false;return true;}
        byte[] StreamRequest(uint id,uint kind){return activeCompact?NpV1PreviewBurst.CompactRequest(id,kind,activeProbe,activeChannels,phase,session,activeBand):NpV1PreviewBurst.Request(id,kind,activeProbe,activeChannel,phase,session,activeBand);}
        void RefreshRecordingInfo(){try{string text=RecordingInfo==null?null:RecordingInfo();recordingInfo.Text=String.IsNullOrWhiteSpace(text)?String.Empty:text;recordingInfo.Visible=recordingInfo.Text.Length!=0;}catch(Exception ex){recordingInfo.Text="Recording information unavailable: "+ex.Message;recordingInfo.Visible=true;}}
        bool Current(){var s=status();return !closed && enabled.Checked && connection.Connected && connection.Epoch==epoch && CanPreview(s) && (s.Mask&(1U<<((int)probe.Value-1)))!=0 && (activeId==0 || s.Hz==hz);}
        void Receive(int generation,byte[] body)
        {
            lock(gate){
                if(closed || generation!=epoch || activeId==0)return;
                try{var frame=new NpV1PreviewBurst(activeId);if(!frame.Accept(body))return;
                    if(frame.Kind!=(activeCompact?5U:2U) || frame.Probe!=activeProbe || frame.Channel!=activeChannel || frame.Band!=activeBand || frame.Session!=session || frame.Hz!=hz || (activeCompact&&!SameChannels(activeChannels,frame.Channels)))throw new IOException("Preview session/selection changed; reopen preview.");
                    if(pending.Count==8){pending.Dequeue();dropped++;}pending.Enqueue(frame);silence.Restart();
                }catch(Exception ex){error=ex;}
            }
        }
        async Task<NpV1PreviewBurst> Snapshot(uint p,uint c,uint b,uint rate)
        {
            var burst=new NpV1PreviewBurst(Next());var done=new TaskCompletionSource<NpV1PreviewBurst>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sync=new object();bool complete=false,sent=false;var lease=Stopwatch.StartNew();
            Action<int,byte[]> receive=(generation,body)=>{
                if(generation!=epoch)return;
                try{lock(sync){if(burst.Accept(body)){
                    if(burst.Kind!=0 || burst.Probe!=p || burst.Channel!=c || burst.Band!=b || burst.Hz!=rate)throw new IOException("Preview origin selection/rate changed.");
                    done.TrySetResult(burst);
                }else if(burst.Data!=null && (burst.Kind!=0 || burst.Probe!=p || burst.Channel!=c || burst.Band!=b || burst.Hz!=rate))throw new IOException("Preview origin selection/rate changed.");}}
                catch(Exception ex){done.TrySetException(ex);}
            };
            connection.PreviewReceived+=receive;
            try{
                sent=true;try{if(!await connection.TryWritePreview(NpV1PreviewBurst.Request(burst.Id,0,p,c,0,0,b),epoch,lifetime.Token)){sent=false;return null;}}catch{lease.Restart();throw;}lease.Restart();
                var wait=Stopwatch.StartNew();
                while(!done.Task.IsCompleted){
                    lifetime.Token.ThrowIfCancellationRequested();
                    if(!Current())throw new IOException("Preview connection/recording changed.");
                    if(wait.ElapsedMilliseconds>=5000)throw new TimeoutException("LFP preview is waiting for a complete origin snapshot.");
                    uint actualSession;lock(sync){actualSession=burst.Session;}
                    // A partial response supplies the real session. Renew that
                    // exact origin tuple so control/IMU traffic can take priority.
                    if(actualSession!=0 && lease.ElapsedMilliseconds>=750)try{
                        if(await connection.TryWritePreview(NpV1PreviewBurst.Request(burst.Id,4,p,c,0,actualSession,b),epoch,lifetime.Token))lease.Restart();
                    }catch{lease.Restart();throw;}
                    await Task.WhenAny(done.Task,Task.Delay(100,lifetime.Token));
                }
                var result=await done.Task;if(!Current())throw new IOException("Preview connection/recording changed.");complete=true;return result;
            }finally{
                connection.PreviewReceived-=receive;
                if(sent && !complete){
                    uint actualSession;lock(sync){actualSession=burst.Session;}
                    if(actualSession!=0)try{await connection.TryWritePreview(NpV1PreviewBurst.Request(burst.Id,3,p,c,0,actualSession,b),epoch,CancellationToken.None);}catch{}
                    // An origin owns its own request ID even while activeId=0.
                    // Without a device ACK, let its last lease expire before retry.
                    int remaining=Math.Max(0,2100-(int)lease.ElapsedMilliseconds);
                    if(!closed && remaining!=0)try{await Task.Delay(remaining,lifetime.Token);}catch(OperationCanceledException){}
                }
            }
        }
        async Task StopPreview(bool waitForExpiry=false)
        {
            uint old;lock(gate){old=activeId;activeId=0;pending.Clear();}
            if(old==0)return;
            bool sent=false;
            try{sent=await connection.TryWritePreview(StreamRequest(old,3),epoch,CancellationToken.None);}catch{ /* The device's two-second preview lease expires independently. */ }
            // Preview writes have no device ACK. Before changing selection,
            // let the old lease expire even after a successful host write.
            if((waitForExpiry || !sent) && !closed)await Task.Delay(2100,lifetime.Token);
        }
        async Task Tick()
        {
            if(closed)return;RefreshRecordingInfo();if(busy)return;busy=true;int generation=selectionVersion;
            try{
                if(!Current()){phases.Clear();await StopPreview();info.Text=enabled.Checked?"Waiting for an active probe recording…":"Paused";return;}
                if(activeId!=0 && activeSelection!=generation){await StopPreview(true);info.Text="Waiting for new preview selection…";return;}
                bool controlBusy=controlsBusy();
                if(activeId==0){
                    if(controlBusy || retryPending && renew.ElapsedMilliseconds<1000)return;retryPending=false;
                    UpdateChannelControls();var selected=SelectedChannels();
                    for(int i=0;i<selected.Length;i++)for(int j=0;j<i;j++)if(selected[i]==selected[j]){info.Text="Select different preview channels.";return;}
                    uint p=(uint)probe.Value,c=(uint)channel.Value,b=PreviewBand,rate=status().Hz;
                    Tuple<uint,uint,int> saved;bool cached=phases.TryGetValue(p,out saved)&&saved.Item2==rate;uint ph,sourceSession;
                    if(cached){ph=(uint)saved.Item3;sourceSession=saved.Item1;}
                    else{
                        info.Text="Preparing LFP preview…";var origin=await Snapshot(p,c,b,rate);if(origin==null){info.Text="Waiting for controls…";return;}
                        ph=(uint)await Task.Run(()=>origin.FindPhase(),lifetime.Token);sourceSession=origin.Session;
                    }
                    if(generation!=selectionVersion || !Current() || controlsBusy() || probe.Value!=p || channel.Value!=c)return;
                    phases[p]=Tuple.Create(sourceSession,rate,(int)ph);activeFromCache=cached;
                    lock(gate){activeProbe=p;activeChannel=c;activeChannels=selected;activeCompact=CompactSupported();activeBand=b;activeSelection=generation;phase=ph;session=sourceSession;hz=rate;error=null;pending.Clear();dropped=packets=0;previousEnd=0;gaps=0;activeId=Next();silence.Restart();}
                    if(!await connection.TryWritePreview(StreamRequest(activeId,activeCompact?5U:2U),epoch,lifetime.Token)){lock(gate){activeId=0;pending.Clear();}info.Text="Waiting for controls…";return;}
                    SetPlotChannels(selected);ClearPlots(hz*780.0);renew.Restart();
                }
                if(generation!=selectionVersion)return;
                if(!controlBusy && renew.ElapsedMilliseconds>=750 && await connection.TryWritePreview(StreamRequest(activeId,4),epoch,lifetime.Token))renew.Restart();
                if(generation!=selectionVersion)return;
                List<NpV1PreviewBurst> frames;int hostDrops;long idle;
                lock(gate){if(error!=null)throw error;frames=new List<NpV1PreviewBurst>(pending);pending.Clear();hostDrops=dropped;idle=silence.ElapsedMilliseconds;}
                foreach(var frame in frames){
                    if(previousEnd!=0 && frame.FirstTick<previousEnd)continue;
                    if(previousEnd!=0 && frame.FirstTick!=previousEnd)gaps++;
                    var rows=activeCompact?frame.DecodeCompact(phase):new[]{frame.DecodeRolling(phase)};var profile=calibration==null?null:calibration.Get((int)activeProbe);
                    for(int c=0;c<rows.Length;c++){
                        var samples=rows[c];uint selected=activeCompact?activeChannels[c]:activeChannel;
                        for(int i=0;i<samples.Length;i++)if(!Double.IsNaN(samples[i])){short nominal=(short)(512-samples[i]);samples[i]=profile==null?nominal:profile.Correct(nominal,(int)selected,activeBand==1);}
                        if(activeCompact?frame.ValidMasks[c]!=65535:frame.ValidMask!=65535)gaps++;
                        plots[c].Append(samples,frame.FirstTick+(activeCompact?frame.TickOffsets[c]:0U),frame.Stride);
                    }
                    ulong latest=frame.FirstTick+15UL*frame.Stride;
                    if(activeCompact)foreach(uint offset in frame.TickOffsets)latest=Math.Max(latest,frame.FirstTick+offset+15UL*frame.Stride);
                    foreach(var trace in plots)trace.SetTimeEnd(latest);
                    previousEnd=frame.FirstTick+16UL*frame.Stride;packets++;
                }
                info.Text=(activeBand==0?"AP":"LFP")+" preview · "+(hz*780.0/((activeBand==0?((hz+499)/500)*780:((hz+5999)/6000)*9360))).ToString("F1")+" samples/s per channel"+(!activeCompact?" · Legacy: one channel":"")+(gaps!=0 || hostDrops!=0?" · Preview gaps":"");
                diagnostics.SetToolTip(info,"Subsampled preview; channel timestamps preserve their ADC offsets · gaps "+gaps+" · host drops "+hostDrops+" · packets "+packets);
                if(idle>5000)throw new TimeoutException("LFP preview is waiting for data.");
            }catch(OperationCanceledException){}
            catch(TimeoutException ex){await RetryPreview(ex.Message,generation);}
            catch(IOException ex){await RetryPreview(ex.Message,generation,true);}
            catch(Exception ex){if(!closed && generation==selectionVersion){enabled.Checked=false;try{await StopPreview();}catch(OperationCanceledException){}info.Text=ex.Message;}}
            finally{busy=false;}
        }
        async Task RetryPreview(string message,int generation,bool revalidate=false)
        {
            if(closed || generation!=selectionVersion)return;
            if(revalidate || activeFromCache)phases.Remove(activeProbe);
            try{await StopPreview(true);}catch(OperationCanceledException){return;}
            if(closed || generation!=selectionVersion)return;
            ClearPlots(0);retryPending=true;renew.Restart();info.Text=message+" Retrying…";
        }
        void ClosePreview(){if(closed)return;closed=true;lifetime.Cancel();timer.Stop();connection.PreviewReceived-=Receive;ObserveStop();}
        async void ObserveStop(){await StopPreview();}
        protected override void OnFormClosing(FormClosingEventArgs e){ClosePreview();base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){if(disposing){ClosePreview();timer.Dispose();diagnostics.Dispose();}base.Dispose(disposing);}
        sealed class Plot : Control
        {
            internal string ChannelLabel=String.Empty;
            const int Capacity=500*MaxWindowSeconds;readonly double[] values=new double[Capacity];readonly ulong[] ticks=new ulong[Capacity];
            int count,write;double ticksPerSecond,windowSeconds=4,gain=1;ulong stride;
            ulong displayEnd;bool sharedEnd;
            const double ReferenceCounts=64; // Match the USB NP1 AP display at 1×.
            internal Plot(){DoubleBuffered=true;BackColor=WildXTheme.Dark;}
            internal void SetGain(double value){if(Double.IsNaN(value)||Double.IsInfinity(value)||value<0.1||value>100)throw new ArgumentOutOfRangeException("value");gain=value;Invalidate();}
            internal float ValueY(double value){return (float)(Height/2.0-value*Math.Max(1,Height-60)*gain/(2*ReferenceCounts));}
            internal void SetWindowSeconds(double seconds){if(Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds<0.1 || seconds>MaxWindowSeconds)throw new ArgumentOutOfRangeException("seconds");windowSeconds=seconds;Invalidate();}
            internal void Clear(double rate){count=write=0;ticksPerSecond=rate;sharedEnd=false;Invalidate();}
            internal void SetTimeEnd(ulong tick){displayEnd=tick;sharedEnd=true;Invalidate();}
            internal void Append(double[] data,ulong first,ulong step){stride=step;for(int i=0;i<data.Length;i++){values[write]=data[i];ticks[write]=first+(ulong)i*step;write=(write+1)%Capacity;if(count<Capacity)count++;}Invalidate();}
            protected override void OnPaint(PaintEventArgs e){
                base.OnPaint(e);
                e.Graphics.DrawString(ChannelLabel+"Counts ±"+(ReferenceCounts/gain).ToString("0.##")+" · "+gain.ToString("0.#")+"× · "+windowSeconds.ToString("0.0")+" seconds",Font,Brushes.LightGray,10,5);
                using(var zero=new Pen(Color.DimGray)){zero.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;e.Graphics.DrawLine(zero,10,Height/2,Math.Max(10,Width-10),Height/2);}
                if(count<2 || ticksPerSecond<=0)return;
                int start=(write-count+Capacity)%Capacity;ulong latest=sharedEnd?displayEnd:ticks[(write+Capacity-1)%Capacity];double window=ticksPerSecond*windowSeconds;
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
