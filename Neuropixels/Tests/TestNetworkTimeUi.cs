using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Inspect the actual Release assembly. The main form is never shown or loaded;
// its controls are painted in an inert host, without USB discovery or capture.
internal static class TestNetworkTimeUi
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    private static void Render(Form form, string path)
    {
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
        form.Show(); form.PerformLayout(); Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
        form.Hide();
    }
    [STAThread] private static int Main(string[] args)
    {
        try {
            Application.EnableVisualStyles();
            var assembly = Assembly.LoadFrom(args[0]); Directory.CreateDirectory(args[1]);
            Check(assembly.GetName().Version.ToString() == "1.0.3.34", "Actual app version");
            var clockType = assembly.GetType("USB512_interface.NetworkTimeClock", true);
            var programType = assembly.GetType("USB512_interface.Program", true);
            object clock = Activator.CreateInstance(clockType, true);
            programType.GetField("<NetworkClock>k__BackingField", Flags).SetValue(null, clock);
            try {
                clockType.GetMethod("Configure", Flags).Invoke(clock, new object[] { true, "time.cloudflare.com" });
                object sample = Activator.CreateInstance(assembly.GetType("USB512_interface.NtpSample", true), true);
                var sampleType = sample.GetType();
                sampleType.GetField("Timestamp", Flags).SetValue(sample, System.Diagnostics.Stopwatch.GetTimestamp());
                sampleType.GetField("OffsetMilliseconds", Flags).SetValue(sample, 12.0);
                sampleType.GetField("DelayMilliseconds", Flags).SetValue(sample, 9.0);
                sampleType.GetField("UncertaintyMilliseconds", Flags).SetValue(sample, 10.0);
                sampleType.GetField("Stratum", Flags).SetValue(sample, 3);
                sampleType.GetField("Endpoint", Flags).SetValue(sample, "offline fixture");
                clockType.GetMethod("Observe", Flags).Invoke(clock, new[] { sample });
                using (var main = (Form)Activator.CreateInstance(assembly.GetType("USB512_interface.Form1", true)))
                using (var host = new Form { Text = main.Text, ClientSize = main.ClientSize }) {
                    var panel = main.Controls.OfType<GroupBox>().Single(g => g.Text == "Secondary network time (UTC)");
                    Check(main.ClientSize.Height == 755 && panel.Bottom < main.ClientSize.Height, "Main clock panel fits");
                    Check(panel.Controls.OfType<Button>().Single().Enabled, "Time settings available independently of USB");
                    string label = panel.Controls.OfType<Label>().Single().Text;
                    Check(label.Contains("UTC") && label.Contains("above 1 ms target") && label.Contains("recording clock unchanged"), "Main status states timing quality");
                    Control[] controls = main.Controls.Cast<Control>().ToArray();
                    main.Controls.Clear(); host.Controls.AddRange(controls);
                    Render(host, Path.Combine(args[1], "main-network-time.png"));
                }
                var formType = assembly.GetType("USB512_interface.NetworkTimeForm", true);
                using (var form = (Form)Activator.CreateInstance(formType, Flags, null, new[] { clock }, null)) {
                    var status = form.Controls.OfType<TextBox>().Single(t => t.Multiline);
                    Check(status.Text.Contains("1 ms target not met") && status.Text.Contains("±10."), "Quality displayed without claiming 1 ms");
                    Check(status.Text.Contains("stratum 3") && status.Text.Contains("Frequency correction"), "Time diagnostics displayed");
                    Check(form.Controls.OfType<TextBox>().Single(t => !t.Multiline).Text == "time.cloudflare.com", "Default server shown");
                    Check(form.Controls.OfType<CheckBox>().Single().Checked, "Background sync enabled");
                    Render(form, Path.Combine(args[1], "network-time-settings.png"));
                    clockType.GetMethod("Configure", Flags).Invoke(clock, new object[] { false, "localhost" });
                    formType.GetMethod("RefreshStatus", Flags).Invoke(form, null);
                    Check(status.Text.Contains("Disabled") && !status.Text.Contains("UTC:"), "Disabled status does not claim a synchronized time");
                }
                var settingsType = assembly.GetType("USB512_interface.Properties.Settings", true);
                object settings = Activator.CreateInstance(settingsType, true);
                Check((bool)settingsType.GetProperty("NetworkTimeEnabled").GetValue(settings), "Enabled preference default");
                Check((string)settingsType.GetProperty("NetworkTimeServer").GetValue(settings) == "time.cloudflare.com", "Server preference default");
            }
            finally { ((IDisposable)clock).Dispose(); programType.GetField("<NetworkClock>k__BackingField", Flags).SetValue(null, null); }
            Console.WriteLine("PASS: " + checks + " actual Release network-time UI/default preference checks; no main-form Load, network, USB or capture");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
