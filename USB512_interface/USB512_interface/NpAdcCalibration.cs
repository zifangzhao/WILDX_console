using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // Factory files describe hardware trims AND host corrections. Importing a
    // file never proves that its comparator/bias trims were written to a probe.
    public sealed class NpAdcCalibration
    {
        public readonly string Serial, Model, GainPath, AdcPath, GainHash, AdcHash;
        public readonly int ApGain, LfpGain;
        public readonly bool DigitalAdc;
        readonly byte[] gainBytes, adcBytes;
        readonly short[][] ap = new short[384][], lfp = new short[384][];
        readonly int midpoint;
        public bool Np2 { get { return Model == "NP2"; } }
        static readonly int[] Gains = { 50, 125, 250, 500, 1000, 1500, 2000, 3000 };

        public NpAdcCalibration(string gainFile, string adcFile, string expectedSerial, int apGain = 0, int lfpGain = 0)
        {
            ulong serial;
            if (!UInt64.TryParse(expectedSerial, NumberStyles.None, CultureInfo.InvariantCulture, out serial) || serial == 0)
                throw new InvalidDataException("Enter the physical probe serial number.");
            Serial = serial.ToString(CultureInfo.InvariantCulture);
            GainPath = Path.GetFullPath(gainFile); gainBytes = ReadFile(GainPath); GainHash = Hash(gainBytes);
            var gainLines = Lines(gainBytes); RequireSerial(gainLines, Serial);
            int columns = gainLines[1].Split(',').Length;
            if (columns != 2 && columns != 17) throw new InvalidDataException("Expected a factory NP2 or NP1 gain CSV.");
            Model = columns == 2 ? "NP2" : "NP1"; midpoint = Np2 ? 2048 : 512;
            int rows = gainLines.Length - 1;
            if (rows != 384 && (Np2 || rows != 960)) throw new InvalidDataException("Factory gain CSV requires 384 channels (or 960 NP1 electrodes).");
            int apIndex = Np2 ? 1 : Array.IndexOf(Gains, apGain) + 1;
            int lfpIndex = Np2 ? 1 : Array.IndexOf(Gains, lfpGain) + 9;
            if (!Np2 && (!Gains.Contains(apGain) || !Gains.Contains(lfpGain)))
                throw new InvalidDataException("Select the AP and LFP gains used during acquisition.");
            ApGain = Np2 ? 0 : apGain; LfpGain = Np2 ? 0 : lfpGain;
            var factors = new double[rows, columns - 1];
            for (int row = 0; row < rows; row++) {
                var fields = gainLines[row + 1].Split(',');
                if (fields.Length != columns || Integer(fields[0]) != row) throw new InvalidDataException("Gain CSV indices must be complete, ordered and unique.");
                for (int col = 1; col < columns; col++) {
                    double value;
                    if (!Double.TryParse(fields[col].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                        Double.IsNaN(value) || Double.IsInfinity(value) || value <= 0)
                        throw new InvalidDataException("Gain corrections must be finite positive numbers.");
                    factors[row, col - 1] = value;
                }
            }
            double electrodeAp = rows == 960 ? UniformElectrodeGain(factors, apIndex - 1) : 0;
            double electrodeLfp = rows == 960 ? UniformElectrodeGain(factors, lfpIndex - 1) : 0;
            var thresholds = new int[32]; var offsets = new int[32];
            if (!String.IsNullOrWhiteSpace(adcFile)) {
                if (Np2) throw new InvalidDataException("NP2 factory calibration uses the gain CSV; NP1 ADC files cannot be applied to NP2.");
                AdcPath = Path.GetFullPath(adcFile); adcBytes = ReadFile(AdcPath); AdcHash = Hash(adcBytes);
                var adcLines = Lines(adcBytes); RequireSerial(adcLines, Serial);
                if (adcLines.Length != 33) throw new InvalidDataException("Factory ADC CSV requires exactly 32 ADC rows.");
                int[] maxima = { 31, 31, 7, 3, 3, 15, 1023, 1023 };
                for (int row = 0; row < 32; row++) {
                    var fields = adcLines[row + 1].Split(',');
                    if (fields.Length != 9 || Integer(fields[0]) != row) throw new InvalidDataException("ADC CSV indices must be complete, ordered and unique.");
                    for (int col = 1; col < 9; col++) {
                        int value = Integer(fields[col]);
                        if (value < 0 || value > maxima[col - 1]) throw new InvalidDataException("Factory ADC parameter is outside its supported range.");
                    }
                    offsets[row] = Integer(fields[7]); thresholds[row] = Integer(fields[8]);
                }
                DigitalAdc = true;
            }
            // Shared LUTs avoid floating point and allocations on acquisition threads.
            var tables = new Dictionary<string, short[]>();
            for (int ch = 0; ch < 384; ch++) {
                int adc = (ch / 24) * 2 + (ch & 1);
                int threshold = DigitalAdc ? thresholds[adc] : midpoint * 2 - 1;
                int offset = DigitalAdc ? offsets[adc] : 0;
                ap[ch] = Table(tables, rows == 960 ? electrodeAp : factors[ch, apIndex - 1], threshold, offset);
                lfp[ch] = Np2 ? ap[ch] : Table(tables, rows == 960 ? electrodeLfp : factors[ch, lfpIndex - 1], threshold, offset);
            }
        }
        static double UniformElectrodeGain(double[,] factors, int column) {
            var values = new HashSet<double>();
            for (int row = 0; row < factors.GetLength(0); row++) if (factors[row, column] != 1.0) values.Add(factors[row, column]);
            if (values.Count > 1) throw new InvalidDataException("Nonuniform 960-electrode gain tables require confirmed channel-to-site routing; they cannot be guessed.");
            return values.Count == 0 ? 1.0 : values.First(); // Factory reference contacts have factor 1.
        }
        short[] Table(Dictionary<string, short[]> tables, double factor, int threshold, int offset) {
            string key = factor.ToString("R", CultureInfo.InvariantCulture) + ":" + threshold + ":" + offset;
            short[] table;
            if (tables.TryGetValue(key, out table)) return table;
            table = new short[midpoint * 2];
            for (int raw = 0; raw < table.Length; raw++) {
                double corrected = Math.Round((midpoint - (raw > threshold ? raw - offset : raw)) * factor, MidpointRounding.AwayFromZero);
                if (corrected < Int16.MinValue || corrected > Int16.MaxValue) throw new InvalidDataException("Calibration exceeds int16 output range; clipping is not permitted.");
                table[raw] = (short)corrected;
            }
            tables.Add(key, table); return table;
        }
        public short Correct(short nominal, int channel, bool isLfp = false) {
            int raw = midpoint - nominal;
            if (channel < 0 || channel >= 384 || raw < 0 || raw >= midpoint * 2 || (Np2 && isLfp))
                throw new InvalidDataException("Calibration input must be uncorrected signed nominal ADC counts in the selected band.");
            return (isLfp ? lfp : ap)[channel][raw];
        }
        public void CorrectBand(short[] samples, int frames, bool isLfp = false) {
            if (frames < 0 || samples == null || frames > samples.Length / 384) throw new ArgumentException("Invalid calibration frame extent.");
            for (int i = 0; i < frames * 384; i++) samples[i] = Correct(samples[i], i % 384, isLfp);
        }
        internal static byte[] ReadFile(string path) {
            using(var file=File.OpenRead(path)) {
                if(file.Length==0 || file.Length>2*1024*1024)throw new InvalidDataException("Calibration CSV is empty or too large.");
                var bytes=new byte[(int)file.Length];int read=0;
                while(read<bytes.Length){int got=file.Read(bytes,read,bytes.Length-read);if(got==0)throw new EndOfStreamException("Calibration CSV is truncated.");read+=got;}
                return bytes;
            }
        }
        static string[] Lines(byte[] bytes) {
            using (var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff'))) {
                var lines = new List<string>(); string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
                if (lines.Count < 2) throw new InvalidDataException("Calibration CSV is truncated.");
                return lines.ToArray();
            }
        }
        static void RequireSerial(string[] lines, string serial) {
            ulong value;
            if (!UInt64.TryParse(lines[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) || value.ToString(CultureInfo.InvariantCulture) != serial)
                throw new InvalidDataException("Calibration file serial does not match the selected physical probe.");
        }
        static int Integer(string text) { int value; if (!Int32.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new InvalidDataException("Invalid integer in calibration CSV."); return value; }
        internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        internal void SaveFiles(string folder, int probe) {
            File.WriteAllBytes(Path.Combine(folder, "probe" + probe + "_gain.csv"), gainBytes);
            if (adcBytes != null) File.WriteAllBytes(Path.Combine(folder, "probe" + probe + "_adc.csv"), adcBytes);
        }
    }

    public sealed class NpCalibrationSet
    {
        public const string ManifestName = "adc_calibration.json";
        readonly SortedDictionary<int, NpAdcCalibration> probes = new SortedDictionary<int, NpAdcCalibration>();
        public int Count { get { return probes.Count; } }
        public IEnumerable<int> Ports { get { return probes.Keys; } }
        public void Set(int port, NpAdcCalibration profile) { if (port < 1 || port > 3 || profile == null) throw new ArgumentException("Invalid calibration connector."); probes[port] = profile; }
        public void Remove(int port) { probes.Remove(port); }
        public NpAdcCalibration Get(int port) { NpAdcCalibration profile; return probes.TryGetValue(port, out profile) ? profile : null; }
        public NpCalibrationSet Copy() { var copy = new NpCalibrationSet(); foreach (var item in probes) copy.Set(item.Key, item.Value); return copy; }
        public void RequireModel(bool np2, int mask) { foreach (var item in probes) if ((mask & (1 << (item.Key - 1))) != 0 && item.Value.Np2 != np2) throw new InvalidDataException("Calibration probe type does not match the recording."); }
        public void Correct(int probe, short[] ap, int apFrames, short[] lfp, int lfpFrames) {
            var profile = Get(probe); if (profile == null) return;
            profile.CorrectBand(ap, apFrames); if (lfpFrames != 0) profile.CorrectBand(lfp, lfpFrames, true);
        }
        public void Save(string folder) {
            if (Count == 0) return;
            string path = Path.Combine(folder, ManifestName);
            if (File.Exists(path)) throw new IOException("Calibration provenance already exists; a fresh output is required.");
            string directory = Path.Combine(folder, "calibration");
            if(Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())throw new IOException("Existing calibration inputs are never overwritten.");
            Directory.CreateDirectory(directory);
            var entries = new List<object>();
            foreach (var item in probes) {
                var p = item.Value; p.SaveFiles(directory, item.Key);
                entries.Add(new { probe = item.Key, serial = p.Serial, model = p.Model, ap_gain = p.ApGain, lfp_gain = p.LfpGain,
                    gain_sha256 = p.GainHash, adc_sha256 = p.AdcHash, digital_adc_correction = p.DigitalAdc });
            }
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(new { schema_version = 1, format = "neuropixels.factory_calibration",
                sample_encoding = "signed_calibrated_nominal_adc_counts", polarity = "midpoint_minus_raw_once", rounding = "nearest_away_from_zero",
                raw_preserved = true, hardware_adc_trims_applied = false, hardware_trim_scope = "this_host_pipeline_only; acquisition_trim_state_unverified", identity_evidence = "operator_supplied_expected_probe_serial",
                stage = "host_digital_correction_before_DAT_and_preview", probes = entries }), new UTF8Encoding(false));
        }
        public static NpCalibrationSet Read(string folder) {
            string path = Path.Combine(folder, ManifestName); if (!File.Exists(path)) return null;
            var json = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 };
            var document = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (Convert.ToInt32(document["schema_version"]) != 1 || (string)document["format"] != "neuropixels.factory_calibration") throw new InvalidDataException("Unsupported calibration provenance.");
            var result = new NpCalibrationSet();
            foreach (Dictionary<string, object> entry in (IEnumerable)document["probes"]) {
                int port = Convert.ToInt32(entry["probe"]);
                if (port < 1 || port > 3 || result.Get(port) != null) throw new InvalidDataException("Invalid/duplicate calibration connector.");
                string root = Path.Combine(folder, "calibration");
                var profile = new NpAdcCalibration(Path.Combine(root, "probe" + port + "_gain.csv"), entry["adc_sha256"] == null ? null : Path.Combine(root, "probe" + port + "_adc.csv"),
                    (string)entry["serial"], Convert.ToInt32(entry["ap_gain"]), Convert.ToInt32(entry["lfp_gain"]));
                if (profile.GainHash != (string)entry["gain_sha256"] || profile.AdcHash != (string)entry["adc_sha256"] || profile.Model != (string)entry["model"])
                    throw new InvalidDataException("Saved factory calibration changed; correction refused.");
                result.Set(port, profile);
            }
            if (result.Count == 0) throw new InvalidDataException("Empty calibration provenance."); return result;
        }
    }

    public static class NpCalibrationDat
    {
        // Output is never the input; only a successful complete pass promotes DAT.
        public static void Correct(string input, string output, NpCalibrationSet calibration, int[] ports, bool lfp = false, bool legacyUnsigned = false, Func<bool> cancelled = null) {
            calibration=calibration==null?null:calibration.Copy(); ports=ports==null?null:(int[])ports.Clone();
            input = Path.GetFullPath(input); output = Path.GetFullPath(output);
            if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output + ".partial") || File.Exists(output + ".calibration.json") || Directory.Exists(output + ".calibration")) throw new IOException("Choose a fresh corrected DAT output.");
            if (calibration == null || ports == null || ports.Length < 1 || ports.Length > 3 || ports.Distinct().Count() != ports.Length || ports.Any(p => calibration.Get(p) == null)) throw new InvalidDataException("Every DAT probe needs an explicit calibration and channel order.");
            string inputFolder = Path.GetDirectoryName(input);
            if (File.Exists(input + ".calibration.json") || new[] { inputFolder, Path.GetDirectoryName(inputFolder) }.Where(p => p != null).Any(p => File.Exists(Path.Combine(p, NpCalibrationSet.ManifestName))))
                throw new InvalidDataException("This DAT already has calibration provenance; double correction is refused.");
            if(!lfp)foreach(string directory in new[]{inputFolder,Path.GetDirectoryName(inputFolder)}.Where(p=>p!=null)) {
                string metadata=Path.Combine(directory,"recording_settings.json");if(!File.Exists(metadata))continue;
                var document=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(metadata));object outputPart,upsampled;
                if(document.TryGetValue("output",out outputPart) && ((Dictionary<string,object>)outputPart).TryGetValue("ap_upsampled_15_to_20",out upsampled) && upsampled is bool && (bool)upsampled)
                    throw new InvalidDataException("Resampled AP DAT cannot reproduce pre-resampling calibration; decode its original RAW instead.");
            }
            int rowBytes = checked(ports.Length * 768);
            string sourceHash;
            using (var source = File.OpenRead(input)) using(var hasher=SHA256.Create()) {
                if (source.Length == 0 || source.Length % rowBytes != 0) throw new InvalidDataException("DAT is empty or has a partial row for the selected probe order.");
                var bytes = new byte[rowBytes * 256]; var values = new short[bytes.Length / 2];
                using (var destination = new FileStream(output + ".partial", FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    while (source.Position < source.Length) {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException("Calibration cancelled; output remains partial.");
                        int count=(int)Math.Min(bytes.Length,source.Length-source.Position),read=0;
                        while(read<count){int got=source.Read(bytes,read,count-read);if(got==0)throw new EndOfStreamException("Truncated DAT read.");read+=got;}
                        hasher.TransformBlock(bytes,0,count,bytes,0);
                        Buffer.BlockCopy(bytes, 0, values, 0, count);
                        for (int at = 0; at < count / 2; at++) {
                            int port = ports[(at / 384) % ports.Length]; var profile = calibration.Get(port);
                            short nominal = values[at];
                            if (legacyUnsigned) nominal = checked((short)((profile.Np2 ? 2048 : 512) - (ushort)nominal));
                            values[at] = profile.Correct(nominal, at % 384, lfp);
                        }
                        Buffer.BlockCopy(values, 0, bytes, 0, count); destination.Write(bytes, 0, count);
                    }
                    destination.Flush(true);
                }
                hasher.TransformFinalBlock(new byte[0],0,0);sourceHash=BitConverter.ToString(hasher.Hash).Replace("-", "");
            }
            if(cancelled!=null && cancelled())throw new OperationCanceledException("Calibration cancelled; output remains partial.");
            calibration.Save(output + ".calibration");
            string receipt = output + ".calibration.json";
            File.WriteAllText(receipt, new JavaScriptSerializer().Serialize(new { schema_version = 1, source_dat = input, source_sha256=sourceHash, source_encoding = legacyUnsigned ? "unsigned_raw_adc_codes" : "signed_nominal_adc_counts",
                output_encoding = "signed_calibrated_nominal_adc_counts", probes = ports, band = lfp ? "LFP" : "AP", factory_gain_hashes = ports.Select(p => calibration.Get(p).GainHash).ToArray(),
                factory_adc_hashes = ports.Select(p => calibration.Get(p).AdcHash).ToArray(), hardware_adc_trims_applied = false }), new UTF8Encoding(false));
            File.Move(output + ".partial", output);
        }
    }
}
