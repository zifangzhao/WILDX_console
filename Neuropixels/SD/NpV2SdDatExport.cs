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
        private const int Sector = 512, Block = 46592;
        private const uint RuSectors = 364;
        private static readonly uint[] CrcTable = MakeCrcTable();
        public sealed class Result
        {
            public uint SampleHz, Session, CommittedBlocks, ProbeMask;
            public ulong SourceTicks;
            public readonly long[] Rows;
            public readonly int[] Phases;
            public Result() : this(2) { }
            internal Result(int probes) { Rows = new long[probes]; Phases = new int[probes]; }
        }

        public static Result Export(Stream card, uint startLba, string output,
            long directoryEndLba = 0, Func<bool> cancelled = null, Action<int> progress = null)
        { return ExportCore(card,startLba,output,directoryEndLba,cancelled,progress,null); }

        public static Result ExportCalibrated(Stream card, uint startLba, string output, NpCalibrationSet calibration,
            long directoryEndLba = 0, Func<bool> cancelled = null, Action<int> progress = null)
        { if(calibration==null)throw new ArgumentNullException("calibration");return ExportCore(card,startLba,output,directoryEndLba,cancelled,progress,calibration.Count==0?null:calibration.Copy()); }

        static Result ExportCore(Stream card, uint startLba, string output,
            long directoryEndLba, Func<bool> cancelled, Action<int> progress, NpCalibrationSet calibration)
        {
            if (card == null || !card.CanRead || !card.CanSeek) throw new ArgumentException("Read-only seekable card/image required.");
            if (String.IsNullOrWhiteSpace(output) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("A fresh export directory is required; existing outputs are never overwritten.");
            byte[] desc = ReadAt(card, ((long)startLba + 3) * Sector, Sector);
            byte[] end = ReadAt(card, ((long)startLba + 4) * Sector, Sector);
            ValidateMetadata(desc, end, startLba, card.Length);
            uint probeMask = U(desc, 24);
            if(calibration!=null)calibration.RequireModel(true,(int)probeMask);
            int probes = probeMask == 1U ? 1 : probeMask == 3U ? 2 : 3, ticks = ProfileTicks(probeMask, U(desc, 16));
            // Triple kind3 stores fingerprints, not the three full selection maps.
            // Validate/archive those exact bytes without inferring applied wiring.
            byte[] configuration = probeMask != 3U ? ReadAt(card, ((long)startLba + 8) * Sector, 2048) : null;
            if (probeMask == 1U) ValidateSingleConfiguration(configuration, desc, U(end, 108));
            if (probeMask == 7U) ValidateTripleConfiguration(configuration, desc, U(end, 108));
            if (directoryEndLba != 0 && (directoryEndLba < U(end, 116) || directoryEndLba > U(desc, 68)))
                throw new InvalidDataException("SD catalog entry does not contain this committed recording extent.");
            byte[] utc = ReadAt(card, ((long)startLba + 5) * Sector, Sector);
            bool hasUtc = Magic(utc, "N2SDUTC1");
            if (hasUtc) ValidateUtc(utc, U(desc, 12), U(desc, 16));
            var result = new Result(probes) { ProbeMask = probeMask, SampleHz = U(desc, 16), Session = U(desc, 12),
                CommittedBlocks = U(end, 108), SourceTicks = Q(end, 120) };
            Directory.CreateDirectory(output);
            string receipt = Path.Combine(output, "np2_sd_export_status.txt");
            using (var f = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write))
                WriteText(f, "Export complete: False\nExport is in progress; .partial files are not complete recordings.\n");
            var files = new FileStream[probes];
            try
            {
                File.WriteAllBytes(Path.Combine(output, "descriptor.bin"), desc);
                File.WriteAllBytes(Path.Combine(output, "footer.bin"), end);
                if (configuration != null) File.WriteAllBytes(Path.Combine(output, "configuration.bin"), configuration);
                if (hasUtc) File.WriteAllBytes(Path.Combine(output, "utc.bin"), utc);
                if(calibration!=null)calibration.Save(output);
                for (int p = 0; p < probes; p++) files[p] = new FileStream(Path.Combine(output, Name(p) + ".partial"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                var rowBytes = new byte[384 * 2];
                var decoder = new NpV2SampleDecoder((mask, hz) => {
                    if (mask != probeMask || hz != result.SampleHz) throw new InvalidDataException("SD/DAT probe/rate contract mismatch.");
                }, (probe, row) => {
                    if (probe < 1 || probe > probes || row.Length != 384) throw new InvalidDataException("Invalid decoded SD row.");
                    if(calibration!=null)calibration.Correct(probe,row,1,null,0);
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
                    if (Q(block, 24) != seq || Q(block, 32) != (ulong)seq * (uint)ticks ||
                        U(block, 44) != result.SampleHz * 1600 || U(block, 60) != result.SampleHz ||
                        U(block, 48) != U(desc, 28) || U(block, 76) != probeMask || U(block, 80) != probeMask || U(block, 84) != 1 ||
                        U(block, 156) != U(desc, 36) || U(block, 160) != U(desc, 32) || (U(block, 52) & 40) != 40 ||
                        U(block, 56) != 0 || U(block, 508) != 0)
                        throw new InvalidDataException("SD block/header/sequence contract mismatch at block " + seq + ".");
                    // Only sequence/tick and advertised raw-D8 lookahead bytes
                    // can vary. The shared verifier checks exact lookahead bounds
                    // and zero tail; all identity/phase/provenance stays immutable.
                    if (immutable == null) immutable = (byte[])block.Clone();
                    else for (int n = 0; n < Sector; n++) if ((n < 24 || n >= 40) &&
                        !((U(block, 52) & 64U) != 0U && n >= 184 && n < 184 + U(block, 180)) && block[n] != immutable[n])
                        throw new InvalidDataException("SD metadata changed at block " + seq + ".");
                    verifier.Feed(block, 0); decoder.Feed(block, 0, verifier);
                    if (progress != null && (seq % 64 == 0 || seq + 1 == result.CommittedBlocks))
                        progress((int)((ulong)(seq + 1) * 100 / result.CommittedBlocks));
                }
                decoder.Complete();
                if (verifier.Blocks != result.CommittedBlocks) throw new InvalidDataException("SD verifier extent mismatch.");
                for (int p = 0; p < probes; p++) {
                    result.Phases[p] = verifier.PhaseForProbe(p);
                    ulong first = (ulong)((1600 - result.Phases[p]) % 1600);
                    long expected = (long)((result.SourceTicks - first) / 1600);
                    if (result.Phases[p] < 0 || result.Rows[p] != expected || files[p].Length != expected * 768)
                        throw new InvalidDataException("SD DAT row count/length mismatch.");
                    files[p].Flush(true); files[p].Dispose(); files[p] = null;
                }
                // Publish the success receipt LAST. No padding, resync, silent
                // recovery, dropped rows or fabricated LFP stream is permitted.
                for (int p = 0; p < probes; p++) File.Move(Path.Combine(output, Name(p) + ".partial"), Path.Combine(output, Name(p)));
                var report = new StringBuilder("Export complete: True\nStored terminal state: Complete\n");
                report.AppendFormat(CultureInfo.InvariantCulture,
                    "Session: {0}\nSample rate Hz: {1}\nCommitted blocks: {2}\nSource ticks: {3}\n", result.Session, result.SampleHz, result.CommittedBlocks, result.SourceTicks);
                report.Append("Format: per-probe AP, 384 channels, sample-major int16 little-endian, signed nominal ADC counts\n");
                report.Append("sample_encoding=" + (calibration==null?NpV2SampleDecoder.SampleEncoding:"signed_calibrated_nominal_adc_counts") + "\n");
                report.Append("sample_transform=" + NpV2SampleDecoder.SampleTransform + (calibration==null?"":"; then factory gain correction") + "\n");
                report.Append(calibration==null?"sample_range=-2047..2048\n":"sample_range=int16; factory-dependent; no clipping\n");
                report.Append("sample_polarity=inverted relative to raw ASIC ADC codes; ASIC negative analog gain undone\n");
                report.Append(calibration==null?"No gain/comparator calibration or microvolt conversion. ":"Factory host gain correction applied; see adc_calibration.json. No hardware trim or microvolt conversion claim. ");
                report.Append("Legacy DAT before WILDX 1.0.0.9 contains unsigned raw 12-bit codes; existing DAT files are not rewritten.\n");
                report.Append("Checks: descriptor/footer CRC32, exact immutable headers/sequence/ticks, native SuperSync in every block, exact row/file lengths\n");
                report.Append("No ADC payload CRC. Whole-superframe loss or arbitrary ADC bit errors are not excluded by this export. Live producer continuity and analog quality require separate hardware evidence.\n");
                report.Append("Boundary policy: trim incomplete leading/trailing AP rows; do not pad. Probe rows have independent source-tick origins; use the offsets below for alignment.\n");
                if (probeMask == 1U) {
                    report.Append("Configuration: configuration.bin; N2IMRO1; planned/staged P1 request only, NOT applied-mapping proof. Physical electrode geometry unavailable.\n");
                }
                if (probeMask == 7U) {
                    report.AppendFormat(CultureInfo.InvariantCulture,
                        "Time base: source ticks / {0} Hz; AP step=1600/{0} seconds (exact rational).\n", result.SampleHz * 1600U);
                    report.AppendFormat(CultureInfo.InvariantCulture,
                        "Configuration: configuration.bin; N2IMRO2 kind3; staged_selection_mask=0x{0:X}; identity/request-CRC fingerprints only, NOT full maps or applied-mapping proof. Physical electrode geometry unavailable.\n", configuration[72]);
                    for (int p = 0; p < probes; p++) if ((configuration[72] & (1 << p)) != 0)
                        report.AppendFormat(CultureInfo.InvariantCulture, "Probe {0} staged configuration: ID={1}; request_CRC32={2:X8}; not applied proof\n",
                            p + 1, BitConverter.ToString(configuration, 128 + p * 36, 32).Replace("-", ""), U(configuration, 160 + p * 36));
                }
                for (int p = 0; p < probes; p++) report.AppendFormat(CultureInfo.InvariantCulture,
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
                try { NpNeuroscopeXml.WriteFolder(output,(int)probeMask,false,(int)result.SampleHz,null,null); }
                catch(IOException ex) { File.AppendAllText(receipt,"Neuroscope XML unavailable: "+ex.Message+"\n"); }
            }
        }

        public static void ValidateMetadata(byte[] d, byte[] f, uint start, long length)
        {
            Seal(d, "N2SDLOG1"); Seal(f, "N2SDEND1");
            int ticks = ProfileTicks(U(d, 24), U(d, 16));
            uint mask = U(d, 24);
            ulong payload = U(d, 60), limit = U(d, 64), end = U(d, 68);
            uint version=U(d,8);
            bool unitGeometry=version==12 || version==13;
            uint au=unitGeometry?U(d,128):0;
            ulong expectedPayload = ((ulong)start + 8192 + 16383) & ~16383UL;
            bool geometry=true;
            if(unitGeometry){
                if(version==12?(au<1024 || au%1024!=0):!IsLegacyAu(au))
                    throw new InvalidDataException("Invalid SD allocation unit for metadata version "+version+".");
                expectedPayload=AlignUp(expectedPayload,au);
                ulong auEnd=AlignUp(limit,au);
                geometry=payload<limit && end==AlignUp(auEnd,16384) &&
                    (version!=12 || (payload/1024<=0x3fffff && (auEnd-au)/1024<=0x3fffff));
            }
            ulong recordingEnd=unitGeometry?limit:end;
            ulong rus = recordingEnd > payload ? (recordingEnd - payload) / RuSectors : 0;
            if ((version!=11 && !unitGeometry) || !geometry || U(d, 12) == 0 ||
                U(d, 20) != U(d, 16) * 1600 || U(d, 28) != (mask==1U?7U:mask==3U?63U:511U) || U(d, 32) != (mask==7U?6U:4U) ||
                U(d, 36) != (mask==1U?3U:mask==3U?6U:9U) || U(d, 40) != Block || U(d, 44) != (uint)ticks || U(d, 48) != 91 || U(d, 52) != 4 ||
                U(d, 56) != start || start < 8192 || start % 8192 != 0 || payload != expectedPayload ||
                end % 16384 != 0 || end > U(d, 72) || rus == 0 || limit != payload + rus * RuSectors ||
                U(d, 96) != (version==13?47U:version==12?31U:15U) || U(d, 100) != 1 || !Zero(d,104,24) ||
                (unitGeometry?!Zero(d,132,376):!Zero(d,128,380)))
                throw new InvalidDataException("Unsupported SD v11/v12/v13 NP2 profile/descriptor/geometry.");
            for (int n = 8; n < 100; n++) if (d[n] != f[n]) throw new InvalidDataException("SD descriptor/footer belong to different records.");
            ulong blocks = U(f, 108), committed = U(f, 112), writtenEnd = U(f, 116);
            if (U(f, 100) != 3 || U(f, 104) != 0 || blocks == 0 || blocks % 4 != 0 ||
                committed != blocks / 4 || committed > rus || writtenEnd != payload + committed * RuSectors ||
                Q(f, 120) != blocks * (uint)ticks || writtenEnd * Sector > (ulong)length ||
                (unitGeometry?U(f,128)!=au || !Zero(f,132,376):!Zero(f,128,380)))
                throw new InvalidDataException("Incomplete, failed, torn or out-of-bounds SD recording; strict DAT export refused.");
        }

        private static bool IsLegacyAu(uint unit)
        {
            switch(unit){case 2048:case 4096:case 8192:case 16384:case 24576:case 32768:
                case 49152:case 65536:case 131072:return true;default:return false;}
        }

        private static int ProfileTicks(uint mask, uint hz)
        {
            if (hz == 7500U || hz == 15000U || hz == 20000U || hz == 30000U) {
                if (mask == 1U) return 122880;
                if (mask == 3U) return 61440;
                if (mask == 7U) return 40960;
            }
            throw new InvalidDataException("Unsupported SD NP2 probe/rate tuple.");
        }

        private static void ValidateSingleConfiguration(byte[] b, byte[] d, uint committed)
        {
            uint kind=U(b,12);
            if (!Magic(b,"N2SDCFG1") || U(b,8)!=1U || (kind!=1U && kind!=2U) ||
                Crc(b,b.Length)!=U(d,76) || U(b,16)!=U(d,16) || U(b,20)!=U(d,20) ||
                U(b,24)!=1U || U(b,28)!=7U || U(b,32)!=4U || U(b,36)!=3U || U(b,40)!=Block ||
                U(b,44)<committed || U(b,44)%4!=0 ||
                (ulong)U(b,44)*91>(ulong)U(d,64)-U(d,60) ||
                ((U(d,8)==12U || U(d,8)==13U) && (ulong)U(b,44)*91!=(ulong)U(d,64)-U(d,60)) ||
                !Zero(b,71,1) || b[72]!=(kind==2U?1:0) || !Zero(b,73,55) || !Zero(b,1004,1044))
                throw new InvalidDataException("Invalid single-probe SD configuration provenance.");
            for(int n=0;n<16;n++)if(b[48+n]!=d[80+n])
                throw new InvalidDataException("Single-probe configuration/card identity mismatch.");
            for(int n=0;n<7;n++)if(b[64+n]!="N2IMRO1"[n])
                throw new InvalidDataException("Unsupported single-probe SD selection provenance.");
            if(kind==1U){
                if(!Zero(b,128,876))throw new InvalidDataException("Planned profile contains an unexpected selection request.");
            }else if(Encoding.ASCII.GetString(b,128,4)!="N2B1" || b[132]!=4 || !Zero(b,133,3))
                throw new InvalidDataException("Single-probe staged selection is not an NP2013 P1 request.");
        }

        private static void ValidateTripleConfiguration(byte[] b, byte[] d, uint committed)
        {
            if (!Magic(b, "N2SDCFG1") || U(b, 8) != 1U || U(b, 12) != 3U ||
                Crc(b, b.Length) != U(d, 76) || U(b, 16) != U(d, 16) || U(b, 20) != U(d, 20) ||
                U(b, 24) != 7U || U(b, 28) != 511U || U(b, 32) != 6U || U(b, 36) != 9U || U(b, 40) != Block ||
                U(b, 44) < committed || U(b, 44) % 4 != 0 ||
                (ulong)U(b, 44) * 91 > (ulong)U(d, 64) - U(d, 60) ||
                ((U(d, 8) == 12U || U(d, 8) == 13U) && (ulong)U(b, 44) * 91 != (ulong)U(d, 64) - U(d, 60)) ||
                !Zero(b, 71, 1) || (b[72] & ~7) != 0 || !Zero(b, 73, 55) || !Zero(b, 236, 1812))
                throw new InvalidDataException("Invalid triple SD configuration/fingerprint provenance.");
            for (int n = 0; n < 16; n++) if (b[48 + n] != d[80 + n])
                throw new InvalidDataException("Triple configuration/card identity mismatch.");
            for (int n = 0; n < 7; n++) if (b[64 + n] != "N2IMRO2"[n])
                throw new InvalidDataException("Unsupported triple SD selection provenance.");
            for (int p = 0; p < 3; p++) if ((b[72] & (1 << p)) == 0 && !Zero(b, 128 + p * 36, 36))
                throw new InvalidDataException("Unselected triple port has unexpected fingerprints.");
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
        private static ulong AlignUp(ulong value,uint unit){return (value+unit-1)/unit*unit;}
        private static uint U(byte[] b, int n) { return BitConverter.ToUInt32(b, n); }
        private static ulong Q(byte[] b, int n) { return BitConverter.ToUInt64(b, n); }
        private static void Seal(byte[] b, string magic) { if (!Magic(b, magic) || U(b, 508) != Crc(b, 508)) throw new InvalidDataException("Missing/torn/corrupt SD metadata: " + magic); }
        private static uint[] MakeCrcTable() { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int n = 0; n < 8; n++) c = (c >> 1) ^ ((c & 1) == 0 ? 0 : 0xedb88320U); t[i] = c; } return t; }
        private static uint Crc(byte[] b, int count) { uint c = uint.MaxValue; for (int n = 0; n < count; n++) c = CrcTable[(c ^ b[n]) & 255] ^ (c >> 8); return c ^ uint.MaxValue; }
    }
}
