using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;
using WildNpConfiguration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace USB512_interface
{
    internal sealed class NpBleForm : Form
    {
        sealed class Peer
        {
            internal ulong Address; internal BluetoothAddressType Type; internal string Name; internal short Rssi;
            public override string ToString() { return Name + " — " + Address.ToString("X12") + " (" + Rssi + " dBm)"; }
        }
        sealed class Profile
        {
            internal uint Mask, Hz;
            public override string ToString() { return "Probe mask " + Mask + " | " + Hz.ToString("N0") + " samples/s"; }
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
        readonly TextBox address = new TextBox { Width = 160 };
        readonly NumericUpDown seconds = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = 60, Width = 90 };
        readonly CheckBox unqualified = new CheckBox { Text = "Allow firmware-supported unqualified bench profile (NP1)", AutoSize = true };
        readonly CheckBox poll = new CheckBox { Text = "Refresh status every 2 seconds", Checked = true, AutoSize = true };
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly Label state = new Label { Text = "Scan or enter a BLE address, then connect.", AutoSize = true, MaximumSize = new Size(850, 0) };
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
        NpBlePreviewForm preview;
        NpSdStatus current;
        int recorderVersion;
        uint np2AvailableMask;
        Button scan,stopScan,connect,disconnect,rescueReset,channelConfiguration,usbDownload,previewButton,startRecording;
        bool busy, closing, connecting, hostBusy;
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
            var options=new FlowLayoutPanel();options.Controls.AddRange(new Control[]{unqualified,poll});
            var settings=FieldGrid("Recording profile",profiles,"Storage bound (s; 0 = continuous)",seconds,options);settings.Name="bleRecordingFields";
            Add(actions, "Read status", async () => { await ReadStatus(); });
            Add(actions, "Apply profile", Apply);
            startRecording=Add(actions, "Start recording", StartRecording);
            Add(actions, "Stop recording", StopRecording);
            Add(actions, "Sync PC time", SyncClock);
            previewButton=Add(actions,"NP Preview",()=>{OpenPreview();return Task.CompletedTask;});
            channelConfiguration=Add(actions, "Probe configuration…", ConfigureChannels);
            var reportButton=new Button {Text="Report…",AutoSize=true};
            reportButton.Click+=delegate {using(var report=new NpReportForm("BLE report","Device: "+(recorderVersion==0?"Unconfirmed":"Neuropixels V"+recorderVersion)+"\r\nBLE address: "+address.Text+"\r\n"+state.Text+"\r\n"+clockStatus.Text+"\r\n"+configurationStatus.Text+"\r\n\r\n"+log.Text))report.ShowDialog(DialogOwner);};
            Add(actions,"Calibration…",()=>{NpCalibrationStore.Show(DialogOwner,"ble:"+address.Text.Trim().ToUpperInvariant(),V2);return Task.CompletedTask;});
            Add(actions, "Mount catalog", async () => {
                var s = await ReadStatus();
                if (V2 && !NpV2SdControl.CanPrepareStorage(s)) throw new IOException("A stopped recorder with storage controls is required.");
                await Send(V2 ? NpV2SdControl.StorageRequest(NpV2SdControl.NextId(s), false) : NpV1BleProtocol.Mount(s, NpSdControl.NextId(s)));
            });
            Add(maintenanceActions, "Recover interrupted catalog", async () => {
                var s = await ReadStatus();
                if (V2 && !NpV2SdControl.CanRecover(s)) throw new IOException("Firmware does not advertise a recoverable interrupted allocation.");
                await Send(V2 ? NpV2SdControl.Request(8, NpV2SdControl.NextId(s)) : NpV1BleProtocol.Recover(s, NpSdControl.NextId(s)));
            });
            Add(actions, "USB streaming", () => SwitchMode(2, 2));
            usbDownload=Add(actions, "USB download", () => { if (V2) throw new IOException("For NP2, enter USB service, then select Download in Firmware Update."); return SwitchMode(4, 2); });
            Add(maintenanceActions, "Firmware loader", () => SwitchMode(1, 1));
            Add(maintenanceActions, "ROM DFU", () => SwitchMode(3, 3));
            maintenanceActions.SetFlowBreak(maintenanceActions.Controls[maintenanceActions.Controls.Count-1],true);
            maintenanceActions.Controls.Add(maintenanceResult);
            var statusPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=Padding.Empty };
            statusPanel.Controls.Add(state);statusPanel.Controls.Add(clockStatus);statusPanel.Controls.Add(configurationStatus);statusPanel.Controls.Add(reportButton);
            statusPanel.SizeChanged+=delegate {foreach(var label in new[]{state,clockStatus,configurationStatus})label.MaximumSize=new Size(Math.Max(1,statusPanel.ClientSize.Width),0);};
            foreach(var label in new[]{clockStatus,configurationStatus}) {
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
            timer.Tick += async delegate { if (!busy && poll.Checked && connection.Connected) await Run(async () => { await ReadStatus();if(V2)await CheckClock(); }); };
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
            foreach(var label in new[]{state,clockStatus,configurationStatus})label.Margin=new Padding(0,0,0,6);
            state.ForeColor=clockStatus.ForeColor=configurationStatus.ForeColor=WildXTheme.Muted;
            UpdateControls();ResumeLayout(true);
        }
        static TableLayoutPanel FieldGrid(string leftTitle,Control left,string rightTitle,Control right,FlowLayoutPanel below)
        {
            var grid=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=2,RowCount=3,
                Margin=Padding.Empty,Padding=new Padding(0,0,0,12)};
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));
            for(int row=0;row<3;row++)grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(WildXTheme.Label(leftTitle),0,0);grid.Controls.Add(WildXTheme.Label(rightTitle),1,0);
            left.Anchor=right.Anchor=AnchorStyles.Left|AnchorStyles.Right;
            left.Margin=new Padding(0,0,16,0);right.Margin=Padding.Empty;
            grid.Controls.Add(left,0,1);grid.Controls.Add(right,1,1);
            below.AutoSize=true;below.AutoSizeMode=AutoSizeMode.GrowAndShrink;below.Dock=DockStyle.Top;
            below.WrapContents=true;below.Margin=new Padding(0,12,0,0);below.Padding=Padding.Empty;
            grid.Controls.Add(below,0,2);grid.SetColumnSpan(below,2);return grid;
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
            maintenancePeer.Text=linked?"Connected · "+address.Text.Trim():"Connect in BLE Mode";
            profiles.Enabled=seconds.Enabled=unqualified.Enabled=poll.Enabled=actions.Enabled=ready;
            channelConfiguration.Visible=recorderVersion!=0;
            channelConfiguration.Text="Probe configuration…";
            usbDownload.Visible=unqualified.Visible=recorderVersion==1;
            previewButton.Visible=V2;previewButton.Enabled=ready && current!=null && (current.Flags&128)!=0;
            startRecording.Enabled=ready && (V2?clock.Synchronized && NpV2SdControl.CanStart(current):NpV1BleProtocol.CanStart(current));
        }
        void ClosePreview(){if(preview!=null){preview.Close();preview.Dispose();preview=null;}}
        void ResetDevice(){ClosePreview();clock.Reset();clockStatus.Text="";configurationReceipts.Reset();configurationStatus.Text="";configurationReadError=null;current=null;recorderVersion=0;np2AvailableMask=0;Profiles(null);UpdateControls();}
        async Task Run(Func<Task> action)
        {
            if (busy || hostBusy) return; busy = true; UpdateControls();
            NotifyState();
            try { await action(); }
            catch (Exception ex) { current = null; state.Text = ex.Message; Append(ex.Message); }
            finally { busy = false; UpdateControls();NotifyState(); }
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
        void ShowStatus(NpSdStatus s){current=s;state.Text=V2?NpV2SdControl.Describe(s):NpV1BleProtocol.StateText(s)+"\r\n"+s;}
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
                    seconds.Value=0;configurationStatus.Text="NP2 IMRO application not verified in this connection.";
                }
                Profiles(current);UpdateControls();
                return current;
            }
            var s = await Send(V2 ? NpV2SdControl.Request(0, Next()) : NpSdControl.Request(0, Next()));
            Profiles(s);
            if(V2)await CheckConfiguration(s);
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
        void Profiles(NpSdStatus s)
        {
            var previous = profiles.SelectedItem as Profile; profiles.Items.Clear();
            if(recorderVersion==0)return;
            if (V2) {
                foreach (uint hz in new uint[] { 20000, 30000 }) profiles.Items.Add(new Profile { Mask = 3, Hz = hz });
                if(np2AvailableMask==7U)foreach(uint hz in new uint[]{15000,20000})profiles.Items.Add(new Profile{Mask=7,Hz=hz});
            }
            else foreach (uint hz in new uint[] { 7500, 15000, 20000, 30000 }) for (uint mask = 1; mask <= 7; mask++)
                if (NpV1BleProtocol.CanSelect(s, mask, hz)) profiles.Items.Add(new Profile { Mask = mask, Hz = hz });
            for (int i = 0; i < profiles.Items.Count; i++) {
                var p = (Profile)profiles.Items[i];
                if (previous != null ? p.Mask == previous.Mask && p.Hz == previous.Hz : s != null && p.Mask == s.Mask && p.Hz == s.Hz) { profiles.SelectedIndex = i; break; }
            }
            if (profiles.SelectedIndex < 0 && profiles.Items.Count != 0) profiles.SelectedIndex = 0;
        }
        async Task Apply()
        {
            var p = profiles.SelectedItem as Profile; if (p == null) throw new IOException("Select a profile.");
            var s = await ReadStatus();
            if (V2 ? !NpV2SdControl.CanConfigure(s) : !NpV1BleProtocol.CanConfigure(s) || !NpV1BleProtocol.CanSelect(s, p.Mask, p.Hz)) throw new IOException("Wait for the recorder to stop and confirm a supported profile.");
            await Send(V2 ? NpV2SdControl.Request(1, NpV2SdControl.NextId(s), p.Hz, 0, 0, p.Mask) : NpSdControl.Request(1, NpSdControl.NextId(s), p.Mask, p.Hz));
            Append("Selection acknowledged. Read status to confirm readiness before Start.");
        }
        async Task StartRecording()
        {
            var p = profiles.SelectedItem as Profile; var s = await ReadStatus();
            if (p == null || p.Mask != s.Mask || p.Hz != s.Hz) throw new IOException("Apply the selected profile before Start.");
            if(V2 && !clock.Synchronized)throw new IOException("Synchronize PC time while stopped before starting.");
            uint duration = (uint)seconds.Value;
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
                await Send(NpV2SdControl.Request(2, NpV2SdControl.NextId(s), p.Hz, blocks, 0, p.Mask));
            }
            else {
                if (!s.Qualified && !unqualified.Checked) throw new IOException("This NP1 card/profile is unqualified. The explicit bench option is required; firmware limits still apply.");
                uint end = NpV1BleProtocol.EndLba(s, duration), session = unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                // Profile changes may reset staged selections; retain and stage
                // each selected port's explicit host request before START.
                await new NpConfigClient(new ConfigTransport(connection)).StageForCaptureAsync(configurations.Values,(int)s.Mask,CancellationToken.None);
                await Send(NpSdControl.Request(2, NpSdControl.NextId(s), s.Mask, s.Hz, s.Start, end, session == 0 ? 1U : session, s.Qualified ? 0U : 1U));
            }
            Append("START acknowledged; wait for device-confirmed recording. "+(duration==0?"Continuous until Stop or card full.":"Duration reserves storage and is not a precise timer."));
        }
        async Task StopRecording()
        {
            // STOP must remain available when status polling is stale.
            uint id = current == null ? Next() : V2 ? NpV2SdControl.NextId(current) : NpSdControl.NextId(current);
            await Send(V2 ? NpV2SdControl.Request(3, id) : NpSdControl.Request(3, id));
            Append("STOP acknowledged; wait for Complete before removing power or changing mode.");
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
            if(!V2 || current==null || (current.Flags&128)==0)throw new IOException("Device does not advertise NP2 preview support.");
            if(preview!=null && !preview.IsDisposed){preview.Activate();return;}
            var calibration=NpCalibrationStore.Get("ble:"+address.Text.Trim().ToUpperInvariant());calibration.RequireModel(true,(int)current.Mask);
            preview=new NpBlePreviewForm(connection,()=>V2?current:null,()=>busy,calibration);
            preview.Show();
        }
        async Task ConfigureChannels()
        {
            var s=await ReadStatus();
            if(V2?!NpV2SdControl.CanConfigure(s):!NpV1BleProtocol.CanConfigure(s))throw new IOException("Stop recording before probe configuration.");
            if (V2) {
                ulong peer=UInt64.Parse(address.Text.Trim(),System.Globalization.NumberStyles.HexNumber);
                Dictionary<int,string> requests;
                if(!np2HostConfigurations.TryGetValue(peer,out requests)){requests=new Dictionary<int,string>();np2HostConfigurations.Add(peer,requests);}
                using(var dialog=new NpConfigurationDialog(null,np2:true,availableMask:(int)np2AvailableMask,retainedRequests:requests,applyConnectors:Np2ApplyRequests(peer,true),
                    receipt:async(port,json)=>NpConfigurationDialog.Receipt(await new Np2ConfigClient(new ConfigTransport(connection)).StatusAsync((byte)(port-1),CancellationToken.None),json==null?null:NpConfigurationDialog.PrepareV2Request(json,port))))dialog.ShowDialog(DialogOwner);
                Append("NP2 IMRO request retained. Apply-on-capture stages only after the final profile and blocks START unless the firmware returns the matching staged receipt.");
                return;
            }
            var client = new NpConfigClient(new ConfigTransport(connection));
            using (var dialog = new NpConfigurationDialog(null,availableMask:(int)s.Mask,retainedV1Requests:configurations,
                receipt:async(port,json)=>NpConfigurationDialog.Receipt(await client.StatusAsync((byte)(port-1),CancellationToken.None),json==null?null:NpConfigurationDialog.PrepareV1Request(json,port)))) {
                WildXTheme.Apply(dialog);dialog.ShowDialog(DialogOwner);
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
