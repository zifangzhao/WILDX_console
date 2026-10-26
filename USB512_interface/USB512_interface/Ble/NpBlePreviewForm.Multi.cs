using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBlePreviewForm
    {
        // Capability-gated extension: old firmware keeps the original preview.
        readonly NumericUpDown traceCount=new NumericUpDown{Minimum=1,Maximum=2,Value=2,Width=45,AccessibleName="Preview channel count"};
        readonly NumericUpDown probe2=new NumericUpDown{Minimum=1,Maximum=2,Value=2,Width=45,AccessibleName="Second trace probe"};
        readonly NumericUpDown channel2=new NumericUpDown{Minimum=0,Maximum=383,Value=1,Width=60,AccessibleName="Second trace channel"};
        readonly FlowLayoutPanel multiSelectors=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty,Visible=false};
        readonly TableLayoutPanel multiPlots=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        readonly Plot plot2=new Plot{Dock=DockStyle.Fill};
        readonly Stopwatch multiForward=Stopwatch.StartNew(),multiFeedback=Stopwatch.StartNew();
        NpV2MultiPreviewReceiver multiReceiver;
        Action<int,byte[]> multiReceive;
        byte[] multiStop;
        string multiError;
        int multiSupport; // 0 query; -1 explicit legacy refusal; 1 filtered/replay.
        uint multiSession,multiRate,multiStart,multiCommand,multiMask,multiAck,multiMissing;

        void InitializeMulti(FlowLayoutPanel controls)
        {
            multiPlots.RowStyles.Add(new RowStyle(SizeType.Percent,50));multiPlots.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            Controls.Remove(plot);multiPlots.Controls.Add(plot,0,0);multiPlots.Controls.Add(plot2,0,1);Controls.Add(multiPlots);
            multiSelectors.Controls.AddRange(new Control[]{new Label{Text="Traces",AutoSize=true},traceCount,new Label{Text="P",AutoSize=true},probe2,new Label{Text="Ch",AutoSize=true},channel2});
            controls.Controls.Add(multiSelectors);controls.Controls.SetChildIndex(multiSelectors,4);
            plot2.Visible=false;multiPlots.SetRowSpan(plot,2);
            traceCount.ValueChanged+=delegate{MultiRows();RetirePreview();};
            probe2.ValueChanged+=delegate{RetirePreview();};channel2.ValueChanged+=delegate{RetirePreview();};
            highPass.Changed+=delegate{plot2.SetHighPass(highPass.CutoffHz);};
            windowSeconds.ValueChanged+=delegate{plot2.SetWindowSeconds((double)windowSeconds.Value);};
            gainBox.ValueChanged+=delegate{plot2.SetGain((double)gainBox.Value);};
        }
        void MultiRows()
        {
            bool two=multiSupport==1&&traceCount.Value==2;
            plot2.Visible=two;multiPlots.SetRowSpan(plot,two?1:2);probe2.Enabled=channel2.Enabled=two;
        }
        void RetireMulti(bool sendStop=true)
        {
            multiError=null;
            var old=multiReceiver;if(old==null)return;
            if(sendStop)multiStop=old.Feedback(true);multiReceiver=null;
            connection.PreviewReceived-=multiReceive;multiReceive=null;old.Dispose();
            rearmAfter=Math.Max(rearmAfter,clock.ElapsedMilliseconds+LeaseRetireMilliseconds);
            multiSession=multiRate=0;plot.Clear((int)channel.Value);plot2.Clear((int)channel2.Value);
        }
        void CloseMulti(){RetireMulti();var stop=multiStop;multiStop=null;if(stop!=null)SendClosingStop(stop);}
        async void SendClosingStop(byte[] stop)
        {
            try{await connection.TryWritePreview(stop,epoch,CancellationToken.None);}catch{ /* Preview lease also expires after 2 s. */ }
        }
        bool MultiRecordingCurrent()
        {
            var s=status();return Current(multiRate)&&(s.Flags&8)==0&&s.Error==0&&s.Fault==0&&
                s.Start==multiStart&&s.LastId==multiCommand&&s.Mask==multiMask;
        }
        async Task<int> QueryMulti(CancellationToken cancel)
        {
            uint id=Next();var reply=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<int,byte[]> receive=(generation,body)=>{
                if(generation!=epoch||!NpV2MultiPreview.Matches(body,id))return;
                try{if(NpV2MultiPreview.LegacyUnsupported(body,id))reply.TrySetResult(-1);
                    else if(((BitConverter.ToUInt32(body,28)>>8)&128)!=0)throw new NpV2PreviewUnavailableException((BitConverter.ToUInt32(body,28)>>8)&127);
                    else if(NpV2MultiPreview.ParseCapabilities(body,id)!=null)reply.TrySetResult(1);
                }catch(Exception ex){reply.TrySetException(ex);}
            };
            connection.PreviewReceived+=receive;
            try{
                if(!await connection.TryWritePreview(NpV2MultiPreview.CapabilityRequest(id),epoch,cancel))return 0;
                if(await Task.WhenAny(reply.Task,Task.Delay(5000,cancel))!=reply.Task){cancel.ThrowIfCancellationRequested();throw new IOException("Preview capability query timed out; recording is independent.");}
                return await reply.Task;
            }finally{connection.PreviewReceived-=receive;}
        }
        void DrainMulti()
        {
            var receiver=multiReceiver;if(receiver==null)return;
            foreach(var frame in receiver.Drain()){
                plot.AppendFiltered(frame,0);if(frame.Samples.GetLength(1)==2)plot2.AppendFiltered(frame,1);
            }
            uint ack=receiver.Acknowledged;
            if(ack!=multiAck){multiForward.Restart();multiAck=ack;}
            plot.Invalidate();plot2.Invalidate();
            info.Text="Preview · 500 samples/s per channel"+(receiver.Missing!=0?" · Recovering…":"");
            diagnostics.SetToolTip(info,"Filtered nominal ADC counts · useful band 0–200 Hz · 320 ms display lead · packets "+receiver.Packets+" · checksum recoveries "+receiver.ChecksumFailures+" · replay history 2.56 s");
            if(multiForward.ElapsedMilliseconds>2500)throw new IOException("Preview recovery timed out; recording is independent.");
        }
        async Task<bool> TickMulti()
        {
            if(multiSupport==-1)return false;
            // Drain checked originals and service cumulative ACKs before the UI
            // busy guard. The connection still yields every write to IMU/control.
            if(multiReceiver!=null&&!MultiRecordingCurrent())RetirePreview();
            try{DrainMulti();}
            catch(Exception ex){enabled.Checked=false;multiError=ex.Message;info.Text=multiError;return true;}
            if(busy)return true;
            int generation=selectionGeneration;
            busy=true;
            try{
                var token=lifetime.Token;
                if(multiStop!=null){var stop=multiStop;multiStop=null;if(!await connection.TryWritePreview(stop,epoch,token)){multiStop=stop;return true;}}
                var receiver=multiReceiver;
                if(receiver!=null){
                    uint acknowledged=receiver.Acknowledged,missing=receiver.Missing;
                    bool stalled=multiForward.ElapsedMilliseconds>(acknowledged==0?1200:350);
                    bool due=acknowledged-multiLastFeedbackAck>=2||missing!=multiMissing||multiFeedback.ElapsedMilliseconds>=600||stalled;
                    if(due&&multiFeedback.ElapsedMilliseconds>=60){
                        if(await connection.TryWritePreview(receiver.Feedback(stalled:stalled),epoch,token)){
                            if(receiver!=multiReceiver)return true;
                            multiLastFeedbackAck=acknowledged;multiMissing=missing;multiFeedback.Restart();
                        }
                    }
                    return true;
                }
                if(!Current(0)){info.Text=enabled.Checked?"Waiting for recording…":multiError??"Paused";return true;}
                if(controlsBusy()||clock.ElapsedMilliseconds<rearmAfter||retry.ElapsedMilliseconds<1000)return true;
                if(multiSupport==0){
                    info.Text="Checking preview support…";int supported=await QueryMulti(token);
                    if(closed||generation!=selectionGeneration)return true;
                    multiSupport=supported;multiSelectors.Visible=supported==1;MultiRows();
                    if(supported==-1)return false;if(supported==0){retry.Restart();return true;}
                }
                var s=status();
                // Only P1/P2's PSSI lanes are supported. P3 recording is retained.
                probe.Maximum=probe2.Maximum=s.Mask==1?1:2;
                if(generation!=selectionGeneration)return true;
                uint p=(uint)probe.Value,c=(uint)channel.Value,p2=(uint)probe2.Value,c2=(uint)channel2.Value;
                int n=(int)traceCount.Value;
                if(n==2&&p==p2&&c==c2){info.Text="Select two different channels.";return true;}
                multiStart=s.Start;multiCommand=s.LastId;multiMask=s.Mask;multiRate=s.Hz;
                if(!MultiRecordingCurrent())return true;
                info.Text="Preparing preview…";
                var snapshot=await connection.CapturePreview(Next(),p,c,multiRate,token);
                if(generation!=selectionGeneration||!MultiRecordingCurrent())return true;
                var selection=await Task.Run(()=>n==1?new[]{new NpV2MultiPreview.Selection(p,c,(uint)snapshot.FindPhaseFor(p))}:
                    new[]{new NpV2MultiPreview.Selection(p,c,(uint)snapshot.FindPhaseFor(p)),new NpV2MultiPreview.Selection(p2,c2,(uint)snapshot.FindPhaseFor(p2))},token);
                if(generation!=selectionGeneration||!MultiRecordingCurrent())return true;
                multiSession=snapshot.Session;
                if(multiSession==0)throw new IOException("Preview recording session is missing.");
                // A single immutable calibration snapshot supplies both probe phases.
                uint id=Next();receiver=new NpV2MultiPreviewReceiver(id,multiSession,multiRate,selection,epoch);
                plot.Units=plot2.Units=8;plot.LeadSeconds=plot2.LeadSeconds=0.32;
                plot.CorrectSample=plot2.CorrectSample=null; // Filtering precedes gain/ADC host correction; display remains nominal.
                plot.TraceLabel="P"+p+" · Ch "+c;plot2.TraceLabel="P"+p2+" · Ch "+c2;
                plot.SetSampleRate(500);plot2.SetSampleRate(500);highPass.SampleRate=500;
                plot.SetHighPass(highPass.CutoffHz);plot2.SetHighPass(highPass.CutoffHz);
                multiAck=multiLastFeedbackAck=multiMissing=0;multiForward.Restart();multiFeedback.Restart();
                multiReceiver=receiver;multiReceive=receiver.Receive;connection.PreviewReceived+=multiReceive;
                bool sent=await connection.TryWritePreview(NpV2MultiPreview.Configure(id,multiSession,selection),epoch,token);
                if(generation!=selectionGeneration)return true;
                if(!sent){RetireMulti();multiStop=null;rearmAfter=clock.ElapsedMilliseconds;retry.Restart();}
            }catch(NpV2PreviewUnavailableException ex){
                if(!closed&&generation==selectionGeneration){
                    if(ex.CanRetry){RetireMulti();retry.Restart();info.Text="Preview waiting for recording bandwidth…";}
                    else{enabled.Checked=false;multiError=ex.Message;info.Text=multiError;}
                }
            }catch(OperationCanceledException){RetireMulti(false);multiStop=null;if(!closed&&generation==selectionGeneration)enabled.Checked=false;}
            catch(Exception ex){
                // Delivery may have stopped inside the UART frame. Do not send
                // another frame to repair it; let the preview lease expire.
                RetireMulti(false);multiStop=null;
                if(!closed&&generation==selectionGeneration){enabled.Checked=false;multiError=ex.Message;info.Text=multiError;}
            }
            finally{if(generation!=selectionGeneration)rearmAfter=Math.Max(rearmAfter,clock.ElapsedMilliseconds+LeaseRetireMilliseconds);busy=false;}
            return true;
        }
        uint multiLastFeedbackAck;
    }
}
