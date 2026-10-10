using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace USB512_interface
{
    public partial class ScopeForm : Form
    {
        private const int DefaultChannelCount = 512;
        private const int DefaultSampleRateHz = 20000;
        private const int MaxSeconds = 5;   // ring buffer length
        private const int DefaultChannelsPerBank = 64;
        private readonly int channelCount;
        private readonly int[] channelIds;
        private readonly int sampleRateHz;
        private readonly int channelsPerBank;
        private readonly int bankCount;

        private readonly short[,] buffer;   // [time, channel]
        private readonly int bufferSamples;
        private int writePos = 0;
        private bool bufferFilled = false;

        private readonly object bufferLock = new object();
        private readonly ConcurrentQueue<short[]> inputQueue = new ConcurrentQueue<short[]>();
        // Scope data is display-only.  Keep enough queued decoded blocks to
        // preserve waveform continuity through a brief UI/render delay; the
        // renderer now snapshots quickly and normally remains far below this.
        private const int MaximumQueuedBlocks = 64;
        private int queuedBlockCount;
        // RenderThread is the sole owner. It snapshots only samples which
        // arrived since the previous frame while holding bufferLock briefly,
        // then draws without blocking the feed thread that keeps the
        // time-series buffer continuous.
        private short[] renderSnapshot = Array.Empty<short>();
        private long totalSamplesWritten;
        private long nextSweepSequence = -1;
        private int sweepSamplesDrawn;
        private int sweepWindowSamples;
        private int sweepColumns;
        private int activeSweepColumn = -1;
        private int appliedSettingsVersion = -1;
        private int settingsVersion;
        private int traceColorMode;
        private long[] sweepColumnSum = Array.Empty<long>();
        private short[] previousColumnValue = Array.Empty<short>();
        private int samplesInActiveColumn;
        private Pen[] tracePens = Array.Empty<Pen>();
        private int tracePensPalette = -1;

        private Thread feedThread;
        private Thread renderThread;
        private bool feedRunning = false;
        private bool renderRunning = false;

        private decimal windowSeconds = (decimal)1.0;  // visible duration
        private double gain = 1.0;      // amplification
        private int currentBank;

        private PictureBox pic;
        private NumericUpDown nudDuration;
        private NumericUpDown nudGain;
        private NumericUpDown nudBank;
        private System.Windows.Forms.ComboBox traceColorBox;

        private Bitmap backBuffer;

        public ScopeForm()
            : this(DefaultChannelCount, DefaultSampleRateHz, "USB512 Scope")
        {
        }

        // The MSC target streams the same packed int16 frame shape as the
        // recorder writes, but with one 64-channel bank at 5 kHz.  Keeping the
        // dimensions configurable lets preview remain display-only instead of
        // routing that stream through the legacy 512-channel decoder.
        public ScopeForm(int channels, int rateHz, string title)
            : this(channels, rateHz, title, null)
        {
        }

        public ScopeForm(int channels, int rateHz, string title, int[] ids)
        {
            if (channels < 1)
                throw new ArgumentOutOfRangeException("channels");
            if (rateHz < 1)
                throw new ArgumentOutOfRangeException("rateHz");
            channelCount = channels;
            if(ids!=null && ids.Length!=channels) throw new ArgumentException("Channel label count mismatch.");
            channelIds=new int[channels];
            for(int i=0;i<channels;i++) channelIds[i]=ids==null?i:ids[i];
            sampleRateHz = rateHz;
            channelsPerBank = DefaultChannelsPerBank;
            bankCount = (channels + channelsPerBank - 1) / channelsPerBank;
            Text = title;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(900, 600);

            bufferSamples = sampleRateHz * MaxSeconds;
            buffer = new short[bufferSamples, channelCount];

            InitializeUi();
            this.KeyPreview = true;   //for key events
            DoubleBuffered = true;

            StartFeedThread();
            StartRenderThread();
        }

        public bool MatchesStream(int channels, int rateHz)
        {
            return channelCount == channels && sampleRateHz == rateHz;
        }

        public bool MatchesChannelIds(int[] ids)
        {
            if(ids==null || ids.Length!=channelCount) return false;
            for(int i=0;i<ids.Length;i++) if(ids[i]!=channelIds[i]) return false;
            return true;
        }

        // ---------------- UI ----------------
        private void InitializeUi()
        {
            pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            pic.Paint += Pic_Paint;

            var panel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };

            var lblDur = new Label
            {
                Text = "Duration (s):",
                AutoSize = true,
                Left = 8,
                Top = 12
            };

            nudDuration = new NumericUpDown
            {
                Minimum = (decimal)0.1,
                Maximum = MaxSeconds,
                Increment = (decimal)0.1,
                DecimalPlaces = 1,
                Value = windowSeconds,
                Left = lblDur.Right + 4,
                Top = 8,
                Width = 50
            };
            nudDuration.ValueChanged += (s, e) =>
            {
                windowSeconds = (decimal)nudDuration.Value;
                DisplaySettingsChanged();
            };

            var lblGain = new Label
            {
                Text = "Gain:",
                AutoSize = true,
                Left = nudDuration.Right + 10,
                Top = 12
            };

            nudGain = new NumericUpDown
            {
                Minimum = 0.1M,
                Maximum = 100M,
                DecimalPlaces = 1,
                Increment = 0.1M,
                Value = 1.0M,
                Left = lblGain.Right + 4,
                Top = 8,
                Width = 60
            };
            nudGain.ValueChanged += (s, e) =>
            {
                gain = (double)nudGain.Value;
                DisplaySettingsChanged();
            };

            var lblBank = new Label
            {
                Text = "Bank (0-" + (bankCount - 1) + "): ",
                AutoSize = true,
                Left = nudGain.Right + 10,
                Top = 12
            };

            nudBank = new NumericUpDown
            {
                Minimum = 0,
                Maximum = bankCount - 1,
                Value = 0,
                Left = lblBank.Right + 4,
                Top = 8,
                Width = 50
            };
            nudBank.ValueChanged += (s, e) =>
            {
                currentBank = (int)nudBank.Value;
                DisplaySettingsChanged();
            };

            var lblTraceColor = new Label
            {
                Text = "Trace:",
                AutoSize = true,
                Left = nudBank.Right + 10,
                Top = 12
            };

            traceColorBox = new System.Windows.Forms.ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = lblTraceColor.Right + 4,
                Top = 8,
                Width = 145
            };
            traceColorBox.Items.AddRange(new object[]
            {
                "Blue",
                "Cyan",
                "Amber",
                "Rainbow gradient",
                "Thermal gradient"
            });
            traceColorBox.SelectedIndexChanged += (s, e) =>
            {
                traceColorMode = traceColorBox.SelectedIndex;
                DisplaySettingsChanged();
            };
            traceColorBox.SelectedIndex = 0;

            panel.Controls.Add(lblDur);
            panel.Controls.Add(nudDuration);
            panel.Controls.Add(lblGain);
            panel.Controls.Add(nudGain);
            panel.Controls.Add(lblBank);
            panel.Controls.Add(nudBank);
            panel.Controls.Add(lblTraceColor);
            panel.Controls.Add(traceColorBox);

            Controls.Add(pic);
            Controls.Add(panel);

            var lblShortcuts = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Left = lblDur.Left,
                Top = nudBank.Bottom + 2,      // directly below the controls
                Width = panel.Width - 10,
                Height = 14,
                Font = new Font("Arial", 7f),
                ForeColor = Color.Gray,
                Text =
                "Shortcuts: ↑↓ Gain   |   ←→ Bank   |   +/- Duration   |   R Reset Gain   |   D Reset Duration   |   B Cycle Banks "
            };
            // Allow panel to resize and keep shortcuts aligned
            panel.Resize += (s, e) =>
            {
                lblShortcuts.Width = panel.Width - 10;
            };

            panel.Controls.Add(lblShortcuts);
            panel.Height = 60;     // increase panel height to fit the second row

            AllowOnlyDigits(nudDuration);
            AllowOnlyDigits(nudGain);
            AllowOnlyDigits(nudBank);

        }

        private void AllowOnlyDigits(NumericUpDown n)
        {
            // Prevent arrow-key and +/- editing
            n.InterceptArrowKeys = false;

            n.KeyPress += (s, e) =>
            {
                // Allow digits only
                if (char.IsDigit(e.KeyChar))
                    return;

                // Allow control keys (backspace)
                if (e.KeyChar == (char)Keys.Back)
                    return;

                // Everything else gets blocked
                e.Handled = true;
            };

            n.KeyDown += (s, e) =>
            {
                // Block keys that cause unintended edits
                if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Subtract ||
                    e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    e.SuppressKeyPress = true;
                }
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.KeyCode)
            {
                case Keys.Up:
                    gain *= 1.1;                 // increase gain
                    nudGain.Value = (decimal)gain;
                    break;

                case Keys.Down:
                    gain /= 1.1;                 // decrease gain
                    nudGain.Value = (decimal)gain;
                    break;

                case Keys.Left:
                    if (currentBank > 0)
                        currentBank--;
                    nudBank.Value = currentBank;
                    break;

                case Keys.Right:
                    if (currentBank < bankCount - 1)
                        currentBank++;
                    nudBank.Value = currentBank;
                    break;

                case Keys.Add:    // + key
                case Keys.Oemplus:
                    windowSeconds = Math.Min(windowSeconds + 0.1m, MaxSeconds);
                    nudDuration.Value = windowSeconds;
                    break;

                case Keys.Subtract: // - key
                case Keys.OemMinus:
                    windowSeconds = Math.Max(windowSeconds - 0.1m, 0.1m);
                    nudDuration.Value = windowSeconds;
                    break;

                case Keys.R:
                    gain = 1.0;
                    nudGain.Value = 1.0m;
                    break;

                case Keys.D:
                    windowSeconds = 1.0m;
                    nudDuration.Value = 1.0m;
                    break;

                case Keys.B:
                    // jump to bank selected in textbox or cycle
                    currentBank = (currentBank + 1) % bankCount;
                    nudBank.Value = currentBank;
                    break;
            }
        }


        // ---------------- Data ingest ----------------
        public void PushData(short[] data)
        {
            if (data == null || data.Length < channelCount)
                return;

            int count = Interlocked.Increment(ref queuedBlockCount);
            inputQueue.Enqueue(data);

            // Drop the oldest display-only block when rendering falls behind.
            // Recording and decoded output are unaffected.
            while (count > MaximumQueuedBlocks && inputQueue.TryDequeue(out short[] discarded))
                count = Interlocked.Decrement(ref queuedBlockCount);
        }

        private void DisplaySettingsChanged()
        {
            // A changed timebase, gain, or bank must start a clean sweep so a
            // single scan never combines incompatible display settings.
            Interlocked.Increment(ref settingsVersion);
        }

        private void StartFeedThread()
        {
            feedRunning = true;
            feedThread = new Thread(FeedThreadLoop)
            {
                IsBackground = true
            };
            feedThread.Start();
        }

        private void FeedThreadLoop()
        {
            while (feedRunning)
            {
                if (inputQueue.TryDequeue(out short[] block))
                {
                    Interlocked.Decrement(ref queuedBlockCount);
                    int blocks = block.Length / channelCount;

                    lock (bufferLock)
                    {
                        for (int t = 0; t < blocks; t++)
                        {
                            int srcBase = t * channelCount;

                            for (int ch = 0; ch < channelCount; ch++)
                                buffer[writePos, ch] = block[srcBase + ch];

                            writePos++;
                            totalSamplesWritten++;
                            if (writePos >= bufferSamples)
                            {
                                writePos = 0;
                                bufferFilled = true;
                            }
                        }
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        // ---------------- Render thread ----------------
        private void StartRenderThread()
        {
            renderRunning = true;
            renderThread = new Thread(RenderThreadLoop)
            {
                IsBackground = true
            };
            renderThread.Start();
        }
        private readonly object backBufferLock = new object();

        private void RenderThreadLoop()
        {
            const int targetFPS = 60;
            int delay = 1000 / targetFPS;

            while (renderRunning)
            {
                RenderToBackbuffer();

                if (pic.IsHandleCreated)
                {
                    try
                    {
                        pic.BeginInvoke(new Action(() => pic.Invalidate()));
                    }
                    catch { }
                }

                Thread.Sleep(delay);
            }
        }

        private void RenderToBackbuffer()
        {
            if (pic.Width < 20 || pic.Height < 20) return;

            lock (backBufferLock)
            {
                bool resized = false;
                if (backBuffer == null ||
                backBuffer.Width != pic.Width ||
                backBuffer.Height != pic.Height)
                {
                    backBuffer?.Dispose();
                    backBuffer = new Bitmap(pic.Width, pic.Height);
                    resized = true;
                }

                using (Graphics g = Graphics.FromImage(backBuffer))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;

                    int width = backBuffer.Width;
                    int height = backBuffer.Height;
                    int bank = currentBank;
                    int firstCh = bank * channelsPerBank;
                    int lastCh = Math.Min(firstCh + channelsPerBank, channelCount) - 1;
                    int nCh = lastCh - firstCh + 1;
                    int windowSamples = Math.Max(2, (int)(windowSeconds * sampleRateHz));
                    int columns = Math.Max(1, width - 32);
                    int configuration = Volatile.Read(ref settingsVersion);
                    int tracePalette = Volatile.Read(ref traceColorMode);
                    bool resetSweep = resized ||
                        configuration != appliedSettingsVersion ||
                        windowSamples != sweepWindowSamples ||
                        columns != sweepColumns;
                    int samplesToRender = 0;

                    lock (bufferLock)
                    {
                        long available = bufferFilled ? bufferSamples : totalSamplesWritten;
                        if (resetSweep || nextSweepSequence < 0 ||
                            totalSamplesWritten - nextSweepSequence > available)
                        {
                            ResetSweep(g, width, height, bank, nCh, windowSamples, columns);
                            appliedSettingsVersion = configuration;
                            nextSweepSequence = totalSamplesWritten;
                            return;
                        }

                        long unseenSamples = totalSamplesWritten - nextSweepSequence;
                        if (unseenSamples <= 0)
                            return;

                        // Keep a completed sweep visible until the next data
                        // arrives. The following frame starts the new scan at
                        // the left edge, rather than sliding the old trace.
                        if (sweepSamplesDrawn >= windowSamples)
                        {
                            ResetSweep(g, width, height, bank, nCh, windowSamples, columns);
                            appliedSettingsVersion = configuration;
                        }

                        samplesToRender = (int)Math.Min(
                            unseenSamples,
                            windowSamples - sweepSamplesDrawn);
                        int requiredSamples = samplesToRender * nCh;
                        if (renderSnapshot.Length < requiredSamples)
                            renderSnapshot = new short[requiredSamples];

                        // Copy only the selected bank while the live ring is
                        // protected. Rendering the scan happens after this
                        // lock is released, so acquisition remains responsive.
                        for (int sample = 0; sample < samplesToRender; sample++)
                        {
                            long sequence = nextSweepSequence + sample;
                            int source = writePos - (int)(totalSamplesWritten - sequence);
                            if (source < 0)
                                source += bufferSamples;
                            int destination = sample * nCh;
                            for (int channel = 0; channel < nCh; channel++)
                                renderSnapshot[destination + channel] = buffer[source, firstCh + channel];
                        }
                        nextSweepSequence += samplesToRender;
                    }

                    for (int sample = 0; sample < samplesToRender; sample++)
                        AppendSweepSample(g, width, height, nCh, (float)gain,
                            tracePalette, renderSnapshot, sample * nCh);

                    DrawActiveSweepColumn(g, width, height, nCh, (float)gain, tracePalette);
                }
            }
        }

        // ---------------- Drawing ----------------
        private void ResetSweep(Graphics g, int width, int height, int bank,
                                int nCh, int windowSamples, int columns)
        {
            int firstCh = bank * channelsPerBank;
            g.Clear(Color.White);
            float channelHeight = (float)height / nCh;
            const int traceLeft = 32;

            using (var font = new Font("Arial", 7f))
            using (var brush = new SolidBrush(Color.Black))
            {
                for (int idxCh = 0; idxCh < nCh; idxCh++)
                {
                    int ch = firstCh + idxCh;
                    float baseline = (idxCh + 0.5f) * channelHeight;
                    g.DrawString(channelIds[ch].ToString(), font, brush, 2, baseline - 6);
                    g.DrawLine(Pens.Gainsboro, traceLeft, baseline, width - 1, baseline);
                }
            }

            for (int division = 0; division <= 10; division++)
            {
                int x = traceLeft + ((columns - 1) * division / 10);
                g.DrawLine(Pens.Gainsboro, x, 0, x, height - 1);
            }

            sweepWindowSamples = windowSamples;
            sweepColumns = columns;
            sweepSamplesDrawn = 0;
            activeSweepColumn = -1;
            samplesInActiveColumn = 0;
            if (sweepColumnSum.Length != nCh)
            {
                sweepColumnSum = new long[nCh];
                previousColumnValue = new short[nCh];
            }
        }

        private void AppendSweepSample(Graphics g, int width, int height, int nCh,
                                       float renderGain, int tracePalette,
                                       short[] samples, int offset)
        {
            int column = (int)((long)sweepSamplesDrawn * sweepColumns / sweepWindowSamples);
            if (column >= sweepColumns)
                column = sweepColumns - 1;

            if (column != activeSweepColumn)
            {
                DrawActiveSweepColumn(g, width, height, nCh, renderGain, tracePalette);
                if (activeSweepColumn >= 0)
                {
                    for (int channel = 0; channel < nCh; channel++)
                        previousColumnValue[channel] = GetActiveColumnValue(channel);
                }

                activeSweepColumn = column;
                for (int channel = 0; channel < nCh; channel++)
                    sweepColumnSum[channel] = samples[offset + channel];
                samplesInActiveColumn = 1;
            }
            else
            {
                for (int channel = 0; channel < nCh; channel++)
                    sweepColumnSum[channel] += samples[offset + channel];
                samplesInActiveColumn++;
            }

            sweepSamplesDrawn++;
        }

        private short GetActiveColumnValue(int channel)
        {
            return (short)(sweepColumnSum[channel] / Math.Max(1, samplesInActiveColumn));
        }

        private void DrawActiveSweepColumn(Graphics g, int width, int height,
                                           int nCh, float renderGain, int tracePalette)
        {
            if (activeSweepColumn < 0)
                return;

            const int traceLeft = 32;
            int x = traceLeft + activeSweepColumn;
            float channelHeight = (float)height / nCh;
            const float maxAmplitude = 2000f;
            float scale = channelHeight / (2f * maxAmplitude);

            // The active pixel is redrawn as more source samples arrive.  One
            // averaged sample per screen column keeps the trace connected
            // without spending CPU on all raw samples in a dense timebase.
            g.FillRectangle(Brushes.White, x, 0, 1, height);
            if (IsSweepDivision(activeSweepColumn))
                g.DrawLine(Pens.Gainsboro, x, 0, x, height - 1);

            for (int channel = 0; channel < nCh; channel++)
            {
                float baseline = (channel + 0.5f) * channelHeight;
                g.DrawLine(Pens.Gainsboro, x, baseline, x, baseline);
            }

            if (activeSweepColumn > 0)
            {
                Pen[] pens = GetTracePens(nCh, tracePalette);
                for (int channel = 0; channel < nCh; channel++)
                {
                    float baseline = (channel + 0.5f) * channelHeight;
                    float previousY = baseline - (previousColumnValue[channel] * renderGain) * scale;
                    float currentY = baseline - (GetActiveColumnValue(channel) * renderGain) * scale;
                    g.DrawLine(pens[channel], x - 1, previousY, x, currentY);
                }
            }

            int cursorX = Math.Min(traceLeft + sweepColumns - 1, x + 1);
            if (cursorX > x)
                g.DrawLine(Pens.LimeGreen, cursorX, 0, cursorX, height - 1);
        }

        private Pen[] GetTracePens(int visibleChannels, int palette)
        {
            if (tracePens.Length == visibleChannels && tracePensPalette == palette)
                return tracePens;

            foreach (Pen pen in tracePens)
                pen.Dispose();

            tracePens = new Pen[visibleChannels];
            for (int channel = 0; channel < visibleChannels; channel++)
                tracePens[channel] = new Pen(GetTraceColor(channel, visibleChannels, palette), 1f);
            tracePensPalette = palette;
            return tracePens;
        }

        private static Color GetTraceColor(int channel, int visibleChannels, int palette)
        {
            switch (palette)
            {
                case 1:
                    return Color.DeepSkyBlue;
                case 2:
                    return Color.DarkOrange;
                case 3:
                    return ColorFromHsv(240.0 * (1.0 - ChannelFraction(channel, visibleChannels)), 0.80, 0.95);
                case 4:
                    return ThermalColor(ChannelFraction(channel, visibleChannels));
                default:
                    return Color.RoyalBlue;
            }
        }

        private static double ChannelFraction(int channel, int visibleChannels)
        {
            return visibleChannels <= 1 ? 0.0 : (double)channel / (visibleChannels - 1);
        }

        private static Color ThermalColor(double fraction)
        {
            if (fraction < 0.5)
                return Blend(Color.MediumBlue, Color.LimeGreen, fraction * 2.0);
            return Blend(Color.LimeGreen, Color.OrangeRed, (fraction - 0.5) * 2.0);
        }

        private static Color Blend(Color from, Color to, double fraction)
        {
            return Color.FromArgb(
                (int)(from.R + ((to.R - from.R) * fraction)),
                (int)(from.G + ((to.G - from.G) * fraction)),
                (int)(from.B + ((to.B - from.B) * fraction)));
        }

        private static Color ColorFromHsv(double hue, double saturation, double value)
        {
            double chroma = value * saturation;
            double second = chroma * (1.0 - Math.Abs(((hue / 60.0) % 2.0) - 1.0));
            double match = value - chroma;
            double red = 0.0, green = 0.0, blue = 0.0;

            if (hue < 60.0) { red = chroma; green = second; }
            else if (hue < 120.0) { red = second; green = chroma; }
            else if (hue < 180.0) { green = chroma; blue = second; }
            else if (hue < 240.0) { green = second; blue = chroma; }
            else if (hue < 300.0) { red = second; blue = chroma; }
            else { red = chroma; blue = second; }

            return Color.FromArgb(
                (int)((red + match) * 255.0),
                (int)((green + match) * 255.0),
                (int)((blue + match) * 255.0));
        }

        private bool IsSweepDivision(int column)
        {
            for (int division = 0; division <= 10; division++)
            {
                if (column == ((sweepColumns - 1) * division / 10))
                    return true;
            }
            return false;
        }

        private void Pic_Paint(object sender, PaintEventArgs e)
        {
            lock (backBufferLock)
            {
                if (backBuffer != null)
                {
                    e.Graphics.DrawImage(backBuffer, 0, 0);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            feedRunning = false;
            renderRunning = false;
            foreach (Pen pen in tracePens)
                pen.Dispose();
            base.OnFormClosing(e);
        }

        private void ScopeForm_Load(object sender, EventArgs e)
        {

        }
    }
}
