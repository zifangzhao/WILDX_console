using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    // Camera-only admission is independent of neural admission. Reuse the
    // existing packet parser, recorder and bounded renderer for this V1 mode.
    internal sealed class NpMediaUsbForm : Form
    {
        readonly string selectedPath,outputRoot,sessionLabel;
        readonly CameraPreviewForm camera;
        readonly Button start=new Button {Text="Start preview",AutoSize=true};
        readonly Button stop=new Button {Text="Stop",AutoSize=true,Enabled=false};
        readonly CheckBox record=new CheckBox {Text="Save camera data",AutoSize=true};
        readonly NumericUpDown rate=new NumericUpDown {Minimum=1,Maximum=58,Value=40,Width=70};
        readonly Label status=new Label {Dock=DockStyle.Bottom,Height=48,AutoEllipsis=true};
        readonly Label audioStatus=new Label {Dock=DockStyle.Top,Height=46,Text="Live USB audio is unavailable in this V1 firmware. Open an exported WAV to listen."};
        readonly SoundPlayer audio=new SoundPlayer();
        readonly PictureBox waveform=new PictureBox {Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom};
        readonly object deviceGate=new object();
        XeNpWinUsbDevice device;
        StreamWriter exposureLog;
        Task capture;
        volatile bool stopping;
        internal string LastFolder,LastError;
        internal long Frames,Packets,Bytes,SequenceGaps;
        internal bool Active {get{return capture!=null && !capture.IsCompleted;}}

        internal NpMediaUsbForm(string path,string folder) : this(path,folder,"") {}
        internal NpMediaUsbForm(string path,string folder,string label)
        {
            selectedPath=path;outputRoot=folder;sessionLabel=label;Text="Media preview";ClientSize=new Size(800,850);MinimumSize=new Size(600,550);
            camera=new CameraPreviewForm((rows,gain)=>{lock(deviceGate){if(device==null)throw new IOException("Camera is stopped.");device.SetCameraExposure(rows,gain);
                if(exposureLog!=null)exposureLog.WriteLine(DateTime.UtcNow.ToString("O")+","+rows+","+gain+",USB ACK");}});
            camera.TopLevel=false;camera.FormBorderStyle=FormBorderStyle.None;camera.Dock=DockStyle.Fill;camera.Text="Camera";
            var tabs=new TabControl {Dock=DockStyle.Fill};var video=new TabPage("Camera");var sound=new TabPage("Audio");
            video.Controls.Add(camera);camera.Show();tabs.TabPages.AddRange(new[]{video,sound});Controls.Add(tabs);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Top,Height=48,Padding=new Padding(8),WrapContents=false};
            actions.Controls.AddRange(new Control[]{start,stop,new Label {Text="FPS",AutoSize=true,Margin=new Padding(12,8,4,0)},rate,record});Controls.Add(actions);
            status.Padding=new Padding(8);status.Text="Ready";Controls.Add(status);
            var open=new Button {Text="Open WAV…",AutoSize=true};var mute=new Button {Text="Stop audio",AutoSize=true};
            var audioActions=new FlowLayoutPanel {Dock=DockStyle.Top,Height=48,Padding=new Padding(8)};audioActions.Controls.AddRange(new Control[]{open,mute});
            sound.Controls.Add(waveform);sound.Controls.Add(audioActions);sound.Controls.Add(audioStatus);
            open.Click+=delegate {using(var picker=new OpenFileDialog {Filter="WAV audio|*.wav"})if(picker.ShowDialog(this)==DialogResult.OK){
                try{var clip=NpAudioPreview.ReadClip(picker.FileName);audio.Stop();var previous=audio.Stream;audio.Stream=clip;if(previous!=null)previous.Dispose();
                    var oldImage=waveform.Image;waveform.Image=NpAudioPreview.Waveform(clip);if(oldImage!=null)oldImage.Dispose();
                    audio.Play();audioStatus.Text=Path.GetFileName(picker.FileName)+" / first 10 seconds";}
                catch(Exception ex){audioStatus.Text="Audio playback failed: "+ex.Message;}}};
            mute.Click+=delegate {audio.Stop();};
            start.Click+=async delegate {await StartPreview();};stop.Click+=delegate {StopPreview();};
            FormClosing+=async delegate(object sender,FormClosingEventArgs e){if(Active){e.Cancel=true;StopPreview();try{await capture;}catch{}Close();}};
            FormClosed+=delegate {audio.Stop();if(audio.Stream!=null)audio.Stream.Dispose();audio.Dispose();if(waveform.Image!=null)waveform.Image.Dispose();camera.Dispose();};
            WildXTheme.Apply(this);WildXTheme.Button(start,true);
        }
        internal void StopPreview(){stopping=true;stop.Enabled=false;status.Text="Stopping…";}
        internal async Task StartPreview()
        {
            if(Active)return;
            if(record.Checked && String.IsNullOrWhiteSpace(outputRoot)){status.Text="Choose an output folder in USB Mode before saving.";return;}
            stopping=false;LastError=null;LastFolder=null;Frames=Packets=Bytes=SequenceGaps=0;
            int requested=(int)rate.Value;bool save=record.Checked;
            start.Enabled=rate.Enabled=record.Enabled=false;stop.Enabled=true;status.Text="Opening camera…";
            capture=Task.Run(()=>RunCapture(requested,save));
            try{await capture;}catch(Exception ex){LastError=(LastError==null?"":LastError+"; ")+"Media metadata failed: "+ex.Message;}
            if(!IsDisposed){start.Enabled=rate.Enabled=record.Enabled=true;stop.Enabled=false;status.Text=LastError==null?
                "Stopped — "+Frames+" frames"+(LastFolder==null?"":" / "+LastFolder):"Preview stopped: "+LastError;}
        }
        void RunCapture(int requested,bool save)
        {
            XeNpWinUsbDevice usb=null;USB512WinUsbQueuedReader reader=null;NpCameraRecording recording=null;NpMediaUsbImu imu=null;
            byte[] frame=null;int frameBytes=0;bool frameValid=false,haveSequence=false,started=false;ushort expected=0;
            var parser=new XeNpPacketParser();DateTime began=DateTime.UtcNow;NpCameraStatus initial=null,final=null;
            Action<byte[],int,int> noNeural=delegate {throw new IOException("Unexpected neural payload in camera-only mode.");};
            byte[] block=new byte[64*1024];
            try {
                if(save)NpFolderNaming.Label(sessionLabel);
                string path=NpV1Management.EnsureStreaming(selectedPath);
                using(var control=XeNpWinUsbDevice.OpenForControl(path)) {
                    if(control.GetStatus().Streaming)throw new IOException("The device is already streaming.");
                    var caps=control.GetModeCapabilities();initial=control.GetCameraStatus();
                    if(caps==null || !caps.SupportsCameraOnly || !initial.SupportsCameraOnly)
                        throw new NotSupportedException("This firmware does not support camera-only USB streaming.");
                    if(!caps.CameraOnlyActive){
                        string uid=NpV1Management.UidFromPath(path);control.SelectCameraOnly();
                        control.Dispose();path=NpV1Management.WaitForStreaming(uid);
                    }
                }
                usb=XeNpWinUsbDevice.Open(path);lock(deviceGate)device=usb;
                initial=usb.GetCameraStatus();
                if(save){LastFolder=NpFolderNaming.CreateFresh(outputRoot,NpFolderNaming.Name(sessionLabel,DateTime.Now,"camera_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")));recording=new NpCameraRecording(LastFolder);
                    imu=new NpMediaUsbImu(LastFolder);
                    initial.Save(Path.Combine(LastFolder,"camera_initial_status.txt"));File.WriteAllBytes(Path.Combine(LastFolder,"camera_initial_status.bin"),initial.RawBytes);
                    lock(deviceGate){exposureLog=new StreamWriter(Path.Combine(LastFolder,"camera_exposure.csv"));exposureLog.WriteLine("host_utc,rows_in_reset,ramp_gain,receipt");}}
                NpMediaUsbImu.Attach(parser,imu);
                parser.OnCamera=(data,offset,count,flags,sequence)=>{
                    bool valid=!haveSequence || sequence==expected;
                    if(!valid){SequenceGaps++;frameValid=false;}
                    expected=unchecked((ushort)(sequence+1));haveSequence=true;Packets++;Bytes+=count;
                    if(recording!=null)valid=recording.Write(data,offset,count,flags,sequence) && valid;
                    if((flags&1)!=0){if(frame!=null)camera.ReturnRawFrameBuffer(frame);frame=null;frameBytes=0;frameValid=valid;camera.TryAcquireRawFrameBuffer(out frame);}
                    if(!valid)frameValid=false;
                    if(frame!=null && count<=frame.Length-frameBytes)Buffer.BlockCopy(data,offset,frame,frameBytes,count);
                    else if(frame!=null)frameValid=false;
                    frameBytes+=count;
                    if((flags&2)!=0){if(frameBytes==NpCameraRecording.FrameBytes && frameValid){Frames++;if(frame!=null){camera.PublishFrame(frame);frame=null;}}
                        if(frame!=null)camera.ReturnRawFrameBuffer(frame);frame=null;frameBytes=0;frameValid=false;}
                };
                reader=usb.CreateQueuedReader(64*1024,32,false);started=true;usb.StartCameraOnly((ushort)requested);
                var clock=Stopwatch.StartNew();var update=Stopwatch.StartNew();long previousFrames=0,lastFrameAt=0;
                while(!stopping){int count=reader.ReadInto(block);if(count>0)parser.Feed(block,count,noNeural);
                    if(Frames!=previousFrames){previousFrames=Frames;lastFrameAt=clock.ElapsedMilliseconds;}
                    if(Frames==0 && clock.ElapsedMilliseconds>12000)throw new IOException("No complete camera frame received within 12 seconds.");
                    if(Frames>0 && clock.ElapsedMilliseconds-lastFrameAt>3000)throw new IOException("Camera stopped producing complete frames.");
                    if(update.ElapsedMilliseconds>=1000){var health=usb.GetCameraStatus();
                        if(Frames==0 && health.FailureCount!=0 && health.LastStatus!=0)
                            throw new IOException("Camera startup failed: status "+health.LastStatus+(health.LastStatus==3?" (timeout)":"")+", failures "+health.FailureCount+", SPI=0x"+health.FailureSpiError.ToString("X8")+", DMA=0x"+health.FailureDmaError.ToString("X8")+".");
                        long frames=Frames;string text=frames+" frames / "+(frames/Math.Max(0.001,clock.Elapsed.TotalSeconds)).ToString("F1")+" fps / gaps "+SequenceGaps;
                        if(!IsDisposed && IsHandleCreated)BeginInvoke((Action)(()=>status.Text=text));update.Restart();}}
            } catch(Exception ex){LastError=ex.Message;}
            finally {
                if(usb!=null && started)try{lock(deviceGate)usb.Stop();if(reader!=null){int count;while((count=reader.DrainStoppedInto(block))>0)parser.Feed(block,count,noNeural);}}
                    catch(Exception ex){LastError=(LastError==null?"":LastError+"; ")+"STOP/drain failed: "+ex.Message;}
                if(reader!=null)try{reader.Dispose();}catch(Exception ex){LastError=(LastError==null?"":LastError+"; ")+"Read drain failed: "+ex.Message;}
                if(usb!=null)try{lock(deviceGate)final=usb.GetCameraStatus();}catch(Exception ex){LastError=(LastError==null?"":LastError+"; ")+"Final status unavailable: "+ex.Message;}
                if(frame!=null)camera.ReturnRawFrameBuffer(frame);
                lock(deviceGate){device=null;if(usb!=null)usb.Dispose();if(exposureLog!=null){try{exposureLog.Dispose();}catch(Exception ex){LastError=(LastError==null?"":LastError+"; ")+"Exposure log flush failed: "+ex.Message;}exposureLog=null;}}
                if(recording!=null)try{recording.Dispose();}catch(Exception ex){LastError=(LastError==null?"":LastError+"; ")+"Camera file flush failed: "+ex.Message;}
                if(imu!=null){imu.Dispose();if(imu.Error!=null)LastError=(LastError==null?"":LastError+"; ")+"IMU sidecar failed: "+imu.Error;}
                if(LastFolder!=null){
                    if(final!=null){final.Save(Path.Combine(LastFolder,"camera_final_status.txt"));File.WriteAllBytes(Path.Combine(LastFolder,"camera_final_status.bin"),final.RawBytes);}
                    File.WriteAllText(Path.Combine(LastFolder,"media_capture_status.txt"),"uid="+NpV1Management.UidFromPath(selectedPath)+"\r\nstarted_utc="+began.ToString("O")+"\r\nstopped_utc="+DateTime.UtcNow.ToString("O")+
                        "\r\nrequested_fps="+requested+"\r\nframes="+Frames+"\r\npackets="+Packets+"\r\nbytes="+Bytes+"\r\nsequence_gaps="+SequenceGaps+"\r\nerror="+(LastError??"none")+
                        "\r\nrecording_complete="+(LastError==null && recording!=null && recording.Complete && final!=null && final.LastStatus==0 && final.FailureCount==0 && final.DroppedFrames==0 && final.MissedDeadlines==0)+"\r\naudio_available=false\r\nwire_frame_bytes=160000\r\nwire_width=320\r\nwire_height=500\r\nvisible_width=320\r\nvisible_height=320\r\nencoding=NanEyeC packed 10-bit Bayer\r\npreview=bounded display; raw camera bytes retained; no neural data\r\n");
                    NpRecordingSettings.RefreshBundle(LastFolder);
                }
            }
        }
    }

    // Optional stream-5 consumer. Canonical NpUsbSensors validates packets and
    // writes the held grid; rejected sensors never invalidate camera packets.
    internal sealed class NpMediaUsbImu : IDisposable
    {
        readonly string folder;
        NpUsbSensors sensors;
        FileStream raw;
        StreamWriter index;
        bool disposed,haveRecord;
        uint lastRecord,firstOverflow,lastOverflow,imuError,environmentError,missed,valid;
        uint? firstTick,lastTick,firstObservation,lastObservation;
        long received,records,rejected,recordGaps;
        string lastValidationError;
        internal string Error {get;private set;}
        internal NpMediaUsbImu(string folder)
        {
            this.folder=folder;
            try {
                sensors=new NpUsbSensors(folder);
                raw=new FileStream(Path.Combine(folder,"imu_snapshots.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
                index=new StreamWriter(new FileStream(Path.Combine(folder,"imu_snapshots.csv"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,16384),new System.Text.UTF8Encoding(false));
                index.WriteLine("snapshot_index,packet_record_sequence,observed_device_ms,imu_sequence,imu_device_ms,usable_valid_flags,firmware_queue_loss");
            } catch(Exception ex){Error=ex.Message;}
        }
        internal static void Attach(XeNpPacketParser parser,NpMediaUsbImu sink)
        {parser.OnMisc=(b,o,n)=>{if(sink!=null)sink.Feed(b,o,n);};}
        internal void Feed(byte[] b,int o,int n)
        {
            if(disposed)return;
            received++;
            if(Error!=null)return;
            // A fresh non-saving validator admits each optional packet through
            // the existing ABI/CRC checks. A rejected packet must not poison
            // later sensor record continuity or stop the camera stream.
            using(var validator=new NpUsbSensors(folder,false)) {
                try {validator.Feed(b,o,n);}
                catch(Exception ex){rejected++;lastValidationError=ex.Message;return;}
                try {
                    uint record=BitConverter.ToUInt32(b,o+8),observed=BitConverter.ToUInt32(b,o+12);
                    uint sequence=BitConverter.ToUInt32(b,o+40),tick=BitConverter.ToUInt32(b,o+52);
                    if(haveRecord){uint delta=unchecked(record-lastRecord);if(delta>1 && delta<0x80000000U)recordGaps+=delta-1;else if(delta!=1)recordGaps++;}
                    else firstOverflow=validator.Overflow;
                    haveRecord=true;lastRecord=record;lastOverflow=validator.Overflow;
                    valid=validator.Valid;imuError=validator.ImuError;environmentError=validator.EnvironmentError;missed=validator.MissedEstimate;
                    raw.Write(b,o+20,140);records++;
                    index.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4},{5},{6}",records-1,record,observed,sequence,tick,valid,lastOverflow));
                    if(!firstTick.HasValue){firstTick=tick;firstObservation=observed;}lastTick=tick;lastObservation=observed;
                    sensors.FeedSnapshot(b,o+20,observed);
                } catch(Exception ex){Error=ex.Message;}
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try{if(sensors!=null)sensors.Dispose();}catch(Exception ex){Error=ex.Message;}
            try{if(index!=null){index.Flush();((FileStream)index.BaseStream).Flush(true);}}catch(Exception ex){Error=ex.Message;}
            try{if(index!=null)index.Dispose();}catch(Exception ex){Error=ex.Message;}
            try{if(raw!=null)raw.Flush(true);}catch(Exception ex){Error=ex.Message;}
            try{if(raw!=null)raw.Dispose();}catch(Exception ex){Error=ex.Message;}
            long samples=sensors==null?0:sensors.ImuSamples,rows=sensors==null?0:sensors.Misc1250Records;
            string availability=Error!=null?"file_error":records==0?(received==0?"not_received":"invalid"):samples==0?"invalid":"available";
            try {using(var status=new StreamWriter(new FileStream(Path.Combine(folder,"imu_capture_status.txt"),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new System.Text.UTF8Encoding(false))) {
                status.WriteLine("optional=true; sensor rejection never stops camera capture");
                status.WriteLine("availability="+availability);status.WriteLine("records="+records);status.WriteLine("distinct_imu_samples="+samples);
                status.WriteLine("error="+(Error??"none").Replace('\r',' ').Replace('\n',' '));
                status.WriteLine("received_packets="+received);status.WriteLine("rejected_packets="+rejected);status.WriteLine("last_validation_error="+(lastValidationError??"none"));
                status.WriteLine("record_sequence_gaps="+recordGaps);status.WriteLine("imu_sequence_gaps="+(sensors==null?0:sensors.ImuSequenceGaps));
                status.WriteLine("snapshot_bytes=140");status.WriteLine("native_file=imu_snapshots.bin");status.WriteLine("timestamp_file=imu_snapshots.csv");
                status.WriteLine("derived_1250_rows="+rows);status.WriteLine("analogin_sample_rate_hz=1250");status.WriteLine("analogin_timing=derived held-value grid from source ticks; not native IMU sample rate or a lossless FIFO");
                status.WriteLine("analogin_pc_time=host-local compatibility anchor; not precision clock synchronization");
                status.WriteLine("imu_error="+imuError);status.WriteLine("environment_error="+environmentError);status.WriteLine("usable_valid_flags="+valid);
                status.WriteLine("queue_loss="+lastOverflow);status.WriteLine("queue_loss_first="+firstOverflow);status.WriteLine("missed_estimate="+missed);
                status.WriteLine("first_imu_device_ms="+firstTick);status.WriteLine("last_imu_device_ms="+lastTick);
                status.WriteLine("first_observed_device_ms="+firstObservation);status.WriteLine("last_observed_device_ms="+lastObservation);
                status.WriteLine("source_timing=native device millisecond ticks/sequences in snapshots and CSV; no bulk-arrival clock fit or IMU EP0 polling");
            }}catch(Exception ex){Error=ex.Message;}
        }
    }
}
