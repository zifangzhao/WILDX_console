using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

// Offline only: replay the actual app decoder against every saved DAT byte.
// Complements live counters; does not independently prove the decoder's physics.
internal static class NpUsbRawDatReplayTests
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
    static long Count(Dictionary<string, string> status, string key) { return Int64.Parse(status[key], CultureInfo.InvariantCulture); }
    static int Main(string[] args)
    {
        var files = new FileStream[3];
        try
        {
            if (args.Length != 2) throw new ArgumentException("Pass actual WILDX_console.exe and completed recording folder.");
            string exe = Path.GetFullPath(args[0]), folder = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string path = Path.Combine(Path.GetDirectoryName(exe), new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            var status = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(Path.Combine(folder, "neuropixels_np2usb_status.txt")))
            {
                int at = line.IndexOf('='); if (at > 0) status.Add(line.Substring(0, at), line.Substring(at + 1));
            }
            Require(status["complete"] == "True" && status["error"] == "none" && status["warning"] == "none", "Recording is not certified complete.");
            uint mask = UInt32.Parse(status["decoded_ap_probe_mask"].Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            uint hz = UInt32.Parse(status["decoded_ap_sample_hz"], CultureInfo.InvariantCulture);
            Require(mask > 0 && mask <= 7, "Invalid live mask.");
            for (int p = 0; p < 3; p++) if ((mask & (1U << p)) != 0)
            {
                string dat = Path.Combine((mask & (mask - 1)) == 0 ? folder : Path.Combine(folder, "probe" + (p + 1)), "neuropixels_ap.dat");
                files[p] = new FileStream(dat, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            }
            Assembly app = Assembly.LoadFrom(exe);
            Type verifierType = app.GetType("USB512_interface.NpV2WireVerifier", true);
            Type decoderType = app.GetType("USB512_interface.NpV2SampleDecoder", true);
            object verifier = Activator.CreateInstance(verifierType, true);
            long[] rows = new long[3]; byte[] datRow = new byte[768]; bool formatSeen = false;
            Action<uint, uint> format = delegate(uint liveMask, uint sampleHz)
            { Require(liveMask == mask && sampleHz == hz, "RAW format differs from recorded metadata."); formatSeen = true; };
            Action<int, short[]> row = delegate(int probe, short[] values)
            {
                Require(probe >= 1 && probe <= 3 && files[probe - 1] != null && values.Length == 384, "Unexpected decoded lane/layout.");
                FileStream file = files[probe - 1];
                int at = 0;
                while (at < datRow.Length)
                {
                    int read = file.Read(datRow, at, datRow.Length - at);
                    Require(read != 0, "DAT ended before replayed RAW."); at += read;
                }
                for (int c = 0; c < values.Length; c++)
                    if ((ushort)values[c] != (uint)(datRow[2 * c] | datRow[2 * c + 1] << 8))
                        throw new InvalidDataException("DAT differs at P" + probe + " row " + rows[probe - 1] + " channel " + c);
                rows[probe - 1]++;
            };
            object decoder = Activator.CreateInstance(decoderType, All, null, new object[] { format, row, 0 }, CultureInfo.InvariantCulture);
            MethodInfo verify = verifierType.GetMethod("Feed", All), decode = decoderType.GetMethod("Feed", All);
            long blocks = 0; byte[] block = new byte[46592];
            using (var raw = new FileStream(Path.Combine(folder, "neuropixels_np2usb.raw"), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                Require(raw.Length == Count(status, "verified_blocks") * block.Length, "RAW byte/block count differs.");
                while (raw.Position < raw.Length)
                {
                    int at = 0;
                    while (at < block.Length) { int read = raw.Read(block, at, block.Length - at); Require(read != 0, "Partial RAW block."); at += read; }
                    verify.Invoke(verifier, new object[] { block, 0 });
                    decode.Invoke(decoder, new object[] { block, 0, verifier }); blocks++;
                }
            }
            decoderType.GetMethod("Complete", All).Invoke(decoder, null);
            long total = 0;
            for (int p = 0; p < 3; p++) if (files[p] != null)
            {
                Require(rows[p] > 0 && files[p].Position == files[p].Length, "DAT has an extra/truncated tail."); total += rows[p];
            }
            Require(formatSeen && blocks == Count(status, "verified_blocks") && total == Count(status, "decoded_ap_rows"), "Replay counters differ.");
            Console.WriteLine("PASS full RAW continuity/decode replay: blocks=" + blocks + "; rows-per-port=" + String.Join(",", rows) + "; every DAT byte matched=" + total * 768L);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { foreach (FileStream file in files) if (file != null) file.Dispose(); }
    }
}
