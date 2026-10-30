using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CE32_console
{
    internal static class WILD_VideoDecoder
    {
        private const int Width = 320;
        private const int Height = 320;
        private const int BytesPerFrame = 320 * 500;
        private const int ActivePackedBytesPerFrame = 320 * 492;
        // Firmware emits this at raw slot offset zero for a missing camera frame.
        private static readonly byte[] CameraGapMarker =
        {
            0x43, 0x45, 0x36, 0x34, 0x43, 0x41, 0x4D, 0x47,
            0x41, 0x50, 0x01, 0x00, 0xA5, 0x5A, 0x69, 0x96
        };
        private const string FfmpegExeEnvVar = "CE32_FFMPEG";
        private const string FfmpegHomeEnvVar = "CE32_FFMPEG_HOME";
        private const string FfmpegProfileEnvVar = "CE32_FFMPEG_PROFILE";
        // Near-lossless target for QSV ICQ/CQP mode (lower is better, typical range 1..51).
        private const int QsvGlobalQualityNearLossless = 8;
        // Keep muxed AAC rate broadly decoder-compatible.
        private const int MaxMuxAudioSampleRate = 48000;
        private static readonly int[] SupportedAacSampleRates =
        {
            96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350
        };

        public static void DecodeHevcWithAudio(
            string miscDatPath,
            int fps = 16,
            int audioSampleRate = 160000,
            bool useNvenc = true,
            bool includeAudio = false,
            Action<int> progressCallback = null,
            string outputVideoPath = null,
            Func<bool> cancelled = null,
            Action<long, FrameRecovery> imageRecovery = null)
        {
            if (string.IsNullOrWhiteSpace(miscDatPath))
            {
                throw new ArgumentException("Input path is empty.", nameof(miscDatPath));
            }

            if (!File.Exists(miscDatPath))
            {
                throw new FileNotFoundException("Video file not found.", miscDatPath);
            }

            string dir = Path.GetDirectoryName(miscDatPath) ?? string.Empty;
            string audioDatPath = Path.Combine(dir, "adc.dat");
            string wavPath = Path.ChangeExtension(audioDatPath, ".wav");
            bool audioFileExists = File.Exists(audioDatPath);
            long audioBytes = audioFileExists ? new FileInfo(audioDatPath).Length : 0;
            bool hasAudioData = audioBytes >= 2;
            int muxAudioSampleRate = ResolveCompatibleMuxSampleRate(audioSampleRate);
            bool muxAudio = false;

            if (!includeAudio)
            {
                Console.WriteLine("Audio mux disabled by caller.");
            }
            else if (!hasAudioData)
            {
                Console.WriteLine(audioFileExists
                    ? "Audio mux skipped (adc.dat empty)."
                    : "Audio mux skipped (adc.dat missing).");
            }
            else
            {
                try
                {
                    SaveBinaryAudioToWav(audioDatPath, audioSampleRate, wavPath);
                    muxAudio = true;
                    Console.WriteLine("Audio mux enabled: input " + audioSampleRate + " Hz -> AAC " + muxAudioSampleRate + " Hz.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Audio preprocessing failed; writing video-only file. " + ex.Message);
                    muxAudio = false;
                }
            }

            long totalFrames = new FileInfo(miscDatPath).Length / BytesPerFrame;
            Console.WriteLine("Total frames: " + totalFrames);
            progressCallback?.Invoke(0);

            string ffmpegExecutable = ResolveFfmpegExecutable();
            if (string.IsNullOrWhiteSpace(ffmpegExecutable))
            {
                throw new FileNotFoundException(
                    "ffmpeg was not found. Install ffmpeg and add it to PATH, set " + FfmpegExeEnvVar +
                    " or " + FfmpegHomeEnvVar +
                    ", or place a portable copy at ffmpeg\\bin\\ffmpeg.exe next to the application.");
            }
            Console.WriteLine("FFmpeg executable: " + ffmpegExecutable);

            FfmpegProfile ffmpegProfile = ResolveFfmpegProfile();
            Console.WriteLine("FFmpeg profile: " + ffmpegProfile.Name);

            VideoEncoderPreset encoderPreset = SelectVideoEncoderPreset(ffmpegExecutable, useNvenc, ffmpegProfile);
            Console.WriteLine("Using encoder: " + encoderPreset.Name);
            string outputPath = outputVideoPath ?? miscDatPath.Replace(".dat", encoderPreset.OutputSuffix);
            if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(miscDatPath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Video output must not overwrite its native input.");

            var ffmpeg = BuildFfmpegProcess(ffmpegExecutable, wavPath, outputPath, fps, encoderPreset, muxAudio, muxAudioSampleRate);
            var stderr = new StringBuilder();
            ffmpeg.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    stderr.AppendLine(args.Data);
                }
            };

            byte[] frameBytes = new byte[BytesPerFrame];
            int[] rowStarts = new int[Height];
            ushort[] bayer = new ushort[Width * Height];
            ushort[] rotated = new ushort[Width * Height];
            ushort[] rgb16 = new ushort[Width * Height * 3];
            byte[] rgb48 = new byte[Width * Height * 3 * 2];
            var tone = new CameraTone();
            int exitCode = -1;
            bool processStarted = false;
            System.Threading.Timer cancellationTimer = null;

            try
            {
                bool started = ffmpeg.Start();
                if (!started)
                {
                    throw new InvalidOperationException("ffmpeg process failed to start.");
                }
                processStarted = true;
                if (cancelled != null)
                    cancellationTimer = new System.Threading.Timer(_ =>
                    {
                        if (!cancelled()) return;
                        try { if (!ffmpeg.HasExited) ffmpeg.Kill(); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                    }, null, 100, 100);
                ffmpeg.BeginErrorReadLine();

                using (var fs = new FileStream(miscDatPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (outputVideoPath != null && (fs.Length == 0 || fs.Length % BytesPerFrame != 0 || fs.Length / BytesPerFrame != totalFrames))
                        throw new InvalidDataException("Native camera length changed or contains a partial frame.");
                    int lastProgress = -1;
                    for (long idx = 0; idx < totalFrames; idx++)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException("Video conversion cancelled.");
                        if (!ReadExact(fs, frameBytes, BytesPerFrame))
                        {
                            if (outputVideoPath != null) throw new EndOfStreamException("Native camera was truncated during conversion.");
                            break;
                        }

                        if (idx % 50 == 0)
                        {
                            Console.WriteLine("Processing " + idx + "/" + totalFrames);
                        }

                        FrameRecovery recovery = DecodeFrameToRgb48(frameBytes, rowStarts, bayer, rotated, rgb16, rgb48, tone);
                        imageRecovery?.Invoke(idx, recovery);
                        ffmpeg.StandardInput.BaseStream.Write(rgb48, 0, rgb48.Length);

                        int progress = totalFrames > 0
                            ? (int)(((idx + 1) * 100L) / totalFrames)
                            : 100;
                        if (progress != lastProgress)
                        {
                            lastProgress = progress;
                            progressCallback?.Invoke(progress);
                        }
                    }
                }

                ffmpeg.StandardInput.Close();
                ffmpeg.WaitForExit();
                exitCode = ffmpeg.ExitCode;
            }
            finally
            {
                if (cancellationTimer != null) cancellationTimer.Dispose();
                if (processStarted)
                {
                    try { if (!ffmpeg.HasExited) ffmpeg.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
                ffmpeg.Dispose();
            }

            if (cancelled != null && cancelled()) throw new OperationCanceledException("Video conversion cancelled.");
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    "ffmpeg failed with exit code " + exitCode +
                    " (codec=" + encoderPreset.Name + ")" +
                    Environment.NewLine +
                    Tail(stderr.ToString(), 60));
            }

            Console.WriteLine("Video written: " + outputPath + " (codec=" + encoderPreset.Name + ")");
            progressCallback?.Invoke(100);
        }

        private static Process BuildFfmpegProcess(
            string ffmpegExecutable,
            string wavPath,
            string outputPath,
            int fps,
            VideoEncoderPreset encoderPreset,
            bool includeAudio,
            int outputAudioSampleRate)
        {
            string audioArgs = includeAudio
                ? "-i " + Quote(wavPath) + " -ac 1 -ar " + outputAudioSampleRate + " -c:a aac -b:a 192k "
                : "-an ";

            string args =
                "-y " +
                "-thread_queue_size 512 " +
                "-f rawvideo -pix_fmt rgb48le -s " + Width + "x" + Height + " -r " + fps + " -i pipe:0 " +
                audioArgs +
                encoderPreset.Args + " " +
                Quote(outputPath);

            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
        }

        private static VideoEncoderPreset SelectVideoEncoderPreset(string ffmpegExecutable, bool useNvenc, FfmpegProfile ffmpegProfile)
        {
            if (ffmpegProfile.Mode == FfmpegProfileMode.Minimal)
            {
                return new VideoEncoderPreset(
                    "mpeg4",
                    "-c:v mpeg4 -q:v 2 -pix_fmt yuv420p",
                    "_MPEG4.mp4");
            }

            if (ffmpegProfile.Mode == FfmpegProfileMode.Auto)
            {
                bool canHevcNvenc = useNvenc && CanUseEncoder(ffmpegExecutable, "hevc_nvenc");
                Console.WriteLine("Encoder probe hevc_nvenc: " + (canHevcNvenc ? "OK" : "NO"));
                if (canHevcNvenc)
                {
                    return new VideoEncoderPreset(
                        "hevc_nvenc",
                        "-c:v hevc_nvenc -preset p7 -tune hq -rc constqp -qp 10 -pix_fmt p010le",
                        "_HEVC10bit.mp4");
                }
                if (useNvenc)
                {
                    Console.WriteLine("NVENC unavailable; checking software encoders.");
                }

                // Intel Quick Sync (QSV) hardware acceleration
                bool canHevcQsv = CanUseEncoder(ffmpegExecutable, "hevc_qsv");
                Console.WriteLine("Encoder probe hevc_qsv: " + (canHevcQsv ? "OK" : "NO"));
                if (canHevcQsv)
                {
                    return new VideoEncoderPreset(
                        "hevc_qsv",
                        "-c:v hevc_qsv -preset veryslow -global_quality " + QsvGlobalQualityNearLossless + " -look_ahead 0 -profile:v main10 -pix_fmt p010le",
                        "_HEVC10bit.mp4");
                }
                bool canH264Qsv = CanUseEncoder(ffmpegExecutable, "h264_qsv");
                Console.WriteLine("Encoder probe h264_qsv: " + (canH264Qsv ? "OK" : "NO"));
                if (canH264Qsv)
                {
                    return new VideoEncoderPreset(
                        "h264_qsv",
                        "-c:v h264_qsv -preset veryslow -global_quality " + QsvGlobalQualityNearLossless + " -look_ahead 0 -pix_fmt nv12",
                        "_H264.mp4");
                }
            }

            bool canLibx265 = CanUseEncoder(ffmpegExecutable, "libx265");
            Console.WriteLine("Encoder probe libx265: " + (canLibx265 ? "OK" : "NO"));
            if (canLibx265)
            {
                return new VideoEncoderPreset("libx265", "-c:v libx265 -preset slow -pix_fmt yuv420p10le -crf 10", "_HEVC10bit.mp4");
            }

            bool canLibx264 = CanUseEncoder(ffmpegExecutable, "libx264");
            Console.WriteLine("Encoder probe libx264: " + (canLibx264 ? "OK" : "NO"));
            if (canLibx264)
            {
                return new VideoEncoderPreset("libx264", "-c:v libx264 -preset slow -pix_fmt yuv420p -crf 12", "_H264.mp4");
            }

            // Native encoder fallback: avoids external x26x codec DLL dependencies.
            return new VideoEncoderPreset("mpeg4", "-c:v mpeg4 -q:v 2 -pix_fmt yuv420p", "_MPEG4.mp4");
        }

        private static bool CanUseNvenc(string ffmpegExecutable)
        {
            return CanUseEncoder(ffmpegExecutable, "hevc_nvenc");
        }

        private static bool CanUseEncoder(string ffmpegExecutable, string encoderName)
        {
            // Fast preflight to avoid running full decode and then failing at encoder init.
            // Use realistic video parameters; tiny 16x16@1fps can be rejected by QSV as unsupported.
            string args =
                "-hide_banner -loglevel error " +
                "-f lavfi -i color=size=320x320:rate=16:color=black " +
                "-frames:v 1 -pix_fmt nv12 -r 16 " +
                "-c:v " + encoderName + " -f null -";
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                if (!p.Start())
                {
                    return false;
                }
                p.WaitForExit(10000);
                return p.HasExited && p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                    }
                }
                catch
                {
                }
                p.Dispose();
            }
        }

        private sealed class VideoEncoderPreset
        {
            public string Name { get; private set; }
            public string Args { get; private set; }
            public string OutputSuffix { get; private set; }

            public VideoEncoderPreset(string name, string args, string outputSuffix)
            {
                Name = name;
                Args = args;
                OutputSuffix = outputSuffix;
            }
        }

        private enum FfmpegProfileMode
        {
            Auto,
            Software,
            Minimal
        }

        private sealed class FfmpegProfile
        {
            public FfmpegProfileMode Mode { get; private set; }
            public string Name { get; private set; }

            public FfmpegProfile(FfmpegProfileMode mode, string name)
            {
                Mode = mode;
                Name = name;
            }
        }

        private static FfmpegProfile ResolveFfmpegProfile()
        {
            string rawValue = Environment.GetEnvironmentVariable(FfmpegProfileEnvVar);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return new FfmpegProfile(FfmpegProfileMode.Auto, "auto");
            }

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "minimal":
                case "tiny":
                case "mpeg4":
                    return new FfmpegProfile(FfmpegProfileMode.Minimal, rawValue.Trim());
                case "software":
                case "sw":
                case "nohw":
                    return new FfmpegProfile(FfmpegProfileMode.Software, rawValue.Trim());
                default:
                    return new FfmpegProfile(FfmpegProfileMode.Auto, rawValue.Trim());
            }
        }

        private static string ResolveFfmpegExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            List<string> candidates = new List<string>();

            AddFfmpegCandidatesFromPath(candidates, Environment.GetEnvironmentVariable(FfmpegExeEnvVar));
            AddFfmpegCandidatesFromPath(candidates, Environment.GetEnvironmentVariable("FFMPEG_EXE"));

            string ffmpegHome = Environment.GetEnvironmentVariable(FfmpegHomeEnvVar);
            if (string.IsNullOrWhiteSpace(ffmpegHome))
            {
                ffmpegHome = Environment.GetEnvironmentVariable("FFMPEG_HOME");
            }
            if (!string.IsNullOrWhiteSpace(ffmpegHome))
            {
                AddFfmpegCandidatesFromDirectory(candidates, ffmpegHome);
                AddFfmpegCandidatesFromDirectory(candidates, Path.Combine(ffmpegHome, "bin"));
            }

            AddFfmpegCandidatesFromPath(candidates, Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe"));
            AddFfmpegCandidatesFromPath(candidates, Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"));
            AddFfmpegCandidatesFromPath(candidates, Path.Combine(baseDir, "ffmpeg.exe"));
            AddFfmpegCandidatesFromPath(candidates, Path.Combine(baseDir, "tools", "ffmpeg.exe"));
            AddFfmpegCandidatesFromPath(candidates, Path.Combine(baseDir, "tools", "ffmpeg", "bin", "ffmpeg.exe"));
            AddFfmpegCandidatesFromPath(candidates, Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"));

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] pathDirs = pathValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pathDirs.Length; i++)
            {
                string dir = pathDirs[i].Trim();
                if (dir.Length == 0)
                {
                    continue;
                }
                AddFfmpegCandidatesFromDirectory(candidates, dir);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static void AddFfmpegCandidatesFromDirectory(List<string> candidates, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            AddFfmpegCandidatesFromPath(candidates, Path.Combine(dir, "ffmpeg.exe"));
            AddFfmpegCandidatesFromPath(candidates, Path.Combine(dir, "ffmpeg"));
        }

        private static void AddFfmpegCandidatesFromPath(List<string> candidates, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            if (!candidates.Contains(candidate))
            {
                candidates.Add(candidate);
            }
        }

        private static bool IsCameraGapFrame(byte[] frameBytes)
        {
            if (frameBytes == null || frameBytes.Length != BytesPerFrame)
            {
                return false;
            }
            for (int i = 0; i < CameraGapMarker.Length; i++)
            {
                if (frameBytes[i] != CameraGapMarker[i])
                {
                    return false;
                }
            }
            return true;
        }

        // Live USB uses the same packing, rotation and demosaic as SD video.
        // One workspace per stream, not fresh image-sized arrays for every frame.
        internal sealed class PreviewDecoder
        {
            private readonly int[] rowStarts = new int[Height];
            private readonly ushort[] bayer = new ushort[Width * Height];
            private readonly ushort[] rotated = new ushort[Width * Height];
            private readonly ushort[] rgb16 = new ushort[Width * Height * 3];
            private readonly byte[] rgb48 = new byte[Width * Height * 6];
            private readonly byte[] bgr24 = new byte[Width * Height * 3];
            private readonly CameraTone tone = new CameraTone();
            private long correctedFrames, heldFrames;
            internal bool HoldingImage { get; private set; }
            internal string RecoverySummary { get; private set; }
            internal FrameRecovery LastRecovery { get; private set; }

            internal void Reset()
            {
                tone.Reset();
                correctedFrames = heldFrames = 0;
                HoldingImage = false;
                RecoverySummary = null;
                LastRecovery = default(FrameRecovery);
                Array.Clear(rgb16, 0, rgb16.Length);
                Array.Clear(rgb48, 0, rgb48.Length);
            }

            internal Bitmap Decode(byte[] frame)
            {
                if (frame == null || frame.Length != BytesPerFrame)
                    throw new InvalidDataException("A camera frame must contain exactly 160000 bytes.");
                LastRecovery = DecodeFrameToRgb48(frame, rowStarts, bayer, rotated, rgb16, rgb48, tone);
                HoldingImage = LastRecovery.Held;
                if (LastRecovery.CorrectedRows != 0) correctedFrames++;
                if (HoldingImage) heldFrames++;
                if (correctedFrames != 0 || heldFrames != 0)
                    RecoverySummary = correctedFrames + " frames realigned; " + heldFrames + " frames held";
                for (int i = 0; i < rgb16.Length; i += 3)
                {
                    bgr24[i] = (byte)(rgb16[i + 2] >> 8);
                    bgr24[i + 1] = (byte)(rgb16[i + 1] >> 8);
                    bgr24[i + 2] = (byte)(rgb16[i] >> 8);
                }
                Bitmap image = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
                try
                {
                    BitmapData data = image.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                    try { Marshal.Copy(bgr24, 0, data.Scan0, bgr24.Length); }
                    finally { image.UnlockBits(data); }
                    return image;
                }
                catch { image.Dispose(); throw; }
            }
        }

        internal struct FrameRecovery
        {
            internal readonly int CorrectedRows, InvalidRows;
            internal readonly bool Gap;
            internal FrameRecovery(int corrected, int invalid, bool gap = false)
            { CorrectedRows = corrected; InvalidRows = invalid; Gap = gap; }
            internal bool Held { get { return Gap || InvalidRows != 0; } }
            internal string Status { get { return Gap ? "gap-held" : Held ? "invalid-held" : CorrectedRows != 0 ? "realigned" : "valid"; } }
        }

        // Training and both guards of every pixel establish alignment
        // independently of brightness or scene motion. Never change raw bytes.
        private static int CameraByte(byte[] frame, int bit)
        {
            int at = bit >> 3, shift = bit & 7;
            return shift == 0 ? frame[at] : ((frame[at] << 8 | frame[at + 1]) >> (8 - shift)) & 255;
        }

        private static bool CameraTraining(byte[] frame, int pixels)
        {
            if (pixels < 96) return false;
            int first = CameraByte(frame, pixels - 96);
            if (first != 0x55 && first != 0xaa) return false;
            for (int n = 1; n < 12; n++)
                if (CameraByte(frame, pixels - 96 + n * 8) != first) return false;
            return true;
        }

        private static bool CameraPixelsValid(byte[] frame, int pixels)
        {
            // Only the raster and twelve captured trailing bytes can supply
            // carry bits. The rest of the wire slot is padding.
            if (pixels < 0 || pixels + Width * 12 > (ActivePackedBytesPerFrame + 12) * 8) return false;
            for (int pair = 0; pair < Width / 2; pair++) {
                int at = pixels + pair * 24;
                if ((CameraByte(frame, at) & 128) == 0 ||
                    (CameraByte(frame, at + 8) & 24) != 8 ||
                    (CameraByte(frame, at + 16) & 1) != 0) return false;
            }
            return true;
        }

        private static int CameraRowStart(byte[] frame, int nominal)
        {
            if (CameraTraining(frame, nominal) && CameraPixelsValid(frame, nominal)) return nominal;
            int found = -1;
            // At most 32 bytes, far shorter than a row. Never borrow a
            // neighboring sensor row or infer shifts from image similarity.
            for (int delta = -256; delta <= 256; delta++) {
                int candidate = nominal + delta;
                if (delta == 0 || candidate < 96 || candidate + Width * 12 > (ActivePackedBytesPerFrame + 12) * 8) continue;
                if (!CameraTraining(frame, candidate) || !CameraPixelsValid(frame, candidate)) continue;
                if (found >= 0) return -1; // Ambiguous framing is not repaired.
                found = candidate;
            }
            return found;
        }

        private static FrameRecovery DecodeFrameToRgb48(
            byte[] frameBytes,
            int[] rowStarts,
            ushort[] bayer,
            ushort[] rotated,
            ushort[] rgb16,
            byte[] rgb48,
            CameraTone tone)
        {
            // Inspect the raw marker before interpreting camera packing/bit phase.
            // The caller still writes one frame for this nominal time slot. Its
            // reused RGB buffer holds the last valid image, or black before the
            // first valid frame because that buffer is initially zero-filled.
            if (IsCameraGapFrame(frameBytes))
            {
                return new FrameRecovery(0, 0, true);
            }

            int invalid = 0, corrected = 0;
            for (int row = 0; row < Height; row++) {
                rowStarts[row] = CameraRowStart(frameBytes, row * 492 * 8 + 96);
                if (rowStarts[row] < 0) invalid++;
            }
            // An early capture can clip just the first training prefix. Use
            // its pixels only if all other rows agree on the exact same shift
            // and every first-row pixel still exists and has valid guards.
            if (invalid == 1 && rowStarts[0] < 0) {
                int shift = rowStarts[1] - (492 * 8 + 96);
                bool uniform = shift < 0 && shift >= -96;
                for (int row = 1; row < Height && uniform; row++)
                    uniform = rowStarts[row] == row * 492 * 8 + 96 + shift;
                if (uniform && CameraPixelsValid(frameBytes, 96 + shift)) { rowStarts[0] = 96 + shift; invalid = 0; }
            }
            if (invalid != 0) return new FrameRecovery(0, invalid);

            // Unpack only a complete validated raster. Hold the whole previous
            // image on damage rather than stitch moving subjects across times.
            int outIdx = 0;
            for (int row = 0; row < Height; row++) {
                int start = rowStarts[row];
                if (start != row * 492 * 8 + 96) corrected++;
                for (int pair = 0; pair < Width / 2; pair++) {
                    int at = start + pair * 24;
                    int b0 = CameraByte(frameBytes, at), b1 = CameraByte(frameBytes, at + 8), b2 = CameraByte(frameBytes, at + 16);
                    bayer[outIdx++] = (ushort)(((b0 & 0x7F) << 3) | (b1 >> 5));
                    bayer[outIdx++] = (ushort)(((b1 & 0x07) << 7) | (b2 >> 1));
                }
            }

            // np.rot90(..., 2)
            for (int y = 0; y < Height; y++)
            {
                int srcRow = (Height - 1 - y) * Width;
                int dstRow = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    rotated[dstRow + x] = bayer[srcRow + (Width - 1 - x)];
                }
            }

            BilinearDemosaicRggb(rotated, Width, Height, rgb16);
            tone.Apply(rgb16);

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(rgb16, 0, rgb48, 0, rgb48.Length);
            }
            else
            {
                int j = 0;
                for (int i = 0; i < rgb16.Length; i++)
                {
                    ushort v = rgb16[i];
                    rgb48[j++] = (byte)(v & 0xff);
                    rgb48[j++] = (byte)(v >> 8);
                }
            }
            return new FrameRecovery(corrected, 0);
        }

        private static void BilinearDemosaicRggb(ushort[] src, int width, int height, ushort[] dstRgb)
        {
            int dst = 0;
            for (int y = 0; y < height; y++)
            {
                bool evenY = (y & 1) == 0;
                for (int x = 0; x < width; x++)
                {
                    bool evenX = (x & 1) == 0;

                    int c = P(src, width, height, x, y);
                    int up = P(src, width, height, x, y - 1);
                    int dn = P(src, width, height, x, y + 1);
                    int lf = P(src, width, height, x - 1, y);
                    int rt = P(src, width, height, x + 1, y);
                    int ul = P(src, width, height, x - 1, y - 1);
                    int ur = P(src, width, height, x + 1, y - 1);
                    int dl = P(src, width, height, x - 1, y + 1);
                    int dr = P(src, width, height, x + 1, y + 1);

                    int r;
                    int g;
                    int b;

                    // Native BGGR becomes RGGB after the 180-degree rotation.
                    // OpenCV's legacy BayerBG2RGB alias also means RGGB.
                    // R G R G
                    // G B G B
                    if (evenY && evenX) // Red site
                    {
                        r = c;
                        g = (up + dn + lf + rt) / 4;
                        b = (ul + ur + dl + dr) / 4;
                    }
                    else if (!evenY && !evenX) // Blue site
                    {
                        b = c;
                        g = (up + dn + lf + rt) / 4;
                        r = (ul + ur + dl + dr) / 4;
                    }
                    else if (evenY) // Green on red row
                    {
                        g = c;
                        r = (lf + rt) / 2;
                        b = (up + dn) / 2;
                    }
                    else // Green on blue row
                    {
                        g = c;
                        r = (up + dn) / 2;
                        b = (lf + rt) / 2;
                    }

                    dstRgb[dst++] = ClampU16(r);
                    dstRgb[dst++] = ClampU16(g);
                    dstRgb[dst++] = ClampU16(b);
                }
            }
        }

        // Display/export only. Lock one curve at the first complete image;
        // per-frame gain/black tracking makes flat dark periods turn gray.
        // This is scene contrast, not a calibrated sensor black reference.
        private sealed class CameraTone
        {
            readonly int[] luminance = new int[1024];
            readonly ushort[] curve = new ushort[1024];
            bool initialized;
            internal void Reset() { initialized = false; }
            internal void Apply(ushort[] rgb)
            {
                if (!initialized) Initialize(rgb);
                for (int i = 0; i < rgb.Length; i++) rgb[i] = curve[rgb[i]];
            }
            void Initialize(ushort[] rgb)
            {
                Array.Clear(luminance, 0, luminance.Length);
                // Exclude demosaic boundaries and the captured reference edge.
                for (int y = 2; y < Height - 2; y++) for (int x = 2; x < Width - 2; x++) {
                    int i = (y * Width + x) * 3;
                    luminance[(54 * rgb[i] + 183 * rgb[i + 1] + 19 * rgb[i + 2] + 128) >> 8]++;
                }
                int pixels = (Width - 4) * (Height - 4), low = 0, high = 1023;
                int count = 0;
                for (int i = 0; i < luminance.Length; i++) {
                    count += luminance[i];
                    if (count >= (pixels + 99) / 100) { low = i; break; }
                }
                count = 0;
                for (int i = 0; i < luminance.Length; i++)
                    if ((count += luminance[i]) >= (pixels * 99 + 99) / 100) { high = i; break; }
                // Keep the bounded first-image shadow correction, but never
                // lower it as the scene darkens or add automatic exposure gain.
                // A flat initial image uses the full native range, not black.
                int shadow = high - low >= 64 ? Math.Max(0, Math.Min(256, low - 8)) : 0;
                for (int i = 0; i < curve.Length; i++)
                {
                    double linear = Math.Max(0, Math.Min(1, (i - shadow) / (double)(1023 - shadow)));
                    double display = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
                    curve[i] = (ushort)Math.Round(display * 65535);
                }
                initialized = true;
            }
        }

        internal static int MeasureExposure(byte[] raw)
        {
            if (raw == null || raw.Length != BytesPerFrame)
                throw new InvalidDataException("A camera frame must contain exactly 160000 bytes.");
            int sum = 0;
            for (int row = 80; row <= 240; row += 80)
            {
                // Invalid alignment must not drive automatic exposure when
                // rendering is hidden/throttled. Metering remains inexpensive.
                if (!CameraTraining(raw, row * 492 * 8 + 96)) return -1;
                for (int column = 80; column <= 240; column += 80)
                {
                    int offset = row * 492 + 12 + column / 2 * 3;
                    if ((raw[offset] & 0x80) == 0 || (raw[offset + 1] & 0x18) != 8 || (raw[offset + 2] & 1) != 0) return -1;
                    sum += ((raw[offset] & 0x7f) << 3) | (raw[offset + 1] >> 5);
                }
            }
            return sum / 9;
        }

        private static int P(ushort[] src, int width, int height, int x, int y)
        {
            if (x < 0) x = 0;
            else if (x >= width) x = width - 1;
            if (y < 0) y = 0;
            else if (y >= height) y = height - 1;
            return src[y * width + x];
        }

        private static ushort ClampU16(int value)
        {
            if (value < 0) return 0;
            if (value > 65535) return 65535;
            return (ushort)value;
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int n = stream.Read(buffer, offset, count - offset);
                if (n <= 0)
                {
                    return false;
                }
                offset += n;
            }
            return true;
        }

        public static void SaveBinaryAudioToWav(string adcDatPath, int sampleRate, string wavPath)
        {
            byte[] raw = File.ReadAllBytes(adcDatPath);
            int sampleCount = raw.Length / 2;
            double[] signal = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int lo = raw[2 * i];
                int hi = raw[2 * i + 1];
                int u = lo | (hi << 8);
                signal[i] = u - 32768.0;
            }

            Console.WriteLine("Audio samples: " + sampleCount + ", fs: " + sampleRate + ", duration(s): " + (sampleCount / (double)sampleRate));

            // 1 Hz high-pass with forward + backward pass (zero-phase style).
            signal = ApplyHighPassZeroPhase(signal, sampleRate, 1.0);

            short[] pcm = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                double v = signal[i];
                if (v > short.MaxValue) v = short.MaxValue;
                if (v < short.MinValue) v = short.MinValue;
                pcm[i] = (short)Math.Round(v);
            }

            WriteMono16Wav(wavPath, sampleRate, pcm);
        }

        private static double[] ApplyHighPassZeroPhase(double[] input, int sampleRate, double cutoffHz)
        {
            double[] forward = ApplyOnePoleHighPass(input, sampleRate, cutoffHz);
            Array.Reverse(forward);
            double[] backward = ApplyOnePoleHighPass(forward, sampleRate, cutoffHz);
            Array.Reverse(backward);
            return backward;
        }

        private static double[] ApplyOnePoleHighPass(double[] input, int sampleRate, double cutoffHz)
        {
            double dt = 1.0 / sampleRate;
            double rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            double alpha = rc / (rc + dt);

            double[] y = new double[input.Length];
            if (input.Length == 0)
            {
                return y;
            }

            y[0] = input[0];
            for (int i = 1; i < input.Length; i++)
            {
                y[i] = alpha * (y[i - 1] + input[i] - input[i - 1]);
            }
            return y;
        }

        private static void WriteMono16Wav(string path, int sampleRate, short[] pcm)
        {
            int bytesPerSample = 2;
            int dataSize = pcm.Length * bytesPerSample;
            int byteRate = sampleRate * bytesPerSample;
            short blockAlign = (short)bytesPerSample;
            short bitsPerSample = 16;

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1); // PCM
                bw.Write((short)1); // mono
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);

                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataSize);

                for (int i = 0; i < pcm.Length; i++)
                {
                    bw.Write(pcm[i]);
                }
            }
        }

        private static string Quote(string path)
        {
            return "\"" + path.Replace("\"", "\\\"") + "\"";
        }

        private static string Tail(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text) || maxLines <= 0)
            {
                return text ?? string.Empty;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int start = lines.Length - maxLines;
            if (start < 0)
            {
                start = 0;
            }

            var sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        private static int ResolveCompatibleMuxSampleRate(int inputSampleRate)
        {
            int target = inputSampleRate > 0 ? inputSampleRate : MaxMuxAudioSampleRate;
            int best = MaxMuxAudioSampleRate;
            long bestDelta = long.MaxValue;

            for (int i = 0; i < SupportedAacSampleRates.Length; i++)
            {
                int rate = SupportedAacSampleRates[i];
                if (rate > MaxMuxAudioSampleRate)
                {
                    continue;
                }

                long delta = Math.Abs((long)rate - target);
                if (delta < bestDelta)
                {
                    best = rate;
                    bestDelta = delta;
                }
            }

            return best;
        }
    }
}
