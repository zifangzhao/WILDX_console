using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NPConfigurator;

/// <summary>Interchange formats; the native JSON remains the complete editable document.</summary>
public static class StandardFormats
{
    private sealed record ImroModel(int Type, ProbeProfile Profile);

    // Only models with the geometry/routing implemented by this editor. Do not
    // infer an NP1 profile from the six-column layout of UHD/NHP/opto tables.
    private static ImroModel Model(string identifier) => identifier switch
    {
        "0" or "NP1000" or "NP1001" => new(0, ProbeProfile.Np1),
        "21" or "NP2000" => new(21, ProbeProfile.Np2Single),
        "2003" or "2004" or "NP2003" or "NP2004" or "NP2005" or "NP2006" => new(2003, ProbeProfile.Np2Single),
        "24" or "NP2010" => new(24, ProbeProfile.Np2Multi),
        "2013" or "2014" or "NP2013" or "NP2014" => new(2013, ProbeProfile.Np2Multi),
        _ => throw new NotSupportedException($"IMRO probe '{identifier}' is not supported. Choose NP1 (0), NP2 single (21/2003), or NP2 four-shank (24/2013).")
    };

    private static string DefaultIdentifier(ProbeConfiguration c) => c.Profile switch
    {
        ProbeProfile.Np1 => "0", ProbeProfile.Np2Single => "2003", ProbeProfile.Np2Multi => "2013",
        _ => throw new NotSupportedException("NRIC is a bare amplifier with custom input wiring; it has no standard Neuropixels IMRO or probe geometry. Save native JSON or channel-map CSV.")
    };

    private static string ExportIdentifier(ProbeConfiguration c, string? identifier)
    {
        if (identifier == null)
        {
            identifier = DefaultIdentifier(c);
            // Import stores the original IMRO model in the existing identity
            // field, preserving old/new reference semantics through native JSON.
            if (!string.IsNullOrWhiteSpace(c.ModelRevision))
            {
                try { Model(c.ModelRevision); identifier = c.ModelRevision; }
                catch (NotSupportedException)
                {
                    if (c.ModelRevision.StartsWith("NP", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Set Model / revision to an exact supported part number before IMRO export, or choose an explicit IMRO type in the API.");
                }
            }
        }
        if (Model(identifier).Profile != c.Profile)
            throw new InvalidDataException("The IMRO model does not match the selected probe profile.");
        // Some older writers emitted part-number suffixes as numeric types.
        // Current SpikeGLX/ProbeInterface readers expect the canonical type.
        return identifier switch { "2004" => "2003", "2014" => "2013", _ => identifier };
    }

    public static ProbeConfiguration Load(string path)
    {
        if (!Path.GetExtension(path).Equals(".imro", StringComparison.OrdinalIgnoreCase))
            return ProbeConfiguration.FromJson(File.ReadAllText(path));
        var configuration = FromImro(File.ReadAllText(path));
        configuration.OriginalImro = File.ReadAllBytes(path);
        return configuration;
    }

    // Archive exact imported bytes only while their acquisition settings still
    // match the current request. Edited requests use the existing serializer.
    public static byte[] RecordingImro(ProbeConfiguration configuration, out string origin)
    {
        string generated = ToImro(configuration);
        if (configuration.OriginalImro is { Length: > 0 and <= 131072 } original)
        {
            using var reader = new StreamReader(new MemoryStream(original), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            if (ToImro(FromImro(reader.ReadToEnd())) == generated)
            {
                origin = "exact_imported_imro";
                return (byte[])original.Clone();
            }
        }
        origin = "generated_from_configuration";
        return Encoding.UTF8.GetBytes(generated);
    }

    public static ProbeConfiguration FromImro(string text)
    {
        if (text.Length > 128 * 1024) throw new InvalidDataException("IMRO file is too large for a 384-channel probe.");
        text = text.Trim().TrimStart('\uFEFF');
        var matches = Regex.Matches(text, @"\(([^()]*)\)");
        int end = 0;
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(text[end..match.Index])) throw new InvalidDataException("Unexpected text outside an IMRO entry.");
            end = match.Index + match.Length;
        }
        if (matches.Count != 385 || !string.IsNullOrWhiteSpace(text[end..]))
            throw new InvalidDataException("IMRO requires a header and exactly 384 complete channel entries.");
        string[] header = matches[0].Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray();
        if (header.Length != 2 || header[1] != "384") throw new InvalidDataException("IMRO header must specify 384 channels.");
        var model = Model(header[0]);
        var result = ProbeConfiguration.Create(model.Profile);
        result.ModelRevision = header[0];
        if (ProbeConfiguration.IsNp2(model.Profile))
            foreach (var channel in result.Channels) channel.ApGain = channel.LfpGain = result.FixedNp2Gain;
        var seen = new HashSet<int>(); int? referenceBank = null;
        foreach (Match match in matches.Cast<Match>().Skip(1))
        {
            string[] fields = match.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int expected = model.Type == 0 ? 6 : model.Profile == ProbeProfile.Np2Single ? 4 : 5;
            if (fields.Length != expected) throw new InvalidDataException($"IMRO type {model.Type} requires {expected} values per channel.");
            int[] v = fields.Select(ParseInteger).ToArray();
            if (v[0] is < 0 or >= 384 || !seen.Add(v[0])) throw new InvalidDataException("IMRO channel IDs must be unique and in 0–383.");
            var channel = result.Channels[v[0]];
            if (model.Type == 0)
            {
                if (v[1] is < 0 or > 2 || v[0] + 384 * v[1] >= 960)
                    throw new InvalidDataException($"Channel {v[0]}: invalid NP1 bank/site.");
                if (v[2] is < 0 or > 4 || v[5] is < 0 or > 1)
                    throw new InvalidDataException($"Channel {v[0]}: invalid NP1 reference or filter.");
                channel.Reference = v[2] == 0 ? "External" : v[2] == 1 ? "Tip" : "Internal";
                if (v[2] >= 2)
                {
                    int selectedBank = v[2] - 2;
                    if (referenceBank != null && referenceBank != selectedBank)
                        throw new NotSupportedException("This editor supports one shared NP1 internal-reference bank per probe.");
                    referenceBank = selectedBank;
                }
                channel.ApGain = v[3]; channel.LfpGain = v[4]; channel.HighPass = v[5] == 1;
                // IMRO always has a row for the reserved reference channel.
                // Its routing bank is irrelevant to ordinary electrode sampling.
                channel.Site = v[0] == 191 ? null : v[0] + 384 * v[1];
                channel.Standby = v[0] == 191;
            }
            else
            {
                bool multi = model.Profile == ProbeProfile.Np2Multi;
                int shank = multi ? v[1] : 0, bank = multi ? v[2] : -1;
                int reference = multi ? v[3] : v[2], site = v[^1];
                if (shank < 0 || shank >= result.Shanks || site is < 0 or >= 1280)
                    throw new InvalidDataException($"Channel {v[0]}: invalid NP2 shank/site.");
                if (!multi && v[1] is not (1 or 2 or 4 or 8))
                    throw new NotSupportedException("NP2 IMRO must select exactly one bank per channel; joined-bank masks cannot be represented by this selector.");
                if (multi ? bank != site / 384 : v[1] != (1 << (site / 384)))
                    throw new InvalidDataException($"Channel {v[0]}: NP2 bank and electrode disagree.");
                if (Catalog.Channel(model.Profile, shank, site) != v[0])
                    throw new InvalidDataException($"Channel {v[0]}: site {site} on shank {shank} conflicts with the installed Imec mapping. Routing was not remapped.");
                channel.Site = site; channel.Shank = shank; channel.Standby = false;
                channel.Reference = DecodeReference(model.Type, shank, reference);
            }
            channel.Export = !channel.Standby;
        }
        result.InternalReferenceBank = referenceBank ?? 0;
        result.RequireValid();
        return result;
    }

    private static int ParseInteger(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n
            : throw new InvalidDataException($"Invalid IMRO integer '{value}'.");

    private static string DecodeReference(int type, int shank, int reference)
    {
        if (reference == 0) return "External";
        if ((type is 2003 or 2013) && reference == 1) return "Ground";
        if (type == 21 && reference == 1 || type == 2003 && reference == 2) return "Tip";
        int firstTip = type == 24 ? 1 : 2;
        if ((type is 24 or 2013) && reference >= firstTip && reference < firstTip + 4)
        {
            int tip = reference - firstTip;
            return tip == shank ? "Tip" : $"Tip{tip}";
        }
        throw new NotSupportedException($"Reference {reference} in IMRO type {type} is not represented by this editor (NP2 on-shank references are unsupported).");
    }

    private static int EncodeReference(ProbeConfiguration c, ChannelSetting channel, int type)
    {
        if (channel.Reference == "External") return 0;
        if (type == 0) return channel.Reference == "Tip" ? 1 : c.InternalReferenceBank + 2;
        if (channel.Reference == "Ground")
        {
            if (type is 21 or 24) throw new NotSupportedException("Legacy NP2 IMRO types 21/24 cannot encode Ground. Use commercial type 2003/2013 or choose another reference.");
            return 1;
        }
        if (type is 21 or 2003) return type == 21 ? 1 : 2;
        int tip = channel.Reference == "Tip" ? channel.Shank : int.Parse(channel.Reference.AsSpan(3), CultureInfo.InvariantCulture);
        return (type == 24 ? 1 : 2) + tip;
    }

    public static string ToImro(ProbeConfiguration c, string? identifier = null)
    {
        c.RequireValid(); identifier = ExportIdentifier(c, identifier);
        int type = Model(identifier).Type;
        var inactive = c.Channels.Where(ch => !(c.Profile == ProbeProfile.Np1 && ch.Channel == 191) && (ch.Site == null || ch.Standby)).ToArray();
        if (inactive.Length != 0)
            throw new NotSupportedException($"IMRO cannot store {inactive.Length} disabled/unassigned channels. Save .npconfig.json to preserve sampling choices, or export ProbeInterface geometry for the selected electrodes. Enable every ordinary channel before exporting IMRO.");
        var text = new StringBuilder($"({identifier},384)");
        foreach (var ch in c.Channels.OrderBy(ch => ch.Channel))
        {
            int bank = ch.Site is int site ? site / 384 : c.InternalReferenceBank;
            int reference = EncodeReference(c, ch, type);
            if (type == 0) text.Append(CultureInfo.InvariantCulture, $"({ch.Channel} {bank} {reference} {ch.ApGain} {ch.LfpGain} {(ch.HighPass ? 1 : 0)})");
            else if (c.Profile == ProbeProfile.Np2Single) text.Append(CultureInfo.InvariantCulture, $"({ch.Channel} {1 << bank} {reference} {ch.Site})");
            else text.Append(CultureInfo.InvariantCulture, $"({ch.Channel} {ch.Shank} {bank} {reference} {ch.Site})");
        }
        return text.Append('\n').ToString();
    }

    /// <summary>Only enabled contacts, sorted by hardware channel. No sync/LFP duplicate contacts.</summary>
    public static string ToProbeInterface(ProbeConfiguration c, bool selectedChannelData = false)
    {
        c.RequireValid(); string identifier = ExportIdentifier(c, null);
        var channels = c.Channels.Where(ch => ch.Site != null && !ch.Standby).OrderBy(ch => ch.Channel).ToArray();
        if (channels.Length == 0) throw new InvalidOperationException("Enable electrodes before exporting probe geometry.");
        bool np1 = c.Profile == ProbeProfile.Np1, multi = c.Profile == ProbeProfile.Np2Multi;
        bool staggered = np1 || identifier == "NP2006";
        string part = identifier switch { "0" => "NP1000", "21" => "NP2000", "24" => "NP2010", "2003" => "NP2003", "2013" => "NP2013", _ => identifier };
        var probe = new
        {
            ndim = 2, si_units = "um",
            annotations = new { name = c.Profile.ToString(), manufacturer = "imec", model_name = part, model_revision = c.ModelRevision,
                serial_number = c.SerialNumber, configuration_id = c.Id, mapping_id = c.MappingId,
                hardware_state = c.HardwareState, numbering = "zero-based",
                device_channel_layout = selectedChannelData ? "selected channels in ascending hardware-channel order" : "384-channel AP or wideband stream; hardware channel indices",
                coordinate_origin = "relative to lowest electrode row; probe-local coordinates, not brain coordinates" },
            contact_positions = channels.Select(ch => {
                int site = ch.Site!.Value, row = site / 2;
                return new[] { (double)(ch.Shank * 250 + site % 2 * 32 + (staggered && row % 2 == 0 ? 16 : 0)), (double)(row * (np1 ? 20 : 15)) };
            }).ToArray(),
            contact_plane_axes = channels.Select(_ => new[] { new[] { 1, 0 }, new[] { 0, 1 } }).ToArray(),
            contact_shapes = channels.Select(_ => "square").ToArray(),
            contact_shape_params = channels.Select(_ => new { width = 12 }).ToArray(),
            contact_ids = channels.Select(ch => multi ? $"s{ch.Shank}e{ch.Site}" : $"e{ch.Site}").ToArray(),
            shank_ids = channels.Select(ch => ch.Shank.ToString(CultureInfo.InvariantCulture)).ToArray(),
            device_channel_indices = channels.Select((ch, i) => selectedChannelData ? i : ch.Channel).ToArray(),
            contact_annotations = new { hardware_channel = channels.Select(ch => ch.Channel).ToArray(),
                electrode = channels.Select(ch => ch.Site!.Value).ToArray(),
                references = channels.Select(ch => ch.Reference).ToArray(),
                ap_gains = channels.Select(ch => ch.ApGain).ToArray(),
                // NP2 has one full-band path, not a second LFP amplifier.
                lf_gains = channels.Select(ch => np1 ? ch.LfpGain : 1).ToArray(),
                ap_hp_filters = channels.Select(ch => ch.HighPass).ToArray() }
        };
        return JsonSerializer.Serialize(new { specification = "probeinterface", version = "0.2.0", probes = new[] { probe } }, new JsonSerializerOptions { WriteIndented = true });
    }
}
