namespace NPConfigurator;

// N2B1 is a request, not an assertion that hardware has applied this selection.
public static class Np2DeviceSelection
{
    public const int PayloadBytes = 876;
    public const int ShankBytes = 161;
    public const int BaseBytes = 96;

    public static ProbeConfiguration LoadForPort(string path, int connector)
        => Normalize(StandardFormats.Load(path), connector);

    private static ProbeConfiguration Normalize(ProbeConfiguration configuration, int connector)
    {
        configuration.RequireValid();
        var c = configuration.Clone(); // Canonical channel order; never mutate the caller.
        c.Connector = connector;
        if (c.ModelRevision.Length == 0) c.ModelRevision = "2013";
        c.HardwareState = "NotApplied";
        c.RequireValid();
        if (c.Profile != ProbeProfile.Np2Multi || c.ModelRevision is not ("2013" or "NP2013"))
            throw new InvalidDataException("V2 device selection supports only NP2013 four-shank, fixed-gain-100 probes; legacy gain-80 and single-shank models are not supported.");
        if (c.Channels.Any(x => x.Standby || x.Site == null))
            throw new InvalidDataException("V2 device selection requires all 384 channels active and connected; standby is not supported.");
        return c;
    }

    public static byte[] Compile(ProbeConfiguration configuration)
    {
        var c = Normalize(configuration, configuration.Connector);
        var shanks = Enumerable.Range(0, 4).Select(_ => new byte[ShankBytes]).ToArray();
        var even = new byte[BaseBytes];
        var odd = new byte[BaseBytes];
        int tipShank = -1;
        foreach (var channel in c.Channels)
        {
            int site = channel.Site!.Value;
            // NP2 V1.2 guide SRCHAIN1-4; OpenEphys NeuropixelsV2.GenerateShankBits.
            int electrodeBit = (site & 1) != 0 ? 642 - site / 2 : 645 + site / 2;
            ConfigurationCompiler.SetBit(shanks[channel.Shank], electrodeBit, 1);

            int reference;
            switch (channel.Reference)
            {
                case "Ground": reference = 1; break;
                case "External":
                    reference = 4;
                    // Guide p28: connect External on shanks with External channels.
                    ConfigurationCompiler.SetBit(shanks[channel.Shank], 2, 1);
                    ConfigurationCompiler.SetBit(shanks[channel.Shank], 1285, 1);
                    break;
                default:
                    reference = 2;
                    int target = channel.Reference == "Tip" ? channel.Shank
                        : channel.Reference is "Tip0" or "Tip1" or "Tip2" or "Tip3"
                            ? channel.Reference[3] - '0'
                            : throw new InvalidDataException("Unsupported NP2013 reference.");
                    if (tipShank >= 0 && tipShank != target)
                        throw new InvalidDataException("V2 device selection supports only one shared Tip-reference shank; different Tip targets would join the reference bus.");
                    tipShank = target;
                    break;
            }
            // SRCHAIN5 even / SRCHAIN6 odd, descending channels. Wire nibble:
            // STDB/ExtRef/TipRef/GNDRef = 8/4/2/1; STDB stays clear.
            var chain = (channel.Channel & 1) == 0 ? even : odd;
            int byteIndex = (383 - channel.Channel) / 4;
            int shift = (channel.Channel & 2) != 0 ? 4 : 0;
            chain[byteIndex] |= (byte)(reference << shift);
        }
        if (tipShank >= 0)
        {
            // Primary OpenEphys constants: External=1285/2, Tip=644/643:
            // https://github.com/open-ephys/bonsai-onix1/blob/main/OpenEphys.Onix1/NeuropixelsV2ProbeConfiguration.cs
            // Single Tip-shank selection:
            // https://github.com/open-ephys/bonsai-onix1/blob/main/OpenEphys.Onix1/NeuropixelsV2QuadShankProbeConfiguration.cs
            ConfigurationCompiler.SetBit(shanks[tipShank], 643, 1);
            ConfigurationCompiler.SetBit(shanks[tipShank], 644, 1);
        }

        var bytes = new byte[PayloadBytes];
        "N2B1"u8.CopyTo(bytes);
        bytes[4] = 4;
        bytes[5] = (byte)(c.Connector - 1);
        Convert.FromHexString(c.Id).CopyTo(bytes, 8);
        for (int shank = 0; shank < 4; shank++) shanks[shank].CopyTo(bytes, 40 + shank * ShankBytes);
        even.CopyTo(bytes, 40 + 4 * ShankBytes);
        odd.CopyTo(bytes, 40 + 4 * ShankBytes + BaseBytes);
        return bytes;
    }
}
