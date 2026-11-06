using System;
using System.Reflection;

static class NpUsbDeviceLabelTests
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static void Need(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Set(object target, string field, object value) { target.GetType().GetField(field, All).SetValue(target, value); }
    static int Main(string[] args)
    {
        try
        {
            var app = Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));
            var labels = app.GetType("USB512_interface.NpUsbDeviceLabel", true);
            var format = labels.GetMethod("Format", All);
            var labelFor = labels.GetMethod("For", All);
            Func<string, string, string, string> label = (name, uid, location) => (string)format.Invoke(null, new object[] { name, uid, location });
            const string uid = "FFFFFFFFFFFFFFFFFFFFFF01", uid2 = "FFFFFFFFFFFFFFFFFFFFFF02";
            const string name = "WILD-X V1 USB";
            Need(label(name, uid, "Port_#0006.Hub_#0001") == name + " — Hub 1 / Port 6 — " + uid, "Windows port/hub formatting");
            Need(label(name, uid, "port_#0003.hub_#0004") == name + " — Hub 4 / Port 3 — " + uid, "Case/leading-zero formatting");
            Need(label(name, uid, "Port_#0006.Hub_#0001") != label(name, uid, "Port_#0003.Hub_#0001"), "Identical devices on different ports look identical");
            Need(label(name, uid, "Port_#0006.Hub_#0001") != label(name, uid, "Port_#0006.Hub_#0002"), "Different hubs look identical");
            Need(label(name, uid, "Port_#0006.Hub_#0001") != label(name, uid2, "Port_#0006.Hub_#0001"), "UID distinction lost");
            foreach (string bad in new[] { null, "", "unknown", "Port_#0.Hub_#1", "Port_#1.Hub_#0", "Port_#4294967296.Hub_#1", "xPort_#1.Hub_#2", "Port_#1.Hub_#2extra" })
                Need(label(name, uid, bad) == name + " — " + uid, "Unavailable/invalid port invented: " + bad);
            Need(label(name, null, "Port_#3.Hub_#2") == name + " — Hub 2 / Port 3", "Missing UID breaks port display");
            Need((string)labelFor.Invoke(null, new object[] { name, null, uid }) == name + " — " + uid, "Disconnected target fallback");

            const string root = @"\\?\usb#vid_1209&pid_b386#", tail = "#{no-device-control}";
            var source = app.GetType("CE32_console.NpDownloadForm", true).GetMethod("UsbSourceLabel", All);
            string v1Path = root + "npv1-" + uid + tail, v2Path = root + uid2 + tail;
            Need((string)source.Invoke(null, new object[] { v1Path }) == name + " — " + uid, "V1 download label/UID");
            Need((string)source.Invoke(null, new object[] { v2Path }) == "WILD-X V2 USB — " + uid2, "V2 download label/UID");

            foreach (bool v2 in new[] { false, true })
            {
                var type = v2 ? app.GetType("USB512_interface.NpFirmwareForm", true).GetNestedType("V2Target", All) : app.GetType("USB512_interface.NpV1Target", true);
                object target = Activator.CreateInstance(type, true);
                string path = v2 ? v2Path : v1Path, id = v2 ? uid2 : uid;
                Set(target, "Path", path); Set(target, "Uid", id);
                Need(target.ToString() == (v2 ? "WILD-X V2 USB" : name) + " — " + id + " — USB service", "Firmware target label");
                Need((string)type.GetField("Path", All).GetValue(target) == path && (string)type.GetField("Uid", All).GetValue(target) == id, "Display changed target identity");
                Set(target, v2 ? "Boot" : "Recovery", true);
                Need(target.ToString().EndsWith(" — recovery"), "Recovery state lost");
                Set(target, "Path", null);
                Need(target.ToString().EndsWith(" — reconnect / DFU") && target.ToString().Contains(id), "Disconnected UID/state lost");
            }
            byte[] body = app.GetType("USB512_interface.Form1", true).GetMethod("EnumerateDeviceItems", All).GetMethodBody().GetILAsByteArray();
            bool wired = false;
            for (int i = 0; i + 4 < body.Length; i++) if (body[i] == 0x28 && BitConverter.ToInt32(body, i + 1) == labelFor.MetadataToken) wired = true;
            Need(wired, "Acquisition selector does not use the shared port label");
            Console.WriteLine("PASS " + checks + " USB port labels, device distinction, fallback, real selector bindings and unchanged selection identities; no device controls.");
            if (args.Length > 1 && args[1] == "--windows-metadata")
            {
                var enumerate = app.GetType("USB512_interface.XeNpWinUsbDevice", true).GetMethod("EnumerateDevicePaths", All);
                foreach (string path in (System.Collections.IEnumerable)enumerate.Invoke(null, null))
                    Console.WriteLine("Windows present-interface label: " + source.Invoke(null, new object[] { path }));
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
