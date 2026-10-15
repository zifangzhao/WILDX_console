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
            var panel = new GroupBox { Text = "Secondary network time (Local)", Location = new Point(5, 653), Size = new Size(559, 89),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var status = new Label { Name="networkTimeStatus", Location = new Point(10, 22), Size = new Size(420, 57), AutoEllipsis = true,
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
                status.Text = snapshot.HasEstimate ? String.Format("{3:yyyy-MM-dd HH:mm:ss.fff zzz}\r\n{0} · estimated ±{1:F2} ms · {2}\r\n{4} · RTT {5:F2} ms",
                    snapshot.Status, snapshot.UncertaintyMilliseconds, snapshot.TargetStatus,
                    NetworkTimeSnapshot.LocalTime(snapshot.Utc),snapshot.Server,snapshot.DelayMilliseconds)
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
        private readonly CheckBox enabled, automatic;
        private readonly Timer timer = new Timer { Interval = 1000 };
        internal NetworkTimeForm(NetworkTimeClock clock)
        {
            this.clock = clock;
            Text = "Secondary network time";
            Icon = NpPreviewTheme.ApplicationIcon;
            Font = WildXTheme.Body;AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(680, 480);
            StartPosition = FormStartPosition.CenterParent;
            var snapshot = clock.GetSnapshot();
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(16)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int row=0;row<5;row++)layout.RowStyles.Add(new RowStyle(row==3?SizeType.Percent:SizeType.AutoSize,row==3?100:0));
            enabled = new CheckBox { Text = "Synchronize in the background", Checked = snapshot.Enabled, AutoSize = true,Margin=new Padding(0,0,0,8) };
            automatic = new CheckBox { Text = "Select best server automatically", Checked = snapshot.Automatic, AutoSize = true,Margin=new Padding(0,0,0,12) };
            var serverRow=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,12)};
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var label = new Label { Text = "Manual server", AutoSize = true,Anchor=AnchorStyles.Left,Margin=new Padding(0,0,12,0) };
            server = new TextBox { Text = snapshot.ConfiguredServer, Anchor=AnchorStyles.Left|AnchorStyles.Right,Margin=new Padding(0,0,12,0) };
            var apply = new Button { Text = "Apply", AutoSize=true,Anchor=AnchorStyles.Left,Margin=Padding.Empty };
            apply.Click += Apply;
            serverRow.Controls.Add(label,0,0);serverRow.Controls.Add(server,1,0);serverRow.Controls.Add(apply,2,0);
            status = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,Dock=DockStyle.Fill,Margin=Padding.Empty };
            var note = new Label { Text = "Local time follows your Windows time zone. Target: ±10 ms; estimated error includes network delay, jitter and clock drift.",
                Dock=DockStyle.Fill,AutoSize=true,Margin=new Padding(0,12,0,0) };
            layout.Controls.Add(enabled,0,0);layout.Controls.Add(automatic,0,1);layout.Controls.Add(serverRow,0,2);layout.Controls.Add(status,0,3);layout.Controls.Add(note,0,4);Controls.Add(layout);
            WildXTheme.Apply(this);WildXTheme.Button(apply,true);note.ForeColor=WildXTheme.Muted;
            apply.Margin=Padding.Empty;apply.MinimumSize=new Size(90,36);
            Action selection=delegate {automatic.Enabled=enabled.Checked;server.Enabled=enabled.Checked && !automatic.Checked;};
            enabled.CheckedChanged+=delegate {selection();};automatic.CheckedChanged+=delegate {selection();};selection();
            timer.Tick += delegate { RefreshStatus(); };
            RefreshStatus(); timer.Start();
        }
        private void Apply(object sender, EventArgs e)
        {
            try {
                string hostname = NetworkTimeClock.ValidateServer(server.Text);
                Properties.Settings.Default.NetworkTimeServer = hostname;
                Properties.Settings.Default.NetworkTimeEnabled = enabled.Checked;
                Properties.Settings.Default.NetworkTimeAutomatic = automatic.Checked;
                Properties.Settings.Default.Save();
                clock.Configure(enabled.Checked, hostname, automatic.Checked);
                server.Text = hostname;
                RefreshStatus();
            }
            catch (Exception ex) { MessageBox.Show(this, "Unable to apply time settings: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void RefreshStatus()
        {
            var s = clock.GetSnapshot();
            status.Text = s.Status + "\r\nServer: " + s.Server + (s.Automatic?" (automatic)":" (manual)") + "\r\n";
            if (s.HasEstimate) status.AppendText(String.Format(
                "Local time: {0:yyyy-MM-dd HH:mm:ss.fff zzz}\r\nEstimated error: ±{1:F3} ms ({2})\r\nOffset from Windows clock: {3:+0.000;-0.000;0.000} ms\r\nSelected round-trip delay: {4:F3} ms · stratum {5} · {6}\r\nLast accepted sample: {7:yyyy-MM-dd HH:mm:ss.fff zzz} · {8:F1} s ago\r\nFrequency correction: {9:+0.0;-0.0;0.0} ppm · {10}/8 filter samples\r\n",
                NetworkTimeSnapshot.LocalTime(s.Utc), s.UncertaintyMilliseconds, s.TargetStatus,
                s.OffsetMilliseconds, s.DelayMilliseconds, s.Stratum, s.Endpoint, NetworkTimeSnapshot.LocalTime(s.LastSyncUtc), s.AgeSeconds, s.FrequencyPpm, s.Samples));
            if(s.Automatic)status.AppendText("\r\n"+String.Join("\r\n",s.ServerResults)+"\r\n");
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
