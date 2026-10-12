using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.CSharp;

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
    static string MethodSource(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) throw new Exception("Missing production method: " + signature);
        int open = source.IndexOf('{', start), depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        throw new Exception("Unclosed production method: " + signature);
    }
    static void UpdaterContracts(string path)
    {
        string source = File.ReadAllText(path);
        string route = MethodSource(source, "private static void RequestRomDfu()");
        int uid = route.IndexOf("if (selectedUid is null)", StringComparison.Ordinal), target = route.IndexOf("FindPath(", StringComparison.Ordinal);
        Need(uid >= 0 && target > uid &&
            route.Contains("throw new ArgumentException"), "ROM DFU must require explicit UID before target selection.");
        Need(route.Contains("var bootPath = FindPath(BootHardwareId);") && route.Contains("if (bootPath is null)"),
            "Existing B387 must enter DFU directly, without an intermediate USB-service hop.");
        Need(Regex.Matches(route, "RequestUnifiedMode\\(").Count == 1 && route.Contains("UnifiedMode(\"--recovery\")") &&
            route.Contains("WaitForPath(BootHardwareId") && !route.Contains("Opcode.UsbService"),
            "B386 may request recovery once, then wait only for selected-UID B387.");
        Need(source.Contains("selectedUid is null || string.Equals(McuUid(path),selectedUid,StringComparison.OrdinalIgnoreCase)"),
            "DFU must retain the exact UID-pinned target selector.");
        int info = route.IndexOf("Transfer(recovery, Opcode.Info", StringComparison.Ordinal);
        int okay = route.IndexOf("EnsureOk(info", StringComparison.Ordinal);
        int admission = route.IndexOf("RequireDfuRecovery(info.Arg0)", StringComparison.Ordinal);
        int command = route.IndexOf("Transfer(recovery, Opcode.RomDfu", StringComparison.Ordinal);
        Need(info >= 0 && okay > info && admission > okay && command > admission &&
            Regex.Matches(route, "Opcode.RomDfu").Count == 1 && source.Contains("RomDfu = 11,"),
            "A successful idle/capable INFO must precede exactly one opcode11 request.");
        Need(!route.Contains("Opcode.Begin") && !route.Contains("Opcode.Data") && !route.Contains("Opcode.Commit") &&
            !route.Contains("Opcode.Abort") && !route.Contains("MscControl") && !route.Contains("RequestRecovery") &&
            route.Contains("Thread.Sleep(150)") && route.Contains("USB disconnect alone does not confirm ROM DFU"),
            "ROM DFU must not flash, abort updates, fall back, retry or claim enumeration from disconnect.");
        int dfuRoute = source.IndexOf("args[0] == \"--dfu\"", StringComparison.Ordinal);
        Need(dfuRoute >= 0 && source.IndexOf("args[0] == \"--usb\" ||", StringComparison.Ordinal) > dfuRoute,
            "DFU must bypass the legacy MSC mode-control route.");
        Need(!source.Contains("case \"--dfu\"") && !source.Contains("\"--dfu\" => 5"),
            "Resident USB unified-mode admission must not add a mode5 fallback.");
        // Compile only the exact pure production admission method, never the
        // updater's Main, WinUSB discovery or hardware command implementation.
        Match abi = Regex.Match(source, @"private const uint LoaderAbiVersion\s*=\s*\d+;");
        Need(abi.Success, "Loader ABI constant missing.");
        string policy = "using System; using System.IO; public static class DfuPolicy { " + abi.Value +
            MethodSource(source, "private static void RequireDfuRecovery(uint info)") + " }";
        using (var compiler = new CSharpCodeProvider())
        {
            var options = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false };
            options.ReferencedAssemblies.Add("System.dll");
            CompilerResults result = compiler.CompileAssemblyFromSource(options, policy);
            if (result.Errors.HasErrors) throw new Exception("Pure production policy compilation failed: " + result.Errors[0]);
            MethodInfo gate = result.CompiledAssembly.GetType("DfuPolicy", true).GetMethod("RequireDfuRecovery", All);
            foreach (uint version in new uint[] { 0, 3, 4, 5, 65535 })
                for (uint flags = 0; flags < 16; flags++)
                {
                    bool admitted = true;
                    try { gate.Invoke(null, new object[] { (version << 16) | flags }); }
                    catch (TargetInvocationException e) { if (!(e.InnerException is IOException)) throw; admitted = false; }
                    Need(admitted == (version == 4 && (flags & 2U) == 0 && (flags & 8U) != 0),
                        "ROM DFU admission changed for ABI " + version + ", INFO flags " + flags + ".");
                }
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Pass the checked WILDX executable and canonical FirmwareUpdater/Program.cs.");
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
            using (var form = (Form)Activator.CreateInstance(app.GetType("USB512_interface.NpV2FirmwareForm", true), All,
                null, new object[] { @"\\?\usb#vid_1209&pid_b387#001B0023434250062039314E#{guid}" }, null))
            {
                bool loaded = false, shown = false;
                form.Load += delegate { loaded = true; }; form.Shown += delegate { shown = true; };
                Need(Find(form, "ROM DFU…") is Button, "V2 Firmware and USB Modes hides ROM DFU.");
                Need(Find(form, "Recovery loader") is Button, "Existing protected Recovery loader action was replaced.");
                Need(!loaded && !shown && !form.IsHandleCreated, "The V2 DFU fixture started USB discovery.");
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
            UpdaterContracts(args[1]);
            Console.WriteLine("PASS " + checks + " actual-assembly BLE/USB DFU visibility, original BLE frame, UID-pinned recovery route and production ABI/update/capability admission; no hardware.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
