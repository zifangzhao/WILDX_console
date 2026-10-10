using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace USB512_interface
{
    // Use PhysicalDrive, not a mounted volume. The unmanaged page buffer is
    // required because FILE_FLAG_NO_BUFFERING rejects ordinary managed arrays.
    internal sealed class Ce64MscRawBlockDevice : IDisposable
    {
        private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
        private const uint Normal = 0x80, NoBuffering = 0x20000000, WriteThrough = 0x80000000;
        private const uint Commit = 0x1000, Reserve = 0x2000, ReadWrite = 4, Release = 0x8000;
        private const uint IoctlDiskGetLengthInfo = 0x0007405C;
        private readonly SafeFileHandle handle;
        private readonly Ce64MscLayout layout;
        private readonly object ioLock = new object();
        private uint nextSequence;
        private bool hasImuAperture;
        private bool manifestHasCompressedAperture;
        private bool hasCompressedAperture;
        private uint compressedApertureLba;
        private bool disposed;

        private Ce64MscRawBlockDevice(SafeFileHandle value, Ce64MscLayout valueLayout) { handle = value; layout = valueLayout; nextSequence = unchecked((uint)Environment.TickCount); }

        internal static Ce64MscRawBlockDevice Open(Ce64MscDeviceInfo device)
        { return OpenCore(device, GenericRead | GenericWrite, Normal | NoBuffering | WriteThrough); }

        // Diagnostics can validate the manifest/status without requesting any
        // write privilege or making a C64Q control transaction.
        internal static Ce64MscRawBlockDevice OpenReadOnly(Ce64MscDeviceInfo device)
        { return OpenCore(device, GenericRead, Normal | NoBuffering); }

        private static Ce64MscRawBlockDevice OpenCore(Ce64MscDeviceInfo device, uint access, uint flags)
        {
            if (device == null) throw new ArgumentNullException("device");
            SafeFileHandle raw = CreateFile(device.PhysicalDrivePath, access, ShareRead | ShareWrite,
                IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
            if (raw.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "CE64 raw MSC access was denied for " + device.PhysicalDrivePath + ".");
            long bytes;
            uint returned;
            if (!DeviceIoControl(raw, IoctlDiskGetLengthInfo, IntPtr.Zero, 0, out bytes, sizeof(long), out returned, IntPtr.Zero) ||
                returned != sizeof(long) || bytes <= 0 || bytes % Ce64MscProtocol.SectorBytes != 0)
            { raw.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not determine a sector-aligned CE64 disk capacity."); }
            ulong advertisedBlocks = checked((ulong)bytes / Ce64MscProtocol.SectorBytes);
            Exception lastProbeFailure = null;
            uint[] candidates = { Ce64MscProtocol.TrailerSectorsV2, Ce64MscProtocol.TrailerSectorsV1 };
            foreach (uint trailerSectors in candidates)
            {
                try
                {
                    Ce64MscRawBlockDevice candidate = new Ce64MscRawBlockDevice(raw,
                        Ce64MscProtocol.CreateLayout(advertisedBlocks, trailerSectors));
                    candidate.ValidateManifest();
                    return candidate;
                }
                catch (InvalidDataException ex) { lastProbeFailure = ex; }
                catch (NotSupportedException ex) { lastProbeFailure = ex; }
            }
            raw.Dispose();
            throw new InvalidDataException("CE64 manifest was not found at either supported virtual-trailer location.", lastProbeFailure);
        }

        internal Ce64MscLayout Layout { get { return layout; } }
        internal byte[] ReadLegacyStartAlias() { return ReadSector(layout.LegacyStartLba); }
        internal byte[] ReadLegacyStopAlias() { return ReadSector(layout.LegacyStopLba); }
        internal byte[] ReadLegacyResetAlias(bool allowDeferredReset)
        {
            if (!allowDeferredReset)
                throw new InvalidOperationException("CE64 legacy reset requires an explicit deferred-reset opt-in.");
            return ReadSector(layout.LegacyResetLba);
        }
        internal void ValidateManifest()
        {
            byte[] manifest = ReadSector(layout.ManifestLba);
            Ce64MscProtocol.ValidateCrcAndMagic(manifest, "C64M");
            if (manifest[4] != Ce64MscProtocol.MajorVersion || manifest[5] != 1 || Ce64MscProtocol.ReadUInt16(manifest, 6) != 512)
                throw new NotSupportedException("CE64 MSC protocol major version is not supported.");
            if (Ce64MscProtocol.ReadUInt32(manifest, 8) != layout.TrailerSectors ||
                Ce64MscProtocol.ReadUInt32(manifest, 12) != 0x80 || Ce64MscProtocol.ReadUInt32(manifest, 16) != 0x81 ||
                Ce64MscProtocol.ReadUInt32(manifest, 20) != 0x82 || Ce64MscProtocol.ReadUInt32(manifest, 24) != 0x1000 ||
                Ce64MscProtocol.ReadUInt32(manifest, 28) != 0x1fff || Ce64MscProtocol.ReadUInt32(manifest, 32) != 1)
                throw new InvalidDataException("CE64 manifest does not describe the locked v1 trailer layout.");
            uint imuFirst = Ce64MscProtocol.ReadUInt32(manifest, 36);
            uint imuLast = Ce64MscProtocol.ReadUInt32(manifest, 40);
            if ((imuFirst == 0) != (imuLast == 0) ||
                (imuFirst != 0 && (imuFirst != 0x0800 || imuLast != 0x0fff)))
                throw new InvalidDataException("CE64 manifest contains an unsupported IMU aperture.");
            hasImuAperture = imuFirst != 0;
            uint compressedFirst = Ce64MscProtocol.ReadUInt32(manifest, 44);
            uint compressedLast = Ce64MscProtocol.ReadUInt32(manifest, 48);
            if (layout.TrailerSectors == Ce64MscProtocol.TrailerSectorsV2)
            {
                if (compressedFirst != Ce64MscProtocol.CompressedV2FirstLba || compressedLast != Ce64MscProtocol.CompressedV2LastLba)
                    throw new InvalidDataException("CE64 v2 manifest does not describe the locked C64Z aperture.");
                manifestHasCompressedAperture = true;
            }
        }

        internal Ce64MscStatus ReadStatus() { return ParseStatus(ReadSector(layout.StatusLba)); }
        internal Ce64MscCapabilities ReadCapabilities()
        {
            byte[] b = ReadSector(layout.CapabilitiesLba);
            Ce64MscProtocol.ValidateCrcAndMagic(b, "C64C");
            if (b[4] != Ce64MscProtocol.MajorVersion) throw new NotSupportedException("CE64 capabilities protocol version is not supported.");
            uint imuFrameBytes = Ce64MscProtocol.ReadUInt32(b, 28);
            uint imuFramesPerSector = Ce64MscProtocol.ReadUInt32(b, 32);
            if (hasImuAperture && (imuFrameBytes != Ce64MscProtocol.Wild64ImuFrameBytes ||
                imuFramesPerSector != Ce64MscProtocol.Wild64ImuFramesPerSector))
                throw new InvalidDataException("CE64 IMU aperture does not use 16 packed 32-byte MGR_misc records per sector.");
            Ce64MscCompressedCapabilities compressed = ParseCompressedCapabilities(b);
            Ce64MscZtestCapabilities ztest = ParseZtestCapabilities(b);
            Ce64MscEisCapabilities eis = ParseEisCapabilities(b);
            if (manifestHasCompressedAperture != (compressed != null))
                throw new InvalidDataException("CE64 manifest and capabilities disagree about the C64Z aperture.");
            hasCompressedAperture = compressed != null;
            compressedApertureLba = hasCompressedAperture ? checked(layout.BaseLba + compressed.FirstLba) : 0;
            return new Ce64MscCapabilities(Ce64MscProtocol.ReadUInt32(b, 8), Ce64MscProtocol.ReadUInt32(b, 12),
                Ce64MscProtocol.ReadUInt32(b, 16), Ce64MscProtocol.ReadUInt32(b, 20),
                Ce64MscProtocol.ReadUInt32(b, 24), imuFrameBytes, imuFramesPerSector,
                Ce64MscProtocol.ReadUInt32(b, 36), hasImuAperture, compressed, ztest, eis);
        }

        internal Ce64MscEisInfo ReadEisInfo(Ce64MscEisCapabilities caps)
        {
            if (caps == null) throw new NotSupportedException("CE64 does not advertise selected-electrode EIS.");
            byte[] b = ReadSector(layout.BaseLba + caps.InfoLba);
            Ce64MscProtocol.ValidateCrcAndMagic(b, "C64E");
            if (b[4] != 1) throw new NotSupportedException("CE64 EIS info version is not supported.");
            return new Ce64MscEisInfo((Ce64MscZtestState)b[5], Ce64MscProtocol.ReadUInt16(b, 6),
                Ce64MscProtocol.ReadUInt32(b, 8), Ce64MscProtocol.ReadUInt32(b, 12),
                Ce64MscProtocol.ReadUInt32(b, 16), Ce64MscProtocol.ReadUInt32(b, 20),
                Ce64MscProtocol.ReadUInt32(b, 24), Ce64MscProtocol.ReadUInt32(b, 28),
                Ce64MscProtocol.ReadUInt32(b, 32), Ce64MscProtocol.ReadUInt32(b, 36),
                Ce64MscProtocol.ReadUInt32(b, 40), Ce64MscProtocol.ReadUInt32(b, 44),
                Ce64MscProtocol.ReadUInt32(b, 48), Ce64MscProtocol.ReadUInt32(b, 52),
                Ce64MscProtocol.ReadUInt32(b, 56), Ce64MscProtocol.ReadUInt32(b, 60));
        }

        internal IList<Ce64MscEisResult> ReadEisResults(Ce64MscEisInfo info)
        {
            if (info == null || info.State != Ce64MscZtestState.Complete || info.Error != 0 ||
                info.Completed != 64 || info.Total != 64 || info.ResultSectors != 2 ||
                info.ResultFirstLba == 0 || info.Epoch == 0 || info.Result0Crc == 0 || info.Result1Crc == 0)
                throw new InvalidDataException("CE64 EIS metadata is not a completed, CRC-cached 64-point spectrum.");
            List<Ce64MscEisResult> values = new List<Ce64MscEisResult>(64);
            uint previousFrequency = 0;
            for (uint sectorIndex = 0; sectorIndex < 2; sectorIndex++)
            {
                byte[] b = ReadSector(layout.BaseLba + info.ResultFirstLba + sectorIndex);
                Ce64MscProtocol.ValidateCrcAndMagic(b, "C64P");
                if (b[4] != 1 || b[5] != (byte)Ce64MscZtestState.Complete || Ce64MscProtocol.ReadUInt16(b, 6) != 0 ||
                    Ce64MscProtocol.ReadUInt32(b, 8) != info.RequestSequence || Ce64MscProtocol.ReadUInt32(b, 12) != sectorIndex * 32 ||
                    Ce64MscProtocol.ReadUInt32(b, 16) != 32 || Ce64MscProtocol.ReadUInt32(b, 20) != info.Epoch)
                    throw new InvalidDataException("CE64 EIS result header does not match the completed request.");
                uint expectedCrc = sectorIndex == 0 ? info.Result0Crc : info.Result1Crc;
                if (expectedCrc != Ce64MscProtocol.ReadUInt32(b, 508))
                    throw new InvalidDataException("CE64 EIS result CRC does not match C64E.");
                for (int entry = 0; entry < 32; entry++)
                {
                    int offset = 24 + entry * 12;
                    uint frequency = Ce64MscProtocol.ReadUInt16(b, offset);
                    ushort entryFlags = Ce64MscProtocol.ReadUInt16(b, offset + 8);
                    if (frequency <= previousFrequency || (entryFlags & Ce64MscProtocol.ZtestResultFlagValid) == 0 || ((entryFlags >> 4) & 3) == 3)
                        throw new InvalidDataException("CE64 EIS result entry is invalid or out of order.");
                    values.Add(new Ce64MscEisResult(frequency, Ce64MscProtocol.ReadUInt32(b, offset + 2),
                        unchecked((short)Ce64MscProtocol.ReadUInt16(b, offset + 6)), entryFlags));
                    previousFrequency = frequency;
                }
            }
            return values;
        }
        internal Ce64MscZtestInfo ReadZtestInfo(Ce64MscZtestCapabilities caps)
        {
            if (caps == null) throw new NotSupportedException("CE64 does not advertise impedance measurement.");
            byte[] b = ReadSector(layout.BaseLba + caps.InfoLba);
            Ce64MscProtocol.ValidateCrcAndMagic(b, "C64I");
            if (b[4] != 1) throw new NotSupportedException("CE64 impedance info version is not supported.");
            return new Ce64MscZtestInfo((Ce64MscZtestState)b[5], Ce64MscProtocol.ReadUInt16(b, 6), Ce64MscProtocol.ReadUInt32(b, 8),
                Ce64MscProtocol.ReadUInt32(b, 12), Ce64MscProtocol.ReadUInt32(b, 16), Ce64MscProtocol.ReadUInt32(b, 20),
                Ce64MscProtocol.ReadUInt32(b, 24), Ce64MscProtocol.ReadUInt32(b, 28), Ce64MscProtocol.ReadUInt32(b, 32),
                Ce64MscProtocol.ReadUInt32(b, 36), Ce64MscProtocol.ReadUInt32(b, 40), Ce64MscProtocol.ReadUInt32(b, 44),
                Ce64MscProtocol.ReadUInt32(b, 48), Ce64MscProtocol.ReadUInt32(b, 52), Ce64MscProtocol.ReadUInt32(b, 56), Ce64MscProtocol.ReadUInt32(b, 60),
                Ce64MscProtocol.ReadUInt32(b, 64), Ce64MscProtocol.ReadUInt32(b, 68), Ce64MscProtocol.ReadUInt32(b, 72), Ce64MscProtocol.ReadUInt32(b, 76),
                Ce64MscProtocol.ReadUInt32(b, 80), Ce64MscProtocol.ReadUInt32(b, 84), Ce64MscProtocol.ReadUInt32(b, 88),
                Ce64MscProtocol.ReadUInt32(b, 92), Ce64MscProtocol.ReadUInt32(b, 96), Ce64MscProtocol.ReadUInt32(b, 100),
                Ce64MscProtocol.ReadUInt32(b, 104), Ce64MscProtocol.ReadUInt32(b, 108), Ce64MscProtocol.ReadUInt32(b, 112));
        }
        // Diagnostic-only raw reads for a completed C64I/C64R validation
        // failure. Callers must not use this for a stream aperture or before
        // C64I COMPLETE, because result sectors are otherwise not published.
        internal byte[] ReadZtestDiagnosticSector(uint relativeLba)
        {
            if (relativeLba != 0x83 && relativeLba != 0x84 && relativeLba != 0x85)
                throw new ArgumentOutOfRangeException("relativeLba");
            return ReadSector(layout.BaseLba + relativeLba);
        }
        internal IList<Ce64MscZtestResult> ReadZtestResults(Ce64MscZtestInfo info)
        {
            if (info == null || info.State != Ce64MscZtestState.Complete || info.Error != 0 || info.Completed != 64 || info.Total != 64 ||
                info.ResultSectors != 2 || info.ResultFirstLba == 0 || info.Epoch == 0 || info.Result0Crc == 0 || info.Result1Crc == 0)
                throw new InvalidDataException("CE64 impedance result metadata is not a completed, CRC-cached 64-channel result.");
            List<Ce64MscZtestResult> values = new List<Ce64MscZtestResult>(64);
            for (uint sectorIndex = 0; sectorIndex < 2; sectorIndex++)
            {
                byte[] b = ReadSector(layout.BaseLba + info.ResultFirstLba + sectorIndex);
                Ce64MscProtocol.ValidateCrcAndMagic(b, "C64R");
                // Firmware publishes the C64I COMPLETE state in C64R@5.
                // C64R is never read before C64I COMPLETE, so require that
                // exact ready state rather than the older assumed zero value.
                if (b[4] != 1 || b[5] != (byte)Ce64MscZtestState.Complete || Ce64MscProtocol.ReadUInt16(b, 6) != 0 ||
                    Ce64MscProtocol.ReadUInt32(b, 8) != info.RequestSequence || Ce64MscProtocol.ReadUInt32(b, 12) != sectorIndex * 32 ||
                    Ce64MscProtocol.ReadUInt32(b, 16) != 32 || Ce64MscProtocol.ReadUInt32(b, 20) != info.Epoch)
                    throw new InvalidDataException("CE64 impedance result header does not match the completed request.");
                uint expectedCrc = sectorIndex == 0 ? info.Result0Crc : info.Result1Crc;
                if (expectedCrc != Ce64MscProtocol.ReadUInt32(b, 508))
                    throw new InvalidDataException("CE64 impedance result CRC does not match C64I.");
                for (int entry = 0; entry < 32; entry++)
                {
                    int offset = 24 + entry * 8;
                    ushort entryFlags = Ce64MscProtocol.ReadUInt16(b, offset + 6);
                    if ((entryFlags & 1) == 0 || ((entryFlags >> 4) & 3) == 3)
                        throw new InvalidDataException("CE64 impedance result entry is invalid.");
                    values.Add(new Ce64MscZtestResult((int)(sectorIndex * 32 + entry), Ce64MscProtocol.ReadUInt32(b, offset),
                        unchecked((short)Ce64MscProtocol.ReadUInt16(b, offset + 4)), entryFlags));
                }
            }
            return values;
        }
        internal Ce64MscStatus Execute(Ce64MscCommand command, byte flags, byte channels, uint rateHz, TimeSpan timeout, CancellationToken cancel)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            uint sequence = WriteCommand(command, flags, channels, rateHz);
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancel.ThrowIfCancellationRequested();
                Ce64MscStatus status = ReadStatus();
                if (status.AckSequence == sequence)
                {
                    if (status.State == Ce64MscState.Error || status.Error != 0)
                        throw new Ce64MscProtocolException("CE64 command " + command + " failed, device code " + status.Error + ".", status.Error);
                    if (HasReachedExpectedState(command, status.State))
                        return status;
                }
                Thread.Sleep(5);
            }
            throw new TimeoutException("CE64 command " + command + " timed out.");
        }

        // DFU is different from normal C64Q commands: after its Idle ACK, the
        // firmware intentionally disconnects into STM32 system-memory DFU. Do
        // not accept a raw-drive disconnect as success, because it could be an
        // unrelated USB failure; the matching ACK is the host's proof that the
        // guarded request was accepted before it disposes the storage handle.
        internal Ce64MscDfuRequestResult RequestDfuFromIdle(TimeSpan timeout, CancellationToken cancel)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            Ce64MscStatus initial = ReadStatus();
            if (initial.State != Ce64MscState.Idle || initial.Error != 0 ||
                initial.PendingFrames != 0 || initial.ImuPendingRecords != 0)
                throw new InvalidOperationException("CE64 must be error-free Idle with no pending ephys or IMU data before entering DFU.");

            uint sequence = WriteCommand(Ce64MscCommand.EnterDfu, 0, 0, 0);
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancel.ThrowIfCancellationRequested();
                Ce64MscStatus status = ReadStatus();
                if (status.AckSequence == sequence)
                {
                    if (status.State == Ce64MscState.Error || status.Error != 0)
                        throw new Ce64MscProtocolException("CE64 ENTER_DFU failed, device code " + status.Error + ".", status.Error);
                    if (status.State != Ce64MscState.Idle)
                        throw new Ce64MscProtocolException("CE64 ENTER_DFU acknowledgement did not preserve Idle state.", status.Error);
                    return new Ce64MscDfuRequestResult(sequence, status);
                }
                Thread.Sleep(5);
            }
            throw new TimeoutException("CE64 ENTER_DFU did not return a matching Idle acknowledgement.");
        }

        private uint WriteCommand(Ce64MscCommand command, byte flags, byte channels, uint rateHz)
        {
            uint sequence = unchecked(++nextSequence);
            byte[] request = new byte[Ce64MscProtocol.SectorBytes];
            Ce64MscProtocol.WriteMagic(request, "C64Q");
            request[4] = (byte)Ce64MscProtocol.MajorVersion;
            request[5] = (byte)command;
            request[6] = flags;
            request[7] = channels;
            Ce64MscProtocol.WriteUInt32(request, 8, rateHz);
            Ce64MscProtocol.WriteUInt32(request, 12, sequence);
            Ce64MscProtocol.WriteUInt32(request, 508, Ce64MscProtocol.Crc32(request, 508));
            WriteSector(layout.CommandLba, request);
            return sequence;
        }

        // MSC BOT command writes can remain queued behind a final data read.
        // The firmware therefore also accepts this read-triggered STOP alias;
        // completion is defined solely by Streaming -> Armed, not C64Q ACK.
        internal Ce64MscStatus ExecuteFastStop(TimeSpan timeout, CancellationToken cancel)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            ReadLegacyStopAlias();
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancel.ThrowIfCancellationRequested();
                Ce64MscStatus status = ReadStatus();
                if (status.State == Ce64MscState.Error || status.Error != 0)
                    throw new Ce64MscProtocolException("CE64 fast STOP failed, device code " + status.Error + ".", status.Error);
                if (status.State == Ce64MscState.Armed)
                    return status;
                Thread.Sleep(2);
            }
            throw new TimeoutException("CE64 fast STOP did not reach Armed state.");
        }

        // Counters survive DISARM. Clear only from a confirmed idle boundary so
        // each newly armed recorder session has self-contained loss accounting.
        internal Ce64MscStatus ClearStatsWhenIdle(TimeSpan timeout, CancellationToken cancel)
        {
            Ce64MscStatus initial = ReadStatus();
            if (initial.State != Ce64MscState.Idle || initial.Error != 0)
                throw new InvalidOperationException("CE64 must be Idle and error-free before clearing capture counters.");
            Ce64MscStatus cleared = Execute(Ce64MscCommand.ClearStats, 0, 0, 0, timeout, cancel);
            if (cleared.State == Ce64MscState.Error || cleared.Error != 0 ||
                cleared.PendingFrames != 0 || cleared.Produced != 0 || cleared.Consumed != 0 || cleared.Dropped != 0 || cleared.Underflow != 0 ||
                cleared.ImuPendingRecords != 0 || cleared.ImuProduced != 0 || cleared.ImuConsumed != 0 || cleared.ImuDropped != 0 || cleared.ImuUnderflow != 0 ||
                cleared.ImuCompletionCount != 0 || cleared.ImuLastCompletionTick != 0 || cleared.ImuRetryRecoveryCount != 0)
                throw new Ce64MscProtocolException("CE64 did not clear all capture counters from Idle.", cleared.Error);
            Ce64MscCompressedStatus compressed = cleared.Compressed;
            // NextBlockSequence resets in START, not CLEAR_STATS. Likewise
            // SourceCapacityFrames is persistent/reallocated configuration
            // (often 32 while Idle, then 1000 after ARM), not a counter.
            if (compressed != null && (compressed.FifoPendingBytes != 0 ||
                compressed.FifoHighWaterBytes != 0 || compressed.LosslessBlocks != 0 || compressed.LossyBlocks != 0 ||
                compressed.MaxQuantShift != 0 || compressed.FifoOverflowBlocks != 0 || compressed.CodecErrorBlocks != 0 ||
                compressed.SourcePendingFrames != 0 || compressed.SourceOverrunBlocks != 0))
                throw new Ce64MscProtocolException("CE64 did not clear C64Z state from Idle: fifo=" + compressed.FifoPendingBytes + "/" + compressed.FifoHighWaterBytes + " L/Q=" +
                    compressed.LosslessBlocks + "/" + compressed.LossyBlocks + " shift=" + compressed.MaxQuantShift +
                    " errors=" + compressed.FifoOverflowBlocks + "/" + compressed.CodecErrorBlocks + "/" + compressed.SourceOverrunBlocks +
                    " sourcePending=" + compressed.SourcePendingFrames + " sourceCapacity(config)=" + compressed.SourceCapacityFrames + ".", cleared.Error);
            return cleared;
        }

        private static bool HasReachedExpectedState(Ce64MscCommand command, Ce64MscState state)
        {
            switch (command)
            {
                case Ce64MscCommand.Arm: return state == Ce64MscState.Armed;
                case Ce64MscCommand.Start: return state == Ce64MscState.Streaming;
                case Ce64MscCommand.Stop: return state == Ce64MscState.Armed;
                case Ce64MscCommand.Disarm: return state == Ce64MscState.Idle;
                case Ce64MscCommand.ClearStats: return state != Ce64MscState.Error;
                case Ce64MscCommand.StartZtest: return state == Ce64MscState.Ztest;
                case Ce64MscCommand.CancelZtest: return state == Ce64MscState.Idle;
                case Ce64MscCommand.StartEis: return state == Ce64MscState.Eis;
                case Ce64MscCommand.CancelEis: return state == Ce64MscState.Idle;
                default: return false;
            }
        }

        // DATA sectors are deliberately headerless: each is four packed rows of
        // 64 little-endian int16 samples. Batch raw reads for FS BOT efficiency.
        internal byte[] ReadStreamSectors(uint apertureOffset, int sectorCount, CancellationToken cancel)
        {
            if (apertureOffset >= 0x1000 || sectorCount < 1 || sectorCount > 128 || apertureOffset + sectorCount > 0x1000)
                throw new ArgumentOutOfRangeException("sectorCount");
            cancel.ThrowIfCancellationRequested();
            return ReadSectors(layout.StreamApertureLba + apertureOffset, sectorCount);
        }

        // V2 is a sequential byte FIFO. Each 512-byte READ consumes its next
        // 512 stream bytes, so callers must never seek/retry/reorder an LBA.
        // The LBA is only an aperture address accepted by firmware; this method
        // deliberately advances locally and wraps at the advertised v2 range.
        private uint compressedApertureOffset;
        internal byte[] ReadCompressedSectors(int sectorCount, CancellationToken cancel)
        {
            if (!hasCompressedAperture)
                throw new InvalidOperationException("C64Z aperture was not advertised by this manifest/capabilities pair.");
            if (sectorCount < 1 || sectorCount > 128) throw new ArgumentOutOfRangeException("sectorCount");
            cancel.ThrowIfCancellationRequested();
            if (compressedApertureOffset + (uint)sectorCount >
                Ce64MscProtocol.CompressedV2LastLba - Ce64MscProtocol.CompressedV2FirstLba + 1U)
                compressedApertureOffset = 0;
            byte[] result = ReadSectors(compressedApertureLba + compressedApertureOffset, sectorCount);
            compressedApertureOffset += (uint)sectorCount;
            if (compressedApertureOffset == Ce64MscProtocol.CompressedV2LastLba - Ce64MscProtocol.CompressedV2FirstLba + 1U)
                compressedApertureOffset = 0;
            return result;
        }

        // Native MGR_misc records are kept in a separate consumer aperture so
        // an IMU reader can never consume or reorder ephys frames.
        internal byte[] ReadImuSectors(uint apertureOffset, int sectorCount, CancellationToken cancel)
        {
            if (!hasImuAperture)
                throw new NotSupportedException("CE64 manifest does not expose an IMU aperture.");
            if (apertureOffset >= 0x0800 || sectorCount < 1 || sectorCount > 128 || apertureOffset + sectorCount > 0x0800)
                throw new ArgumentOutOfRangeException("sectorCount");
            cancel.ThrowIfCancellationRequested();
            return ReadSectors(layout.ImuApertureLba + apertureOffset, sectorCount);
        }

        internal byte[] ReadSector(uint lba) { return ReadSectors(lba, 1); }
        // Normal SD sectors remain pass-through MSC storage. This explicit API
        // prevents a downloader from accidentally reading the virtual trailer.
        internal byte[] ReadSdSectors(uint sdLba, int sectorCount)
        {
            if (sectorCount < 1 || (ulong)sdLba + (uint)sectorCount > layout.BaseLba)
                throw new ArgumentOutOfRangeException("sectorCount", "Requested SD blocks overlap the CE64 virtual trailer.");
            return ReadSectors(sdLba, sectorCount);
        }
        internal byte[] ReadSectors(uint lba, int sectorCount)
        {
            if (sectorCount < 1 || sectorCount > 128) throw new ArgumentOutOfRangeException("sectorCount");
            int byteCount = checked(sectorCount * 512);
            lock (ioLock)
            {
                ThrowIfDisposed(); IntPtr buffer = Allocate(byteCount);
                try { Seek(lba); uint read; if (!ReadFile(handle, buffer, (uint)byteCount, out read, IntPtr.Zero)) throw Transport("read", lba, Marshal.GetLastWin32Error());
                    if (read != byteCount) throw new IOException("CE64 raw read was short at LBA " + lba + "."); byte[] result = new byte[byteCount]; Marshal.Copy(buffer, result, 0, byteCount); return result; }
                finally { VirtualFree(buffer, UIntPtr.Zero, Release); }
            }
        }
        internal void WriteSector(uint lba, byte[] sector)
        {
            if (sector == null || sector.Length != 512) throw new ArgumentException("CE64 command writes are exactly 512 bytes.", "sector");
            lock (ioLock)
            {
                ThrowIfDisposed(); IntPtr buffer = Allocate(512);
                try { Marshal.Copy(sector, 0, buffer, 512); Seek(lba); uint written; if (!WriteFile(handle, buffer, 512, out written, IntPtr.Zero)) throw Transport("write", lba, Marshal.GetLastWin32Error());
                    if (written != 512) throw new IOException("CE64 raw write was short at LBA " + lba + "."); }
                finally { VirtualFree(buffer, UIntPtr.Zero, Release); }
            }
        }
        private void Seek(uint lba) { long pos; if (!SetFilePointerEx(handle, checked((long)lba * 512), out pos, 0)) throw Transport("seek", lba, Marshal.GetLastWin32Error()); }
        private static IntPtr Allocate(int bytes) { IntPtr p = VirtualAlloc(IntPtr.Zero, (UIntPtr)bytes, Commit | Reserve, ReadWrite); if (p == IntPtr.Zero) throw new OutOfMemoryException("CE64 sector buffer allocation failed."); return p; }
        private static Exception Transport(string op, uint lba, int error) { return new Ce64MscTransportException("CE64 " + op + " failed at LBA " + lba + " (Win32 " + error + ").", error, IsReconnectError(error)); }
        internal static bool IsReconnectError(int error) { return error == 6 || error == 21 || error == 31 || error == 1117 || error == 1167 || error == 995; }
        private static Ce64MscCompressedCapabilities ParseCompressedCapabilities(byte[] b)
        {
            if (b[40] != (byte)'C' || b[41] != (byte)'6' || b[42] != (byte)'4' || b[43] != (byte)'V') return null;
            if (Ce64MscProtocol.ReadUInt32(b, 44) != 2) throw new NotSupportedException("CE64 compressed capability version is not supported.");
            uint packedQ8 = Ce64MscProtocol.ReadUInt32(b, 68);
            uint q8BytesPerFrame = packedQ8 & 0xffffU;
            uint q8Shift = packedQ8 >> 16;
            uint packedQ7 = Ce64MscProtocol.ReadUInt32(b, 88);
            uint q7BytesPerFrame = packedQ7 & 0xffffU;
            uint q7Shift = packedQ7 >> 16;
            uint packedQ4 = Ce64MscProtocol.ReadUInt32(b, 96);
            uint q4BytesPerFrame = packedQ4 & 0xffffU;
            uint q4Shift = packedQ4 >> 16;
            Ce64MscCompressedCapabilities result = new Ce64MscCompressedCapabilities(
                Ce64MscProtocol.ReadUInt32(b, 48), Ce64MscProtocol.ReadUInt32(b, 52), Ce64MscProtocol.ReadUInt32(b, 56),
                Ce64MscProtocol.ReadUInt32(b, 60), Ce64MscProtocol.ReadUInt32(b, 64), q8Shift, q8BytesPerFrame, q7Shift, q7BytesPerFrame,
                Ce64MscProtocol.ReadUInt32(b, 92), q4Shift, q4BytesPerFrame, Ce64MscProtocol.ReadUInt32(b, 100),
                Ce64MscProtocol.ReadUInt32(b, 72), Ce64MscProtocol.ReadUInt32(b, 76), Ce64MscProtocol.ReadUInt32(b, 80), Ce64MscProtocol.ReadUInt32(b, 84));
            if (result.RateHz != Ce64MscProtocol.Wild64CompressedV2RateHz || result.BlockFrames != Ce64MscProtocol.CompressedV2MaxBlockFrames ||
                result.Channels != Ce64MscProtocol.Wild64Channels || result.RiceK != 7 || result.MaxPayloadBytes != Ce64MscProtocol.CompressedV2MaxPayloadBytes ||
                result.FirstLba != Ce64MscProtocol.CompressedV2FirstLba || result.LastLba != Ce64MscProtocol.CompressedV2LastLba)
                throw new NotSupportedException("CE64 compressed capabilities do not match the locked v2 wire contract.");
            if ((result.Flags & (Ce64MscProtocol.CompressedV2FlagQ8Supported | Ce64MscProtocol.CompressedV2FlagQ8DecodedCrcOptional)) != 0 &&
                (result.FixedAbsoluteShift != 8 || result.FixedAbsoluteBytesPerFrame != Ce64MscProtocol.Wild64Channels))
                throw new NotSupportedException("CE64 packed Q8 capabilities do not match the locked v2 wire contract.");
            if ((result.Flags & Ce64MscProtocol.CompressedV2FlagQ7Supported) != 0 &&
                (result.Q7Shift != 9 || result.Q7BytesPerFrame != 56))
                throw new NotSupportedException("CE64 packed Q7 capabilities do not match the locked v2 wire contract.");
            if ((result.Flags & Ce64MscProtocol.CompressedV2FlagQ4Supported) != 0 &&
                (result.Q4RateHz != Ce64MscProtocol.Wild64CompressedV2ExperimentalRateHz || result.Q4Shift != 12 || result.Q4BytesPerFrame != 32 ||
                result.Q4BlockFrames != Ce64MscProtocol.CompressedV2Q4BlockFrames))
                throw new NotSupportedException("CE64 packed Q4 capabilities do not match the locked experimental wire contract.");
            return result;
        }
        private static Ce64MscZtestCapabilities ParseZtestCapabilities(byte[] b)
        {
            if (b[104] != (byte)'C' || b[105] != (byte)'6' || b[106] != (byte)'4' || b[107] != (byte)'I') return null;
            if (Ce64MscProtocol.ReadUInt32(b, 108) != 1) throw new NotSupportedException("CE64 impedance capability version is not supported.");
            Ce64MscZtestCapabilities result = new Ce64MscZtestCapabilities(Ce64MscProtocol.ReadUInt32(b, 112), Ce64MscProtocol.ReadUInt32(b, 116),
                Ce64MscProtocol.ReadUInt32(b, 120), Ce64MscProtocol.ReadUInt32(b, 124), Ce64MscProtocol.ReadUInt32(b, 128));
            if (result.InfoLba != 0x83 || result.ResultFirstLba != 0x84 || result.ResultSectors != 2 || result.FrequencyHz == 0 ||
                !result.IsNominalUncalibrated || !result.SupportsMagnitude || !result.SupportsPhase || !result.SupportsCancel)
                throw new NotSupportedException("CE64 impedance capabilities do not match the locked wire contract.");
            return result;
        }
        private static Ce64MscEisCapabilities ParseEisCapabilities(byte[] b)
        {
            if (b[132] != (byte)'C' || b[133] != (byte)'6' || b[134] != (byte)'4' || b[135] != (byte)'E') return null;
            if (Ce64MscProtocol.ReadUInt32(b, 136) != 1) throw new NotSupportedException("CE64 EIS capability version is not supported.");
            Ce64MscEisCapabilities result = new Ce64MscEisCapabilities(Ce64MscProtocol.ReadUInt32(b, 140),
                Ce64MscProtocol.ReadUInt32(b, 144), Ce64MscProtocol.ReadUInt32(b, 148),
                Ce64MscProtocol.ReadUInt32(b, 152), Ce64MscProtocol.ReadUInt32(b, 156),
                Ce64MscProtocol.ReadUInt32(b, 160), Ce64MscProtocol.ReadUInt32(b, 164),
                Ce64MscProtocol.ReadUInt32(b, 168));
            if (result.InfoLba != 0x86 || result.ResultFirstLba != 0x87 || result.ResultSectors != 2 ||
                result.Points < 50 || result.Points != 64 || result.SampleRateHz != 30000 ||
                result.MinHz != 100 || result.MaxHz != 10000 || (result.Flags & 0x1fU) != 0x1fU)
                throw new NotSupportedException("CE64 selective EIS capabilities do not match the locked 64-point wire contract.");
            return result;
        }
        private static Ce64MscCompressedStatus ParseCompressedStatus(byte[] b)
        {
            if (b[128] != (byte)'C' || b[129] != (byte)'6' || b[130] != (byte)'4' || b[131] != (byte)'V') return null;
            if (Ce64MscProtocol.ReadUInt32(b, 132) != 2) throw new NotSupportedException("CE64 compressed status version is not supported.");
            return new Ce64MscCompressedStatus(Ce64MscProtocol.ReadUInt32(b, 136), Ce64MscProtocol.ReadUInt32(b, 140),
                Ce64MscProtocol.ReadUInt64(b, 144), Ce64MscProtocol.ReadUInt32(b, 152), Ce64MscProtocol.ReadUInt32(b, 156),
                Ce64MscProtocol.ReadUInt32(b, 160), Ce64MscProtocol.ReadUInt64(b, 164), Ce64MscProtocol.ReadUInt64(b, 172),
                Ce64MscProtocol.ReadUInt32(b, 180), Ce64MscProtocol.ReadUInt32(b, 184), Ce64MscProtocol.ReadUInt32(b, 188),
                Ce64MscProtocol.ReadUInt32(b, 192), Ce64MscProtocol.ReadUInt32(b, 196), Ce64MscProtocol.ReadUInt32(b, 200),
                Ce64MscProtocol.ReadUInt32(b, 204), Ce64MscProtocol.ReadUInt32(b, 208), Ce64MscProtocol.ReadUInt32(b, 212),
                Ce64MscProtocol.ReadUInt32(b, 216), Ce64MscProtocol.ReadUInt32(b, 220), Ce64MscProtocol.ReadUInt32(b, 224),
                Ce64MscProtocol.ReadUInt32(b, 228), Ce64MscProtocol.ReadUInt32(b, 232), Ce64MscProtocol.ReadUInt32(b, 236),
                Ce64MscProtocol.ReadUInt32(b, 240), Ce64MscProtocol.ReadUInt32(b, 244));
        }
        private static Ce64MscStatus ParseStatus(byte[] b) { Ce64MscProtocol.ValidateCrcAndMagic(b, "C64S"); if (b[4] != Ce64MscProtocol.MajorVersion) throw new NotSupportedException("CE64 status protocol version is not supported."); return new Ce64MscStatus((Ce64MscState)b[5], Ce64MscProtocol.ReadUInt16(b, 6), Ce64MscProtocol.ReadUInt32(b, 8), Ce64MscProtocol.ReadUInt32(b, 12), Ce64MscProtocol.ReadUInt32(b, 16), Ce64MscProtocol.ReadUInt32(b, 20), Ce64MscProtocol.ReadUInt32(b, 24), Ce64MscProtocol.ReadUInt32(b, 28), Ce64MscProtocol.ReadUInt32(b, 32), Ce64MscProtocol.ReadUInt64(b, 40), Ce64MscProtocol.ReadUInt64(b, 48), Ce64MscProtocol.ReadUInt64(b, 56), Ce64MscProtocol.ReadUInt64(b, 64), Ce64MscProtocol.ReadUInt32(b, 72), Ce64MscProtocol.ReadUInt64(b, 80), Ce64MscProtocol.ReadUInt64(b, 88), Ce64MscProtocol.ReadUInt64(b, 96), Ce64MscProtocol.ReadUInt64(b, 104), Ce64MscProtocol.ReadUInt64(b, 112), Ce64MscProtocol.ReadUInt32(b, 120), Ce64MscProtocol.ReadUInt32(b, 124), ParseCompressedStatus(b)); }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException("Ce64MscRawBlockDevice"); }
        // Closing the synchronous raw handle is the reliable cancellation path
        // when a USB mass-storage read is stalled in the Windows storage stack.
        internal void Abort() { Dispose(); }
        public void Dispose() { if (!disposed) { disposed = true; handle.Dispose(); } }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string p, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle f, uint code, IntPtr input, uint inputBytes, out long output, uint outputBytes, out uint returnedBytes, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFilePointerEx(SafeFileHandle f, long distance, out long position, uint method);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadFile(SafeFileHandle f, IntPtr b, uint n, out uint done, IntPtr o);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WriteFile(SafeFileHandle f, IntPtr b, uint n, out uint done, IntPtr o);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr n, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualFree(IntPtr a, UIntPtr n, uint type);
    }
    internal sealed class Ce64MscDfuRequestResult
    {
        internal Ce64MscDfuRequestResult(uint sequence, Ce64MscStatus acknowledgedStatus)
        { Sequence = sequence; AcknowledgedStatus = acknowledgedStatus; }
        internal uint Sequence { get; private set; }
        internal Ce64MscStatus AcknowledgedStatus { get; private set; }
        internal bool Acknowledged { get { return AcknowledgedStatus != null; } }
    }
    internal sealed class Ce64MscTransportException : IOException { internal Ce64MscTransportException(string m, int e, bool r) : base(m) { NativeError = e; RequiresReconnect = r; } internal int NativeError { get; private set; } internal bool RequiresReconnect { get; private set; } }
    internal sealed class Ce64MscProtocolException : IOException { internal Ce64MscProtocolException(string m, uint c) : base(m) { DeviceCode = c; } internal uint DeviceCode { get; private set; } }
}
