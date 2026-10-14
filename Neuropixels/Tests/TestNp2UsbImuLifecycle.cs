using System;
using System.IO;

namespace USB512_interface {
    // Test-only stand-in: the monitor only needs this type for its idle EP0 method.
    internal sealed class NpV2UsbDevice { internal byte[] GetSensorSnapshot() { return null; } }
}

internal static class TestNp2UsbImuLifecycle
{
    static int checks;
    static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    static void Put(byte[] bytes, int offset, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4); }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("<NpV2UsbConsole.cs> <mainForm.SystemInfo.cs> <fresh-folder>");
            string folder = Path.GetFullPath(args[2]);
            if (Directory.Exists(folder)) throw new IOException("Fresh folder required");
            Directory.CreateDirectory(folder);
            int displays = 0;
            using (var monitor = new USB512_interface.NpUsbSensorMonitor(null, folder, "fixture-UID", false, delegate { displays++; }))
            {
                var block = new byte[512];
                Put(block, 52, 128U); Put(block, 256, 0x31534e53U); Put(block, 260, 1U);
                Put(block, 268, 1U); Put(block, 272, 1000U); Put(block, 276, 1U); Put(block, 288, 1000U);
                monitor.FeedBlock(block, 0);
                Check(monitor.Records == 1 && displays == 1 && monitor.LastDisplay != null, "monitor-only USB IMU snapshot was not delivered");
            }
            Check(!File.Exists(Path.Combine(folder, "imu_snapshots.bin")) && !File.Exists(Path.Combine(folder, "analogin.dat")) &&
                !File.Exists(Path.Combine(folder, "imu_observation_status.txt")), "record-misc disabled but IMU sidecars were written");
            string source = File.ReadAllText(Path.GetFullPath(args[0]));
            int monitorAt = source.IndexOf("sensorMonitor=new NpUsbSensorMonitor", StringComparison.Ordinal);
            int headersAt = source.IndexOf("device.EnableSensorHeaders(true)", StringComparison.Ordinal);
            int startAt = source.IndexOf("device.Start(targetBlocks)", StringComparison.Ordinal);
            Check(monitorAt >= 0 && headersAt > monitorAt && startAt > headersAt, "SNS1 headers are not armed before NP2 source start");
            Check(source.IndexOf("npRecordMiscSignals ? new Action<NpImuDisplay>", StringComparison.Ordinal) < 0 &&
                source.IndexOf("}), npRecordMiscSignals,npMergeProbeDat,saveRaw:false);", StringComparison.Ordinal) >= 0, "IMU monitoring remains gated by record-misc persistence");
            string systemInfo = File.ReadAllText(Path.GetFullPath(args[1]));
            int guardAt = systemInfo.IndexOf("private bool CanRefreshIdleNp2Sensor()", StringComparison.Ordinal);
            int readAt = systemInfo.IndexOf("device.GetSensorSnapshot()", guardAt, StringComparison.Ordinal);
            int idleCheckAt = systemInfo.IndexOf("service.Mode != 2U || service.Busy != 0U", guardAt, StringComparison.Ordinal);
            int captureCheckAt = systemInfo.IndexOf("capture.State != 0U || capture.SourceState != 0U", guardAt, StringComparison.Ordinal);
            Check(guardAt >= 0 && idleCheckAt > guardAt && captureCheckAt > idleCheckAt && readAt > captureCheckAt && systemInfo.IndexOf("!running && !ztestRunning", guardAt, StringComparison.Ordinal) > guardAt &&
                systemInfo.IndexOf("!firmwareManagementActive && !DownloadBusy && !BleInUse", guardAt, StringComparison.Ordinal) > guardAt &&
                systemInfo.IndexOf("npRecoveryCancelled && !npRecoveryAttempt", guardAt, StringComparison.Ordinal) > guardAt, "idle IMU read lacks capture/update/recovery exclusion");
            Console.WriteLine("PASS " + checks + " USB IMU lifecycle checks; source-only/no device.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
