using NPConfigurator;

internal static class Np2DeviceSelectionTests
{
    public static void Run(string[] paths)
    {
        string? output=null;
        if(paths.Length>=2 && paths[0]=="--output") {output=paths[1];Directory.CreateDirectory(output);paths=paths.Skip(2).ToArray();}
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        void Reject(ProbeConfiguration c, string message)
        {
            bool rejected = false;
            try { Np2DeviceSelection.Compile(c); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        int Bit(byte[] b, int shank, int bit) => (b[40 + shank * 161 + bit / 8] >> (7 - bit % 8)) & 1;
        void Verify(ProbeConfiguration c, byte[] bytes)
        {
            Check(bytes.Length == 876 && bytes.AsSpan(0, 4).SequenceEqual("N2B1"u8), "N2B1 shape");
            Check(bytes[4] == 4 && bytes[5] == c.Connector - 1 && bytes[6] == 0 && bytes[7] == 0, "Profile/physical port/reserved");
            Check(bytes.AsSpan(8, 32).SequenceEqual(Convert.FromHexString(c.Id)), "Normalized JSON SHA256 identity");
            var expectedBits = new HashSet<(int Shank, int Bit)>();
            int? tip = null;
            foreach (var channel in c.Channels)
            {
                int site = channel.Site!.Value;
                // Independent integer-index oracle, checked for every selected site.
                expectedBits.Add((channel.Shank, (site & 1) == 0 ? 645 + site / 2 : 642 - site / 2));
                int nibble;
                if (channel.Reference == "Ground") nibble = 1;
                else if (channel.Reference == "External")
                {
                    nibble = 4;
                    expectedBits.Add((channel.Shank, 2));
                    expectedBits.Add((channel.Shank, 1285));
                }
                else
                {
                    nibble = 2;
                    tip = channel.Reference == "Tip" ? channel.Shank : channel.Reference[3] - '0';
                }
                // Walk ascending channels from the end of each 96-byte chain.
                int offset = 684 + (channel.Channel & 1) * 96 + 95 - channel.Channel / 4;
                int shift = channel.Channel % 4 >= 2 ? 4 : 0;
                Check(((bytes[offset] >> shift) & 15) == nibble, "All 384 base reference nibbles");
            }
            if (tip is int shank)
            {
                expectedBits.Add((shank, 643)); expectedBits.Add((shank, 644));
            }
            for (int s = 0; s < 4; s++) for (int bit = 0; bit < 1288; bit++)
                Check(Bit(bytes, s, bit) == (expectedBits.Contains((s, bit)) ? 1 : 0), "All shank electrode/reference/dummy bits");
            Check(c.Channels.Select(x => (x.Shank, x.Site)).Distinct().Count() == 384, "384 unique active physical sites");
            var recovered=Np2DeviceSelection.FromPayload(bytes);
            Check(recovered.HardwareState=="NotApplied" && recovered.Connector==c.Connector,"Recovered staged request has no applied claim");
            var restored=Np2DeviceSelection.Compile(recovered);
            Check(restored.AsSpan(40).SequenceEqual(bytes.AsSpan(40)),"Every saved routing/reference bit round trips");
            string imro=StandardFormats.ToImro(recovered);
            var imported=StandardFormats.FromImro(imro);imported.Connector=recovered.Connector;
            Check(Np2DeviceSelection.Compile(imported).AsSpan(40).SequenceEqual(bytes.AsSpan(40)),"Regenerated IMRO preserves all routing/reference bits");
        }
        ProbeConfiguration Ground()
        {
            var c = ProbeConfiguration.Create(ProbeProfile.Np2Multi);
            c.ModelRevision = "2013";
            foreach (var channel in c.Channels) channel.Reference = "Ground";
            return c;
        }
        var ground = Ground();
        string original = ground.ToJson();
        var payload = Np2DeviceSelection.Compile(ground);
        Verify(ground, payload);
        Check(payload.AsSpan(684, 192).ToArray().All(x => x == 0x11), "Active Ground base images");
        Check(ground.ToJson() == original, "Compile leaves caller immutable");
        for (int port = 1; port <= 3; port++)
        {
            var c = ground.Clone(); c.Connector = port;
            Verify(c, Np2DeviceSelection.Compile(c));
            if(output!=null){c.Save(Path.Combine(output,$"probe_{port}.json"));File.WriteAllBytes(Path.Combine(output,$"probe_{port}.n2b1"),Np2DeviceSelection.Compile(c));}
        }
        var dispersed=ground.Clone();
        foreach(var channel in dispersed.Channels) {
            channel.Shank=channel.Channel%4;
            var sites=Enumerable.Range(0,1280).Where(s=>Catalog.Channel(dispersed.Profile,channel.Shank,s)==channel.Channel).ToArray();
            channel.Site=sites[channel.Channel%sites.Length];
        }
        Verify(dispersed,Np2DeviceSelection.Compile(dispersed));
        var external = ground.Clone();
        foreach (var channel in external.Channels) channel.Reference = "External";
        Verify(external, Np2DeviceSelection.Compile(external));
        Check(Np2DeviceSelection.Compile(external).AsSpan(684, 192).ToArray().All(x => x == 0x44), "External base images");
        for (int target = 0; target < 4; target++)
        {
            var c = ground.Clone();
            foreach (var channel in c.Channels) channel.Reference = "Tip" + target;
            Verify(c, Np2DeviceSelection.Compile(c));
        }
        var ownTip = ground.Clone();
        foreach (var channel in ownTip.Channels) channel.Reference = "Tip";
        Verify(ownTip, Np2DeviceSelection.Compile(ownTip));
        var conflictingTip = ownTip.Clone(); conflictingTip.Channels[0].Reference = "Tip1";
        Reject(conflictingTip, "Different Tip targets refused");
        var mixed = ground.Clone(); mixed.Channels[0].Reference = "External"; mixed.Channels[1].Reference = "Tip2";
        Verify(mixed, Np2DeviceSelection.Compile(mixed));
        var legacy = ground.Clone(); legacy.ModelRevision = "24";
        foreach (var channel in legacy.Channels) { channel.ApGain = 80; channel.LfpGain = 80; }
        Reject(legacy, "Legacy gain-80 refused");
        var np2014 = ground.Clone(); np2014.ModelRevision = "NP2014";
        Reject(np2014, "Unadmitted model refused");
        Reject(ProbeConfiguration.Create(ProbeProfile.Np2Single), "Single-shank refused");
        Reject(ProbeConfiguration.Create(ProbeProfile.Np1), "NP1 refused");
        var standby = ground.Clone(); standby.Channels[0].Standby = true;
        Reject(standby, "Standby explicitly refused");
        var disconnected = standby.Clone(); disconnected.Channels[0].Site = null;
        Reject(disconnected, "Disconnected explicitly refused");
        var invalid = ground.Clone(); invalid.Channels[0].Channel = 1;
        Reject(invalid, "Duplicate channel refused");
        invalid = ground.Clone(); invalid.Channels[0].Site = 1;
        Reject(invalid, "Wrong site/channel mapping refused");
        invalid = ground.Clone(); invalid.Connector = 0;
        Reject(invalid, "Invalid connector refused");
        invalid = ground.Clone(); invalid.MappingId = "unverified";
        Reject(invalid, "Wrong mapping identity refused");
        invalid = ground.Clone(); invalid.HardwareState = "Applied";
        Reject(invalid, "Applied assertion refused");
        invalid = ground.Clone(); invalid.Channels[0].Reference = "Internal";
        Reject(invalid, "Unsupported reference refused");
        var reordered = ground.Clone(); reordered.Channels.Reverse();
        Check(Np2DeviceSelection.Compile(reordered).SequenceEqual(payload), "Canonical channel order gives stable payload/identity");
        Check(Np2DeviceSelection.Compile(ground).SequenceEqual(payload), "Repeat compilation stable");
        foreach(int offset in new[]{0,4,5,6,7,40,683,684}) {
            var broken=(byte[])payload.Clone();broken[offset]=255;
            bool rejected=false;try{Np2DeviceSelection.FromPayload(broken);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Malformed saved request refused at "+offset);
        }
        foreach (string path in paths)
        {
            var c = Np2DeviceSelection.LoadForPort(path, 2);
            Check(c.HardwareState == "NotApplied" && c.Connector == 2 && c.ModelRevision == "2013", "Supplied IMRO normalized");
            Check(c.Channels.All(x => x.Reference == "External" && !x.Standby && x.Site != null), "Supplied 384 External active channels");
            var b = Np2DeviceSelection.Compile(c);
            Verify(c, b);
            Check(Enumerable.Range(0, 4).All(s => Bit(b, s, 2) == 1 && Bit(b, s, 1285) == 1), "Supplied all-four-shank External switches");
            Console.WriteLine($"PASS supplied NP2013: {Path.GetFileName(path)} config={c.Id} bytes={b.Length}");
        }
        Console.WriteLine($"PASS N2B1 compiler: {assertions} assertions; supplied maps={paths.Length}; no hardware apply claimed.");
    }
}
