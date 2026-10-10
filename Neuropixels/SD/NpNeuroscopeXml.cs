using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace USB512_interface
{
    // Metadata only: never read, rewrite or reorder sample data. Run at session
    // boundaries, not in USB, decoder, preview or SD-transfer callbacks.
    public static class NpNeuroscopeXml
    {
        const int Channels = 384;
        static readonly Regex DatName = new Regex(
            @"^(?:(neuropixels)_(ap|lfp)|(?:np|neuropixels_probe)([123])_(ap|lfp))\.dat(?:\.partial)?$",
            RegexOptions.CultureInvariant);

        sealed class Contact
        {
            internal int Channel, Site;
            internal bool Skip;
        }

        static List<Contact> Contacts(int port, IDictionary<int, byte[]> selections, out bool physical)
        {
            byte[] b;
            physical = selections != null && selections.TryGetValue(port, out b) &&
                b != null && b.Length == 1576 && b[0] == 'N' && b[1] == 'P' &&
                b[2] == 'B' && b[3] == '1' && b[4] == 2 && b[5] == port - 1;
            // Version 2 is NP1. NRIC custom wiring and NP2 are not inferred.
            var contacts = new List<Contact>(Channels);
            if (physical)
            {
                b = selections[port];
                for (int channel = 0; channel < Channels; channel++)
                {
                    int at = 40 + channel * 4, site = b[at] | b[at + 1] << 8;
                    bool skip = site == 65535 || (b[at + 2] & 128) != 0;
                    if (site != 65535 && (site >= 960 || site % Channels != channel ||
                        (!skip && (site == 191 || site == 575 || site == 959))))
                    {
                        physical = false;
                        contacts.Clear();
                        break;
                    }
                    contacts.Add(new Contact { Channel = channel, Site = site, Skip = skip });
                }
            }
            if (!physical)
                for (int channel = 0; channel < Channels; channel++)
                    contacts.Add(new Contact { Channel = channel, Site = channel });
            else
                contacts.Sort((a, c) => a.Site != c.Site ? a.Site.CompareTo(c.Site) : a.Channel.CompareTo(c.Channel));
            return contacts;
        }

        public static List<string> WriteFolder(string folder, int mask, bool merged, int apHz,
            double? lfpHz, IDictionary<int, byte[]> selections)
        {
            if (mask < 1 || mask > 7 || apHz <= 0) throw new ArgumentOutOfRangeException("mask");
            var written = new List<string>();
            foreach (string path in Directory.GetFiles(folder, "*.dat*"))
            {
                string name = Path.GetFileName(path);
                Match match = DatName.Match(name);
                if (!match.Success) continue; // camera, analogin, timestamps etc. are not ephys.
                bool partial = name.EndsWith(".partial", StringComparison.Ordinal);
                string datName = partial ? name.Substring(0, name.Length - 8) : name;
                if (partial && File.Exists(Path.Combine(folder, datName))) continue;
                bool lfp = match.Groups[2].Value == "lfp" || match.Groups[4].Value == "lfp";
                if (lfp && !lfpHz.HasValue) continue;
                int fileMask = mask;
                if (match.Groups[3].Success)
                    fileMask = mask & (1 << (Int32.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) - 1));
                else if (!merged && Count(mask) != 1)
                    continue; // multi-probe per-probe files belong in their own directories.
                if (fileMask == 0) continue;
                string xmlName = Path.ChangeExtension(datName, ".xml");
                Write(Path.Combine(folder, xmlName), datName, partial, fileMask,
                    lfp ? lfpHz.Value : apHz, lfpHz, selections);
                written.Add(xmlName);
            }
            return written;
        }

        static int Count(int mask) { int count = 0; for (; mask != 0; mask >>= 1) count += mask & 1; return count; }
        static void Element(XmlWriter xml, string name, object value)
        {
            xml.WriteElementString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        static void Write(string path, string datName, bool partial, int mask, double rate,
            double? lfpHz, IDictionary<int, byte[]> selections)
        {
            string temporary = path + ".tmp";
            try
            {
                using (var xml = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                {
                    xml.WriteStartDocument();
                    xml.WriteStartElement("parameters");
                    xml.WriteAttributeString("version", "1.0");
                    xml.WriteAttributeString("creator", "WILDX");
                    xml.WriteComment("Data: " + datName + (partial ? ".partial; INCOMPLETE output" : "; see recording status for completeness"));
                    xml.WriteComment("Channel IDs are zero-based DAT columns. Only the display order changes; DAT bytes are unchanged.");
                    xml.WriteComment("Samples are signed int16 ADC counts, not calibrated microvolts. Voltage range and amplification are intentionally unspecified; set calibrated values in NeuroScope Properties before voltage measurements.");
                    xml.WriteStartElement("acquisitionSystem");
                    Element(xml, "nBits", 16); Element(xml, "nChannels", Count(mask) * Channels);
                    Element(xml, "samplingRate", rate.ToString("R", CultureInfo.InvariantCulture));
                    Element(xml, "offset", 0);
                    xml.WriteEndElement();
                    if (lfpHz.HasValue)
                    {
                        xml.WriteStartElement("fieldPotentials");
                        Element(xml, "lfpSamplingRate", lfpHz.Value.ToString("R", CultureInfo.InvariantCulture));
                        xml.WriteEndElement();
                    }
                    xml.WriteStartElement("anatomicalDescription");
                    xml.WriteStartElement("channelGroups");
                    int offset = 0;
                    for (int port = 1; port <= 3; port++) if ((mask & (1 << (port - 1))) != 0)
                    {
                        bool physical;
                        List<Contact> contacts = Contacts(port, selections, out physical);
                        xml.WriteComment("Probe " + port + ": " + (physical ?
                            "applied NP1 selection; ascending electrode depth then lateral position, relative to lowest electrode row" :
                            "physical geometry unavailable; hardware channel order, no inferred electrode positions"));
                        xml.WriteStartElement("group");
                        foreach (Contact contact in contacts)
                        {
                            if (physical && contact.Site != 65535)
                            {
                                int row = contact.Site / 2;
                                xml.WriteComment("electrode=" + contact.Site + "; x_um=" + (contact.Site % 2 * 32 + (row % 2 == 0 ? 16 : 0)) + "; y_um=" + row * 20);
                            }
                            xml.WriteStartElement("channel");
                            xml.WriteAttributeString("skip", contact.Skip ? "1" : "0");
                            xml.WriteString((offset + contact.Channel).ToString(CultureInfo.InvariantCulture));
                            xml.WriteEndElement();
                        }
                        xml.WriteEndElement();
                        offset += Channels;
                    }
                    xml.WriteEndElement(); xml.WriteEndElement();
                    xml.WriteStartElement("spikeDetection"); xml.WriteEndElement();
                    xml.WriteEndElement(); xml.WriteEndDocument();
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
