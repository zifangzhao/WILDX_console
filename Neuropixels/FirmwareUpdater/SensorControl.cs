using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

// Read-only EP0 control path. Never consumes, resets or flushes the NP bulk pipe.
internal static class SensorControl
{
    public const int Bytes = 140;
    public static object Decode(byte[] bytes)
    {
        if (bytes.Length != Bytes) throw new InvalidDataException("Truncated sensor snapshot.");
        uint U(int word) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4));
        int I(int word) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(word * 4));
        double[] V(int word, double divisor) => [I(word) / divisor, I(word + 1) / divisor, I(word + 2) / divisor];
        if (U(0) != 0x31534E53 || U(1) != 1) throw new InvalidDataException("Unsupported sensor snapshot ABI.");
        uint valid = U(3), now = U(4);
        // Recheck freshness in host parsing, independently of the device flags.
        if (U(5) == 0 || unchecked(now - U(8)) > 100) valid &= ~1U;
        if (U(6) == 0 || unchecked(now - U(9)) > 200) valid &= ~2U;
        if (U(7) == 0 || unchecked(now - U(10)) > 3000) valid &= ~4U;
        return new
        {
            version = U(1), enabled = U(2), valid, publishedMs = now,
            imuSequence = U(5), magSequence = U(6), environmentSequence = U(7),
            imuAgeMs = unchecked(now - U(8)), magAgeMs = unchecked(now - U(9)), environmentAgeMs = unchecked(now - U(10)),
            sensorTime24 = U(11),
            accelerationG = (valid & 1) != 0 ? V(12, 1_000_000) : null,
            angularRateDps = (valid & 1) != 0 ? V(15, 1000) : null,
            magneticFieldMicroTesla = (valid & 2) != 0 ? V(18, 1000) : null,
            temperatureC = (valid & 4) != 0 ? (double?)I(21) / 100 : null,
            pressurePa = (valid & 4) != 0 ? (uint?)U(22) : null,
            humidityPercent = (valid & 4) != 0 ? (double?)U(23) / 1000 : null,
            imuChipId = U(24), environmentChipId = U(25),
            imuError = U(26), environmentError = U(27),
            imuIoErrors = U(28), environmentIoErrors = U(29),
            imuTimeouts = U(30), environmentTimeouts = U(31),
            estimatedMissedImuSamples = U(32), invalidMagSamples = U(33), ignoredCompletions = U(34)
        };
    }
    public static void Read(string path)
    {
        using var device = WinUsbDevice.Open(path);
        var setup = new Native.WinUsbSetupPacket { RequestType = 0xC0, Request = 0x4A, Length = Bytes };
        var bytes = new byte[Bytes];
        if (!Native.WinUsb_ControlTransfer(device.Interface, setup, bytes, Bytes, out var count, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GET_SENSORS failed; the installed firmware may not include the new sensor service.");
        if (count != Bytes) throw new InvalidDataException("Short sensor snapshot response.");
        Console.WriteLine(JsonSerializer.Serialize(Decode(bytes), new JsonSerializerOptions { WriteIndented = true }));
    }
    public static void SelfTest()
    {
        var bytes = new byte[Bytes];
        void Put(int word, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(word * 4), value);
        Put(0, 0x31534E53); Put(1, 1); Put(2, 3); Put(3, 7); Put(4, 500);
        Put(5, 1); Put(6, 1); Put(7, 1); Put(8, 495); Put(9, 490); Put(10, 400);
        Put(12, 1_000_000); Put(15, unchecked((uint)-10000)); Put(18, 50000); Put(21, 2508); Put(22, 100653); Put(23, 50727);
        using (var parsed = JsonDocument.Parse(JsonSerializer.Serialize(Decode(bytes))))
        {
            var root = parsed.RootElement;
            if (root.GetProperty("accelerationG")[0].GetDouble() != 1 ||
                root.GetProperty("angularRateDps")[0].GetDouble() != -10 ||
                root.GetProperty("temperatureC").GetDouble() != 25.08 ||
                root.GetProperty("humidityPercent").GetDouble() != 50.727)
                throw new Exception("Sensor wire units/offsets failed.");
        }
        Put(4, 10000);
        using (var parsed = JsonDocument.Parse(JsonSerializer.Serialize(Decode(bytes))))
            if (parsed.RootElement.GetProperty("valid").GetUInt32() != 0 ||
                parsed.RootElement.GetProperty("temperatureC").ValueKind != JsonValueKind.Null)
                throw new Exception("Stale sensor values were presented as fresh.");
        bool rejected = false;
        try { Decode(bytes.AsSpan(0, 139).ToArray()); } catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new Exception("Truncated sensor response accepted.");
        foreach (int word in new[] { 0, 1 })
        {
            uint saved = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4));
            Put(word, 0); rejected = false;
            try { Decode(bytes); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid sensor magic/version accepted.");
            Put(word, saved);
        }
        Console.WriteLine("PASS: sensor snapshot length/magic/version/units/freshness parser (no hardware accessed).");
    }
}
