using System;
using System.Drawing;
using System.Windows.Forms;

namespace USB512_interface
{
    public partial class Form1
    {
        private void InitializeNetworkTime()
        {
            ClientSize = new Size(ClientSize.Width, 755);
            var panel = new GroupBox { Text = "Secondary network time (UTC)", Location = new Point(5, 653), Size = new Size(559, 89),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var status = new Label { Location = new Point(10, 22), Size = new Size(420, 57), AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var settings = new Button { Text = "Time settings…", Location = new Point(438, 22), Size = new Size(111, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right, Enabled = Program.NetworkClock != null };
            settings.Click += delegate { using (var dialog = new NetworkTimeForm(Program.NetworkClock)) dialog.ShowDialog(this); };
            panel.Controls.AddRange(new Control[] { status, settings });
            Controls.Add(panel);
            var timer = new Timer { Interval = 1000 };
            if (components == null) components = new System.ComponentModel.Container();
            components.Add(timer);
            Action refresh = delegate {
                var clock = Program.NetworkClock;
                if (clock == null) { status.Text = "Network clock is available when the app starts."; return; }
                var snapshot = clock.GetSnapshot();
                status.Text = snapshot.HasEstimate ? String.Format("{0} · estimated ±{1:F2} ms · {2}\r\nUTC {3:yyyy-MM-dd HH:mm:ss.fff} · recording clock unchanged",
                    snapshot.Status, snapshot.UncertaintyMilliseconds, snapshot.MeetsOneMillisecondTarget ? "≤1 ms estimate" : "above 1 ms target", snapshot.Utc)
                    : snapshot.Status + " · " + snapshot.Server + "\r\n" + (snapshot.Error ?? "Existing recording clock remains active.");
            };
            timer.Tick += delegate { refresh(); };
            refresh(); timer.Start();
        }
    }

    internal sealed class NetworkTimeForm : Form
    {
        private readonly NetworkTimeClock clock;
        private readonly TextBox server, status;
        private readonly CheckBox enabled;
        private readonly Timer timer = new Timer { Interval = 1000 };
        internal NetworkTimeForm(NetworkTimeClock clock)
        {
            this.clock = clock;
            Text = "Secondary network time";
            Icon = NpPreviewTheme.ApplicationIcon;
            Font = new Font("Segoe UI", 9);
            ClientSize = new Size(650, 390);
            MinimumSize = new Size(600, 425);
            StartPosition = FormStartPosition.CenterParent;
            var snapshot = clock.GetSnapshot();
            enabled = new CheckBox { Text = "Synchronize in the background", Checked = snapshot.Enabled, Location = new Point(14, 12), AutoSize = true };
            var label = new Label { Text = "NTP server", Location = new Point(14, 46), AutoSize = true };
            server = new TextBox { Text = snapshot.Server, Location = new Point(96, 42), Size = new Size(410, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var apply = new Button { Text = "Apply", Location = new Point(518, 40), Size = new Size(116, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            apply.Click += Apply;
            status = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(14, 83), Size = new Size(620, 219),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            var note = new Label { Text = "This is an app-local UTC clock for future use. Current recording timestamps and the Windows clock are unchanged.\r\nEstimated error includes network delay, server uncertainty, jitter, correction and holdover drift. Internet NTP cannot guarantee ±1 ms; a nearby disciplined server may be needed.",
                Location = new Point(14, 314), Size = new Size(620, 65), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.AddRange(new Control[] { enabled, label, server, apply, status, note });
            WildXTheme.Apply(this);WildXTheme.Button(apply,true);note.ForeColor=WildXTheme.Muted;
            timer.Tick += delegate { RefreshStatus(); };
            RefreshStatus(); timer.Start();
        }
        private void Apply(object sender, EventArgs e)
        {
            try {
                string hostname = NetworkTimeClock.ValidateServer(server.Text);
                Properties.Settings.Default.NetworkTimeServer = hostname;
                Properties.Settings.Default.NetworkTimeEnabled = enabled.Checked;
                Properties.Settings.Default.Save();
                clock.Configure(enabled.Checked, hostname);
                server.Text = hostname;
                RefreshStatus();
            }
            catch (Exception ex) { MessageBox.Show(this, "Unable to apply time settings: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void RefreshStatus()
        {
            var s = clock.GetSnapshot();
            status.Text = s.Status + "\r\nServer: " + s.Server + "\r\n";
            if (s.HasEstimate) status.AppendText(String.Format(
                "UTC: {0:yyyy-MM-dd HH:mm:ss.fff}\r\nEstimated error: ±{1:F3} ms ({2})\r\nOffset from Windows UTC: {3:+0.000;-0.000;0.000} ms\r\nSelected round-trip delay: {4:F3} ms · stratum {5} · {6}\r\nLast accepted sample: {7:yyyy-MM-dd HH:mm:ss.fff} UTC · {8:F1} s ago\r\nFrequency correction: {9:+0.0;-0.0;0.0} ppm · {10}/8 filter samples\r\n",
                s.Utc, s.UncertaintyMilliseconds, s.MeetsOneMillisecondTarget ? "within 1 ms target by estimate" : "1 ms target not met",
                s.OffsetMilliseconds, s.DelayMilliseconds, s.Stratum, s.Endpoint, s.LastSyncUtc, s.AgeSeconds, s.FrequencyPpm, s.Samples));
            if (s.Error != null) status.AppendText("Last network result: " + s.Error + "\r\n");
            if (Program.NetworkTimeSettingsError != null) status.AppendText("Startup settings: " + Program.NetworkTimeSettingsError);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
