using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;
using WildNpConfiguration;
using Windows.Devices.Bluetooth.Advertisement;

namespace USB512_interface
{
    internal sealed class NpBleForm : Form
    {
        sealed class Peer
        {
            internal ulong Address; internal string Name; internal short Rssi;
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
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly NpBleClock clock=new NpBleClock();
        readonly Label clockStatus=new Label{AutoSize=true};
        NpBlePreviewForm preview;
        NpSdStatus current;
        int recorderVersion;
        Button scan,stopScan,connect,disconnect,channelConfiguration,usbDownload,previewButton,startRecording;
        bool busy, closing;
        internal bool IsBusy{get{return busy;}}
        internal bool SessionOwned{get{return busy || connection.Connected || watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started;}}
        internal event Action StateChanged;
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
            Text = "WILD-X — BLE Control"+(maintenance?" / Advanced":""); Icon = NpPreviewTheme.ApplicationIcon;
            ClientSize = new Size(910, 610); MinimumSize = new Size(820, 550); StartPosition = FormStartPosition.CenterParent;
            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10) };
            top.Controls.AddRange(new Control[] { devices, new Label { Text = "BLE address:", AutoSize = true }, address });
            scan=Add(top, "Scan", () => { if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started) watcher.Start(); state.Text = "Scanning…"; return Task.CompletedTask; });
            stopScan=Add(top, "Stop scan", () => { watcher.Stop(); return Task.CompletedTask; });
            connect=Add(top, "Connect", Connect);
            disconnect=Add(top, "Disconnect", () => { connection.Disconnect(); ResetDevice(); state.Text = "Disconnected."; return Task.CompletedTask; });
            var settings = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10) };
            settings.Controls.AddRange(new Control[] { profiles, new Label { Text = "Storage bound (s; 0 = continuous):", AutoSize = true }, seconds, unqualified, poll });
            Add(actions, "Read status", async () => { await ReadStatus(); });
            Add(actions, "Apply profile", Apply);
            startRecording=Add(actions, "Start SD recording", StartRecording);
            Add(actions, "Stop and drain", StopRecording);
            Add(actions, "Sync PC time", SyncClock);
            previewButton=Add(actions,"NP Preview",()=>{OpenPreview();return Task.CompletedTask;});
            channelConfiguration=Add(actions, "Channel configuration", ConfigureChannels);
            Add(actions, "Mount catalog", async () => {
                var s = await ReadStatus();
                if (V2 && !NpV2SdControl.CanPrepareStorage(s)) throw new IOException("A stopped recorder with storage controls is required.");
                await Send(V2 ? NpV2SdControl.StorageRequest(NpV2SdControl.NextId(s), false) : NpV1BleProtocol.Mount(s, NpSdControl.NextId(s)));
            });
            Add(actions, "Recover interrupted catalog", async () => {
                var s = await ReadStatus();
                if (V2 && !NpV2SdControl.CanRecover(s)) throw new IOException("Firmware does not advertise a recoverable interrupted allocation.");
                await Send(V2 ? NpV2SdControl.Request(8, NpV2SdControl.NextId(s)) : NpV1BleProtocol.Recover(s, NpSdControl.NextId(s)));
            });
            Add(actions, "USB streaming", () => SwitchMode(2, 2));
            usbDownload=Add(actions, "USB download", () => { if (V2) throw new IOException("For NP2, enter USB service, then select Download in Firmware Update."); return SwitchMode(4, 2); });
            if(maintenance){
                Add(actions, "Firmware loader", () => SwitchMode(1, 1));
                Add(actions, "ROM DFU", () => SwitchMode(3, 3));
            }
            var statusPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10) }; statusPanel.Controls.Add(state);statusPanel.Controls.Add(clockStatus);
            Controls.Add(log); Controls.Add(statusPanel); Controls.Add(actions); Controls.Add(settings); Controls.Add(top);
            devices.SelectedIndexChanged += delegate { var p = devices.SelectedItem as Peer; if (p != null) address.Text = p.Address.ToString("X12"); };
            watcher.Received += Advertisement;
            watcher.Stopped += WatcherStopped;
            connection.Disconnected += message => Ui(() => { if(connection.Connected)return;ResetDevice();state.Text = message; Append(message);NotifyState(); });
            timer.Tick += async delegate { if (!busy && poll.Checked && connection.Connected && current != null) await Run(async () => { await ReadStatus();if(V2)await CheckClock(); }); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; return; } Shutdown(); };
            Profiles(null); timer.Start();WildXTheme.Apply(this);
            foreach(Control control in top.Controls)if(control is Button && control.Text=="Connect")WildXTheme.Button((Button)control,true);
            foreach(Control control in actions.Controls)if(control is Button){
                if(control.Text=="Start SD recording")WildXTheme.Button((Button)control,true);
                else if(control.Text=="Stop and drain" || control.Text=="ROM DFU")WildXTheme.Button((Button)control,false,true);
            }
            state.ForeColor=clockStatus.ForeColor=WildXTheme.Muted;
            UpdateControls();
        }
        void WatcherStopped(BluetoothLEAdvertisementWatcher sender,BluetoothLEAdvertisementWatcherStoppedEventArgs e)
        {
            Ui(()=>{if(e.Error!=Windows.Devices.Bluetooth.BluetoothError.Success)state.Text="BLE scan stopped: "+e.Error;UpdateControls();NotifyState();});
        }
        void NotifyState(){var changed=StateChanged;if(!closing && changed!=null)changed();}
        void Shutdown()
        {
            if(closing)return;closing=true;ClosePreview();timer.Stop();watcher.Received-=Advertisement;watcher.Stopped-=WatcherStopped;
            if(watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started)watcher.Stop();connection.Dispose();timer.Dispose();
        }
        protected override void Dispose(bool disposing){if(disposing)Shutdown();base.Dispose(disposing);}
        void Ui(Action action) { if (!closing && !IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        void Advertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            string name = e.Advertisement.LocalName;
            bool known = !String.IsNullOrWhiteSpace(name) && (name.IndexOf("NP", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("WILD", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("CE64", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!known && !e.Advertisement.ServiceUuids.Contains(new Guid("0000fff0-0000-1000-8000-00805f9b34fb"))) return;
            Ui(() => {
                if (busy || connection.Connected) return;
                Peer p; if (!peers.TryGetValue(e.BluetoothAddress, out p)) { p = new Peer { Address = e.BluetoothAddress }; peers.Add(p.Address, p); devices.Items.Add(p); }
                if (!String.IsNullOrWhiteSpace(name)) p.Name = name; if (String.IsNullOrWhiteSpace(p.Name)) p.Name = "FFF0 device";
                p.Rssi = e.RawSignalStrengthInDBm; int index = devices.Items.IndexOf(p); devices.Items[index] = p;
            });
        }
        Button Add(FlowLayoutPanel panel, string title, Func<Task> action)
        {
            var button = new Button { Text = title, AutoSize = true }; panel.Controls.Add(button);
            button.Click += async delegate { await Run(action); };
            return button;
        }
        void UpdateControls()
        {
            bool linked=connection.Connected,ready=linked && recorderVersion!=0 && !busy;
            devices.Enabled=address.Enabled=!busy && !linked;
            scan.Enabled=connect.Enabled=!busy && !linked;
            stopScan.Enabled=!busy && watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started;
            disconnect.Enabled=!busy && linked;
            profiles.Enabled=seconds.Enabled=unqualified.Enabled=poll.Enabled=actions.Enabled=ready;
            channelConfiguration.Visible=recorderVersion!=0;
            channelConfiguration.Text=V2?"NP2 IMRO / channel maps…":"Channel configuration";
            usbDownload.Visible=unqualified.Visible=recorderVersion==1;
            previewButton.Visible=V2;previewButton.Enabled=ready && current!=null && (current.Flags&128)!=0;
            startRecording.Enabled=ready && (!V2 || clock.Synchronized);
        }
        void ClosePreview(){if(preview!=null){preview.Close();preview.Dispose();preview=null;}}
        void ResetDevice(){ClosePreview();clock.Reset();clockStatus.Text="";current=null;recorderVersion=0;Profiles(null);UpdateControls();}
        async Task Run(Func<Task> action)
        {
            if (busy) return; busy = true; UpdateControls();
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
            watcher.Stop(); ResetDevice(); state.Text = "Connecting " + selected + "...";
            try {
                await connection.ConnectAsync(value);
                var s=await ReadStatus();if(V2)await InitializeClock(s);
                if(!connection.Connected)throw new IOException("BLE disconnected during initialization.");
                Append("Connected to " + selected + ".");
            }catch{connection.Disconnect();ResetDevice();throw;}
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
                recorderVersion=detected.Item1;ShowStatus(detected.Item2);Profiles(current);UpdateControls();return current;
            }
            var s = await Send(V2 ? NpV2SdControl.Request(0, Next()) : NpSdControl.Request(0, Next()));
            Profiles(s); return s;
        }
        void Profiles(NpSdStatus s)
        {
            var previous = profiles.SelectedItem as Profile; profiles.Items.Clear();
            if(recorderVersion==0)return;
            if (V2) foreach (uint hz in new uint[] { 20000, 30000 }) profiles.Items.Add(new Profile { Mask = 3, Hz = hz });
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
            await Send(V2 ? NpV2SdControl.Request(1, NpV2SdControl.NextId(s), p.Hz) : NpSdControl.Request(1, NpSdControl.NextId(s), p.Mask, p.Hz));
            Append("Selection acknowledged. Read status to confirm readiness before Start.");
        }
        async Task StartRecording()
        {
            var p = profiles.SelectedItem as Profile; var s = await ReadStatus();
            if (p == null || p.Mask != s.Mask || p.Hz != s.Hz) throw new IOException("Apply the selected profile before Start.");
            if(V2 && !clock.Synchronized)throw new IOException("Synchronize PC time while stopped before starting.");
            uint duration = (uint)seconds.Value;
            if (V2) {
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
                }
                uint blocks = NpV2SdControl.Blocks(s, p.Hz, duration); await Send(NpV2SdControl.Request(2, NpV2SdControl.NextId(s), p.Hz, blocks));
            }
            else {
                if (!s.Qualified && !unqualified.Checked) throw new IOException("This NP1 card/profile is unqualified. The explicit bench option is required; firmware limits still apply.");
                uint end = NpV1BleProtocol.EndLba(s, duration), session = unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                // Profile changes may reset staged selections; retain and stage
                // each selected port's explicit host request before START.
                await new NpConfigClient(new ConfigTransport(connection)).StageForCaptureAsync(configurations.Values,(int)s.Mask,CancellationToken.None);
                await Send(NpSdControl.Request(2, NpSdControl.NextId(s), s.Mask, s.Hz, s.Start, end, session == 0 ? 1U : session, s.Qualified ? 0U : 1U));
            }
            Append("START acknowledged; wait for device-confirmed recording. Duration reserves storage and is not a precise timer.");
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
            preview=new NpBlePreviewForm(connection,()=>V2?current:null,()=>busy);
            preview.Show(DialogOwner);
        }
        async Task ConfigureChannels()
        {
            if (V2) {
                ulong peer=UInt64.Parse(address.Text.Trim(),System.Globalization.NumberStyles.HexNumber);
                Dictionary<int,string> requests;
                if(!np2HostConfigurations.TryGetValue(peer,out requests)){requests=new Dictionary<int,string>();np2HostConfigurations.Add(peer,requests);}
                using(var dialog=new NpConfigurationDialog(null,np2:true,retainedRequests:requests,applyConnectors:Np2ApplyRequests(peer,true)))dialog.ShowDialog(DialogOwner);
                Append("NP2 IMRO request retained. Apply-on-capture stages only after the final profile and blocks START unless the firmware returns the matching staged receipt.");
                return;
            }
            var s = await ReadStatus(); if (!NpV1BleProtocol.CanConfigure(s)) throw new IOException("Stop the NP1 recorder before channel configuration.");
            var client = new NpConfigClient(new ConfigTransport(connection));
            using (var dialog = new NpDeviceConfigForm(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NPConfigurator", "NPConfigurator.exe"), client.StageAsync, client.StatusAsync,connectedMask:(int)s.Mask,retainedRequests:configurations)) {
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
    }
}
