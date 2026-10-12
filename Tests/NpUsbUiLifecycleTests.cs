using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Actual-assembly UI ownership check. The main form is never shown/loaded;
// only a modeled capture worker runs, without USB discovery or device calls.
internal static class NpUsbUiLifecycleTests
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly app;
    static int checks;
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static FieldInfo Field(object target, string name)
    {
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, All | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    static object Get(object target, string name) { return Field(target, name).GetValue(target); }
    static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }
    static object Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, All).Invoke(target, args); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    static bool PreviewOwned(Form preview)
    {
        return (bool)preview.GetType().GetProperty("KeepAliveWhileRecording", All).GetValue(preview, null);
    }
    static void RequireOwned(Form form, Form preview, Thread worker, bool alive)
    {
        Check((bool)Get(form, "running"), "V2 Stop released the busy gate before completion");
        Check(Object.ReferenceEquals(Get(form, "captureThread"), worker), "V2 Stop released the worker owner");
        Check(worker.IsAlive == alive, "Modeled worker lifecycle differs");
        Check(Object.ReferenceEquals(Get(form, "neuropixelsPreview"), preview) && !preview.IsDisposed && PreviewOwned(preview),
            "V2 Stop/close released the preview owner");
        Call(form, "UpdateStartEnabled");
        foreach (string name in new[] { "btnStart", "btnStreams", "btnDfu", "btnRefresh", "comboDrives" })
            Check(!((Control)Get(form, name)).Enabled, name + " released during V2 drain");
        Check(!((Control)Get(form, "btnStop")).Enabled, "Repeated Stop was re-enabled during drain");
        Call(form, "btnStart_Click", null, EventArgs.Empty);
        Call(form, "btnStreams_Click", null, EventArgs.Empty);
        Check(Object.ReferenceEquals(Get(form, "captureThread"), worker) && Get(form, "runFolder") == null,
            "A draining Start/settings handler began another recording");
        var tabs = (TabControl)Get(form, "workspaceTabs");
        tabs.SelectedIndex = 1; Check(tabs.SelectedIndex == 0, "BLE workspace entered during V2 drain");
        tabs.SelectedIndex = 3; Check(tabs.SelectedIndex == 0, "Advanced workspace entered during V2 drain");
    }
    static void Scenario(bool preparing)
    {
        using (var form = (Form)Activator.CreateInstance(app.GetType("USB512_interface.Form1", true), true))
        using (var preview = (Form)Activator.CreateInstance(app.GetType("USB512_interface.NeuropixelsPreviewForm", true),
            All, null, new object[] { 20000, 3, true, 12 }, null))
        using (var release = new ManualResetEvent(false))
        using (var entered = new ManualResetEvent(false))
        {
            bool loaded = false, shown = false, completed = false;
            Exception workerError = null;
            form.Load += delegate { loaded = true; };
            form.Shown += delegate { shown = true; };
            form.ShowInTaskbar = false;
            // Handle creation, not Show, lets the real PostToUi queue completion.
            IntPtr handle = form.Handle;
            var tabs = (TabControl)Get(form, "workspaceTabs");
            IntPtr tabHandle = tabs.Handle;
            Check(handle != IntPtr.Zero && tabHandle != IntPtr.Zero, "Offscreen UI queue/selection handles missing");
            Set(form, "selectedDeviceKind", Enum.Parse(Field(form, "selectedDeviceKind").FieldType, "NpV2"));
            Set(form, "devicePath", "offline-owned-NP2");
            Set(form, "saveFolder", Path.GetTempPath());
            Set(form, "running", true);
            Set(form, "ce64StopRequested", false);
            Set(form, "neuropixelsPreview", preview);
            preview.GetType().GetProperty("KeepAliveWhileRecording", All).SetValue(preview, true, null);
            ((Control)Get(form, "btnRefresh")).Enabled = false;
            ((Control)Get(form, "comboDrives")).Enabled = false;
            ((Control)Get(form, "lblRecInfo")).Text = preparing ? "Preparing NP2" : "Recording NP2";
            var worker = new Thread(delegate()
            {
                try
                {
                    entered.Set();
                    if (!release.WaitOne(10000)) throw new TimeoutException("Fixture worker release missing");
                    Call(form, "PostToUi", (Action)delegate
                    {
                        Call(form, "FinishCaptureOnUi", preparing ? "Stopped during preparation" : "Stopped");
                        completed = true;
                    });
                }
                catch (Exception e) { workerError = e; }
            }) { IsBackground = true };
            Set(form, "captureThread", worker);
            worker.Start();
            try
            {
                Check(entered.WaitOne(1000), "Fixture worker did not start");
                var elapsed = Stopwatch.StartNew();
                Call(form, "StopCapture");
                elapsed.Stop();
                Check(elapsed.ElapsedMilliseconds < 1000, "V2 Stop blocked on the capture-thread join");
                Check((bool)Get(form, "ce64StopRequested"), "V2 Stop did not request owner cancellation");
                RequireOwned(form, preview, worker, true);
                Call(form, "StopCapture");
                RequireOwned(form, preview, worker, true);
                var close = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Call(form, "OnFormClosing", close);
                Check(close.Cancel && !form.IsDisposed, "Close disposed a live V2 capture owner");
                RequireOwned(form, preview, worker, true);

                release.Set();
                Check(worker.Join(1000) && workerError == null, "Modeled worker failed to queue completion");
                // The worker has exited, but its completion callback still owns
                // the form until the UI message queue actually dispatches it.
                Check(!completed, "Completion ran outside the UI queue");
                close = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Call(form, "OnFormClosing", close);
                Check(close.Cancel && !form.IsDisposed, "Close raced the queued completion callback");
                RequireOwned(form, preview, worker, false);
                var deadline = Stopwatch.StartNew();
                while (!completed && deadline.ElapsedMilliseconds < 1000) Application.DoEvents();
                Check(completed && Get(form, "captureThread") == null && !(bool)Get(form, "running"), "Completion did not release V2 ownership");
                Check(!preview.IsDisposed && !PreviewOwned(preview), "Completion disposed or retained the preview incorrectly");
                Check(((Control)Get(form, "btnStart")).Enabled && ((Control)Get(form, "btnStreams")).Enabled,
                    "Completion did not restore Start/settings");
                Check(!((Control)Get(form, "btnStop")).Enabled, "Completion left Stop enabled");
                tabs.SelectedIndex = 3; Check(tabs.SelectedIndex == 3, "Completion did not release the workspace");
                close = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Call(form, "OnFormClosing", close);
                Check(!close.Cancel, "Idle close still refused after completion");
                Check(!loaded && !shown, "Fixture invoked main-form Load/Shown");
            }
            finally { release.Set(); worker.Join(1000); }
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass the actual WILDX_console.exe only.");
            string path = Path.GetFullPath(args[0]), directory = Path.GetDirectoryName(path);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string dependency = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
            };
            app = Assembly.LoadFrom(path);
            Application.EnableVisualStyles();
            Scenario(false); Scenario(true);
            Console.WriteLine("PASS " + checks + " actual-assembly V2 USB UI stop/preparing/repeated-stop/close/completion ownership checks; modeled worker only, no main Load/Shown or hardware.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
