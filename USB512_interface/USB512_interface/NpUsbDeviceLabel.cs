using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace USB512_interface
{
    // Presentation only. Keep the interface path/UID as the selection identity.
    internal static class NpUsbDeviceLabel
    {
        internal static string For(string name, string path, string uid)
        {
            return Format(name, uid, Location(path));
        }

        internal static string Format(string name, string uid, string location)
        {
            var match = Regex.Match(location ?? String.Empty,
                @"\APort_#([0-9]+)\.Hub_#([0-9]+)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            uint port, hub;
            string label = name;
            if (match.Success && UInt32.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
                UInt32.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out hub) && port > 0 && hub > 0)
                label += " — Hub " + hub.ToString(CultureInfo.InvariantCulture) + " / Port " + port.ToString(CultureInfo.InvariantCulture);
            return label + (String.IsNullOrWhiteSpace(uid) ? String.Empty : " — " + uid);
        }

        static string Location(string path)
        {
            string[] parts = (path ?? String.Empty).Split('#');
            if (parts.Length != 4 ||
                !Regex.IsMatch(parts[1], @"\Avid_1209&pid_b38[67]\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(parts[2], @"\A[A-Za-z0-9&_.-]{1,200}\z", RegexOptions.CultureInvariant)) return null;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\USB\" + parts[1] + "\\" + parts[2], false))
                    return key == null ? null : key.GetValue("LocationInformation") as string;
            }
            catch (UnauthorizedAccessException) { return null; }
            catch (SecurityException) { return null; }
            catch (IOException) { return null; }
        }
    }
}
