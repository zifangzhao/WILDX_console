using System.Buffers.Binary;
using System.Text.Json;
using NPConfigurator;
if(args.Length>=3 && args[0]=="--imro-device") {
    await ImroDeviceTests.Run(args.Skip(1).ToArray());return;
}

int assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, message); }
int Bit(byte[] bytes, int i) => (bytes[i/8] >> (7-i%8)) & 1;

foreach (var profile in Enum.GetValues<ProbeProfile>())
{
    var c = ProbeConfiguration.Create(profile);
    Check(c.Validate().Count == 0, $"Default {profile}");
    Check(c.Clone().Id == c.Id, "Stable JSON identity");
    Check(!c.ToJson().Contains("\"Id\""), "No recursive computed properties");
    for (int shank = 0; shank < c.Shanks; shank++)
    {
        for (int bank = 0; bank < (c.SitesPerShank + 383)/384; bank++)
        {
            int count = Math.Min(384, c.SitesPerShank-bank*384);
            c.SelectRange(shank, bank*384, count);
            Check(c.Validate().Count == 0, $"Valid {profile} shank {shank} bank {bank}");
            Check(c.ExportChannels.Length == count - (profile == ProbeProfile.Np1 ? 1 : 0), "Partial bank/reference exclusion count");
            foreach (var channel in c.Channels.Where(x => !x.Standby))
                Check(channel.Site / 384 == bank && channel.Shank == shank, "Bank selection never leaks sites");
        }
    }
    string before = c.Id;
    Reject(() => c.SelectRange(0, c.SitesPerShank-2, 4), "Invalid range refused");
    Check(c.Id == before, "Failed preset leaves selection unchanged");
    c.ClearSites(); Check(c.Validate().Count == 0 && c.ExportChannels.Length == 0, "Clear leaves valid standby configuration");
}

foreach (var profile in Enum.GetValues<ProbeProfile>())
{
    var c = ProbeConfiguration.Create(profile); c.ClearSites();
    var added = c.EditRange(0, 10, 20, RangeSelectionMode.Add);
    Check(added.Channels.Length == 20 && c.ExportChannels.Length == 20, "Inclusive range endpoints");
    int firstChannel = Catalog.Channel(profile, 0, 10);
    c.Channels[firstChannel].ApGain = ProbeConfiguration.IsNp2(profile) ? 100 : 500;
    string before = c.Id;
    c.EditRange(0, 10, 20, RangeSelectionMode.Add);
    Check(c.Id == before, "Adding an active range never toggles it off or changes gain");
    c.EditRange(0, 100, 5, RangeSelectionMode.Add);
    Check(c.ExportChannels.Length == 25 && c.Channels[firstChannel].Site == 10, "Add preserves disjoint selections");
    c.EditRange(0, 15, 10, RangeSelectionMode.Remove);
    Check(c.ExportChannels.Length == 15 && c.Channels[firstChannel].Export, "Remove preserves sites outside the range");
    Check(c.Channels.Where(x => x.Site is >= 15 and < 25).All(x => x.Standby && !x.Export), "Removed fixed inputs are in standby");
    c.EditRange(0, 200, 10, RangeSelectionMode.ReplaceAll);
    Check(c.ExportChannels.Length == 10 && c.Channels.Where(x => !x.Standby).All(x => x.Site is >= 200 and <= 209), "Replace all is explicit");
    Check(c.Channels[firstChannel].ApGain == (ProbeConfiguration.IsNp2(profile) ? 100 : 500), "Range edits retain channel settings");
    before = c.Id;
    Reject(() => c.EditRange(0, c.SitesPerShank - 1, 2, RangeSelectionMode.Add), "Past-end range rejected");
    Reject(() => c.EditRange(0, 1, int.MaxValue, RangeSelectionMode.Remove), "Overflow range rejected");
    Reject(() => c.EditRange(c.Shanks, 0, 1, RangeSelectionMode.Add), "Unknown shank rejected");
    Reject(() => c.EditRange(0, 0, 1, (RangeSelectionMode)20), "Unknown range action rejected");
    Check(c.Id == before, "Invalid range actions preserve all settings");
    if (profile == ProbeProfile.Nric1384)
        Check(c.Channels.All(x => x.Site == x.Channel), "Range removal never disconnects fixed NRIC wiring");
    else
    {
        Reject(() => c.EditRange(0, 0, 385, RangeSelectionMode.Add, true), "Replacement cannot hide an internal range collision");
        Reject(() => c.EditRange(0, 0, 385, RangeSelectionMode.ReplaceAll), "Invalid replacement range rejected");
        Check(c.Id == before, "Internally conflicting ranges are atomic");
        c.EditRange(0, 0, c.SitesPerShank, RangeSelectionMode.Remove);
        Check(c.ExportChannels.Length == 0 && c.Validate().Count == 0, "Remove may span more than 384 physical sites");
    }
}
foreach (var profile in new[] { ProbeProfile.Np1, ProbeProfile.Np2Single, ProbeProfile.Np2Multi })
{
    var c = ProbeConfiguration.Create(profile);
    for (int s = 0; s < c.Shanks; s++)
    for (int b = 1; b * 384 < c.SitesPerShank; b++)
    {
        int first = b * 384 - 4, last = b * 384 + 6;
        c.SelectRange(s, 0, 384);
        foreach (var channel in c.Channels) channel.Standby = true;
        c.Channels[0].ApGain = ProbeConfiguration.IsNp2(profile) ? 100 : 500;
        var added = c.EditRange(s, first, last - first + 1, RangeSelectionMode.Add);
        Check(c.Channels.Where(x => !x.Standby).All(x => x.Shank == s) &&
            Enumerable.Range(first, last - first + 1).ToHashSet().SetEquals(c.Channels.Where(x => !x.Standby).Select(x => x.Site!.Value)),
            $"{profile} shank {s} boundary {b}: inactive assignments allow a cross-bank range");
        Check(added.ReplacedChannels == 0 && c.Validate().Count == 0, "Reusing inactive assignments is not an enabled-channel conflict");
        Check(c.Channels[0].ApGain == (ProbeConfiguration.IsNp2(profile) ? 100 : 500), "Reassignment preserves gain settings");
        Check(c.Clone().Id == c.Id, "Cross-bank selection survives JSON round trip");
        c.EditRange(s, first, last - first + 1, RangeSelectionMode.Remove);
        Check(c.SamplingChannels.Length == 0, "Removing a cross-bank range disables both banks");
    }
    c = ProbeConfiguration.Create(profile);
    int target = Catalog.Channel(profile, c.Shanks - 1, 384);
    c.Channels.Single(x => x.Channel == target).Standby = true;
    Check(c.SelectSite(c.Shanks - 1, 384, false, out _) && !c.Channels[target].Standby && c.Channels[target].Site == 384,
        "Single-site selection reuses a disabled channel across banks/shanks");

    c.SelectRange(0, 0, 384);
    foreach (var channel in c.Channels) channel.Standby = true;
    int occupied = Catalog.Channel(profile, 0, 390);
    c.Channels[occupied].Standby = false;
    string before = c.Id;
    Reject(() => c.EditRange(0, 380, 11, RangeSelectionMode.Add), "An enabled assignment still blocks a cross-bank range");
    Check(c.Id == before, "A late active conflict rolls back reassignment of disabled channels");
    Check(c.EditRange(0, 380, 11, RangeSelectionMode.Add, true).ReplacedChannels == 1 && c.SamplingChannels.Length == 11,
        "Explicit replacement counts only the enabled conflict");
}
foreach (var profile in Enum.GetValues<ProbeProfile>())
{
    var c = ProbeConfiguration.Create(profile); c.ClearSites();
    (int Shank, int Site)[] separate = [(0, 5), (0, 100), (0, 5)];
    int firstChannel = Catalog.Channel(profile, 0, 5);
    c.Channels[firstChannel].ApGain = ProbeConfiguration.IsNp2(profile) ? 100 : 500;
    var enabled = c.SetSiteSampling(separate, true);
    Check(enabled.Channels.Length == 2 && c.SamplingChannels.Length == 2, "Physical selection enables separate sites and deduplicates repeats");
    Check(c.Channels[firstChannel].ApGain == (ProbeConfiguration.IsNp2(profile) ? 100 : 500), "Physical sampling preserves gains");
    string before = c.Id;
    c.SetSiteSampling(separate, true);
    Check(c.Id == before, "Enabling already enabled physical sites is idempotent");
    c.SetSiteSampling(separate, false);
    Check(c.SamplingChannels.Length == 0 && c.Channels[firstChannel].Site == 5, "Physical disable retains assignments for later re-enable");
    c.SetSiteSampling(separate, true);
    Check(c.Clone().Id == c.Id, "Physical site sampling survives configuration round trip");
    before = c.Id;
    Reject(() => c.SetSiteSampling([(0, 10), (c.Shanks, 0)], true), "Invalid shank in a physical selection is rejected atomically");
    Reject(() => c.SetSiteSampling([(0, 10), (0, c.SitesPerShank)], false), "Invalid site in a physical selection is rejected atomically");
    Check(c.Id == before, "Invalid physical selection leaves the configuration unchanged");
    if (profile == ProbeProfile.Np1)
    {
        var referenceSelection = c.SetSiteSampling([(0, 191), (0, 575), (0, 959)], true);
        Check(referenceSelection.ReservedSites == 3 && referenceSelection.Channels.Length == 0 && c.Id == before, "Physical selection skips all NP1 reference sites");
    }
    if (c.SitesPerShank > 384)
    {
        c = ProbeConfiguration.Create(profile);
        int s = c.Shanks - 1, ch = Catalog.Channel(profile, s, 384);
        var old = c.Channels[ch]; var oldSite = (old.Shank, old.Site!.Value);
        before = c.Id;
        c.SetSiteSampling([(s, 384)], false);
        Check(c.Id == before, "Disabling an unassigned physical site never disables another site's shared channel");
        int countBefore = c.SamplingChannels.Length;
        var physicalReplacement = c.SetSiteSampling([(s, 384)], true);
        Check(physicalReplacement.ReplacedChannels == 1 && c.SamplingChannels.Length == countBefore && c.Channels[ch].Site == 384 && c.Channels[ch].Shank == s,
            "Explicit physical enable replaces a conflicting assignment across banks/shanks");
        before = c.Id;
        Reject(() => c.SetSiteSampling([(0, 10), oldSite, (s, 384)], true), "An internally conflicting physical selection is rejected before any assignment");
        Check(c.Id == before, "A physical selection conflict is atomic");
        c.SetSiteSampling([oldSite, (s, 384)], false);
        Check(c.SamplingChannels.Length == countBefore - 1 && c.Channels[ch].Standby, "Disabling overlapping channel mappings affects only the assigned site");
    }
}
var rangeNp1 = ProbeConfiguration.Create(ProbeProfile.Np1); rangeNp1.ClearSites();
rangeNp1.EditRange(0, 0, 10, RangeSelectionMode.Add);
string rangeBefore = rangeNp1.Id;
Reject(() => rangeNp1.EditRange(0, 380, 10, RangeSelectionMode.Add), "Late bank-crossing collision rejected");
Check(rangeNp1.Id == rangeBefore, "Late conflict does not leave an added partial range");
var replacement = rangeNp1.EditRange(0, 380, 10, RangeSelectionMode.Add, true);
Check(replacement.ReplacedChannels == 6 && rangeNp1.ExportChannels.Length == 14, "Explicit replacement preserves unrelated channels");
rangeBefore = rangeNp1.Id;
rangeNp1.EditRange(0, 0, 6, RangeSelectionMode.Remove);
Check(rangeNp1.Id == rangeBefore, "Removal does not remove the same channel's site in another bank");
var refs = rangeNp1.EditRange(0, 180, 21, RangeSelectionMode.ReplaceAll);
Check(refs.ReservedSites == 1 && refs.Channels.Length == 20 && rangeNp1.Channels[191].Site == null, "Reference sites are skipped and reported");
var rangeMulti = ProbeConfiguration.Create(ProbeProfile.Np2Multi);
rangeBefore = rangeMulti.Id;
rangeMulti.EditRange(1, 288, 1, RangeSelectionMode.Remove);
Check(rangeMulti.Id == rangeBefore, "Removal does not remove a mapped channel on another shank");
Reject(() => rangeMulti.EditRange(1, 288, 1, RangeSelectionMode.Add), "Cross-shank range conflict rejected");
Check(rangeMulti.Id == rangeBefore, "Cross-shank range conflict is atomic");
Check(rangeMulti.EditRange(1, 288, 1, RangeSelectionMode.Add, true).ReplacedChannels == 1, "Cross-shank replacement is explicit");
{
    var c = ProbeConfiguration.Create(ProbeProfile.Np2Single);
    string before = c.Id;
    Reject(() => c.EditRange(0, 2, 384, RangeSelectionMode.ReplaceAll), "Scrambled bank-boundary range conflict rejected below capacity");
    Check(c.Id == before, "Scrambled mapping conflict preserves prior selection");
}

foreach (var table in Catalog.Data.Np2Mapping.Values)
{
    Check(table.Length == 1280, "Full NP2 table");
    for (int bank = 0; bank < 4; bank++)
    {
        var channels = table.Skip(bank*384).Take(384).ToArray();
        Check(channels.All(x => x is >= 0 and < 384) && channels.Distinct().Count() == channels.Length, "NP2 bank bijection");
    }
}
Check(Catalog.Channel(ProbeProfile.Np2Single, 0, 393) == 1, "Vendor odd bank1 scrambling");
Check(Catalog.Channel(ProbeProfile.Np2Single, 0, 785) == 1, "Vendor odd bank2 scrambling");
Check(Catalog.Channel(ProbeProfile.Np2Single, 0, 1177) == 1, "Vendor odd bank3 scrambling");
Check(Catalog.Channel(ProbeProfile.Np2Single, 0, 386) == 14, "PDF even example");
var multi = ProbeConfiguration.Create(ProbeProfile.Np2Multi); string unchanged = multi.Id;
Check(!multi.SelectSite(1, 288, false, out var conflict) && conflict.Contains("channel 0"), "Cross-shank conflict reported");
Check(multi.Id == unchanged, "Conflict is non-destructive");
Check(multi.SelectSite(1, 288, true, out _) && multi.Validate().Count == 0, "Explicit replacement");
Check(multi.Channels[0].Site == 288 && multi.Channels[0].Shank == 1, "Replacement routes expected channel");
multi.SelectMultiShankTip();
Check(multi.ExportChannels.Length == 384 && multi.Validate().Count == 0, "Balanced multi-shank preset is conflict-free");
for(int s = 0; s < 4; s++) Check(multi.Channels.Count(c => c.Shank == s && !c.Standby && c.Site < 96) == 96, "96 tip sites per shank");
var np1 = ProbeConfiguration.Create(ProbeProfile.Np1);
Reject(np1.SelectMultiShankTip, "Balanced preset rejects other hardware");
foreach (int site in new[] { 191, 575, 959 }) Check(!np1.SelectSite(0, site, true, out _), "Reserved NP1 site");
np1.SelectRange(0, 384, 384);
var image = ConfigurationCompiler.Compile(np1).Single(x => x.Register == 0x0E).Data;
Check(image.Length == 121, "NP1 shank length");
var fields = Catalog.Data.Np1ShankFields;
Check(Bit(image, fields["Electrode 384"]) == 0 && Bit(image, fields["Electrode 385"]) == 1 && Bit(image, fields["Electrode 768"]) == 1, "Bank1 explicit zero-to-one based conversion");
Check(Bit(image, fields["Electrode 576"]) == 0 && Bit(image, fields["Ext. Electrode 1"]) == 1 && Bit(image, fields["Ext. Electrode 2"]) == 1, "External reference routing");
foreach (int bank in new[] { 0, 1, 2 })
{
    np1.InternalReferenceBank = bank;
    foreach (var c in np1.Channels) c.Reference = "Internal";
    var bits = ConfigurationCompiler.Compile(np1).Single(x => x.Register == 14).Data;
    for (int b = 0; b < 3; b++) Check(Bit(bits, fields[$"Electrode {192+b*384}"]) == (bank == b ? 1 : 0), "Single shared internal reference");
    Check(Bit(bits, fields["Ext. Electrode 1"]) == 0, "Unused external reference cleared");
}
var nric = ProbeConfiguration.Create(ProbeProfile.Nric1384);
var sampleSelection = nric.Clone();
foreach (var channel in sampleSelection.Channels) channel.Export = false;
Check(sampleSelection.SamplingChannels.Length == 384, "Legacy export flags do not control sampling");
sampleSelection.Channels[0].Sample = false;
Check(sampleSelection.Channels[0].Standby && !sampleSelection.SamplingChannels.Contains(0), "Sample off disables the amplifier in the requested configuration");
sampleSelection.Channels[0].Sample = true;
Check(!sampleSelection.Channels[0].Standby && sampleSelection.SamplingChannels.Contains(0), "Sample on enables the amplifier in the requested configuration");
Check(!sampleSelection.ToJson().Contains("\"Sample\"") && sampleSelection.Clone().SamplingChannels.Length == 384, "Sampling UI preserves configuration schema compatibility");
Check(sampleSelection.ChannelCsv().Split('\n')[0].Contains(",sample,") && !sampleSelection.ChannelCsv().Split('\n')[0].Contains(",export,"), "Channel map describes sampling selection");
var defaults = ConfigurationCompiler.Compile(nric);
foreach (var t in Catalog.Data.BaseTemplates["Nric1384"])
    Check(defaults.Single(x => x.Register == t.Register).Data.SequenceEqual(t.Bytes.Select(x => (byte)x)), "NRIC baseline matches vendor wire bytes");
nric.Channels[0].ApGain = 3000; nric.Channels[0].LfpGain = 125; nric.Channels[0].Standby = true;
nric.Channels[0].HighPass = false; nric.Channels[0].Reference = "Parity";
var compiled = ConfigurationCompiler.Compile(nric);
var ct = Catalog.Data.BaseTemplates["Nric1384"][0];
var wire = compiled.Single(x => x.Register == ct.Register).Data;
Check(Bit(wire, ct.Fields["CH001STDB"]) == 1 && Bit(wire, ct.Fields["CH001Ref_Par"]) == 1, "Asymmetric channel0 fields");
Check(Bit(wire, ct.Fields["CH003STDB"]) == 0, "Neighbour channel preserved");
Check(wire.AsSpan(264).SequenceEqual(defaults.Single(x => x.Register == ct.Register).Data.AsSpan(264)), "ADC fields preserved");
Check(compiled[1].Data.SequenceEqual(defaults[1].Data), "Other parity chain preserved");
Reject(() => ConfigurationCompiler.Compile(multi), "No invented NP2 register layout");
Reject(() => ProbeConfiguration.FromJson(nric.ToJson().Replace("zero-based", "one-based")), "Numbering mismatch");
Reject(() => ProbeConfiguration.FromJson(nric.ToJson().Replace("NotApplied", "Applied")), "Cannot import unverified applied status");
Reject(() => ProbeConfiguration.FromJson(nric.ToJson().Replace("nric1384-fixed-input-v1", "unknown")), "Mapping mismatch");
var malformed = nric.Clone(); malformed.Channels.RemoveAt(0); Reject(malformed.RequireValid, "Missing channel");
malformed = nric.Clone(); malformed.Channels[1].Channel = 0; Reject(malformed.RequireValid, "Duplicate channel");
malformed = nric.Clone(); malformed.Channels[0].Site = 384; Reject(malformed.RequireValid, "Invalid input");
malformed = nric.Clone(); malformed.Channels[0].ApGain = 100; Reject(malformed.RequireValid, "Invalid gain");
malformed = nric.Clone(); malformed.Channels[0].Reference = "Tip"; Reject(malformed.RequireValid, "Wrong hardware reference");

string temp = Path.Combine(Path.GetTempPath(), "npconfig-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string input = Path.Combine(temp, "full.dat"), output = Path.Combine(temp, "subset.dat");
    var random = new Random(405); byte[] source = new byte[2051*768]; random.NextBytes(source); File.WriteAllBytes(input, source);
    int[] selection = [383, 0, 191, 17];
    Check(SelectedChannelExport.Copy(input, output, selection) == 2051, "Subset frame count across buffer boundaries");
    byte[] actual = File.ReadAllBytes(output);
    for (int n = 0; n < 2051; n++) for (int ch = 0; ch < selection.Length; ch++)
        Check(BinaryPrimitives.ReadInt16LittleEndian(actual.AsSpan((n*selection.Length+ch)*2)) ==
              BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(n*768+selection[ch]*2)), "Sample alignment and channel order");
    Reject(() => SelectedChannelExport.Copy(input, input, selection), "Source cannot be overwritten");
    Reject(() => SelectedChannelExport.Copy(input, output, selection), "Output cannot be overwritten");
    Reject(() => SelectedChannelExport.Copy(input, output, [0,0]), "Duplicate export channel");
    File.WriteAllBytes(Path.Combine(temp, "truncated.dat"), [0,1,2]);
    Reject(() => SelectedChannelExport.Copy(Path.Combine(temp,"truncated.dat"), Path.Combine(temp,"bad.dat"), [0]), "Truncated frames rejected");
    foreach (var p in Enum.GetValues<ProbeProfile>())
    {
        string folder = Path.Combine(temp, p.ToString()); var config = ProbeConfiguration.Create(p);
        ConfigurationCompiler.ExportBundle(config, folder);
        Check(ProbeConfiguration.FromJson(File.ReadAllText(Path.Combine(folder,"configuration.npconfig.json"))).Id == config.Id, "Bundle roundtrip");
        Check(File.ReadAllLines(Path.Combine(folder,"channel-map.csv")).Length == 385, "Complete channel CSV");
        Check(Directory.GetFiles(folder,"*.bin").Length == (ProbeConfiguration.IsNp2(p) ? 0 : p == ProbeProfile.Np1 ? 3 : 2), "Correct register image count");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "bundle.json"))))
        {
            Check(manifest.RootElement.GetProperty("requestedSamplingChannels").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(config.SamplingChannels), "Bundle identifies requested sampling channels");
            Check(manifest.RootElement.GetProperty("hardwareState").GetString() == "NotApplied" && !manifest.RootElement.TryGetProperty("selectedOutputChannels", out _), "Bundle does not imply applied sampling or separate output selection");
        }
        Reject(() => ConfigurationCompiler.ExportBundle(config,folder), "Never mix bundle generations");
    }
    string capture = Path.Combine(temp,"capture");
    foreach (string band in new[] { "AP", "LFP" })
    {
        string folder = Path.Combine(capture,"continuous","Neuropixels-"+band); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"continuous.dat"),source);
    }
    var subsetConfig = ProbeConfiguration.Create(ProbeProfile.Nric1384);
    foreach(var channel in subsetConfig.Channels) channel.Export = selection.Contains(channel.Channel);
    SelectedChannelExport.ExportCapture(capture,subsetConfig);
    using (var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture,"selected-channels","metadata.json"))))
    {
        Check(meta.RootElement.GetProperty("hardwareConfigurationState").GetString() == "NotVerified", "No false hardware claim in recording metadata");
        Check(meta.RootElement.GetProperty("physicalElectrodeMapping").ValueKind == JsonValueKind.Null, "No unverified electrode routing in actual output");
        Check(meta.RootElement.GetProperty("outputIndexToAcquisitionChannel").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(selection.Order()), "Recorded subset index map");
    }
    string failed = Path.Combine(temp,"failed-capture");
    Directory.CreateDirectory(failed);
    Reject(() => SelectedChannelExport.ExportCapture(failed,subsetConfig), "Missing decoded input rejected");
    Check(!Directory.EnumerateFileSystemEntries(failed).Any(), "Failed subset export leaves no completed or partial output");
    if (args.Length == 1)
    {
        Directory.CreateDirectory(args[0]);
        foreach (var p in Enum.GetValues<ProbeProfile>()) ProbeConfiguration.Create(p).Save(Path.Combine(args[0],p+".npconfig.json"));
    }
}
finally { Directory.Delete(temp,true); }
StandardFormatTests.Run(Check, Reject, args.Length == 1 ? args[0] : null);
Console.WriteLine($"PASS: {assertions} assertions; all profiles, bank boundaries, conflicts, reference/gain packing, invalid imports, and sample-exact subset export.");
