using System;

namespace CE32_console
{
    // Native-rate burst decoder shared by live BLE preview and offline tests. Gather
    // raw PSSI bytes; all ADC transposition and display processing stays on PC.
    internal sealed class NpV2PreviewPlan
    {
        internal const int RowTicks = 1600, WordTicks = 12;
        internal readonly int Probe, Channel, BaseChannel, Samples, SampleHz;
        internal readonly int FirstTick, SourceSpan, PayloadBytes;
        internal int BlockSourceOffset { get { return RowTicks - WordTicks; } }

        // phase is the selected probe's validated SuperSync phase relative to
        // source tick zero. sourceTick is the first available raw-ring tick.
        internal NpV2PreviewPlan(int probe, int channel, int phase, int samples,
                                 int sampleHz, ulong sourceTick)
        {
            if (probe < 1 || probe > 2 || channel < 0 || channel >= 384 ||
                phase < 0 || phase >= RowTicks || samples < 1 || samples > 256 ||
                (sampleHz != 15000 && sampleHz != 20000 && sampleHz != 30000))
                throw new ArgumentOutOfRangeException();
            Probe = probe; Channel = channel; BaseChannel = channel % 128;
            Samples = samples; SampleHz = sampleHz;
            int frame = (BaseChannel % 32) / 2;
            int slot = (BaseChannel / 32) * 2 + (BaseChannel & 1);
            int wordPhase = ((RowTicks - phase) % RowTicks + frame * 100 + 4 + slot * 12) % RowTicks;
            FirstTick = (wordPhase - (int)(sourceTick % RowTicks) + RowTicks) % RowTicks;
            SourceSpan = FirstTick + (samples - 1) * RowTicks + WordTicks;
            PayloadBytes = samples * WordTicks;
        }

        // Models a 2-D DMA gather for tests. Firmware must establish stable
        // source ownership and a non-wrapping span BEFORE launching hardware.
        internal byte[] GatherForTest(byte[] source)
        {
            if (source == null || source.Length < SourceSpan) throw new ArgumentException("Short source window");
            byte[] result = new byte[PayloadBytes];
            for (int row = 0; row < Samples; row++)
                Buffer.BlockCopy(source, FirstTick + row * RowTicks, result, row * WordTicks, WordTicks);
            return result;
        }

        // Returns the three ADC lanes: BaseChannel, +128, +256. Values are
        // unsigned 12-bit codes, not calibrated microvolts. No aliasing-prone
        // sample decimation is performed; samples within a burst are adjacent.
        internal ushort[,] Decode(byte[] raw)
        {
            if (raw == null || raw.Length != PayloadBytes) throw new ArgumentException("Incomplete preview burst");
            var result = new ushort[Samples, 3];
            int shift = (Probe - 1) * 3;
            for (int row = 0; row < Samples; row++)
                for (int bit = 0; bit < WordTicks; bit++)
                {
                    int lanes = raw[row * WordTicks + bit] >> shift;
                    for (int lane = 0; lane < 3; lane++)
                        result[row, lane] = (ushort)((result[row, lane] << 1) | ((lanes >> lane) & 1));
                }
            return result;
        }
    }
}
