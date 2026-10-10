using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class TestNpSystemInfo
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Methods = BindingFlags.Static | BindingFlags.NonPublic;
    const string Uid = "001E002A5942500B20313354";
    const string PathV1 = @"\\?\usb#vid_1209&pid_b386#npv1-001E002A5942500B20313354#{fake}";
    static Assembly assembly;
    static Type model;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Field(object target, string name) { return target.GetType().GetField(name, Fields).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, Fields).SetValue(target, value); }
    static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Fields).Invoke(target, args); }
    static void Put(byte[] bytes, int offset, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4); }
    static byte[] Descriptor(ushort release = 0x0103) {
        var b = new byte[18]; b[0] = 18; b[1] = 1; b[8] = 9; b[9] = 0x12; b[10] = 0x86; b[11] = 0xb3;
        b[12] = (byte)release; b[13] = (byte)(release >> 8); return b;
    }
    static byte[] Service(uint mode = 2, uint busy = 0, uint pending = 0, bool v2 = false) {
        var b = new byte[32]; Put(b, 0, v2 ? 0x3253504eU : 0x3153504eU); Put(b, 4, v2 ? 4U : 257U);
        Put(b, 8, mode); Put(b, 12, 15); Put(b, 16, busy); Put(b, 20, pending);
        Put(b, 24, Convert.ToUInt32(Uid.Substring(0, 8), 16)); Put(b, 28, Convert.ToUInt32(Uid.Substring(8, 8), 16) ^ Convert.ToUInt32(Uid.Substring(16, 8), 16)); return b;
    }
    static byte[] Sd(uint state = 2, uint flags = 1) {
        var b = new byte[384]; Put(b, 0, 0x53444946); Put(b, 4, 1); Put(b, 8, 384); Put(b, 12, state); Put(b, 16, 7);
        Put(b, 20, 5000); Put(b, 28, flags); Put(b, 32, 125000000); Put(b, 40, 25000000); Put(b, 44, 4); Put(b, 48, 1);
        b[64] = 0x1b; b[65] = (byte)'S'; b[66] = (byte)'M'; Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("SN64G"), 0, b, 67, 5);
        Put(b, 168, 0x00ffffff); Put(b, 240, 0x00ff0ff2); Put(b, 312, 0x00ff0ff3); return b;
    }
    static object Snapshot(string family, Func<byte, byte[]> query, List<byte> requests, byte[] descriptor = null) {
        Func<byte, byte, ushort, int, byte[]> read = (type, request, value, length) => {
            Check((type & 0x80) != 0 && length > 0 && length <= 384, "Non-read-only or unbounded request");
            requests.Add(request);
            if (request == 6) { Check(type == 0x80 && value == 0x100 && length == 18, "Descriptor contract"); return descriptor ?? Descriptor(); }
            Check(type == 0xc0 && value == 0, "Vendor IN contract"); return query(request);
        };
        return model.GetMethod("ReadSnapshot", Methods).Invoke(null, new object[] { family == "NP V2" ? PathV1.Replace("npv1-", "npv2-") : PathV1, family, read });
    }
    static string Text(object snapshot, string name) { return (string)Field(snapshot, name); }
    static void Data() {
        var requests = new List<byte>(); var result = Snapshot("NP V1", r => r == 0x6e ? Service() : Sd(), requests);
        Check(String.Join(",", requests) == "6,110,75", "Passive stream sequence");
        Check(Text(result, "Card").Contains("SN64G") && Text(result, "Capacity") == "64.00 GB / 59.60 GiB", "CID and total capacity");
        Check(Text(result, "Bus").Contains("25.000 MHz") && Text(result, "Bus").Contains("4-bit"), "Cached bus source");
        Check(Text(result, "UsbRelease") == "1.03", "USB release mistaken for semantic version");
        var report = (string)Call(result, "Report", "1.0.3.29");
        foreach (var label in new[] { "DEVICE", "FIRMWARE AND SOFTWARE", "SD CARD", "STATUS", "Not reported by firmware", "Free recording space: Unavailable", "Measured recording/write speed: Unavailable", "query-session", "1.0.3.29" }) Check(report.Contains(label), "Missing report field " + label);
        Check(!report.Contains("1.0.36") && !report.Contains("firmware release: 1.03"), "Firmware version guessed");
        requests.Clear(); var capacity = new byte[32]; Put(capacity, 0, 0x3144504e); Put(capacity, 4, 1); Put(capacity, 8, 125000000); Put(capacity, 28, 1);
        result = Snapshot("NP V1", r => r == 0x6e ? Service(3) : capacity, requests);
        Check(String.Join(",", requests) == "6,110,96", "Passive download sequence"); Check(Text(result, "Card") == "Unavailable" && Text(result, "Bus") == "Unavailable", "Invented download identity/speed");
        Check(Text(result, "Capacity").Contains("64.00 GB"), "Passive download capacity lost");
        foreach (var family in new[] { "USB512", "WILD64 MSC", "Unidentified NP USB" }) { requests.Clear(); Snapshot(family, r => { throw new Exception("Unrelated vendor request"); }, requests); Check(requests.Count == 1, "Unrelated device extension"); }
        foreach (var family in new[] { "NP V1", "NP V2" }) foreach (uint busy in new uint[] { 0, 1 }) foreach (uint pending in new uint[] { 0, 2 }) {
            requests.Clear(); result = Snapshot(family, r => r == 0x6e ? Service(2, busy, pending, family == "NP V2") : Sd(), requests);
            Check(requests.Count == (busy == 0 && pending == 0 ? 3 : 2), "Busy/pending SD exclusion");
        }
        foreach (uint state in new uint[] { 0, 1, 2, 3 }) foreach (uint flags in new uint[] { 0, 1 }) {
            requests.Clear(); result = Snapshot("NP V1", r => r == 0x6e ? Service() : Sd(state, flags), requests);
            bool valid = state >= 2 && flags == 1; Check((Text(result, "Card") != "Unavailable") == valid, "Invalid cached identity"); Check((Text(result, "Capacity") != "Unavailable") == valid, "Invalid cached capacity");
        }
        requests.Clear(); result = Snapshot("NP V1", r => { throw new TimeoutException("offline timeout"); }, requests); Check(requests.Count == 2 && Text(result, "Status").Contains("offline timeout"), "Timeout follow-up query");
        requests.Clear(); result = Snapshot("NP V1", r => { throw new System.ComponentModel.Win32Exception(1167); }, requests); Check(requests.Count == 2 && Text(result, "UsbRelease") == "1.03" && Text(result, "Card") == "Unavailable", "Disconnection follow-up/retained descriptor");
        foreach (int length in new[] { 0, 32, 383, 385 }) { requests.Clear(); result = Snapshot("NP V1", r => r == 0x6e ? Service() : new byte[length], requests); Check(Text(result, "Card") == "Unavailable" && Text(result, "Capacity") == "Unavailable", "Malformed cached SD reply accepted"); }
        requests.Clear(); result = Snapshot("NP V1", r => r == 0x6e ? Service() : throw new System.ComponentModel.Win32Exception(31), requests); Check(Text(result, "Card") == "Unavailable" && Text(result, "Capacity") == "Unavailable", "Unsupported SD fallback guessed");
        for (int length = 0; length < 18; length++) { requests.Clear(); result = Snapshot("NP V1", r => { throw new Exception("Unexpected vendor query"); }, requests, new byte[length]); Check(requests.Count == 1 && Text(result, "UsbRelease") == "Unavailable", "Short descriptor accepted"); }
        for (int length = 0; length < 32; length++) { requests.Clear(); result = Snapshot("NP V1", r => new byte[length], requests); Check(requests.Count == 2 && Text(result, "Card") == "Unavailable", "Short service accepted"); }
        requests.Clear(); var changed = Descriptor(); changed[8] = 1; Snapshot("NP V1", r => { throw new Exception("Changed device vendor query"); }, requests, changed); Check(requests.Count == 1, "Changed USB identity vendor query");
        Check((string)model.GetMethod("CapacityText", Methods).Invoke(null, new object[] { 0UL }) == "Unavailable", "Zero capacity invented");
        Check(((string)model.GetMethod("UsbReleaseText", Methods).Invoke(null, new object[] { (ushort)0xabcd })).StartsWith("0xABCD"), "Reserved BCD guessed");
    }
    static void Ui(string output) {
        using (var form = (Form)Activator.CreateInstance(assembly.GetType("USB512_interface.Form1", true))) {
            Check(form.Icon!=null && form.Icon.Size==new Size(32,32),"Main-window waveform icon missing");
            using(var icon=assembly.GetManifestResourceStream("USB512_interface.Waveform.ico"))Check(icon!=null && icon.Length>1000,"Native icon source resource missing");
            Set(form, "devicePath", PathV1); Set(form, "saveFolder", @"C:\offline-only"); Set(form, "deviceRefreshActive", 0);
            var kindType = form.GetType().GetField("selectedDeviceKind", Fields).FieldType;
            foreach (var kind in new[] { "XeNp", "NpV2", "USB512", "Ce64Msc" }) foreach (bool running in new[] { false, true }) foreach (bool impedance in new[] { false, true }) {
                Set(form, "selectedDeviceKind", Enum.Parse(kindType, kind)); Set(form, "running", running); Set(form, "ztestRunning", impedance); Set(form, "systemInfoActive", 1);
                Call(form, "UpdateStartEnabled");
                foreach (var field in new[] { "btnStart", "btnDfu", "btnNpLogger", "btnSdInfo", "btnStreams", "btnImpedance", "btnRefresh", "btnSystemInfoRefresh" }) Check(!((Button)Field(form, field)).Enabled, "EP0 exclusion " + field);
                Check(!((ComboBox)Field(form, "comboDrives")).Enabled, "Selection not excluded");
            }
            Set(form, "selectedDeviceKind", Enum.Parse(kindType, "XeNp")); Set(form, "running", false); Set(form, "ztestRunning", false);
            Call(form, "btnStart_Click", null, EventArgs.Empty); Call(form, "btnDfu_Click", null, EventArgs.Empty); Call(form, "btnStreams_Click", null, EventArgs.Empty);
            Check(!(bool)Field(form, "running"), "Start handler bypassed info ownership");
            var requests = new List<byte>(); var snapshot = Snapshot("NP V1", r => r == 0x6e ? Service() : Sd(), requests);
            var text = (TextBox)Field(form, "systemInfoSummary"); text.Text = "Retained old selection";
            Set(form, "systemInfoGeneration", 3); Call(form, "CompleteSystemInfo", PathV1, 2, snapshot); Check(text.Text == "Retained old selection", "Stale generation accepted");
            Set(form, "systemInfoActive", 1); Call(form, "CompleteSystemInfo", PathV1 + "changed", 3, snapshot); Check(text.Text == "Retained old selection", "Stale path accepted");
            Set(form, "systemInfoActive", 1); Set(form, "running", true); Call(form, "CompleteSystemInfo", PathV1, 3, snapshot); Check(text.Text == "Retained old selection", "Capture-time result accepted");
            Call(form, "RefreshSystemInfo"); Check((int)Field(form, "systemInfoActive") == 0, "Capture-time query scheduled");
            Set(form, "running", false); Call(form, "CompleteSystemInfo", PathV1, 3, snapshot);
            Check(text.Text.Contains("Firmware release") && text.Text.Contains("SD total capacity") && text.ReadOnly, "Main GUI fields absent");
            Check((string)form.GetType().GetMethod("GetPackageVersion", Methods).Invoke(null, null) == assembly.GetName().Version.ToString(), "Title uses stale installation version");
            using (var host = new Form { Text = form.Text, ClientSize = form.ClientSize, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) }) {
                var controls = new List<Control>(); foreach (Control control in form.Controls) controls.Add(control);
                foreach (var control in controls) host.Controls.Add(control);
                host.Show(); Application.DoEvents();
                Check(text.Visible && text.Height >= 120 && text.Bottom <= text.Parent.ClientSize.Height, "System info layout clipped");
                using (var image = new Bitmap(host.Width, host.Height)) { host.DrawToBitmap(image, new Rectangle(Point.Empty, host.Size)); image.Save(System.IO.Path.Combine(output, "usb-system-info-main.png"), ImageFormat.Png); }
                host.Hide(); foreach (var control in controls) form.Controls.Add(control);
            }
        }
    }
    [STAThread] static int Main(string[] args) {
        try { assembly = Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0])); model = assembly.GetType("USB512_interface.NpSystemInfo", true); Directory.CreateDirectory(args[1]); Application.EnableVisualStyles(); Data(); Ui(args[1]); Console.WriteLine("PASS: " + checks + " system-info protocol, format, ownership and offline GUI checks"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
