using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface {
    internal sealed partial class NpBleForm {
        readonly Label bleImuLabel=new Label {Name="bleImuStatus",AutoSize=true,Visible=false,MaximumSize=new Size(850,0)};
        readonly System.Windows.Forms.Timer bleImuTimer=new System.Windows.Forms.Timer {Interval=1000};
        CancellationTokenSource bleImuCancel;
        Task bleImuTask=Task.CompletedTask;
        NpBleImuSession bleImuSession;
        string bleImuFolder;
        bool bleImuUnsupported,bleBackgroundBusy;
        long bleImuObservedAt=-1;
        readonly NpBleImuRetry bleImuRetry=new NpBleImuRetry();
        readonly System.Diagnostics.Stopwatch bleImuClock=System.Diagnostics.Stopwatch.StartNew();
        internal NpImuDisplay LastBleImu {get;private set;}
        void InitializeBleImu(FlowLayoutPanel panel) {
            panel.Controls.Add(bleImuLabel);bleImuLabel.Margin=new Padding(0,0,0,6);
            panel.SizeChanged+=delegate {bleImuLabel.MaximumSize=new Size(Math.Max(1,panel.ClientSize.Width),0);};
            bleImuTimer.Tick+=async delegate {await PollBleBackground();};
            bleImuTimer.Start();
        }
        async Task PollBleBackground() {
            RefreshBleImuAge(bleImuClock.ElapsedMilliseconds);
            if(closing || bleBackgroundBusy)return;
            bleBackgroundBusy=true;
            try {
                if(!BleImuShouldYield() && !bleImuUnsupported && bleImuTask.IsCompleted && bleImuRetry.CanAttempt(bleImuClock.ElapsedMilliseconds)) {
                    await RetireBleRecordingClock();
                    if(!BleImuShouldYield()){bleImuTask=PollBleImu();await bleImuTask;}
                }
                await PollRecordingClockIfDue();
            }finally{bleBackgroundBusy=false;}
        }
        void RefreshBleImuAge(long now) {
            if(closing || bleImuUnsupported || LastBleImu==null || bleImuObservedAt<0 || now-bleImuObservedAt<3000)return;
            LastBleImu=null;bleImuLabel.Text="IMU waiting for fresh data…";bleImuLabel.Visible=true;
        }
        bool BleImuShouldYield() {
            return closing || !NpBleImu.CanPoll(recorderVersion,current==null?0U:current.Flags) || !connection.Connected || busy || hostBusy || stopRequested || stopping || pendingStop!=null ||
                !refreshTask.IsCompleted;
        }
        void CancelBleImuPoll(){if(bleImuCancel!=null)bleImuCancel.Cancel();}
        void CloseBleImuJournal() {
            if(bleImuSession!=null) {bleImuSession.Dispose();bleImuSession=null;}
            bleImuFolder=null;
        }
        void ResetBleImu() {
            connection.CancelRecordingClock();ResetRecordingClock();CancelBleImuPoll();CloseBleImuJournal();bleImuUnsupported=false;bleImuRetry.Reset();LastBleImu=null;bleImuObservedAt=-1;bleImuLabel.Visible=false;
        }
        void ShutdownBleImu(){bleImuTimer.Stop();ResetBleImu();bleImuTimer.Dispose();}
        void RetryBleImu(string message) {
            bleImuRetry.Defer(bleImuClock.ElapsedMilliseconds);LastBleImu=null;
            bleImuLabel.Text=message+" Retrying…";bleImuLabel.Visible=true;
        }
        async Task PollBleImu() {
            int epoch=connection.Epoch;
            using(var cancel=new CancellationTokenSource(900)) {
                bleImuCancel=cancel;
                try {
                    byte[] view=await connection.ReadImuSnapshot(BleImuShouldYield,cancel.Token);
                    if(view==null || BleImuShouldYield() || connection.Epoch!=epoch)return;
                    string folder=current!=null && current.State==(V2?3U:1U) && bleRecordingMetadata!=null && bleMetadataRecordingSeen && !bleMetadataFinished?bleMetadataFolder:null;
                    if(bleImuSession==null || folder!=bleImuFolder) {
                        CloseBleImuJournal();bleImuSession=new NpBleImuSession(folder,bleDeviceUid??("BLE-"+connectedAddress.ToString("X12")));bleImuFolder=folder;
                    }
                    LastBleImu=bleImuSession.Accept(view);bleImuObservedAt=bleImuClock.ElapsedMilliseconds;bleImuRetry.Reset();bleImuLabel.Text=bleImuSession.DisplayText;bleImuLabel.Visible=true;
                    if(bleImuSession.Error!=null)bleImuLabel.Text+="\nIMU log: "+bleImuSession.Error;
                }catch(OperationCanceledException) {
                    if(!BleImuShouldYield() && connection.Epoch==epoch)RetryBleImu("IMU response timed out.");
                }
                catch(NpBleImuUnavailableException ex) {
                    if(closing || connection.Epoch!=epoch)return;
                    if(ex.Error==2){bleImuUnsupported=true;LastBleImu=null;bleImuLabel.Text=ex.Message;bleImuLabel.Visible=true;}
                    else RetryBleImu(ex.Message);
                    // Only explicit unsupported disables this optional capability.
                }catch(Exception ex) {
                    if(closing || connection.Epoch!=epoch)return;
                    RetryBleImu(ex is TimeoutException?"IMU response timed out.":"IMU unavailable: "+ex.Message);
                }finally{if(bleImuCancel==cancel)bleImuCancel=null;}
            }
        }
    }
}
