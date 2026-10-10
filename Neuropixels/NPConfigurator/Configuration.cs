using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NPConfigurator;

public enum ProbeProfile { Nric1384, Np1, Np2Single, Np2Multi }
public enum RangeSelectionMode { Add, Remove, ReplaceAll }
public sealed record RangeSelectionResult(int[] Channels, int ReservedSites, int ReplacedChannels);

public sealed class ChannelSetting
{
    public int Channel { get; set; }
    public int? Site { get; set; }
    public int Shank { get; set; }
    public bool Standby { get; set; }
    [JsonIgnore] public bool Sample { get => !Standby; set => Standby = !value; }
    // Retained for compatibility with existing configuration files; not a sampling control.
    public bool Export { get; set; } = true;
    public string Reference { get; set; } = "External";
    public int ApGain { get; set; } = 1000;
    public int LfpGain { get; set; } = 50;
    public bool HighPass { get; set; } = true;
}

public sealed class ProbeConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public string Numbering { get; set; } = "zero-based";
    public ProbeProfile Profile { get; set; }
    public int Connector { get; set; } = 1;
    public string ModelRevision { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string MappingId { get; set; } = "";
    public int InternalReferenceBank { get; set; }
    public string CalibrationIdentity { get; set; } = "";
    public string HardwareState { get; set; } = "NotApplied";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? OriginalImro { get; set; }
    public List<ChannelSetting> Channels { get; set; } = [];

    public static readonly int[] Gains = [50, 125, 250, 500, 1000, 1500, 2000, 3000];
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static ProbeConfiguration Create(ProbeProfile profile)
    {
        var c = new ProbeConfiguration { Profile = profile, MappingId = Catalog.MappingId(profile) };
        for (int ch = 0; ch < 384; ch++)
            c.Channels.Add(new ChannelSetting {
                Channel = ch, Site = profile == ProbeProfile.Np1 && ch == 191 ? null
                    : profile == ProbeProfile.Np2Multi ? Enumerable.Range(0, 384).Single(site => Catalog.Channel(profile, 0, site) == ch) : ch,
                Standby = profile == ProbeProfile.Np1 && ch == 191,
                Export = !(profile == ProbeProfile.Np1 && ch == 191),
                Reference = profile == ProbeProfile.Nric1384 ? "All" : "External",
                ApGain = IsNp2(profile) ? 100 : 1000,
                LfpGain = IsNp2(profile) ? 100 : 50, HighPass = !IsNp2(profile)
            });
        return c;
    }
    public static bool IsNp2(ProbeProfile p) => p is ProbeProfile.Np2Single or ProbeProfile.Np2Multi;
    [JsonIgnore] public int FixedNp2Gain => ModelRevision is "21" or "24" or "NP2000" or "NP2010" ? 80 : 100;
    [JsonIgnore] public int Shanks => Profile == ProbeProfile.Np2Multi ? 4 : 1;
    [JsonIgnore] public int SitesPerShank => Profile switch { ProbeProfile.Nric1384 => 384, ProbeProfile.Np1 => 960, _ => 1280 };
    [JsonIgnore] public string[] References => Profile switch {
        ProbeProfile.Nric1384 => ["All", "Parity"], ProbeProfile.Np1 => ["External", "Tip", "Internal"],
        ProbeProfile.Np2Multi => ["External", "Tip", "Ground", "Tip0", "Tip1", "Tip2", "Tip3"],
        _ => ["External", "Tip", "Ground"] };
    [JsonIgnore] public int[] ExportChannels => Channels.Where(c => c.Export).Select(c => c.Channel).Order().ToArray();
    [JsonIgnore] public int[] SamplingChannels => Channels.Where(c => c.Site != null && !c.Standby).Select(c => c.Channel).Order().ToArray();
    [JsonIgnore] public string Id => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson()))).ToLowerInvariant();
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public ProbeConfiguration Clone() => FromJson(ToJson());
    public static ProbeConfiguration FromJson(string json)
    {
        var c = JsonSerializer.Deserialize<ProbeConfiguration>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty configuration.");
        var errors = c.Validate();
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        c.Channels = c.Channels.OrderBy(x => x.Channel).ToList();
        return c;
    }
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Profile)) errors.Add("Unknown hardware profile.");
        if (SchemaVersion != 1 || Numbering != "zero-based") errors.Add("Unsupported schema or numbering convention.");
        if (MappingId != Catalog.MappingId(Profile)) errors.Add("Mapping identity differs from the installed vendor catalog.");
        if (Connector is < 1 or > 3) errors.Add("Connector must be 1, 2 or 3.");
        if (InternalReferenceBank is < 0 or > 2) errors.Add("Internal reference bank must be 0-2.");
        if (HardwareState != "NotApplied") errors.Add("This editor cannot verify a hardware-applied state.");
        if (ModelRevision == null || SerialNumber == null || CalibrationIdentity == null)
            errors.Add("Identity fields must be strings.");
        if (Channels == null || Channels.Count != 384 || Channels.Any(x => x == null) ||
            !Channels.Select(x => x.Channel).Order().SequenceEqual(Enumerable.Range(0, 384)))
        { errors.Add("Exactly one entry for every channel 0-383 is required."); return errors; }
        var occupied = new HashSet<(int, int)>();
        foreach (var c in Channels)
        {
            string prefix = $"Channel {c.Channel}: ";
            if (c.Shank < 0 || c.Shank >= Shanks) errors.Add(prefix + "invalid shank.");
            if (!References.Contains(c.Reference)) errors.Add(prefix + "invalid reference for this hardware.");
            if (c.Site is int site)
            {
                if (site < 0 || site >= SitesPerShank || c.Shank < 0 || c.Shank >= Shanks)
                    errors.Add(prefix + "site is out of range.");
                else if (Catalog.Channel(Profile, c.Shank, site) != c.Channel)
                    errors.Add(prefix + $"site {site} maps to another channel.");
                if (!occupied.Add((c.Shank, site))) errors.Add(prefix + "duplicate physical site.");
                if (Profile == ProbeProfile.Np1 && site is 191 or 575 or 959)
                    errors.Add(prefix + "site is reserved for internal reference.");
            }
            else if (!c.Standby) errors.Add(prefix + "an unconnected channel must be in standby.");
            if (Profile == ProbeProfile.Nric1384 && (c.Site != c.Channel || c.Shank != 0))
                errors.Add(prefix + "NRIC input wiring is fixed; use standby to disable the amplifier.");
            if (IsNp2(Profile))
            {
                if (c.ApGain != FixedNp2Gain || c.LfpGain != FixedNp2Gain || c.HighPass)
                    errors.Add(prefix + $"NP2 uses fixed gain {FixedNp2Gain} for this model and full-band acquisition.");
            }
            else if (!Gains.Contains(c.ApGain) || !Gains.Contains(c.LfpGain)) errors.Add(prefix + "invalid gain.");
        }
        return errors;
    }
    public void RequireValid()
    {
        var errors = Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Take(12)));
    }
    // Only enabled assignments occupy a sampling channel; disabled assignments may be reused.
    public bool SelectSite(int shank, int site, bool replace, out string message)
    {
        message = "";
        if (shank < 0 || shank >= Shanks || site < 0 || site >= SitesPerShank)
        { message = "Site is out of range."; return false; }
        if (Profile == ProbeProfile.Np1 && site is 191 or 575 or 959)
        { message = "This site is reserved for internal reference."; return false; }
        int ch = Catalog.Channel(Profile, shank, site);
        var c = Channels.Single(x => x.Channel == ch);
        if (c.Site != null && !c.Standby && (c.Site != site || c.Shank != shank) && !replace)
        { message = $"Conflict: site {site} on shank {shank} shares channel {ch} with enabled site {c.Site} on shank {c.Shank}. Disable the old site or allow replacement."; return false; }
        c.Site = site; c.Shank = shank; c.Standby = false; c.Export = true;
        return true;
    }
    public void ClearSites()
    {
        foreach (var c in Channels) { if (Profile != ProbeProfile.Nric1384) c.Site = null; c.Standby = true; c.Export = false; }
    }
    // Apply a highlighted physical selection atomically. Off never touches another site on the same channel.
    public RangeSelectionResult SetSiteSampling(IEnumerable<(int Shank, int Site)> sites, bool enabled)
    {
        var requested = sites.ToHashSet();
        if (requested.Any(s => s.Shank < 0 || s.Shank >= Shanks || s.Site < 0 || s.Site >= SitesPerShank))
            throw new ArgumentException("Selection is outside this probe.");
        int reserved = requested.RemoveWhere(s => Profile == ProbeProfile.Np1 && s.Site is 191 or 575 or 959);
        if (enabled)
        {
            var byChannel = new Dictionary<int, (int Shank, int Site)>();
            foreach (var site in requested)
            {
                int channel = Catalog.Channel(Profile, site.Shank, site.Site);
                if (byChannel.TryGetValue(channel, out var other))
                    throw new ArgumentException($"S{other.Shank}/site {other.Site} and S{site.Shank}/site {site.Site} share channel {channel}. Highlight only one of them to enable. Sampling unchanged.");
                byChannel.Add(channel, site);
            }
        }
        var candidate = Clone();
        var affected = new List<int>(); int replaced = 0;
        foreach (var site in requested)
        {
            int channel = Catalog.Channel(Profile, site.Shank, site.Site);
            var current = candidate.Channels[channel];
            if (enabled)
            {
                if (!current.Standby && current.Site != null && (current.Site != site.Site || current.Shank != site.Shank)) replaced++;
                if (!candidate.SelectSite(site.Shank, site.Site, true, out var error)) throw new ArgumentException(error);
            }
            else
            {
                if (current.Site != site.Site || current.Shank != site.Shank) continue;
                current.Standby = true;
            }
            affected.Add(channel);
        }
        candidate.RequireValid(); Channels = candidate.Channels;
        return new RangeSelectionResult(affected.Order().ToArray(), reserved, replaced);
    }
    // Evaluate the entire range before committing, including collisions within the requested range.
    public RangeSelectionResult EditRange(int shank, int firstSite, int count, RangeSelectionMode mode, bool replaceConflicts = false)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Unknown range action.");
        if (count < 1 || firstSite < 0 || firstSite + (long)count > SitesPerShank || shank < 0 || shank >= Shanks)
            throw new ArgumentException("Range is outside this shank.");
        var requested = new Dictionary<int, int>();
        int reserved = 0, replaced = 0;
        for (int site = firstSite; site < firstSite + count; site++)
        {
            if (Profile == ProbeProfile.Np1 && site is 191 or 575 or 959) { reserved++; continue; }
            int channel = Catalog.Channel(Profile, shank, site);
            if (mode != RangeSelectionMode.Remove && requested.TryGetValue(channel, out int other))
                throw new ArgumentException($"Sites {other} and {site} on shank {shank} share channel {channel}. Shorten or move the range; both cannot be recorded independently. Selection unchanged.");
            requested.TryAdd(channel, site);
        }
        var candidate = Clone();
        if (mode == RangeSelectionMode.ReplaceAll) candidate.ClearSites();
        var affected = new List<int>();
        if (mode == RangeSelectionMode.Remove)
        {
            foreach (var channel in candidate.Channels.Where(c => c.Shank == shank && c.Site >= firstSite && c.Site < firstSite + count))
            {
                if (Profile != ProbeProfile.Nric1384) channel.Site = null;
                channel.Standby = true; channel.Export = false; affected.Add(channel.Channel);
            }
        }
        else foreach (var (channel, site) in requested)
        {
            var old = candidate.Channels.Single(c => c.Channel == channel);
            if (old.Site != null && !old.Standby && (old.Site != site || old.Shank != shank)) replaced++;
            if (!candidate.SelectSite(shank, site, replaceConflicts, out var error))
                throw new ArgumentException(error + " Selection unchanged.");
            affected.Add(channel);
        }
        candidate.RequireValid(); Channels = candidate.Channels;
        return new RangeSelectionResult(affected.Order().ToArray(), reserved, replaced);
    }
    // Bank presets replace the entire selection. Partial banks leave other channels disconnected.
    public void SelectRange(int shank, int firstSite, int count)
    {
        EditRange(shank, firstSite, count, RangeSelectionMode.ReplaceAll);
    }
    public void Save(string path) { RequireValid(); AtomicWrite(path, ToJson()); }
    public void SelectMultiShankTip()
    {
        if (Profile != ProbeProfile.Np2Multi) throw new InvalidOperationException("The 96-site-per-shank preset requires NP2 four-shank hardware.");
        var candidate = Clone(); candidate.ClearSites();
        for (int shank = 0; shank < 4; shank++) for (int site = 0; site < 96; site++)
            if (!candidate.SelectSite(shank, site, false, out var message)) throw new InvalidOperationException(message);
        candidate.RequireValid(); Channels = candidate.Channels;
    }
    public static void AtomicWrite(string path, string contents)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, contents, new UTF8Encoding(false)); File.Move(temporary, full, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string ChannelCsv()
    {
        RequireValid();
        var text = new StringBuilder("channel,site,shank,bank,standby,sample,reference,ap_gain,lfp_gain,high_pass\n");
        foreach (var c in Channels.OrderBy(c => c.Channel))
            text.AppendLine(string.Join(",", c.Channel, c.Site?.ToString(CultureInfo.InvariantCulture) ?? "", c.Shank,
                c.Site is int s ? (s / 384).ToString(CultureInfo.InvariantCulture) : "", c.Standby ? 1 : 0,
                c.Site != null && !c.Standby ? 1 : 0, c.Reference, c.ApGain, c.LfpGain, c.HighPass ? 1 : 0));
        return text.ToString();
    }
}

public sealed class ChainTemplate
{
    public int Register { get; set; }
    public int[] Bytes { get; set; } = [];
    public Dictionary<string, int> Fields { get; set; } = [];
}
public sealed class CatalogData
{
    public int Version { get; set; }
    public string Np2Sha256 { get; set; } = "";
    public Dictionary<string, int[]> Np2Mapping { get; set; } = [];
    public Dictionary<string, ChainTemplate[]> BaseTemplates { get; set; } = [];
    public Dictionary<string, int> Np1ShankFields { get; set; } = [];
}
public static class Catalog
{
    public static readonly CatalogData Data = Load();
    private static CatalogData Load()
    {
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("NPConfigurator.catalog.json")
            ?? throw new InvalidDataException("Vendor mapping catalog is missing.");
        return JsonSerializer.Deserialize<CatalogData>(stream) ?? throw new InvalidDataException("Invalid vendor catalog.");
    }
    public static string MappingId(ProbeProfile p) => ProbeConfiguration.IsNp2(p)
        ? "imec-np2-sha256:" + Data.Np2Sha256 : p == ProbeProfile.Np1 ? "imec-np1-phaseiii-v2-zero-based-v1" : "nric1384-fixed-input-v1";
    public static int Channel(ProbeProfile p, int shank, int site)
    {
        int limit = p == ProbeProfile.Nric1384 ? 384 : p == ProbeProfile.Np1 ? 960 : 1280;
        if (!Enum.IsDefined(p) || site < 0 || site >= limit || shank < 0 || shank >= (p == ProbeProfile.Np2Multi ? 4 : 1))
            throw new ArgumentOutOfRangeException(nameof(site));
        if (!ProbeConfiguration.IsNp2(p)) return site % 384;
        return Data.Np2Mapping[p == ProbeProfile.Np2Single ? "Single-shank" : $"Multi-shank Shank {shank}"][site];
    }
}

public sealed record RegisterImage(int Register, byte[] Data);
public static class ConfigurationCompiler
{
    // Result is I2C wire byte order from the vendor workbook, not the current NRIC driver's reversed-byte representation.
    public static List<RegisterImage> Compile(ProbeConfiguration config)
    {
        config.RequireValid();
        if (ProbeConfiguration.IsNp2(config.Profile))
            throw new NotSupportedException("NP2 selection can be saved and exported as a channel map. Register export needs confirmed SRCHAIN1-4/SRCHAIN5-6 layouts and resolution of the PDF/workbook mapping discrepancy.");
        var result = new List<RegisterImage>();
        var byChannel = config.Channels.ToDictionary(c => c.Channel);
        foreach (var template in Catalog.Data.BaseTemplates[config.Profile.ToString()])
        {
            var data = template.Bytes.Select(b => checked((byte)b)).ToArray();
            foreach (var pair in template.Fields)
            {
                if (!pair.Key.StartsWith("CH", StringComparison.Ordinal) || pair.Key.Length < 6) continue;
                // Workbook labels are 1-based; the saved configuration uses channels 0-383.
                int channel = int.Parse(pair.Key.AsSpan(2, 3), CultureInfo.InvariantCulture) - 1;
                var c = byChannel[channel]; string field = pair.Key[5..];
                int? bit = field switch {
                    "Ref_All" => c.Reference == "All" ? 1 : 0, "Ref_Par" => c.Reference == "Parity" ? 1 : 0,
                    "ExtRef" => c.Reference == "External" ? 1 : 0, "TipRef" => c.Reference == "Tip" ? 1 : 0,
                    "IntRef" => c.Reference == "Internal" ? 1 : 0, "STDB" => c.Standby ? 1 : 0,
                    "FULL" => c.HighPass ? 0 : 1, _ => null };
                if (field.StartsWith("G_AP", StringComparison.Ordinal)) bit = (Array.IndexOf(ProbeConfiguration.Gains, c.ApGain) >> int.Parse(field[4..])) & 1;
                if (field.StartsWith("G_LFP", StringComparison.Ordinal)) bit = (Array.IndexOf(ProbeConfiguration.Gains, c.LfpGain) >> int.Parse(field[5..])) & 1;
                if (bit is int value) SetBit(data, pair.Value, value);
            }
            result.Add(new RegisterImage(template.Register, data));
        }
        if (config.Profile == ProbeProfile.Np1)
        {
            var shank = new byte[121]; var fields = Catalog.Data.Np1ShankFields;
            foreach (var c in config.Channels.Where(c => c.Site != null)) SetBit(shank, fields[$"Electrode {c.Site + 1}"], 1);
            foreach (string kind in new[] { "External", "Tip" })
            {
                int value = config.Channels.Any(c => !c.Standby && c.Reference == kind) ? 1 : 0;
                foreach (int parity in new[] { 1, 2 }) SetBit(shank, fields[$"{(kind == "External" ? "Ext." : "Tip")} Electrode {parity}"], value);
            }
            if (config.Channels.Any(c => !c.Standby && c.Reference == "Internal"))
                SetBit(shank, fields[$"Electrode {192 + 384 * config.InternalReferenceBank}"], 1);
            result.Insert(0, new RegisterImage(0x0E, shank));
        }
        return result;
    }
    public static void SetBit(byte[] bytes, int index, int value)
    {
        byte mask = (byte)(1 << (7 - index % 8));
        bytes[index / 8] = value != 0 ? (byte)(bytes[index / 8] | mask) : (byte)(bytes[index / 8] & ~mask);
    }
    public static void ExportBundle(ProbeConfiguration c, string directory)
    {
        c.RequireValid();
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Choose a new or empty bundle directory to avoid mixing configurations.");
        Directory.CreateDirectory(directory);
        c.Save(Path.Combine(directory, "configuration.npconfig.json"));
        ProbeConfiguration.AtomicWrite(Path.Combine(directory, "channel-map.csv"), c.ChannelCsv());
        bool np2 = ProbeConfiguration.IsNp2(c.Profile);
        var images = np2 ? [] : Compile(c);
        // Register files are review artifacts. No code path in this tool claims they were applied.
        foreach (var image in images) File.WriteAllBytes(Path.Combine(directory, $"register-{image.Register:X2}.bin"), image.Data);
        ProbeConfiguration.AtomicWrite(Path.Combine(directory, "bundle.json"), JsonSerializer.Serialize(new {
            schemaVersion = 2, configurationId = c.Id, hardwareState = "NotApplied", c.MappingId,
            registerEncoding = "I2C wire bytes, MSB first", registers = images.Select(i => new { address = i.Register, bytes = i.Data.Length,
                file = $"register-{i.Register:X2}.bin", sha256 = Convert.ToHexString(SHA256.HashData(i.Data)).ToLowerInvariant() }),
            limitation = np2 ? "NP2 register layouts and mapping discrepancy require confirmation. No register images generated."
                : "Vendor-workbook register images for review. Current NRIC driver differs in chain parity/byte representation and skips chain2. Direct firmware binding is not supported. ADC fields retain workbook defaults, not per-probe calibration.",
            requestedSamplingChannels = c.SamplingChannels
        }, ProbeConfiguration.JsonOptions));
    }
}
