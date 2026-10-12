using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace USB512_interface
{
    // Offline only. Reuse the exact USB wire verifier/384-channel decoder;
    // never add decoding, checksums or preview work to the MCU recording path.
    public static class NpV2SdDatExport
    {
        private const int Sector = 512, Block = 46592, Ticks = 61440;
        private const uint RuSectors = 364;
        private static readonly uint[] CrcTable = MakeCrcTable();
        public sealed class Result
        {
            public uint SampleHz, Session, CommittedBlocks;
            public ulong SourceTicks;
            public readonly long[] Rows = new long[2];
            public readonly int[] Phases = new int[2];
        }

        public static Result Export(Stream card, uint startLba, string output,
            long directoryEndLba = 0, Func<bool> cancelled = null, Action<int> progress = null)
        {
            if (card == null || !card.CanRead || !card.CanSeek) throw new ArgumentException("Read-only seekable card/image required.");
            if (String.IsNullOrWhiteSpace(output) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("A fresh export directory is required; existing outputs are never overwritten.");
            byte[] desc = ReadAt(card, ((long)startLba + 3) * Sector, Sector);
            byte[] end = ReadAt(card, ((long)startLba + 4) * Sector, Sector);
            ValidateMetadata(desc, end, startLba, card.Length);
            if (directoryEndLba != 0 && (directoryEndLba < U(end, 116) || directoryEndLba > U(desc, 68)))
                throw new InvalidDataException("SD catalog entry does not contain this committed recording extent.");
            byte[] utc = ReadAt(card, ((long)startLba + 5) * Sector, Sector);
            bool hasUtc = Magic(utc, "N2SDUTC1");
            if (hasUtc) ValidateUtc(utc, U(desc, 12), U(desc, 16));
            var result = new Result { SampleHz = U(desc, 16), Session = U(desc, 12),
                CommittedBlocks = U(end, 108), SourceTicks = Q(end, 120) };
            Directory.CreateDirectory(output);
            string receipt = Path.Combine(output, "np2_sd_export_status.txt");
            using (var f = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write))
                WriteText(f, "Export complete: False\nExport is in progress; .partial files are not complete recordings.\n");
            var files = new FileStream[2];
            try
            {
                File.WriteAllBytes(Path.Combine(output, "descriptor.bin"), desc);
                File.WriteAllBytes(Path.Combine(output, "footer.bin"), end);
                if (hasUtc) File.WriteAllBytes(Path.Combine(output, "utc.bin"), utc);
                for (int p = 0; p < 2; p++) files[p] = new FileStream(Path.Combine(output, Name(p) + ".partial"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                var rowBytes = new byte[384 * 2];
                var decoder = new NpV2SampleDecoder((mask, hz) => {
                    if (mask != 3 || hz != result.SampleHz) throw new InvalidDataException("SD/DAT probe/rate contract mismatch.");
                }, (probe, row) => {
                    if (probe < 1 || probe > 2 || row.Length != 384) throw new InvalidDataException("Invalid decoded SD row.");
                    // The existing decoder's row array is reused. Consume it
                    // synchronously here, never retain it in an asynchronous queue.
                    for (int c = 0; c < row.Length; c++) {
                        rowBytes[c * 2] = (byte)row[c]; rowBytes[c * 2 + 1] = (byte)(row[c] >> 8);
                    }
                    files[probe - 1].Write(rowBytes, 0, rowBytes.Length); result.Rows[probe - 1]++;
                });
                var verifier = new NpV2WireVerifier();
                var block = new byte[Block]; byte[] immutable = null;
                card.Position = (long)U(desc, 60) * Sector;
                for (uint seq = 0; seq < result.CommittedBlocks; seq++) {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException("SD DAT export cancelled; output remains partial.");
                    ReadExact(card, block);
                    if (Q(block, 24) != seq || Q(block, 32) != (ulong)seq * Ticks ||
                        U(block, 44) != result.SampleHz * 1600 || U(block, 60) != result.SampleHz ||
                        U(block, 76) != 3 || U(block, 80) != 3 || U(block, 84) != 1 ||
                        U(block, 156) != 6 || U(block, 160) != 4 || (U(block, 52) & 40) != 40 ||
                        U(block, 56) != 0 || U(block, 508) != 0)
                        throw new InvalidDataException("SD block/header/sequence contract mismatch at block " + seq + ".");
                    // All header bytes except sequence/tick must be immutable,
                    // including reserved fields and probe setup provenance.
                    if (immutable == null) immutable = (byte[])block.Clone();
                    else for (int n = 0; n < Sector; n++) if ((n < 24 || n >= 40) && block[n] != immutable[n])
                        throw new InvalidDataException("SD metadata changed at block " + seq + ".");
                    verifier.Feed(block, 0); decoder.Feed(block, 0, verifier);
                    if (progress != null && (seq % 64 == 0 || seq + 1 == result.CommittedBlocks))
                        progress((int)((ulong)(seq + 1) * 100 / result.CommittedBlocks));
                }
                decoder.Complete();
                if (verifier.Blocks != result.CommittedBlocks) throw new InvalidDataException("SD verifier extent mismatch.");
                for (int p = 0; p < 2; p++) {
                    result.Phases[p] = verifier.PhaseForProbe(p);
                    ulong first = (ulong)((1600 - result.Phases[p]) % 1600);
                    long expected = (long)((result.SourceTicks - first) / 1600);
                    if (result.Phases[p] < 0 || result.Rows[p] != expected || files[p].Length != expected * 768)
                        throw new InvalidDataException("SD DAT row count/length mismatch.");
                    files[p].Flush(true); files[p].Dispose(); files[p] = null;
                }
                // Publish the success receipt LAST. No padding, resync, silent
                // recovery, dropped rows or fabricated LFP stream is permitted.
                for (int p = 0; p < 2; p++) File.Move(Path.Combine(output, Name(p) + ".partial"), Path.Combine(output, Name(p)));
                var report = new StringBuilder("Export complete: True\nStored terminal state: Complete\n");
                report.AppendFormat(CultureInfo.InvariantCulture,
                    "Session: {0}\nSample rate Hz: {1}\nCommitted blocks: {2}\nSource ticks: {3}\n", result.Session, result.SampleHz, result.CommittedBlocks, result.SourceTicks);
                report.Append("Format: per-probe AP, 384 channels, sample-major int16 little-endian, signed nominal ADC counts\n");
                report.Append("sample_encoding=" + NpV2SampleDecoder.SampleEncoding + "\n");
                report.Append("sample_transform=" + NpV2SampleDecoder.SampleTransform + "\n");
                report.Append("sample_range=-2047..2048\nsample_polarity=inverted relative to raw ASIC ADC codes; ASIC negative analog gain undone\n");
                report.Append("No gain/comparator calibration or microvolt conversion. Legacy DAT before WILDX 1.0.0.9 contains unsigned raw 12-bit codes; existing DAT files are not rewritten.\n");
                report.Append("Checks: descriptor/footer CRC32, exact immutable headers/sequence/ticks, native SuperSync in every block, exact row/file lengths\n");
                report.Append("No ADC payload CRC. Whole-superframe loss or arbitrary ADC bit errors are not excluded by this export. Live producer continuity and analog quality require separate hardware evidence.\n");
                report.Append("Boundary policy: trim incomplete leading/trailing AP rows; do not pad. Probe rows have independent source-tick origins; use the offsets below for alignment.\n");
                for (int p = 0; p < 2; p++) report.AppendFormat(CultureInfo.InvariantCulture,
                    "Probe {0}: file={1}; rows={2}; phase={3}; first_source_tick={4}; trailing_ticks={5}; SHA256={6}\n",
                    p + 1, Name(p), result.Rows[p], result.Phases[p], (1600 - result.Phases[p]) % 1600,
                    (result.SourceTicks - (ulong)((1600 - result.Phases[p]) % 1600)) % 1600, Hash(Path.Combine(output, Name(p))));
                report.Append(hasUtc ? "UTC: validated utc.bin; capture-start bracket only, not ADC synchronization\n" : "UTC: not present; no timing accuracy claim\n");
                if (progress != null) progress(100);
                File.WriteAllText(receipt, report.ToString(), Encoding.UTF8);
                return result;
            }
            catch (Exception ex)
            {
                File.WriteAllText(receipt, "Export complete: False\nPartial or unqualified output: do not treat as an error-free recording.\nError: " + ex.Message + "\n", Encoding.UTF8);
                throw;
            }
            finally {
                foreach (var f in files) if (f != null) f.Dispose();
                try { NpNeuroscopeXml.WriteFolder(output,3,false,(int)result.SampleHz,null,null); }
                catch(IOException ex) { File.AppendAllText(receipt,"Neuroscope XML unavailable: "+ex.Message+"\n"); }
            }
        }

        public static void ValidateMetadata(byte[] d, byte[] f, uint start, long length)
        {
            Seal(d, "N2SDLOG1"); Seal(f, "N2SDEND1");
            ulong payload = U(d, 60), limit = U(d, 64), end = U(d, 68);
            ulong expectedPayload = ((ulong)start + 8192 + 16383) & ~16383UL;
            ulong rus = end > payload ? (end - payload) / RuSectors : 0;
            if (U(d, 8) != 11 || U(d, 12) == 0 || (U(d, 16) != 20000 && U(d, 16) != 30000) ||
                U(d, 20) != U(d, 16) * 1600 || U(d, 24) != 3 || U(d, 28) != 63 || U(d, 32) != 4 ||
                U(d, 36) != 6 || U(d, 40) != Block || U(d, 44) != Ticks || U(d, 48) != 91 || U(d, 52) != 4 ||
                U(d, 56) != start || start < 8192 || start % 8192 != 0 || payload != expectedPayload ||
                end % 16384 != 0 || end > U(d, 72) || rus == 0 || limit != payload + rus * RuSectors ||
                U(d, 96) != 15 || U(d, 100) != 1 || !Zero(d, 104, 404))
                throw new InvalidDataException("Unsupported SD v11 packed dual-NP2 descriptor/geometry.");
            for (int n = 8; n < 100; n++) if (d[n] != f[n]) throw new InvalidDataException("SD descriptor/footer belong to different records.");
            ulong blocks = U(f, 108), committed = U(f, 112), writtenEnd = U(f, 116);
            if (U(f, 100) != 3 || U(f, 104) != 0 || blocks == 0 || blocks % 4 != 0 ||
                committed != blocks / 4 || committed > rus || writtenEnd != payload + committed * RuSectors ||
                Q(f, 120) != blocks * Ticks || writtenEnd * Sector > (ulong)length || !Zero(f, 128, 380))
                throw new InvalidDataException("Incomplete, failed, torn or out-of-bounds SD recording; strict DAT export refused.");
        }

        private static void ValidateUtc(byte[] b, uint session, uint hz)
        {
            Seal(b, "N2SDUTC1"); uint flags = U(b, 16);
            if (U(b, 8) != 1 || U(b, 12) != session || (flags & ~7U) != 0 ||
                U(b, 72) != hz || U(b, 76) != 1000 || !Zero(b, 80, 428) ||
                ((flags & 1) != 0 && (U(b, 20) == 0 || U(b, 24) < 1000 || Q(b, 32) == 0)))
                throw new InvalidDataException("Invalid SD UTC companion.");
            if ((flags & 3) == 3) for (int p = 0; p < 2; p++) {
                uint delta = unchecked(U(b, 48 + p * 4) - U(b, 28));
                if (delta >= 0x80000000 || Q(b, 56 + p * 8) != checked(Q(b, 32) + (ulong)delta * 1000))
                    throw new InvalidDataException("SD UTC stamp does not match its anchor.");
            }
        }

        private static string Name(int p) { return "neuropixels_probe" + (p + 1) + "_ap.dat"; }
        private static string Hash(string path) { using (var f = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", ""); }
        private static void WriteText(Stream f, string text) { var b = Encoding.UTF8.GetBytes(text); f.Write(b, 0, b.Length); }
        private static byte[] ReadAt(Stream f, long at, int count) { f.Position = at; var b = new byte[count]; ReadExact(f, b); return b; }
        private static void ReadExact(Stream f, byte[] b) { int n = 0, got; while (n < b.Length && (got = f.Read(b, n, b.Length - n)) != 0) n += got; if (n != b.Length) throw new EndOfStreamException("Truncated SD record."); }
        private static bool Magic(byte[] b, string s) { for (int n = 0; n < s.Length; n++) if (b[n] != s[n]) return false; return true; }
        private static bool Zero(byte[] b, int at, int count) { for (int n = at; n < at + count; n++) if (b[n] != 0) return false; return true; }
        private static uint U(byte[] b, int n) { return BitConverter.ToUInt32(b, n); }
        private static ulong Q(byte[] b, int n) { return BitConverter.ToUInt64(b, n); }
        private static void Seal(byte[] b, string magic) { if (!Magic(b, magic) || U(b, 508) != Crc(b, 508)) throw new InvalidDataException("Missing/torn/corrupt SD metadata: " + magic); }
        private static uint[] MakeCrcTable() { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int n = 0; n < 8; n++) c = (c >> 1) ^ ((c & 1) == 0 ? 0 : 0xedb88320U); t[i] = c; } return t; }
        private static uint Crc(byte[] b, int count) { uint c = uint.MaxValue; for (int n = 0; n < count; n++) c = CrcTable[(c ^ b[n]) & 255] ^ (c >> 8); return c ^ uint.MaxValue; }
    }
}
