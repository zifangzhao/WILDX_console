using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// HIL ONLY: main Form1 is hidden (never Load/Shown); its real worker owns USB.
// No separate capture implementation, device enumeration, modal answers or retry.
internal static class NpUsbUiHardwareTests
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string TargetUid = "001A0004434250062039314E";
    const string InterfaceGuid = "8f5a4a7b-0aa6-4f8e-a14f-a2f7b3c6aa4e";
    static Assembly app;
    static StreamWriter events;
    static Form form;
    static int checks;
    static long phaseDeadline, uiLast;
    static double uiMaxGap;
    static int uiTicks;
    static uint uiThread;
    static string phase = "admission";
    static readonly Stopwatch wall = Stopwatch.StartNew();
    delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    static void Log(string value)
    {
        string line = wall.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + " " + value;
        Console.WriteLine(line);
        if (events != null) lock (events) { events.WriteLine(line); events.Flush(); }
    }
    static void Require(bool value, string reason) { checks++; if (!value) throw new IOException(reason); }
    static Type Type(string name) { return app.GetType("USB512_interface." + name, true); }
    static FieldInfo Field(object target, string name)
    {
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, All | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
    static object Get(object target, string name)
    {
        FieldInfo f = Field(target, name);
        if (f != null) return f.GetValue(target);
        PropertyInfo p = target.GetType().GetProperty(name, All);
        if (p == null) throw new MissingMemberException(target.GetType().FullName, name);
        return p.GetValue(target, null);
    }
    static void Set(object target, string name, object value)
    {
        FieldInfo f = Field(target, name);
        if (f == null) throw new MissingFieldException(target.GetType().FullName, name);
        f.SetValue(target, value);
    }
    static object Call(object target, string name, params object[] args)
    {
        Type t = target as Type ?? target.GetType();
        MethodInfo selected = null;
        foreach (MethodInfo m in t.GetMethods(All))
            if (m.Name == name && m.GetParameters().Length == args.Length)
            {
                if (selected != null) throw new AmbiguousMatchException(name);
                selected = m;
            }
        if (selected == null) throw new MissingMethodException(t.FullName, name);
        try { return selected.Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
    static long Number(object value) { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
    static void Profile(uint mask, uint rate)
    {
        Require(mask == 1 || mask == 2 || mask == 3 || mask == 7, "Unsupported mask");
        Require(rate == 15000 || rate == 20000 || rate == 30000, "Unsupported rate");
        Require(!(rate == 30000 && (mask == 3 || mask == 7)), "Dual/triple30 stress is outside this UI lifecycle slice");
    }
    static bool Running { get { return (bool)Get(form, "running"); } }
    static string Label { get { return ((Control)Get(form, "lblRecInfo")).Text; } }
    static void Pump() { Application.DoEvents(); Thread.Sleep(5); }
    static void Until(Func<bool> ready, double seconds, string reason)
    {
        var wait = Stopwatch.StartNew();
        while (!ready()) { if (wait.Elapsed.TotalSeconds >= seconds) throw new TimeoutException(reason); Pump(); }
    }
    static void For(double seconds)
    {
        var wait = Stopwatch.StartNew(); while (wait.Elapsed.TotalSeconds < seconds) Pump();
    }
    static void Phase(string name)
    {
        phase = name; Interlocked.Exchange(ref phaseDeadline, Stopwatch.GetTimestamp() + 360L * Stopwatch.Frequency);
        uiTicks = 0; uiMaxGap = 0; uiLast = Stopwatch.GetTimestamp(); Log("phase=" + name);
    }
    static void Watchdog(object unused)
    {
        // A real MessageBox has its own nested message pump; DoEvents alone
        // cannot bound it. Never click/close it or convert its error to success.
        EnumThreadWindows(uiThread, delegate(IntPtr window, IntPtr parameter)
        {
            var name = new StringBuilder(64); GetClassName(window, name, name.Capacity);
            if (name.ToString() == "#32770")
            {
                var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity);
                Log("FAIL unexpected modal: " + text + "; files retained; no retry/recovery. phase=" + phase);
                Environment.Exit(2);
            }
            return true;
        }, IntPtr.Zero);
        if (Stopwatch.GetTimestamp() >= Interlocked.Read(ref phaseDeadline))
        {
            Log("FAIL bounded UI/worker deadline; files retained. Hardware state is NOT qualified or reset. phase=" + phase);
            Environment.Exit(2);
        }
    }
    static void Admit(string path, string uid, uint mask, uint rate, bool completed)
    {
        Require(String.Equals((string)Call(Type("NpV2FirmwareForm"), "Uid", path), uid, StringComparison.OrdinalIgnoreCase), "Full path UID mismatch");
        Require(path.IndexOf("vid_1209&pid_b386", StringComparison.OrdinalIgnoreCase) >= 0 &&
            path.IndexOf(InterfaceGuid, StringComparison.OrdinalIgnoreCase) >= 0, "Exact NP2 USB interface required");
        object route = Call(Type("NpV2UsbTopology"), "Inspect", path);
        Require(Number(Get(route, "HubCount")) == 0, "Unknown/external-hub topology refused (no modal override)");
        Log("route=" + Get(route, "LocationPath"));
        using (var device = (IDisposable)Call(Type("NpV2UsbDevice"), "Open", path))
        {
            object service = Call(device, "GetServiceStatus");
            Call(service, "RequireDeviceUid", uid, Call(Type("NpV2FirmwareForm"), "Uid", path));
            Require(Number(Get(service, "Mode")) == 2 && Number(Get(service, "Busy")) == 0, "Idle USB-stream service required; no APP/SD action allowed");
            object capture = Call(device, "GetCaptureStatus"), source = Call(device, "GetSourceStatus");
            long state = Number(Get(capture, "State")), sourceState = Number(Get(source, "State"));
            Require(Number(Get(capture, "Error")) == 0 && Number(Get(capture, "ProducerError")) == 0 &&
                Number(Get(capture, "SourceError")) == 0 && Number(Get(source, "Error")) == 0, "Stopped source/capture error");
            Require(completed ? state == 4 && sourceState == 11 : (state == 0 && sourceState == 0) || (state == 4 && sourceState == 11), "Fresh idle/Complete stream required");
            object caps = Call(device, "GetProfileCapabilities");
            Require((bool)Get(caps, "Valid") && (bool)Call(caps, "SupportsProbeMask", mask) && (bool)Call(caps, "SupportsRate", rate), "Requested mask/rate not supported");
            Log("fresh-stopped uid=" + uid + " capture=" + state + " source=" + sourceState + " path=" + path);
        }
    }
    static Dictionary<string, object> Json(string path)
    {
        return (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.DeserializeObject(File.ReadAllText(path));
    }
    static Dictionary<string, string> Status(string folder)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(folder, "neuropixels_np2usb_status.txt")))
        {
            int at = line.IndexOf('='); if (at > 0) result.Add(line.Substring(0, at), line.Substring(at + 1));
        }
        return result;
    }
    static long Count(Dictionary<string, string> status, string key) { return Int64.Parse(status[key], CultureInfo.InvariantCulture); }
    static void PrepareRequests(string folder, uint mask)
    {
        var requests = (IDictionary)Call(form, "NpV2RequestsForDevice", Get(form, "devicePath"));
        var apply = (HashSet<int>)Call(form, "NpV2ApplyRequestsForDevice", Get(form, "devicePath"), true);
        requests.Clear(); apply.Clear();
        for (int p = 1; p <= 3; p++) if ((mask & (1U << (p - 1))) != 0)
        {
            string json = File.ReadAllText(Path.Combine(folder, "probe" + p + ".json"));
            byte[] selection = File.ReadAllBytes(Path.Combine(folder, "probe" + p + ".n2b1"));
            Require(selection.Length == 876, "Prepared N2B1 has wrong size");
            // Actual configurator admission before UI Start; only file work.
            // Production performs this synchronous wrapper on its capture
            // thread, never under a WinForms synchronization context.
            var prepare = System.Threading.Tasks.Task.Run(delegate
            { return Call(Type("NpConfigurationDialog"), "PrepareV2Request", json, p); });
            Until(delegate { return prepare.IsCompleted; }, 30, "Configurator preparation did not complete");
            object request = prepare.GetAwaiter().GetResult();
            byte[] actual = (byte[])Get(request, "Selection");
            Require(actual.Length == selection.Length, "Prepared selection length changed");
            for (int i = 0; i < actual.Length; i++) Require(actual[i] == selection[i], "Prepared JSON/N2B1 mismatch at P" + p + " byte " + i);
            requests.Add(p, json); apply.Add(p);
            Log("prepared-request P" + p + " bytes=" + actual.Length);
        }
    }
    static Form Preview { get { return (Form)Get(form, "neuropixelsPreview"); } }
    static void RequireBusy(Thread owner)
    {
        Require(Running && Object.ReferenceEquals(Get(form, "captureThread"), owner), "Worker/UI ownership released prematurely");
        foreach (string name in new[] { "btnStart", "btnStreams", "btnDfu", "btnRefresh", "comboDrives" })
            Require(!((Control)Get(form, name)).Enabled, name + " enabled before worker completion");
        Require(Preview != null && !Preview.IsDisposed && (bool)Get(Preview, "KeepAliveWhileRecording"), "Preview owner released before completion");
    }
    static double Stop(Thread owner)
    {
        var latency = Stopwatch.StartNew(); Call(form, "btnStop_Click", null, EventArgs.Empty); latency.Stop();
        Require(latency.Elapsed.TotalMilliseconds < 250, "Stop event blocked (250 ms gate)");
        RequireBusy(owner); Require((bool)Get(form, "ce64StopRequested"), "Stop cancellation flag missing");
        Require(!((Control)Get(form, "btnStop")).Enabled, "Repeated Stop still enabled");
        Call(form, "btnStop_Click", null, EventArgs.Empty); RequireBusy(owner);
        var completion = Stopwatch.StartNew();
        Until(delegate { return !Running && Get(form, "captureThread") == null; }, 60, "Real worker/UI completion did not drain within 60 seconds");
        Until(delegate { return !owner.IsAlive; }, 1, "Completed worker did not return after its final UI post");
        Require(!(bool)Get(Preview, "KeepAliveWhileRecording"), "Completed preview still capture-owned");
        Require(((Control)Get(form, "btnStart")).Enabled && ((Control)Get(form, "btnStreams")).Enabled, "Controls not restored after completion");
        Log("stop-event-ms=" + latency.Elapsed.TotalMilliseconds.ToString("F3") + " worker-completion-ms=" + completion.Elapsed.TotalMilliseconds.ToString("F3") + " label=" + Label);
        return completion.Elapsed.TotalMilliseconds;
    }
    static void ValidateRecording(string folder, uint mask, uint rate)
    {
        var status = Status(folder);
        Require(status["complete"] == "True" && status["cancelled_before_capture"] == "False", "Actual recording not complete");
        foreach (string key in new[] { "error", "warning", "raw_writer_error", "decode_error", "preview_error" }) Require(status[key] == "none", key + " is not clear");
        foreach (string key in new[] { "raw_dropped_blocks", "decoded_dropped_lane_blocks", "final_capture_error", "final_producer_error", "final_source_error" }) Require(Count(status, key) == 0, key + " is nonzero");
        long blocks = Count(status, "verified_blocks"), rows = Count(status, "decoded_ap_rows");
        Require(blocks > 0 && rows > 0 && Count(status, "received_blocks") == blocks && Count(status, "requested_blocks") == blocks, "Incomplete block accounting");
        Require(status["final_state"] == "complete" && status["recording_mode"] == "continuous_until_stop", "Wrong terminal/mode evidence");
        Require(status["probe_mask"] == "0x" + mask.ToString("X1") && status["live_probe_mask"] == "0x" + mask.ToString("X1") &&
            status["decoded_ap_probe_mask"] == "0x" + mask.ToString("X1") && Count(status, "sample_hz") == rate && Count(status, "decoded_ap_sample_hz") == rate, "Recorded layout differs from request");
        long raw = new FileInfo(Path.Combine(folder, "neuropixels_np2usb.raw")).Length;
        Require(raw == checked(blocks * 46592L) && Count(status, "wire_bytes") == raw && Count(status, "payload_bytes") == checked(blocks * 46080L), "Persisted RAW byte accounting differs");
        var metadata = Json(Path.Combine(folder, "recording_settings.json"));
        Require((string)metadata["state"] == "complete" && (bool)metadata["recording_complete"] && metadata["failure"] == null, "Setup metadata is incomplete");
        var actual = (Dictionary<string, object>)metadata["actual"];
        Require(Number(actual["probe_mask"]) == mask && Number(actual["ap_sample_rate_hz"]) == rate, "Actual metadata layout differs");
        var identity = (Dictionary<string, object>)metadata["device"];
        Require(String.Equals((string)identity["usb_path"], (string)Get(form, "devicePath"), StringComparison.OrdinalIgnoreCase), "Metadata path differs from admitted rebind");
        string[] receipts = File.ReadAllLines(Path.Combine(folder, "requested-configuration-applied-receipts.txt"));
        int count = 0; long datRows = 0; object queue = Get(Preview, "previewQueue");
        for (int p = 1; p <= 3; p++) if ((mask & (1U << (p - 1))) != 0)
        {
            count++;
            string dat = Path.Combine((mask & (mask - 1)) == 0 ? folder : Path.Combine(folder, "probe" + p), "neuropixels_ap.dat");
            long bytes = new FileInfo(dat).Length;
            Require(bytes > 0 && bytes % 768 == 0, "DAT is not a positive whole 384-channel row stream"); datRows += bytes / 768;
            Require(Number(Call(queue, "Written", false, p)) == bytes / 768, "Actual preview PushSamples rows differ from DAT P" + p);
            var probe = Json(Path.Combine(folder, "probe_" + p + ".json"));
            Require((string)probe["configuration_state"] == "device_confirmed_requested" && (string)probe["imro_state"] == "device_confirmed_requested", "IMRO not actually confirmed before source arm");
            Require(new FileInfo(Path.Combine(folder, "probe_" + p + ".imro")).Length > 0, "Confirmed IMRO missing");
            Require(File.ReadAllText(Path.Combine(folder, "probe_" + p + ".requested.npconfig.json")) == (string)((IDictionary)Call(form, "NpV2RequestsForDevice", Get(form, "devicePath")))[p], "Requested map evidence changed");
            int matches = 0; foreach (string receipt in receipts) if (receipt.StartsWith("P" + p + ": applied; ID=", StringComparison.Ordinal) && receipt.Contains("; CRC32=") && receipt.Contains("; verification=")) { matches++; Log(receipt); }
            Require(matches == 1, "Missing/duplicated applied ID/CRC receipt P" + p);
        }
        Require(receipts.Length == count && datRows == rows && Number(Get(form, "neuropixelsApRows")) == rows, "DAT/callback/metadata row accounting differs");
        For(0.2);
        Require(Get(Preview, "renderFailure") == null, "Actual preview renderer failed");
        Require(Number(Get(Get(Preview, "sweep"), "CopiedRows")) > 0 && Number(Get(Preview, "frameVersion")) > 0, "No actual row rendered");
        lock (Get(Preview, "renderLock"))
        {
            Require(Get(Preview, "backBuffer") != null, "Rendered back buffer missing");
            using (var copy = (Bitmap)((Bitmap)Get(Preview, "backBuffer")).Clone()) copy.Save(Path.Combine(folder, "preview-backbuffer.png"), ImageFormat.Png);
        }
        using (var bitmap = new Bitmap(Preview.ClientSize.Width, Preview.ClientSize.Height))
        {
            Preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "preview-control.png"), ImageFormat.Png);
        }
        Log("recording-PASS blocks=" + blocks + " RAW-bytes=" + raw + " DAT-rows=" + datRows + " preview-display-dropped=" + Get(Preview, "DroppedPreviewRows") + " folder=" + folder);
    }
    static void Cycle(int index, uint mask, uint rate, int seconds, bool prepareStop)
    {
        Phase((prepareStop ? "prepare-stop" : "record") + index);
        string path = (string)Get(form, "devicePath"); Admit(path, TargetUid, mask, rate, false);
        Call(form, "btnStart_Click", null, EventArgs.Empty);
        Require(Running && Get(form, "captureThread") is Thread, "Actual Start handler did not launch worker");
        Thread owner = (Thread)Get(form, "captureThread"); string folder = (string)Get(form, "runFolder");
        Log("start path=" + path + " mask=" + mask + " hz=" + rate + " folder=" + folder);
        if (prepareStop)
        {
            Until(delegate { return !Running || Label.Contains("USB reads not armed yet"); }, 240, "No actual pre-arm configuration progress witnessed");
            Require(Running && Number(Get(form, "neuropixelsPackets")) == 0 && Number(Get(form, "neuropixelsApRows")) == 0 && Label.Contains("USB reads not armed yet"), "Preparation Stop did not witness pre-arm phase");
            Log("prepare-stop witnessed=" + Label); Stop(owner);
            var status = Status(folder); var metadata = Json(Path.Combine(folder, "recording_settings.json"));
            Require(status["complete"] == "False" && status["cancelled_before_capture"] == "True" && status["warning"] == "none", "Missing explicit pre-arm cancellation classification");
            foreach (string key in new[] { "received_blocks", "verified_blocks", "wire_bytes", "payload_bytes", "decoded_ap_rows", "final_capture_error", "final_producer_error", "final_source_error" }) Require(Count(status, key) == 0, "Cancelled preparation has data/fault: " + key);
            Require((string)metadata["state"] == "incomplete" && !(bool)metadata["recording_complete"] && (bool)metadata["np2_cancelled_before_capture"], "Cancellation metadata fabricated completeness");
            foreach (string file in Directory.GetFiles(folder, "*.dat", SearchOption.AllDirectories)) Require(new FileInfo(file).Length == 0, "Cancelled preparation contains DAT data");
            string raw = Path.Combine(folder, "neuropixels_np2usb.raw"); Require(!File.Exists(raw) || new FileInfo(raw).Length == 0, "Cancelled preparation contains RAW data");
            Require(Label.StartsWith("Stopped WILD-X V2 NP2 USB preparation before capture began", StringComparison.Ordinal), "Cancelled preparation did not complete informationally");
            Log("prepare-stop-lifecycle-PASS; recording remains INCOMPLETE/zero-data, not integrity qualification. folder=" + folder);
        }
        else
        {
            Until(delegate { return !Running || Number(Get(form, "neuropixelsPackets")) > 0 && Number(Get(form, "neuropixelsApRows")) > 0; }, 240, "No actual verified USB/DAT progress");
            RequireBusy(owner); Require(Number(Get(form, "neuropixelsPackets")) > 0, "Worker ended before recording progress");
            Form preview = Preview; Require(preview.Visible, "Actual renderer not visible offscreen");
            long before = Number(Get(form, "neuropixelsApRows")); preview.Hide(); For(0.1);
            Call(form, "btnScope_Click", null, EventArgs.Empty);
            Require(Object.ReferenceEquals(Preview, preview) && !preview.IsDisposed && preview.Visible, "Scope hide/reopen replaced/disposed owned preview");
            RequireBusy(owner); Log("preview-hide-reopen actual-rows=" + before);
            var capture = Stopwatch.StartNew();
            while (capture.Elapsed.TotalSeconds < seconds) { Require(Running, "Capture failed before requested UI Stop"); Pump(); }
            Stop(owner); ValidateRecording(folder, mask, rate);
            Admit((string)Get(form, "devicePath"), TargetUid, mask, rate, true);
        }
        Require(uiTicks > 0 && uiMaxGap < 1000, "UI timer starved for >=1 second");
        Log("ui-timer ticks=" + uiTicks + " max-gap-ms=" + uiMaxGap.ToString("F3", CultureInfo.InvariantCulture));
    }
    [STAThread] static int Main(string[] args)
    {
        if (args.Length != 8 && args.Length != 9 && args.Length != 11)
        {
            Console.Error.WriteLine("Usage: NpUsbUiHardwareTests.exe WILDX_console.exe exact-usb-path UID fresh-output prepared-map-folder mask rate-hz seconds [once|repeat|prepare-stop|repeat-and-prepare-stop [second-mask second-rate-hz]]"); return 2;
        }
        System.Threading.Timer watchdog = null; System.Windows.Forms.Timer uiTimer = null;
        try
        {
            string exe = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[3]), maps = Path.GetFullPath(args[4]);
            uint mask = UInt32.Parse(args[5], CultureInfo.InvariantCulture), rate = UInt32.Parse(args[6], CultureInfo.InvariantCulture);
            int seconds = Int32.Parse(args[7], CultureInfo.InvariantCulture); string scenario = args.Length >= 9 ? args[8] : "repeat";
            uint secondMask = args.Length == 11 ? UInt32.Parse(args[9], CultureInfo.InvariantCulture) : mask;
            uint secondRate = args.Length == 11 ? UInt32.Parse(args[10], CultureInfo.InvariantCulture) : rate;
            Require(args[2].Equals(TargetUid, StringComparison.OrdinalIgnoreCase), "This qualification targets the explicitly attested UID only");
            Profile(mask, rate); Profile(secondMask, secondRate);
            Require(seconds >= 1 && seconds <= 60, "Duration must be 1..60 seconds after first verified data");
            Require(scenario == "once" || scenario == "repeat" || scenario == "prepare-stop" || scenario == "repeat-and-prepare-stop", "Unknown scenario");
            Require(args.Length != 11 || scenario == "repeat" || scenario == "repeat-and-prepare-stop", "Second profile requires repeated Start scenario");
            Require(File.Exists(exe) && Directory.Exists(maps), "Candidate/prepared maps missing");
            Require(!File.Exists(output) && !Directory.Exists(output), "Fresh output required"); Directory.CreateDirectory(output);
            events = new StreamWriter(Path.Combine(output, "ui-events.log")) { AutoFlush = true };
            string directory = Path.GetDirectoryName(exe);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            { string file = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll"); return File.Exists(file) ? Assembly.LoadFrom(file) : null; };
            using (var hash = SHA256.Create()) using (var input = File.OpenRead(exe)) Log("candidate-SHA256=" + BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""));
            app = Assembly.LoadFrom(exe); uiThread = GetCurrentThreadId(); Phase("admission");
            watchdog = new System.Threading.Timer(Watchdog, null, 100, 100);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log("FAIL UI exception: " + e.Exception); Environment.Exit(2); };
            Admit(args[1], args[2], mask, rate, false);
            form = (Form)Activator.CreateInstance(Type("Form1"), true);
            form.Load += delegate { throw new IOException("Main Load/discovery is forbidden"); };
            form.Shown += delegate { throw new IOException("Main Shown is forbidden"); };
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
            IntPtr handle = form.Handle; Require(handle != IntPtr.Zero && !form.Visible, "Hidden real message-queue handle missing");
            Set(form, "selectedDeviceKind", Enum.Parse(Field(form, "selectedDeviceKind").FieldType, "NpV2"));
            Set(form, "devicePath", args[1]); Set(form, "saveFolder", output);
            Set(form, "npV2RequestedProbeMask", mask); Set(form, "npV2RequestedSampleHz", rate);
            PrepareRequests(maps, mask); Call(form, "UpdateStartEnabled");
            Call(form, "btnScope_Click", null, EventArgs.Empty);
            Require(Preview != null && Preview.Visible && !form.Visible, "Offscreen actual preview/main-hidden lifecycle failed");
            uiTimer = new System.Windows.Forms.Timer { Interval = 50 };
            uiTimer.Tick += delegate { long now = Stopwatch.GetTimestamp(); uiMaxGap = Math.Max(uiMaxGap, (now - uiLast) * 1000.0 / Stopwatch.Frequency); uiLast = now; uiTicks++; };
            uiTimer.Start();
            if (scenario != "prepare-stop") Cycle(1, mask, rate, seconds, false);
            if (scenario == "repeat" || scenario == "repeat-and-prepare-stop")
            {
                Set(form, "npV2RequestedProbeMask", secondMask); Set(form, "npV2RequestedSampleHz", secondRate);
                PrepareRequests(maps, secondMask); Cycle(2, secondMask, secondRate, seconds, false);
            }
            if (scenario == "prepare-stop" || scenario == "repeat-and-prepare-stop") Cycle(3, scenario == "prepare-stop" ? mask : secondMask, scenario == "prepare-stop" ? rate : secondRate, seconds, true);
            Require(!form.Visible, "Main form was shown");
            Log("PASS " + checks + " checks; actual UI lifecycle/USB evidence only. Offscreen render is not visual usability/electrode-mapping/analog-signal qualification."); return 0;
        }
        catch (Exception ex)
        {
            Log("FAIL " + ex + "; files retained; no automatic capture retry/reset or successful integrity claim.");
            // Request ordinary UI Stop once if the real worker still owns capture.
            // Never dispose those handles early, nor operate a second USB client.
            if (form != null && Running && !(bool)Get(form, "ce64StopRequested")) Call(form, "btnStop_Click", null, EventArgs.Empty);
            if (form != null && Get(form, "captureThread") != null)
                try { Until(delegate { return !Running && Get(form, "captureThread") == null; }, 60, "Failure cleanup still owned by worker"); }
                catch (Exception cleanup) { Log("Failure cleanup incomplete: " + cleanup.Message + "; main must inspect hardware, no forced reset."); }
            return 2;
        }
        finally
        {
            if (uiTimer != null) uiTimer.Dispose(); if (watchdog != null) watchdog.Dispose();
            if (form != null && Get(form, "captureThread") == null) { if (Preview != null && !Preview.IsDisposed) Preview.Close(); form.Dispose(); }
            if (events != null) events.Dispose();
        }
    }
}
