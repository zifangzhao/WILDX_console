using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        string selectedUid;
        bool working;
        internal NpV2FirmwareForm(string selectedPath)
        {
            selectedUid = Uid(selectedPath); Text = "WILD-X — Firmware and USB Modes"; Icon = NpPreviewTheme.ApplicationIcon;
            ClientSize = new Size(900, 460); StartPosition = FormStartPosition.CenterParent;
            Add("Refresh", () => { Discover(); return Task.CompletedTask; });
            Add("Read status", async () => { var target = Selected(); await Helper(target, target.Boot ? "--boot-info" : "--mode-info"); });
            Add("Run + hold self-test", () => SelfTest(1U)); Add("Hold rails", () => SelfTest(2U)); Add("Read self-test report", ReadSelfTestReport);
            Add("Release rails", () => SelfTest(4U)); Add("Manual SD 1.8 V", () => SelfTest(3U)); Add("Manual SD 3.3 V", () => SelfTest(5U));
            Add("Update NP2 APP…", UpdateApp);
            Add("USB streaming", () => Mode("--stream")); Add("USB download", () => Mode("--download"));
            Add("SD / BLE", () => Mode("--sd")); Add("Recovery loader", () => Mode("--recovery"));
            Add("NP1 firmware manager…", () => { using (var form = new NpFirmwareForm(null)) form.ShowDialog(this); return Task.CompletedTask; });
            var notice = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(8) }; notice.Controls.Add(status);
            Controls.Add(log); Controls.Add(notice); Controls.Add(actions); Controls.Add(devices);
            devices.SelectedIndexChanged += delegate { var target = devices.SelectedItem as Target; if (target != null) selectedUid = target.Uid; };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) e.Cancel = true; };
            Shown += delegate { Discover(); };
            WildXTheme.Apply(this);status.ForeColor=WildXTheme.Muted;
            foreach(Control control in actions.Controls)if(control is Button && control.Text=="Update NP2 APP…")WildXTheme.Button((Button)control,true);
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
        }
        Target Selected() { var target = devices.SelectedItem as Target; if (target == null) throw new IOException("Select the exact NP2 MCU UID."); return target; }
        void Add(string title, Func<Task> action)
        {
            var button = new Button { Text = title, AutoSize = true }; actions.Controls.Add(button);
            button.Click += async delegate {
                if (working) return; working = true; actions.Enabled = devices.Enabled = false;
                try { await action(); }
                catch (Exception ex) { status.Text = "Stopped: " + ex.Message; log.AppendText(ex.Message + Environment.NewLine); }
                finally { working = false; actions.Enabled = devices.Enabled = true; }
            };
        }
        async Task Mode(string mode) { var target = Selected(); await Helper(target, mode); status.Text = "Mode request acknowledged for UID " + target.Uid + ". Refresh after USB re-enumeration."; }
        async Task ReadSelfTestReport()
        {
            var target = Selected();
            if (target.Boot) throw new IOException("The V2 self-test is available only from the resident B386 USB stream service.");
            var report = await Task.Run(() => { using (var device = NpV2UsbDevice.Open(target.Path)) return device.GetSelfTestReport(target.Uid); });
            ShowSelfTestReport(target, report);
        }
        async Task SelfTest(uint command)
        {
            var target = Selected();
            if (target.Boot) throw new IOException("The V2 self-test is available only from the resident B386 USB stream service.");
            var report = await Task.Run(() => {
                using (var device = NpV2UsbDevice.Open(target.Path))
                {
                    uint baselineSequence = device.StartSelfTest(command,target.Uid);
                    var watch = Stopwatch.StartNew(); NpV2UsbSelfTestReport latest = null;
                    while (watch.Elapsed.TotalSeconds < 20.0)
                    {
                        latest = device.GetSelfTestReport(target.Uid);
                        if (latest.IsNewTerminalFor(command, baselineSequence)) return latest;
                        Thread.Sleep(200);
                    }
                    throw new TimeoutException("Self-test report did not advance to a new terminal state within 20 seconds. No retry or release was sent; any requested hold remains active.");
                }
            });
            ShowSelfTestReport(target, report);
        }
        void ShowSelfTestReport(Target target, NpV2UsbSelfTestReport report)
        {
            string text = report.Text();
            log.AppendText(DateTime.UtcNow.ToString("O")+" UID "+target.Uid+"\r\n"+text+"\r\n");
            string outcome = report.State==NpV2UsbSelfTestReport.Rejected || report.Error!=0U ? "Self-test command rejected or failed; inspect the report." :
                report.State==NpV2UsbSelfTestReport.Queued || report.State==NpV2UsbSelfTestReport.Running ? "Self-test is still in progress." :
                report.Checked==0U && report.Skipped==0U ? "No component checks have run." :
                report.Failed != 0U ? "Self-test has failed checks; inspect the report." : report.Skipped != 0U ? "Self-test has skipped checks; it is not an overall pass." : "Self-test has no failed or skipped checks.";
            status.Text = outcome+" "+(report.Holding ? "Power hold remains active; use Release rails when measurements are complete." : "Use the explicit USB streaming action to reboot to normal mode before capture.");
        }
        async Task UpdateApp()
        {
            var target = Selected(); string file;
            using (var dialog = new OpenFileDialog { Filter = "V2 APP package (*.xenp)|*.xenp", Title = "Select a V2 application package" }) { if (dialog.ShowDialog(this) != DialogResult.OK) return; file = dialog.FileName; }
            if (MessageBox.Show(this, "Update NP2 APP on MCU UID " + target.Uid + "?\r\n\r\n" + file + "\r\n\r\nKeep USB and power connected until the loader confirms the CRC commit.", "Update selected NP2", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await Helper(target, file); status.Text = "APP CRC commit acknowledged and boot requested for UID " + target.Uid + ". Reconnect to confirm firmware operation.";
        }
        async Task Helper(Target target, string operation)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FirmwareUpdater", "XeNpFirmwareUpdater.exe");
            if (!File.Exists(executable)) throw new IOException("Bundled V2 firmware updater is missing; rebuild the WILDX application.");
            var arguments = new[] { operation, "--uid", target.Uid };
            string command = String.Join(" ", Array.ConvertAll(arguments, NpFirmwareFlash.Quote));
            status.Text = "Working on UID " + target.Uid + "...";
            string output = await Task.Run(() => {
                var text = new StringBuilder(); object sync = new object();
                using (var process = new Process { StartInfo = new ProcessStartInfo(executable, command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } }) {
                    DataReceivedEventHandler receive = (sender, e) => { if (e.Data == null) return; lock (sync) { if (text.Length < 4*1024*1024) text.AppendLine(e.Data); } if (!IsDisposed && IsHandleCreated) try { BeginInvoke((Action)(() => log.AppendText(e.Data + Environment.NewLine))); } catch (InvalidOperationException) { } };
                    process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
                    if (!process.Start()) throw new IOException("V2 updater could not start.");
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if (!process.WaitForExit(300000)) { process.Kill(); process.WaitForExit(); throw new IOException("V2 updater timed out; outcome unknown. No retry sent."); }
                    process.WaitForExit();
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WILDX firmware logs"); Directory.CreateDirectory(folder);
                    string saved = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + target.Uid + ".log"); File.WriteAllText(saved, command + Environment.NewLine + text);
                    if (process.ExitCode != 0) throw new IOException("V2 operation failed; no automatic retry. See " + saved);
                    return text.ToString();
                }
            });
            status.Text = output.Trim();
        }
    }
}
