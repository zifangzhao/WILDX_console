using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

// Exercise the delivered conversion path, including its audit and native-file
// preservation. No main-form load, discovery, device, BLE or firmware access.
static class NpCameraRecoveryTests
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static byte[] Frame()
    {
        var raw = new byte[160000];
        for (int y = 0; y < 320; y++) {
            for (int n = 0; n < 12; n++) raw[y * 492 + n] = 0x55;
            for (int x = 0; x < 320; x += 2) {
                int a = (x * 7 + y * 13) % 1024, b = ((x + 1) * 7 + y * 13) % 1024, at = y * 492 + 12 + x / 2 * 3;
                raw[at] = (byte)(128 | a >> 3); raw[at + 1] = (byte)((a & 7) << 5 | 8 | b >> 7); raw[at + 2] = (byte)((b & 127) << 1);
            }
        }
        return raw;
    }
    static int Main(string[] args)
    {
        try {
            var app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            string output = args[1]; Directory.CreateDirectory(output);
            Environment.SetEnvironmentVariable("CE32_FFMPEG", args[2]); Environment.SetEnvironmentVariable("CE32_FFMPEG_PROFILE", "minimal");
            var good = Frame(); var torn = (byte[])good.Clone(); torn[42 * 492 + 12] ^= 128;
            var shifted = new byte[160000];
            for (int n = 0; n < 157452; n++) shifted[n] = (byte)((good[n] >> 1) | (n == 0 ? 0 : good[n - 1] << 7));
            var gap = new byte[160000]; Buffer.BlockCopy(new byte[] {67,69,54,52,67,65,77,71,65,80,1,0,165,90,105,150}, 0, gap, 0, 16);
            var native = new Dictionary<string, byte[]> {
                {"camera.raw", good.Concat(torn).Concat(gap).Concat(shifted).Concat(good).ToArray()},
                {"microphone.dat", new byte[0]}, {"camera.timestamps.csv", System.Text.Encoding.UTF8.GetBytes("frame,tick\n0,100\n1,125\n2,150\n3,175\n4,200\n")}
            };
            var json = new JavaScriptSerializer();
            native["recording.json"] = System.Text.Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string, object> {
                {"status","validated transport"},{"format_version",8},{"microphone_samples",0},{"camera_frames",5},{"camera_requested_or_effective_hz",40},
                {"camera_timing_irregularities",1},{"source_stop_timing_assumption","fixture"},{"capture_qualification","failed"},{"capture_lossless_qualified",false}
            }));
            foreach (var file in native) File.WriteAllBytes(Path.Combine(output, file.Key), file.Value);
            var convert = app.GetType("CE32_console.NpMediaExport", true).GetMethod("ConvertFiles", All);
            string warning = (string)convert.Invoke(null, new object[] { output, null, null });
            foreach (var file in native) Check(File.ReadAllBytes(Path.Combine(output, file.Key)).SequenceEqual(file.Value), "Native file changed: " + file.Key);
            var report = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(output, "conversion.json")));
            foreach (var expected in new Dictionary<string, long> { {"camera_image_frames_checked",5},{"camera_image_realigned_frames",1},{"camera_image_realigned_rows",320},{"camera_image_held_frames",2},{"camera_image_gap_frames",1} })
                Check(Convert.ToInt64(report[expected.Key]) == expected.Value, "Incorrect audit count: " + expected.Key);
            Check((string)report["status"] == "complete" && !(bool)report["capture_lossless_qualified"] && (string)report["capture_qualification"] == "failed", "Image recovery changed native qualification");
            Check(warning.Contains("1 frames realigned") && warning.Contains("2 frames held") && warning.Contains("Timing warning"), "User warning lost recovery/timing distinction");
            string[] rows = File.ReadAllLines(Path.Combine(output, "camera.image-recovery.csv"));
            Check(rows.SequenceEqual(new[] {"frame_index,status,realigned_rows,invalid_rows","1,invalid-held,0,1","2,gap-held,0,0","3,realigned,320,0"}), "Audit frame mask differs from actual source");
            Check(!File.Exists(Path.Combine(output, "camera.partial.image-recovery.csv")) && !File.Exists(Path.Combine(output, "camera.partial.mp4")), "Successful conversion left partial output");
            using (var process = new Process { StartInfo = new ProcessStartInfo(args[2], "-hide_banner -loglevel error -i \"" + Path.Combine(output, "camera.mp4") + "\" -f rawvideo -pix_fmt rgb24 -vsync 0 pipe:1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
            using (var decoded = new MemoryStream()) {
                process.Start(); process.StandardOutput.BaseStream.CopyTo(decoded); string error = process.StandardError.ReadToEnd();
                Check(process.WaitForExit(10000) && process.ExitCode == 0, "MP4 decode failed: " + error);
                byte[] pixels = decoded.ToArray(); int bytes = 320 * 320 * 3;
                Check(pixels.Length == 5 * bytes, "Conversion removed/added frame slots");
                for (int frame = 1; frame <= 2; frame++) {
                    long difference = 0; for (int n = 0; n < bytes; n++) difference += Math.Abs(pixels[n] - pixels[frame * bytes + n]);
                    Check(difference / (double)bytes < 2, "Held MP4 image differs from previous complete image");
                }
            }
            byte[] video = File.ReadAllBytes(Path.Combine(output, "camera.mp4"));
            bool refused = false; try { convert.Invoke(null, new object[] { output, null, null }); } catch (TargetInvocationException e) { refused = e.InnerException is IOException; }
            Check(refused && video.SequenceEqual(File.ReadAllBytes(Path.Combine(output, "camera.mp4"))), "Conversion overwrote an existing video");
            Console.WriteLine("PASS " + checks + " delivered image-recovery checks; raw/timestamps/qualification unchanged, exact affected-frame audit, frame slots and MP4 holds verified. No hardware."); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
