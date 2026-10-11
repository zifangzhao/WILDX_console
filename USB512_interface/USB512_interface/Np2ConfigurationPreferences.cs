using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // Stores only host requests and an intent to stage them.  Firmware receipts remain per-recording evidence.
    internal static class Np2ConfigurationPreferences
    {
        internal sealed class Device
        {
            internal readonly Dictionary<int, string> Requests = new Dictionary<int, string>();
            internal readonly HashSet<int> Apply = new HashSet<int>();
        }

        private const int Version = 1, MaxBytes = 4 * 1024 * 1024, MaxRequestBytes = 1024 * 1024;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = MaxBytes };

        internal static Dictionary<string, Device> Load(string stored, Func<string, int, string> normalize)
        {
            var result = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(stored) || stored.Length > MaxBytes || normalize == null) return result;
            try
            {
                var root = Json.Deserialize<Dictionary<string, object>>(stored); object version, devices;
                if (root == null || !root.TryGetValue("version", out version) || Convert.ToInt32(version) != Version ||
                    !root.TryGetValue("devices", out devices)) return result;
                var serializedDevices = devices as Dictionary<string, object>; if (serializedDevices == null) return result;
                foreach (var item in serializedDevices)
                {
                    if (String.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 512) continue;
                    var record = item.Value as Dictionary<string, object>; object rawRequests;
                    if (record == null || !record.TryGetValue("requests", out rawRequests)) continue;
                    var serializedRequests = rawRequests as Dictionary<string, object>; if (serializedRequests == null) continue;
                    var device = new Device();
                    foreach (var request in serializedRequests)
                    {
                        int port; string raw = request.Value as string;
                        if (!Int32.TryParse(request.Key, out port) || port < 1 || port > 3 || String.IsNullOrWhiteSpace(raw) || raw.Length > MaxRequestBytes) continue;
                        try { device.Requests.Add(port, normalize(raw, port)); } catch (Exception) { }
                    }
                    object rawApply;
                    if (record.TryGetValue("apply", out rawApply))
                    {
                        var values = rawApply as IEnumerable;
                        if (values != null) foreach (object value in values)
                        {
                            int port;
                            try { port = Convert.ToInt32(value); } catch (Exception) { continue; }
                            if (device.Requests.ContainsKey(port)) device.Apply.Add(port);
                        }
                    }
                    if (device.Requests.Count != 0 && !result.ContainsKey(item.Key)) result.Add(item.Key, device);
                }
            }
            catch (Exception) { }
            return result;
        }

        internal static string Save(IDictionary<string, Device> devices)
        {
            var serializedDevices = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in devices)
            {
                if (String.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 512 || item.Value == null || item.Value.Requests.Count == 0) continue;
                var requests = new Dictionary<string, object>();
                foreach (var request in item.Value.Requests)
                    if (request.Key >= 1 && request.Key <= 3 && !String.IsNullOrWhiteSpace(request.Value) && request.Value.Length <= MaxRequestBytes)
                        requests.Add(request.Key.ToString(), request.Value);
                if (requests.Count == 0) continue;
                var apply = new List<object>(); foreach (int port in item.Value.Apply) if (requests.ContainsKey(port.ToString())) apply.Add(port);
                serializedDevices.Add(item.Key, new Dictionary<string, object> { { "requests", requests }, { "apply", apply.ToArray() } });
            }
            string saved = Json.Serialize(new Dictionary<string, object> { { "version", Version }, { "devices", serializedDevices } });
            if (saved.Length > MaxBytes) throw new InvalidOperationException("Retained NP2 configuration preferences exceed the local settings limit.");
            return saved;
        }
    }
}
