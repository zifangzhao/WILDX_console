using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal sealed class NpV2FirmwareForm : Form
    {
        sealed class Target
        {
            internal string Path, Uid; internal bool Boot;
            public override string ToString() { return "NP2 " + (Boot ? "firmware loader" : "USB service") + " — UID " + Uid; }
        }
        readonly ComboBox devices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly Label status = new Label { AutoSize = true, Text = "Select the exact NP2 MCU UID before updating." };
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        readonly ComboBox bundledPart=new ComboBox{Name="v2BundledFirmwarePart",DropDownStyle=ComboBoxStyle.DropDownList,Width=250};
        string selectedUid;
        NpFirmwareIdentity firmwareIdentity;
        bool working;
        internal NpV2FirmwareForm(string selectedPath)
        {
            selectedUid = Uid(selectedPath); Text = "NP V2 — Firmware Update"; Icon = NpPreviewTheme.ApplicationIcon;
            ClientSize = new Size(900, 500);MinimumSize=new Size(780,460);StartPosition = FormStartPosition.CenterParent;
            bundledPart.Items.AddRange(new object[]{"Bundled APP only","Full bundle: APP + USB + boot"});bundledPart.SelectedIndex=0;actions.Controls.Add(bundledPart);
            Add("Update bundled firmware…",UpdateBundled);
            Add("Refresh", () => { Discover(); return Task.CompletedTask; });
            Add("Read status",ReadStatus);
            Add("Update APP file…", UpdateApp);
            Add("Bundled firmware files…", () => {NpFirmwareNotice.OpenFiles();return Task.CompletedTask;});
            Add("Recovery loader", () => Mode("--recovery"));
            Add("Exit recovery…", () => {if(!Selected().Boot)throw new IOException("Select the recovery loader to exit. Normal USB modes are in Advanced.");return Mode("--stream");});
            Add("ROM DFU…", () => Mode("--dfu"));
            Add("NP1 firmware manager…", () => { using (var form = new NpFirmwareForm(null)) form.ShowDialog(this); return Task.CompletedTask; });
            var notice = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding = new Padding(8) };status.MaximumSize=new Size(850,0);notice.Controls.Add(status);
            Controls.Add(log); Controls.Add(notice); Controls.Add(actions); Controls.Add(devices);
            devices.SelectedIndexChanged += delegate { var target = devices.SelectedItem as Target; if (target != null) selectedUid = target.Uid;firmwareIdentity=null; };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) e.Cancel = true; };
            Shown += async delegate { Discover();if(devices.SelectedItem!=null)await Run(ReadStatus); };
            WildXTheme.Apply(this);status.ForeColor=WildXTheme.Muted;
            Resize+=delegate{status.MaximumSize=new Size(Math.Max(200,ClientSize.Width-40),0);};
            foreach(Control control in actions.Controls)if(control is Button && control.Text=="Update bundled firmware…")WildXTheme.Button((Button)control,true);
        }
        internal static string Uid(string path)
        {
            var match = Regex.Match(path ?? "", @"#(?:npv2-)?([0-9a-f]{24})#", RegexOptions.IgnoreCase);
            if (!match.Success || match.Groups[1].Value == new string('0', 24) || match.Groups[1].Value.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase)) return null;
            return match.Groups[1].Value.ToUpperInvariant();
        }
        void Discover()
        {
            devices.Items.Clear(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in NpDownloadNative.EnumerateInterfacePaths(NpDownloadNative.NeuropixelsInterfaceGuid)) {
                bool boot = path.IndexOf("vid_1209&pid_b387", StringComparison.OrdinalIgnoreCase) >= 0;
                bool app = path.IndexOf("vid_1209&pid_b386", StringComparison.OrdinalIgnoreCase) >= 0;
                string uid = Uid(path);
                if ((!app && !boot) || uid == null || !seen.Add(path)) continue;
                int i = devices.Items.Add(new Target { Path = path, Uid = uid, Boot = boot });
                if (uid.Equals(selectedUid, StringComparison.OrdinalIgnoreCase)) devices.SelectedIndex = i;
            }
            // A previously selected UID is never replaced with a different board.
            if (selectedUid == null && devices.Items.Count == 1) devices.SelectedIndex = 0;
            status.Text = devices.Items.Count == 0 ? "No NP2 USB service/loader found. Use Advanced → BLE firmware / recovery → Firmware loader, then Refresh. NP1 uses the separate manager." :
                devices.SelectedIndex < 0 ? "Select the exact NP2 MCU UID; the previous target was not substituted." : "Read status or select a mode. APP updates require a V2 .xenp package.";
            status.Text+=FirmwareVersions();
        }
        string FirmwareVersions(){return "\r\nInstalled: "+NpFirmwareCatalog.Bundled.Installed(2,firmwareIdentity)+"\r\nBundled: "+(NpFirmwareCatalog.Bundled.For(2)?.Versions??"Unavailable")+(firmwareIdentity==null?"\r\nVersion comparison unavailable; this firmware does not report image versions.":"");}
        async Task ReadStatus()
        {
            var target=Selected();await Helper(target,target.Boot?"--boot-info":"--mode-info");firmwareIdentity=null;
            if(!target.Boot)try{firmwareIdentity=await Task.Run(()=>NpFirmwareIdentity.ReadUsb(target.Path,2));}catch(Exception ex){log.AppendText("Firmware identity unavailable: "+ex.Message+Environment.NewLine);}
            if(firmwareIdentity!=null&&firmwareIdentity.Usb!=null&&!NpFirmwareCatalog.Bundled.For(2).usbIdentity.Same(firmwareIdentity.Usb))bundledPart.SelectedIndex=1;
            status.Text+=FirmwareVersions();
        }
        Target Selected() { var target = devices.SelectedItem as Target; if (target == null) throw new IOException("Select the exact NP2 MCU UID."); return target; }
        void Add(string title, Func<Task> action)
        {
            var button = new Button { Text = title, AutoSize = true }; actions.Controls.Add(button);
            button.Click += async delegate {await Run(action);};
        }
        async Task Run(Func<Task> action)
        {
                if (working) return; working = true; actions.Enabled = devices.Enabled = false;
                try { await action(); }
                catch (Exception ex) { status.Text = "Stopped: " + ex.Message; log.AppendText(ex.Message + Environment.NewLine); }
                finally { working = false; actions.Enabled = devices.Enabled = true; }
                if(Visible&&ContainsFocus&&firmwareIdentity!=null&&NpFirmwareNotice.Offer(this,selectedUid,NpFirmwareCatalog.Bundled.For(2),firmwareIdentity))bundledPart.Focus();
        }
        async Task UpdateBundled()
        {
            var target=Selected();var release=NpFirmwareCatalog.Bundled.For(2);if(release==null)throw new IOException("Bundled V2 firmware is unavailable.");
            if(bundledPart.SelectedIndex==0){string path=NpFirmwareNotice.AppPath(this,2);if(path!=null)await UpdateApp(path);return;}
            if(target.Boot)throw new IOException("For a full update, exit recovery into the V2 USB service first, then select this logger again.");
            if(release.full==null)throw new IOException("Bundled full V2 firmware is unavailable.");
            string file=NpFirmwareCatalog.Bundled.CheckedPath(release.full);var image=await Task.Run(()=>NpFirmwareImage.LoadBundledV2(file,release.full.sha256));
            await Task.Run(()=>RequireIdleV2(target.Path,target.Uid));
            string cli=NpFirmwareFlash.DefaultCli();
            if(cli==null){using(var dialog=new OpenFileDialog{Filter="STM32CubeProgrammer CLI|STM32_Programmer_CLI.exe",Title="Locate installed STM32CubeProgrammer CLI"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;cli=dialog.FileName;}}
            if(!File.Exists(cli)||!Path.GetFileName(cli).Equals("STM32_Programmer_CLI.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Select STM32_Programmer_CLI.exe from an installed STM32CubeProgrammer distribution.");
            if(MessageBox.Show(this,"Install bundled full V2 firmware on UID "+target.Uid+"?\r\n\r\n"+release.Versions+"\r\n\r\nThis replaces APP, USB service and recovery through ROM DFU. The configuration page and omitted flash are backed up, preserved and checked. Keep USB and power connected; interruption can require physical recovery.\r\n\r\nContinue?","Confirm full V2 update",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX_console","FirmwareOperations",Guid.NewGuid().ToString("N"));
            string result=await Task.Run(()=>new NpFirmwareFlash(a=>NpFirmwareFlash.RunProcess(cli,a),ReportFlash,directory).Execute(image,target.Uid,false,
                ()=>RunHelper(target.Uid,"--dfu",null).GetAwaiter().GetResult(),()=>WaitForV2(target.Uid)));
            firmwareIdentity=null;status.Text=result+FirmwareVersions();log.AppendText(result+Environment.NewLine);
        }
        void ReportFlash(string message,int percent)
        {if(!IsDisposed&&IsHandleCreated)try{BeginInvoke((Action)(()=>status.Text=percent+"% · "+message));}catch(InvalidOperationException){}}
        static void RequireIdleV2(string path,string uid)
        {
            using(var control=new NpSystemInfoControl(path)){var state=NpV2ServiceStatus.Parse(control.Read(0xc0,0x6e,0,32));state.RequireUid(uid);
                if(state.Busy!=0||state.Target!=0||(state.Mode!=2&&state.Mode!=3))throw new IOException("Select an idle V2 USB streaming or download service before a full update.");}
        }
        static bool WaitForV2(string uid)
        {
            var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<30000){var paths=DownloadPaths(uid);if(paths.Count>1)throw new IOException("The reconnecting V2 UID is ambiguous.");
                if(paths.Count==1)try{RequireIdleV2(paths[0],uid);return true;}catch(Win32Exception){}catch(IOException){}Thread.Sleep(250);}return false;
        }
        async Task Mode(string mode)
        {
            var target = Selected();
            if (mode == "--dfu" && MessageBox.Show(this,
                "Reset MCU UID " + target.Uid + " into STM32 ROM DFU?\r\n\r\nThis disconnects USB and leaves normal logging unavailable until the device is restarted. No firmware will be written automatically.\r\n\r\nAfter the request is acknowledged, confirm STM32 BOOTLOADER in STM32CubeProgrammer before updating firmware.",
                "Enter ROM DFU", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await Helper(target, mode);
            status.Text = mode == "--dfu" ?
                "ROM DFU request acknowledged for UID " + target.Uid + ". Confirm STM32 BOOTLOADER in STM32CubeProgrammer; USB disconnect alone is not confirmation. No firmware was written." :
                "Mode request acknowledged for UID " + target.Uid + ". Refresh after USB re-enumeration.";
        }
        Task UpdateApp() { return UpdateApp(null); }
        async Task UpdateApp(string bundledPath)
        {
            var target = Selected(); string file=bundledPath;
            if(file==null)using (var dialog = new OpenFileDialog { Filter = "V2 APP package (*.xenp)|*.xenp", Title = "Select a V2 application package" }) { if (dialog.ShowDialog(this) != DialogResult.OK) return; file = dialog.FileName; }
            if (MessageBox.Show(this, "Update NP2 APP on MCU UID " + target.Uid + "?\r\n\r\n" + file + "\r\n\r\nKeep USB and power connected until the loader confirms the CRC commit.", "Update selected NP2", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await Helper(target, file);firmwareIdentity=null; status.Text = "APP CRC commit acknowledged and boot requested for UID " + target.Uid + ". Reconnect to confirm firmware operation.";
        }
        async Task Helper(Target target, string operation)
        {
            status.Text = "Working on UID " + target.Uid + "...";
            status.Text=await RunHelper(target.Uid,operation,line=>{
                if (!IsDisposed && IsHandleCreated) try { BeginInvoke((Action)(() => log.AppendText(line + Environment.NewLine))); } catch (InvalidOperationException) { }
            });
        }
        internal static string UsbModeOperation(ushort mode)
        {
            switch(mode){case 1:return "--sd";case 2:return "--stream";case 3:return "--download";default:throw new ArgumentOutOfRangeException("mode");}
        }
        internal static Task<string> SwitchUsbMode(string selectedPath,ushort mode)
        {
            string uid=Uid(selectedPath);
            if(uid==null || NpV1Management.UidFromPath(selectedPath)!=null)throw new IOException("Select the exact V2 USB device before changing mode.");
            return RunHelper(uid,UsbModeOperation(mode),null);
        }
        static NpV2ServiceStatus DownloadStatus(string path,string uid)
        {
            using(var control=new NpSystemInfoControl(path)){
                byte[] bytes;
                try{bytes=control.Read(0xc0,0x6e,0,32);}
                catch(Win32Exception ex) when(ex.NativeErrorCode==1 || ex.NativeErrorCode==31){
                    // Older standalone download firmware has no mode service.
                    // Confirm its read-only protocol; never send it a mode OUT.
                    var info=control.Read(0xc0,0x60,0,32);
                    if(BitConverter.ToUInt32(info,0)!=0x3144504e || BitConverter.ToUInt32(info,4)!=1 || BitConverter.ToUInt32(info,28)!=1)
                        throw new IOException("This firmware does not expose a supported USB mode or download service.");
                    return new NpV2ServiceStatus{Mode=3,Busy=BitConverter.ToUInt32(info,16)};
                }
                var state=NpV2ServiceStatus.Parse(bytes);state.RequireUid(uid);
                if(state.Mode!=2 && state.Mode!=3)throw new IOException("Selected V2 must be in USB streaming or download mode.");
                return state;
            }
        }
        static List<string> DownloadPaths(string uid){return NpDownloadNative.EnumerateNpPaths().Where(path=>NpV1Management.UidFromPath(path)==null && String.Equals(Uid(path),uid,StringComparison.OrdinalIgnoreCase)).ToList();}
        internal static async Task<string> EnsureDownload(string selectedPath,Func<bool> cancelled=null)
        {
            string uid=Uid(selectedPath);
            if(uid==null || NpV1Management.UidFromPath(selectedPath)!=null)throw new IOException("Select the exact V2 USB logger before downloading.");
            if(cancelled!=null && cancelled())throw new OperationCanceledException();
            var paths=DownloadPaths(uid);
            if(paths.Count!=1 || !paths[0].Equals(selectedPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Selected V2 is absent or ambiguous. Refresh sources; another logger was not substituted.");
            var state=await Task.Run(()=>DownloadStatus(selectedPath,uid));
            if(state.Busy!=0 || state.Target!=0)throw new IOException("Stop recording and finish the active V2 operation before downloading.");
            if(state.Mode==3)return selectedPath;
            if(cancelled!=null && cancelled())throw new OperationCanceledException();
            await SwitchUsbMode(selectedPath,3);
            var elapsed=Stopwatch.StartNew();
            while(elapsed.ElapsedMilliseconds<30000){
                if(cancelled!=null && cancelled())throw new OperationCanceledException();
                await Task.Delay(250);paths=DownloadPaths(uid);
                if(paths.Count>1)throw new IOException("Ambiguous V2 UID after download-mode entry.");
                if(paths.Count==0)continue;
                try{state=await Task.Run(()=>DownloadStatus(paths[0],uid));
                    if(state.Mode==3 && state.Busy==0 && state.Target==0)return paths[0];
                }catch(Win32Exception){ /* The selected device may still be re-enumerating. */ }
            }
            throw new IOException("Selected V2 did not return to idle USB download within 30 seconds. No mode request was replayed.");
        }
        static async Task<string> RunHelper(string uid,string operation,Action<string> append)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FirmwareUpdater", "XeNpFirmwareUpdater.exe");
            if (!File.Exists(executable)) throw new IOException("Bundled V2 firmware updater is missing; rebuild the WILDX application.");
            var arguments = new[] { operation, "--uid", uid };
            string command = String.Join(" ", Array.ConvertAll(arguments, NpFirmwareFlash.Quote));
            string output = await Task.Run(() => {
                var text = new StringBuilder(); object sync = new object();
                using (var process = new Process { StartInfo = new ProcessStartInfo(executable, command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } }) {
                    DataReceivedEventHandler receive = (sender, e) => { if (e.Data == null) return; lock (sync) { if (text.Length < 4*1024*1024) text.AppendLine(e.Data); } if(append!=null)append(e.Data); };
                    process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
                    if (!process.Start()) throw new IOException("V2 updater could not start.");
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if (!process.WaitForExit(300000)) { process.Kill(); process.WaitForExit(); throw new IOException("V2 updater timed out; outcome unknown. No retry sent."); }
                    process.WaitForExit();
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WILDX firmware logs"); Directory.CreateDirectory(folder);
                    string saved = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + uid + ".log"); File.WriteAllText(saved, command + Environment.NewLine + text);
                    if (process.ExitCode != 0) throw new IOException("V2 operation failed; no automatic retry. See " + saved);
                    return text.ToString();
                }
            });
            return output.Trim();
        }
    }
}
