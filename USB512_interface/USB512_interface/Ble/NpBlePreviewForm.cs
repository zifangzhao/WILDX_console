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
        readonly uint id,probe,channel,session,hz;
        readonly int epoch;
        readonly Queue<NpV2PreviewBurst> pending=new Queue<NpV2PreviewBurst>();
        readonly object gate=new object();
        bool disposed;Exception error;
        internal int Dropped {get;private set;}
        internal readonly Stopwatch Silence=Stopwatch.StartNew();
        internal NpBlePreviewStream(NpBleConnection source,uint request,uint selectedProbe,uint selectedChannel,uint expectedSession,uint expectedHz)
        {
            connection=source;epoch=source.Epoch;id=request;probe=selectedProbe;channel=selectedChannel;session=expectedSession;hz=expectedHz;
            connection.PreviewReceived+=Receive;
        }
        internal void Receive(int generation,byte[] body)
        {
            lock(gate){
                if(disposed || generation!=epoch)return;
                try{
                    var burst=new NpV2PreviewBurst(id);if(!burst.Accept(body))return;
                    if(burst.Kind!=2 || burst.Probe!=probe || burst.Channel!=channel || burst.Session!=session || burst.Hz!=hz)
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
        readonly NpBleConnection connection;
        readonly Func<NpSdStatus> status;
        readonly Func<bool> controlsBusy;
        readonly NpCalibrationSet factoryCalibration;
        readonly NumericUpDown probe=new NumericUpDown{Minimum=1,Maximum=2,Value=1,Width=55};
        readonly NumericUpDown channel=new NumericUpDown{Minimum=0,Maximum=127,Width=65};
        readonly CheckBox enabled=new CheckBox{Text="Preview",AutoSize=true,Checked=true};
        readonly NpHighPassControl highPass=new NpHighPassControl();
        readonly Label info=new Label{AutoSize=true,MaximumSize=new Size(800,0)};
        readonly Plot plot=new Plot{Dock=DockStyle.Fill};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=50};
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly Stopwatch heartbeat=Stopwatch.StartNew(),retry=Stopwatch.StartNew();
        readonly int epoch;
        NpBlePreviewStream stream;
        int phase;uint session,hz,activeProbe,activeChannel,request;
        bool busy,closed;
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
            highPass.SampleRate=500;highPass.CutoffBox.Text="100";
            highPass.Changed+=delegate{plot.SetHighPass(highPass.CutoffHz);};
            controls.Controls.AddRange(new Control[]{new Label{Text="Probe",AutoSize=true},probe,new Label{Text="Base channel",AutoSize=true},channel,enabled,highPass,info});
            Controls.Add(plot);Controls.Add(controls);WildXTheme.Apply(this);info.ForeColor=WildXTheme.Muted;
            probe.ValueChanged+=delegate{enabled.Checked=false;DropStream();};channel.ValueChanged+=delegate{enabled.Checked=false;DropStream();};
            enabled.CheckedChanged+=delegate{DropStream();info.Text=enabled.Checked?"Waiting for recording…":"Paused";retry.Restart();};
            timer.Tick+=async delegate{await Tick();};Shown+=delegate{timer.Start();};
        }
        bool Current(uint rate)
        {
            var s=status();return !closed && enabled.Checked && connection.Connected && connection.Epoch==epoch &&
                NpV2SdControl.CanPreview(s) && (rate==0 || s.Hz==rate);
        }
        void DropStream(){var old=stream;stream=null;session=hz=0;previousEnd=0;if(old!=null)old.Dispose();}
        protected override void OnFormClosing(FormClosingEventArgs e){closed=true;lifetime.Cancel();DropStream();base.OnFormClosing(e);}
        protected override void Dispose(bool disposing)
        {
            if(disposing && !closed){closed=true;lifetime.Cancel();DropStream();}
            if(disposing){timer.Stop();timer.Dispose();lifetime.Dispose();}
            base.Dispose(disposing);
        }
        async Task Tick()
        {
            if(busy || closed)return;
            if(!Current(hz)){DropStream();info.Text=enabled.Checked?"Waiting for recording…":"Paused";return;}
            if(controlsBusy())return; // Preview never queues in front of control/status/clock work.
            busy=true;
            try{
                var token=lifetime.Token;
                if(stream==null){
                    if(retry.ElapsedMilliseconds<1000)return;
                    activeProbe=(uint)probe.Value;activeChannel=(uint)channel.Value;hz=status().Hz;
                    info.Text="Reading probe framing…";
                    var calibration=await connection.CapturePreview(Next(),activeProbe,activeChannel,hz,token);
                    phase=await Task.Run(()=>calibration.FindPhase(),token);session=calibration.Session;
                    await Task.Delay(550,token); // Firmware preview admission interval after calibration.
                    if(!Current(hz) || controlsBusy() || probe.Value!=activeProbe || channel.Value!=activeChannel){DropStream();return;}
                    request=Next();stream=new NpBlePreviewStream(connection,request,activeProbe,activeChannel,session,hz);
                    if(!await connection.TryWritePreview(NpV2PreviewBurst.Request(request,2,activeProbe,activeChannel,(uint)phase,session),epoch,token)){DropStream();retry.Restart();return;}
                    if(!Current(hz) || stream==null){DropStream();return;}
                    plot.Clear((int)activeChannel);gaps=skipped=0;packets=0;heartbeat.Restart();
                    var profile=factoryCalibration==null?null:factoryCalibration.Get((int)activeProbe);
                    plot.CorrectSample=profile==null?null:(Func<int,short,short>)((ch,value)=>profile.Correct(value,ch));
                }
                if(heartbeat.ElapsedMilliseconds>=750 && await connection.TryWritePreview(NpV2PreviewBurst.Request(request,4,activeProbe,activeChannel,(uint)phase,session),epoch,token))heartbeat.Restart();
                if(!Current(hz) || stream==null){DropStream();return;}
                var frames=stream.Drain();
                var decoded=await Task.Run(()=>{var samples=new List<ushort[,]>();foreach(var frame in frames)samples.Add(frame.DecodeRolling(phase));return samples;},token);
                if(!Current(hz) || probe.Value!=activeProbe || channel.Value!=activeChannel){DropStream();return;}
                ulong stride=(ulong)(hz/500)*1600;
                for(int i=0;i<frames.Count;i++){
                    var frame=frames[i];
                    if(previousEnd!=0 && frame.FirstTick<previousEnd)continue;
                    if(previousEnd!=0 && frame.FirstTick!=previousEnd)gaps++;
                    for(int n=0;n<16;n++)if((frame.ValidMask&(1U<<n))==0)skipped++;
                    previousEnd=frame.FirstTick+16*stride;packets++;plot.Append(decoded[i],frame.FirstTick,stride,frame.ValidMask);
                }
                info.Text="500 samples/s · gaps "+gaps+" · skipped "+skipped+" · host drops "+stream.Dropped+" · packets "+packets;
                if(stream.Silence.ElapsedMilliseconds>2500)throw new NpV2PreviewUnavailableException(128);
            }catch(OperationCanceledException){if(!closed){DropStream();enabled.Checked=false;}}
            catch(Exception ex){if(!closed){DropStream();enabled.Checked=false;info.Text=ex.Message;}}
            finally{busy=false;}
        }
        // BLE wire stays raw. Convert only the disposable PC display samples.
        sealed class Plot : Control
        {
            const int Capacity=2000;
            readonly short[,] values=new short[Capacity,3];readonly ulong[] ticks=new ulong[Capacity];
            readonly NpDisplayHighPass highPass=new NpDisplayHighPass(3);
            readonly short[] displayRow=new short[3];
            internal Func<int,short,short> CorrectSample;
            int count,write,baseChannel;ulong stride;
            internal Plot(){DoubleBuffered=true;BackColor=WildXTheme.Dark;ForeColor=Color.LightGreen;}
            internal void SetHighPass(double cutoff){highPass.Configure(500,cutoff);Clear(baseChannel);}
            internal void Clear(int channel){count=write=0;baseChannel=channel;highPass.Reset();Invalidate();}
            internal void Append(ushort[,] data,ulong first,ulong step,uint valid)
            {
                stride=step;for(int i=0;i<data.GetLength(0);i++){
                    if((valid&(1U<<i))==0)continue;ticks[write]=first+(ulong)i*step;
                    for(int lane=0;lane<3;lane++){
                        short value=NpV2SampleDecoder.NominalCount(data[i,lane]);
                        displayRow[lane]=CorrectSample==null?value:CorrectSample(baseChannel+lane*128,value);
                    }
                    highPass.Apply(displayRow,0,displayRow,0,checked((long)(ticks[write]/step)));
                    for(int lane=0;lane<3;lane++)values[write,lane]=displayRow[lane];write=(write+1)%Capacity;if(count<Capacity)count++;
                }Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);if(count<2)return;
                int start=(write-count+Capacity)%Capacity;ulong latest=ticks[(write+Capacity-1)%Capacity];double window=(Capacity-1)*(double)stride;
                Color[] colors={Color.LightGreen,Color.LightSkyBlue,Color.Orange};
                for(int lane=0;lane<3;lane++){
                    double sum=0;int min=short.MaxValue,max=short.MinValue;for(int i=0;i<count;i++){int v=values[(start+i)%Capacity,lane];sum+=v;min=Math.Min(min,v);max=Math.Max(max,v);}
                    double mean=sum/count,range=Math.Max(32,max-min);float top=lane*Height/3f,band=Height/3f;var segment=new List<PointF>();ulong prev=0;
                    using(var pen=new Pen(colors[lane])){
                        for(int i=0;i<count;i++){
                            int at=(start+i)%Capacity;ulong tick=ticks[at];if(latest-tick>window)continue;
                            if(segment.Count>0 && tick-prev!=stride){if(segment.Count>1)e.Graphics.DrawLines(pen,segment.ToArray());segment.Clear();}
                            segment.Add(new PointF((float)(15+(1-(latest-tick)/window)*(Width-30)),(float)(top+band/2-(values[at,lane]-mean)*(band-50)/range)));prev=tick;
                        }
                        if(segment.Count>1)e.Graphics.DrawLines(pen,segment.ToArray());
                    }
                    e.Graphics.DrawString("Ch "+(baseChannel+lane*128)+(CorrectSample==null?" · nominal counts ":" · calibrated counts ")+min+"–"+max,Font,Brushes.LightGray,10,top+5);
                }
            }
        }
    }
}
