using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CE32_console
{
    // Native V1 v8 media, not neural samples. Mirrors the strict firmware
    // Tools/V1Capture/UsbCamera/extract_media.py; never remap or repair bytes.
    internal static class NpMediaExport
    {
        internal const int ChunkBytes = 65536, HeaderOffset = 61632, FrameBytes = 160000;
        static uint U(byte[] b, int offset) { return BitConverter.ToUInt32(b, offset); }
        static ulong Q(byte[] b, int offset) { return BitConverter.ToUInt64(b, offset); }
        static ulong Number(Dictionary<string, object> m, string key) { return Convert.ToUInt64(m[key], CultureInfo.InvariantCulture); }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
        static string Magic(byte[] b, int offset) { return Encoding.ASCII.GetString(b, offset, 8); }
        internal static ulong AdvanceCameraTick(ulong continuous,uint previous,uint next){uint delta=unchecked(next-previous);Require(delta>0&&delta<0x80000000U,"Camera timestamp stopped or moved backward; do not invent timing");return checked(continuous+delta);}
        internal static bool IsTyped(byte[] h) { return U(h, 8) == 7 || U(h, 8) == 8; }
        static void Header(byte[] b, string magic)
        {
            Require(b.Length == 512 && Magic(b, 0) == magic && U(b, 508) == NpDownloadStream.Crc(b, 0, 508),
                "Invalid media descriptor/footer signature or CRC.");
        }
        internal static void Validate(byte[] h, byte[] f, long start, long end)
        { ValidateCore(h,f,start,end,false); }
        internal static void ValidateCommitted(byte[] h,byte[] f,long start,long end)
        { ValidateCore(h,f,start,end,true); }
        static void ValidateCore(byte[] h,byte[] f,long start,long end,bool allowIncomplete)
        {
            Header(h, "NPSDLOG1"); Header(f, "NPSDEND1");
            NpV1StoreBinding.Validate(h,f);
            Require(IsTyped(h) && U(h, 16) == 0 && U(h, 24) == 0,
                "Only dedicated V1 v7/v8 media recordings are supported; mixed NP data must not be decoded as media or Intan.");
            foreach (int p in new[] { 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 80, 96, 112, 116, 120 })
                Require(U(h, p) == U(f, p), "Media footer/session geometry mismatch.");
            uint hs = U(h, 48), chunks = U(f, 68);
            Require(start >= 8192 && start % 8192 == 0 && U(h, 40) == start && U(h, 12) != 0 &&
                U(h, 32) == 15360 && U(h, 36) == 128 && U(h, 80) == 2 && U(h, 96) == HeaderOffset &&
                (hs == 8192 || hs == 16384) && (start + hs) % 16384 == 0 && end <= U(h, 44) &&
                (allowIncomplete?end>=checked(start+hs+(long)chunks*128):end==checked(start+hs+(long)chunks*128) && chunks>0) && chunks % 8 == 0,
                "Invalid media committed extent/chunk geometry.");
            Require(U(h, 112) <= 58 &&
                ((U(h, 8) == 7 && U(h, 112) > 0 && U(h, 116) == 0 && U(h, 120) == 0) ||
                 (U(h, 8) == 8 && U(h, 116) == 200000 && U(h, 120) == 1)),
                "Unsupported media descriptor rates/channels.");
            Require(allowIncomplete?U(f,60)>=1 && U(f,60)<=4:U(f,60)==3 && U(f,64)==0,"Unsupported media recording state or incomplete strict export.");
        }
        static void Exact(Stream s, byte[] b, int count = -1, Func<bool> cancelled = null)
        {
            if (count < 0) count = b.Length;
            int at = 0; while (at < count) { if(cancelled!=null && cancelled())throw new OperationCanceledException("Media prefix export cancelled.");int n = s.Read(b, at, count - at); if (n == 0) throw new EndOfStreamException("Truncated media recording."); at += n; }
        }
        static byte[] RecoverySector(Stream card,long lba,Func<bool> cancelled){var bytes=new byte[512];card.Position=checked(lba*512);Exact(card,bytes,-1,cancelled);return bytes;}
        static void Zero(byte[] b, int first, int end)
        {
            for (int n = first; n < end; n++) Require(b[n] == 0, "Nonzero media payload padding.");
        }
        static FileStream Create(string directory, string name)
        { return new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 131072); }

        // NPMEDX1 uses only bytes 128..295; the existing sector CRC covers it.
        // Keep names/semantics aligned with the firmware's extract_media.py.
        internal static Dictionary<string, object> Metadata(byte[] sector)
        {
            if (Magic(sector, 128) != "NPMEDX1\0") return null;
            var result = new Dictionary<string, object> { { "version", U(sector, 136) } };
            string[] fields = { "flags", "start_tick", "stop_tick", "start_clock_tick", "start_clock_generation",
                "camera_hz", "camera_source_completed", "camera_saved", "camera_first_id", "camera_last_id",
                "camera_first_tick", "camera_last_tick", "camera_fault", "camera_cadence_misses", "camera_baseline_misses",
                "camera_spi_sr", "camera_spi_error", "camera_dma_error", "mic_hz", "mic_origin", "mic_fault", "mic_overruns",
                "seed_reg0", "effective_reg0", "reg1", "auto_enabled", "mean", "stop_clock_tick", "stop_clock_generation",
                "start_uncertainty_us", "stop_uncertainty_us" };
            int[] offsets = { 140,144,148,152,156,168,172,176,180,184,188,192,196,200,204,208,212,216,220,224,
                244,248,252,256,260,264,268,272,276,288,292 };
            for (int i = 0; i < fields.Length; i++) result[fields[i]] = U(sector, offsets[i]);
            result["start_clock_utc_us"] = Q(sector, 160); result["stop_clock_utc_us"] = Q(sector, 280);
            result["mic_produced"] = Q(sector, 228); result["mic_saved"] = Q(sector, 236);
            return result;
        }

        internal static ulong? UtcAt(Dictionary<string, object> metadata, uint tick, bool stop)
        {
            if (metadata == null || Number(metadata, "version") != 1 || (Number(metadata, "flags") & (stop ? 2UL : 1UL)) == 0) return null;
            string prefix = stop ? "stop" : "start";
            uint elapsed = unchecked(tick - (uint)Number(metadata, prefix + "_clock_tick"));
            ulong utc = Number(metadata, prefix + "_clock_utc_us"), uncertainty = Number(metadata, prefix + "_uncertainty_us");
            if (elapsed >= 0x80000000U || Number(metadata, prefix + "_clock_generation") == 0 ||
                utc < 1577836800000000UL || utc > 4102444800000000UL || uncertainty < 1000 || uncertainty > 5000000) return null;
            return utc + elapsed * 1000UL;
        }

        internal static Dictionary<string, object> ClockMetadata(byte[] sector, uint session)
        {
            if (Magic(sector, 0) != "N1SDUTC1") return null;
            var result = new Dictionary<string, object>();
            string[] fields = { "version", "session", "flags", "generation", "uncertainty_us", "tick", "before_tick", "after_tick", "ap_hz", "tick_quantum_us" };
            int[] offsets = { 8,12,16,20,24,28,64,68,72,76 };
            for (int i = 0; i < fields.Length; i++) result[fields[i]] = U(sector, offsets[i]);
            result["utc_us"] = Q(sector, 32); result["scheduled_utc_us"] = Q(sector, 40);
            result["before_utc_us"] = Q(sector, 48); result["after_utc_us"] = Q(sector, 56);
            bool valid = U(sector, 508) == NpDownloadStream.Crc(sector, 0, 508) && U(sector, 8) == 1 &&
                U(sector, 12) == session && (U(sector, 16) & ~7U) == 0 && U(sector, 76) == 1000;
            result["metadata_valid"] = valid;
            result["anchor_valid"] = valid && (U(sector, 16) & 1) != 0 && U(sector, 20) != 0 &&
                Q(sector, 32) >= 1577836800000000UL && Q(sector, 32) <= 4102444800000000UL &&
                U(sector, 24) >= 1000 && U(sector, 24) <= 5000000;
            result["error"] = valid ? null : "Invalid legacy clock CRC/identity/format";
            return result;
        }

        static void Qualify(byte[] h, byte[] f, Dictionary<string, object> initial, Dictionary<string, object> final,
            Dictionary<string, object> report, HashSet<uint> misses)
        {
            var reasons = new List<string>();
            ulong frames = Number(report, "camera_frames"), samples = Number(report, "microphone_samples");
            uint cameraHz=U(h,112);decimal cameraPeriod=cameraHz==0?0:1000m/cameraHz;
            if ((string)report["status"] != "validated transport") reasons.Add("Transport export is incomplete");
            if (U(f, 60) != 3 || U(f, 64) != 0) reasons.Add("Footer reports an incomplete/failed recording");
            if (Number(report, "camera_frame_gaps") != 0) reasons.Add("Camera frame IDs have gaps");
            if (Number(report, "camera_timing_irregularities") != 0) reasons.Add("Camera timestamp interval exceeds the 1 ms quantization bound");
            if (Number(report, "camera_timing_phase_errors") != 0) reasons.Add("Camera timestamp phase drifts beyond 1 ms from the first frame");
            if (report.ContainsKey("camera_invalid_frames") && Number(report, "camera_invalid_frames") != 0) reasons.Add("Camera contains explicitly invalid image slots");
            if (frames > 0 && (cameraHz==0 || cameraHz>50)) reasons.Add("Unsupported SD camera rate");
            string qualification;
            if (initial == null && final == null)
            {
                qualification = reasons.Count == 0 ? "transport-only" : "failed";
                reasons.Add("Legacy record has no capture qualification metadata");
            }
            else
            {
                if (initial == null || final == null || Number(initial, "version") != 1 || Number(final, "version") != 1)
                    reasons.Add("Missing/unsupported NPMEDX1 descriptor/footer metadata");
                else
                {
                    uint flags = (uint)Number(final, "flags"), initialFlags = (uint)Number(initial, "flags");
                    if ((initialFlags & 4) != 0 || (flags & 4) == 0 || ((flags | initialFlags) & ~31U) != 0) reasons.Add("Invalid metadata role/flags");
                    if (((flags ^ initialFlags) & 24) != 0) reasons.Add("Requested streams changed");
                    // Descriptor is written before ARM: only the final snapshot owns capture timing.
                    uint start = (uint)Number(final, "start_tick"), stop = (uint)Number(final, "stop_tick"), span = unchecked(stop - start);
                    ulong? startUtc = UtcAt(final, start, false), stopUtc = UtcAt(final, stop, true);
                    if (span == 0 || span >= 0x80000000U) reasons.Add("Invalid capture start/stop chronology");
                    if (!startUtc.HasValue || !stopUtc.HasValue) reasons.Add("Capture lacks valid START/STOP UTC anchors");
                    else if (Math.Abs((decimal)stopUtc.Value - startUtc.Value - span * 1000UL) >
                        Number(final, "start_uncertainty_us") + Number(final, "stop_uncertainty_us")) reasons.Add("START/STOP UTC anchors disagree beyond their uncertainty");
                    report["capture_start_utc_us"] = startUtc; report["capture_stop_utc_us"] = stopUtc;
                    if ((flags & 8) != 0)
                    {
                        if (frames == 0 || cameraHz==0 || Number(final, "camera_hz") != cameraHz) reasons.Add("Requested camera stream/rate is missing");
                        if (Number(final, "camera_source_completed") != frames || Number(final, "camera_saved") != frames) reasons.Add("Camera source/saved/exported frame counts differ");
                        if (Number(final, "camera_fault") != 0 || Number(final, "camera_spi_error") != 0 || Number(final, "camera_dma_error") != 0) reasons.Add("Camera capture fault is latched");
                        uint baseline = (uint)Number(final, "camera_baseline_misses");
                        if (Number(final, "camera_cadence_misses") != baseline || misses.Any(value => value != baseline)) reasons.Add("Camera cadence misses occurred");
                        if (frames > 0)
                        {
                            foreach (string edge in new[] { "first", "last" })
                            {
                                string prefix = "camera_" + edge;
                                if (Number(report, prefix + "_id") != Number(final, prefix + "_id") || Number(report, prefix + "_tick") != Number(final, prefix + "_tick")) reasons.Add("Camera first/last metadata does not match payload");
                                if ((decimal)unchecked((uint)Number(report, prefix + "_tick") - start) > (decimal)span + cameraPeriod+1) reasons.Add("Camera timestamp is outside capture bounds");
                            }
                            uint first = unchecked((uint)Number(report, "camera_first_tick") - start), last = unchecked((uint)Number(report, "camera_last_tick") - start);
                            // Mirror firmware extract_media.py: warmup + first raster,
                            // one in-flight STOP raster, and 1-ms tick quantization.
                            if (first > cameraPeriod*2+1) reasons.Add("Camera does not cover capture startup");
                            if (Math.Abs((decimal)last - span) > cameraPeriod+1) reasons.Add("Camera does not cover capture STOP endpoint");
                        }
                        if (Math.Abs((decimal)frames * cameraPeriod - span) > cameraPeriod+1) reasons.Add("Camera frame count does not cover the capture duration");
                    }
                    else if (frames > 0 || U(h, 112) != 0) reasons.Add("Unexpected camera data without requested-stream flag");
                    if ((flags & 16) != 0)
                    {
                        if (samples == 0 || U(h, 116) != 200000 || Number(final, "mic_hz") != 200000) reasons.Add("Requested microphone stream/rate is missing");
                        if (Number(final, "mic_produced") != samples || Number(final, "mic_saved") != samples) reasons.Add("Microphone produced/saved/exported sample counts differ");
                        if (report["microphone_start_ms"] == null || Number(final, "mic_origin") != Number(report, "microphone_start_ms")) reasons.Add("Microphone origin does not match payload");
                        if (Number(final, "mic_fault") != 0 || Number(final, "mic_overruns") != 0) reasons.Add("Microphone capture fault/overrun is latched");
                        if (report["microphone_start_ms"] != null && unchecked((uint)Number(report, "microphone_start_ms") - start) > span) reasons.Add("Microphone origin is outside capture bounds");
                        if (report["microphone_start_ms"] != null)
                        {
                            uint origin = unchecked((uint)Number(report, "microphone_start_ms") - start);
                            if (origin > 1) reasons.Add("Microphone origin is not aligned with capture arm");
                            // STOP is producer stop/request, not SD-drain completion.
                            // Two 1-ms ticks plus one 5-us sample; no DMA/chunk slack.
                            if (Math.Abs((decimal)origin * 1000 + (decimal)samples * 5 - (decimal)span * 1000) > 2005)
                                reasons.Add("Microphone sample count does not cover the 200 kHz capture duration");
                        }
                    }
                    else if (samples > 0 || U(h, 116) != 0) reasons.Add("Unexpected microphone data without requested-stream flag");
                    if ((flags & 24) == 0) reasons.Add("No media stream was requested");
                }
                qualification = reasons.Count == 0 ? "lossless-qualified" : "failed";
            }
            report["qualification"] = qualification; report["capture_qualification"] = qualification;
            report["capture_lossless_qualified"] = qualification == "lossless-qualified";
            report["qualification_reasons"] = reasons.Distinct().ToArray(); report["usable_prefix_only"] = qualification == "failed";
            report["qualification_scope"] = "Frame/sample continuity and START/source-STOP duration with bounded warmup/frame/tick tolerances; not a source-payload checksum proof";
            report["source_stop_timing_assumption"] = "STOP marks producer stop/request; older post-drain STOP metadata can fail duration checks without proving native payload loss";
            // This is exact nominal-timing eligibility, not playback permission.
            // A derived MP4 may still play the stored prefix with a timing warning.
            report["camera_nominal_video_eligible"] = Number(report, "camera_frame_gaps") == 0 && Number(report, "camera_timing_irregularities") == 0 && Number(report, "camera_timing_phase_errors") == 0;
        }

        // Unsigned LE16 ADC -> signed PCM16, sample-exact: flip bit 15 only.
        // Do not use the legacy high-pass/resampled audio rendering here.
        internal static void WriteMicrophoneWav(string input, string output, long samples, Func<bool> cancelled, Action<int> progress)
        {
            using (var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(samples > 0 && source.Length == checked(samples * 2) && source.Length <= uint.MaxValue - 36L,
                    "Microphone sample count mismatch or WAV exceeds the RIFF size limit; native file retained.");
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(destination))
                {
                    uint bytes = checked((uint)source.Length);
                    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(bytes + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16U); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(200000U); writer.Write(400000U);
                    writer.Write((ushort)2); writer.Write((ushort)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                    var block = new byte[131072]; int lastProgress = -1;
                    while (source.Position < source.Length)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        int count = (int)Math.Min(block.Length, source.Length - source.Position), at = 0;
                        while (at < count) { int n = source.Read(block, at, count - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                        for (int n = 1; n < count; n += 2) block[n] ^= 0x80;
                        writer.Write(block, 0, count);
                        int percent = (int)(source.Position * 100 / source.Length);
                        if (percent != lastProgress) { lastProgress = percent; if (progress != null) progress(percent); }
                    }
                }
            }
        }

        static void VerifyMicrophoneWav(string input, string output, long samples, Func<bool> cancelled, Action<int> progress)
        {
            using (var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var wav = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(samples > 0 && source.Length == checked(samples * 2) && source.Length <= uint.MaxValue - 36L && wav.Length == source.Length + 44,
                    "Existing WAV length differs from native samples; nothing overwritten.");
                var header = new byte[44]; Exact(wav, header);
                Require(Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && U(header, 4) == source.Length + 36 &&
                    Encoding.ASCII.GetString(header, 8, 8) == "WAVEfmt " && U(header, 16) == 16 &&
                    BitConverter.ToUInt16(header, 20) == 1 && BitConverter.ToUInt16(header, 22) == 1 &&
                    U(header, 24) == 200000 && U(header, 28) == 400000 && BitConverter.ToUInt16(header, 32) == 2 &&
                    BitConverter.ToUInt16(header, 34) == 16 && Encoding.ASCII.GetString(header, 36, 4) == "data" && U(header, 40) == source.Length,
                    "Existing WAV format differs from sample-exact 200 kHz PCM; nothing overwritten.");
                var raw = new byte[131072]; var pcm = new byte[raw.Length]; int lastProgress = -1;
                while (source.Position < source.Length)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    int count = (int)Math.Min(raw.Length, source.Length - source.Position);
                    Exact(source, raw, count); Exact(wav, pcm, count);
                    for (int n = 0; n < count; n++)
                        Require(pcm[n] == (n % 2 == 0 ? raw[n] : (byte)(raw[n] ^ 0x80)), "Existing WAV samples differ from native data; nothing overwritten.");
                    int percent = (int)(source.Position * 100 / source.Length);
                    if (percent != lastProgress) { lastProgress = percent; if (progress != null) progress(percent); }
                }
            }
        }

        internal static string TimingWarning(Dictionary<string, object> native)
        {
            if (Number(native, "camera_frames") == 0) return null;
            bool irregular = new[] { "camera_frame_gaps", "camera_timing_irregularities", "camera_timing_phase_errors" }
                .Any(key => native.ContainsKey(key) && Number(native, key) != 0);
            string warning = null;
            if (irregular || (native.ContainsKey("camera_nominal_video_eligible") && !Convert.ToBoolean(native["camera_nominal_video_eligible"])))
                warning = "Timing warning: camera gaps or irregular timestamps are present. MP4 plays saved frames at nominal " +
                    Number(native, "camera_requested_or_effective_hz") + " fps, not the original timestamp timeline. Use camera.timestamps.csv; lossless qualification is unchanged.";
            if (native.ContainsKey("camera_invalid_frames") && Number(native, "camera_invalid_frames") != 0)
                warning = (warning == null ? "" : warning + " ") + "Image warning: best-effort held-image playback includes " +
                    Number(native, "camera_invalid_frames") + " explicitly invalid camera slots. Native frames/timestamps are retained; lossless qualification is separate.";
            return warning;
        }

        internal static string ConvertFiles(string directory, Func<bool> cancelled, Action<int, string> progress)
        {
            var native = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(directory, "recording.json")));
            Require((string)native["status"] == "validated transport" && (Convert.ToInt32(native["format_version"]) == 7 || Convert.ToInt32(native["format_version"]) == 8),
                "Conversion requires a validated native V1 media export.");
            long samples = Convert.ToInt64(native["microphone_samples"]), frames = Convert.ToInt64(native["camera_frames"]);
            int rate = Convert.ToInt32(native["camera_requested_or_effective_hz"]);
            Require(samples >= 0 && (samples == 0 || Convert.ToInt32(native["microphone_hz"]) == 200000), "Unsupported microphone rate.");
            Require(new FileInfo(Path.Combine(directory, "microphone.dat")).Length == checked(samples * 2), "Microphone length differs from the native report.");
            string camera = Path.Combine(directory, "camera.raw"), wav = Path.Combine(directory, "microphone.wav"), mp4 = Path.Combine(directory, "camera.mp4");
            Require(frames >= 0 && new FileInfo(camera).Length == checked(frames * FrameBytes) && (frames == 0 || rate > 0 && rate <= 58),
                "Camera length/rate differs from the native report.");
            string partialWav = Path.Combine(directory, "microphone.partial.wav"), partialVideo = Path.Combine(directory, "camera.partial.mp4");
            string imageReport = Path.Combine(directory, "camera.image-recovery.csv"), partialImageReport = Path.Combine(directory, "camera.partial.image-recovery.csv");
            if (File.Exists(mp4)) throw new IOException("Converted MP4 already exists; nothing overwritten.");
            if (File.Exists(partialWav) || File.Exists(partialVideo) || File.Exists(imageReport) || File.Exists(partialImageReport)) throw new IOException("A previous/partial conversion exists; retain/move it before retrying.");
            bool reuseWav = File.Exists(wav); string warning = TimingWarning(native);
            long checkedFrames = 0, realignedFrames = 0, realignedRows = 0, heldFrames = 0, gapFrames = 0;
            var report = new Dictionary<string, object> {
                {"status", "converting"}, {"native_files_modified", false}, {"microphone_samples", samples}, {"microphone_hz", samples > 0 ? 200000 : 0},
                {"camera_frames", frames}, {"camera_hz", rate}, {"audio_resampling_or_filtering", false}, {"streams_muxed_or_truncated", false},
                {"video_timing", "Saved frames at nominal camera rate only; timestamp CSVs remain authoritative. No missing frames are invented."},
                {"video_color_processing", "BGGR native / RGGB after 180-degree rotation; RGB channel order. Camera tone v2: bounded luminance-shadow contrast, smoothed 99th-percentile white reference, at most 2x display exposure, sRGB curve. Flat scenes keep zero-based tone; no calibrated sensor black level or white balance; native pixels retained."},
                {"video_image_recovery", "Every row training marker and every pixel start/stop guard checked. Unique shifts within 32 bytes realigned for display only. Damaged/ambiguous frames hold the previous complete image, or black before the first valid image. No interpolation, native byte changes, frame removal or timestamp changes. Framing guards are not a pixel-value checksum."},
                {"image_recovery_report", "camera.image-recovery.csv: zero-based frame index; affected frames only; held images are not recovered sensor measurements"},
                {"timing_warning", warning}, {"native_timestamps_retained", true}, {"microphone_wav_reused", false}
            };
            bool durationChecked = native.ContainsKey("source_stop_timing_assumption");
            report["capture_qualification"] = durationChecked && native.ContainsKey("capture_qualification") ? native["capture_qualification"] : "transport-only (legacy export; duration coverage unverified)";
            report["capture_lossless_qualified"] = durationChecked && native.ContainsKey("capture_lossless_qualified") && Convert.ToBoolean(native["capture_lossless_qualified"]);
            foreach (string key in new[] { "qualification_reasons", "camera_invalid_frames", "camera_frame_gaps", "camera_timing_irregularities", "camera_timing_phase_errors", "camera_nominal_video_eligible" })
                if (native.ContainsKey(key)) report[key] = native[key];
            try
            {
                if (reuseWav)
                {
                    var estimate=new USB512_interface.NpProgressEstimate();
                    if(progress!=null)progress(0,"Verifying existing WAV · "+estimate.Percent(0));
                    VerifyMicrophoneWav(Path.Combine(directory, "microphone.dat"), wav, samples, cancelled,
                        p => { if (progress != null) progress(p * 20 / 100, "Verifying existing sample-exact WAV: " + p + "% · "+estimate.Percent(p)); });
                    report["microphone_wav_reused"] = true;
                }
                else if (samples > 0)
                {
                    var estimate=new USB512_interface.NpProgressEstimate();
                    if(progress!=null)progress(0,"Microphone WAV · "+estimate.Percent(0));
                    WriteMicrophoneWav(Path.Combine(directory, "microphone.dat"), partialWav, samples, cancelled,
                        p => { if (progress != null) progress(p * 20 / 100, "Microphone WAV: " + p + "% (200 kHz, sample-exact) · "+estimate.Percent(p)); });
                    File.Move(partialWav, wav);
                }
                if (frames > 0)
                {
                    var estimate=new USB512_interface.NpProgressEstimate();
                    if(progress!=null)progress(20,"Camera MP4 · "+estimate.Percent(0));
                    using (var imageLog = new StreamWriter(new FileStream(partialImageReport, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))) {
                    imageLog.WriteLine("frame_index,status,realigned_rows,invalid_rows");
                    WILD_VideoDecoder.DecodeHevcWithAudio(camera, rate, 200000, false, false,
                        p => { if (progress != null) progress(20 + p * 79 / 100, "Camera MP4: " + p + "% (saved frames only) · "+estimate.Percent(p)); }, partialVideo, cancelled,
                        (index, recovery) => {
                            checkedFrames++;
                            if (recovery.CorrectedRows != 0) { realignedFrames++; realignedRows += recovery.CorrectedRows; }
                            if (recovery.Held) heldFrames++;
                            if (recovery.Gap) gapFrames++;
                            if (recovery.Held || recovery.CorrectedRows != 0)
                                imageLog.WriteLine(index.ToString(CultureInfo.InvariantCulture) + "," + recovery.Status + "," + recovery.CorrectedRows + "," + recovery.InvalidRows);
                        });
                    }
                    File.Move(partialVideo, mp4);
                    File.Move(partialImageReport, imageReport);
                    if (realignedFrames != 0 || heldFrames != 0) {
                        string imageWarning = "Image recovery: " + realignedFrames + " frames realigned; " + heldFrames + " frames held. See camera.image-recovery.csv.";
                        report["image_warning"] = imageWarning;
                        warning = string.IsNullOrEmpty(warning) ? imageWarning : warning + " " + imageWarning;
                    }
                }
                report["status"] = "complete";
                if (progress != null) progress(100, "MP4/WAV complete; native files and timestamps retained. " + warning);
                return warning;
            }
            catch (Exception ex)
            {
                report["status"] = cancelled != null && cancelled() ? "cancelled" : "failed"; report["error"] = ex.Message;
                if (cancelled != null && cancelled()) throw new OperationCanceledException("Media conversion cancelled; native files retained.", ex);
                throw;
            }
            finally {
                report["camera_image_frames_checked"] = checkedFrames;
                report["camera_image_realigned_frames"] = realignedFrames;
                report["camera_image_realigned_rows"] = realignedRows;
                report["camera_image_held_frames"] = heldFrames;
                report["camera_image_gap_frames"] = gapFrames;
                File.WriteAllText(Path.Combine(directory, "conversion.json"), new JavaScriptSerializer().Serialize(report) + "\n", new UTF8Encoding(false));
            }
        }

        internal static void Export(Stream card, long start, long end, string output, Func<bool> cancelled, Action<int> progress)
        { ExportCore(card,start,end,output,cancelled,progress,false); }

        // Explicit read-only salvage. The original initial footer remains the
        // authority for committed_chunks=0; scanned chunks are never committed.
        internal static void ExportPowerInterruptedPrefix(Stream card,long start,long end,string output,Func<bool> cancelled,Action<int> progress)
        { ExportCore(card,start,end,output,cancelled,progress,true); }

        static void ExportCore(Stream card,long start,long end,string output,Func<bool> cancelled,Action<int> progress,bool recoverPowerCut)
        {
            if(recoverPowerCut) {
                if(cancelled!=null && cancelled())throw new OperationCanceledException("Media prefix export cancelled.");
                Require(card!=null && card.CanRead && card.CanSeek && start>=8192 && start<=uint.MaxValue && end<=uint.MaxValue && end>=start+6,
                    "Bounded readable V1 media source required for prefix export.");
                Require(card.Length/512>=start+6 && card.Length/512<=uint.MaxValue,"Invalid media prefix source capacity.");
                Require(!string.IsNullOrWhiteSpace(output) && !File.Exists(output),"A fresh media prefix export folder is required.");
            }
            byte[] h = recoverPowerCut?RecoverySector(card,start+3,cancelled):NpDownloadCatalog.Sector(card, start + 3), f = recoverPowerCut?RecoverySector(card,start+4,cancelled):NpDownloadCatalog.Sector(card, start + 4);
            ValidateCommitted(h, f, start, end);
            if(recoverPowerCut)Require(U(f,68)==0 && U(f,60)==1 && U(f,64)==0,"Media prefix recovery requires the original zero-count, fault-free initial footer.");
            else if (end > card.Length / 512) throw new InvalidDataException("Media extent exceeds source capacity.");
            if (Directory.Exists(output)) throw new IOException("Media export folder exists; choose a new destination.");
            Directory.CreateDirectory(output);
            uint committedChunks=U(f,68),session=U(h,12),hs=U(h,48);
            long bound=Math.Min(end,Math.Min((long)U(h,44),card.Length/512));
            uint chunks=recoverPowerCut?(uint)(Math.Max(0,bound-start-hs)/128):committedChunks;
            bool recordingComplete=!recoverPowerCut && chunks>0 && U(f,60)==3 && U(f,64)==0;
            byte[] oldClockSector = recoverPowerCut?RecoverySector(card,start+5,cancelled):NpDownloadCatalog.Sector(card, start + 5);
            var initialMetadata = Metadata(h); var finalMetadata = Metadata(f); var oldClock = ClockMetadata(oldClockSector, session);
            var clockAnchor = finalMetadata; string clockSource = finalMetadata == null ? null : "NPMEDX1.footer";
            if (clockAnchor == null && oldClock != null && (bool)oldClock["anchor_valid"])
            {
                clockAnchor = new Dictionary<string, object> { { "version", 1U }, { "flags", 1U }, { "start_clock_tick", oldClock["tick"] },
                    { "start_clock_generation", oldClock["generation"] }, { "start_clock_utc_us", oldClock["utc_us"] }, { "start_uncertainty_us", oldClock["uncertainty_us"] } };
                clockSource = "N1SDUTC1";
            }
            ulong samples = 0; uint frames = 0, invalidFrames = 0, padding = 0, gaps = 0, processed = 0;
            uint? firstFrameId = null, firstCameraTick = null; uint irregularTiming = 0, phaseErrors = 0;
            var cameraMisses = new HashSet<uint>(); var cameraIntervals = new Dictionary<string, uint>();
            uint? origin = null, previousFrame = null; uint frameId = 0, frameTick = 0, frameRate = 0, frameMissed = 0;
            ulong continuousCameraTick=0;uint previousCameraTick=0;
            int frameAt = 0; bool complete = false; string failure = null;
            Exception prefixError=null;string prefixBoundary="Bounded metadata reservation/source limit reached.";
            uint sensorRecords = 0, validImu = 0, validMag = 0, validEnv = 0, lastSensorValid = 0;
            var snapshots=new List<byte[]>();ulong analogRows=0;uint analogFinalValid=0;
            ulong? analogStartUtc=finalMetadata==null?null:UtcAt(clockAnchor,(uint)Number(finalMetadata,"start_tick"),false);
            var names = new[] { "camera.raw", "microphone.dat", "camera.timestamps.csv", "microphone.timestamps.csv", "misc.dat", "analogin.dat", "sensors.dat", "sensors.csv", "analogin.timestamps.csv" };
            try
            {
                using (var cam = Create(output, names[0] + ".partial"))
                using (var mic = Create(output, names[1] + ".partial"))
                using (var camTime = new StreamWriter(Create(output, names[2] + ".partial"), new UTF8Encoding(false)))
                using (var micTime = new StreamWriter(Create(output, names[3] + ".partial"), new UTF8Encoding(false)))
                using (var misc = Create(output, names[4] + ".partial"))
                using (var analog = Create(output, names[5] + ".partial"))
                using (var sensorBinary = Create(output, names[6] + ".partial"))
                using (var sensorCsv = Create(output, names[7] + ".partial"))
                using (var analogTimes = new StreamWriter(Create(output, names[8]+".partial"),new UTF8Encoding(false)))
                {
                    byte[] sensorColumns = Encoding.ASCII.GetBytes(NpCe64Export.SensorColumns + "\n"); sensorCsv.Write(sensorColumns, 0, sensorColumns.Length);
                    camTime.WriteLine("frame_index,device_frame_id,device_tick_ms_u32,camera_hz,device_missed_cadence,final_chunk,device_tick_ms_unwrapped_u64,utc_us,utc_source");
                    micTime.WriteLine("chunk,first_sample_u64,sample_count,microphone_hz,device_origin_ms_u32,device_collection_ms_u32,first_sample_time_ns_u64,end_sample_time_ns_u64,origin_utc_us,first_sample_utc_us,utc_source");
                    var prefix = new byte[1536]; card.Position = checked(start * 512); Exact(card, prefix,-1,recoverPowerCut?cancelled:null);
                    using (var parameters = Create(output, "CE_params.bin")) parameters.Write(prefix, 0, prefix.Length);
                    File.WriteAllBytes(Path.Combine(output, "descriptor.bin"), h); File.WriteAllBytes(Path.Combine(output, "footer.bin"), f);
                    File.WriteAllBytes(Path.Combine(output, "clock_anchor_sector5.bin"), oldClockSector);
                    var raw = new byte[ChunkBytes]; var raster = new byte[FrameBytes]; int lastProgress = -1;
                    if(!recoverPowerCut || chunks>0)card.Position = checked((start + hs) * 512);
                    for (uint sequence = 0; sequence < chunks; sequence++)
                    {
                        Action rollback=null;
                        if(recoverPowerCut) {
                            camTime.Flush();micTime.Flush();analogTimes.Flush();
                            Stream[] outputs={cam,mic,camTime.BaseStream,micTime.BaseStream,misc,analog,sensorBinary,sensorCsv,analogTimes.BaseStream};
                            long[] lengths=outputs.Select(stream=>stream.Position).ToArray();
                            var saved=new{samples,frames,invalidFrames,padding,gaps,processed,firstFrameId,firstCameraTick,irregularTiming,phaseErrors,origin,previousFrame,frameId,frameTick,frameRate,frameMissed,continuousCameraTick,previousCameraTick,frameAt,sensorRecords,validImu,validMag,validEnv,lastSensorValid};
                            int snapshotCount=snapshots.Count;uint[] misses=cameraMisses.ToArray();var intervals=cameraIntervals.ToArray();
                            rollback=()=>{
                                camTime.Flush();micTime.Flush();analogTimes.Flush();
                                for(int i=0;i<outputs.Length;i++){outputs[i].SetLength(lengths[i]);outputs[i].Position=lengths[i];}
                                samples=saved.samples;frames=saved.frames;invalidFrames=saved.invalidFrames;padding=saved.padding;gaps=saved.gaps;processed=saved.processed;
                                firstFrameId=saved.firstFrameId;firstCameraTick=saved.firstCameraTick;irregularTiming=saved.irregularTiming;phaseErrors=saved.phaseErrors;origin=saved.origin;previousFrame=saved.previousFrame;
                                frameId=saved.frameId;frameTick=saved.frameTick;frameRate=saved.frameRate;frameMissed=saved.frameMissed;continuousCameraTick=saved.continuousCameraTick;previousCameraTick=saved.previousCameraTick;frameAt=saved.frameAt;
                                sensorRecords=saved.sensorRecords;validImu=saved.validImu;validMag=saved.validMag;validEnv=saved.validEnv;lastSensorValid=saved.lastSensorValid;
                                snapshots.RemoveRange(snapshotCount,snapshots.Count-snapshotCount);cameraMisses.Clear();foreach(uint value in misses)cameraMisses.Add(value);
                                cameraIntervals.Clear();foreach(var interval in intervals)cameraIntervals.Add(interval.Key,interval.Value);
                                // A fragment only appends after saved.frameAt. Its old
                                // prefix remains intact; no rejected raster is emitted.
                            };
                        }
                        try {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException("Media export cancelled.");
                        Exact(card, raw,-1,recoverPowerCut?cancelled:null); int o = HeaderOffset;
                        Require(U(raw, o + 508) == NpDownloadStream.Crc(raw, o, 508), "Media chunk header CRC mismatch.");
                        NpV1StoreBinding.Validate(h,raw,o);
                        Require(U(raw, o + 12) == session && U(raw, o + 16) == sequence &&
                            U(raw, o + 20) == start + hs + (long)sequence * 128, "Media session/sequence/address discontinuity.");
                        string kind = Magic(raw, o); uint version = U(raw, o + 8);
                        if (kind == "NPMICBL1")
                        {
                            Require(U(h, 116) == 200000 && U(h, 120) == 1, "Unexpected microphone payload in a camera-only descriptor.");
                            ulong first = U(raw, o + 24) | ((ulong)U(raw, o + 28) << 32); uint valid = U(raw, o + 32), tick = U(raw, o + 52);
                            Require(padding == 0 && version == 8 && first == samples && valid > 0 && valid <= 61440 && valid % 2 == 0,
                                "Microphone sample counter/length discontinuity or audio after STOP padding.");
                            Require(U(raw, o + 36) == 200000 && U(raw, o + 40) == 1 && U(raw, o + 44) == 14 &&
                                U(raw, o + 48) == 2 && U(raw, o + 60) == 0, "Unsupported microphone format/rate or overrun.");
                            Require(!origin.HasValue || origin == tick, "Microphone device origin changed."); origin = tick;
                            Zero(raw, (int)valid, 61440);
                            for (int n = 0; n < valid; n += 2) Require((raw[n] & 3) == 0, "Microphone left-alignment bits are not zero.");
                            mic.Write(raw, 0, (int)valid);
                            ulong? utc = UtcAt(clockAnchor, tick, false);
                            micTime.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},200000,{3},{4},{5},{6},{7},{8},{9}", sequence, first, valid / 2, tick, U(raw, o + 56),checked(first*5000UL),checked((first+valid/2U)*5000UL),utc,utc.HasValue ? (ulong?)(utc.Value + checked(first*5UL)) : null,utc.HasValue ? clockSource : null));
                            samples += valid / 2;
                        }
                        else if (kind == "NPCAMBL1")
                        {
                            uint id = U(raw, o + 24), offset = U(raw, o + 32), valid = U(raw, o + 36), tick = U(raw, o + 28), rate = U(raw, o + 44), missed = U(raw, o + 60);
                            Require(padding == 0 && (version == 7 || version == 8) && U(raw, o + 40) == FrameBytes &&
                                U(raw, o + 48) == 320 && U(raw, o + 52) == 320 && valid > 0 && valid <= 61440 &&
                                offset <= FrameBytes && valid <= FrameBytes - offset && rate > 0 && rate == U(h, 112), "Invalid camera fragment geometry/rate.");
                            if (offset == 0)
                            {
                                Require(frameAt == 0, "New camera frame before previous frame completed.");
                                if (previousFrame.HasValue) { uint distance = unchecked(id - previousFrame.Value); Require(distance > 0 && distance < 0x80000000U, "Camera frame counter reordered."); gaps = checked(gaps + distance - 1); }
                                frameId = id; frameTick = tick; frameRate = rate; frameMissed = missed;
                            }
                            Require(id == frameId && offset == frameAt && tick == frameTick && rate == frameRate && missed == frameMissed,
                                "Camera fragment counter/offset/timestamp discontinuity.");
                            uint flags = (offset == 0 ? 1U : 0U) | (offset + valid == FrameBytes ? 2U : 0U);
                            Require(U(raw, o + 56) == flags, "Camera fragment flags mismatch."); Zero(raw, (int)valid, 61440);
                            Buffer.BlockCopy(raw, 0, raster, frameAt, (int)valid); frameAt += (int)valid;
                            if (frameAt == FrameBytes)
                            {
                                cam.Write(raster, 0, raster.Length);
                                // Exact CE64CAMGAP v1 marker; the existing player holds these slots.
                                if (Magic(raster, 0) == "CE64CAMG" && U(raster, 8) == 0x00015041U && U(raster, 12) == 0x96695AA5U) invalidFrames++;
                                if (previousFrame.HasValue)
                                {
                                    uint interval = unchecked(frameTick - previousCameraTick); string key = interval.ToString(CultureInfo.InvariantCulture);
                                    if (!cameraIntervals.ContainsKey(key)) cameraIntervals[key] = 0; cameraIntervals[key]++;
                                    if (Math.Abs((decimal)interval-1000m/frameRate)>1) irregularTiming++;
                                }
                                else { firstFrameId = frameId; firstCameraTick = frameTick; }
                                cameraMisses.Add(frameMissed);
                                continuousCameraTick=previousFrame.HasValue?AdvanceCameraTick(continuousCameraTick,previousCameraTick,frameTick):frameTick;previousCameraTick=frameTick;
                                if (Math.Abs((decimal)continuousCameraTick - firstCameraTick.Value - (decimal)frames * 1000m/frameRate) > 1) phaseErrors++;
                                ulong? utc = UtcAt(clockAnchor, frameTick, false);
                                camTime.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8}", frames, frameId, frameTick, frameRate, frameMissed, sequence,continuousCameraTick,utc,utc.HasValue ? clockSource : null));
                                frames++; previousFrame = frameId; frameAt = 0;
                            }
                        }
                        else if (kind == "NPPADBL1")
                        { Require((version == 7 || version == 8) && frameAt == 0, "Invalid STOP padding or incomplete camera frame."); Zero(raw, 0, 61440); padding++; }
                        else throw new InvalidDataException("Unknown typed payload; never decode it as neural data.");
                        if (Magic(raw, 61440) == "NPSENS01")
                        {
                            lastSensorValid = NpCe64Export.SensorRecord(raw, 61440, sequence, sensorBinary, sensorCsv); sensorRecords++;
                            if(!recoverPowerCut){var snapshot=new byte[164];Buffer.BlockCopy(raw,61440,snapshot,0,snapshot.Length);snapshots.Add(snapshot);}
                            if ((lastSensorValid & 1) != 0) validImu++;
                            if ((lastSensorValid & 2) != 0) validMag++;
                            if ((lastSensorValid & 4) != 0) validEnv++;
                            Zero(raw, 61604, HeaderOffset);
                        }
                        else { Zero(raw, 61440, HeaderOffset); lastSensorValid = 0; }
                        misc.Write(raw, 65024, 512); processed++;
                        }catch(Exception ex) {
                            if(!recoverPowerCut)throw;
                            rollback();prefixBoundary="Chunk "+sequence+": "+ex.Message;
                            if(!(ex is InvalidDataException))prefixError=ex;
                            failure="Power-interrupted, structurally validated prefix only; "+prefixBoundary;
                            break;
                        }
                        int percent = (int)(processed * 100L / chunks);
                        if (progress != null && percent != lastProgress) { lastProgress = percent; progress(percent); }
                    }
                    if(recordingComplete)Require(frameAt==0 && (U(h,116)==0 || samples>0) && (U(h,112)==0 || frames>0),"Incomplete final camera frame or empty requested stream.");
                    else if(frameAt>0)using(var tail=Create(output,"camera_tail.partial"))tail.Write(raster,0,frameAt);
                    // Physical chunk order interleaves producers. Shared mapping
                    // uses each sensor's native publication tick and validity.
                    if(!recoverPowerCut && finalMetadata!=null&&Number(finalMetadata,"version")==1&&(Number(finalMetadata,"flags")&4)!=0&&snapshots.Count>0)
                        analogRows=NpCe64Export.SensorAnalog(snapshots,(uint)Number(finalMetadata,"start_tick"),unchecked((uint)Number(finalMetadata,"stop_tick")-(uint)Number(finalMetadata,"start_tick")),analogStartUtc,analog,analogTimes,cancelled,out analogFinalValid);
                }
                foreach (string name in names) File.Move(Path.Combine(output, name + ".partial"), Path.Combine(output, name));
                complete = prefixError==null;
                if(prefixError!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(prefixError).Throw();
            }
            catch (Exception ex) { failure = ex.Message; throw; }
            finally
            {
                var report = new Dictionary<string, object> {
                    { "status", complete && !recoverPowerCut ? "validated transport" : "INCOMPLETE" }, { "error", failure },
                    { "recording_complete", recordingComplete }, { "incomplete_camera_frame_bytes", frameAt },
                    { "format_version", U(h, 8) }, { "session", session }, { "start_lba", start }, { "end_lba", end },
                    { "committed_chunks", committedChunks }, { "chunks", processed }, { "camera_frames", frames }, { "camera_invalid_frames", invalidFrames }, { "camera_frame_gaps", gaps },
                    { "camera_first_id", firstFrameId }, { "camera_last_id", previousFrame }, { "camera_first_tick", firstCameraTick }, { "camera_last_tick", previousFrame.HasValue ? (uint?)previousCameraTick : null },
                    { "camera_intervals_ms", cameraIntervals }, { "camera_timing_irregularities", irregularTiming }, { "camera_timing_phase_errors", phaseErrors }, { "camera_timestamp_quantum_ms", 1 }, { "camera_cadence_misses", cameraMisses },
                    { "metadata_descriptor", initialMetadata }, { "metadata_footer", finalMetadata }, { "legacy_clock_metadata", oldClock },
                    { "footer_state", U(f, 60) }, { "footer_fault", U(f, 64) },
                    { "camera_frame_bytes", FrameBytes }, { "camera_width", 320 }, { "camera_height", 320 },
                    { "camera_requested_or_effective_hz", U(h, 112) }, { "microphone_descriptor_hz", U(h, 116) },
                    { "microphone_hz", U(h, 116) }, { "microphone_samples", samples }, { "microphone_start_ms", origin },
                    { "sensor_records", sensorRecords }, { "valid_imu_snapshots", validImu }, { "valid_mag_snapshots", validMag }, { "valid_env_snapshots", validEnv },
                    { "imu_valid", validImu > 0 }, { "latest_imu_available", (analogFinalValid & 1) != 0 },
                    { "imu_status", sensorRecords == 0 ? "unavailable (no sensor extension)" : validImu == 0 ? "unavailable (no valid sensor snapshot)" : "valid latest-value snapshots; not a lossless FIFO" },
                    { "analogin_layout", "16 channels LE16: digital1; accel2-4; gyro5-7; magnetic8-10; slowADC11; test12; CL13-14; PCtime15-16. Valid IMU/magnetic components and anchored PCtime are populated; digital/slowADC/test/CL unavailable" },
                    { "analogin_timing", "DERIVED 1250-Hz causal latest-value hold, START-relative, independent native sensor publication ticks; not a lossless FIFO and not new measurements" },
                    { "analogin_sample_rate_hz", analogRows>0?(uint?)1250:null }, { "analogin_rows", analogRows },
                    { "analogin_validity", "analogin.timestamps.csv bit0=accel/gyro, bit1=magnetic; expired/invalid/unrepresentable values unavailable. 0x8000 alone is not a validity flag" },
                    { "analogin_scaling", "accel4096 counts/g; gyro16.4 counts/dps; magnetic0.3uT/count (compensated field, not packed BMM register recreation). Exact physical units retained in sensors.csv" },
                    { "analogin_scaling_profile", "CE64-derived-v1; differs from legacy NpMisc1250 USB export (mg,0.1dps,uT); no implicit unit conversion between profiles" },
                    { "analogin_units", new Dictionary<string,object> {
                        { "channels_2_4", "signed int16, 1/4096 g/count; round(micro_g*4096/1e6)" },
                        { "channels_5_7", "signed int16, 1/16.4 degree/s/count; round(milli_dps*16.4/1000)" },
                        { "channels_8_10", "signed int16, 0.3 microtesla/count; round(nanotesla/300); compensated field, not packed BMM registers" },
                        { "channels_15_16", "uint32 UTC milliseconds-of-day, low uint16 then high uint16; floor(START_utc_us+row*800)/1000 modulo 86400000" },
                        { "rounding", "nearest, midpoint away from zero; no clipping; unrepresentable sensor group invalidated in sidecar" } } },
                    { "analogin_pc_time_valid", analogRows>0&&analogStartUtc.HasValue },
                    { "analogin_pc_time_source", analogRows>0&&analogStartUtc.HasValue?clockSource:null },
                    { "analogin_pc_time_validity", "analogin.timestamps.csv pc_time_valid column; utc_us retained. Missing anchor leaves PCtime unavailable, not a fabricated chunk counter" },
                    { "microphone_format", "unsigned LE16; 14-bit left aligned" }, { "padding_chunks", padding },
                    { "device_timestamps", "raw uint32 device millisecond ticks; retain wrap; no PC resampling or timestamp repair" },
                    { "payload_integrity", "metadata CRC/geometry/continuity validated; native payload has no embedded checksum" },
                    { "resampling_or_repair", false }
                    ,{ "capture_lossless_qualified", false }, { "capture_qualification", "Transport validation is not full-capture qualification; retain camera/microphone fault and source/saved counters from descriptor/footer." }
                    ,{ "continuous_timestamps", "Raw device ticks retained; camera uint32 wrap unwrapped without inventing frames; microphone relative sample time is exact 5000 ns/sample." }
                };
                Qualify(h, f, initialMetadata, finalMetadata, report, cameraMisses);
                if(recoverPowerCut) {
                    report["power_cut_prefix_export"]=true;report["power_interrupted"]=true;report["recovered"]=processed>0;
                    report["recovered_prefix_chunks"]=processed;report["prefix_scan_limit_chunks"]=chunks;report["prefix_export_finished"]=complete;
                    report["prefix_boundary"]=prefixBoundary;report["firmware_committed"]=false;report["finalized"]=false;report["lossless"]=false;
                    report["recovery_notice"]="Read-only structurally validated contiguous prefix; original footer still reports zero committed chunks. Not finalized, firmware-committed or lossless; no footer/count/timing synthesis. Native pixel/ADC payload has no independent payload checksum.";
                    report["analogin_timing"]="Not generated for recovery: the initial footer supplies no confirmed capture START/STOP interval. Original sensor snapshots are retained in sensors.dat/csv.";
                }
                File.WriteAllText(Path.Combine(output, "recording.json"), new JavaScriptSerializer().Serialize(report) + "\n", new UTF8Encoding(false));
            }
        }
    }

    // Reuses the existing SD/USB raster decoder. Best-effort host playback;
    // timestamps remain authoritative in the exported CSV, not this UI timer.
    internal sealed class NpMediaPlaybackForm : Form
    {
        readonly PictureBox image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
        readonly Button open = new Button { Text = "Open camera.raw…", AutoSize = true };
        readonly Label status = new Label { AutoSize = true };
        readonly Timer timer = new Timer { Interval = 20 };
        readonly WILD_VideoDecoder.PreviewDecoder decoder = new WILD_VideoDecoder.PreviewDecoder();
        readonly byte[] raw = new byte[NpMediaExport.FrameBytes]; FileStream stream; long frame;
        internal NpMediaPlaybackForm()
        {
            Text = "Recorded V1 camera — best-effort playback"; Width = 650; Height = 600;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46 }; actions.Controls.Add(open); actions.Controls.Add(status);
            Controls.Add(image); Controls.Add(actions); open.Click += (s, e) => Open(); timer.Tick += (s, e) => Next();
            USB512_interface.WildXTheme.Apply(this);USB512_interface.WildXTheme.Button(open,true);status.ForeColor=USB512_interface.WildXTheme.Muted;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Stop(); timer.Dispose(); if (stream != null) { stream.Dispose(); stream = null; } var previous = image.Image; image.Image = null; if (previous != null) previous.Dispose(); }
            base.Dispose(disposing);
        }
        void Open()
        {
            using (var dialog = new OpenFileDialog { Filter = "Native V1 camera (camera.raw)|camera.raw", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                timer.Stop(); if (stream != null) { stream.Dispose(); stream = null; }
                try
                {
                    string reportPath = Path.Combine(Path.GetDirectoryName(dialog.FileName), "recording.json");
                    var report = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(reportPath));
                    if ((string)report["status"] != "validated transport" || (Convert.ToInt32(report["format_version"]) != 7 && Convert.ToInt32(report["format_version"]) != 8))
                        throw new InvalidDataException("Open a validated native V1 media export.");
                    var candidate = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (candidate.Length == 0 || candidate.Length % raw.Length != 0 || candidate.Length / raw.Length != Convert.ToInt64(report["camera_frames"]))
                    { candidate.Dispose(); throw new InvalidDataException("Camera file length differs from its validated report."); }
                    stream = candidate; frame = 0; decoder.Reset(); uint rate = Convert.ToUInt32(report["camera_requested_or_effective_hz"]);
                    timer.Interval = rate > 0 && rate <= 58 ? Math.Max(1, (int)(1000 / rate)) : 20; timer.Start();
                }
                catch (Exception ex) { status.Text = ex.Message; }
            }
        }
        void Next()
        {
            try
            {
                if (stream == null || stream.Position == stream.Length) { timer.Stop(); status.Text = "Finished " + frame + " frames"; return; }
                int at = 0; while (at < raw.Length) { int n = stream.Read(raw, at, raw.Length - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                var next = decoder.Decode(raw); var previous = image.Image; image.Image = next; if (previous != null) previous.Dispose();
                status.Text = "Frame " + (++frame) + " / " + stream.Length / raw.Length +
                    (decoder.RecoverySummary == null ? "" : " — " + decoder.RecoverySummary);
            }
            catch (Exception ex) { timer.Stop(); status.Text = "Playback stopped: " + ex.Message; }
        }
    }
}
