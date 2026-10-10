using System;
using System.Collections.Generic;
using System.Management;

namespace USB512_interface
{
    internal static class Ce64MscDiscovery
    {
        // WMI maps the storage product/inquiry name to its PhysicalDrive path.
        // This is intentionally stricter than accepting every USB removable disk.
        internal static IList<Ce64MscDeviceInfo> Enumerate()
        {
            var found = new List<Ce64MscDeviceInfo>();
            try
            {
                using (var search = new ManagementObjectSearcher("SELECT DeviceID,Model,PNPDeviceID,SerialNumber,InterfaceType FROM Win32_DiskDrive"))
                using (var disks = search.Get())
                {
                    foreach (ManagementObject disk in disks)
                    {
                        string path = Get(disk, "DeviceID");
                        string model = Get(disk, "Model");
                        string pnp = Get(disk, "PNPDeviceID");
                        bool usb = Contains(Get(disk, "InterfaceType"), "USB") || Contains(pnp, "USBSTOR");
                    bool currentProduct = Contains(model, Ce64MscProtocol.PrimaryProductName) ||
                        Contains(model, Ce64MscProtocol.PrimaryInquiryProduct) ||
                        (Contains(pnp, Ce64MscProtocol.PrimaryPnpVendorToken) && HasBlankNormalizedProduct(pnp));
                    bool ce64Wild64Fallback = Contains(pnp, Ce64MscProtocol.Ce64Wild64PnpIdentity);
                    bool ce128Product = Contains(model, Ce64MscProtocol.Ce128ProductName) ||
                        Contains(model, Ce64MscProtocol.Ce128InquiryProduct) ||
                        Contains(pnp, Ce64MscProtocol.Ce128PnpIdentity) ||
                        (Contains(pnp, Ce64MscProtocol.Ce128PnpVendorToken) && HasBlankNormalizedProduct(pnp));
                    bool legacyProduct = Contains(model, Ce64MscProtocol.LegacyProductName) ||
                        Contains(model, Ce64MscProtocol.LegacyInquiryProduct) ||
                        Contains(pnp, Ce64MscProtocol.LegacyPnpProductToken);
                    bool product = currentProduct || ce64Wild64Fallback || ce128Product || legacyProduct;
                        if (usb && product && !String.IsNullOrWhiteSpace(path))
                            found.Add(new Ce64MscDeviceInfo(path, pnp, model, Get(disk, "SerialNumber")));
                    }
                }
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            return found;
        }
        private static string Get(ManagementBaseObject o, string property) { object value = o[property]; return value == null ? String.Empty : Convert.ToString(value).Trim(); }
        private static bool Contains(string value, string token) { return !String.IsNullOrEmpty(value) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0; }
        private static bool HasBlankNormalizedProduct(string pnp)
        {
            const string prefix = "PROD_";
            int start = pnp.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;
            start += prefix.Length;
            int end = pnp.IndexOf('&', start);
            string value = end < 0 ? pnp.Substring(start) : pnp.Substring(start, end - start);
            // USBSTOR commonly converts inquiry padding spaces to underscores.
            return String.IsNullOrWhiteSpace(value.Replace('_', ' '));
        }
    }
    internal sealed class Ce64MscDeviceInfo
    {
        internal Ce64MscDeviceInfo(string path, string pnp, string model, string serial) { PhysicalDrivePath = path; PnpDeviceId = pnp; Model = model; SerialNumber = serial; }
        internal string PhysicalDrivePath { get; private set; } internal string PnpDeviceId { get; private set; }
        internal string Model { get; private set; } internal string SerialNumber { get; private set; }
        public override string ToString() { return Model + " — " + PhysicalDrivePath; }
    }
}
