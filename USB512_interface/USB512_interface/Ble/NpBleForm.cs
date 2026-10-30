using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;
using WildNpConfiguration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace USB512_interface
{
    internal sealed partial class NpBleForm : Form
    {
        sealed class Peer
        {
            internal ulong Address; internal BluetoothAddressType Type; internal string Name; internal short Rssi;
            public override string ToString() { return Name + " — " + Address.ToString("X12") + " (" + Rssi + " dBm)"; }
        }
        sealed class Profile
        {
            internal uint Mask, Hz;
            public override string ToString() {
                if(Mask==0)return "Camera / Microphone";
                string label=NpCaptureRequest.Ports((int)Mask);
                return label+" | "+Hz.ToString("N0")+" samples/s"+(Mask==7&&Hz==30000?" — experimental; not sustained-qualified":"");
            }
        }
        sealed class ConfigTransport : INpConfigTransport, INp2ConfigTransport
        {
            readonly NpBleConnection connection;
            readonly Action<byte[]> progress;
            internal ConfigTransport(NpBleConnection connection,Action<byte[]> progress=null) { this.connection = connection;this.progress=progress; }
            public Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancel) { progress?.Invoke(request);return connection.ExchangeAsync(0xb8, request, cancel); }
        }
        readonly NpBleConnection connection = new NpBleConnection();
        readonly BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        readonly object advertisementToken,watcherStoppedToken;
        readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        readonly Dictionary<ulong,Dictionary<byte,NpConfigRequest>> v1HostConfigurations = new Dictionary<ulong,Dictionary<byte,NpConfigRequest>>();
        ulong connectedAddress;
        readonly Dictionary<ulong,Dictionary<int,string>> np2HostConfigurations = new Dictionary<ulong,Dictionary<int,string>>();
        readonly Dictionary<ulong,HashSet<int>> np2ApplyConfigurations = new Dictionary<ulong,HashSet<int>>();
        readonly ComboBox devices = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox profiles = new ComboBox { Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Label recordingSummary = new Label {AutoSize=true,Text="Connect to read recording settings."};
        readonly Label recordingTime = new Label {Name="bleRecordingTime",AutoSize=true,Text="Recording time 00:00:00",Font=WildXTheme.Strong};
        ulong recordingSeconds;
        readonly TextBox address = new TextBox { Width = 160 };
        readonly CheckBox poll = new CheckBox { Text = "Refresh status every 2 seconds", Checked = true, AutoSize = true };
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly Label state = new Label { Text = "Scan or enter a BLE address, then connect.", AutoSize = true, MaximumSize = new Size(850, 0) };
        readonly Label firmwareStatus = new Label { AutoSize=true,Visible=false,MaximumSize=new Size(850,0) };
        NpFirmwareIdentity firmwareIdentity;
        internal event Action FirmwareReviewRequested;
        readonly FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        readonly FlowLayoutPanel maintenanceActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        readonly Label maintenancePeer = new Label { AutoSize = true, Margin = new Padding(0,0,0,12) };
        readonly Label maintenanceResult = new Label { AutoSize = true, MaximumSize = new Size(850,0) };
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly NpBleClock clock=new NpBleClock();
        readonly Label clockStatus=new Label{AutoSize=true};
        readonly Label configurationStatus=new Label{AutoSize=true,MaximumSize=new Size(850,0)};
        readonly NpBleConfigurationReceipts configurationReceipts=new NpBleConfigurationReceipts();
        readonly HashSet<Tuple<uint,uint,uint>> acknowledgedSdProfiles=new HashSet<Tuple<uint,uint,uint>>();
        string configurationReadError;
        Form preview;
        NpSdStatus current;
        int recorderVersion;
        uint np2AvailableMask;
        Button scan,stopScan,connect,disconnect,rescueReset,recordingSettings,usbDownload,previewButton,startRecording,stopRecording,recordingsButton,overwriteRecording;
        bool busy, closing, connecting, hostBusy, mediaSettingsChanged,stopRequested,stopping,refreshingPeers;
        System.Diagnostics.Stopwatch pendingStop;
        int pendingStopEpoch;
        ulong pendingStopPeer;
        bool pendingStopAcknowledged;
        internal bool IsBusy{get{return busy;}}
        internal bool SessionOwned{get{return busy || connection.Connected;}}
        internal event Action StateChanged;
        internal Control MaintenanceControls { get { return maintenanceActions; } }
        internal void SetHostBusy(bool value){if(hostBusy==value)return;hostBusy=value;if(value)CancelBleImuPoll();UpdateControls();}
        IWin32Window DialogOwner{get{return TopLevelControl??this;}}
        uint sequence = unchecked((uint)Environment.TickCount);
        bool V2 { get { return recorderVersion == 2; } }
        Dictionary<byte,NpConfigRequest> V1Configurations()
        {
            if(connectedAddress==0)throw new IOException("Connect the V1 recorder before editing or applying probe settings.");
            Dictionary<byte,NpConfigRequest> requests;
            if(!v1HostConfigurations.TryGetValue(connectedAddress,out requests)){
                requests=new Dictionary<byte,NpConfigRequest>();v1HostConfigurations.Add(connectedAddress,requests);
            }
            return requests;
        }
        HashSet<int> Np2ApplyRequests(ulong peer,bool create=false)
        {
            HashSet<int> requests;
            if(!np2ApplyConfigurations.TryGetValue(peer,out requests) && create){requests=new HashSet<int>();np2ApplyConfigurations.Add(peer,requests);}
            return requests;
        }
        internal NpBleForm():this(false){}
        internal NpBleForm(bool maintenance)
        {
            SuspendLayout();
            Font=WildXTheme.Body;Padding=new Padding(12);
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            Text = "WILD-X — BLE Control"+(maintenance?" / Advanced":""); Icon = NpPreviewTheme.ApplicationIcon;
            ClientSize = new Size(910, 610); MinimumSize = new Size(820, 550); StartPosition = FormStartPosition.CenterParent;
            var connectionActions=new FlowLayoutPanel();
            var top=FieldGrid("Device",devices,"BLE address",address,connectionActions);top.Name="bleConnectionFields";
            scan=Add(connectionActions, "Scan", () => { StartScan(); return Task.CompletedTask; });
            stopScan=Add(connectionActions, "Stop scan", () => { watcher.Stop(); return Task.CompletedTask; });
            connect=Add(connectionActions, "Connect", Connect);
            disconnect=new Button {Text="Disconnect",AutoSize=true};connectionActions.Controls.Add(disconnect);
            disconnect.Click+=async delegate {
                // Local cancellation must not wait behind Run's connecting lease.
                if(connecting){connection.Disconnect();state.Text="Canceling connection…";return;}
                await Run(()=>{connection.Disconnect();ResetDevice();state.Text="Disconnected.";return Task.CompletedTask;});
            };
            maintenanceActions.Controls.Add(maintenancePeer);maintenanceActions.SetFlowBreak(maintenancePeer,true);
            rescueReset=Add(maintenanceActions,"Rescue reset",RescueReset);
            var options=new FlowLayoutPanel {Visible=false};
            InitializeMediaControls(options);
            var settings=FieldGrid("Recording",recordingSummary,null,null,options);settings.Name="bleRecordingFields";
            profiles.SelectedIndexChanged+=delegate {recordingSummary.Text=profiles.SelectedItem==null?"Connect to read recording settings.":profiles.SelectedItem+" · continuous until Stop";};
            Add(actions, "Read status", async () => { await ReadStatus(); });
            startRecording=Add(actions, "Start recording", StartRecording);
            stopRecording=new Button {Text="Stop recording",AutoSize=true};actions.Controls.Add(stopRecording);
            stopRecording.Click+=async delegate {
                if(!connection.Connected){StopHostRecordingExplicitly();UpdateControls();return;}
                if(hostBusy || stopping || stopRequested || pendingStop!=null || !connection.Connected)return;
                StopHostRecordingExplicitly();
                stopRequested=true;CancelBleFirstSyncStart();UpdateControls();
                if(busy){Append("Stop requested; waiting for the current transfer to finish. Pending capture start will be canceled.");return;}
                await Run(StopRecording);
            };
            recordingSettings=Add(actions,"Recording settings",OpenRecordingSettings);
            var sdTestButton=new Button {Text="SD card test…",AutoSize=true};actions.Controls.Add(sdTestButton);
            sdTestButton.Click+=delegate {if(!busy&&!hostBusy)using(var dialog=new NpSdTestForm(this))dialog.ShowDialog(DialogOwner);};
            Add(actions, "Sync PC time", () => SyncClock());
            previewButton=Add(actions,"NP Preview",()=>{OpenPreview();return Task.CompletedTask;});
            recordingsButton=Add(actions,"Recordings…",OpenRecordings);
            overwriteRecording=Add(actions,"Overwrite from last known position…",OverwriteFromLastKnownPosition);
            var reportButton=new Button {Text="Report…",AutoSize=true};
            reportButton.Click+=async delegate {await OpenReport();};
            Add(actions,"Calibration…",()=>{Dictionary<int,string> requests=null;if(V2)np2HostConfigurations.TryGetValue(connectedAddress,out requests);
                else if(connectedAddress!=0)requests=V1Configurations().Values.ToDictionary(p=>p.Port+1,p=>p.Json);
                NpCalibrationStore.Show(DialogOwner,"ble:"+address.Text.Trim().ToUpperInvariant(),V2,requests);return Task.CompletedTask;});
            Add(actions, "Recover interrupted catalog", async () => {
                var s = await ReadStatus();
                if (V2 && !NpV2SdControl.CanRecover(s)) throw new IOException(NpV2SdControl.RecoveryRequiresUpgrade(s)?"Firmware does not advertise stored 7.5-kHz catalog recovery. Upgrade before recovery; raw download remains available.":"Firmware does not advertise a recoverable interrupted allocation.");
                await Send(V2 ? NpV2SdControl.Request(8, NpV2SdControl.NextId(s)) : NpV1BleProtocol.Recover(s, NpSdControl.NextId(s)));
            });
            Add(actions, "USB streaming", () => SwitchMode(NpV1BleProtocol.DestinationUsbStreaming, 2));
            usbDownload=Add(actions, "USB download", () => { if (V2) throw new IOException("For NP2, enter USB service, then select Download in Firmware Update."); return SwitchMode(NpV1BleProtocol.DestinationUsbDownload, 2); });
            Add(maintenanceActions, "Firmware loader", () => SwitchMode(NpV1BleProtocol.DestinationLoader, 1));
            Add(maintenanceActions, "ROM DFU", () => SwitchMode(NpV1BleProtocol.DestinationDfu, 3));
            Add(maintenanceActions,"Bundled firmware files…",()=>{NpFirmwareNotice.OpenFiles();return Task.CompletedTask;});
            maintenanceActions.SetFlowBreak(maintenanceActions.Controls[maintenanceActions.Controls.Count-1],true);
            maintenanceActions.Controls.Add(maintenanceResult);
            var statusPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=Padding.Empty };
            statusPanel.Controls.Add(recordingTime);statusPanel.Controls.Add(state);statusPanel.Controls.Add(firmwareStatus);statusPanel.Controls.Add(mediaStatus);statusPanel.Controls.Add(clockStatus);statusPanel.Controls.Add(configurationStatus);statusPanel.Controls.Add(reportButton);
            statusPanel.SizeChanged+=delegate {foreach(var label in new[]{state,firmwareStatus,mediaStatus,clockStatus,configurationStatus})label.MaximumSize=new Size(Math.Max(1,statusPanel.ClientSize.Width),0);};
            InitializeBleImu(statusPanel);
            maintenanceActions.SizeChanged+=delegate {maintenancePeer.MaximumSize=new Size(Math.Max(1,maintenanceActions.ClientSize.Width),0);};
            foreach(var label in new[]{mediaStatus,clockStatus,configurationStatus}) {
                label.Visible=false;label.TextChanged+=delegate {label.Visible=label.Text.Length!=0;};
            }
            Controls.Add(log); Controls.Add(statusPanel); Controls.Add(actions); Controls.Add(settings);Controls.Add(BuildBleSessionControls());Controls.Add(top);
            if(maintenance)Controls.Add(maintenanceActions);
            devices.SelectedIndexChanged += delegate { if(!refreshingPeers)SelectCurrentPeer(); };
            // Use the same native WinRT registration as connection discovery.
            // The Framework projected event cache missed live advertisements.
            advertisementToken=watcher.GetType().GetMethod("add_Received").Invoke(watcher,new object[]{
                new Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher,BluetoothLEAdvertisementReceivedEventArgs>(Advertisement)});
            watcherStoppedToken=watcher.GetType().GetMethod("add_Stopped").Invoke(watcher,new object[]{
                new Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher,BluetoothLEAdvertisementWatcherStoppedEventArgs>(WatcherStopped)});
            connection.Disconnected += message => Ui(() => { if(connection.Connected)return;ResetDevice();state.Text = message; Append(message);NotifyState(); });
            // A failed read clears current, but must not permanently disable
            // read-only status/heartbeat recovery on a still-live connection.
            timer.Tick += async delegate { if (!busy && !hostBusy && refreshTask.IsCompleted && (poll.Checked || pendingStop!=null) && connection.Connected) {refreshTask=RefreshStatusQuietly();await refreshTask;} };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; return; } Shutdown(); };
            Profiles(null); timer.Start();WildXTheme.Apply(this);
            WildXTheme.Apply(maintenanceActions);maintenancePeer.ForeColor=maintenanceResult.ForeColor=WildXTheme.Muted;
            foreach(Control control in maintenanceActions.Controls)if(control is Button && (control.Text=="ROM DFU" || control.Text=="Rescue reset"))WildXTheme.Button((Button)control,false,true);
            WildXTheme.Button(connect,true);
            foreach(Control control in actions.Controls)if(control is Button){
                if(control.Text=="Start recording")WildXTheme.Button((Button)control,true);
                else if(control.Text=="Stop recording" || control.Text=="ROM DFU" || control==overwriteRecording)WildXTheme.Button((Button)control,false,true);
            }
            foreach(var row in new[]{connectionActions,options,actions,maintenanceActions,statusPanel})foreach(Control control in row.Controls) {
                control.Anchor=AnchorStyles.Left;
                if(control is Button){control.MinimumSize=new Size(0,36);((Button)control).AutoSizeMode=AutoSizeMode.GrowAndShrink;}
                if(row==options)control.Margin=new Padding(0,0,16,0);
            }
            foreach(var label in new[]{recordingTime,state,firmwareStatus,clockStatus,configurationStatus})label.Margin=new Padding(0,0,0,6);
            state.ForeColor=firmwareStatus.ForeColor=clockStatus.ForeColor=configurationStatus.ForeColor=WildXTheme.Muted;
            recordingTime.Font=WildXTheme.Strong;
            UpdateControls();ResumeLayout(true);
        }
        static TableLayoutPanel FieldGrid(string leftTitle,Control left,string rightTitle,Control right,FlowLayoutPanel below)
        {
            bool paired=right!=null;
            var grid=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=paired?2:1,RowCount=3,
                Margin=Padding.Empty,Padding=new Padding(0,0,0,12)};
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));if(paired)grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));
            for(int row=0;row<3;row++)grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(WildXTheme.Label(leftTitle),0,0);
            left.Anchor=AnchorStyles.Left|AnchorStyles.Right;left.Margin=new Padding(0,0,paired?16:0,0);grid.Controls.Add(left,0,1);
            if(paired){grid.Controls.Add(WildXTheme.Label(rightTitle),1,0);right.Anchor=AnchorStyles.Left|AnchorStyles.Right;right.Margin=Padding.Empty;grid.Controls.Add(right,1,1);}
            below.AutoSize=true;below.AutoSizeMode=AutoSizeMode.GrowAndShrink;below.Dock=DockStyle.Top;
            below.WrapContents=true;below.Margin=new Padding(0,12,0,0);below.Padding=Padding.Empty;
            grid.Controls.Add(below,0,2);grid.SetColumnSpan(below,paired?2:1);return grid;
        }
        internal void StartScan()
        {
            if(closing || connection.Connected)return;
            try {
                if(!IsHandleCreated)CreateHandle();
                if(watcher.Status!=BluetoothLEAdvertisementWatcherStatus.Started)watcher.Start();
                state.Text="Scanning…";
            }catch(Exception ex){state.Text="BLE scan unavailable: "+ex.Message;Append(state.Text);}
            UpdateControls();
        }
        void SelectCurrentPeer()
        {
            var peer=devices.SelectedItem as Peer;
            if(peer!=null)address.Text=peer.Address.ToString("X12");
        }
        void ObservePeer(ulong value,BluetoothAddressType type,string name,short rssi)
        {
            if(closing || busy || connection.Connected)return;
            Peer peer;bool added=!peers.TryGetValue(value,out peer);
            if(added){peer=new Peer{Address=value};peers.Add(value,peer);}
            peer.Type=type;peer.Rssi=rssi;
            if(!String.IsNullOrWhiteSpace(name))peer.Name=name;
            if(String.IsNullOrWhiteSpace(peer.Name))peer.Name="FFF0 device";
            // Replacing a displayed item also raises selection events. An RSSI
            // refresh must not replace an address the user is typing.
            refreshingPeers=true;
            try {if(added)devices.Items.Add(peer);else devices.Items[devices.Items.IndexOf(peer)]=peer;}
            finally {refreshingPeers=false;}
            if(String.IsNullOrWhiteSpace(address.Text)){
                if(devices.SelectedIndex<0)devices.SelectedIndex=0;
                SelectCurrentPeer();
            }
        }
        void WatcherStopped(BluetoothLEAdvertisementWatcher sender,BluetoothLEAdvertisementWatcherStoppedEventArgs e)
        {
            Ui(()=>{if(e.Error!=Windows.Devices.Bluetooth.BluetoothError.Success)state.Text="BLE scan stopped: "+e.Error;UpdateControls();NotifyState();});
        }
        void NotifyState(){var changed=StateChanged;if(!closing && changed!=null)changed();}
        void Shutdown()
        {
            if(closing)return;ShutdownBleImu();RetireBleRecordingMetadata();closing=true;ClosePreview();timer.Stop();
            if(advertisementToken!=null)watcher.GetType().GetMethod("remove_Received").Invoke(watcher,new[]{advertisementToken});
            if(watcherStoppedToken!=null)watcher.GetType().GetMethod("remove_Stopped").Invoke(watcher,new[]{watcherStoppedToken});
            if(watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started)watcher.Stop();connection.Dispose();timer.Dispose();
            profiles.Dispose();poll.Dispose();
        }
        protected override void Dispose(bool disposing){if(disposing){Shutdown();maintenanceActions.Dispose();}base.Dispose(disposing);}
        void Ui(Action action) { if (!closing && !IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        void Advertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            string name = e.Advertisement.LocalName;
            bool known = !String.IsNullOrWhiteSpace(name) && (name.IndexOf("NP", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("WILD", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("CE64", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("HJ-185", StringComparison.OrdinalIgnoreCase));
            if (!known && !e.Advertisement.ServiceUuids.Contains(new Guid("0000fff0-0000-1000-8000-00805f9b34fb"))) return;
            Ui(() => ObservePeer(e.BluetoothAddress,e.BluetoothAddressType,name,e.RawSignalStrengthInDBm));
        }
        Button Add(FlowLayoutPanel panel, string title, Func<Task> action)
        {
            var button = new Button { Text = title, AutoSize = true }; panel.Controls.Add(button);
            button.Click += async delegate {
                if(busy || hostBusy)return;
                if(panel==maintenanceActions)maintenanceResult.Text="";
                await Run(action);
                if(panel==maintenanceActions)maintenanceResult.Text=state.Text;
            };
            return button;
        }
        void UpdateControls()
        {
            bool linked=connection.Connected,ready=linked && recorderVersion!=0 && !busy && !hostBusy;
            bool folderEditable=!busy && !hostBusy && pendingStop==null && (current==null || !(V2?current.State==3:current.State==1)) && !HasHostRecording();
            bleDestinationFolder.Enabled=bleSelectFolder.Enabled=folderEditable;
            devices.Enabled=address.Enabled=!busy && !hostBusy && !linked;
            scan.Enabled=connect.Enabled=!busy && !hostBusy && !linked;
            stopScan.Enabled=!busy && !hostBusy && watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started;
            disconnect.Text=connecting?"Cancel connection":"Disconnect";
            disconnect.Enabled=connecting || (!busy && !hostBusy && linked);
            foreach(Control control in maintenanceActions.Controls)if(control is Button)control.Enabled=ready;
            rescueReset.Enabled=ready && V2;
            maintenancePeer.Text=linked?"Connected · "+address.Text.Trim()+
                (recorderVersion==0?"":"\r\nInstalled: "+NpFirmwareCatalog.Bundled.Installed((uint)recorderVersion,firmwareIdentity)+
                "\r\nBundled: "+(NpFirmwareCatalog.Bundled.For((uint)recorderVersion)?.Versions??"Unavailable")):"Connect in BLE Mode";
            profiles.Enabled=poll.Enabled=ready;
            foreach(Control control in actions.Controls)control.Enabled=ready;
            stopRecording.Enabled=!hostBusy && !stopping && !stopRequested && pendingStop==null &&
                (linked && recorderVersion!=0 || HasHostRecording());
            recordingSettings.Enabled=ready && pendingStop==null && (V2?NpV2SdControl.CanConfigure(current):NpV1BleProtocol.CanManageStorage(current));
            usbDownload.Visible=recorderVersion==1;
            previewButton.Visible=recorderVersion!=0;previewButton.Enabled=ready && (V2?NpV2SdControl.CanPreview(current):NpV1BlePreviewForm.CanPreview(current));
            recordingsButton.Enabled=ready;
            // Manual V2 START is a request, not a cached host admission decision.
            // Firmware owns storage/profile acceptance and returns its rejection.
            startRecording.Enabled=ready && pendingStop==null && (V2 || NpV1BleProtocol.CanManualStart(current));
            UpdateOverwriteControl(ready);
            UpdateMediaControls();
        }
        void UpdateOverwriteControl(bool ready)
        {
            overwriteRecording.Visible=V2 && NpV2SdControl.HasOverwriteOffer(current);
            overwriteRecording.Enabled=ready && V2 && NpV2SdControl.CanOverwriteFromLastKnownPosition(current);
        }
        void ClosePreview(){if(preview!=null){preview.Close();preview.Dispose();preview=null;}}
        void ResetDevice(){ResetBleFirstSync();ClearBleProbePreparation();ResetBleImu();ForgetPendingStop();RetireBleRecordingMetadata();bleDeviceUid=null;ClosePreview();ResetMedia();recordingSeconds=0;recordingTime.Text=RecordingTimeText(0);connectedAddress=0;mediaSettingsChanged=stopRequested=false;clock.Reset();clockStatus.Text="";configurationReceipts.Reset();acknowledgedSdProfiles.Clear();configurationStatus.Text="";configurationReadError=null;current=null;recorderVersion=0;np2AvailableMask=0;firmwareIdentity=null;firmwareStatus.Visible=false;firmwareStatus.Text="";Profiles(null);UpdateControls();}
        async Task Run(Func<Task> action)
        {
            if (busy || hostBusy) return; busy = true; UpdateControls();
            NotifyState();
            try { CancelBleImuPoll();await RetireBleRecordingClock();await bleImuTask;await refreshTask;await action(); }
            catch (Exception ex) {
                // A CRC/identity-validated rejection still contains fresh device
                // state. A transport failure does not establish a new state.
                var rejected=ex as NpSdCommandException;
                if(rejected!=null && connection.Connected && recorderVersion==rejected.Version){ShowStatus(rejected.Status);ObserveBleRecordingMetadata(rejected.Status);}
                else current=null;
                state.Text=ex.Message;Append(ex.Message);
            }
            finally {
                busy = false; UpdateControls();NotifyState();
                if(stopRequested && connection.Connected && !hostBusy)await Run(StopRecording);
                else OfferFirmwareReview();
            }
        }
        void OfferFirmwareReview()
        {
            if(!Visible || !ContainsFocus || busy || hostBusy || pendingStop!=null || !connection.Connected || current==null || current.Busy || current.Fault!=0 ||
                !(V2?(current.State==1 || current.State==5 || current.State==7):(current.State==0 || current.State==3)))return;
            if(NpFirmwareNotice.Offer(DialogOwner,"ble:"+address.Text.Trim(),NpFirmwareCatalog.Bundled.For((uint)recorderVersion),firmwareIdentity))
                FirmwareReviewRequested?.Invoke();
        }
        void Append(string message) {
            string line=DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine;log.AppendText(line);
            string folder=blePendingSessionFolder??bleMetadataFolder;
            if(!String.IsNullOrEmpty(folder))try{File.AppendAllText(Path.Combine(folder,"ble_session.log"),line);}catch{ /* A log write cannot interrupt device control. */ }
        }
        async Task OpenReport()
        {
            string details="";
            string failure=current!=null && current.Fault!=0?"Original failure status:\r\n"+current:"";
            if(!busy && !hostBusy && connection.Connected && recorderVersion==1){
                details="V1 probe receipts unavailable: status read did not complete.";
                await Run(async()=>{
                    var s=await ReadStatus();int epoch=connection.Epoch;
                    using(var timeout=new CancellationTokenSource(4500)){
                        var client=new NpConfigClient(new ConfigTransport(connection));
                        details=await ReadV1FailureDetails(s,V1Configurations(),async(port,cancel)=>{
                            if(!connection.Connected || connection.Epoch!=epoch || recorderVersion!=1)throw new IOException("Connection changed; diagnostic discarded.");
                            var reply=await client.StatusAsync(port,cancel);
                            if(!connection.Connected || connection.Epoch!=epoch || recorderVersion!=1)throw new IOException("Connection changed; diagnostic discarded.");
                            return reply;
                        },timeout.Token);
                    }
                });
            }
            string text="Console: "+typeof(NpBleForm).Assembly.GetName().Version+"\r\nDevice: "+(recorderVersion==0?"Unconfirmed":"Neuropixels V"+recorderVersion)+
                "\r\nBLE address: "+address.Text+"\r\nRecording metadata: "+(bleMetadataFolder??"No host recording receipt")+FirstSyncEvidenceReport()+"\r\n"+firmwareStatus.Text+"\r\n"+recordingTime.Text+"\r\n"+state.Text+
                "\r\n"+clockStatus.Text+"\r\n"+configurationStatus.Text+"\r\n\r\nDevice status:\r\n"+current+
                (current==null?"":"\r\nFlags 0x"+current.Flags.ToString("X8")+"; next/start LBA "+current.Start+"; end LBA "+current.End+
                    "; last command "+current.LastId+"; receipt "+current.Operation+"/"+current.RequestId+"; command error "+current.Error)+
                "\r\n\r\n"+failure+"\r\n\r\n"+details+"\r\n\r\n"+log.Text;
            using(var report=new NpReportForm("BLE report",text))report.ShowDialog(DialogOwner);
        }
        internal static async Task<string> ReadV1FailureDetails(NpSdStatus s,IDictionary<byte,NpConfigRequest> requests,
            Func<byte,CancellationToken,Task<NpConfigReply>> read,CancellationToken cancel)
        {
            if(s==null || s.Fault==0 || s.Mask<1 || s.Mask>7 || !NpV1BleProtocol.CanManageStorage(s))
                return "Probe receipts not queried: a finalized, stopped V1 probe failure is required.";
            var result=new System.Text.StringBuilder("Cached V1 probe configuration receipts\r\nSTATUS error 0 confirms the command only, not probe initialization. These receipts do not expose the I2C / capture-lock failure stage or detected physical probe model.\r\nBLE host requests below are separate from USB-mode requests; they are not assumed identical. Removing a host request does not clear firmware-held maps.\r\n");
            for(byte port=0;port<3;port++)if((s.Mask&(1U<<port))!=0){
                result.Append("\r\nProbe ").Append(port+1).Append(": ");
                try {
                    cancel.ThrowIfCancellationRequested();var reply=await read(port,cancel);
                    string[] states={"No staged map","Receiving","Staged","Applied","Failed"};
                    result.Append("configuration ").Append(reply.PortState<states.Length?states[reply.PortState]:"Unknown").Append(" (").Append(reply.PortState).Append(")")
                        .Append("; verification 0x").Append(reply.Verification.ToString("X2")).Append("; STATUS error ").Append(reply.Error)
                        .Append("; transfer state ").Append(reply.TransferState).Append("\r\nDevice CRC32: ").Append(reply.PayloadCrc.ToString("X8"))
                        .Append("\r\nDevice configuration ID: ").Append(reply.ConfigurationId==null?"unavailable":BitConverter.ToString(reply.ConfigurationId).Replace("-",""));
                    NpConfigRequest request;
                    if(requests.TryGetValue(port,out request)){
                        result.Append("\r\nBLE host request: ").Append(request.Selection[4]==2?"NP1 / profile Np1":"NRIC1384 / profile Nric1384")
                            .Append("; selection model ").Append(request.Selection[4]).Append("; CRC32 ").Append(NpConfigClient.Crc(request.Selection,request.Selection.Length).ToString("X8"))
                            .Append("\r\nHost configuration ID: ").Append(BitConverter.ToString(request.Selection,8,32).Replace("-",""));
                        try{NpConfigClient.RequireApplied(reply,request.Selection);result.Append("\r\nMatching applied configuration receipt; analog signal not verified.");}
                        catch(IOException ex){result.Append("\r\nRequested configuration NOT confirmed applied: ").Append(ex.Message);}
                    } else result.Append("\r\nNo host map retained for comparison; device defaults/previous settings are not inferred.");
                }catch(Exception ex) when(ex is IOException || ex is TimeoutException || ex is OperationCanceledException){
                    result.Append("unavailable: ").Append(ex.Message);
                    if(cancel.IsCancellationRequested)break;
                }
                result.Append("\r\n");
            }
            return result.ToString();
        }
        uint Next() { uint n = unchecked(++sequence); return n == 0 ? ++sequence : n; }
        async Task Connect()
        {
            if(String.IsNullOrWhiteSpace(address.Text))SelectCurrentPeer();
            ulong value;
            string selected = address.Text.Replace(":", "").Replace("-", "").Trim();
            if (selected.Length != 12 || !ulong.TryParse(selected, System.Globalization.NumberStyles.HexNumber, null, out value) || value == 0) throw new IOException("Select or enter the exact 12-digit BLE address.");
            address.Text=selected.ToUpperInvariant();
            var witness=devices.SelectedItem as Peer;
            BluetoothAddressType? observedType=witness!=null && witness.Address==value && witness.Type!=BluetoothAddressType.Unspecified
                ? (BluetoothAddressType?)witness.Type : null;
            watcher.Stop(); ResetDevice(); state.Text = "Connecting " + selected + "...";
            connecting=true;UpdateControls();
            try {
                await connection.ConnectAsync(value,observedType);
                connectedAddress=value;
                var s=await ReadStatus();if(V2)await InitializeClock(s);
                await WarmBleFirstSyncCapabilities();
                if(!connection.Connected)throw new IOException("BLE disconnected during initialization.");
                Append("Connected to " + selected + ".");
            }catch{connection.Disconnect();ResetDevice();throw;}
            finally{connecting=false;UpdateControls();}
        }
        void ShowStatus(NpSdStatus s){
            ObserveRecordingClockStatus(s);
            ObserveSdProfileContext(s);
            if(!V2 && s.Fault!=0 && (current==null || current.Fault!=s.Fault || current.Chunks!=s.Chunks))Append("V1 "+NpV1BleProtocol.StateText(s));
            current=s;state.Text=V2?NpV2SdControl.Describe(s):"WILD-X V1 — "+NpV1BleProtocol.StateText(s);
            if(V2 || s.Mask!=0 || s.Chunks==0)recordingSeconds=V2?NpV2SdControl.RecordedSeconds(s):NpV1BleProtocol.RecordedSeconds(s);
            recordingTime.Text=RecordingTimeText(recordingSeconds)+RecordingStorageText(V2,s);
            firmwareStatus.Visible=recorderVersion!=0;firmwareStatus.Text="Firmware: "+NpFirmwareCatalog.Bundled.Installed((uint)recorderVersion,firmwareIdentity);
            if(s.Operation==0)ObservePendingStop(s);}
        static string RecordingTimeText(ulong seconds){return "Recording time "+(seconds/3600).ToString("00")+":"+(seconds/60%60).ToString("00")+":"+(seconds%60).ToString("00");}
        static string RecordingStorageText(bool v2,NpSdStatus s){
            if(s==null)return "";
            return "   |   Recorded size "+StorageSize((ulong)s.Chunks*(v2?46592UL:65536UL))+
                (s.Capacity==0?"":"   |   SD capacity "+StorageSize((ulong)s.Capacity*512));
        }
        static string StorageSize(ulong bytes){string[] units={"B","KiB","MiB","GiB","TiB"};double size=bytes;int unit=0;while(size>=1024 && unit<units.Length-1){size/=1024;unit++;}return size.ToString(unit==0?"F0":"F2")+" "+units[unit];}
        async Task<NpSdStatus> Send(byte[] request)
        {
            if(recorderVersion==0)throw new IOException("Connect and read the device status first.");
            // Preserve the last attempted ID even if its acknowledgement is
            // lost, so a subsequent STOP cannot reuse that mutation's ID.
            sequence=NpSdGeometry.U(request,12);
            int epoch=connection.Epoch,version=recorderVersion;ulong peer=connectedAddress;
            var body = await connection.ExchangeAsync(request); uint op = NpSdGeometry.U(request, 8), id = NpSdGeometry.U(request, 12);
            if(!connection.Connected || connection.Epoch!=epoch || recorderVersion!=version || connectedAddress!=peer)
                throw new IOException("Connection changed before the command receipt. Read status; no command replayed.");
            var s=NpBleConnection.ParseStatus(body,version,op,id);ShowStatus(s);ObserveBleRecordingMetadata(s);
            if(op==0)await ObserveBleAcquisitionAnchor(s);
            return s;
        }
        async Task<NpSdStatus> ReadStatus()
        {
            if(recorderVersion==0){
                var detected=await NpBleConnection.ReadDevice(connection.ExchangeAsync,Next);
                recorderVersion=detected.Item1;ShowStatus(detected.Item2);
                if(V2){
                    var caps=await new Np2ConfigClient(new ConfigTransport(connection)).CapsAsync(0,CancellationToken.None);
                    if(caps.AvailableMask!=3U && caps.AvailableMask!=7U)throw new IOException("Unsupported NP2 connector capability.");
                    np2AvailableMask=caps.AvailableMask;
                    bleDeviceUid=caps.Uid0.ToString("X8")+caps.Uid1.ToString("X8")+caps.Uid2.ToString("X8");
                    ObserveRecordingClockStatus(current);
                    configurationStatus.Text="NP2 IMRO application not verified in this connection.";
                }
                Profiles(current);if(!V2)await RefreshV1Media(current);UpdateControls();
                // This identity capability is V2-only; V1 uses the same bit for FS2 storage.
                if(V2 && (current.Flags&NpFirmwareIdentity.BleCapability)!=0){
                    firmwareIdentity=NpFirmwareIdentity.Parse(await connection.ExchangeAsync(NpFirmwareIdentity.BleCommand,new byte[0],CancellationToken.None),(uint)recorderVersion);
                    ShowStatus(current);UpdateControls();
                }
                return current;
            }
            var s = await Send(V2 ? NpV2SdControl.Request(0, Next()) : NpSdControl.Request(0, Next()));
            Profiles(s);
            if(V2)await CheckConfiguration(s);
            else await RefreshV1Media(s);
#if NP_PREPARATION_ENABLE
            await ObserveProbePreparation(s);
#endif
            return s;
        }
        async Task CheckConfiguration(NpSdStatus s)
        {
            if(configurationReceipts.Pending==0)return;
            try {
                var client=new Np2ConfigClient(new ConfigTransport(connection));
                string message=await configurationReceipts.Observe(s.State,client.StatusAsync,CancellationToken.None);
                if(message!=null){configurationStatus.Text=message;Append(message);}
                configurationReadError=null;
            }catch(Exception ex){
                // Missing receipts affect confidence, not recording. Retry only
                // this read on a later status poll; never replay START or COMMIT.
                if(!V2 || !connection.Connected)return;
                configurationStatus.Text="NP2 IMRO application unconfirmed; receipt read will retry.";
                if(configurationReadError!=ex.Message)Append(configurationStatus.Text+" "+ex.Message);
                configurationReadError=ex.Message;
            }
        }
        NpUsbRecordingProfile RecordingProfile()
        {
            var selected=profiles.SelectedItem as Profile;
            var choices=profiles.Items.Cast<Profile>().Where(p=>p.Mask!=0).ToArray();
            uint capacity=current==null?0:current.Capacity;
            return new NpUsbRecordingProfile {
                AvailableMask=choices.Aggregate(0,(mask,p)=>mask|(int)p.Mask),SelectedMask=selected==null?0:(int)selected.Mask,
                SelectedHz=selected==null?0:(int)selected.Hz,Rates=choices.Select(p=>(int)p.Hz).Distinct().OrderBy(hz=>hz).ToArray(),
                Status="Neuropixels V"+recorderVersion+" · SD recording\r\n"+(V2?NpV2SdControl.Describe(current):NpV1BleProtocol.StateText(current)),
                Validate=(mask,hz)=>choices.Any(p=>p.Mask==(uint)mask && p.Hz==(uint)hz)?null:"This probe combination / sample rate is not supported by the device.",
                Warning=(mask,hz)=>NpCaptureMode.Count(mask)>1 && hz==30000 && !acknowledgedSdProfiles.Contains(Tuple.Create((uint)mask,(uint)hz,capacity))?
                    (V2?"Multi-probe 30 kHz recording is experimental. Continue with this profile?":
                        "Two-probe 30 kHz SD recording has shown sustained-recording failures. Data may be incomplete. Continue with this profile?"):null,
                WarningAccepted=(mask,hz)=>acknowledgedSdProfiles.Add(Tuple.Create((uint)mask,(uint)hz,capacity))
            };
        }
        async Task OpenRecordingSettings()
        {
            await ReadStatus();var p=profiles.SelectedItem as Profile;
            var options=new NpSdRecordingOptions {
                MediaAvailable=!V2 && NpV1BleProtocol.CanSelect(current,0,0),Media=p!=null&&p.Mask==0,
                Camera=mediaCamera.Checked,Microphone=mediaMicrophone.Checked,CameraHz=(int)mediaRate.Value,
                Read=async()=>{await ReadStatus();return RecordingProfile();},Configure=ConfigureChannels
            };
            using(var dialog=new NpRecordingDialog(V2,p==null?0:(int)p.Mask,p==null?0:(int)p.Hz,false,false,false,RecordingProfile,null,options)) {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;
                RequireStartNotCanceled();
#if NP_PREPARATION_ENABLE
                await RetireBleProbePreparationForSettings();RequireStartNotCanceled();
#endif
                mediaSettingsChanged|=options.Camera!=mediaCamera.Checked || options.Microphone!=mediaMicrophone.Checked || options.CameraHz!=(int)mediaRate.Value;
                mediaCamera.Checked=options.Camera;mediaMicrophone.Checked=options.Microphone;mediaRate.Value=options.CameraHz;
                uint mask=options.Media?0U:(uint)dialog.ProbeMask,hz=options.Media?0U:(uint)dialog.SampleHz;
                profiles.SelectedItem=profiles.Items.Cast<Profile>().Single(choice=>choice.Mask==mask && choice.Hz==hz);
            }
            var selected=profiles.SelectedItem as Profile;
            var status=await ReadStatus();
            if(selected.Mask!=status.Mask || selected.Hz!=status.Hz || !V2 && selected.Mask==0 && mediaSettingsChanged){
                await Apply();status=await ReadStatus();
            }
            BeginBleRtcSettingsSetup(status);
            await PrepareBleHardwareRtc(status);RequireStartNotCanceled();
            await StageRecordingConfigurations(status);
#if NP_PREPARATION_ENABLE
            await PrepareBleProbes(status);
#endif
            Append("Recording settings saved on the device.");
        }
        void Profiles(NpSdStatus s)
        {
            var previous = profiles.SelectedItem as Profile;var choices=new List<Profile>();
            if (V2) {
                foreach(uint mask in new uint[]{1U,3U,7U})if((np2AvailableMask&mask)==mask)
                    foreach(uint hz in NpV2SdControl.SupportsLive7500(s)?new uint[]{7500,15000,20000,30000}:new uint[]{15000,20000,30000})choices.Add(new Profile{Mask=mask,Hz=hz});
            }
            else if(recorderVersion==1) {
                foreach (uint hz in new uint[] { 7500, 15000, 20000, 30000 }) for (uint mask = 1; mask <= 7; mask++)
                    if (NpV1BleProtocol.CanSelect(s, mask, hz)) choices.Add(new Profile { Mask = mask, Hz = hz });
                if(NpV1BleProtocol.CanSelect(s,0,0))choices.Add(new Profile{Mask=0,Hz=0});
            }
            bool same=profiles.Items.Count==choices.Count;
            for(int i=0;same && i<choices.Count;i++){var item=(Profile)profiles.Items[i];same=item.Mask==choices[i].Mask && item.Hz==choices[i].Hz;}
            if(same)return; // Keep an open menu and pending selection stable during polling.
            profiles.BeginUpdate();profiles.Items.Clear();foreach(var choice in choices)profiles.Items.Add(choice);profiles.EndUpdate();
            for (int i = 0; i < profiles.Items.Count; i++) {
                var p = (Profile)profiles.Items[i];
                if (previous != null ? p.Mask == previous.Mask && p.Hz == previous.Hz : s != null && p.Mask == s.Mask && p.Hz == s.Hz) { profiles.SelectedIndex = i; break; }
            }
            if (profiles.SelectedIndex < 0 && profiles.Items.Count != 0) profiles.SelectedIndex = 0;
        }
        async Task Apply()
        {
            var p = profiles.SelectedItem as Profile; if (p == null) throw new IOException("Select a profile.");
            if(!V2 && p.Mask==0){await ApplyV1Media();return;}
            var s = await ReadStatus();
            RequireStartNotCanceled();
            if (V2 ? !NpV2SdControl.IsProfile(p.Mask,p.Hz) : !NpV1BleProtocol.CanSelectProfile(s,p.Mask,p.Hz)) throw new IOException("Wait for the recorder to stop and confirm an advertised profile.");
            uint selectId=V2?NpV2SdControl.NextId(s):NpSdControl.NextId(s);
            var selected=await Send(V2 ? NpV2SdControl.Request(1, selectId, p.Hz, 0, 0, p.Mask) : NpSdControl.Request(1, selectId, p.Mask, p.Hz));
            // SELECT may only acknowledge a queued command. Wait using reads;
            // never replay SELECT or START if a receipt or preparation is late.
            int epoch=connection.Epoch;var wait=System.Diagnostics.Stopwatch.StartNew();
            string peer=address.Text;bool statusContinued=false;
            // A correlated pending V1 SELECT may span a rate-family reboot.
            // Only fresh read-only STATUS receipts may be repeated in this window.
            bool pendingSelect=!V2 && selected.Operation==1 && selected.RequestId==selectId && selected.Error==0 &&
                selected.Mask==p.Mask && selected.Hz==p.Hz && (selected.Flags&8)!=0;
            while(wait.Elapsed.TotalSeconds<60){
                await Task.Delay(250);
                RequireStartNotCanceled();
                if(!connection.Connected || connection.Epoch!=epoch || address.Text!=peer)throw new IOException("Connection changed while applying recording settings. Reconnect and read status.");
                if(wait.Elapsed.TotalSeconds>=60)break;
                try{s=await ReadStatus();}
                catch(NpV1ReceiptTimeout ex) when(!V2 && (pendingSelect || !statusContinued) && ex.Operation==0 && ex.Transaction==sequence){
                    RequireStartNotCanceled();
                    if(!connection.Connected || connection.Epoch!=epoch || address.Text!=peer || wait.Elapsed.TotalSeconds>=60)throw;
                    if(!statusContinued)Append("Waiting for the device to confirm recording settings…");
                    statusContinued=true;
                    continue;
                }
                RequireStartNotCanceled();
                if(!connection.Connected || connection.Epoch!=epoch || address.Text!=peer)throw new IOException("Connection changed while applying recording settings. Reconnect and read status.");
                if(wait.Elapsed.TotalSeconds>=60)break;
                if((s.Flags&8)==0)pendingSelect=false;
                if(s.Mask==p.Mask && s.Hz==p.Hz && (V2?!s.Busy:NpV1BleProtocol.CanSelectProfile(s,p.Mask,p.Hz))){Append("Recording settings confirmed.");return;}
            }
            throw new IOException("Recording settings were not confirmed. Read status before retrying; recording was not started.");
        }
        void ObserveSdProfileContext(NpSdStatus s)
        {
            if(current!=null && (s.Capacity!=current.Capacity || (current.Flags&16)!=0 && (s.Flags&16)==0))acknowledgedSdProfiles.Clear();
        }
        async Task PrepareRecordingClock(NpSdStatus s)
        {
            if(await PrepareBleHardwareRtc(s))return;
            if(V2 || (s.Flags&NpV1BleProtocol.Clock)==0 || !NpV1BleProtocol.CanConfigure(s))return;
            clockStatus.Text="Synchronizing recording time…";
            try{await SyncClock(8);clockStatus.Text="PC time synced";}
            catch(Exception ex) when(ex is IOException || ex is TimeoutException){
                clockStatus.Text="Recording time unavailable";
                Append("Could not synchronize recording time; recording will continue. "+ex.Message);
            }
        }
        async Task StartRecording()
        { await StartRecordingCore(0); }
        async Task<List<Np2ConfigRequest>> StageRecordingConfigurations(NpSdStatus s)
        {
            var staged=new List<Np2ConfigRequest>();
            int epoch=connection.Epoch;ulong peer=connectedAddress;
            Action check=()=>{
                RequireStartNotCanceled();
                if(!connection.Connected || connection.Epoch!=epoch || connectedAddress!=peer)
                    throw new IOException("Connection changed while preparing probe settings. Reconnect and read status.");
            };
            check();
            if(V2){
                HashSet<int> apply=Np2ApplyRequests(peer);
                Dictionary<int,string> requests;
                if(!np2HostConfigurations.TryGetValue(peer,out requests))requests=new Dictionary<int,string>();
                if(apply!=null)foreach(int connector in apply){
                    if((s.Mask&(1U<<(connector-1)))==0U)continue;
                    string json;if(!requests.TryGetValue(connector,out json))throw new IOException("NP2 apply was requested for connector "+connector+" without a retained configuration.");
                    var request=NpConfigurationDialog.PrepareV2Request(json,connector);
                    await new Np2ConfigClient(new ConfigTransport(connection)).StageAsync(request,CancellationToken.None,true);
                    check();staged.Add(request);
                }
            }else if(s.Mask!=0){
                var transport=new ConfigTransport(connection,frame=>{
                    byte op=frame[5];if(op==4)return;
                    string port="Probe "+(frame[6]+1);
                    configurationStatus.Text=op==2?port+" configuration upload · "+
                        Math.Min(100,(NpSdGeometry.U(frame,12)+NpSdGeometry.U(frame,16))*100U/1576U)+"%":
                        port+(op==0?" · checking retained configuration…":op==3?" · confirming staged configuration…":" · preparing configuration upload…");
                });
                await new NpConfigClient(transport).StageForCaptureAsync(V1Configurations().Values,(int)s.Mask,CancellationToken.None,true);
                check();
                if(V1Configurations().Values.Any(request=>(s.Mask&(1U<<request.Port))!=0))
                    configurationStatus.Text="Probe maps saved on the device.";
            }
            return staged;
        }
        async Task StartRecordingCore(uint duration,Action<NpSdStatus,byte[]> prepared=null)
        {
            RequireNoPendingFirstSyncStart();
            if(pendingStop!=null)throw new IOException("Wait for Stop completion to be confirmed before starting another recording.");
            if(!PrepareBleSessionFolder())return;
            var p = profiles.SelectedItem as Profile; var s = await ReadStatus();
            RequireStartNotCanceled();
            if(p==null)throw new IOException("Select recording settings before Start.");
            if(V2 && !NpV2SdControl.IsProfile(p.Mask,p.Hz))throw new IOException("Invalid V2 recording profile.");
            BeginBleSessionStorage();
            if(p.Mask!=s.Mask || p.Hz!=s.Hz || !V2 && p.Mask==0 && mediaSettingsChanged){await Apply();s=await ReadStatus();}
            RequireStartNotCanceled();
            if(p.Mask!=s.Mask || p.Hz!=s.Hz)throw new IOException("Device recording settings differ from the request. Recording was not started.");
            BeginBleRtcRecordingSetup(s);
            await PrepareBleHardwareRtc(s);RequireStartNotCanceled();
            bleClosedLoopSetup=await NpClosedLoopSettings.Prepare(V2,s.Mask,s.Hz,
                (request,token)=>connection.ExchangeAsync(0xb9,request,token),CancellationToken.None,Append);
            RequireStartNotCanceled();
            var warningProfile=Tuple.Create(s.Mask,s.Hz,s.Capacity);
            if(!V2 && (!s.Qualified || s.Mask==3 && s.Hz==30000) && !acknowledgedSdProfiles.Contains(warningProfile)) {
                MessageBox.Show(DialogOwner,s.Mask==3 && s.Hz==30000?
                    "Two-probe 30 kHz SD recording has shown sustained-recording failures. Data may be incomplete.":
                    "Firmware does not report validation for this SD card and recording profile. Recording may fail or lose data.",
                    "Recording profile",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                RequireStartNotCanceled();
                acknowledgedSdProfiles.Add(warningProfile);
            }
            if(V2&&duration!=0){if(!await PrepareBleHardwareRtc(s)){await SyncClock(8);if(!clock.Synchronized)throw new IOException("SD test clock synchronization was not confirmed.");}}
            else await PrepareRecordingClock(s);
            RequireStartNotCanceled();
            if (V2) {
                // Immediate continuous recording: no scheduled UTC or cached
                // readiness/clock gate. Firmware validates the ordinary START.
                uint blocks=duration==0?0:NpSdLoggerTest.Blocks(p.Mask,p.Hz,duration);
                configurationReceipts.Reset();configurationReadError=null;
                configurationStatus.Text="No new NP2 IMRO requested.";
                var staged=await StageRecordingConfigurations(s);
                RequireStartNotCanceled();
                configurationReceipts.Expect(staged);
                if(staged.Count!=0){configurationStatus.Text="NP2 IMRO staged; probe application pending. Read status confirms it after preparation.";Append(configurationStatus.Text);}
                var start=NpV2SdControl.Request(2, NpV2SdControl.NextId(s), p.Hz, blocks, 0, p.Mask);
                if(prepared!=null)prepared(s,start);
                BeginBleRecordingMetadata(s,start);
                try{await SendBleSdStart(start);}
                catch(NpSdCommandException){
                    configurationReceipts.Reset();
                    configurationStatus.Text=staged.Count==0?"":"Probe settings staged; recording was rejected before capture started.";
                    throw;
                }
            }
            else {
                if(s.Mask==0)await VerifyV1Media();
                uint end=s.Mask==0?NpV1BleProtocol.EndLba(s,duration,mediaCamera.Checked?(uint)mediaRate.Value:0U,mediaMicrophone.Checked):NpV1BleProtocol.EndLba(s,duration);
                uint session=unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                // Recheck resident identities after any profile/reset transition.
                await StageRecordingConfigurations(s);
                RequireStartNotCanceled();
#if NP_IMU_GRID_ENABLE && NP_FIRST_SYNC_ENABLE
                if(s.Mask!=0){int gridEpoch=connection.Epoch;ulong gridPeer=connectedAddress;s=await NpImuGridControl.PrepareBle(s,Send,ReadStatus,true);RequireStartNotCanceled();if(!connection.Connected||connection.Epoch!=gridEpoch||connectedAddress!=gridPeer)throw new IOException("Connection changed during grid selection; recording was not started.");}
#endif
                var start=NpSdControl.Request(2, NpSdControl.NextId(s), s.Mask, s.Hz, s.Start, end, session == 0 ? 1U : session, s.Qualified ? 0U : 1U);
                if(prepared!=null)prepared(s,start);
                BeginBleRecordingMetadata(s,start);await SendBleSdStart(start);
            }
            Append("Start requested; waiting for the recorder…");
        }
        void RequireStartNotCanceled(){if(stopRequested)throw new OperationCanceledException("Recording start canceled by Stop.");}
        async Task StopRecording()
        {
            stopping=true;stopRequested=false;UpdateControls();
            try {
                if(pendingStop!=null){await ReadStatus();return;}
                NpSdStatus s=null;
                try{s=await ReadStatus();}catch(Exception ex) when(ex is IOException || ex is TimeoutException){Append("Status unavailable before Stop: "+ex.Message);}
                int epoch=connection.Epoch;var wait=System.Diagnostics.Stopwatch.StartNew();
                if(s!=null && (s.Flags&8)!=0)Append("Waiting for the previous device command before Stop.");
                while(s!=null && (s.Flags&8)!=0 && wait.Elapsed.TotalSeconds<30){
                    await Task.Delay(250);
                    if(!connection.Connected || connection.Epoch!=epoch)throw new IOException("Connection changed before Stop; reconnect and read status.");
                    s=await ReadStatus();
                }
                if(s!=null && (s.Flags&8)!=0)throw new IOException("The previous device command is still pending. Stop was not sent over it. Keep power connected and read status.");
                if(ObserveIssuedBleFirstSyncStop(s))return;
                bool firstSyncOrderedStop=await PrepareBleFirstSyncStop();
                if(UnknownBlePreparation && probePreparationAttempt.StopIssued){
                    await StopUnknownBlePreparation(s);s=await ReadStatus();
                    if(!StopConfirmed(s))throw new IOException("Stop is still unconfirmed. Read status; no Stop replayed.");
                    ReportStopped(s);return;
                }
                if(s!=null && StopConfirmed(s)&&!firstSyncOrderedStop){
                    if(UnknownBlePreparation)await StopUnknownBlePreparation(s);else await CancelBleProbePreparation();
                    ReportStopped(s);return;
                }
                // A missing status must not prevent one explicit STOP. Never
                // replay it after a missing ACK; later polls establish state.
                uint id=s==null?Next():V2?NpV2SdControl.NextId(s):NpSdControl.NextId(s);
                id=BleFirstSyncStopRequestId(s,id);
                if(V2){
                    pendingStop=System.Diagnostics.Stopwatch.StartNew();pendingStopEpoch=epoch;pendingStopPeer=connectedAddress;pendingStopAcknowledged=false;
                    if(NpV2SdControl.StopIsDraining(s)&&!firstSyncOrderedStop){
                        Append("Recorder is already finalizing; observing completion without another Stop command.");
                    }else{
                        if(UnknownBlePreparation)probePreparationAttempt.StopIssued=true;
                        try{s=await SendBleSdStop(NpV2SdControl.Request(3,id));}
                        catch(NpSdCommandException) when(bleFirstSyncDefiniteStopRejection){throw;}
                        catch(Exception ex) when(ex is IOException || ex is TimeoutException){
                            throw new IOException("Stop outcome is unconfirmed. Keep power connected; status polling will continue without replaying Stop. "+ex.Message,ex);
                        }
                        if(pendingStop==null || !connection.Connected || connection.Epoch!=pendingStopEpoch || connectedAddress!=pendingStopPeer)
                            throw new IOException("Connection changed after Stop; reconnect and read status. Completion is unknown.");
                        pendingStopAcknowledged=true;
                        Append("Stop acknowledged; finalizing SD. Keep power connected; completion will be reported automatically.");
                    }
                    ObservePendingStop(s);return;
                }
                if(UnknownBlePreparation)probePreparationAttempt.StopIssued=true;
                s=await SendBleSdStop(V2?NpV2SdControl.Request(3,id):NpSdControl.Request(3,id));
                Append("Stop acknowledged; waiting for the recorder to finish.");
                wait.Restart();
                while(!StopConfirmed(s) && wait.Elapsed.TotalSeconds<30){
                    await Task.Delay(250);
                    if(!connection.Connected || connection.Epoch!=epoch)throw new IOException("Connection changed after Stop. Reconnect and read status to confirm the recorder stopped.");
                    s=await ReadStatus();
                }
                if(!StopConfirmed(s))throw new IOException("Stop was acknowledged, but the recorder has not confirmed it is stopped. Keep power connected and read status; Stop was not replayed.");
                ReportStopped(s);
            } finally {stopping=false;}
        }
        void ForgetPendingStop()
        {
            if(pendingStop==null)return;
            pendingStop=null;
            Append("Connection changed before Stop completion was confirmed. Reconnect and read status; completion is unknown and Stop was not replayed.");
        }
        void ObservePendingStop(NpSdStatus s)
        {
            if(pendingStop==null)return;
            if(!connection.Connected || connection.Epoch!=pendingStopEpoch || connectedAddress!=pendingStopPeer){ForgetPendingStop();return;}
            if(s==null)return;
            if(StopConfirmed(s)){pendingStop=null;ReportStopped(s);return;}
            state.Text+="\r\n"+(pendingStopAcknowledged?"Stop acknowledged":"Stop completion unconfirmed")+
                " · "+(NpV2SdControl.StopIsDraining(s)?"finalizing SD":"waiting for the recorder")+
                " · "+pendingStop.Elapsed.TotalSeconds.ToString("F0")+" s elapsed. Keep power connected; waiting for a terminal device status.";
        }
        bool StopConfirmed(NpSdStatus s){return V2?NpV2SdControl.StopAlreadySatisfied(s):NpV1BleProtocol.StopAlreadySatisfied(s);}
        void ReportStopped(NpSdStatus s){
            CompleteBleFirstSyncStop(s);
            bool failed=s.Fault!=0 || s.State==(V2?255U:4U);
            Append(failed?"Recording is stopped after a device failure. "+(V2?"Fault "+s.Fault:NpV1BleProtocol.FaultText(s))+"; saved data may be incomplete.":V2 && s.State==5?"Recording complete; device confirmed Stop. No capture is active.":"Recording stopped; no capture is active.");
        }
        async Task SyncClock(uint observations=16)
        {
            var s = await ReadStatus();
            if(await PrepareBleHardwareRtc(s,true)){ShowBleRtcStatus();return;}
            if (V2 ? !NpV2SdControl.CanConfigure(s) : !NpV1BleProtocol.CanConfigure(s) || (s.Flags & NpV1BleProtocol.Clock) == 0) throw new IOException("Stopped recorder with PC-time support required.");
            if(V2){await InitializeClock(s);return;}
            var result = await NpV2OnlineClock.Synchronize(connection.ExchangeAsync, Next, observations);
            Append("PC time confirmed; uncertainty " + (result.UncertaintySeconds * 1000).ToString("F2") + " ms.");
        }
        void ShowClock(){if(ModernBleRtc){ShowBleRtcStatus();return;}clockStatus.Text=clock.Synchronized?"PC time synced · ±"+clock.BoundMilliseconds.ToString("F2")+" ms":clock.Error==null?"":"PC time unconfirmed";}
        async Task InitializeClock(NpSdStatus s)
        {
            if(ModernBleRtc){ShowBleRtcStatus();return;}
            try{await clock.Initialize(s,connection.ExchangeAsync,Next);}
            catch(Exception ex){clock.Failed(ex);Append("PC time: "+ex.Message);}
            if(!V2 || !connection.Connected){clock.Reset();clockStatus.Text="";return;}
            ShowClock();
        }
        async Task CheckClock()
        {
            if(ModernBleRtc){ShowBleRtcStatus();return;}
            string previous=clock.Error;
            try{await clock.Observe(connection.ExchangeAsync,Next);}
            catch(Exception ex){if(previous!=ex.Message)Append("PC time: "+ex.Message);}
            if(!V2 || !connection.Connected){clock.Reset();clockStatus.Text="";return;}
            ShowClock();
        }
        void OpenPreview()
        {
            if(V2?!NpV2SdControl.CanPreview(current):!NpV1BlePreviewForm.CanPreview(current))throw new IOException("Start a probe recording with device preview support before opening Preview.");
            if(preview!=null && !preview.IsDisposed){preview.Activate();return;}
            var calibration=NpCalibrationStore.Get("ble:"+address.Text.Trim().ToUpperInvariant());calibration.RequireModel(V2,(int)current.Mask);
            preview=V2?(Form)new NpBlePreviewForm(connection,()=>V2?current:null,()=>busy || hostBusy || connection.PreviewLowerPriorityBusy,calibration){RecordingInfo=()=>recordingTime.Text}:new NpV1BlePreviewForm(connection,()=>recorderVersion==1?current:null,()=>busy || hostBusy || connection.PreviewLowerPriorityBusy,calibration){RecordingInfo=()=>recordingTime.Text};
            preview.Show();
        }
        async Task ConfigureChannels(IWin32Window owner)
        {
            var s=await ReadStatus();
            if(V2?!NpV2SdControl.CanConfigure(s):!NpV1BleProtocol.CanConfigure(s))throw new IOException("Stop recording before probe configuration.");
            if (V2) {
                ulong peer=UInt64.Parse(address.Text.Trim(),System.Globalization.NumberStyles.HexNumber);
                Dictionary<int,string> requests;
                if(!np2HostConfigurations.TryGetValue(peer,out requests)){requests=new Dictionary<int,string>();np2HostConfigurations.Add(peer,requests);}
                using(var dialog=new NpConfigurationDialog(null,np2:true,availableMask:(int)np2AvailableMask,retainedRequests:requests,applyConnectors:Np2ApplyRequests(peer,true),
                    receipt:async(port,json)=>NpConfigurationDialog.Receipt(await new Np2ConfigClient(new ConfigTransport(connection)).StatusAsync((byte)(port-1),CancellationToken.None),json==null?null:NpConfigurationDialog.PrepareV2Request(json,port))))dialog.ShowDialog(owner);
                Append("Probe configuration saved; it will be applied when recording starts.");
                return;
            }
            var client = new NpConfigClient(new ConfigTransport(connection));
            using (var dialog = new NpConfigurationDialog(null,availableMask:(int)s.Mask,retainedV1Requests:V1Configurations(),
                receipt:async(port,json)=>NpConfigurationDialog.Receipt(await client.StatusAsync((byte)(port-1),CancellationToken.None),json==null?null:NpConfigurationDialog.PrepareV1Request(json,port)))) {
                WildXTheme.Apply(dialog);dialog.ShowDialog(owner);
            }
            current = null; Append("Read device status again after channel configuration.");
        }
        async Task SwitchMode(uint v1, uint v2)
        {
            var s = await ReadStatus();
            if (V2 && !NpV2SdControl.CanSwitchMode(s)) throw new IOException("A stopped recorder with mode-switch support is required.");
            await Send(V2 ? NpV2SdControl.ModeRequest(NpV2SdControl.NextId(s), v2) : NpV1BleProtocol.Mode(s, NpSdControl.NextId(s), v1));
            connection.Disconnect(); ResetDevice(); state.Text = TopLevel?"Mode handoff acknowledged. Close BLE Control and refresh USB devices.":"Mode handoff acknowledged. Open USB Mode or Data Download and refresh devices.";
        }
        async Task OverwriteFromLastKnownPosition()
        {
            // Confirm the cached offer first: Cancel must issue no wire command.
            if(!V2 || !connection.Connected || connectedAddress==0 || hostBusy || !NpV2SdControl.CanOverwriteFromLastKnownPosition(current))
                throw new IOException("The V2 logger does not advertise an available overwrite position. Read status first.");
            uint position=current.Start,capacity=current.Capacity;int generation=connection.Epoch;ulong peer=connectedAddress;
            if(MessageBox.Show(DialogOwner,"Overwrite from the last known position on V2 "+peer.ToString("X12")+"?\r\n\r\n"+
                "LBA "+position.ToString("N0")+" · byte offset "+((ulong)position*512).ToString("N0")+" ("+(position*512.0/1073741824).ToString("N2")+" GiB)\r\n"+
                "Card: "+(capacity*512.0/1073741824).ToString("N2")+" GiB\r\n\r\n"+
                "This removes catalog entries at and after this position. Future recordings may overwrite ALL data at or after it, including damaged or unindexed recordings. Earlier entries are retained.\r\n\r\n"+
                "Raw sectors are not erased by this action. Recording will not start automatically.",
                "Overwrite from last known position",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Action target=()=>{if(!connection.Connected || connection.Epoch!=generation || !V2 || connectedAddress!=peer || hostBusy)
                throw new IOException("The connected logger changed. Read status and confirm again; no overwrite replayed.");};
            target();ClosePreview();var fresh=await ReadStatus();target();
            byte[] request=NpV2SdControl.OverwriteRequest(fresh,NpV2SdControl.NextId(fresh),position,capacity);
            uint mutationId=NpSdGeometry.U(request,12);
            NpSdStatus receipt;
            try{receipt=await Send(request);target();}
            catch(TimeoutException ex){
                throw new IOException("Overwrite outcome is unknown. "+ex.Message+" Read status; no command replayed and no recording started.",ex);
            }
            catch(IOException ex){
                throw new IOException("Overwrite was not confirmed: "+ex.Message+" Read status; no command replayed and no recording started.",ex);
            }
            if(receipt.Error!=0)throw new IOException("Overwrite rejected: "+receipt.Error+". No command replayed.");
            if(receipt.Operation!=12 || receipt.RequestId!=mutationId || receipt.LastId!=mutationId)
                throw new IOException("Overwrite acknowledgement did not match. Read status; no command replayed.");
            var wait=System.Diagnostics.Stopwatch.StartNew();
            try{
                while(current!=null && (current.Flags&8)!=0 && wait.ElapsedMilliseconds<10000){
                    await Task.Delay(200);target();await ReadStatus();target();
                }
            }catch(Exception ex) when(ex is IOException || ex is TimeoutException){
                throw new IOException("Overwrite completion is unknown. Read status; no command replayed and no recording started.",ex);
            }
            var completed=current;
            if(completed==null || completed.LastId!=mutationId || completed.Error!=0 || completed.Fault!=0 || completed.Busy ||
                completed.State!=1 || (completed.Flags&16)==0 || NpV2SdControl.NeedsRecovery(completed) ||
                NpV2SdControl.HasOverwriteOffer(completed) || completed.Start!=position || completed.Capacity!=capacity)
                throw new IOException("Overwrite was not confirmed complete. Read status; no command replayed and no recording started.");
            state.Text="Last known recording position restored. New recordings may overwrite data from LBA "+position.ToString("N0")+". Press Start recording when ready.";
            Append(state.Text);
        }
        async Task RescueReset()
        {
            if(!V2 || !connection.Connected)throw new IOException("Connect the NP2 recorder before rescue reset.");
            if(MessageBox.Show(DialogOwner,"Emergency reset does not drain recording buffers. Any active session may be incomplete. Use Stop and drain normally.\r\n\r\nReset this recorder now?",
                "NP2 rescue reset",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            ClosePreview();
            try { await connection.RescueReset(CancellationToken.None); }
            finally { ResetDevice(); }
            state.Text="Rescue request sent without a firmware ACK. Reconnect and read status to confirm recovery.";
            Append(state.Text);
        }
    }
}
