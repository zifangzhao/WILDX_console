using System;
using System.IO;
using System.Reflection;
using USB512_interface;

internal static class NpV2SdDatExportTests
{
    private const uint Start = 8192, Payload = 16384, ReservationEnd = 32768;
    private static int checks;
    private static MethodInfo ExternalBridge;
    private static void Bridge(Stream card, long start, long end, string output, Func<bool> cancel, Action<int> progress)
    {
        if(ExternalBridge==null){CE32_console.NpCe64Export.Export(card,start,end,output,cancel,progress);return;}
        try{ExternalBridge.Invoke(null,new object[]{card,start,end,output,cancel,progress});}
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}
    }
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static void Put(byte[] b, int at, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4); }
    private static void PutQ(byte[] b, int at, ulong value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 8); }
    private static void Seal(byte[] b) { uint c = uint.MaxValue; for (int n = 0; n < 508; n++) { c ^= b[n]; for (int bit = 0; bit < 8; bit++) c = (c >> 1) ^ ((c & 1) == 0 ? 0 : 0xedb88320U); } Put(b, 508, c ^ uint.MaxValue); }
    private static readonly MethodInfo Fixture = typeof(NpV2UsbConsoleTests).GetMethod("MakeV3Block", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo Word = typeof(NpV2UsbConsoleTests).GetMethod("V3DataWord", BindingFlags.NonPublic | BindingFlags.Static);
    // A bounded real8-block image relocated by8GiB. Unrepresented prefix reads
    // refuse; there is no huge backing file, fabricated payload, or exporter substitute.
    private sealed class OffsetImage : Stream
    {
        private readonly MemoryStream bytes;
        private readonly long origin;
        internal OffsetImage(byte[] data, long origin) { bytes = new MemoryStream(data, false); this.origin = origin; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return checked(origin + bytes.Length); } }
        public override long Position {
            get { return checked(origin + bytes.Position); }
            set { if (value < origin || value > Length) throw new ArgumentOutOfRangeException(); bytes.Position = value - origin; }
        }
        public override int Read(byte[] buffer, int offset, int count) { return bytes.Read(buffer, offset, count); }
        public override long Seek(long offset, SeekOrigin from) {
            Position = checked(offset + (from == SeekOrigin.Begin ? 0 : from == SeekOrigin.Current ? Position :
                from == SeekOrigin.End ? Length : throw new ArgumentOutOfRangeException())); return Position;
        }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing) bytes.Dispose(); base.Dispose(disposing); }
    }
    private static void Metadata(byte[] d, byte[] f, uint start, long length)
    {
        try { typeof(NpV2SdDatExport).GetMethod("ValidateMetadata", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(null, new object[] { d, f, start, length }); }
        catch (TargetInvocationException e) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException ?? e).Throw(); throw;
        }
    }
    private static void MakeImage(string path, uint hz, string fault)
    {
        const uint blocks = 8;
        byte[] d = new byte[512], f = new byte[512], u = new byte[512];
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("N2SDLOG1"), 0, d, 0, 8);
        uint[] fields = { 11, 7, hz, hz * 1600, 3, 63, 4, 6, 46592, 61440, 91, 4, Start, Payload,
            Payload + ((ReservationEnd - Payload) / 364) * 364, ReservationEnd, 65536, 0 };
        for (int n = 0; n < fields.Length; n++) Put(d, 8 + n * 4, fields[n]);
        Put(d, 96, 15); Put(d, 100, 1); Seal(d);
        Buffer.BlockCopy(d, 0, f, 0, d.Length); Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("N2SDEND1"), 0, f, 0, 8);
        Put(f, 100, 3); Put(f, 104, 0); Put(f, 108, blocks); Put(f, 112, blocks / 4); Put(f, 116, Payload + blocks * 91); PutQ(f, 120, blocks * 61440UL); Seal(f);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("N2SDUTC1"), 0, u, 0, 8);
        Put(u, 8, 1); Put(u, 12, 7); Put(u, 16, 3); Put(u, 20, 9); Put(u, 24, 3000); Put(u, 28, uint.MaxValue - 2);
        PutQ(u, 32, 1700000000000000); Put(u, 48, 4); Put(u, 52, 5);
        PutQ(u, 56, 1700000000007000); PutQ(u, 64, 1700000000008000); Put(u, 72, hz); Put(u, 76, 1000); Seal(u);
        if (fault == "metadata") d[8] ^= 1;
        if (fault == "footer") { Put(f, 100, 4); Put(f, 104, 1); Seal(f); }
        if (fault == "card") { f[80] = 1; Seal(f); }
        if (fault == "bounds") { Put(d, 64, uint.MaxValue); Seal(d); }
        if (fault == "utc") { Put(u, 12, 8); Seal(u); }
        if (fault == "reserved") { Put(d, 104, 1); Seal(d); }
        using (var image = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite)) {
            image.SetLength((Payload + blocks * 91L) * 512);
            image.Position = (Start + 3L) * 512; image.Write(d, 0, 512); image.Write(f, 0, 512); image.Write(u, 0, 512);
            image.Position = Payload * 512L;
            for (uint seq = 0; seq < blocks; seq++) {
                var block = (byte[])Fixture.Invoke(null, new object[] { (ulong)seq, (ulong)seq * 61440, 3U, 3U, hz, true, uint.MaxValue });
                if (seq == 4 && fault == "gap") PutQ(block, 24, 9);
                if (seq == 4 && fault == "tick") PutQ(block, 32, (ulong)seq * 61440 + 1600);
                if (seq == 4 && fault == "profile") Put(block, 60, hz == 20000 ? 30000U : 20000U);
                if (seq == 4 && fault == "sync") block[512 + 3 * 6 / 8] ^= 1 << ((3 * 6) % 8);
                if (seq == 4 && fault == "header") block[92] ^= 1;
                image.Write(block, 0, block.Length);
            }
            if (fault == "truncated") image.SetLength(image.Length - 1);
        }
    }

    private static void Valid(string root, uint hz)
    {
        string image = Path.Combine(root, "card_" + hz + ".img"), output = Path.Combine(root, "dat_" + hz);
        MakeImage(image, hz, ""); NpV2SdDatExport.Result r;
        using (var card = File.OpenRead(image)) r = NpV2SdDatExport.Export(card, Start, output);
        Check(r.SampleHz == hz && r.CommittedBlocks == 8 && r.SourceTicks == 8 * 61440UL, "Export extent/rate");
        for (int p = 0; p < 2; p++) {
            byte[] bytes = File.ReadAllBytes(Path.Combine(output, "neuropixels_probe" + (p + 1) + "_ap.dat"));
            int first = (1600 - (137 + p * 311)) % 1600;
            Check(r.Rows[p] == (8 * 61440 - first) / 1600 && bytes.Length == r.Rows[p] * 768, "Exact rows across SD block boundaries");
            for (int row = 0; row < r.Rows[p]; row++) for (int frame = 0; frame < 16; frame++) for (int adc = 0; adc < 24; adc++) {
                int ch = (adc / 2) * 32 + (adc & 1) + frame * 2;
                short expected = (short)(2048 - Convert.ToUInt16(Word.Invoke(null, new object[] { first + row * 1600, frame, adc / 8, adc % 8, p })));
                Check(BitConverter.ToInt16(bytes, row * 768 + ch * 2) == expected, "Every signed exported channel/value/byte order must match independent serial reference");
            }
        }
        string report = File.ReadAllText(Path.Combine(output, "np2_sd_export_status.txt"));
        Check(report.StartsWith("Export complete: True") && report.Contains("No ADC payload CRC") && report.Contains("first_source_tick="), "Honest export receipt/boundary policy");
        bool refused = false; try { using (var card = File.OpenRead(image)) NpV2SdDatExport.Export(card, Start, output); } catch (IOException) { refused = true; }
        Check(refused, "Existing DAT files must never be overwritten");
        int progress = 0;
        string bridge = Path.Combine(root, "wild_dat_" + hz);
        using (var card = File.OpenRead(image)) Bridge(card, Start, Payload + 8 * 91,
            bridge, () => false, value => { Check(value >= 0 && value <= 100, "WILD progress bounds"); progress++; });
        Check(progress > 0, "Normal WILD SD export must use the NP2 companion decoder");
        for (int p = 1; p <= 2; p++) {
            string name = "neuropixels_probe" + p + "_ap.dat";
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output, name))) ==
                Convert.ToBase64String(File.ReadAllBytes(Path.Combine(bridge, name))), "WILD bridge DAT bytes must match standalone export exactly");
        }
        string cancel = Path.Combine(root, "cancel_" + hz); bool stopped = false;
        try { using (var card = File.OpenRead(image)) Bridge(card, Start, Payload + 8 * 91, cancel, () => true, null); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && File.ReadAllText(Path.Combine(cancel, "np2_sd_export_status.txt")).StartsWith("Export complete: False") &&
            Directory.GetFiles(cancel, "*.dat").Length == 0, "WILD cancellation must retain partial files, never a success receipt");
        bool catalogRefused = false;
        try { using (var card = File.OpenRead(image)) NpV2SdDatExport.Export(card, Start, Path.Combine(root, "bad_catalog_" + hz), Payload + 7 * 91); }
        catch (InvalidDataException) { catalogRefused = true; }
        Check(catalogRefused, "Catalog entry shorter than committed footer must be rejected");
    }

    private static void LargeOffset(string root, uint hz)
    {
        const uint shift = 0x01000000U, blocks = 262144U;
        uint start = Start + shift, payload = Payload + shift;
        byte[] data = File.ReadAllBytes(Path.Combine(root, "card_" + hz + ".img"));
        byte[] d = new byte[512], f = new byte[512];
        Buffer.BlockCopy(data, checked((int)((Start + 3) * 512)), d, 0, 512);
        Buffer.BlockCopy(data, checked((int)((Start + 4) * 512)), f, 0, 512);
        foreach (int at in new[] { 56, 60, 64, 68, 72 }) Put(d, at, BitConverter.ToUInt32(d, at) + shift);
        Seal(d); Buffer.BlockCopy(d, 8, f, 8, 92); Put(f, 116, payload + 8 * 91); Seal(f);
        Buffer.BlockCopy(d, 0, data, checked((int)((Start + 3) * 512)), 512);
        Buffer.BlockCopy(f, 0, data, checked((int)((Start + 4) * 512)), 512);
        string output = Path.Combine(root, "high_lba_" + hz);
        using (var card = new OffsetImage(data, (long)shift * 512))
        {
            Check(card.Length > uint.MaxValue, "Actual exporter high-offset stream is beyond4GiB");
            var r = NpV2SdDatExport.Export(card, start, output);
            Check(r.SampleHz == hz && r.CommittedBlocks == 8 && r.SourceTicks == 8 * 61440UL &&
                  card.Position == ((long)payload + 8 * 91) * 512, "Actual Export narrowed LBA/file position");
        }
        for (int p = 1; p <= 2; p++)
        {
            string name = "neuropixels_probe" + p + "_ap.dat";
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(output, name))) ==
                  Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "dat_" + hz, name))),
                  "Relocated actual export must preserve every DAT byte");
        }

        // Exercise the real private metadata gate with large counts only. Do not
        // pretend these absent blocks or >4GiB DAT outputs have been exported.
        uint end = payload + blocks * 91;
        Put(d, 64, end); Put(d, 68, end); Put(d, 72, end + 16384); Seal(d);
        Buffer.BlockCopy(d, 8, f, 8, 92); Put(f, 108, blocks); Put(f, 112, blocks / 4);
        Put(f, 116, end); PutQ(f, 120, (ulong)blocks * 61440); Seal(f);
        long length = (long)end * 512;
        Metadata(d, f, start, length);
        Check(length > uint.MaxValue && BitConverter.ToUInt64(f, 120) > uint.MaxValue,
              "Large footer/file-length admission actually exceeds32 bits");
        foreach (long shortLength in new[] { length - 1, (long)unchecked((uint)length) })
        {
            bool refused = false; try { Metadata(d, f, start, shortLength); } catch (InvalidDataException) { refused = true; }
            Check(refused, "Large footer accepted a truncated/narrowed image length");
        }
        foreach (int at in new[] { 108, 112, 116, 120, 124 })
        {
            byte[] bad = (byte[])f.Clone(); Put(bad, at, BitConverter.ToUInt32(bad, at) ^ 1U); Seal(bad);
            bool refused = false; try { Metadata(d, bad, start, length); } catch (InvalidDataException) { refused = true; }
            Check(refused, "Resealed large footer accounting/high tick word accepted");
        }
        byte[] failed = (byte[])f.Clone(); Put(failed, 100, 4); Put(failed, 104, 1); Seal(failed);
        bool incomplete = false; try { Metadata(d, failed, start, length); } catch (InvalidDataException) { incomplete = true; }
        Check(incomplete, "Large-count Failed footer was promoted");
    }

    private static int Main(string[] args)
    {
        try {
            if(args.Length==2){
                ExternalBridge=Assembly.LoadFrom(Path.GetFullPath(args[1])).GetType("CE32_console.NpCe64Export",true).GetMethod("Export",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                if(ExternalBridge==null)throw new Exception("Actual console export bridge missing");
                Console.WriteLine("Testing actual Release bridge: "+Path.GetFullPath(args[1]));
            }
            string root = Path.Combine(args[0], "export_tests_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            Valid(root, 20000); Valid(root, 30000);
            checks += NpV2UsbConsoleTests.LargeCounterTests();
            LargeOffset(root, 20000); LargeOffset(root, 30000);
            foreach (string fault in new[] { "metadata", "footer", "card", "bounds", "utc", "reserved", "gap", "tick", "profile", "sync", "header", "truncated" }) {
                string image = Path.Combine(root, fault + ".img"), output = Path.Combine(root, fault); MakeImage(image, 20000, fault);
                bool rejected = false; try { using (var card = File.OpenRead(image)) NpV2SdDatExport.Export(card, Start, output); } catch (Exception e) when (e is IOException || e is InvalidDataException) { rejected = true; }
                Check(rejected, "Corruption must be rejected: " + fault);
                if (Directory.Exists(output)) {
                    Check(File.ReadAllText(Path.Combine(output, "np2_sd_export_status.txt")).StartsWith("Export complete: False"), "No false-complete export receipt: " + fault);
                    Check(Directory.GetFiles(output, "*.dat").Length == 0, "Failed verification must retain only partial DAT files: " + fault);
                }
            }
            Console.WriteLine("PASS {0} SD-to-classic-DAT checks: every sample/channel/row, 20/30 kHz, torn metadata, incomplete footer, truncation, profile/tick/sync corruption, UTC wrap, immutable headers, no-overwrite/fail-closed output; seeded source32 continuation, actual high-LBA export, large-footer/length helpers (no full >4GiB DAT write)", checks); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
