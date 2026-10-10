using System;
using System.Collections.Generic;
using System.IO;

namespace USB512_interface
{
    // Strict decoder for the locked C64Z v2 block contract. It emits the same
    // chronological frame-major little-endian int16 bytes used by raw v1, so
    // amplifier.dat and the preview require no lossy-specific data path.
    internal sealed class Ce64MscCompressedDecoder
    {
        private readonly List<byte> pending = new List<byte>();
        private readonly bool allowQ8DecodedCrcOmitted;
        private readonly bool allowQ7;
        private readonly bool allowQ4;
        private uint nextSequence;
        private bool haveSequence;
        private ulong decodedFrames;

        internal ulong DecodedFrames { get { return decodedFrames; } }
        // Number of encoded bytes removed only after a complete block passed
        // every validation. It is the absolute offset of the pending header.
        internal ulong ConsumedEncodedBytes { get; private set; }

        internal Ce64MscCompressedDecoder(bool allowQ8DecodedCrcOmitted = false, bool allowQ7 = false, bool allowQ4 = false)
        {
            this.allowQ8DecodedCrcOmitted = allowQ8DecodedCrcOmitted;
            this.allowQ7 = allowQ7;
            this.allowQ4 = allowQ4;
        }

        internal void Append(byte[] bytes, int count)
        {
            if (bytes == null || count < 0 || count > bytes.Length) throw new ArgumentOutOfRangeException("count");
            for (int i = 0; i < count; i++) pending.Add(bytes[i]);
        }

        internal bool TryDecode(out Ce64MscCompressedBlock block)
        {
            block = null;
            if (pending.Count < Ce64MscProtocol.CompressedV2HeaderBytes) return false;
            RequireMagic();
            if (pending[4] != 2) throw new InvalidDataException("C64Z version is invalid.");
            byte mode = pending[5], shift = pending[6], riceK = pending[7];
            uint sequence = Ce64MscProtocol.ReadUInt32(pending.ToArray(), 8);
            ushort sampleCount = Ce64MscProtocol.ReadUInt16(pending.ToArray(), 12);
            ushort channelCount = Ce64MscProtocol.ReadUInt16(pending.ToArray(), 14);
            ushort payloadBytes = Ce64MscProtocol.ReadUInt16(pending.ToArray(), 16);
            ushort headerBytes = Ce64MscProtocol.ReadUInt16(pending.ToArray(), 18);
            uint decodedBytes = Ce64MscProtocol.ReadUInt32(pending.ToArray(), 20);
            uint payloadCrc = Ce64MscProtocol.ReadUInt32(pending.ToArray(), 24);
            uint decodedCrc = Ce64MscProtocol.ReadUInt32(pending.ToArray(), 28);
            uint headerCrc = Ce64MscProtocol.ReadUInt32(pending.ToArray(), 32);
            uint reserved = Ce64MscProtocol.ReadUInt32(pending.ToArray(), 36);
            int maximumSamples = mode == 4 ? Ce64MscProtocol.CompressedV2Q4BlockFrames : Ce64MscProtocol.CompressedV2MaxBlockFrames;
            if (mode > 4 || sampleCount < 1 || sampleCount > maximumSamples ||
                channelCount != Ce64MscProtocol.Wild64Channels || headerBytes != Ce64MscProtocol.CompressedV2HeaderBytes ||
                payloadBytes > Ce64MscProtocol.CompressedV2MaxPayloadBytes || decodedBytes != (uint)sampleCount * Ce64MscProtocol.Wild64FrameBytes || reserved != 0)
                throw new InvalidDataException("C64Z header fields are invalid.");
            byte[] header = pending.GetRange(0, 32).ToArray();
            if (Ce64MscProtocol.Crc32(header, header.Length) != headerCrc)
                throw new InvalidDataException("C64Z header CRC is invalid.");
            if ((mode == 0 && (shift != 0 || riceK != 7)) ||
                (mode == 1 && (shift < 1 || shift > 7 || riceK != 7)) ||
                (mode == 2 && (shift != 8 || riceK != 0xff)) ||
                (mode == 3 && (shift != 9 || riceK != 0xff || !allowQ7)) ||
                (mode == 4 && (shift != 12 || riceK != 0xff || !allowQ4)))
                throw new InvalidDataException("C64Z mode, quantization shift, or Rice parameter is invalid.");
            int total = headerBytes + payloadBytes;
            if (pending.Count < total) return false;
            if (haveSequence && sequence != nextSequence)
                throw new InvalidDataException("C64Z block sequence discontinuity: expected " + nextSequence + ", got " + sequence + ".");
            byte[] payload = pending.GetRange(headerBytes, payloadBytes).ToArray();
            if (Ce64MscProtocol.Crc32(payload, payload.Length) != payloadCrc)
                throw new InvalidDataException("C64Z payload CRC is invalid.");
            byte[] decoded = DecodePayload(payload, sampleCount, mode, shift);
            bool absoluteCrcOmitted = (mode == 2 && decodedCrc == 0 && allowQ8DecodedCrcOmitted) ||
                (mode == 3 && decodedCrc == 0 && allowQ7);
            absoluteCrcOmitted = absoluteCrcOmitted || (mode == 4 && decodedCrc == 0 && allowQ4);
            if (!absoluteCrcOmitted && Ce64MscProtocol.Crc32(decoded, decoded.Length) != decodedCrc)
                throw new InvalidDataException("C64Z decoded CRC is invalid.");
            pending.RemoveRange(0, total);
            ConsumedEncodedBytes += (uint)total;
            nextSequence = unchecked(sequence + 1); haveSequence = true; decodedFrames += sampleCount;
            block = new Ce64MscCompressedBlock(sequence, sampleCount, mode, shift, payloadBytes, decoded);
            return true;
        }

        internal void RequireDrained()
        {
            if (pending.Count != 0) throw new InvalidDataException("C64Z stream ended with an incomplete block.");
        }

        // Intended solely for a host diagnostic after TryDecode throws. It
        // never influences acceptance and reports values directly from the
        // still-pending wire bytes, including independently recalculated CRCs.
        internal Ce64MscCompressedHeaderDiagnostic GetPendingHeaderDiagnostic()
        {
            Ce64MscCompressedHeaderDiagnostic result = new Ce64MscCompressedHeaderDiagnostic();
            result.ByteOffset = ConsumedEncodedBytes;
            if (pending.Count < Ce64MscProtocol.CompressedV2HeaderBytes) return result;
            byte[] header = pending.GetRange(0, Ce64MscProtocol.CompressedV2HeaderBytes).ToArray();
            result.HeaderAvailable = true;
            result.Sequence = Ce64MscProtocol.ReadUInt32(header, 8);
            result.Mode = header[5]; result.QuantShift = header[6];
            result.PayloadBytes = Ce64MscProtocol.ReadUInt16(header, 16);
            result.ExpectedHeaderCrc = Ce64MscProtocol.ReadUInt32(header, 32);
            result.CalculatedHeaderCrc = Ce64MscProtocol.Crc32(header, 32);
            result.ExpectedPayloadCrc = Ce64MscProtocol.ReadUInt32(header, 24);
            int total = Ce64MscProtocol.CompressedV2HeaderBytes + result.PayloadBytes;
            if (pending.Count >= total)
            {
                byte[] payload = pending.GetRange(Ce64MscProtocol.CompressedV2HeaderBytes, result.PayloadBytes).ToArray();
                result.PayloadAvailable = true;
                result.CalculatedPayloadCrc = Ce64MscProtocol.Crc32(payload, payload.Length);
            }
            return result;
        }

        private void RequireMagic()
        {
            if (pending[0] != (byte)'C' || pending[1] != (byte)'6' || pending[2] != (byte)'4' || pending[3] != (byte)'Z')
                throw new InvalidDataException("C64Z magic is invalid or the FIFO byte stream is discontinuous.");
        }

        private static byte[] DecodePayload(byte[] payload, ushort samples, byte mode, byte shift)
        {
            int decodedBytes = samples * (int)Ce64MscProtocol.Wild64FrameBytes;
            byte[] decoded = new byte[decodedBytes];
            if (mode == 2)
            {
                if (payload.Length != samples * 64) throw new InvalidDataException("C64Z fixed-absolute payload length is invalid.");
                for (int frame = 0, source = 0; frame < samples; frame++)
                    for (int channel = 0; channel < 64; channel++, source++)
                        WriteInt16(decoded, frame * 128 + channel * 2, unchecked((short)((sbyte)payload[source] << 8)));
                return decoded;
            }
            if (mode == 3)
            {
                int expectedBytes = samples * 56;
                if (payload.Length != expectedBytes) throw new InvalidDataException("C64Z packed Q7 payload length is invalid.");
                BitReader q7 = new BitReader(payload, 0);
                for (int frame = 0; frame < samples; frame++)
                    for (int channel = 0; channel < 64; channel++)
                    {
                        int encoded = (int)q7.ReadBits(7);
                        if ((encoded & 0x40) != 0) encoded |= unchecked((int)0xffffff80);
                        WriteInt16(decoded, frame * 128 + channel * 2, unchecked((short)(encoded << 9)));
                    }
                if (q7.RemainingNonZeroBits()) throw new InvalidDataException("C64Z packed Q7 payload has nonzero trailing bits.");
                return decoded;
            }
            if (mode == 4)
            {
                int expectedBytes = samples * 32;
                if (payload.Length != expectedBytes) throw new InvalidDataException("C64Z packed Q4 payload length is invalid.");
                BitReader q4 = new BitReader(payload, 0);
                for (int frame = 0; frame < samples; frame++)
                    for (int channel = 0; channel < 64; channel++)
                    {
                        int encoded = (int)q4.ReadBits(4);
                        if ((encoded & 0x8) != 0) encoded |= unchecked((int)0xfffffff0);
                        WriteInt16(decoded, frame * 128 + channel * 2, unchecked((short)(encoded << 12)));
                    }
                if (q4.RemainingNonZeroBits()) throw new InvalidDataException("C64Z packed Q4 payload has nonzero trailing bits.");
                return decoded;
            }
            if (payload.Length < 128) throw new InvalidDataException("C64Z Rice payload is missing its first frame.");
            Buffer.BlockCopy(payload, 0, decoded, 0, 128);
            BitReader reader = new BitReader(payload, 128);
            for (int frame = 1; frame < samples; frame++)
                for (int channel = 0; channel < 64; channel++)
                {
                    short previous = ReadInt16(decoded, (frame - 1) * 128 + channel * 2);
                    uint quotient = reader.ReadUnaryWithEscape(out bool escaped);
                    short current;
                    if (escaped)
                        current = reader.ReadInt16AlignedBits();
                    else
                    {
                        uint remainder = reader.ReadBits(7);
                        uint zigzag = (quotient << 7) | remainder;
                        int delta = (int)(zigzag >> 1) ^ -((int)zigzag & 1);
                        current = unchecked((short)(previous + delta));
                    }
                    WriteInt16(decoded, frame * 128 + channel * 2, current);
                }
            if (reader.RemainingNonZeroBits()) throw new InvalidDataException("C64Z Rice payload has nonzero trailing bits.");
            if (mode == 1)
            {
                int mask = (1 << shift) - 1;
                for (int offset = 0; offset < decoded.Length; offset += 2)
                    if ((ReadInt16(decoded, offset) & mask) != 0)
                        throw new InvalidDataException("C64Z quantized sample did not clear its declared low bits.");
            }
            return decoded;
        }

        private static short ReadInt16(byte[] b, int o) { return unchecked((short)(b[o] | b[o + 1] << 8)); }
        private static void WriteInt16(byte[] b, int o, short value) { b[o] = (byte)value; b[o + 1] = (byte)(value >> 8); }

        private sealed class BitReader
        {
            private readonly byte[] bytes; private int bit;
            internal BitReader(byte[] value, int start) { bytes = value; bit = start * 8; }
            internal uint ReadBits(int count)
            {
                if (count < 0 || bit > bytes.Length * 8 - count) throw new InvalidDataException("C64Z Rice payload is truncated.");
                uint value = 0; for (int i = 0; i < count; i++, bit++) value |= (uint)((bytes[bit >> 3] >> (bit & 7)) & 1) << i;
                return value;
            }
            internal uint ReadUnaryWithEscape(out bool escaped)
            {
                uint zeros = 0; while (ReadBits(1) == 0) { zeros++; if (zeros > 31) throw new InvalidDataException("C64Z Rice unary code is invalid."); }
                escaped = zeros == 31; return zeros;
            }
            internal short ReadInt16AlignedBits() { return unchecked((short)ReadBits(16)); }
            internal bool RemainingNonZeroBits() { while (bit < bytes.Length * 8) if (ReadBits(1) != 0) return true; return false; }
        }
    }

    internal sealed class Ce64MscCompressedBlock
    {
        internal Ce64MscCompressedBlock(uint sequence, ushort sampleCount, byte mode, byte quantShift, ushort payloadBytes, byte[] decoded)
        { Sequence = sequence; SampleCount = sampleCount; Mode = mode; QuantShift = quantShift; PayloadBytes = payloadBytes; Decoded = decoded; }
        internal uint Sequence { get; private set; } internal ushort SampleCount { get; private set; } internal byte Mode { get; private set; }
        internal byte QuantShift { get; private set; } internal ushort PayloadBytes { get; private set; } internal byte[] Decoded { get; private set; }
        internal bool IsLossy { get { return Mode != 0; } }
    }

    internal sealed class Ce64MscCompressedHeaderDiagnostic
    {
        internal bool HeaderAvailable { get; set; }
        internal bool PayloadAvailable { get; set; }
        internal ulong ByteOffset { get; set; }
        internal uint Sequence { get; set; }
        internal byte Mode { get; set; }
        internal byte QuantShift { get; set; }
        internal ushort PayloadBytes { get; set; }
        internal uint ExpectedHeaderCrc { get; set; }
        internal uint CalculatedHeaderCrc { get; set; }
        internal uint ExpectedPayloadCrc { get; set; }
        internal uint CalculatedPayloadCrc { get; set; }
    }
}
