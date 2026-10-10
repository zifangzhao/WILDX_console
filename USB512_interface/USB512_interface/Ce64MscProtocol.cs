using System;
using System.IO;

namespace USB512_interface
{
    // CE64 USB-FS MSC wire contract, deliberately independent of USB512 WinUSB.
    internal static class Ce64MscProtocol
    {
        internal const ushort MajorVersion = 1;
        internal const int SectorBytes = 512;
        // Display fallback before C64C is available. Capture always uses the
        // peer-advertised fixed rate and validates its aggregate payload.
        // USB WILD64 production is fixed 10 kHz compressed; the CE128 resident
        // service uses the same raw V1 framing with a topology-dependent rate.
        internal const uint Wild64RateHz = 10000;
        internal const uint Wild64RawV1RateHz = 5000;
        internal const uint Wild64CompressedV2RateHz = 10000;
        internal const uint Wild64CompressedV2ExperimentalRateHz = 20000;
        internal const uint CompressedV2FirstLba = 0x2000;
        internal const uint CompressedV2LastLba = 0x3fff;
        internal const int CompressedV2HeaderBytes = 40;
        internal const int CompressedV2MaxBlockFrames = 32;
        internal const int CompressedV2Q4BlockFrames = 64;
        internal const int CompressedV2MaxPayloadBytes = 4096;
        internal const uint CompressedV2Format = 2;
        // Live C64Z reads use the normal 128-sector PhysicalDrive batch cap;
        // each request is still bounded by the immediately pre-read FIFO floor.
        internal const uint CompressedV2RunningMaxSectors = 128;
        // C64C v2 flags@76: mode-2 decoded CRC may be zero only when this is
        // advertised. Header/payload CRC and all Rice decoded CRCs remain
        // mandatory.
        internal const uint CompressedV2FlagQ8Supported = 1U << 2;
        internal const uint CompressedV2FlagQ8DecodedCrcOptional = 1U << 4;
        internal const uint CompressedV2FlagQ7Supported = 1U << 5;
        internal const uint CompressedV2FlagQ4Supported = 1U << 6;
        internal const uint ZtestFlagNominalUncalibrated = 1U << 0;
        internal const uint ZtestFlagMagnitude = 1U << 1;
        internal const uint ZtestFlagPhase = 1U << 2;
        internal const uint ZtestFlagCancel = 1U << 3;
        internal const ushort ZtestResultFlagValid = 0x0001;
        internal const ushort ZtestResultFlagLowerBound = 0x0002;
        internal const ushort EisResultFlagSparseBelowSixSamples = 0x0004;
        internal const ushort EisResultFlagSparseBelowFourSamples = 0x0008;
        internal const uint Wild64Channels = 64;
        internal const uint Wild64FrameBytes = Wild64Channels * sizeof(short);
        internal const uint Wild64FramesPerSector = SectorBytes / Wild64FrameBytes;
        internal const uint Wild64ImuFrameBytes = 32;
        internal const uint Wild64ImuFramesPerSector = SectorBytes / Wild64ImuFrameBytes;
        // Raw-v1 exposes a 4 MiB trailer. C64Z v2 extends it to 8 MiB so
        // aperture 0x2000..0x3fff is actually within the virtual media.
        internal const uint TrailerSectorsV1 = 0x2000;
        internal const uint TrailerSectorsV2 = 0x4000;
        // WILD64 is the current shipping identity. Its USBSTOR product field
        // is intentionally blank (or normalized to underscores/spaces).
        internal const string PrimaryProductName = "WILD64 USB Device";
        internal const string PrimaryInquiryProduct = "WILD64 USB Device";
        internal const string PrimaryPnpVendorToken = "VEN_WILD64";
        internal const string Ce128ProductName = "CE128 USB Device";
        internal const string Ce128InquiryProduct = "CE128 USB Device";
        internal const string Ce128PnpVendorToken = "VEN_CE128";
        internal const string Ce128PnpIdentity = "VEN_CE128&PROD_CE128_USB_DEVICE";
        // CE64 identities remain valid for previously flashed protocol peers.
        internal const string Ce64Wild64PnpIdentity = "VEN_CE64&PROD_WILD64_USB_MODE";
        internal const string LegacyProductName = "CE64 Stream+SD";
        internal const string LegacyInquiryProduct = "CE64 STREAM SD";
        internal const string LegacyPnpProductToken = "PROD_CE64_STREAM_SD";

        internal static Ce64MscLayout CreateLayout(ulong advertisedBlocks, uint trailerSectors)
        {
            if (trailerSectors != TrailerSectorsV1 && trailerSectors != TrailerSectorsV2)
                throw new ArgumentOutOfRangeException("trailerSectors");
            if (advertisedBlocks < trailerSectors)
                throw new InvalidDataException("CE64 media is smaller than its virtual trailer.");
            if (advertisedBlocks - trailerSectors > UInt32.MaxValue)
                throw new NotSupportedException("CE64 MSC capacity exceeds this version's 32-bit LBA transport.");
            return new Ce64MscLayout(checked((uint)(advertisedBlocks - trailerSectors)), advertisedBlocks, trailerSectors);
        }

        internal static uint Crc32(byte[] bytes, int count)
        {
            return Crc32(bytes, 0, count);
        }

        internal static uint Crc32(byte[] bytes, int offset, int count)
        {
            if (bytes == null || offset < 0 || count < 0 || offset > bytes.Length - count)
                throw new ArgumentOutOfRangeException("count");
            uint crc = 0xffffffffU;
            for (int i = 0; i < count; i++)
            {
                crc ^= bytes[offset + i];
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320U : crc >> 1;
            }
            return ~crc;
        }

        internal static void ValidateCrcAndMagic(byte[] sector, string magic)
        {
            if (sector == null || sector.Length != SectorBytes) throw new InvalidDataException("CE64 sector is short.");
            for (int i = 0; i < 4; i++) if (sector[i] != (byte)magic[i])
                throw new InvalidDataException("CE64 " + magic + " magic is invalid (stale/cached sector).");
            if (ReadUInt32(sector, 508) != Crc32(sector, 508))
                throw new InvalidDataException("CE64 " + magic + " CRC is invalid.");
        }

        internal static uint ReadUInt32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }
        internal static ushort ReadUInt16(byte[] b, int o) { return (ushort)(b[o] | b[o + 1] << 8); }
        internal static ulong ReadUInt64(byte[] b, int o) { return ReadUInt32(b, o) | ((ulong)ReadUInt32(b, o + 4) << 32); }
        internal static void WriteUInt32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        internal static void WriteUInt16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        internal static void WriteMagic(byte[] b, string value) { for (int i = 0; i < 4; i++) b[i] = (byte)value[i]; }
        internal static bool IsAllowedEphysRate(uint rateHz)
        {
            return rateHz >= 1250 && rateHz <= 20000 && (rateHz % 1250) == 0;
        }

        // CE128 uses one 32-channel physical bank per active RHD2132 phase or
        // RHD2164 half.  C64M still uses 512-byte sectors, so a 96-channel
        // frame leaves a zero-filled tail in every sector and must be packed by
        // the host rather than written as a 512-byte multiple.
        internal static bool IsSupportedRawEphysLayout(uint channels, uint frameBytes, uint framesPerSector)
        {
            return channels >= 32 && channels <= 128 && (channels % 32) == 0 &&
                frameBytes == channels * (uint)sizeof(short) && frameBytes <= SectorBytes &&
                framesPerSector == SectorBytes / frameBytes && framesPerSector != 0;
        }

        // Worst permitted compressed wire representation: every ephys sample
        // uses the packed absolute fallback plus one 40-byte block header per
        // 32 production frames, or per 64 experimental Q4 frames. This is an
        // admission bound, not a claim about the
        // decoded amplifier.dat rate (which remains 1.28 MB/s at 10 kHz).
        internal static ulong CompressedV2GuaranteedWireBytesPerSecond(uint rateHz)
        {
            if (rateHz != Wild64CompressedV2RateHz && rateHz != Wild64CompressedV2ExperimentalRateHz)
                throw new ArgumentOutOfRangeException("rateHz");
            uint framesPerBlock = rateHz == Wild64CompressedV2ExperimentalRateHz ?
                (uint)CompressedV2Q4BlockFrames : (uint)CompressedV2MaxBlockFrames;
            ulong blocksPerSecond = ((ulong)rateHz + framesPerBlock - 1U) / framesPerBlock;
            uint bytesPerFrame = rateHz == Wild64CompressedV2ExperimentalRateHz ? 32U : 56U;
            return (ulong)rateHz * bytesPerFrame + blocksPerSecond * CompressedV2HeaderBytes;
        }
    }

    internal sealed class Ce64MscLayout
    {
        internal Ce64MscLayout(uint baseLba, ulong advertisedBlocks, uint trailerSectors) { BaseLba = baseLba; AdvertisedBlocks = advertisedBlocks; TrailerSectors = trailerSectors; }
        internal uint BaseLba { get; private set; }
        internal ulong AdvertisedBlocks { get; private set; }
        internal uint TrailerSectors { get; private set; }
        internal uint ManifestLba { get { return BaseLba; } }
        internal uint SignatureLba { get { return BaseLba + 1; } }
        internal uint LegacyStartLba { get { return BaseLba + 0x30; } }
        internal uint LegacyStopLba { get { return BaseLba + 0x40; } }
        internal uint LegacyResetLba { get { return BaseLba + 0x50; } }
        internal uint StatusLba { get { return BaseLba + 0x80; } }
        internal uint CommandLba { get { return BaseLba + 0x81; } }
        internal uint CapabilitiesLba { get { return BaseLba + 0x82; } }
        internal uint ImuApertureLba { get { return BaseLba + 0x0800; } }
        internal uint ImuApertureLastLba { get { return BaseLba + 0x0fff; } }
        internal uint StreamApertureLba { get { return BaseLba + 0x1000; } }
        internal uint StreamApertureLastLba { get { return BaseLba + 0x1fff; } }
    }

    // ENTER_DFU is accepted only from error-free Idle with no pending data. The
    // firmware ACKs it while still Idle, then defers its system-memory jump long
    // enough for the host to close the PhysicalDrive handle cleanly.
    internal enum Ce64MscCommand : byte { Arm = 1, Start = 2, Stop = 3, Disarm = 4, ClearStats = 5, EnterDfu = 6, StartZtest = 7, CancelZtest = 8, StartEis = 9, CancelEis = 10 }
    // Must stay numerically identical to CE64 firmware's C64S state byte.
    internal enum Ce64MscState : byte { Disabled = 0, Idle = 1, Armed = 2, Streaming = 3, Error = 4, Ztest = 5, Eis = 6 }

    internal sealed class Ce64MscStatus
    {
        internal Ce64MscStatus(Ce64MscState state, ushort flags, uint error, uint ackSequence, uint sessionSequence, uint rateHz, uint channels, uint byteRate, uint pendingFrames, ulong produced, ulong consumed, ulong dropped, ulong underflow, uint imuPendingRecords, ulong imuProduced, ulong imuConsumed, ulong imuDropped, ulong imuUnderflow, ulong imuCompletionCount, uint imuLastCompletionTick, uint imuRetryRecoveryCount, Ce64MscCompressedStatus compressed)
        { State = state; Flags = flags; Error = error; AckSequence = ackSequence; SessionSequence = sessionSequence; RateHz = rateHz; Channels = channels; ByteRate = byteRate; PendingFrames = pendingFrames; Produced = produced; Consumed = consumed; Dropped = dropped; Underflow = underflow; ImuPendingRecords = imuPendingRecords; ImuProduced = imuProduced; ImuConsumed = imuConsumed; ImuDropped = imuDropped; ImuUnderflow = imuUnderflow; ImuCompletionCount = imuCompletionCount; ImuLastCompletionTick = imuLastCompletionTick; ImuRetryRecoveryCount = imuRetryRecoveryCount; Compressed = compressed; }
        internal Ce64MscState State { get; private set; } internal ushort Flags { get; private set; } internal uint Error { get; private set; }
        internal uint AckSequence { get; private set; } internal uint SessionSequence { get; private set; } internal uint RateHz { get; private set; }
        internal uint Channels { get; private set; } internal uint ByteRate { get; private set; } internal uint PendingFrames { get; private set; }
        internal ulong Produced { get; private set; } internal ulong Consumed { get; private set; } internal ulong Dropped { get; private set; } internal ulong Underflow { get; private set; }
        internal uint ImuPendingRecords { get; private set; }
        internal ulong ImuProduced { get; private set; } internal ulong ImuConsumed { get; private set; }
        internal ulong ImuDropped { get; private set; } internal ulong ImuUnderflow { get; private set; }
        // v1 extension: nonzero values advertise firmware-authoritative BMX
        // completion freshness, unlike PCtime/HAL_GetTick in native records.
        internal ulong ImuCompletionCount { get; private set; }
        internal uint ImuLastCompletionTick { get; private set; }
        internal uint ImuRetryRecoveryCount { get; private set; }
        internal Ce64MscCompressedStatus Compressed { get; private set; }
    }
    internal sealed class Ce64MscCapabilities
    {
        internal Ce64MscCapabilities(uint frameBytes, uint framesPerSector, uint safeBytesPerSecond, uint maxChannels, uint fixedRateHz, uint imuFrameBytes, uint imuFramesPerSector, uint imuRateHz, bool imuApertureAvailable, Ce64MscCompressedCapabilities compressed, Ce64MscZtestCapabilities ztest, Ce64MscEisCapabilities eis)
        { FrameBytes = frameBytes; FramesPerSector = framesPerSector; SafeBytesPerSecond = safeBytesPerSecond; MaxChannels = maxChannels; FixedRateHz = fixedRateHz; ImuFrameBytes = imuFrameBytes; ImuFramesPerSector = imuFramesPerSector; ImuRateHz = imuRateHz; ImuApertureAvailable = imuApertureAvailable; Compressed = compressed; Ztest = ztest; Eis = eis; }
        internal uint FrameBytes { get; private set; } internal uint FramesPerSector { get; private set; }
        internal uint SafeBytesPerSecond { get; private set; } internal uint MaxChannels { get; private set; }
        internal uint FixedRateHz { get; private set; } internal uint ImuFrameBytes { get; private set; }
        internal uint ImuFramesPerSector { get; private set; } internal uint ImuRateHz { get; private set; }
        internal bool ImuApertureAvailable { get; private set; }
        internal Ce64MscCompressedCapabilities Compressed { get; private set; }
        internal Ce64MscZtestCapabilities Ztest { get; private set; }
        internal Ce64MscEisCapabilities Eis { get; private set; }
    }

    internal sealed class Ce64MscZtestCapabilities
    {
        internal Ce64MscZtestCapabilities(uint infoLba, uint resultFirstLba, uint resultSectors, uint frequencyHz, uint flags)
        { InfoLba = infoLba; ResultFirstLba = resultFirstLba; ResultSectors = resultSectors; FrequencyHz = frequencyHz; Flags = flags; }
        internal uint InfoLba { get; private set; } internal uint ResultFirstLba { get; private set; } internal uint ResultSectors { get; private set; }
        internal uint FrequencyHz { get; private set; } internal uint Flags { get; private set; }
        internal bool IsNominalUncalibrated { get { return (Flags & Ce64MscProtocol.ZtestFlagNominalUncalibrated) != 0; } }
        internal bool SupportsMagnitude { get { return (Flags & Ce64MscProtocol.ZtestFlagMagnitude) != 0; } }
        internal bool SupportsPhase { get { return (Flags & Ce64MscProtocol.ZtestFlagPhase) != 0; } }
        internal bool SupportsCancel { get { return (Flags & Ce64MscProtocol.ZtestFlagCancel) != 0; } }
    }

    internal enum Ce64MscZtestState : byte { Idle = 0, Preparing = 1, Measuring = 2, Complete = 3, Cancelled = 4, Error = 5 }
    internal sealed class Ce64MscZtestInfo
    {
        internal Ce64MscZtestInfo(Ce64MscZtestState state, ushort error, uint requestSequence, uint completed, uint total, uint frequencyHz, uint samples, uint steps, uint cycles, uint elapsedMilliseconds, uint resultFirstLba, uint resultSectors, uint epoch, uint result0Crc, uint result1Crc, uint flags, uint ioDetail, uint spi2Cr1, uint spi2Cr2, uint spi2Sr, uint spi4Cr1, uint spi4Cr2, uint spi4Sr, uint rccApb1Enr, uint rccApb2Enr, uint gpioaModer, uint gpioaAfrl, uint gpiobModer, uint gpiobAfrh)
        { State = state; Error = error; RequestSequence = requestSequence; Completed = completed; Total = total; FrequencyHz = frequencyHz; Samples = samples; Steps = steps; Cycles = cycles; ElapsedMilliseconds = elapsedMilliseconds; ResultFirstLba = resultFirstLba; ResultSectors = resultSectors; Epoch = epoch; Result0Crc = result0Crc; Result1Crc = result1Crc; Flags = flags; IoDetail = ioDetail; Spi2Cr1 = spi2Cr1; Spi2Cr2 = spi2Cr2; Spi2Sr = spi2Sr; Spi4Cr1 = spi4Cr1; Spi4Cr2 = spi4Cr2; Spi4Sr = spi4Sr; RccApb1Enr = rccApb1Enr; RccApb2Enr = rccApb2Enr; GpioaModer = gpioaModer; GpioaAfrl = gpioaAfrl; GpiobModer = gpiobModer; GpiobAfrh = gpiobAfrh; }
        internal Ce64MscZtestState State { get; private set; } internal ushort Error { get; private set; } internal uint RequestSequence { get; private set; }
        internal uint Completed { get; private set; } internal uint Total { get; private set; } internal uint FrequencyHz { get; private set; } internal uint Samples { get; private set; } internal uint Steps { get; private set; } internal uint Cycles { get; private set; } internal uint ElapsedMilliseconds { get; private set; }
        internal uint ResultFirstLba { get; private set; } internal uint ResultSectors { get; private set; } internal uint Epoch { get; private set; } internal uint Result0Crc { get; private set; } internal uint Result1Crc { get; private set; } internal uint Flags { get; private set; }
        // Firmware-owned diagnostic word at C64I@64. Its packed meaning is
        // logged verbatim for failed START/measurement forensics.
        internal uint IoDetail { get; private set; }
        // Captured only after the first device-side ZTEST transport failure.
        // All remain zero for a non-faulted info record.
        internal uint Spi2Cr1 { get; private set; } internal uint Spi2Cr2 { get; private set; } internal uint Spi2Sr { get; private set; }
        internal uint Spi4Cr1 { get; private set; } internal uint Spi4Cr2 { get; private set; } internal uint Spi4Sr { get; private set; }
        internal uint RccApb1Enr { get; private set; } internal uint RccApb2Enr { get; private set; }
        internal uint GpioaModer { get; private set; } internal uint GpioaAfrl { get; private set; }
        internal uint GpiobModer { get; private set; } internal uint GpiobAfrh { get; private set; }
    }
    internal sealed class Ce64MscZtestResult { internal Ce64MscZtestResult(int channel, uint ohms, short phaseTenths, ushort flags) { Channel = channel; Ohms = ohms; PhaseTenths = phaseTenths; Flags = flags; } internal int Channel { get; private set; } internal uint Ohms { get; private set; } internal short PhaseTenths { get; private set; } internal ushort Flags { get; private set; } }

    internal sealed class Ce64MscEisCapabilities
    {
        internal Ce64MscEisCapabilities(uint infoLba, uint resultFirstLba, uint resultSectors, uint points, uint sampleRateHz, uint minHz, uint maxHz, uint flags)
        { InfoLba = infoLba; ResultFirstLba = resultFirstLba; ResultSectors = resultSectors; Points = points; SampleRateHz = sampleRateHz; MinHz = minHz; MaxHz = maxHz; Flags = flags; }
        internal uint InfoLba { get; private set; } internal uint ResultFirstLba { get; private set; } internal uint ResultSectors { get; private set; }
        internal uint Points { get; private set; } internal uint SampleRateHz { get; private set; } internal uint MinHz { get; private set; } internal uint MaxHz { get; private set; } internal uint Flags { get; private set; }
    }

    internal sealed class Ce64MscEisInfo
    {
        internal Ce64MscEisInfo(Ce64MscZtestState state, ushort error, uint requestSequence, uint channel, uint completed, uint total, uint sampleRateHz, uint minHz, uint maxHz, uint elapsedMilliseconds, uint resultFirstLba, uint resultSectors, uint epoch, uint result0Crc, uint result1Crc, uint flags)
        { State = state; Error = error; RequestSequence = requestSequence; Channel = channel; Completed = completed; Total = total; SampleRateHz = sampleRateHz; MinHz = minHz; MaxHz = maxHz; ElapsedMilliseconds = elapsedMilliseconds; ResultFirstLba = resultFirstLba; ResultSectors = resultSectors; Epoch = epoch; Result0Crc = result0Crc; Result1Crc = result1Crc; Flags = flags; }
        internal Ce64MscZtestState State { get; private set; } internal ushort Error { get; private set; } internal uint RequestSequence { get; private set; } internal uint Channel { get; private set; }
        internal uint Completed { get; private set; } internal uint Total { get; private set; } internal uint SampleRateHz { get; private set; } internal uint MinHz { get; private set; } internal uint MaxHz { get; private set; } internal uint ElapsedMilliseconds { get; private set; }
        internal uint ResultFirstLba { get; private set; } internal uint ResultSectors { get; private set; } internal uint Epoch { get; private set; } internal uint Result0Crc { get; private set; } internal uint Result1Crc { get; private set; } internal uint Flags { get; private set; }
    }

    internal sealed class Ce64MscEisResult
    {
        internal Ce64MscEisResult(uint frequencyHz, uint ohms, short phaseTenths, ushort flags)
        { FrequencyHz = frequencyHz; Ohms = ohms; PhaseTenths = phaseTenths; Flags = flags; }
        internal uint FrequencyHz { get; private set; } internal uint Ohms { get; private set; } internal short PhaseTenths { get; private set; } internal ushort Flags { get; private set; }
    }

    internal sealed class Ce64MscCompressedCapabilities
    {
        internal Ce64MscCompressedCapabilities(uint rateHz, uint blockFrames, uint channels, uint riceK, uint maxPayloadBytes, uint fixedAbsoluteShift, uint fixedAbsoluteBytesPerFrame, uint q7Shift, uint q7BytesPerFrame, uint q4RateHz, uint q4Shift, uint q4BytesPerFrame, uint q4BlockFrames, uint fifoBytes, uint flags, uint firstLba, uint lastLba)
        { RateHz = rateHz; BlockFrames = blockFrames; Channels = channels; RiceK = riceK; MaxPayloadBytes = maxPayloadBytes; FixedAbsoluteShift = fixedAbsoluteShift; FixedAbsoluteBytesPerFrame = fixedAbsoluteBytesPerFrame; Q7Shift = q7Shift; Q7BytesPerFrame = q7BytesPerFrame; Q4RateHz = q4RateHz; Q4Shift = q4Shift; Q4BytesPerFrame = q4BytesPerFrame; Q4BlockFrames = q4BlockFrames; FifoBytes = fifoBytes; Flags = flags; FirstLba = firstLba; LastLba = lastLba; }
        internal uint RateHz { get; private set; } internal uint BlockFrames { get; private set; } internal uint Channels { get; private set; }
        internal uint RiceK { get; private set; } internal uint MaxPayloadBytes { get; private set; } internal uint FixedAbsoluteShift { get; private set; } internal uint FixedAbsoluteBytesPerFrame { get; private set; }
        internal uint Q7Shift { get; private set; } internal uint Q7BytesPerFrame { get; private set; }
        internal uint Q4RateHz { get; private set; } internal uint Q4Shift { get; private set; } internal uint Q4BytesPerFrame { get; private set; } internal uint Q4BlockFrames { get; private set; }
        internal uint FifoBytes { get; private set; } internal uint Flags { get; private set; } internal uint FirstLba { get; private set; } internal uint LastLba { get; private set; }
    }

    internal sealed class Ce64MscCompressedStatus
    {
        internal Ce64MscCompressedStatus(uint activeFormat, uint state, ulong nextBlockSequence, uint fifoPendingBytes, uint fifoHighWaterBytes, uint fifoCapacityBytes, ulong losslessBlocks, ulong lossyBlocks, uint maxQuantShift, uint fifoOverflowBlocks, uint codecErrorBlocks, uint sourcePendingFrames, uint sourceOverrunBlocks, uint codecLastCycles, uint codecMaxCycles, uint codecCycleBudget, uint riceLastCycles, uint riceMaxCycles, uint q8LastCycles, uint q8MaxCycles, uint sourceCapacityFrames, uint q7LastCycles, uint q7MaxCycles, uint q4LastCycles, uint q4MaxCycles)
        { ActiveFormat = activeFormat; State = state; NextBlockSequence = nextBlockSequence; FifoPendingBytes = fifoPendingBytes; FifoHighWaterBytes = fifoHighWaterBytes; FifoCapacityBytes = fifoCapacityBytes; LosslessBlocks = losslessBlocks; LossyBlocks = lossyBlocks; MaxQuantShift = maxQuantShift; FifoOverflowBlocks = fifoOverflowBlocks; CodecErrorBlocks = codecErrorBlocks; SourcePendingFrames = sourcePendingFrames; SourceOverrunBlocks = sourceOverrunBlocks; CodecLastCycles = codecLastCycles; CodecMaxCycles = codecMaxCycles; CodecCycleBudget = codecCycleBudget; RiceLastCycles = riceLastCycles; RiceMaxCycles = riceMaxCycles; Q8LastCycles = q8LastCycles; Q8MaxCycles = q8MaxCycles; SourceCapacityFrames = sourceCapacityFrames; Q7LastCycles = q7LastCycles; Q7MaxCycles = q7MaxCycles; Q4LastCycles = q4LastCycles; Q4MaxCycles = q4MaxCycles; }
        internal uint ActiveFormat { get; private set; } internal uint State { get; private set; } internal ulong NextBlockSequence { get; private set; }
        internal uint FifoPendingBytes { get; private set; } internal uint FifoHighWaterBytes { get; private set; } internal uint FifoCapacityBytes { get; private set; }
        internal ulong LosslessBlocks { get; private set; } internal ulong LossyBlocks { get; private set; } internal uint MaxQuantShift { get; private set; }
        internal uint FifoOverflowBlocks { get; private set; } internal uint CodecErrorBlocks { get; private set; } internal uint SourcePendingFrames { get; private set; } internal uint SourceOverrunBlocks { get; private set; }
        // Optional C64S v2 extension at 200..211. Zero means a peer that
        // predates timing telemetry, preserving wire compatibility.
        internal uint CodecLastCycles { get; private set; } internal uint CodecMaxCycles { get; private set; } internal uint CodecCycleBudget { get; private set; }
        // Optional mode-specific C64S v2 timing extension at 212..227.
        internal uint RiceLastCycles { get; private set; } internal uint RiceMaxCycles { get; private set; }
        internal uint Q8LastCycles { get; private set; } internal uint Q8MaxCycles { get; private set; }
        // Optional source ring capacity at C64S@228; v2 peers that report it
        // must provide at least 512 frames for the fixed 10 kHz contract.
        internal uint SourceCapacityFrames { get; private set; }
        internal uint Q7LastCycles { get; private set; } internal uint Q7MaxCycles { get; private set; }
        internal uint Q4LastCycles { get; private set; } internal uint Q4MaxCycles { get; private set; }
    }
}
