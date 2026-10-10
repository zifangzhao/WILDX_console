using System.Text.Json;

namespace NPConfigurator;

public static class SelectedChannelExport
{
    // Copy complete int16 frames in fixed memory; source remains a 384-channel recording.
    public static long Copy(string source, string destination, int[] channels)
    {
        if (channels.Length == 0 || channels.Any(c => c < 0 || c >= 384) || channels.Distinct().Count() != channels.Length)
            throw new ArgumentException("Select distinct channels 0-383.");
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The selected-channel output cannot overwrite its source.");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length % 768 != 0) throw new InvalidDataException("Source ends with an incomplete 384-channel int16 frame.");
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] frame = new byte[768 * 1024], selected = new byte[channels.Length * 2 * 1024];
        long samples = 0;
        while (input.Position < input.Length)
        {
            int bytes = (int)Math.Min(frame.Length, input.Length - input.Position); input.ReadExactly(frame.AsSpan(0, bytes));
            int frames = bytes / 768;
            for (int n = 0; n < frames; n++) for (int c = 0; c < channels.Length; c++)
            {
                int from = n*768 + channels[c]*2, to = (n*channels.Length+c)*2;
                selected[to] = frame[from]; selected[to+1] = frame[from+1];
            }
            output.Write(selected, 0, frames*channels.Length*2); samples += frames;
        }
        return samples;
    }
    public static void ExportCapture(string directory, ProbeConfiguration requested)
    {
        requested.RequireValid(); var channels = requested.ExportChannels;
        if (channels.Length == 0) throw new InvalidOperationException("No channels selected for export.");
        string root = Path.GetFullPath(Path.Combine(directory, "selected-channels"));
        if (Directory.Exists(root)) throw new IOException("Selected-channel output already exists.");
        string staging = root + ".partial-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var counts = new Dictionary<string, long>();
            foreach (string band in new[] { "AP", "LFP" })
                counts[band] = Copy(Path.Combine(directory, "continuous", "Neuropixels-" + band, "continuous.dat"), Path.Combine(staging, band.ToLowerInvariant()+".dat"), channels);
            // This is only an export mask. Requested electrode locations are not asserted as observed routing.
            ProbeConfiguration.AtomicWrite(Path.Combine(staging, "metadata.json"), JsonSerializer.Serialize(new {
                schemaVersion = 1, format = "sample-major little-endian int16", channelCount = channels.Length,
                outputIndexToAcquisitionChannel = channels, samples = counts, requestedConfigurationId = requested.Id,
                hardwareConfigurationState = "NotVerified", physicalElectrodeMapping = (string?)null,
                sampleRateSource = "../structure.oebin"
            }, ProbeConfiguration.JsonOptions));
            Directory.Move(staging, root);
        }
        finally
        {
            // staging is a freshly created GUID sibling of root, containing only this operation's files.
            if (Directory.Exists(staging))
            { foreach (var file in Directory.GetFiles(staging)) File.Delete(file); Directory.Delete(staging); }
        }
    }
}
