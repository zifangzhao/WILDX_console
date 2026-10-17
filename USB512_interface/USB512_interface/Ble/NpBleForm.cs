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
            internal ConfigTransport(NpBleConnection connection) { this.connection = connection; }
            public Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancel) { return connection.ExchangeAsync(0xb8, request, cancel); }
        }
        readonly NpBleConnection connection = new NpBleConnection();
        readonly BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        readonly object advertisementToken,watcherStoppedToken;
        readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        readonly Dictionary<byte,NpConfigRequest> configurations = new Dictionary<byte,NpConfigRequest>();
        readonly Dictionary<ulong,Dictionary<int,string>> np2HostConfigurations = new Dictionary<ulong,Dictionary<int,string>>();
        readonly Dictionary<ulong,HashSet<int>> np2ApplyConfigurations = new Dictionary<ulong,HashSet<int>>();
        readonly ComboBox devices = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox profiles = new ComboBox { Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Label recordingSummary = new Label {AutoSize=true,Text="Connect to read recording settings."};
        readonly TextBox address = new TextBox { Width = 160 };
        readonly CheckBox unqualified = new CheckBox { Text = "Allow firmware-supported unqualified bench profile (NP1)", AutoSize = true };
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
        string configurationReadError;
        Form preview;
        NpSdStatus current;
        int recorderVersion;
        uint np2AvailableMask;
        Button scan,stopScan,connect,disconnect,rescueReset,recordingSettings,usbDownload,previewButton,startRecording,stopRecording,recordingsButton;
        bool busy, closing, connecting, hostBusy, mediaSettingsChanged,stopRequested,stopping;
        internal bool IsBusy{get{return busy;}}
        internal bool SessionOwned{get{return busy || connection.Connected || watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started;}}
        internal event Action StateChanged;
        internal Control MaintenanceControls { get { return maintenanceActions; } }
        internal void SetHostBusy(bool value){if(hostBusy==value)return;hostBusy=value;UpdateControls();}
        IWin32Window DialogOwner{get{return TopLevelControl??this;}}
        uint sequence = unchecked((uint)Environment.TickCount);
        bool V2 { get { return recorderVersion == 2; } }
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
            scan=Add(connectionActions, "Scan", () => { if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started) watcher.Start(); state.Text = "Scanning…"; return Task.CompletedTask; });
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
                if(hostBusy || stopping || stopRequested || !connection.Connected)return;
                stopRequested=true;UpdateControls();
                if(busy){Append("Stop requested; waiting for the current transfer to finish. Pending capture start will be canceled.");return;}
                await Run(StopRecording);
            };
            recordingSettings=Add(actions,"Recording settings",OpenRecordingSettings);
            Add(actions, "Sync PC time", SyncClock);
            previewButton=Add(actions,"NP Preview",()=>{OpenPreview();return Task.CompletedTask;});
            recordingsButton=Add(actions,"Recordings…",OpenRecordings);
            var reportButton=new Button {Text="Report…",AutoSize=true};
            reportButton.Click+=delegate {using(var report=new NpReportForm("BLE report","Device: "+(recorderVersion==0?"Unconfirmed":"Neuropixels V"+recorderVersion)+"\r\nBLE address: "+address.Text+"\r\n"+state.Text+"\r\n"+clockStatus.Text+"\r\n"+configurationStatus.Text+"\r\n\r\n"+log.Text))report.ShowDialog(DialogOwner);};
            Add(actions,"Calibration…",()=>{NpCalibrationStore.Show(DialogOwner,"ble:"+address.Text.Trim().ToUpperInvariant(),V2);return Task.CompletedTask;});
            Add(maintenanceActions, "Recover interrupted catalog", async () => {
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
            statusPanel.Controls.Add(state);statusPanel.Controls.Add(firmwareStatus);statusPanel.Controls.Add(mediaStatus);statusPanel.Controls.Add(clockStatus);statusPanel.Controls.Add(configurationStatus);statusPanel.Controls.Add(reportButton);
            statusPanel.SizeChanged+=delegate {foreach(var label in new[]{state,firmwareStatus,mediaStatus,clockStatus,configurationStatus})label.MaximumSize=new Size(Math.Max(1,statusPanel.ClientSize.Width),0);};
            maintenanceActions.SizeChanged+=delegate {maintenancePeer.MaximumSize=new Size(Math.Max(1,maintenanceActions.ClientSize.Width),0);};
            foreach(var label in new[]{mediaStatus,clockStatus,configurationStatus}) {
                label.Visible=false;label.TextChanged+=delegate {label.Visible=label.Text.Length!=0;};
            }
            Controls.Add(log); Controls.Add(statusPanel); Controls.Add(actions); Controls.Add(settings); Controls.Add(top);
            if(maintenance)Controls.Add(maintenanceActions);
            devices.SelectedIndexChanged += delegate { var p = devices.SelectedItem as Peer; if (p != null) address.Text = p.Address.ToString("X12"); };
            // Use the same native WinRT registration as connection discovery.
            // The Framework projected event cache missed live advertisements.
            advertisementToken=watcher.GetType().GetMethod("add_Received").Invoke(watcher,new object[]{
                new Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher,BluetoothLEAdvertisementReceivedEventArgs>(Advertisement)});
            watcherStoppedToken=watcher.GetType().GetMethod("add_Stopped").Invoke(watcher,new object[]{
                new Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher,BluetoothLEAdvertisementWatcherStoppedEventArgs>(WatcherStopped)});
            connection.Disconnected += message => Ui(() => { if(connection.Connected)return;ResetDevice();state.Text = message; Append(message);NotifyState(); });
            // A failed read clears current, but must not permanently disable
            // read-only status/heartbeat recovery on a still-live connection.
            timer.Tick += async delegate { if (!busy && !hostBusy && refreshTask.IsCompleted && poll.Checked && connection.Connected) {refreshTask=RefreshStatusQuietly();await refreshTask;} };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; return; } Shutdown(); };
            Profiles(null); timer.Start();WildXTheme.Apply(this);
            WildXTheme.Apply(maintenanceActions);maintenancePeer.ForeColor=maintenanceResult.ForeColor=WildXTheme.Muted;
            foreach(Control control in maintenanceActions.Controls)if(control is Button && (control.Text=="ROM DFU" || control.Text=="Rescue reset"))WildXTheme.Button((Button)control,false,true);
            WildXTheme.Button(connect,true);
            foreach(Control control in actions.Controls)if(control is Button){
                if(control.Text=="Start recording")WildXTheme.Button((Button)control,true);
                else if(control.Text=="Stop recording" || control.Text=="ROM DFU")WildXTheme.Button((Button)control,false,true);
            }
            foreach(var row in new[]{connectionActions,options,actions,maintenanceActions,statusPanel})foreach(Control control in row.Controls) {
                control.Anchor=AnchorStyles.Left;
                if(control is Button){control.MinimumSize=new Size(0,36);((Button)control).AutoSizeMode=AutoSizeMode.GrowAndShrink;}
                if(row==options)control.Margin=new Padding(0,0,16,0);
            }
            foreach(var label in new[]{state,firmwareStatus,clockStatus,configurationStatus})label.Margin=new Padding(0,0,0,6);
            state.ForeColor=firmwareStatus.ForeColor=clockStatus.ForeColor=configurationStatus.ForeColor=WildXTheme.Muted;
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
        void WatcherStopped(BluetoothLEAdvertisementWatcher sender,BluetoothLEAdvertisementWatcherStoppedEventArgs e)
        {
            Ui(()=>{if(e.Error!=Windows.Devices.Bluetooth.BluetoothError.Success)state.Text="BLE scan stopped: "+e.Error;UpdateControls();NotifyState();});
        }
        void NotifyState(){var changed=StateChanged;if(!closing && changed!=null)changed();}
        void Shutdown()
        {
            if(closing)return;closing=true;ClosePreview();timer.Stop();
            if(advertisementToken!=null)watcher.GetType().GetMethod("remove_Received").Invoke(watcher,new[]{advertisementToken});
            if(watcherStoppedToken!=null)watcher.GetType().GetMethod("remove_Stopped").Invoke(watcher,new[]{watcherStoppedToken});
            if(watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started)watcher.Stop();connection.Dispose();timer.Dispose();
            profiles.Dispose();unqualified.Dispose();poll.Dispose();
        }
        protected override void Dispose(bool disposing){if(disposing){Shutdown();maintenanceActions.Dispose();}base.Dispose(disposing);}
        void Ui(Action action) { if (!closing && !IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        void Advertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            string name = e.Advertisement.LocalName;
            bool known = !String.IsNullOrWhiteSpace(name) && (name.IndexOf("NP", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("WILD", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("CE64", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("HJ-185", StringComparison.OrdinalIgnoreCase));
            if (!known && !e.Advertisement.ServiceUuids.Contains(new Guid("0000fff0-0000-1000-8000-00805f9b34fb"))) return;
            Ui(() => {
                if (busy || connection.Connected) return;
                Peer p; if (!peers.TryGetValue(e.BluetoothAddress, out p)) { p = new Peer { Address = e.BluetoothAddress }; peers.Add(p.Address, p); devices.Items.Add(p); }
                p.Type=e.BluetoothAddressType;
                if (!String.IsNullOrWhiteSpace(name)) p.Name = name; if (String.IsNullOrWhiteSpace(p.Name)) p.Name = "FFF0 device";
                p.Rssi = e.RawSignalStrengthInDBm; int index = devices.Items.IndexOf(p); devices.Items[index] = p;
            });
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
            profiles.Enabled=unqualified.Enabled=poll.Enabled=ready;
            foreach(Control control in actions.Controls)control.Enabled=ready;
            stopRecording.Enabled=linked && recorderVersion!=0 && !hostBusy && !stopping && !stopRequested;
            recordingSettings.Enabled=ready && (V2?NpV2SdControl.CanConfigure(current):NpV1BleProtocol.CanManageStorage(current));
            usbDownload.Visible=recorderVersion==1;
            previewButton.Visible=recorderVersion!=0;previewButton.Enabled=ready && (V2?NpV2SdControl.CanPreview(current):NpV1BlePreviewForm.CanPreview(current));
            recordingsButton.Enabled=ready;
            startRecording.Enabled=ready && (V2?clock.Synchronized && NpV2SdControl.CanStart(current):NpV1BleProtocol.CanManualStart(current));
            UpdateMediaControls();
        }
        void ClosePreview(){if(preview!=null){preview.Close();preview.Dispose();preview=null;}}
        void ResetDevice(){ClosePreview();ResetMedia();mediaSettingsChanged=stopRequested=false;clock.Reset();clockStatus.Text="";configurationReceipts.Reset();configurationStatus.Text="";configurationReadError=null;current=null;recorderVersion=0;np2AvailableMask=0;firmwareIdentity=null;firmwareStatus.Visible=false;firmwareStatus.Text="";Profiles(null);UpdateControls();}
        async Task Run(Func<Task> action)
        {
            if (busy || hostBusy) return; busy = true; UpdateControls();
            NotifyState();
            try { await refreshTask;await action(); }
            catch (Exception ex) { current = null; state.Text = ex.Message; Append(ex.Message); }
            finally {
                busy = false; UpdateControls();NotifyState();
                if(stopRequested && connection.Connected && !hostBusy)await Run(StopRecording);
                else OfferFirmwareReview();
            }
        }
        void OfferFirmwareReview()
        {
            if(!Visible || !ContainsFocus || busy || hostBusy || !connection.Connected || current==null || current.Busy || current.Fault!=0 ||
                !(V2?(current.State==1 || current.State==5 || current.State==7):(current.State==0 || current.State==3)))return;
            if(NpFirmwareNotice.Offer(DialogOwner,"ble:"+address.Text.Trim(),NpFirmwareCatalog.Bundled.For((uint)recorderVersion),firmwareIdentity))
                FirmwareReviewRequested?.Invoke();
        }
        void Append(string message) { log.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine); }
        uint Next() { uint n = unchecked(++sequence); return n == 0 ? ++sequence : n; }
        async Task Connect()
        {
            ulong value;
            string selected = address.Text.Replace(":", "").Replace("-", "").Trim();
            if (selected.Length != 12 || !ulong.TryParse(selected, System.Globalization.NumberStyles.HexNumber, null, out value) || value == 0) throw new IOException("Select or enter the exact 12-digit BLE address.");
            var witness=devices.SelectedItem as Peer;
            BluetoothAddressType? observedType=witness!=null && witness.Address==value && witness.Type!=BluetoothAddressType.Unspecified
                ? (BluetoothAddressType?)witness.Type : null;
            watcher.Stop(); ResetDevice(); state.Text = "Connecting " + selected + "...";
            connecting=true;UpdateControls();
            try {
                await connection.ConnectAsync(value,observedType);
                var s=await ReadStatus();if(V2)await InitializeClock(s);
                if(!connection.Connected)throw new IOException("BLE disconnected during initialization.");
                Append("Connected to " + selected + ".");
            }catch{connection.Disconnect();ResetDevice();throw;}
            finally{connecting=false;UpdateControls();}
        }
        void ShowStatus(NpSdStatus s){
            if(!V2 && s.Fault!=0 && (current==null || current.Fault!=s.Fault || current.Chunks!=s.Chunks))Append("V1 "+NpV1BleProtocol.StateText(s));
            current=s;state.Text=V2?NpV2SdControl.Describe(s):"WILD-X V1 — "+NpV1BleProtocol.StateText(s)+"\r\n"+s;
            firmwareStatus.Visible=recorderVersion!=0;firmwareStatus.Text="Firmware: "+NpFirmwareCatalog.Bundled.Installed((uint)recorderVersion,firmwareIdentity);}
        async Task<NpSdStatus> Send(byte[] request)
        {
            if(recorderVersion==0)throw new IOException("Connect and read the device status first.");
            // Preserve the last attempted ID even if its acknowledgement is
            // lost, so a subsequent STOP cannot reuse that mutation's ID.
            sequence=NpSdGeometry.U(request,12);
            var body = await connection.ExchangeAsync(request); uint op = NpSdGeometry.U(request, 8), id = NpSdGeometry.U(request, 12);
            var s=NpBleConnection.ParseStatus(body,recorderVersion,op,id);ShowStatus(s);
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
                    configurationStatus.Text="NP2 IMRO application not verified in this connection.";
                }
                Profiles(current);if(!V2)await RefreshV1Media(current);UpdateControls();
                if((current.Flags&NpFirmwareIdentity.BleCapability)!=0){
                    firmwareIdentity=NpFirmwareIdentity.Parse(await connection.ExchangeAsync(NpFirmwareIdentity.BleCommand,new byte[0],CancellationToken.None),(uint)recorderVersion);
                    ShowStatus(current);UpdateControls();
                }
                return current;
            }
            var s = await Send(V2 ? NpV2SdControl.Request(0, Next()) : NpSdControl.Request(0, Next()));
            Profiles(s);
            if(V2)await CheckConfiguration(s);
            else await RefreshV1Media(s);
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
            return new NpUsbRecordingProfile {
                AvailableMask=choices.Aggregate(0,(mask,p)=>mask|(int)p.Mask),SelectedMask=selected==null?0:(int)selected.Mask,
                SelectedHz=selected==null?0:(int)selected.Hz,Rates=choices.Select(p=>(int)p.Hz).Distinct().OrderBy(hz=>hz).ToArray(),
                Status="Neuropixels V"+recorderVersion+" · SD recording\r\n"+(V2?NpV2SdControl.Describe(current):NpV1BleProtocol.StateText(current)),
                Validate=(mask,hz)=>choices.Any(p=>p.Mask==(uint)mask && p.Hz==(uint)hz)?null:"This probe combination / sample rate is not supported by the device.",
                Warning=(mask,hz)=>V2 && NpCaptureMode.Count(mask)>1 && hz==30000?"Multi-probe 30 kHz recording is experimental. Continue with this profile?":null
            };
        }
        async Task OpenRecordingSettings()
        {
            await ReadStatus();var p=profiles.SelectedItem as Profile;
            var options=new NpSdRecordingOptions {
                AllowUnqualified=unqualified.Checked,MediaAvailable=!V2 && NpV1BleProtocol.CanSelect(current,0,0),Media=p!=null&&p.Mask==0,
                Camera=mediaCamera.Checked,Microphone=mediaMicrophone.Checked,CameraHz=(int)mediaRate.Value,
                Read=async()=>{await ReadStatus();return RecordingProfile();},Configure=ConfigureChannels
            };
            using(var dialog=new NpRecordingDialog(V2,p==null?0:(int)p.Mask,p==null?0:(int)p.Hz,false,false,false,RecordingProfile,null,options)) {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;
                mediaSettingsChanged|=options.Camera!=mediaCamera.Checked || options.Microphone!=mediaMicrophone.Checked || options.CameraHz!=(int)mediaRate.Value;
                unqualified.Checked=options.AllowUnqualified;mediaCamera.Checked=options.Camera;mediaMicrophone.Checked=options.Microphone;mediaRate.Value=options.CameraHz;
                uint mask=options.Media?0U:(uint)dialog.ProbeMask,hz=options.Media?0U:(uint)dialog.SampleHz;
                profiles.SelectedItem=profiles.Items.Cast<Profile>().Single(choice=>choice.Mask==mask && choice.Hz==hz);
            }
            Append("Recording settings saved. Start applies these settings before capture.");
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
            if (V2 ? !NpV2SdControl.CanSelectProfile(s,p.Mask,p.Hz) : !NpV1BleProtocol.CanSelectProfile(s,p.Mask,p.Hz)) throw new IOException("Wait for the recorder to stop and confirm an advertised profile.");
            await Send(V2 ? NpV2SdControl.Request(1, NpV2SdControl.NextId(s), p.Hz, 0, 0, p.Mask) : NpSdControl.Request(1, NpSdControl.NextId(s), p.Mask, p.Hz));
            // SELECT may only acknowledge a queued command. Wait using reads;
            // never replay SELECT or START if a receipt or preparation is late.
            int epoch=connection.Epoch;var wait=System.Diagnostics.Stopwatch.StartNew();
            while(wait.Elapsed.TotalSeconds<60){
                await Task.Delay(250);
                RequireStartNotCanceled();
                if(!connection.Connected || connection.Epoch!=epoch)throw new IOException("Connection changed while applying recording settings. Reconnect and read status.");
                s=await ReadStatus();
                if(s.Mask==p.Mask && s.Hz==p.Hz && (V2?NpV2SdControl.CanSelectProfile(s,p.Mask,p.Hz):NpV1BleProtocol.CanSelectProfile(s,p.Mask,p.Hz))){Append("Recording settings confirmed.");return;}
            }
            throw new IOException("Recording settings were not confirmed. Read status before retrying; recording was not started.");
        }
        async Task StartRecording()
        {
            var p = profiles.SelectedItem as Profile; var s = await ReadStatus();
            RequireStartNotCanceled();
            if(p==null)throw new IOException("Select recording settings before Start.");
            if(p.Mask!=s.Mask || p.Hz!=s.Hz || !V2 && p.Mask==0 && mediaSettingsChanged){await Apply();s=await ReadStatus();}
            RequireStartNotCanceled();
            if(p.Mask!=s.Mask || p.Hz!=s.Hz)throw new IOException("Device recording settings differ from the request. Recording was not started.");
            if(V2 && !clock.Synchronized)throw new IOException("Synchronize PC time while stopped before starting.");
            const uint duration=0;
            if (V2) {
                // Validate storage/readiness before changing any staged maps.
                uint blocks = NpV2SdControl.Blocks(s, p.Hz, duration);
                configurationReceipts.Reset();configurationReadError=null;
                configurationStatus.Text="No new NP2 IMRO requested.";
                var staged=new List<Np2ConfigRequest>();
                ulong peer=UInt64.Parse(address.Text.Trim(),System.Globalization.NumberStyles.HexNumber);
                HashSet<int> apply=Np2ApplyRequests(peer);
                Dictionary<int,string> requests;
                if(!np2HostConfigurations.TryGetValue(peer,out requests))requests=new Dictionary<int,string>();
                if(apply!=null)foreach(int connector in apply){
                    if((s.Mask&(1U<<(connector-1)))==0U)continue;
                    string json;if(!requests.TryGetValue(connector,out json))throw new IOException("NP2 apply was requested for connector "+connector+" without a retained configuration.");
                    var request=NpConfigurationDialog.PrepareV2Request(json,connector);
                    var client=new Np2ConfigClient(new ConfigTransport(connection));
                    await client.StageAsync(request,CancellationToken.None);
                    staged.Add(request);
                }
                configurationReceipts.Expect(staged);
                if(staged.Count!=0){configurationStatus.Text="NP2 IMRO staged; probe application pending. Read status confirms it after preparation.";Append(configurationStatus.Text);}
                RequireStartNotCanceled();
                await Send(NpV2SdControl.Request(2, NpV2SdControl.NextId(s), p.Hz, blocks, 0, p.Mask));
            }
            else {
                if(s.Mask==0)await VerifyV1Media();
                if (!s.Qualified && !unqualified.Checked) throw new IOException("This V1 card/profile is unqualified. Enable Allow unqualified card/profile in Recording settings to proceed; firmware limits still apply.");
                uint end=s.Mask==0?NpV1BleProtocol.EndLba(s,duration,mediaCamera.Checked?(uint)mediaRate.Value:0U,mediaMicrophone.Checked):NpV1BleProtocol.EndLba(s,duration);
                uint session=unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                // Profile changes may reset staged selections; retain and stage
                // each selected port's explicit host request before START.
                if(s.Mask!=0)await new NpConfigClient(new ConfigTransport(connection)).StageForCaptureAsync(configurations.Values,(int)s.Mask,CancellationToken.None);
                RequireStartNotCanceled();
                await Send(NpSdControl.Request(2, NpSdControl.NextId(s), s.Mask, s.Hz, s.Start, end, session == 0 ? 1U : session, s.Qualified ? 0U : 1U));
            }
            Append("START acknowledged; wait for device-confirmed recording. Continuous until Stop or card full.");
        }
        void RequireStartNotCanceled(){if(stopRequested)throw new OperationCanceledException("Recording start canceled by Stop.");}
        async Task StopRecording()
        {
            stopping=true;stopRequested=false;UpdateControls();
            try {
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
                if(s!=null && StopConfirmed(s)){ReportStopped(s);return;}
                // A missing status must not prevent one explicit STOP. Never
                // replay it after a missing ACK; later polls establish state.
                uint id=s==null?Next():V2?NpV2SdControl.NextId(s):NpSdControl.NextId(s);
                s=await Send(V2?NpV2SdControl.Request(3,id):NpSdControl.Request(3,id));
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
        bool StopConfirmed(NpSdStatus s){return V2?NpV2SdControl.StopAlreadySatisfied(s):NpV1BleProtocol.StopAlreadySatisfied(s);}
        void ReportStopped(NpSdStatus s){
            bool failed=s.Fault!=0 || s.State==(V2?255U:4U);
            Append(failed?"Recording is stopped after a device failure. "+(V2?"Fault "+s.Fault:NpV1BleProtocol.FaultText(s))+"; saved data may be incomplete.":"Recording stopped; no capture is active.");
        }
        async Task SyncClock()
        {
            var s = await ReadStatus();
            if (V2 ? !NpV2SdControl.CanConfigure(s) : !NpV1BleProtocol.CanConfigure(s) || (s.Flags & NpV1BleProtocol.Clock) == 0) throw new IOException("Stopped recorder with PC-time support required.");
            if(V2){await InitializeClock(s);return;}
            var result = await NpV2OnlineClock.Synchronize(connection.ExchangeAsync, Next, 16);
            Append("PC time confirmed; uncertainty " + (result.UncertaintySeconds * 1000).ToString("F2") + " ms.");
        }
        void ShowClock(){clockStatus.Text=clock.Synchronized?"PC time synced · ±"+clock.BoundMilliseconds.ToString("F2")+" ms":clock.Error==null?"":"PC time unconfirmed";}
        async Task InitializeClock(NpSdStatus s)
        {
            try{await clock.Initialize(s,connection.ExchangeAsync,Next);}
            catch(Exception ex){clock.Failed(ex);Append("PC time: "+ex.Message);}
            if(!V2 || !connection.Connected){clock.Reset();clockStatus.Text="";return;}
            ShowClock();
        }
        async Task CheckClock()
        {
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
            preview=V2?(Form)new NpBlePreviewForm(connection,()=>V2?current:null,()=>busy || hostBusy,calibration):new NpV1BlePreviewForm(connection,()=>recorderVersion==1?current:null,()=>busy || hostBusy,calibration);
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
                Append("NP2 IMRO request retained. Apply-on-capture stages only after the final profile and blocks START unless the firmware returns the matching staged receipt.");
                return;
            }
            var client = new NpConfigClient(new ConfigTransport(connection));
            using (var dialog = new NpConfigurationDialog(null,availableMask:(int)s.Mask,retainedV1Requests:configurations,
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
