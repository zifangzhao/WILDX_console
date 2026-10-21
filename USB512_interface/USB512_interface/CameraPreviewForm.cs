using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        private readonly ToolTip diagnostics = new ToolTip();
        private readonly System.Windows.Forms.Timer presentTimer = new System.Windows.Forms.Timer();
        private readonly Thread decodeThread;
        private readonly Action<byte, byte> setSensorExposure;
        // The preview deliberately drops frames when it is busy.  Keep the
        // resulting working set bounded instead of allocating 160 kB raw
        // buffers at 50 Hz and 960 kB decode buffers at 25 Hz indefinitely.
        private readonly Queue<byte[]> availableRawFrames = new Queue<byte[]>();
        private readonly CE32_console.WILD_VideoDecoder.PreviewDecoder decoder = new CE32_console.WILD_VideoDecoder.PreviewDecoder();
        private volatile bool acceptingFrames = true;
        private volatile bool displayActive;
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
        private long lastExposureMeterTimestamp;
        private byte lastSentRowsInReset = Byte.MaxValue;
        private byte lastSentRampGain = Byte.MaxValue;
        private string lastExposureError;

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
            decodeThread.Priority = ThreadPriority.BelowNormal;
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            displayActive=Visible && WindowState!=FormWindowState.Minimized;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            displayActive=Visible && WindowState!=FormWindowState.Minimized;
        }

        internal static int PreviewIdleMilliseconds(long workMilliseconds)
        {
            int work=(int)Math.Max(0,Math.Min(Int32.MaxValue/3,workMilliseconds));
            return Math.Max(1,Math.Max(40-work,work*3));
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

                byte[] raw;
                lock (rawLock)
                {
                    raw = latestRaw;
                    latestRaw = null;
                }

                if (raw == null)
                    continue;

                long began=clock.ElapsedMilliseconds;
                bool render=displayActive && began>=nextDecodeAtMilliseconds;

                Bitmap decoded = null;
                try
                {
                    if(!render) {
                        // Metering is cheap and still controls recorded exposure
                        // while hidden or waiting for the next image deadline.
                        UpdateSensorExposure(MeasureCe64Exposure(raw));
                        continue;
                    }
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
                    long finished=clock.ElapsedMilliseconds;
                    if(render)nextDecodeAtMilliseconds=finished+PreviewIdleMilliseconds(finished-began);
                }
            }
        }

        private void presentTimer_Tick(object sender, EventArgs e)
        {
            if(!displayActive)return;
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
                ? (displayedFrames == 0 ? "Waiting for camera frames…" : decoder.HoldingImage ? "Image damaged — holding previous frame" : "Live preview")
                : "Preview decoder error: " + error;
            diagnostics.SetToolTip(status, string.Format("{0:N0} received, {1:N0} decoded, {2:N0} displayed; {3:N0} display frames skipped",
                submitted, decodedFrames, displayedFrames, dropped) +
                (decoder.RecoverySummary == null ? "" : "; " + decoder.RecoverySummary));

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
                : submitted == 0
                    ? "Sensor exposure: waiting"
                    : string.Format("Brightness: {0:F0}%", meter * 100.0 / 1023);
            diagnostics.SetToolTip(exposureStatus, string.Format("Meter: {0} / 1023; row reset: {1}; ramp gain: {2} {3}",
                meter, rows, ramp, automatic ? "(auto)" : "(manual)"));
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

        // Share packing, Bayer phase and display tone with saved playback/MP4.
        private Bitmap DecodeCe64Frame(byte[] raw)
        {
            UpdateSensorExposure(MeasureCe64Exposure(raw));
            return decoder.Decode(raw);
        }

        private static int MeasureCe64Exposure(byte[] raw)
        {
            return CE32_console.WILD_VideoDecoder.MeasureExposure(raw);
        }

        private void UpdateSensorExposure(int meter)
        {
            byte rowsToSend = 0;
            byte gainToSend = 0;
            bool send = false;
            lock (exposureLock)
            {
                exposureMean = meter;
                if (!autoExposure || meter < 0)
                    return;
                // Preserve the original 25 Hz control-filter cadence even
                // when cheap metering receives every incoming camera frame.
                long now = Stopwatch.GetTimestamp();
                if(now-lastExposureMeterTimestamp<Stopwatch.Frequency*40/1000)return;
                lastExposureMeterTimestamp=now;

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
                diagnostics.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
