using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Actual assembly, offscreen controls and modeled status only: no scan,
// connection, command dispatch, main-form Load or hardware access.
internal static class NpDfuVisibilityTests
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static void Need(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static Control Find(Control parent, string text)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Text == text) return child;
            Control found = Find(child, text); if (found != null) return found;
        }
        return null;
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass the checked WILDX executable.");
            string path = Path.GetFullPath(args[0]), directory = Path.GetDirectoryName(path);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string dependency = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
            };
            Assembly app = Assembly.LoadFrom(path);
            Application.EnableVisualStyles();
            using (var form = (Form)Activator.CreateInstance(app.GetType("USB512_interface.Form1", true), true))
            {
                bool loaded = false, shown = false;
                form.Load += delegate { loaded = true; }; form.Shown += delegate { shown = true; };
                var workspace = (Control)form.GetType().GetField("bleWorkspace", All).GetValue(form);
                Control dfu = Find(workspace, "ROM DFU"), loader = Find(workspace, "Firmware loader");
                Need(dfu is Button, "Normal BLE workspace hides the existing ROM DFU command.");
                Need(loader is Button, "Normal BLE workspace hides protected firmware-loader entry.");
                Need(!dfu.Enabled && !loader.Enabled, "Disconnected mode switches must remain disabled.");
                Need(!loaded && !shown, "The fixture started main-form discovery.");
            }
            Type control = app.GetType("CE32_console.NpV2SdControl", true);
            var request = (byte[])control.GetMethod("ModeRequest", All).Invoke(null, new object[] { 91U, 3U });
            Need(request.Length == 64 && BitConverter.ToUInt32(request, 8) == 5U &&
                BitConverter.ToUInt32(request, 12) == 91U && BitConverter.ToUInt32(request, 16) == 3U,
                "ROM DFU must retain the original V2 opcode 5 / destination 3 frame.");
            Type status = app.GetType("CE32_console.NpSdStatus", true);
            MethodInfo allowed = control.GetMethod("CanSwitchMode", All);
            foreach (uint state in new uint[] { 0, 1, 2, 3, 4, 5, 6, 7, 255 })
            {
                object s = Activator.CreateInstance(status, true);
                status.GetField("State", All).SetValue(s, state);
                status.GetField("Flags", All).SetValue(s, 8192U);
                bool stopped = state == 0 || state == 1 || state == 5 || state == 7;
                Need((bool)allowed.Invoke(null, new object[] { s }) == stopped, "Mode-switch state admission changed.");
                status.GetField("Fault", All).SetValue(s, 1U);
                Need(!(bool)allowed.Invoke(null, new object[] { s }), "Faulted mode switch was admitted.");
                status.GetField("Fault", All).SetValue(s, 0U);
                status.GetField("Flags", All).SetValue(s, 8192U | 8U);
                Need(!(bool)allowed.Invoke(null, new object[] { s }), "Pending mode switch was admitted.");
                status.GetField("Flags", All).SetValue(s, 0U);
                Need(!(bool)allowed.Invoke(null, new object[] { s }), "Missing mode capability was invented.");
            }
            Console.WriteLine("PASS " + checks + " actual-assembly DFU visibility, original command frame and stopped/fault/pending/capability gates; no hardware.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
