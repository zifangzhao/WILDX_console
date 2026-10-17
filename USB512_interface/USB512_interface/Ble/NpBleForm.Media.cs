using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBleForm
    {
        readonly FlowLayoutPanel mediaControls=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Name="bleMediaControls"};
        readonly CheckBox mediaCamera=new CheckBox {Text="Camera",Checked=true,AutoSize=true,Name="bleMediaCamera"};
        readonly NumericUpDown mediaRate=new NumericUpDown {Minimum=1,Maximum=50,Value=40,Width=64,Name="bleMediaRate"};
        readonly CheckBox mediaMicrophone=new CheckBox {Text="Microphone (200 kHz)",AutoSize=true,Name="bleMediaMicrophone"};
        readonly Label mediaStatus=new Label {AutoSize=true};
        Task refreshTask=Task.CompletedTask;
        bool mediaConfirmed,mediaWasRecording;

        void InitializeMediaControls(FlowLayoutPanel options)
        {
            mediaControls.Controls.AddRange(new Control[]{mediaCamera,mediaRate,new Label{Text="fps",AutoSize=true,Margin=new Padding(0,7,12,0)},mediaMicrophone});
            options.Controls.Add(mediaControls);
            profiles.SelectedIndexChanged+=delegate {UpdateMediaControls();};
            mediaCamera.CheckedChanged+=delegate {UpdateMediaControls();};
        }
        void UpdateMediaControls()
        {
            var selected=profiles.SelectedItem as Profile;
            bool media=recorderVersion==1 && selected!=null && selected.Mask==0;
            mediaControls.Visible=media;
            mediaControls.Enabled=media && connection.Connected && !busy && !hostBusy && NpV1BleProtocol.CanConfigure(current);
            mediaRate.Enabled=mediaCamera.Checked;
        }
        void ResetMedia(){mediaConfirmed=mediaWasRecording=false;mediaStatus.Text="";}
        async Task RefreshStatusQuietly()
        {
            try {await ReadStatus();if(V2)await CheckClock();}
            catch(Exception ex){if(!closing && !IsDisposed){current=null;state.Text=ex.Message;Append(ex.Message);}}
            finally {if(!closing && !IsDisposed && !busy)UpdateControls();}
        }
        internal static bool MediaParameters(byte[] data,out bool camera,out bool microphone,out uint rate)
        {
            // V1 CE32_systemParam: Nch at8, eight sampling rates at40.
            bool valid=NpV1MediaStatus.ReadRates(data,40,8,out camera,out microphone);
            rate=valid?NpSdGeometry.U(data,48):0;return valid;
        }
        async Task<byte[]> ReadMediaParameters()
        {
            var data=await connection.ExchangeParameters(new byte[]{0x3c,0x90,0x3e});
            bool camera,microphone;uint rate;
            if(!MediaParameters(data,out camera,out microphone,out rate))throw new IOException("Device media parameters are not confirmed.");
            return data;
        }
        void AdoptMediaParameters(byte[] data)
        {
            bool camera,microphone;uint rate;
            if(!MediaParameters(data,out camera,out microphone,out rate))throw new IOException("Invalid V1 media profile.");
            mediaCamera.Checked=camera;mediaMicrophone.Checked=microphone;if(camera)mediaRate.Value=rate;
            mediaConfirmed=true;mediaStatus.Text=(camera?"Camera "+rate+" fps":"")+(camera&&microphone?" · ":"")+(microphone?"Microphone 200 kHz":"");
        }
        async Task RefreshV1Media(NpSdStatus s)
        {
            if(!NpV1BleProtocol.IsMedia(s)){ResetMedia();return;}
            if(!mediaConfirmed && !mediaSettingsChanged) {
                try {AdoptMediaParameters(await ReadMediaParameters());}
                catch(Exception ex) when(ex is IOException || ex is TimeoutException){mediaStatus.Text="Media settings unconfirmed: "+ex.Message;}
            }
            bool recording=s.State==1 || s.State==2;
            if(recording || mediaWasRecording) {
                try {
                    var m=NpV1MediaStatus.Parse(await connection.ExchangeAsync(NpV1MediaStatus.Request()));
                    recordingSeconds=m.MicrophoneHz!=0?m.Samples/m.MicrophoneHz:m.CameraHz!=0?m.Frames/m.CameraHz:0;
                    recordingTime.Text=RecordingTimeText(recordingSeconds)+RecordingStorageText(false,current);
                    mediaStatus.Text="Camera "+m.CameraHz+" fps · "+m.Frames+" frames · "+m.Missed+" misses · microphone "+m.MicrophoneHz.ToString("N0")+" Hz / "+m.Samples.ToString("N0")+" samples · faults "+m.CameraFault+" / "+m.MicrophoneFault+" · overruns "+m.Overruns;
                }catch(Exception ex) when(ex is IOException || ex is TimeoutException){mediaStatus.Text="Media counters delayed: "+ex.Message;}
            }
            mediaWasRecording=recording;
        }
        async Task VerifyV1Media()
        {
            bool camera,microphone;uint rate;
            var data=await ReadMediaParameters();
            if(!MediaParameters(data,out camera,out microphone,out rate) || camera!=mediaCamera.Checked || microphone!=mediaMicrophone.Checked || camera && rate!=(uint)mediaRate.Value)
                throw new IOException("Apply the selected camera/microphone profile before Start.");
            mediaConfirmed=true;
        }
        async Task ApplyV1Media()
        {
            bool camera=mediaCamera.Checked,microphone=mediaMicrophone.Checked;uint rate=(uint)mediaRate.Value;
            var command=NpV1MediaStatus.ProfileCommand(camera,microphone,rate);
            var s=await ReadStatus();
            RequireStartNotCanceled();
            if(!NpV1BleProtocol.CanConfigure(s) || !NpV1BleProtocol.CanSelect(s,0,0))throw new IOException("Stop recording before applying a supported V1 media profile.");
            mediaConfirmed=false;
            try {await connection.ExchangeParameters(command);}
            catch(TimeoutException){Append("Profile receipt delayed; checking device settings without replaying the command.");}
            int generation=connection.Epoch;
            var wait=Stopwatch.StartNew();string detail="Waiting for the device profile.";
            while(wait.Elapsed.TotalSeconds<60) {
                await Task.Delay(250);
                RequireStartNotCanceled();
                if(!connection.Connected || connection.Epoch!=generation)throw new IOException("Connection changed; reconnect and read the device profile.");
                try {
                    s=await Send(NpSdControl.Request(0,Next()));
                    if(!NpV1BleProtocol.CanConfigure(s) || !NpV1BleProtocol.IsMedia(s)){detail=NpV1BleProtocol.StateText(s);continue;}
                    var data=await ReadMediaParameters();bool actualCamera,actualMicrophone;uint actualRate;
                    if(!MediaParameters(data,out actualCamera,out actualMicrophone,out actualRate) || actualCamera!=camera || actualMicrophone!=microphone || camera && actualRate!=rate){detail="Device settings differ from the requested media profile.";continue;}
                    AdoptMediaParameters(data);
                    mediaSettingsChanged=false;
                    await SyncClock();
                    Profiles(s);Append("Camera/microphone profile and PC time confirmed. Ready for Start.");return;
                }catch(Exception ex) when(ex is IOException || ex is TimeoutException){detail=ex.Message;}
            }
            mediaConfirmed=false;
            throw new IOException("Media profile not confirmed; recording was not started. "+detail);
        }
    }
}
