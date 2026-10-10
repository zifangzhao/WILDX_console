using System.Text.Json;
using NPConfigurator;

internal static class StandardFormatTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject, string? fixtures)
    {
        string Np1Row(int ch) => $"({ch} {(ch < 191 ? 2 : ch == 191 ? 0 : 1)} 3 {(ch % 2 == 0 ? 500 : 1000)} 250 {ch % 2})";
        string np1Text = "(0,384)\n" + string.Concat(Enumerable.Range(0, 384).Reverse().Select(Np1Row));
        var np1 = StandardFormats.FromImro("\uFEFF" + np1Text);
        check(np1.Channels[0].Site == 768 && np1.Channels[383].Site == 767 && np1.Channels[191].Site == null, "Independent NP1 fixture: mixed banks and reserved reference");
        check(np1.InternalReferenceBank == 1 && np1.Channels[0].Reference == "Internal" && np1.Channels[0].ApGain == 500 && !np1.Channels[0].HighPass && np1.Channels[1].HighPass, "NP1 gains, reference bank and per-channel filters");
        check(np1.SamplingChannels.Length == 383, "NP1 IMRO includes all 384 rows but 383 ordinary electrodes");
        reject(() => StandardFormats.FromImro(np1Text.Replace(Np1Row(0), Np1Row(1))), "Duplicate channel rejected");
        reject(() => StandardFormats.FromImro(np1Text.Replace(Np1Row(1), "")), "Missing channel rejected");
        reject(() => StandardFormats.FromImro(np1Text[..^1]), "Truncated entry rejected");
        reject(() => StandardFormats.FromImro(np1Text + "extra"), "Trailing text rejected");
        reject(() => StandardFormats.FromImro(np1Text.Replace(Np1Row(1), "(1 2 4 1000 250 1)")), "Different internal reference banks rejected");
        reject(() => StandardFormats.FromImro(np1Text.Replace(Np1Row(1), "(1 2 3 999 250 1)")), "Unsupported gain rejected");
        reject(() => StandardFormats.FromImro(np1Text.Replace(Np1Row(1), "(1 2 3 1000 250 2)")), "Nonbinary filter rejected");
        reject(() => StandardFormats.FromImro(np1Text.Replace(Np1Row(383), "(383 2 3 1000 250 1)")), "Past NP1 tip bank rejected");
        reject(() => StandardFormats.FromImro(np1Text.Replace("(0,384)", "(0,383)")), "Wrong channel count rejected");
        reject(() => StandardFormats.FromImro(np1Text.Replace("(0,384)", "(NP1100,384)")), "UHD geometry must not be misidentified as NP1");
        reject(() => StandardFormats.FromImro(new string(' ', 131073)), "Oversized table rejected");

        var single = ProbeConfiguration.Create(ProbeProfile.Np2Single);
        single.SetSiteSampling([(0, 393), (0, 900), (0, 1200)], true);
        single.Channels[1].Reference = "Tip";
        check(single.Channels[1].Site == 393, "NP2 catalog bank-one routing fixture");
        var multi = ProbeConfiguration.Create(ProbeProfile.Np2Multi); multi.SelectMultiShankTip();
        multi.SetSiteSampling([(1, 500), (2, 900), (3, 1200)], true);
        multi.Channels[0].Reference = "Tip3";

        var cases = new[] {
            ("np1", np1, "0"), ("np1_part", np1, "NP1000"),
            ("single_legacy", single, "21"), ("single_legacy_part", single, "NP2000"),
            ("single", single, "2003"), ("single_part", single, "NP2003"),
            ("multi_legacy", multi, "24"), ("multi_legacy_part", multi, "NP2010"),
            ("multi", multi, "2013"), ("multi_part", multi, "NP2013"),
            ("np1_part_1001", np1, "NP1001"), ("single_part_2004", single, "NP2004"),
            ("single_part_2005", single, "NP2005"), ("single_part_2006", single, "NP2006"),
            ("multi_part_2014", multi, "NP2014"), ("single_2004", single, "2004"), ("multi_2014", multi, "2014") };
        if (fixtures != null) Directory.CreateDirectory(fixtures);
        foreach (var (name, source, identifier) in cases)
        {
            string before = source.Id, text = StandardFormats.ToImro(source, identifier);
            var imported = StandardFormats.FromImro(text);
            string canonical = identifier switch { "2004" => "2003", "2014" => "2013", _ => identifier };
            check(source.Id == before && imported.ModelRevision == canonical && imported.Profile == source.Profile, name + ": model identity without source mutation");
            if (canonical != identifier)
            {
                var alias = StandardFormats.FromImro(text.Replace($"({canonical},384)", $"({identifier},384)"));
                check(alias.ModelRevision == identifier && StandardFormats.ToImro(alias) == text, "Older numeric alias imports and exports a canonical header");
            }
            foreach (var expected in source.Channels)
            {
                var actual = imported.Channels[expected.Channel];
                // A tip on its own shank can have either spelling in the native model.
                bool sameReference = actual.Reference == expected.Reference || actual.Reference == "Tip" && expected.Reference == $"Tip{actual.Shank}";
                int ap = ProbeConfiguration.IsNp2(imported.Profile) ? imported.FixedNp2Gain : expected.ApGain;
                int lf = ProbeConfiguration.IsNp2(imported.Profile) ? imported.FixedNp2Gain : expected.LfpGain;
                check(actual.Site == expected.Site && actual.Shank == expected.Shank && actual.Standby == expected.Standby && sameReference && actual.ApGain == ap && actual.LfpGain == lf && actual.HighPass == expected.HighPass, name + ": routing and settings round trip, channel " + expected.Channel);
            }
            check(StandardFormats.ToImro(imported.Clone()) == text, name + ": native save retains IMRO reference semantics");
            if (fixtures != null)
            {
                File.WriteAllText(Path.Combine(fixtures, name + ".imro"), text);
                File.WriteAllText(Path.Combine(fixtures, name + ".probeinterface.json"), StandardFormats.ToProbeInterface(imported));
                imported.Save(Path.Combine(fixtures, name + ".npconfig.json"));
            }
        }
        var ground = single.Clone(); ground.Channels[0].Reference = "Ground";
        var legacy = StandardFormats.FromImro(StandardFormats.ToImro(single, "21"));
        check(legacy.Channels.All(ch => ch.ApGain == 80 && ch.LfpGain == 80) && legacy.Clone().Id == legacy.Id, "Legacy NP2 fixed gain is 80 and survives native JSON");
        check(single.Channels.All(ch => ch.ApGain == 100), "Commercial NP2 retains fixed gain 100");
        check(StandardFormats.FromImro(StandardFormats.ToImro(ground)).Channels[0].Reference == "Ground", "Commercial NP2 ground reference");
        reject(() => StandardFormats.ToImro(ground, "21"), "Legacy NP2 cannot silently encode ground as tip");
        reject(() => StandardFormats.ToImro(single, "24"), "Mismatched IMRO type rejected");
        var unsupported = single.Clone(); unsupported.ModelRevision = "NP2020";
        reject(() => StandardFormats.ToImro(unsupported), "Unknown part number requires supported geometry");
        reject(() => StandardFormats.ToProbeInterface(unsupported), "Unknown part number cannot acquire generic geometry");
        string defaultSingle = StandardFormats.ToImro(ProbeConfiguration.Create(ProbeProfile.Np2Single), "21");
        reject(() => StandardFormats.FromImro(defaultSingle.Replace("(0 1 0 0)", "(0 3 0 0)")), "Joined bank masks rejected");
        reject(() => StandardFormats.FromImro(defaultSingle.Replace("(0 1 0 0)", "(0 2 0 0)")), "Bank/site mismatch rejected");
        reject(() => StandardFormats.FromImro(defaultSingle.Replace("(0 1 0 0)", "(0 1 0 1)")), "Site/channel mismatch rejected");
        reject(() => StandardFormats.FromImro(defaultSingle.Replace("(0 1 0 0)", "(0 1 2 0)")), "Unsupported on-shank NP2 reference rejected");
        reject(() => StandardFormats.FromImro(defaultSingle.Replace("(0 1 0 0)", "(-1 1 0 0)")), "Negative channel rejected");
        reject(() => StandardFormats.FromImro(defaultSingle.Replace("(0 1 0 0)", "(384 1 0 0)")), "Out-of-range channel rejected");

        foreach (var profile in new[] { ProbeProfile.Np1, ProbeProfile.Np2Single, ProbeProfile.Np2Multi })
        {
            var subset = ProbeConfiguration.Create(profile); subset.Channels[10].Standby = true;
            string before = subset.Id;
            reject(() => StandardFormats.ToImro(subset), "IMRO must not silently enable disabled channels");
            check(subset.Id == before, "Rejected IMRO export preserves selection");
            using var geometry = JsonDocument.Parse(StandardFormats.ToProbeInterface(subset));
            using var compact = JsonDocument.Parse(StandardFormats.ToProbeInterface(subset, true));
            var probe = geometry.RootElement.GetProperty("probes")[0];
            var indices = probe.GetProperty("device_channel_indices").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            check(indices.SequenceEqual(subset.SamplingChannels), "ProbeInterface preserves channel gaps in full-stream geometry");
            check(compact.RootElement.GetProperty("probes")[0].GetProperty("device_channel_indices").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(Enumerable.Range(0, subset.SamplingChannels.Length)), "Compact geometry uses sample-column indices");
            check(probe.GetProperty("contact_positions")[0][0].GetDouble() == (profile == ProbeProfile.Np1 ? 16 : 0), "Known first electrode coordinate");
            if (fixtures != null)
            {
                subset.Save(Path.Combine(fixtures, profile + "_subset.npconfig.json"));
                File.WriteAllText(Path.Combine(fixtures, profile + "_subset.probeinterface.json"), StandardFormats.ToProbeInterface(subset));
                File.WriteAllText(Path.Combine(fixtures, profile + "_compact.probeinterface.json"), StandardFormats.ToProbeInterface(subset, true));
            }
            subset.ClearSites(); reject(() => StandardFormats.ToProbeInterface(subset), "Empty geometry rejected");
        }
        var nric = ProbeConfiguration.Create(ProbeProfile.Nric1384);
        reject(() => StandardFormats.ToImro(nric), "NRIC does not pretend to be a probe");
        reject(() => StandardFormats.ToProbeInterface(nric), "No invented NRIC electrode geometry");
        if (fixtures != null)
        {
            check(StandardFormats.Load(Path.Combine(fixtures, "multi.imro")).Profile == ProbeProfile.Np2Multi, "File loader detects IMRO");
            check(StandardFormats.Load(Path.Combine(fixtures, "multi.npconfig.json")).Profile == ProbeProfile.Np2Multi, "Native loader remains compatible");
        }
    }
}
