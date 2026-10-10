using System;
using System.IO;
using System.Reflection;
using System.Threading;
using USB512_interface;

internal static class NpV2UsbConsoleTests
{
    const int Header = 512, Payload = 46080, Block = 46592;
    static readonly byte[] Sync = { 0xD3, 0x6A, 0x59, 0xC5, 0x3E, 0xA7, 0x1D, 0xE4, 0xB2, 0x4F, 0x78, 0x96 };
    static readonly uint[] CrcTable = MakeCrcTable();
    static int checks;

    static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    static void Put32(byte[] bytes, int at, uint value) { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); bytes[at + 2] = (byte)(value >> 16); bytes[at + 3] = (byte)(value >> 24); }
    static void Put64(byte[] bytes, int at, ulong value) { Put32(bytes, at, (uint)value); Put32(bytes, at + 4, (uint)(value >> 32)); }
    static uint Crc(byte[] bytes, int at, int length) { uint c = 0xffffffffU; for (int i = 0; i < length; i++) c = CrcTable[(c ^ bytes[at + i]) & 255U] ^ (c >> 8); return c ^ 0xffffffffU; }
    static uint[] MakeCrcTable() { var table = new uint[256]; for (uint i = 0; i < table.Length; i++) { uint c = i; for (int bit = 0; bit < 8; bit++) c = (c >> 1) ^ ((c & 1U) == 0 ? 0U : 0xedb88320U); table[i] = c; } return table; }
    static byte Expected(int position)
    {
        int frame = position / 100, bit = position % 100;
        if (bit == 0) return (byte)(frame == 0 ? 7 : 0);
        if (bit == 1) return 0;
        byte wire = 0;
        for (int line = 0; line < 3; line++) { int at = frame * 6 + line * 2 + bit - 2; wire |= (byte)(((Sync[at / 8] >> (at % 8)) & 1) << line); }
        return wire;
    }
    static byte[] MakeBlock(ulong sequence, ulong tick, int bits)
    {
        int ticks = bits == 3 ? 122880 : 61440;
        var bytes = new byte[Block];
        byte[] magic = System.Text.Encoding.ASCII.GetBytes(bits == 3 ? "NP2USB02" : "NP2USB01"); Buffer.BlockCopy(magic, 0, bytes, 0, 8);
        Put32(bytes, 8, bits == 3 ? 2U : 1U); Put32(bytes, 12, Header); Put32(bytes, 16, Block); Put32(bytes, 20, Payload); Put64(bytes, 24, sequence); Put64(bytes, 32, tick);
        Put32(bytes, 40, (uint)ticks); Put32(bytes, 44, 48000000); Put32(bytes, 48, 0x38); Put32(bytes, 52, 1); Buffer.BlockCopy(Sync, 0, bytes, 64, Sync.Length); Put32(bytes, 96, 1); Put32(bytes, 100, (uint)bits); Put32(bytes, 104, bits == 3 ? 3U : 2U);
        for (int i = 0; i < ticks; i++)
        {
            int position = (int)((tick + (ulong)i + 719UL) % 1600UL); byte wire = position % 100 < 4 ? Expected(position) : (byte)((i * 17 + 3) & 7);
            int bit = i * bits, at = Header + bit / 8, shift = bit % 8; uint value = (uint)(bits == 3 ? wire : ((wire << 3) | (i & 7))) << shift;
            bytes[at] |= (byte)value; if (shift + bits > 8) bytes[at + 1] |= (byte)(value >> 8);
        }
        Put32(bytes, 56, Crc(bytes, Header, Payload)); Put32(bytes, 508, Crc(bytes, 0, 508)); return bytes;
    }
    static int PopCount3(uint mask) { return (int)((mask & 1U) + ((mask >> 1) & 1U) + ((mask >> 2) & 1U)); }
    static uint WireMask(uint mask) { return ((mask & 1U) != 0U ? 0x007U : 0U) | ((mask & 2U) != 0U ? 0x038U : 0U) | ((mask & 4U) != 0U ? 0x1C0U : 0U); }
    static int PhaseFor(int probe) { return 137 + probe * 311; }
    static void PutBits(byte[] bytes, int bit, uint value, int count)
    {
        for (int n = 0; n < count; n++) if ((value & (1U << n)) != 0U) bytes[Header + (bit + n) / 8] |= (byte)(1 << ((bit + n) % 8));
    }
    static byte[] MakeV3Block(ulong sequence, ulong tick, uint mask, uint live, uint sampleHz = 20000U,
        bool packedDual = false, uint rawD8Phase = uint.MaxValue)
    {
        bool rawD8 = rawD8Phase != uint.MaxValue;
        int count = PopCount3(mask); uint codec = count == 1 && (mask & 4U) == 0U ? 4U : mask == 3U ? 5U : mask == 7U ? 6U : 0U;
        if (packedDual) { if (mask != 3U) throw new ArgumentException("packed dual requires connectors 1+2"); codec = 4U; }
        if (codec == 0U) throw new ArgumentException("unsupported performance profile");
        int bits = codec == 4U ? count * 3 : codec == 5U ? 8 : 9, ticks = codec == 4U ? Payload * 8 / bits : codec == 5U ? 46080 : 40960;
        uint flags = 1U | 8U | 16U | 32U | (live != mask ? 2U : 0U) | (live == 0U ? 4U : 0U) | (rawD8 ? 64U : 0U);
        var bytes = new byte[Block]; Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("NP2USB03"), 0, bytes, 0, 8);
        Put32(bytes, 8, 3); Put32(bytes, 12, Header); Put32(bytes, 16, Block); Put32(bytes, 20, Payload);
        Put64(bytes, 24, sequence); Put64(bytes, 32, tick); Put32(bytes, 40, (uint)ticks); Put32(bytes, 44, sampleHz * 1600U);
        Put32(bytes, 48, WireMask(mask)); Put32(bytes, 52, flags); Put32(bytes, 60, sampleHz); Buffer.BlockCopy(Sync, 0, bytes, 64, Sync.Length);
        Put32(bytes, 76, mask); Put32(bytes, 80, live); Put32(bytes, 84, 1U); Put32(bytes, 156, (uint)bits); Put32(bytes, 160, codec);
        if (rawD8)
        {
            if (codec != 6U || rawD8Phase >= 1600U) throw new ArgumentOutOfRangeException(nameof(rawD8Phase));
            Put32(bytes, 176, rawD8Phase); Put32(bytes, 180, (rawD8Phase + 7U) / 8U);
        }
        for (int p = 0; p < 3; p++) if ((mask & (1U << p)) != 0U)
        {
            bytes[96 + p * 8] = (byte)('1' + p); bytes[120 + p * 8] = (byte)('A' + p); Put32(bytes, 144 + p * 4, (uint)(10 + p));
        }
        for (int i = 0; i < ticks; i++)
        {
            int group = 0;
            for (int p = 0; p < 3; p++) if ((mask & (1U << p)) != 0U)
            {
                int position = (int)((tick + (ulong)i + (ulong)PhaseFor(p)) % 1600UL);
                uint hash = ((uint)i + (uint)tick + (uint)p * 17U) * 2654435761U;
                byte wire = (live & (1U << p)) != 0U && position % 100 < 4 ? Expected(position) : (byte)((hash >> 19) & 7U);
                if (codec == 4U) PutBits(bytes, i * bits + group * 3, wire, 3);
                else if (codec == 5U) bytes[Header + i] |= (byte)(wire << (p * 3));
                else if (p < 2) bytes[Header + i] |= (byte)(wire << (p * 3));
                else
                {
                    bytes[Header + i] |= (byte)((wire & 3U) << 6);
                    if (!rawD8 && (wire & 4U) != 0U) bytes[Header + ticks + i / 8] |= (byte)(1 << (i & 7));
                }
                group++;
            }
        }
        if (rawD8)
        {
            byte WireAt(long sourceTick)
            {
                if (sourceTick < 0) return 0;
                ulong at = (ulong)sourceTick;
                int position = (int)((at + (ulong)PhaseFor(2)) % 1600UL);
                uint hash = ((uint)at + 2U * 17U) * 2654435761U;
                return (live & 4U) != 0U && position % 100 < 4 ? Expected(position) : (byte)((hash >> 19) & 7U);
            }
            for (int i = 0; i < ticks; i++)
            {
                byte wire = WireAt((long)tick + i - rawD8Phase);
                if ((wire & 4U) != 0U) bytes[Header + ticks + i / 8] |= (byte)(1 << (i & 7));
            }
            int lookaheadBytes = (int)((rawD8Phase + 7U) / 8U);
            for (int i = 0; i < lookaheadBytes * 8; i++)
            {
                byte wire = WireAt((long)tick + ticks + i - rawD8Phase);
                if ((wire & 4U) != 0U) bytes[184 + i / 8] |= (byte)(1 << (i & 7));
            }
        }
        Put32(bytes, 56, 0U); Put32(bytes, 508, 0U); return bytes;
    }
    static void Reject(Action action, string text) { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception(text); }
    static void WireTests()
    {
        foreach (int bits in new[] { 3, 6 })
        {
            int ticks = bits == 3 ? 122880 : 61440; var verifier = new NpV2WireVerifier(); verifier.Feed(MakeBlock(0, 0, bits), 0); verifier.Feed(MakeBlock(1, (uint)ticks, bits), 0);
            Check(verifier.Blocks == 2 && verifier.StoredBits == bits && verifier.TicksPerBlock == ticks && verifier.SyncPositionsChecked > 0, "valid NP2USB block rejected");
            byte[] bad = MakeBlock(0, 0, bits); bad[700] ^= 1; Reject(delegate { new NpV2WireVerifier().Feed(bad, 0); }, "payload CRC corruption accepted");
            var gap = new NpV2WireVerifier(); gap.Feed(MakeBlock(0, 0, bits), 0); Reject(delegate { gap.Feed(MakeBlock(2, (uint)ticks * 2U, bits), 0); }, "source sequence gap accepted");
        }
        foreach (uint mask in new uint[] { 1U, 2U, 3U, 7U })
        {
            int ticks = PopCount3(mask) == 1 ? 122880 : mask == 3U ? 46080 : 40960; var verifier = new NpV2WireVerifier();
            verifier.Feed(MakeV3Block(0, 0, mask, mask), 0); verifier.Feed(MakeV3Block(1, (uint)ticks, mask, mask), 0);
            Check(verifier.Blocks == 2 && verifier.FormatVersion == 3 && verifier.ProbeMask == mask && verifier.LiveProbeMask == mask &&
                  verifier.SampleHz == 20000U && verifier.MclkHz == 32000000U &&
                  verifier.IngressBytesPerTick == 1U && verifier.SyncPositionsChecked > 0,
                  "valid NP2USB03 mask rejected");
        }
        var partial = new NpV2WireVerifier(); partial.Feed(MakeV3Block(0, 0, 7, 2), 0);
        Check(partial.LiveProbeMask == 2U && partial.SyncPositionsChecked > 0 && partial.Codec == 6U && partial.ApplicationCrcPolicy == "none", "partial-live NP2USB03 rejected");
        var synthetic = new NpV2WireVerifier(); synthetic.Feed(MakeV3Block(0, 0, 7, 0), 0);
        Check(synthetic.SyncPositionsChecked == 0, "synthetic lanes treated as live probes");
        var zeroD8 = new NpV2WireVerifier(); zeroD8.Feed(MakeV3Block(0, 0, 7, 7, 20000U, false, 0U), 0);
        Check(zeroD8.D8UsesRawPhase && zeroD8.D8PhaseBits == 0U, "zero-offset deferred D8 mode rejected");
        const uint d8Phase = 137U;
        var rawD8 = new NpV2WireVerifier(); rawD8.Feed(MakeV3Block(0, 0, 7, 7, 20000U, false, d8Phase), 0);
        rawD8.Feed(MakeV3Block(1, 40960U, 7, 7, 20000U, false, d8Phase), 0);
        Check(rawD8.Blocks == 2 && rawD8.D8UsesRawPhase && rawD8.D8PhaseBits == d8Phase,
            "deferred raw-D8 lookahead or multi-block contract rejected");
        byte[] badD8Tail = MakeV3Block(0, 0, 7, 7, 20000U, false, d8Phase);
        badD8Tail[186] = (byte)(badD8Tail[186] ^ (1U << 2));
        Reject(delegate { new NpV2WireVerifier().Feed(badD8Tail, 0); }, "raw-D8 lookahead corruption accepted");
        var rate30 = new NpV2WireVerifier(); rate30.Feed(MakeV3Block(0, 0, 2, 2, 30000U), 0);
        Check(rate30.SampleHz == 30000U && rate30.MclkHz == 48000000U && rate30.Blocks == 1, "NP2USB03 30 kHz clock profile");
        var packed = new NpV2WireVerifier(); packed.Feed(MakeV3Block(0, 0, 3, 3, 30000U, true), 0);
        packed.Feed(MakeV3Block(1, 61440, 3, 3, 30000U, true), 0);
        Check(packed.StoredBits == 6 && packed.Codec == 4U && packed.TicksPerBlock == 61440 && packed.Blocks == 2, "packed-dual wire profile");
        Reject(delegate { packed.Feed(MakeV3Block(2, 122880, 3, 3, 30000U), 0); }, "codec change midstream accepted");
        foreach (int field in new[] { 40, 76, 156, 160 })
        {
            byte[] invalid = MakeV3Block(0, 0, 3, 3, 30000U, true); Put32(invalid, field, field == 76 ? 5U : 0U);
            Reject(delegate { new NpV2WireVerifier().Feed(invalid, 0); }, "malformed packed-dual contract accepted");
        }
        byte[] badV3 = MakeV3Block(0, 0, 7, 2); int corruptTick = 1600 - PhaseFor(1);
        badV3[Header + corruptTick] ^= 1 << 3;
        Reject(delegate { new NpV2WireVerifier().Feed(badV3, 0); }, "NP2USB03 live sync corruption accepted");
    }
    static void RejectIo(Action action, string text)
    {
        try { action(); } catch (IOException) { checks++; return; }
        throw new Exception(text);
    }
    static NpV2CaptureStatus IdleCapture()
    {
        var words = new uint[32]; words[0] = NpV2CaptureStatus.MagicValue; words[1] = 1U;
        return new NpV2CaptureStatus { Words = words };
    }
    static void SelfTestReportTests()
    {
        var bytes = new byte[256];
        Put32(bytes, 0, NpV2UsbSelfTestReport.MagicValue); Put32(bytes, 4, NpV2UsbSelfTestReport.VersionValue); Put32(bytes, 8, 256U);
        Put32(bytes, 16, NpV2UsbSelfTestReport.Held); Put32(bytes, 20, 1U); Put32(bytes, 36, NpV2UsbSelfTestReport.Hold | NpV2UsbSelfTestReport.VoltsUnmeasured | NpV2UsbSelfTestReport.RebootRequired);
        Put32(bytes, 40, 0x03ffU); Put32(bytes, 44, 0x03ffU); Put32(bytes, 52, NpV2UsbSelfTestReport.ExternalChecks);
        Put32(bytes, 236, 0x11223344U); Put32(bytes, 240, 0x55667788U); Put32(bytes, 244, 0x99aabbccU);
        var report = NpV2UsbSelfTestReport.Parse(bytes);
        Check(report.Holding && report.IsTerminalFor(1U) && !report.IsTerminalFor(2U), "self-test terminal command correlation changed");
        Check(report.Skipped == NpV2UsbSelfTestReport.ExternalChecks && report.Uid0 == 0x11223344U && report.Uid2 == 0x99aabbccU, "self-test field mapping changed");
        RejectIo(() => NpV2UsbSelfTestReport.Parse(new byte[255]), "truncated self-test report accepted");
        byte[] bad = (byte[])bytes.Clone(); Put32(bad, 4, 2U); RejectIo(() => NpV2UsbSelfTestReport.Parse(bad), "unknown self-test report ABI accepted");
        bad = (byte[])bytes.Clone(); Put32(bad, 40, 0x4000U); RejectIo(() => NpV2UsbSelfTestReport.Parse(bad), "unknown self-test check accepted");
        bad = (byte[])bytes.Clone(); Put32(bad, 44, 1U); Put32(bad, 48, 1U); RejectIo(() => NpV2UsbSelfTestReport.Parse(bad), "overlapping self-test result accepted");
        bad = (byte[])bytes.Clone(); Put32(bad, 40, 1U); RejectIo(() => NpV2UsbSelfTestReport.Parse(bad), "unperformed passed checks accepted");
        report.RequireUid("112233445566778899aabbcc");
        RejectIo(()=>report.RequireUid("112233445566778899AABBCD"),"Another report MCU UID accepted");
        bad=(byte[])bytes.Clone();Put32(bad,40,0x3fffU);RejectIo(()=>NpV2UsbSelfTestReport.Parse(bad),"Skipped checks admitted as attempted");
        var initial=new byte[256];Put32(initial,0,NpV2UsbSelfTestReport.MagicValue);Put32(initial,4,1U);Put32(initial,8,256U);Put32(initial,36,12U);
        var boot=NpV2UsbSelfTestReport.Parse(initial);boot.RequireUid("112233445566778899AABBCC");
        Check(boot.Sequence==0U && boot.Checked==0U,"Cold boot report was not shown as unrun");
        boot.Command=1U;RejectIo(()=>boot.RequireUid("112233445566778899AABBCC"),"Zero UID admitted after a diagnostic action");
        var identity=new NpV2ServiceStatus {Magic=NpV2ServiceStatus.MagicValue,Abi=4U,Uid0=0x11223344U,UidXor=0x55667788U^0x99aabbccU};
        identity.RequireUid("112233445566778899AABBCC");
        foreach(string uid in new[]{"112233455566778899AABBCC","112233445566778899AABBCD",null,"invalid",new string('0',24),new string('F',24)})
            RejectIo(()=>identity.RequireUid(uid),"Invalid selected service UID admitted");
        var service = new NpV2ServiceStatus { Magic = NpV2ServiceStatus.MagicValue, Abi = NpV2ServiceStatus.AbiValue, Mode = 2U, Busy = 0U };
        Check(NpV2UsbDevice.CanStartSelfTest(service, IdleCapture(), report), "idle stream self-test admission rejected");
        service.Busy = 1U; Check(NpV2UsbDevice.CanStartSelfTest(service, IdleCapture(), report), "held self-test follow-up rejected");
        report.State = NpV2UsbSelfTestReport.Running; Check(!NpV2UsbDevice.CanStartSelfTest(service, IdleCapture(), report), "running self-test admitted another command");
        service.Busy=0U;Check(!NpV2UsbDevice.CanStartSelfTest(service,IdleCapture(),report),"Running report admitted with stale idle status");
        report.State=NpV2UsbSelfTestReport.Queued;Check(!NpV2UsbDevice.CanStartSelfTest(service,IdleCapture(),report),"Queued report admitted a second command");service.Busy=1U;
        report.State = NpV2UsbSelfTestReport.Held; report.Flags = 0U; Check(!NpV2UsbDevice.CanStartSelfTest(service, IdleCapture(), report), "unheld busy self-test admission accepted");
        service.Busy = 0U; service.Mode = 1U; Check(!NpV2UsbDevice.CanStartSelfTest(service, IdleCapture(), report), "non-stream self-test admission accepted");
        service.Mode = 2U; var capture = IdleCapture(); capture.Words[2] = 2U; Check(!NpV2UsbDevice.CanStartSelfTest(service, capture, report), "active capture self-test admission accepted");
        capture = IdleCapture(); capture.Words[22] = 1U; Check(!NpV2UsbDevice.CanStartSelfTest(service, capture, report), "active source self-test admission accepted");
        report.Flags = NpV2UsbSelfTestReport.Hold; report.State = NpV2UsbSelfTestReport.Held; report.Sequence = 9U;
        Check(!report.IsNewTerminalFor(1U, 9U) && report.IsNewTerminalFor(1U, 8U), "stale self-test sequence could be mistaken for a new report");
        Check(!report.IsNewTerminalFor(1U,10U) && !report.IsNewTerminalFor(2U,8U),"Older or wrong-command report completed an action");
        report.Sequence=0U;Check(report.IsNewTerminalFor(1U,uint.MaxValue),"Wrapped fresh sequence rejected");
    }
    static ushort V3DataWord(int firstTick, int frame, int line, int slot, int probe)
    {
        ushort word = 0;
        for (int bit = 0; bit < 12; bit++)
        {
            int tick = firstTick + frame * 100 + 4 + slot * 12 + bit;
            uint hash = ((uint)tick + (uint)probe * 17U) * 2654435761U;
            word = (ushort)(((uint)word << 1) | ((hash >> (19 + line)) & 1U));
        }
        return word;
    }
    internal static int LargeCounterTests()
    {
        int before = checks;
        const ulong originSequence = 69904, ticks = 61440, boundary = 1UL << 32;
        foreach (uint rate in new uint[] { 20000U, 30000U })
        {
            ulong origin = originSequence * ticks, end = (originSequence + 4) * ticks;
            Reject(() => new NpV2WireVerifier().Feed(MakeV3Block(originSequence, origin, 3U, 3U, rate, true), 0),
                "Default verifier accepted a nonzero tail origin");
            var verifier = new NpV2WireVerifier(originSequence, origin);
            var rows = new int[2]; var first = new ulong[2]; var crossed = new bool[2];
            for (int p = 0; p < 2; p++)
                first[p] = origin + (1600UL - (origin % 1600UL + (ulong)PhaseFor(p)) % 1600UL) % 1600UL;
            var decoder = new NpV2SampleDecoder((mask, hz) => Check(mask == 3U && hz == rate, "Large-origin decoder profile"),
                (probe, row) => {
                    int p = probe - 1; ulong at = first[p] + (ulong)rows[p] * 1600UL;
                    Check(probe >= 1 && probe <= 2 && row.Length == 384, "Large-origin row ownership");
                    for (int frame = 0; frame < 16; frame++) for (int adc = 0; adc < 24; adc++)
                    {
                        int ch = (adc / 2) * 32 + (adc & 1) + frame * 2;
                        // The existing scalar fixture pattern intentionally hashes low32 ticks.
                        Check((ushort)row[ch] == V3DataWord(unchecked((int)at), frame, adc / 8, adc % 8, p),
                            "Large-origin ADC/channel/byte ordering changed");
                    }
                    if (at < boundary && at + 1600UL > boundary) crossed[p] = true;
                    rows[p]++;
                }, originSequence, origin);
            for (ulong seq = originSequence; seq <= originSequence + 3; seq++)
            {
                byte[] block = MakeV3Block(seq, seq * ticks, 3U, 3U, rate, true);
                if (seq == originSequence)
                {
                    byte[] bad = (byte[])block.Clone(); Put32(bad, 28, 1U);
                    Reject(() => new NpV2WireVerifier(originSequence, origin).Feed(bad, 0), "Tail first sequence corruption accepted");
                    bad = (byte[])block.Clone(); Put32(bad, 36, BitConverter.ToUInt32(bad, 36) ^ 1U);
                    Reject(() => new NpV2WireVerifier(originSequence, origin).Feed(bad, 0), "Tail first source corruption accepted");
                }
                if (seq == 69906)
                {
                    int emitted = rows[0] + rows[1]; long accepted = verifier.Blocks;
                    byte[] bad = (byte[])block.Clone(); Put32(bad, 36, BitConverter.ToUInt32(bad, 36) ^ 1U);
                    Reject(() => verifier.Feed(bad, 0), "High source-tick word corruption accepted");
                    Reject(() => decoder.Feed(bad, 0, verifier), "Decoder narrowed high source-tick word");
                    Check(rows[0] + rows[1] == emitted && verifier.Blocks == accepted, "Rejected origin advanced rows/blocks");
                    bad = (byte[])block.Clone(); Put32(bad, 28, 1U);
                    Reject(() => verifier.Feed(bad, 0), "High sequence word corruption accepted");
                    bad = (byte[])block.Clone();
                    int syncAt = (int)((1600UL - (seq * ticks % 1600UL + (ulong)PhaseFor(0)) % 1600UL) % 1600UL);
                    bad[Header + syncAt * 6 / 8] ^= (byte)(1 << (syncAt * 6 % 8));
                    Reject(() => verifier.Feed(bad, 0), "High-origin framing corruption accepted");
                }
                verifier.Feed(block, 0); decoder.Feed(block, 0, verifier);
                if (seq == originSequence)
                    for (int p = 0; p < 2; p++)
                        Check(verifier.PhaseForProbe(p) == PhaseFor(p), "Tail phase was not normalized to source zero.");
            }
            decoder.Complete();
            Check(69905UL * ticks < boundary && 69906UL * ticks > boundary, "Exact source32 boundary exercised");
            Check(verifier.Blocks == 4 && verifier.PayloadBytesSeen == 4UL * Payload,
                  "Actual continuation counters lost high origin");
            for (int p = 0; p < 2; p++)
                Check(crossed[p] && (ulong)rows[p] == (end - first[p]) / 1600UL, "High-origin row boundary was lost/padded");
        }
        return checks - before;
    }
    static void DecoderTests()
    {
        // The probe-2 lane below begins an aligned physical superframe at tick
        // 1152.  Its payload bits are deterministic, so this asserts both
        // serial bit order and the 24-ADC / 16-mux-slot DAT row layout.
        byte[] first = MakeV3Block(0, 0, 2U, 2U);
        byte[] second = MakeV3Block(1, 122880U, 2U, 2U);
        var verifier = new NpV2WireVerifier(); verifier.Feed(first, 0); verifier.Feed(second, 0);
        int rows = 0, callbackProbe = 0; short[] firstRow = null; uint callbackMask = 0, callbackRate = 0;
        var decoder = new NpV2SampleDecoder(delegate(uint mask, uint rate) { callbackMask = mask; callbackRate = rate; },
            delegate(int probe, short[] row) { rows++; callbackProbe = probe; if (firstRow == null) firstRow = (short[])row.Clone(); });
        decoder.Feed(first, 0, verifier); decoder.Feed(second, 0, verifier); decoder.Complete();
        Check(callbackMask == 2U && callbackRate == 20000U && callbackProbe == 2 && rows > 1 && firstRow != null, "NP2 DAT decoder did not emit probe-2 AP rows");
        for (int frame = 0; frame < 16; frame++) for (int line = 0; line < 3; line++) for (int slot = 0; slot < 8; slot++)
        {
            int adc = line * 8 + slot;
            int channel = (adc / 2) * 32 + (adc & 1) + frame * 2;
            Check((ushort)firstRow[channel] == V3DataWord(1152, frame, line, slot, 1), "NP2 DAT channel order or serial word bit order changed");
        }
        string folder = Path.Combine(Path.GetTempPath(), "NpV2DatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            long recordedRows;
            int previewAttempts = 0;
            using (var recorder = new NpV2DatRecorder(folder, delegate { previewAttempts++; throw new InvalidOperationException("synthetic preview failure"); }))
            {
                recorder.Enqueue(first, 0, verifier); recorder.Enqueue(second, 0, verifier); recorder.Complete();
                recordedRows = recorder.TotalRows;
                Check(recorder.LiveProbeMask == 2U && recorder.SampleHz == 20000U && recordedRows == rows, "NP2 DAT writer receipt disagrees with decoder");
                Check(previewAttempts == 1 && recorder.PreviewError != null && recorder.PreviewError.Contains("synthetic preview failure"), "NP2 preview failure was not isolated from recording");
            }
            string dat = Path.Combine(folder, "neuropixels_ap.dat");
            Check(File.Exists(dat) && new FileInfo(dat).Length == recordedRows * 384L * sizeof(short), "NP2 DAT output is not 384-channel int16 rows");
            byte[] stored = File.ReadAllBytes(dat);
            Check(BitConverter.ToUInt16(stored, 0) == (ushort)firstRow[0], "NP2 DAT file does not preserve decoded little-endian samples");
            string fallbackFolder = Path.Combine(folder, "raw_fallback");
            Directory.CreateDirectory(fallbackFolder);
            using (var recorder = new NpV2DatRecorder(fallbackFolder, null))
            {
                recorder.EnqueueRaw(first, 0); recorder.EnqueueRaw(second, 0); recorder.Complete();
                Check(recorder.RawError == null && recorder.RawDroppedBlocks == 0 && recorder.TotalRows == 0,
                    "NP2 best-effort raw-only fallback reported a false failure");
            }
            Check(new FileInfo(Path.Combine(fallbackFolder, "neuropixels_np2usb.raw")).Length == 2L * NpV2UsbDevice.BlockBytes,
                "NP2 best-effort raw-only fallback did not preserve complete blocks");
        }
        finally { Directory.Delete(folder, true); }
    }
    static void DecodeEveryChannelTests()
    {
        // Independent scalar oracle: extract each ADC bit by its physical
        // source tick, then compare every channel of every complete row. The
        // last row in each block frequently straddles the following block.
        foreach (uint profile in new uint[] { 1U, 2U, 3U, 7U, 0x103U }) foreach (uint rate in new uint[] { 20000U, 30000U })
        {
            uint mask = profile & 7U; bool packedDual = (profile & 0x100U) != 0U;
            int ticks = packedDual ? 61440 : PopCount3(mask) == 1 ? 122880 : mask == 3U ? 46080 : 40960;
            int[] rows = new int[4];
            var verifier = new NpV2WireVerifier();
            var decoder = new NpV2SampleDecoder(delegate { }, delegate(int probe, short[] row)
            {
                int p = probe - 1, firstTick = (1600 - PhaseFor(p)) % 1600 + rows[probe] * 1600;
                for (int frame = 0; frame < 16; frame++) for (int adc = 0; adc < 24; adc++)
                {
                    int ch = (adc / 2) * 32 + (adc & 1) + frame * 2;
                    Check((ushort)row[ch] == V3DataWord(firstTick, frame, adc / 8, adc % 8, p), "NP2 packed channel changed across a block/frame boundary");
                }
                rows[probe]++;
            });
            for (int sequence = 0; sequence < 3; sequence++)
            {
                byte[] block = MakeV3Block((ulong)sequence, (ulong)(sequence * ticks), mask, mask, rate, packedDual);
                verifier.Feed(block, 0); decoder.Feed(block, 0, verifier);
            }
            decoder.Complete();
            for (int p = 0; p < 3; p++)
                Check(rows[p + 1] == ((mask & (1U << p)) == 0 ? 0 : (3 * ticks - (1600 - PhaseFor(p)) % 1600) / 1600), "NP2 decoder lost or padded a row");
            byte[] corrupt = MakeV3Block(3, (ulong)(3 * ticks), mask, mask, rate, packedDual);
            int tick = (1600 - (3 * ticks + PhaseFor(mask == 2U ? 1 : 0)) % 1600) % 1600;
            if (packedDual) corrupt[Header + tick * 6 / 8] ^= (byte)(1 << (tick * 6 % 8));
            else if (PopCount3(mask) == 1) corrupt[Header + tick * 3 / 8] ^= (byte)(1 << (tick * 3 % 8));
            else corrupt[Header + tick] ^= 1;
            Reject(delegate { verifier.Feed(corrupt, 0); }, "NP2 verifier skipped a sync error between formerly sampled blocks");
        }
        foreach (int bits in new[] { 3, 6 })
        {
            int ticks = bits == 3 ? 122880 : 61440, rows = 0;
            var verifier = new NpV2WireVerifier();
            var decoder = new NpV2SampleDecoder(delegate { }, delegate(int probe, short[] row)
            {
                int start = 881 + rows * 1600;
                for (int frame = 0; frame < 16; frame++) for (int adc = 0; adc < 24; adc++)
                {
                    int word = 0;
                    for (int bit = 0; bit < 12; bit++)
                    {
                        int local = (start + frame * 100 + 4 + (adc % 8) * 12 + bit) % ticks;
                        word = (word << 1) | ((((local * 17 + 3) & 7) >> (adc / 8)) & 1);
                    }
                    Check(row[(adc / 2) * 32 + (adc & 1) + frame * 2] == word, "NP2 legacy packed decoder mismatch");
                }
                rows++;
            });
            for (int sequence = 0; sequence < 3; sequence++)
            {
                byte[] block = MakeBlock((ulong)sequence, (ulong)(sequence * ticks), bits);
                verifier.Feed(block, 0); decoder.Feed(block, 0, verifier);
            }
            Check(rows == (3 * ticks - 881) / 1600, "NP2 legacy complete-row count");
        }
    }

    static void DeferredRawD8DecoderTests()
    {
        const int ticks = 40960;
        const uint rawPhase = 137U;
        int rows = 0;
        var verifier = new NpV2WireVerifier();
        var decoder = new NpV2SampleDecoder(delegate { }, delegate(int probe, short[] row)
        {
            Check(probe == 3, "deferred D8 decoder emitted the wrong probe");
            int firstTick = (1600 - PhaseFor(2)) % 1600 + rows * 1600;
            for (int frame = 0; frame < 16; frame++) for (int adc = 0; adc < 24; adc++)
            {
                int channel = (adc / 2) * 32 + (adc & 1) + frame * 2;
                Check((ushort)row[channel] == V3DataWord(firstTick, frame, adc / 8, adc % 8, 2),
                    "deferred raw-D8 alignment changed the decoded ADC row");
            }
            rows++;
        }, 3);
        for (int sequence = 0; sequence < 3; sequence++)
        {
            byte[] block = MakeV3Block((ulong)sequence, (ulong)(sequence * ticks), 7U, 7U,
                20000U, false, rawPhase);
            verifier.Feed(block, 0); decoder.Feed(block, 0, verifier);
        }
        decoder.Complete();
        Check(verifier.D8UsesRawPhase && verifier.D8PhaseBits == rawPhase &&
            rows == (3 * ticks - (1600 - PhaseFor(2)) % 1600) / 1600,
            "deferred raw-D8 decoder did not preserve rows across block boundaries");
    }

    sealed class PausedStream : MemoryStream
    {
        internal readonly ManualResetEvent Entered = new ManualResetEvent(false), Resume = new ManualResetEvent(false);
        private int calls;
        public override void Write(byte[] bytes, int offset, int count)
        {
            if (Interlocked.Increment(ref calls) == 1) { Entered.Set(); Resume.WaitOne(); }
            base.Write(bytes, offset, count);
        }
    }
    sealed class FlushFailureStream : MemoryStream
    { public override void Flush() { throw new IOException("synthetic flush failure"); } }
    static void BufferedWriterTests()
    {
        var sink = new PausedStream();
        var writer = new NpV2BatchedWriter(2, 1536);
        Exception error = null;
        writer.Open(sink, "test slow disk");
        var producer = new Thread(delegate()
        {
            try
            {
                var row = new short[384];
                for (int sample = 0; sample < 101; sample++)
                {
                    for (int ch = 0; ch < row.Length; ch++) row[ch] = (short)(sample + ch);
                    writer.WriteRow(row);
                }
                writer.Complete();
            }
            catch (Exception ex) { error = ex; }
        });
        producer.Start();
        try { Check(sink.Entered.WaitOne(5000), "DAT writer did not reach disk worker"); }
        finally { sink.Resume.Set(); }
        Check(producer.Join(5000) && error == null, "DAT writer deadlocked or failed while draining a stalled disk");
        byte[] data = sink.ToArray();
        Check(data.Length == 101 * 768 && writer.WrittenBytes == data.Length, "DAT writer lost the final partial chunk");
        for (int sample = 0; sample < 101; sample++) for (int ch = 0; ch < 384; ch++)
            Check(BitConverter.ToInt16(data, (sample * 384 + ch) * 2) == sample + ch, "DAT writer reused a buffer before disk completion");
        writer.Dispose(); sink.Entered.Dispose(); sink.Resume.Dispose();
        var failing = new NpV2BatchedWriter(2, 768);
        failing.Open(new FlushFailureStream(), "test flush failure"); failing.WriteRow(new short[384]);
        bool reported = false;
        try { failing.Complete(); } catch (IOException) { reported = true; }
        Check(reported, "DAT flush failure was silently accepted");
        try { failing.Dispose(); } catch (IOException) { }
    }
    static void ConcurrentPreviewTests()
    {
        var queue = new NpPreviewQueue(16384);
        var sweep = new NpSweepBuffer(1000);
        var start = new ManualResetEvent(false);
        Exception error = null;
        Thread[] producers = new Thread[3];
        for (int p = 1; p <= 3; p++)
        {
            int probe = p;
            producers[p - 1] = new Thread(delegate()
            {
                try
                {
                    var row = new short[384];
                    for (int ch = 0; ch < row.Length; ch++) row[ch] = (short)(probe * 1000 + ch);
                    start.WaitOne();
                    for (int sample = 0; sample < 30000; sample++) queue.Push(row, 1, Array.Empty<short>(), 0, probe);
                }
                catch (Exception ex) { Interlocked.CompareExchange(ref error, ex, null); }
            });
            producers[p - 1].Start();
        }
        start.Set();
        for (int i = 0; i < 3000; i++) { queue.Select(false, i % 6, 1 + i % 3); queue.DrainTo(sweep, queue.Generation); }
        foreach (Thread producer in producers) Check(producer.Join(5000), "NP preview producer stalled");
        Check(error == null, "NP preview failed during concurrent probe selection");
        for (int p = 1; p <= 3; p++) Check(queue.Written(false, p) == 30000, "NP preview lost a probe's sample clock");
        start.Dispose();
    }
    static void StatusTests()
    {
        var service = new byte[32]; Put32(service, 0, 0x3253504e); Put32(service, 4, 4); Put32(service, 8, 2); Put32(service, 12, 0x5a);
        NpV2ServiceStatus s = NpV2ServiceStatus.Parse(service); Check(s.Valid && s.ModeName == "USB stream" && s.Capabilities == 0x5a, "ABI4 service status");
        var capture = new byte[128]; Put32(capture, 0, 0x3155324e); Put32(capture, 4, 1); Put32(capture, 8, 4); Put32(capture, 16, 9); Put32(capture, 20, 9); Put32(capture, 24, 9); Put32(capture, 28, 9); Put32(capture, 76, 32000000); Put32(capture, 80, WireMask(7)); Put32(capture, 84, 3); Put32(capture, 88, 11); Put32(capture, 96, 7); Put32(capture, 100, 7); Put32(capture, 104, 20000); Put32(capture, 108, 1);
        NpV2CaptureStatus c = NpV2CaptureStatus.Parse(capture); Check(c.CompleteFor(9) && c.StateName == "complete", "NP2 completion status");
        Check(c.CurrentProfileSupported && c.SampleHz == 20000U && c.StoredProbeCount == 3 && c.LiveProbeMask == 7U && c.IngressBytesPerTick == 1U, "production PSSI8+SPI2 NP2USB03 profile rejected");
        var unsupportedCapture = (byte[])capture.Clone(); Put32(unsupportedCapture, 96, 5); Put32(unsupportedCapture, 80, WireMask(5)); Put32(unsupportedCapture, 100, 5);
        Check(!NpV2CaptureStatus.Parse(unsupportedCapture).CurrentProfileSupported, "unsupported connector mask accepted by status preflight");
        var source = new byte[144]; Put32(source, 0, 0x3153324e); Put32(source, 4, 1); Put32(source, 8, 11); Put32(source, 116, 1); Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("NP2013"), 0, source, 136, 6);
        NpV2SourceStatus n = NpV2SourceStatus.Parse(source); Check(n.Valid && n.State == 11 && n.Model == "NP2013", "NP2 source status");
        var profile = new byte[32]; Put32(profile, 0, 0x3143324e); Put32(profile, 4, 1); Put32(profile, 8, 2); Put32(profile, 12, 2); Put32(profile, 16, 2); Put32(profile, 20, 2); Put32(profile, 24, 6); Put32(profile, 28, 20000);
        NpV2ProfileCapabilities p = NpV2ProfileCapabilities.Parse(profile);
        Check(p.Valid && p.AvailableProbeMask == 2U && p.SelectedProbeMask == 2U && p.SupportsRate(20000U) && p.SupportsRate(30000U) && p.SupportedRates.Length == 2, "NP2 runtime profile capabilities");
        Check(p.SupportsProbeMask(2U) && !p.SupportsProbeMask(1U) && !p.SupportsProbeMask(4U), "NP2 runtime profile mask admission");
        var tripleProfile = new byte[32]; Put32(tripleProfile, 0, 0x3143324e); Put32(tripleProfile, 4, 1); Put32(tripleProfile, 8, 7); Put32(tripleProfile, 12, 7); Put32(tripleProfile, 16, 7); Put32(tripleProfile, 20, 7); Put32(tripleProfile, 24, 6); Put32(tripleProfile, 28, 20000);
        NpV2ProfileCapabilities triple = NpV2ProfileCapabilities.Parse(tripleProfile);
        Check(triple.SupportsProbeMask(1U) && triple.SupportsProbeMask(2U) && triple.SupportsProbeMask(3U) && triple.SupportsProbeMask(7U) &&
              !triple.SupportsProbeMask(4U) && !triple.SupportsProbeMask(5U) && !triple.SupportsProbeMask(6U), "triple-build optimized mask set");
        Check(NpV2UsbDevice.IsV2Path("usb#VID_1209&PID_B386#x") && !NpV2UsbDevice.IsV2Path("usb#VID_1209&PID_B387#x"), "V2 discovery filter");
    }
    static void HilStatus(string exactPath)
    {
        string path = exactPath;
        if (String.IsNullOrEmpty(path)) { var paths = NpV2UsbDevice.EnumerateDevicePaths(); Check(paths.Count == 1, "expected exactly one B386 device"); path = paths[0]; }
        using (var device = NpV2UsbDevice.Open(path))
        {
            NpV2ServiceStatus service = device.GetServiceStatus(); NpV2CaptureStatus capture = device.GetCaptureStatus(); NpV2SourceStatus source = device.GetSourceStatus(); NpV2ProfileCapabilities profile = device.GetProfileCapabilities();
            Console.WriteLine("HIL STATUS: " + path + " mode=" + service.ModeName + "(" + service.Mode + ") busy=" + service.Busy +
                " capture=" + capture.StateName + " source=" + source.State + " sample=" + capture.SampleHz + " mask=0x" + capture.ProbeMask.ToString("X1") +
                " fault=" + capture.Error + "/" + capture.SourceError + "/" + source.Error + " supported=" + capture.CurrentProfileSupported);
            NpV2ProfileCapabilities configured = device.ConfigureProfile(profile.SelectedProbeMask, profile.SampleHz);
            Check(service.Valid && service.Mode == 2 && service.Busy == 0 && capture.State == 0 && source.State == 0 && profile.Valid &&
                configured.SelectedProbeMask == profile.SelectedProbeMask && configured.SampleHz == profile.SampleHz, "fresh V2 NP2 source unavailable");
        }
    }
    static void HilRecord(string exactPath, string output, uint blocks)
    {
        string path = exactPath;
        if (String.IsNullOrEmpty(path)) { var paths = NpV2UsbDevice.EnumerateDevicePaths(); Check(paths.Count == 1, "expected exactly one B386 device"); path = paths[0]; }
        NpV2RecordingResult result = NpV2Recording.Run(path, output, blocks, delegate { return false; }, delegate(NpV2RecordingProgress value) { Console.WriteLine("HIL " + value.Text); });
        Check(result.Complete, "V2 console recorder did not produce a complete receipt: " + result.Error);
        Console.WriteLine("HIL RECORD: folder=" + result.Folder + " blocks=" + result.VerifiedBlocks + " payload=" + result.PayloadBytes + " bytes");
    }
    static void HilProfile(string exactPath, uint sampleHz)
    {
        string path = exactPath;
        if (String.IsNullOrEmpty(path)) { var paths = NpV2UsbDevice.EnumerateDevicePaths(); Check(paths.Count == 1, "expected exactly one B386 device"); path = paths[0]; }
        using (var device = NpV2UsbDevice.Open(path))
        {
            NpV2ProfileCapabilities before = device.GetProfileCapabilities();
            NpV2ProfileCapabilities after = device.ConfigureProfile(before.SelectedProbeMask, sampleHz);
            NpV2CaptureStatus capture = device.GetCaptureStatus();
            Check(after.SampleHz == sampleHz && capture.SampleHz == sampleHz && capture.PssiHz == sampleHz * 1600U,
                "V2 did not apply the requested physical rate profile");
            Console.WriteLine("HIL PROFILE: sample=" + sampleHz + " MCO=" + capture.PssiHz + " mask=0x" + after.SelectedProbeMask.ToString("X1"));
        }
    }
    static void HilContinuous(string exactPath, string output, int secondsAfterData)
    {
        string path = exactPath; DateTime? dataSeen = null;
        long[] previewRows = new long[4]; int malformedPreviewRows = 0;
        if (String.IsNullOrEmpty(path)) { var paths = NpV2UsbDevice.EnumerateDevicePaths(); Check(paths.Count == 1, "expected exactly one B386 device"); path = paths[0]; }
        NpV2RecordingResult result = NpV2Recording.RunContinuousInFolder(path, output,
            delegate { return dataSeen.HasValue && (DateTime.UtcNow - dataSeen.Value).TotalSeconds >= secondsAfterData; },
            delegate(NpV2RecordingProgress value) { if (value.ReceivedBlocks > 0U && !dataSeen.HasValue) dataSeen = DateTime.UtcNow; Console.WriteLine("HIL " + value.Text); },
            delegate(int probe, short[] row)
            {
                if (probe < 1 || probe > 3 || row == null || row.Length != NpV2SampleDecoder.Channels) { System.Threading.Interlocked.Increment(ref malformedPreviewRows); return; }
                System.Threading.Interlocked.Increment(ref previewRows[probe]);
            });
        Check(result.Complete, "V2 continuous recorder did not produce a complete receipt: " + result.Error);
        Check(result.TargetBlocks > 0U && result.TargetBlocks == result.ReceivedBlocks && result.VerifiedBlocks == result.TargetBlocks, "V2 continuous final boundary mismatch");
        long totalPreviewRows = previewRows[1] + previewRows[2] + previewRows[3];
        uint previewProbeMask = (previewRows[1] > 0 ? 1U : 0U) | (previewRows[2] > 0 ? 2U : 0U) | (previewRows[3] > 0 ? 4U : 0U);
        Check(malformedPreviewRows == 0, "V2 live preview received a malformed AP row");
        Check(totalPreviewRows > 0 && totalPreviewRows == result.DecodedDatRows, "V2 live preview row count does not match decoded DAT output");
        Check(previewProbeMask == result.DecodedDatProbeMask, "V2 live preview probe mask does not match decoded DAT output");
        Console.WriteLine("HIL CONTINUOUS: folder=" + result.Folder + " blocks=" + result.VerifiedBlocks + " payload=" + result.PayloadBytes +
            " bytes preview_rows=" + totalPreviewRows + " [" + previewRows[1] + "," + previewRows[2] + "," + previewRows[3] + "]");
    }
    static void HilContinuousPreview(string exactPath, string output, int secondsAfterData)
    {
        string path = exactPath; DateTime? dataSeen = null; long previewRows = 0;
        if (String.IsNullOrEmpty(path)) { var paths = NpV2UsbDevice.EnumerateDevicePaths(); Check(paths.Count == 1, "expected exactly one B386 device"); path = paths[0]; }
        using (var preview = new NeuropixelsPreviewForm(20000, 7, true, 12))
        {
            preview.PrepareForCapture();
            typeof(NeuropixelsPreviewForm).GetField("displayActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(preview, true);
            preview.KeepAliveWhileRecording = true;
            NpV2RecordingResult result = NpV2Recording.RunContinuousInFolder(path, output,
                delegate { return dataSeen.HasValue && (DateTime.UtcNow - dataSeen.Value).TotalSeconds >= secondsAfterData; },
                delegate(NpV2RecordingProgress value)
                {
                    if (value.ReceivedBlocks > 0U && !dataSeen.HasValue) dataSeen = DateTime.UtcNow;
                    preview.UpdateRecordingReport(value.VerifiedBlocks * 46080L, value.VerifiedBlocks, System.Threading.Interlocked.Read(ref previewRows), 0, 0);
                    Console.WriteLine("HIL PREVIEW " + value.Text);
                },
                delegate(int probe, short[] row) { System.Threading.Interlocked.Increment(ref previewRows); preview.PushSamples(probe, row, 1, Array.Empty<short>(), 0); });
            preview.KeepAliveWhileRecording = false;
            preview.Close();
            Check(result.Complete, "V2 live-preview recorder did not produce a complete receipt: " + result.Error);
            Check(previewRows > 0 && previewRows == result.DecodedDatRows, "V2 live-preview row count does not match decoded DAT output");
            Check(String.IsNullOrEmpty((string)typeof(NeuropixelsPreviewForm).GetField("renderFailure", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview)), "V2 preview renderer failed");
            Console.WriteLine("HIL LIVE PREVIEW: folder=" + result.Folder + " blocks=" + result.VerifiedBlocks + " preview_rows=" + previewRows + " dropped_display_rows=" + preview.DroppedPreviewRows);
        }
    }
    static string WaitMode(uint uid0, uint uidXor, uint mode)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 30.0)
        {
            System.Threading.Thread.Sleep(200);
            foreach (string path in NpV2UsbDevice.EnumerateDevicePaths()) try
            {
                using (var device = NpV2UsbDevice.Open(path))
                {
                    NpV2ServiceStatus status = device.GetServiceStatus();
                    if (status.Uid0 == uid0 && status.UidXor == uidXor && status.Mode == mode && status.Busy == 0U) return path;
                }
            }
            catch (Exception) { }
        }
        throw new TimeoutException("V2 did not enumerate in mode " + mode);
    }
    static void HilModeCycle(string exactPath)
    {
        string path = exactPath; uint uid0, uidXor;
        if (String.IsNullOrEmpty(path)) { var paths = NpV2UsbDevice.EnumerateDevicePaths(); Check(paths.Count == 1, "expected exactly one B386 device"); path = paths[0]; }
        using (var device = NpV2UsbDevice.Open(path)) { NpV2ServiceStatus status = device.GetServiceStatus(); uid0 = status.Uid0; uidXor = status.UidXor; device.RequestDownloadMode(); }
        path = WaitMode(uid0, uidXor, 3U);
        using (var device = NpV2UsbDevice.Open(path)) device.RequestStreamMode();
        path = WaitMode(uid0, uidXor, 2U);
        using (var device = NpV2UsbDevice.Open(path))
        {
            NpV2CaptureStatus capture = device.GetCaptureStatus(); NpV2SourceStatus source = device.GetSourceStatus();
            Check(capture.State == 0U && source.State == 0U, "V2 mode cycle did not produce a fresh stream source");
        }
        Console.WriteLine("HIL MODE CYCLE: USB download -> idle USB stream passed for " + path);
    }
    static void ReadWatchdogTests()
    {
        var watch = new NpV2ReadWatchdog(100);
        Check(!watch.ShouldReadStatus(107.99), "healthy bulk path must not poll EP0");
        Check(watch.ShouldReadStatus(108), "stalled producer must be inspected");
        Check(!watch.ShouldReadStatus(108.5) && watch.ShouldReadStatus(109), "stalled status polling is rate limited");
        Check(!watch.StopExpired(1000), "best-effort pause without Stop is not an automatic timeout");
        watch.Progress(110);
        Check(!watch.ShouldReadStatus(117.99), "bulk recovery resets stall threshold");
        watch.RequestStop(111);
        Check(watch.ShouldReadStatus(111), "Stop inspects stalled firmware before waiting for all queued URBs");
        Check(!watch.StopExpired(115.99) && watch.StopExpired(116), "stopped no-progress drain is bounded");
        watch.RequestStop(115);
        Check(watch.StopExpired(116), "repeated Stop cannot extend deadline");
        var slow = new NpV2ReadWatchdog(0);
        slow.RequestStop(1);
        for (int second = 2; second < 16; second++)
        {
            slow.Progress(second);
            Check(!slow.StopExpired(second), "progress within the total drain deadline");
        }
        slow.Progress(16);
        Check(slow.StopExpired(16), "trickling completions cannot keep Stop alive forever");
    }
    static void TopologyTests()
    {
        var direct = NpV2UsbTopology.FromLocationPath("PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)");
        var oneHub = NpV2UsbTopology.FromLocationPath("PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)#USB(3)");
        var twoHubs = NpV2UsbTopology.FromLocationPath("PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)#USB(1)#USB(3)");
        var unknown = NpV2UsbTopology.FromLocationPath("Port_#0003.Hub_#0006");
        Check(direct.HubCount == 0 && direct.Warning.Contains("Direct PC"), "direct root-port route misclassified");
        Check(oneHub.HubCount == 1 && oneHub.Warning.Contains("behind 1 hub"), "one-hub route misclassified");
        Check(twoHubs.HubCount == 2 && twoHubs.Warning.Contains("behind 2 hubs"), "two-hub route misclassified");
        Check(unknown.HubCount == -1 && unknown.Warning.Contains("could not be verified"), "unknown route falsely called direct");
    }
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static int Run(string[] args)
    {
        if((args.Length==1 || args.Length==2) && args[0]=="--self-test-report-only"){
            if(args.Length==2){
                var actual=NpV2UsbSelfTestReport.Parse(File.ReadAllBytes(args[1]));
                Check(actual.Checked==0x03bfU && actual.Passed==0x009fU && actual.Failed==0x0320U && actual.Skipped==0x3c40U,"Retained production report masks changed");
                Check(actual.Sequence>0U && ((actual.Command==1U && actual.State==NpV2UsbSelfTestReport.Done) || (actual.Command==5U && actual.State==NpV2UsbSelfTestReport.Held)),"Retained production report state changed");
                actual.RequireUid("001B0019434250062039314E");
                Check(actual.IsNewTerminalFor(actual.Command,actual.Sequence-1U),"Retained report not admitted as a fresh matching result");
                Console.WriteLine("PASS retained production report: sequence="+actual.Sequence+" command="+actual.Command+" checked=0x"+actual.Checked.ToString("X4")+" failed=0x"+actual.Failed.ToString("X4")+" skipped=0x"+actual.Skipped.ToString("X4")+"; selected UID confirmed, no overall-pass claim.");
            }
            SelfTestReportTests();Console.WriteLine("PASS: "+checks+" focused V2 self-test report/admission checks (no hardware accessed).");return 0;
        }
        WireTests(); DecoderTests(); DecodeEveryChannelTests(); LargeCounterTests(); DeferredRawD8DecoderTests(); BufferedWriterTests(); ConcurrentPreviewTests(); StatusTests(); SelfTestReportTests(); ReadWatchdogTests(); TopologyTests();
        if ((args.Length == 1 || args.Length == 2) && args[0] == "--hil-topology")
        {
            string path = args.Length == 2 ? args[1] : null;
            if (String.IsNullOrEmpty(path)) { var paths = NpV2UsbDevice.EnumerateDevicePaths(); Check(paths.Count == 1, "expected exactly one V2 interface"); path = paths[0]; }
            NpV2UsbRoute route = NpV2UsbTopology.Inspect(path);
            Console.WriteLine("V2 USB location: " + (route.LocationPath ?? "unknown") + "; hub_count=" + route.HubCount + "; " + route.Warning);
        }
        if ((args.Length == 1 || args.Length == 2) && args[0] == "--hil-status") HilStatus(args.Length == 2 ? args[1] : null);
        if (args.Length == 4 && args[0] == "--hil-record") HilRecord(args[1], args[2], UInt32.Parse(args[3]));
        if (args.Length == 3 && args[0] == "--hil-profile") HilProfile(args[1], UInt32.Parse(args[2]));
        if (args.Length == 4 && args[0] == "--hil-continuous") HilContinuous(args[1], args[2], Int32.Parse(args[3]));
        if (args.Length == 4 && args[0] == "--hil-continuous-preview") HilContinuousPreview(args[1], args[2], Int32.Parse(args[3]));
        if ((args.Length == 1 || args.Length == 2) && args[0] == "--hil-mode-cycle") HilModeCycle(args.Length == 2 ? args[1] : null);
        if (args.Length != 0 && !((args.Length == 1 || args.Length == 2) && args[0] == "--hil-status") && !((args.Length == 1 || args.Length == 2) && args[0] == "--hil-topology") && !(args.Length == 4 && args[0] == "--hil-record") && !(args.Length == 3 && args[0] == "--hil-profile") && !(args.Length == 4 && args[0] == "--hil-continuous") && !(args.Length == 4 && args[0] == "--hil-continuous-preview") && !((args.Length == 1 || args.Length == 2) && args[0] == "--hil-mode-cycle")) throw new ArgumentException("--hil-status [path], --hil-topology [path], --hil-profile <path> <sample-hz>, --hil-mode-cycle [path], --hil-record <path> <folder> <blocks>, --hil-continuous <path> <folder> <seconds-after-data>, or --hil-continuous-preview <path> <folder> <seconds-after-data>");
        Console.WriteLine("PASS: " + checks + " V2 USB console wire/status checks" + (args.Length == 0 ? " (no hardware accessed)." : ".")); return 0;
    }
}
