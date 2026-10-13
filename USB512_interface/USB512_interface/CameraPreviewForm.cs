using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace USB512_interface
{
    /// <summary>
    /// A deliberately lossy live view of the camera stream.  The capture thread
    /// only swaps a completed raw frame into this object; unpacking and painting
    /// occur on a separate worker/UI timer and therefore never delay disk logging.
    /// </summary>
    public sealed class CameraPreviewForm : Form
    {
        private const int FrameWidth = 320;
        private const int FrameHeight = 320;
        private const int FrameBytes = FrameWidth * 500;
        private const int ActiveRowBytes = 492;
        private const int RowHeaderBytes = 12;
        private const int PackedPixelsPerRow = 480;
        private const int PreviewRawBufferCount = 3;

        private readonly object rawLock = new object();
        private readonly object bitmapLock = new object();
        private readonly object exposureLock = new object();
        private readonly AutoResetEvent frameReady = new AutoResetEvent(false);
        private readonly PictureBox picture = new PictureBox();
        private readonly Label status = new Label();
        private readonly CheckBox autoExposureCheck = new CheckBox();
        private readonly TrackBar exposureSlider = new TrackBar();
        private readonly Label exposureStatus = new Label();
        private readonly System.Windows.Forms.Timer presentTimer = new System.Windows.Forms.Timer();
        private readonly Thread decodeThread;
        private readonly Action<byte, byte> setSensorExposure;
        // The preview deliberately drops frames when it is busy.  Keep the
        // resulting working set bounded instead of allocating 160 kB raw
        // buffers at 50 Hz and 960 kB decode buffers at 25 Hz indefinitely.
        private readonly Queue<byte[]> availableRawFrames = new Queue<byte[]>();
        private readonly ushort[] bayerScratch = new ushort[FrameWidth * FrameHeight];
        private readonly byte[] pixelScratch = new byte[FrameWidth * FrameHeight * 4];
        private volatile bool acceptingFrames = true;
        internal bool KeepAliveWhileRecording {get;set;}
        private byte[] latestRaw;
        private Bitmap latestBitmap;
        private long submittedFrames;
        private long replacedFrames;
        private long decodedFrames;
        private long displayedFrames;
        private string lastDecodeError;
        private bool autoExposure = true;
        private int exposureMean;
        // Sensor register writes restart the NanEye frame pipeline.  Keep the
        // PC-side controller intentionally slow so it converges to ambient
        // light instead of chasing per-frame metering noise.
        private const int ExposureTarget = 0x200;
        private const int ExposureDeadBand = 32;
        private const int ExposureUpdateIntervalMs = 250;
        private const int MaximumRowsPerUpdate = 2;
        private const int GainHysteresisFrames = 8;
        private int rowsInReset;
        private int rampGain = 1;
        private int underExposureCount;
        private int overExposureCount;
        private int filteredExposureMean;
        private bool filteredExposureInitialized;
        private long lastExposureUpdateTimestamp;
        private byte lastSentRowsInReset = Byte.MaxValue;
        private byte lastSentRampGain = Byte.MaxValue;
        private string lastExposureError;
        private bool displayLevelsInitialized;
        private int displayBlack;
        private int displayWhite = 1023;

        public CameraPreviewForm(Action<byte, byte> setSensorExposure)
        {
            this.setSensorExposure = setSensorExposure;
            Text = "Camera preview";
            ClientSize = new Size(760, 800);
            MinimumSize = new Size(380, 440);

            picture.Dock = DockStyle.Fill;
            picture.BackColor = Color.Black;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            Controls.Add(picture);

            FlowLayoutPanel exposureControls = new FlowLayoutPanel();
            exposureControls.Dock = DockStyle.Bottom;
            exposureControls.Height = 46;
            exposureControls.Padding = new Padding(8, 4, 8, 2);
            exposureControls.WrapContents = false;

            autoExposureCheck.AutoSize = true;
            autoExposureCheck.Checked = true;
            autoExposureCheck.Text = "Auto sensor exposure";
            autoExposureCheck.Margin = new Padding(0, 8, 8, 0);
            autoExposureCheck.CheckedChanged += autoExposureCheck_CheckedChanged;
            exposureControls.Controls.Add(autoExposureCheck);

            exposureSlider.AutoSize = false;
            exposureSlider.Width = 190;
            exposureSlider.Height = 34;
            exposureSlider.Minimum = 0;
            exposureSlider.Maximum = 255;
            exposureSlider.Value = 255;
            exposureSlider.TickFrequency = 32;
            exposureSlider.Enabled = false;
            exposureSlider.Margin = new Padding(0, 2, 8, 0);
            exposureSlider.ValueChanged += exposureSlider_ValueChanged;
            exposureControls.Controls.Add(exposureSlider);

            exposureStatus.AutoSize = true;
            exposureStatus.Margin = new Padding(0, 9, 0, 0);
            exposureStatus.Text = "Sensor exposure: waiting";
            exposureControls.Controls.Add(exposureStatus);
            Controls.Add(exposureControls);

            status.Dock = DockStyle.Bottom;
            status.Height = 28;
            status.Padding = new Padding(8, 6, 0, 0);
            status.Text = "Waiting for a complete camera frame…";
            Controls.Add(status);
            WildXTheme.Apply(this);status.ForeColor=WildXTheme.Muted;

            presentTimer.Interval = 40; // Cap GUI refresh to 25 Hz; acquisition may remain at 50 Hz.
            presentTimer.Tick += presentTimer_Tick;
            presentTimer.Start();

            for (int index = 0; index < PreviewRawBufferCount; index++)
                availableRawFrames.Enqueue(new byte[FrameBytes]);

            decodeThread = new Thread(DecodeLoop);
            decodeThread.IsBackground = true;
            decodeThread.Name = "Camera preview decoder";
            decodeThread.Start();
        }

        // The logger calls this before assembling a preview frame.  A missing
        // buffer means the preview is behind; it must skip that visual frame
        // rather than put allocation pressure on the recorder.
        public bool TryAcquireRawFrameBuffer(out byte[] buffer)
        {
            lock (rawLock)
            {
                if (!acceptingFrames || availableRawFrames.Count == 0)
                {
                    buffer = null;
                    return false;
                }
                buffer = availableRawFrames.Dequeue();
                return true;
            }
        }

        public void ReturnRawFrameBuffer(byte[] buffer)
        {
            if (buffer == null || buffer.Length != FrameBytes)
                return;
            lock (rawLock)
            {
                availableRawFrames.Enqueue(buffer);
            }
        }

        /// <summary>Transfers ownership of one complete 160,000 byte camera frame.</summary>
        public void PublishFrame(byte[] completeRawFrame)
        {
            if (completeRawFrame == null || completeRawFrame.Length != FrameBytes)
                return;

            lock (rawLock)
            {
                if (!acceptingFrames)
                {
                    availableRawFrames.Enqueue(completeRawFrame);
                    return;
                }
                if (latestRaw != null)
                {
                    availableRawFrames.Enqueue(latestRaw);
                    replacedFrames++;
                }
                latestRaw = completeRawFrame;
                submittedFrames++;
            }
            frameReady.Set();
        }

        private void DecodeLoop()
        {
            Stopwatch clock = Stopwatch.StartNew();
            long nextDecodeAtMilliseconds = 0;
            while (acceptingFrames)
            {
                frameReady.WaitOne(100);
                if (!acceptingFrames)
                    break;

                // Decode no faster than 25 Hz.  Incoming 50 Hz camera frames are
                // coalesced in latestRaw during this pause instead of competing
                // with the USB capture/logging thread for CPU time.
                long delay = nextDecodeAtMilliseconds - clock.ElapsedMilliseconds;
                if (delay > 0)
                    Thread.Sleep((int)Math.Min(delay, 100));
                if (!acceptingFrames)
                    break;

                byte[] raw;
                lock (rawLock)
                {
                    raw = latestRaw;
                    latestRaw = null;
                }

                if (raw == null)
                    continue;

                nextDecodeAtMilliseconds = clock.ElapsedMilliseconds + 40;

                Bitmap decoded = null;
                try
                {
                    decoded = DecodeCe64Frame(raw);
                    lock (bitmapLock)
                    {
                        Bitmap previous = latestBitmap;
                        latestBitmap = decoded;
                        decoded = null;
                        if (previous != null)
                            previous.Dispose();
                    }
                    decodedFrames++;
                    lastDecodeError = null;
                }
                catch (Exception ex)
                {
                    lastDecodeError = ex.GetType().Name + ": " + ex.Message;
                    if (decoded != null)
                        decoded.Dispose();
                }
                finally
                {
                    ReturnRawFrameBuffer(raw);
                }
            }
        }

        private void presentTimer_Tick(object sender, EventArgs e)
        {
            Bitmap next = null;
            lock (bitmapLock)
            {
                if (latestBitmap != null)
                {
                    next = latestBitmap;
                    latestBitmap = null;
                }
            }

            if (next != null)
            {
                Image previous = picture.Image;
                picture.Image = next;
                if (previous != null)
                    previous.Dispose();
                displayedFrames++;
            }

            long submitted;
            long dropped;
            lock (rawLock)
            {
                submitted = submittedFrames;
                dropped = replacedFrames;
            }
            string error = lastDecodeError;
            status.Text = String.IsNullOrEmpty(error)
                ? string.Format("Preview: {0:N0} received, {1:N0} decoded, {2:N0} displayed; {3:N0} stale frames replaced",
                    submitted, decodedFrames, displayedFrames, dropped)
                : "Preview decoder error: " + error;

            int meter;
            bool automatic;
            int rows;
            int ramp;
            string exposureError;
            lock (exposureLock)
            {
                meter = exposureMean;
                automatic = autoExposure;
                rows = rowsInReset;
                ramp = rampGain;
                exposureError = lastExposureError;
            }
            exposureStatus.Text = !String.IsNullOrEmpty(exposureError)
                ? "Sensor exposure error: " + exposureError
                : meter == 0
                    ? "Sensor exposure: waiting"
                    : string.Format("Meter: {0} / 1023; row reset: {1}; ramp gain: {2} {3}",
                        meter, rows, ramp, automatic ? "(auto)" : "(manual)");
        }

        private void autoExposureCheck_CheckedChanged(object sender, EventArgs e)
        {
            bool automatic = autoExposureCheck.Checked;
            lock (exposureLock)
            {
                autoExposure = automatic;
                if (!automatic)
                {
                    // Manual mode starts from the CE64 nominal analog gain;
                    // the slider then controls only ROW_IN_RESET (brightness).
                    rampGain = 1;
                }
            }
            exposureSlider.Enabled = !automatic;
            if (!automatic)
                ApplyManualSensorExposure();
        }

        private void exposureSlider_ValueChanged(object sender, EventArgs e)
        {
            ApplyManualSensorExposure();
        }

        // Matches WILD_VideoDecodewAudio_folder.py:
        // raw[:320*492].reshape(-1,492).T, discard the first 12 bytes per row,
        // unpack packed 10-bit Bayer RG values, then rotate 180 degrees.
        private Bitmap DecodeCe64Frame(byte[] raw)
        {
            int meter = MeasureCe64Exposure(raw);
            UpdateSensorExposure(meter);

            ushort[] bayer = bayerScratch;
            for (int row = 0; row < FrameHeight; row++)
            {
                int source = row * ActiveRowBytes + RowHeaderBytes;
                int column = 0;
                for (int packed = 0; packed < PackedPixelsPerRow; packed += 3)
                {
                    byte a = raw[source + packed];
                    byte b = raw[source + packed + 1];
                    byte c = raw[source + packed + 2];
                    ushort first = (ushort)(((a & 0x7F) << 3) | ((b & 0xE0) >> 5));
                    ushort second = (ushort)(((b & 0x07) << 7) | ((c & 0xFE) >> 1));

                    StoreRotated(bayer, column++, row, first);
                    StoreRotated(bayer, column++, row, second);
                }
            }

            // The camera delivers native 10-bit values.  CE64 expands these to
            // 16-bit for demosaicing, then normalizes percentile levels for its
            // display/export path.  Use the same contrast-window idea here;
            // it affects only this lossy preview, never misc.dat or the sensor.
            int black;
            int white;
            GetPreviewLevels(bayer, out black, out white);
            byte[] pixels = pixelScratch;
            for (int y = 0; y < FrameHeight; y++)
            {
                for (int x = 0; x < FrameWidth; x++)
                {
                    int rawValue = bayer[y * FrameWidth + x];
                    int red;
                    int green;
                    int blue;
                    bool evenY = (y & 1) == 0;
                    bool evenX = (x & 1) == 0;

                    if (evenY && evenX) // RG Bayer: red sample
                    {
                        red = rawValue;
                        green = AverageCross(bayer, x, y);
                        blue = AverageDiagonal(bayer, x, y);
                    }
                    else if (!evenY && !evenX) // blue sample
                    {
                        blue = rawValue;
                        green = AverageCross(bayer, x, y);
                        red = AverageDiagonal(bayer, x, y);
                    }
                    else if (evenY) // green on a red row
                    {
                        green = rawValue;
                        red = AverageHorizontal(bayer, x, y);
                        blue = AverageVertical(bayer, x, y);
                    }
                    else // green on a blue row
                    {
                        green = rawValue;
                        red = AverageVertical(bayer, x, y);
                        blue = AverageHorizontal(bayer, x, y);
                    }

                    int offset = (y * FrameWidth + x) * 4;
                    pixels[offset] = Scale(blue, black, white);
                    pixels[offset + 1] = Scale(green, black, white);
                    pixels[offset + 2] = Scale(red, black, white);
                    pixels[offset + 3] = 255;
                }
            }

            Bitmap bitmap = new Bitmap(FrameWidth, FrameHeight, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, FrameWidth, FrameHeight), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        private void GetPreviewLevels(ushort[] bayer, out int black, out int white)
        {
            const int bins = 1024;
            int[] histogram = new int[bins];
            for (int index = 0; index < bayer.Length; index++)
                histogram[bayer[index]]++;

            int lowTarget = (bayer.Length + 99) / 100;       // 1st percentile
            int highTarget = (bayer.Length * 99 + 99) / 100; // 99th percentile
            int count = 0;
            int frameBlack = 0;
            int frameWhite = 1023;
            bool lowFound = false;
            for (int value = 0; value < bins; value++)
            {
                count += histogram[value];
                if (!lowFound && count >= lowTarget)
                {
                    frameBlack = value;
                    lowFound = true;
                }
                if (count >= highTarget)
                {
                    frameWhite = value;
                    break;
                }
            }
            if (frameWhite <= frameBlack)
            {
                frameBlack = Math.Min(1022, frameBlack);
                frameWhite = frameBlack + 1;
            }

            // Per-frame percentiles make a low-light live view useful, while a
            // short EMA avoids brightness pumping as an object crosses a pixel.
            if (!displayLevelsInitialized)
            {
                displayBlack = frameBlack;
                displayWhite = frameWhite;
                displayLevelsInitialized = true;
            }
            else
            {
                displayBlack = (displayBlack * 3 + frameBlack) / 4;
                displayWhite = (displayWhite * 3 + frameWhite) / 4;
            }
            if (displayWhite <= displayBlack)
            {
                displayBlack = Math.Min(1022, displayBlack);
                displayWhite = displayBlack + 1;
            }

            black = displayBlack;
            white = displayWhite;
        }

        // This is the CE64 nine-point metering pattern, applied to the same
        // packed 10-bit values before Bayer interpolation.
        private static int MeasureCe64Exposure(byte[] raw)
        {
            int[] rowLocations = { FrameHeight / 4, FrameHeight / 2, (FrameHeight * 3) / 4 };
            int[] columnLocations = { 80, 240, 400 };
            int total = 0;
            int samples = 0;

            for (int rowIndex = 0; rowIndex < rowLocations.Length; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < columnLocations.Length; columnIndex++)
                {
                    int location = ActiveRowBytes * rowLocations[rowIndex] +
                                   RowHeaderBytes + columnLocations[columnIndex];
                    if (location + 1 >= raw.Length)
                        continue;

                    total += (((raw[location] & 0x7F) << 8) | raw[location + 1]) >> 5;
                    samples++;
                }
            }
            return samples == 0 ? 0 : total / samples;
        }

        private void UpdateSensorExposure(int meter)
        {
            byte rowsToSend = 0;
            byte gainToSend = 0;
            bool send = false;
            lock (exposureLock)
            {
                exposureMean = meter;
                if (!autoExposure || meter <= 0)
                    return;

                // Metering uses only nine Bayer sites, so it contains normal
                // scene/shot noise.  Filter it and change the sensor no more
                // than four times per second; otherwise each queued REG0 write
                // visibly flashes as NanEye restarts its frame pipeline.
                if (!filteredExposureInitialized)
                {
                    filteredExposureMean = meter;
                    filteredExposureInitialized = true;
                }
                else
                {
                    filteredExposureMean = (filteredExposureMean * 7 + meter) / 8;
                }
                long now = Stopwatch.GetTimestamp();
                long intervalTicks = Stopwatch.Frequency * ExposureUpdateIntervalMs / 1000;
                if ((now - lastExposureUpdateTimestamp) < intervalTicks)
                    return;
                lastExposureUpdateTimestamp = now;

                int error = filteredExposureMean - ExposureTarget;
                if (error > ExposureDeadBand || error < -ExposureDeadBand)
                {
                    int rowStep = error / 64;
                    if (rowStep == 0)
                        rowStep = error > 0 ? 1 : -1;
                    if (rowStep > MaximumRowsPerUpdate) rowStep = MaximumRowsPerUpdate;
                    else if (rowStep < -MaximumRowsPerUpdate) rowStep = -MaximumRowsPerUpdate;
                    rowsInReset += rowStep;
                    if (rowsInReset < 0) rowsInReset = 0;
                    else if (rowsInReset > 255) rowsInReset = 255;
                }

                if (filteredExposureMean > (int)(0.65 * 1024))
                {
                    overExposureCount++;
                }
                else if (filteredExposureMean < (int)(0.35 * 1024))
                {
                    underExposureCount++;
                }
                else
                {
                    if (overExposureCount > 0) overExposureCount--;
                    if (underExposureCount > 0) underExposureCount--;
                }
                if (overExposureCount >= GainHysteresisFrames)
                {
                    if (rampGain > 0) rampGain--;
                    overExposureCount = 0;
                }
                if (underExposureCount >= GainHysteresisFrames)
                {
                    if (rampGain < 3) rampGain++;
                    underExposureCount = 0;
                }

                rowsToSend = (byte)rowsInReset;
                gainToSend = (byte)rampGain;
                send = (rowsToSend != lastSentRowsInReset) ||
                       (gainToSend != lastSentRampGain);
            }
            if (send)
                SendSensorExposure(rowsToSend, gainToSend);
        }

        private void ApplyManualSensorExposure()
        {
            byte rowsToSend;
            byte gainToSend;
            lock (exposureLock)
            {
                if (autoExposure)
                    return;

                // A rightward UI movement means more light.  NanEye's ROW_IN_RESET
                // has the inverse sense: more rows held in reset means less light.
                rowsInReset = 255 - exposureSlider.Value;
                rowsToSend = (byte)rowsInReset;
                gainToSend = (byte)rampGain;
            }
            SendSensorExposure(rowsToSend, gainToSend);
        }

        private void SendSensorExposure(byte rows, byte gain)
        {
            lock (exposureLock)
            {
                if ((rows == lastSentRowsInReset) && (gain == lastSentRampGain))
                    return;
            }

            try
            {
                if (setSensorExposure == null)
                    throw new InvalidOperationException("No camera exposure controller is available.");

                setSensorExposure(rows, gain);
                lock (exposureLock)
                {
                    lastSentRowsInReset = rows;
                    lastSentRampGain = gain;
                    lastExposureError = null;
                }
            }
            catch (Exception ex)
            {
                lock (exposureLock)
                {
                    lastExposureError = ex.GetType().Name + ": " + ex.Message;
                }
            }
        }

        private static void StoreRotated(ushort[] destination, int column, int row, ushort value)
        {
            destination[(FrameHeight - 1 - row) * FrameWidth + (FrameWidth - 1 - column)] = value;
        }

        private static int AverageHorizontal(ushort[] values, int x, int y)
        {
            return (Sample(values, x - 1, y) + Sample(values, x + 1, y)) / 2;
        }

        private static int AverageVertical(ushort[] values, int x, int y)
        {
            return (Sample(values, x, y - 1) + Sample(values, x, y + 1)) / 2;
        }

        private static int AverageCross(ushort[] values, int x, int y)
        {
            return (Sample(values, x - 1, y) + Sample(values, x + 1, y) +
                    Sample(values, x, y - 1) + Sample(values, x, y + 1)) / 4;
        }

        private static int AverageDiagonal(ushort[] values, int x, int y)
        {
            return (Sample(values, x - 1, y - 1) + Sample(values, x + 1, y - 1) +
                    Sample(values, x - 1, y + 1) + Sample(values, x + 1, y + 1)) / 4;
        }

        private static int Sample(ushort[] values, int x, int y)
        {
            if (x < 0) x = 0;
            else if (x >= FrameWidth) x = FrameWidth - 1;
            if (y < 0) y = 0;
            else if (y >= FrameHeight) y = FrameHeight - 1;
            return values[y * FrameWidth + x];
        }

        private static byte Scale(int value, int black, int white)
        {
            if (value <= black) return 0;
            int range = white - black;
            if (range <= 0) return 0;
            if (value >= white) return 255;
            return (byte)((value - black) * 255 / range);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if(KeepAliveWhileRecording && e.CloseReason==CloseReason.UserClosing) {
                e.Cancel=true;Hide();return;
            }
            acceptingFrames = false;
            frameReady.Set();
            presentTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                acceptingFrames = false;
                frameReady.Set();
                if (decodeThread != null && decodeThread.IsAlive)
                    decodeThread.Join(250);
                lock (bitmapLock)
                {
                    if (latestBitmap != null)
                    {
                        latestBitmap.Dispose();
                        latestBitmap = null;
                    }
                }
                frameReady.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
