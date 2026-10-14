using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Threading;
using System.Windows.Forms;

namespace USB512_interface
{
    public partial class Form1 : Form
    {
        private const ushort PacketMagic = 0xA512;
        private const int PacketHeaderBytes = 6;
        private const int FullTransferBytes = 512;
        private const int FullPacketPayloadBytes = FullTransferBytes - PacketHeaderBytes;
        private const int CameraFrameBytes = 320 * 500;
        private const int ReceiveBufferBytes = 32 * 1024;
        // 64 MiB absorbs about 2.3 seconds at 29 MB/s. This host-only backlog
        // separates the reader from decode/disk stalls and is allocated before
        // acquisition; it does not increase the device's unsettled replay window.
        private const int ReceiveQueueDepth = 2048;
        // Keep the host's unsettled read window well below the firmware's
        // retained replay window.  Two 32 KiB reads retain the required mixed
        // stream throughput while avoiding the WinUSB instability observed
        // with deeper queues and leaving substantial replay margin.
        // Keep four overlapped bulk-IN requests queued. This gives the HS hub
        // several milliseconds of host credit through ordinary scheduler jitter
        // and prevents endpoint starvation that otherwise surfaces as Win32 31.
        private const int UsbReadDepth = 4;
        // amplifier.dat is a continuous, packed sequence of 512-channel int16
        // samples.  It has no packet-aligned sample marker, but its byte count
        // does provide an exact rate when expressed in complete 512-channel rows.
        private const int AmplifierChannels = 512;
        private const int AmplifierSampleBytes = sizeof(short);
        private const int AmplifierFrameBytes = AmplifierChannels * AmplifierSampleBytes;
        // One OSPI acquisition command contributes a 32-byte, 16-chip block.
        // RHD converters return data two commands after receiving a CONVERT
        // command, so the first two blocks after each firmware start are not
        // channel 0/1 data and must not enter a multiplexed output frame.
        private const int IntanAcquisitionBlockBytes = 32;
        private const int IntanLanes = 16;
        private const int IntanBlocksPerFrame = AmplifierChannels / IntanLanes;
        private const int IntanPipelinePrimingBytes = 2 * IntanAcquisitionBlockBytes;
        private const int AmplifierConfiguredRateHz = 20000;
        private const int AnalogInSampleBytes = 32;
        private const int AnalogInRecordsPerPacket = FullPacketPayloadBytes / AnalogInSampleBytes;
        private const int AnalogInPcTimeOffsetBytes = 28;
        private const int TimestampBytesPerRecord = sizeof(uint);
        private const uint Wild64RateHz = Ce64MscProtocol.Wild64RateHz;
        private const uint Wild64Channels = Ce64MscProtocol.Wild64Channels;
        private const int UiUpdateIntervalMilliseconds = 500;
        private const int RateAveragingWindowMilliseconds = 5000;
        private const int AdcSampleBytes = sizeof(short);
        // Zero preserves the validated firmware clock instead of applying a
        // divider derived from a different GPIO-board clock tree.
        private const ushort XeNpMcoDivider = 0;
        private volatile int xeNpPreviewRateHz = 30000;

        private sealed class DeviceItem
        {
            public string Path { get; set; }
            public string DisplayName { get; set; }
            public DeviceKind Kind { get; set; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private enum DeviceKind
        {
            USB512,
            XeNp,
            NpV2,
            NpUnavailable,
            Ce64Msc
        }

        private struct ReceiveBlock
        {
            internal ReceiveBlock(byte[] buffer, int count) : this(buffer,count,DateTime.MinValue) {}
            internal ReceiveBlock(byte[] buffer, int count, DateTime receiveUtc)
            {
                Buffer = buffer;
                Count = count;
                ReceiveUtc = receiveUtc;
            }

            internal static ReceiveBlock CreateStreamRestartMarker()
            {
                return new ReceiveBlock(null, -1);
            }

            internal byte[] Buffer { get; private set; }
            internal int Count { get; private set; }
            internal DateTime ReceiveUtc { get; private set; }
            internal bool IsStreamRestartMarker { get { return Count < 0; } }
        }

        private sealed class RateSnapshot
        {
            internal DateTime Timestamp;
            internal long TotalPayloadBytes;
            internal long AmplifierPayloadBytes;
            internal long AnalogInPayloadBytes;
            internal long CameraPayloadBytes;
            internal long CameraFrames;
            internal long AdcPayloadBytes;
            internal long NeuropixelsPayloadBytes;
            internal long NeuropixelsApRows;
            internal long NeuropixelsLfpRows;
        }

        private readonly object deviceLock = new object();
        private readonly object previewLock = new object();
        private readonly object cameraAssemblyLock = new object();
        private readonly object clockSyncLock = new object();
        private readonly object imuSampleLock = new object();
        private readonly Dictionary<byte, ushort> previousSequences = new Dictionary<byte, ushort>();
        private readonly Queue<RateSnapshot> rateSnapshots = new Queue<RateSnapshot>();
        private int deviceRefreshActive;
        private string devicePath;
        private DeviceKind selectedDeviceKind;
        private string npRequestedConfiguration,npConfigurationDevicePath;
        private string saveFolder;
        private string runFolder;
        private bool npPreviewShowRequested;
        private double npPreviewPreparationMs, npPreviewOpenMaxMs;
        private int npPreviewOpenCount, npPreviewOpenGen2;
        private long npHostQueueCapacityBytes;
        private int npHostQueueCapacityBlocks, npHostQueueQueuedBlocks;
        private double npHostQueueCapacitySeconds;
        private Thread captureThread;
        private Thread loggerThread;
        private volatile bool running;
        // Camera is part of the normal recording stream.  The preview window only
        // controls display; it never changes which sources are recorded.
        private bool captureCameraEnabled;
        private ushort requestedStreams = (ushort)(USB512WinUsbDevice.StreamMaskEphys |
                                                   USB512WinUsbDevice.StreamMaskImu |
                                                   USB512WinUsbDevice.StreamMaskCamera |
                                                   USB512WinUsbDevice.StreamMaskMicrophone |
                                                   USB512WinUsbDevice.StreamMaskNeuropixels);
        private ushort activeStreams;
        private byte requestedCameraProfile = USB512WinUsbDevice.CameraProfileNormal50Hz;
        private ushort requestedCameraRateHz = 50;
        private USB512WinUsbDevice activeDevice;
        private XeNpWinUsbDevice activeXeNpDevice;
        private Ce64MscRawBlockDevice activeCe64Device;
        private CancellationTokenSource ce64Cancellation;
        private volatile bool ce64StopRequested;
        private Button btnImpedance;
        private Button btnSdInfo;
        private Button btnNpLogger;
        private volatile bool ztestRunning;
        private CancellationTokenSource ztestCancellation;
        private volatile bool ztestContinuous;
        private ImpedanceReportForm ztestReport;
        // Until C64C is read this is only the legacy display fallback. A CE64
        // session always replaces it with the peer-negotiated fixed rate before
        // its first ephys frame reaches the preview.
        private volatile int ce64PreviewRateHz = (int)Wild64RateHz;
        private volatile int ce64PreviewChannels = (int)Wild64Channels;
        private volatile int ce64FrameBytes = (int)Ce64MscProtocol.Wild64FrameBytes;
        private volatile int ce64FramesPerSector = (int)Ce64MscProtocol.Wild64FramesPerSector;
        private CameraPreviewForm cameraPreview;
        private ScopeForm scopeForm;
        private NeuropixelsPreviewForm neuropixelsPreview;
        // Match the proven mass-storage console's port health indicator: it
        // checks the latest decoded 512-channel multiplexed frame, not raw USB
        // packet bytes.
        private readonly object lastDecodedLock = new object();
        private short[] lastDecodedBlock;
        private Thread goodChannelThread;
        private volatile bool goodChannelRunning;

        private FileStream amplifierFile;
        private FileStream analogInFile;
        private FileStream timestampFile;
        private FileStream cameraFile;
        private FileStream adcFile;
        private FileStream metadataFile;
        private FileStream neuropixelsFile;
        private FileStream neuropixelsApFile;
        private FileStream neuropixelsLfpFile;
        private StreamWriter ce64CompressionCsv;

        private byte[] cameraAssembly;
        private int cameraAssemblyLength;
        private bool cameraAssemblyOpen;
        private CameraPreviewForm cameraAssemblyPreview;
        /* WinUSB reads are byte streams; short camera/IMU packets may be
         * coalesced with the following packet.  Retain at most one compact
         * packet while its expected payload is assembled. */
        private readonly byte[] packetAssembly = new byte[FullTransferBytes];
        private int packetAssemblyLength;
        // Validate stream packet sequences on the capture thread, before a
        // completed USB block reaches the asynchronous logger.  That gives a
        // detected gap an immediate replay opportunity while it is still in
        // the firmware's retained USB window.
        private readonly byte[] integrityAssembly = new byte[FullTransferBytes];
        private int integrityAssemblyLength;
        private readonly Dictionary<byte, ushort> integrityPreviousSequences =
            new Dictionary<byte, ushort>();
        private readonly HashSet<byte> integrityReplayPendingStreams =
            new HashSet<byte>();
        private bool integrityReplayInFlight;
        // Ephys packet payloads are an uninterrupted OSPI bit stream.  They are
        // 506 bytes long, so packet boundaries are not sample boundaries.  Hold
        // whole native 32-byte raw blocks before applying the legacy unpack/remap.
        private readonly byte[] amplifierAssembly = new byte[ReceiveBufferBytes];
        private int amplifierAssemblyLength;
        private int amplifierPrimingBytesRemaining;
        // A confirmed missing USB packet is represented by zero-valued raw
        // bytes.  This preserves the multiplexed int16 timeline exactly: later
        // packets retain their physical channel position instead of being
        // compressed forward to a new channel-0 boundary.
        private static readonly byte[] amplifierGapPayload = new byte[FullPacketPayloadBytes];
        // Retain one raw block only to keep complete 512-channel rows together
        // across WinUSB packet boundaries.  The actual bit shift deliberately
        // remains the legacy console's zero-filled multiplexed-int16 decode.
        private ushort[] amplifierPendingLanes = new ushort[IntanLanes];
        private ushort[] amplifierScratchLanes = new ushort[IntanLanes];
        private bool amplifierHasPendingBlock;
        private readonly short[] amplifierFrame = new short[AmplifierChannels];
        private int amplifierFrameBlockCount;
        // This is the physical port ordering used by the pre-WinUSB console.
        private static readonly int[] LegacyIntanPortMap =
            { 0, 2, 4, 6, 8, 10, 12, 14, 1, 3, 5, 7, 9, 11, 13, 15 };
        private int streamCameraFrameLength;
        private long totalPayloadBytes;
        private long sequenceGaps;
        private long transportRecoveries;
        private long amplifierPackets;
        private long analogInPackets;
        private long cameraPackets;
        private long cameraFrames;
        private long adcPackets;
        private long metadataPackets;
        private long neuropixelsPackets;
        private long neuropixelsApRows;
        private long neuropixelsLfpRows;
        private long npImuSamples;
        private long amplifierPayloadBytes;
        private long analogInPayloadBytes;
        private long cameraPayloadBytes;
        private long adcPayloadBytes;
        private long neuropixelsPayloadBytes;
        private DateTime captureStartedAtUtc = DateTime.MinValue;
        private NpRecordingSettings npRecordingSettings;
        private DateTime lastUiUpdate = DateTime.MinValue;
        private BlockingCollection<byte[]> receiveBufferPool;
        private BlockingCollection<ReceiveBlock> completedReceiveBlocks;
        private int receiveQueuePeak;
        private string loggerFailure;
        // LoggerLoop is the sole writer, so this fixed packet-sized scratch
        // buffer needs no locking or per-record allocation.
        private byte[] timestampWriteBuffer =
            new byte[AnalogInRecordsPerPacket * TimestampBytesPerRecord];
        private long clockSyncCount;
        private double clockSyncLastOffsetMilliseconds;
        private double clockSyncMinimumOffsetMilliseconds;
        private double clockSyncMaximumOffsetMilliseconds;
        private double clockSyncAbsoluteOffsetTotalMilliseconds;
        private double clockSyncMaximumAbsoluteOffsetMilliseconds;
        private double clockSyncLastRoundTripMilliseconds;
        private double clockSyncRoundTripTotalMilliseconds;
        private DateTime clockSyncLastAtUtc;
        private ImuSample latestImuSample;
        private bool hasLatestImuSample;
        // HAL_GetTick/PCtime can advance while a sensor scan is stale. Track
        // only actual accel/gyro/mag tuple changes for a display warning.
        private DateTime imuTupleChangedAtUtc = DateTime.MinValue;
        private bool hasAuthoritativeImuFreshness;
        private ulong lastImuCompletionCount;
        private uint lastImuCompletionTick;
        private uint lastImuRetryRecoveryCount;
        private DateTime imuCompletionChangedAtUtc = DateTime.MinValue;
        private DateTime lastImuTelemetryPostedAtUtc = DateTime.MinValue;

        private struct ImuSample
        {
            internal short Ax, Ay, Az;
            internal short Gx, Gy, Gz;
            internal short Mx, My, Mz;
            internal uint PcTime;
        }

        private struct ImuFreshness
        {
            internal bool IsAuthoritative;
            internal TimeSpan UnchangedFor;
            internal ulong CompletionCount;
            internal uint RetryRecoveryCount;
        }

        public Form1()
        {
            // ImuTelemetryPanel is an internal nested control, so the WinForms
            // designer does not reliably preserve its construction statement
            // when it rewrites mainForm.Designer.cs.  Construct it before the
            // generated layout code applies its properties and adds it.
            imuTelemetry = new ImuTelemetryPanel();
            InitializeComponent();
            InitializeSystemInfo();
            InitializeNetworkTime();
            Icon=NpPreviewTheme.ApplicationIcon;
            NpInitializeRecovery();
            btnImpedance = new Button { Text = "Impedance", Location = new Point(360, 138), Size = new Size(105, 36), Enabled = false };
            btnImpedance.Click += btnImpedance_Click;
            Controls.Add(btnImpedance);
            btnSdInfo=new Button { Text="SD Info",Location=new Point(360,106),Size=new Size(105,32),Enabled=false };
            btnSdInfo.Click+=delegate {
                if(running || systemInfoActive!=0 || selectedDeviceKind!=DeviceKind.XeNp || String.IsNullOrWhiteSpace(devicePath))return;
                using(var dialog=new XeNpSdInfoForm(devicePath))dialog.ShowDialog(this);
                RefreshSystemInfo();
            };
            Controls.Add(btnSdInfo);
            btnNpLogger = new Button { Text = "SD Download", Location = new Point(471, 106), Size = new Size(93, 32) };
            btnNpLogger.Click += delegate
            {
                if (running || ztestRunning || systemInfoActive!=0) return;
                workspaceTabs.SelectedTab=downloadPage;
            };
            Controls.Add(btnNpLogger);
            InitializeWildX();
        }

        private static string GetPackageVersion()
        {
            // Identify the executable that is actually running; installer
            // registry metadata may describe a different installed copy.
            return typeof(Form1).Assembly.GetName().Version.ToString();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            lblSpeed.Text = "WinUSB idle";
            lblRecInfo.Text = "Select a Neuropixels USB device and destination folder, or use BLE Mode.";
            RefreshDeviceList();
        }

        private void btnStreams_Click(object sender, EventArgs e)
        {
            if(running || ztestRunning || systemInfoActive!=0 || deviceRefreshActive!=0 || firmwareManagementActive || DownloadBusy || BleInUse || !npRecoveryCancelled)return;
            if(selectedDeviceKind==DeviceKind.NpV2) {
                NpShowModeSettings();
                return;
            }
            if(selectedDeviceKind==DeviceKind.XeNp) {
                if(!npRecoveryCancelled)return;
                NpShowModeSettings();
                return;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = "USB512 stream selection";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(290, 218);

                CheckBox ephys = new CheckBox { Text = "Amplifier (20 kHz)", Location = new Point(14, 14), AutoSize = true,
                    Checked = (requestedStreams & USB512WinUsbDevice.StreamMaskEphys) != 0 };
                CheckBox imu = new CheckBox { Text = "IMU / analogin (1.25 kHz)", Location = new Point(14, 40), AutoSize = true,
                    Checked = (requestedStreams & USB512WinUsbDevice.StreamMaskImu) != 0 };
                CheckBox camera = new CheckBox { Text = "Camera", Location = new Point(14, 66), AutoSize = true,
                    Checked = (requestedStreams & USB512WinUsbDevice.StreamMaskCamera) != 0 };
                CheckBox microphone = new CheckBox { Text = "ADC / microphone (240 kHz)", Location = new Point(14, 92), AutoSize = true,
                    Checked = (requestedStreams & USB512WinUsbDevice.StreamMaskMicrophone) != 0 };
                CheckBox neuropixels = new CheckBox { Text = "Neuropixels (device-selected rate)", Location = new Point(14, 118), AutoSize = true,
                    Checked = (requestedStreams & USB512WinUsbDevice.StreamMaskNeuropixels) != 0 };
                ComboBox profile = new ComboBox { Location = new Point(36, 145), Size = new Size(230, 21), DropDownStyle = ComboBoxStyle.DropDownList };
                profile.Items.Add("Normal: 50 Hz, 72 MHz camera SPI");
                profile.Items.Add("Balanced: 40 Hz, 72 MHz camera SPI");
                profile.Items.Add("Safe: 30 Hz, 36 MHz camera SPI");
                profile.SelectedIndex = requestedCameraProfile == USB512WinUsbDevice.CameraProfileSafe30Hz ? 2 :
                    (requestedCameraRateHz == 40 ? 1 : 0);
                Label label = new Label { Text = "Camera link profile:", Location = new Point(14, 171), AutoSize = true };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(110, 191), Size = new Size(75, 26) };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(191, 191), Size = new Size(75, 26) };
                dialog.ClientSize = new Size(290, 230);
                dialog.Controls.AddRange(new Control[] { ephys, imu, camera, microphone, neuropixels, profile, label, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                WildXTheme.Apply(dialog);WildXTheme.Button(ok,true);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                ushort selected = 0;
                if (ephys.Checked) selected |= USB512WinUsbDevice.StreamMaskEphys;
                if (imu.Checked) selected |= USB512WinUsbDevice.StreamMaskImu;
                if (camera.Checked) selected |= USB512WinUsbDevice.StreamMaskCamera;
                if (microphone.Checked) selected |= USB512WinUsbDevice.StreamMaskMicrophone;
                if (neuropixels.Checked) selected |= USB512WinUsbDevice.StreamMaskNeuropixels;
                if (selected == 0)
                {
                    MessageBox.Show(this, "Select at least one stream.", "USB512", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                requestedStreams = selected;
                if (profile.SelectedIndex == 2)
                {
                    requestedCameraProfile = USB512WinUsbDevice.CameraProfileSafe30Hz;
                    requestedCameraRateHz = 30;
                }
                else
                {
                    requestedCameraProfile = USB512WinUsbDevice.CameraProfileNormal50Hz;
                    requestedCameraRateHz = profile.SelectedIndex == 1 ? (ushort)40 : (ushort)50;
                }
            }
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            if (!running)
                RefreshDeviceList();
        }

        private void comboDrives_Click(object sender, EventArgs e)
        {
            // Hardware enumeration can include control transfers to distinguish
            // V1 from V2. Do not repeat that scan on every ordinary drop-down
            // click; the Refresh button remains the explicit rescan path.
            if (!running && comboDrives.Items.Count == 0)
                RefreshDeviceList();
        }

        private void comboDrives_SelectedIndexChanged(object sender, EventArgs e)
        {
            DeviceItem item = comboDrives.SelectedItem as DeviceItem;
            devicePath = item == null ? null : item.Path;
            selectedDeviceKind = item == null ? DeviceKind.USB512 : item.Kind;
            if (selectedDeviceKind != DeviceKind.Ce64Msc)
                lblRecInfo.MaximumSize = Size.Empty;
            bool npDevice = selectedDeviceKind == DeviceKind.XeNp || selectedDeviceKind == DeviceKind.NpV2 || selectedDeviceKind == DeviceKind.NpUnavailable;
            btnScope.Text = npDevice ? "NP Preview" : "Ephys Preview";
            btnPreview.Enabled = selectedDeviceKind == DeviceKind.XeNp || (!npDevice && selectedDeviceKind != DeviceKind.Ce64Msc);
            btnStreams.Enabled = selectedDeviceKind != DeviceKind.Ce64Msc;
            btnScope.Enabled = selectedDeviceKind != DeviceKind.Ce64Msc && selectedDeviceKind != DeviceKind.NpUnavailable;
            if (btnImpedance != null) btnImpedance.Enabled = selectedDeviceKind == DeviceKind.Ce64Msc && !running && !ztestRunning;
            SystemInfoSelectionChanged();
            UpdateStartEnabled();
        }

        private void btnImpedance_Click(object sender, EventArgs e)
        {
            if (ztestRunning)
            {
                if (ztestCancellation != null) ztestCancellation.Cancel();
                btnImpedance.Enabled = false;
                btnImpedance.Text = "Stopping...";
                return;
            }
            if (running || systemInfoActive!=0 || selectedDeviceKind != DeviceKind.Ce64Msc || String.IsNullOrWhiteSpace(devicePath)) return;
            StartZtest(false);
        }

        private void StartZtest(bool continuous)
        {
            if (ztestRunning || running || systemInfoActive!=0 || selectedDeviceKind != DeviceKind.Ce64Msc || String.IsNullOrWhiteSpace(devicePath)) return;
            if (ztestReport != null && !ztestReport.IsDisposed)
            {
                ztestReport.Close();
                ztestReport = null;
            }
            ztestRunning = true;
            ztestContinuous = continuous;
            ztestCancellation = new CancellationTokenSource();
            btnImpedance.Text = continuous ? "Stop impedance" : "Cancel impedance";
            btnStart.Enabled = btnDfu.Enabled = btnRefresh.Enabled = comboDrives.Enabled = false;
            Thread worker = new Thread(ZtestWorker) { IsBackground = true,
                Name = continuous ? "WILD64 continuous impedance measurement" : "WILD64 impedance measurement",
                Priority = ThreadPriority.AboveNormal };
            worker.Start();
        }

        private void ZtestWorker()
        {
            string failure = null;
            IList<Ce64MscZtestResult> results = null;
            uint frequency = 0, elapsedMilliseconds = 0;
            int completedSweeps = 0;
            bool continuous = ztestContinuous;
            bool eisSupported = false;
            try
            {
                Ce64MscDeviceInfo selected = null;
                foreach (Ce64MscDeviceInfo candidate in Ce64MscDiscovery.Enumerate())
                    if (String.Equals(candidate.PhysicalDrivePath, devicePath, StringComparison.OrdinalIgnoreCase)) { selected = candidate; break; }
                if (selected == null) throw new IOException("WILD64 MSC device is no longer present.");
                using (Ce64MscRawBlockDevice device = Ce64MscRawBlockDevice.Open(selected))
                {
                    device.ValidateManifest();
                    Ce64MscCapabilities caps = device.ReadCapabilities();
                    Ce64MscZtestCapabilities zcaps = caps.Ztest;
                    eisSupported = caps.Eis != null;
                    Ce64MscStatus initial = device.ReadStatus();
                    if (zcaps == null || initial.State != Ce64MscState.Idle || initial.Error != 0 || initial.PendingFrames != 0 || initial.ImuPendingRecords != 0)
                        throw new InvalidOperationException("Impedance requires a supported, error-free WILD64 in Idle with no pending capture data.");
                    Ce64MscZtestInfo before = device.ReadZtestInfo(zcaps);
                    if (before.State != Ce64MscZtestState.Idle && before.State != Ce64MscZtestState.Complete && before.State != Ce64MscZtestState.Cancelled)
                        throw new InvalidOperationException("WILD64 impedance engine is not idle (state " + before.State + ").");
                    while (!ztestCancellation.IsCancellationRequested)
                    {
                        // START may have reached the command mailbox before an
                        // operator presses Stop. Never abandon that ACK wait with
                        // the UI token: after entering ZTEST the inner loop sends
                        // the protocol-defined CANCEL command instead.
                        Ce64MscStatus started = device.Execute(Ce64MscCommand.StartZtest, 0, 0, 0,
                            TimeSpan.FromSeconds(2), CancellationToken.None);
                        if (started.State != Ce64MscState.Ztest) throw new IOException("WILD64 did not enter impedance state.");
                        bool cancelSent = false;
                        while (true)
                        {
                            if (ztestCancellation.IsCancellationRequested && !cancelSent)
                            {
                                if (!zcaps.SupportsCancel) throw new NotSupportedException("WILD64 does not advertise impedance cancellation.");
                                Ce64MscStatus cancelled = device.Execute(Ce64MscCommand.CancelZtest, 0, 0, 0, TimeSpan.FromSeconds(2), CancellationToken.None);
                                Ce64MscZtestInfo cancelledInfo = device.ReadZtestInfo(zcaps);
                                if (cancelled.State != Ce64MscState.Idle || cancelledInfo.State != Ce64MscZtestState.Cancelled || cancelledInfo.Error != 0)
                                    throw new IOException("WILD64 impedance cancellation did not reach C64I Cancelled and C64S Idle.");
                                cancelSent = true;
                            }
                            Ce64MscZtestInfo info = device.ReadZtestInfo(zcaps);
                            if (info.RequestSequence != started.AckSequence)
                                throw new InvalidDataException("WILD64 impedance C64I request sequence changed during the measurement.");
                            int displaySweep = completedSweeps + 1;
                            PostToUi(delegate
                            {
                                lblRecInfo.Text = (continuous ? "WILD64 continuous impedance sweep " + displaySweep + ": " : "WILD64 impedance: ") +
                                    info.State + " " + info.Completed + "/" + info.Total + " channels, " + info.ElapsedMilliseconds + " ms";
                            });
                            if (info.State == Ce64MscZtestState.Error || info.Error != 0)
                                throw new Ce64MscProtocolException("WILD64 impedance failed, device code " + info.Error + ".", info.Error);
                            if (info.State == Ce64MscZtestState.Cancelled) return;
                            if (info.State == Ce64MscZtestState.Complete)
                            {
                                if (info.Completed != 64 || info.Total != 64 || info.ResultFirstLba != zcaps.ResultFirstLba || info.ResultSectors != zcaps.ResultSectors ||
                                    info.Epoch == 0 || info.Result0Crc == 0 || info.Result1Crc == 0)
                                    throw new InvalidDataException("WILD64 impedance completion/result layout is incomplete.");
                                results = device.ReadZtestResults(info);
                                frequency = info.FrequencyHz;
                                elapsedMilliseconds = info.ElapsedMilliseconds;
                                Ce64MscStatus final = device.ReadStatus();
                                if (final.State != Ce64MscState.Idle || final.Error != 0 || final.PendingFrames != 0 || final.ImuPendingRecords != 0 ||
                                    (final.Compressed != null && (final.Compressed.FifoPendingBytes != 0 || final.Compressed.FifoOverflowBlocks != 0 ||
                                    final.Compressed.CodecErrorBlocks != 0 || final.Compressed.SourcePendingFrames != 0 || final.Compressed.SourceOverrunBlocks != 0)))
                                    throw new IOException("WILD64 did not return a clean Idle transport state after impedance measurement.");
                                completedSweeps++;
                                if (!continuous) return;
                                IList<Ce64MscZtestResult> snapshot = results;
                                uint snapshotFrequency = frequency;
                                uint snapshotElapsed = elapsedMilliseconds;
                                int snapshotSweep = completedSweeps;
                                PostToUi(delegate { ShowContinuousZtestResults(snapshot, snapshotFrequency, snapshotElapsed, snapshotSweep); });
                                break;
                            }
                            Thread.Sleep(100);
                        }
                        if (!continuous || ztestCancellation.IsCancellationRequested) return;
                        Thread.Sleep(100);
                    }
                }
            }
            catch (OperationCanceledException) { failure = "Impedance measurement cancelled."; }
            catch (Exception ex) { failure = ex.Message; }
            finally
            {
                PostToUi(delegate
                {
                    ztestRunning = false;
                    if (ztestCancellation != null) { ztestCancellation.Dispose(); ztestCancellation = null; }
                    btnImpedance.Text = "Impedance";
                    comboDrives.Enabled = btnRefresh.Enabled = true;
                    UpdateStartEnabled();
                    if (continuous)
                    {
                        string stopped = failure ?? "stopped after " + completedSweeps + " complete sweep" + (completedSweeps == 1 ? "" : "s");
                        if (ztestReport != null && !ztestReport.IsDisposed) ztestReport.MarkContinuousStopped(stopped);
                        lblRecInfo.Text = "WILD64 continuous impedance " + stopped + ".";
                    }
                    else if (results != null) ShowZtestResults(results, frequency, elapsedMilliseconds, eisSupported);
                    else lblRecInfo.Text = failure ?? "WILD64 impedance cancelled.";
                });
            }
        }

        private void ShowContinuousZtestResults(IList<Ce64MscZtestResult> results, uint frequencyHz,
            uint elapsedMilliseconds, int sweepNumber)
        {
            Action export = delegate { ExportZtestCsv(results, frequencyHz, elapsedMilliseconds); };
            if (ztestReport == null || ztestReport.IsDisposed)
            {
                ztestReport = new ImpedanceReportForm(results, frequencyHz, elapsedMilliseconds, export, true,
                    delegate { if (ztestCancellation != null) ztestCancellation.Cancel(); }, sweepNumber);
                ztestReport.FormClosed += delegate { ztestReport = null; };
                ztestReport.Show(this);
            }
            else
            {
                ztestReport.UpdateMeasurement(results, frequencyHz, elapsedMilliseconds, export, sweepNumber);
            }
        }

        private void ShowZtestResults(IList<Ce64MscZtestResult> results, uint frequencyHz, uint elapsedMilliseconds, bool eisSupported)
        {
            DialogResult result;
            int selectedEisChannel;
            bool continuousRequested;
            using (ImpedanceReportForm report = new ImpedanceReportForm(results, frequencyHz, elapsedMilliseconds,
                delegate { ExportZtestCsv(results, frequencyHz, elapsedMilliseconds); }))
            {
                report.SetEisAvailable(eisSupported);
                result = report.ShowDialog(this);
                selectedEisChannel = report.SelectedEisChannel;
                continuousRequested = report.ContinuousRequested;
            }

            if (result == DialogResult.Retry)
            {
                lblRecInfo.Text = continuousRequested ?
                    "Starting continuous WILD64 impedance measurement..." :
                    "Refreshing WILD64 impedance measurement...";
                StartZtest(continuousRequested);
                return;
            }
            if (result == DialogResult.Yes && selectedEisChannel >= 0)
            {
                StartEis(selectedEisChannel);
                return;
            }
            lblRecInfo.Text = "WILD64 impedance complete: 64 channels at " + frequencyHz + " Hz in " + elapsedMilliseconds + " ms.";
        }

        private void StartEis(int channel)
        {
            if (channel < 0 || channel >= 64 || ztestRunning || running || selectedDeviceKind != DeviceKind.Ce64Msc || String.IsNullOrWhiteSpace(devicePath)) return;
            ztestRunning = true;
            ztestContinuous = false;
            ztestCancellation = new CancellationTokenSource();
            btnImpedance.Text = "Stop EIS";
            btnStart.Enabled = btnDfu.Enabled = btnRefresh.Enabled = comboDrives.Enabled = false;
            lblRecInfo.Text = "Starting 64-point EIS spectrum on E" + channel.ToString("00") + "...";
            Thread worker = new Thread(new ThreadStart(delegate { EisWorker(channel); })) { IsBackground = true, Name = "WILD64 EIS" };
            worker.Start();
        }

        private void EisWorker(int channel)
        {
            IList<Ce64MscEisResult> results = null;
            uint sampleRateHz = 0, elapsedMilliseconds = 0;
            string failure = null;
            try
            {
                Ce64MscDeviceInfo selected = null;
                foreach (Ce64MscDeviceInfo candidate in Ce64MscDiscovery.Enumerate())
                    if (String.Equals(candidate.PhysicalDrivePath, devicePath, StringComparison.OrdinalIgnoreCase)) { selected = candidate; break; }
                if (selected == null) throw new IOException("WILD64 MSC device is no longer present.");
                using (Ce64MscRawBlockDevice device = Ce64MscRawBlockDevice.Open(selected))
                {
                    device.ValidateManifest();
                    Ce64MscCapabilities caps = device.ReadCapabilities();
                    Ce64MscEisCapabilities eisCaps = caps.Eis;
                    if (eisCaps == null) throw new NotSupportedException("This WILD64 firmware does not support selected-electrode EIS.");
                    Ce64MscStatus before = device.ReadStatus();
                    if (before.State != Ce64MscState.Idle || before.Error != 0 || before.PendingFrames != 0 || before.ImuPendingRecords != 0 ||
                        (before.Compressed != null && (before.Compressed.FifoPendingBytes != 0 || before.Compressed.SourcePendingFrames != 0)))
                        throw new IOException("WILD64 must be clean Idle before EIS.");
                    Ce64MscEisInfo prior = device.ReadEisInfo(eisCaps);
                    if (prior.State == Ce64MscZtestState.Preparing || prior.State == Ce64MscZtestState.Measuring)
                        throw new IOException("WILD64 already reports an active EIS sweep.");

                    Ce64MscStatus started = device.Execute(Ce64MscCommand.StartEis, 0, (byte)channel, 0,
                        TimeSpan.FromSeconds(3), CancellationToken.None);
                    uint requestSequence = started.AckSequence;
                    bool cancelSent = false;
                    while (true)
                    {
                        if (ztestCancellation.IsCancellationRequested && !cancelSent)
                        {
                            cancelSent = true;
                            Ce64MscStatus cancelled = device.Execute(Ce64MscCommand.CancelEis, 0, 0, 0,
                                TimeSpan.FromSeconds(2), CancellationToken.None);
                            Ce64MscEisInfo cancelledInfo = device.ReadEisInfo(eisCaps);
                            if (cancelled.State != Ce64MscState.Idle || cancelledInfo.State != Ce64MscZtestState.Cancelled || cancelledInfo.Error != 0)
                                throw new IOException("WILD64 did not cancel EIS cleanly.");
                            return;
                        }
                        Ce64MscEisInfo info = device.ReadEisInfo(eisCaps);
                        if (info.RequestSequence != requestSequence || info.Channel != (uint)channel)
                            throw new InvalidDataException("WILD64 EIS info does not match the active electrode/request.");
                        sampleRateHz = info.SampleRateHz;
                        elapsedMilliseconds = info.ElapsedMilliseconds;
                        uint completed = info.Completed;
                        PostToUi(delegate { lblRecInfo.Text = "E" + channel.ToString("00") + " EIS: " + completed + "/64 points..."; });
                        if (info.State == Ce64MscZtestState.Error || info.Error != 0)
                            throw new IOException("WILD64 EIS failed, device code " + info.Error + ".");
                        if (info.State == Ce64MscZtestState.Cancelled) return;
                        if (info.State == Ce64MscZtestState.Complete)
                        {
                            if (info.Completed != eisCaps.Points || info.Total != eisCaps.Points || info.Epoch == 0 || info.Result0Crc == 0 || info.Result1Crc == 0)
                                throw new InvalidDataException("WILD64 completed EIS metadata is incomplete.");
                            Ce64MscStatus terminal = device.ReadStatus();
                            if (terminal.State != Ce64MscState.Idle || terminal.Error != 0)
                                throw new IOException("WILD64 did not return to clean Idle after EIS.");
                            results = device.ReadEisResults(info);
                            break;
                        }
                        Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex) { failure = ex.Message; }
            finally
            {
                IList<Ce64MscEisResult> completedResults = results;
                uint completedRate = sampleRateHz, completedElapsed = elapsedMilliseconds;
                string completedFailure = failure;
                PostToUi(delegate
                {
                    ztestRunning = false;
                    if (ztestCancellation != null) { ztestCancellation.Dispose(); ztestCancellation = null; }
                    btnImpedance.Text = "Impedance";
                    btnRefresh.Enabled = comboDrives.Enabled = true;
                    UpdateStartEnabled();
                    if (completedResults != null)
                        ShowEisResults(channel, completedResults, completedRate, completedElapsed);
                    else lblRecInfo.Text = completedFailure ?? "WILD64 EIS cancelled.";
                });
            }
        }

        private void ShowEisResults(int channel, IList<Ce64MscEisResult> results, uint sampleRateHz, uint elapsedMilliseconds)
        {
            using (EisSpectrumForm report = new EisSpectrumForm(channel, results, sampleRateHz, elapsedMilliseconds,
                delegate { ExportEisCsv(channel, results, sampleRateHz, elapsedMilliseconds); }))
                report.ShowDialog(this);
            lblRecInfo.Text = "WILD64 EIS complete: E" + channel.ToString("00") + ", " + results.Count + " points.";
        }

        private void ExportEisCsv(int channel, IList<Ce64MscEisResult> results, uint sampleRateHz, uint elapsedMilliseconds)
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", FileName = "WILD64-E" + channel.ToString("00") + "-EIS.csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (StreamWriter output = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8))
                {
                    output.WriteLine("channel," + channel);
                    output.WriteLine("sample_rate_hz," + sampleRateHz);
                    output.WriteLine("elapsed_ms," + elapsedMilliseconds);
                    output.WriteLine("nominal_uncalibrated,true");
                    output.WriteLine("frequency_hz,magnitude_ohm,phase_deci_deg,phase_deg,range,quality_flags");
                    foreach (Ce64MscEisResult r in results)
                        output.WriteLine(r.FrequencyHz + "," + r.Ohms + "," + r.PhaseTenths + "," +
                            (r.PhaseTenths / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                            ((r.Flags >> 4) & 3) + ",0x" + r.Flags.ToString("X4"));
                }
            }
        }

        private void ExportZtestCsv(IList<Ce64MscZtestResult> results, uint frequencyHz, uint elapsedMilliseconds)
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "WILD64_impedance_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (StreamWriter output = new StreamWriter(dialog.FileName, false))
                { output.WriteLine("frequency_hz," + frequencyHz); output.WriteLine("elapsed_ms," + elapsedMilliseconds); output.WriteLine("channel,bank,magnitude_ohm,phase_deci_deg,phase_deg,flags"); foreach (Ce64MscZtestResult r in results) output.WriteLine(r.Channel + "," + (r.Channel < 32 ? 0 : 1) + "," + r.Ohms + "," + r.PhaseTenths + "," + (r.PhaseTenths / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",0x" + r.Flags.ToString("X4")); }
            }
        }

        private void RefreshDeviceList()
        {
            if (running || systemInfoActive!=0 || Interlocked.CompareExchange(ref deviceRefreshActive, 1, 0) != 0)
                return;

            btnRefresh.Enabled = false;
            comboDrives.Enabled = false;
            btnStart.Enabled = false;
            lblRecInfo.Text = "Scanning connected devices...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DeviceItem> discovered = null;
                string failure = null;
                try
                {
                    discovered = EnumerateDeviceItems();
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }

                PostToUi(delegate { CompleteDeviceRefresh(discovered, failure); });
            });
        }

        private static List<DeviceItem> EnumerateDeviceItems()
        {
            List<DeviceItem> discovered = new List<DeviceItem>();

            var v2Paths = new HashSet<string>(NpV2UsbDevice.EnumerateDevicePaths(), StringComparer.OrdinalIgnoreCase);
            var v1Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NpV1Target target in NpV1Management.Enumerate())
                if (!target.Recovery) v1Paths.Add(target.Path);
            foreach (string path in XeNpWinUsbDevice.EnumerateDevicePaths())
            {
                bool v2 = v2Paths.Contains(path);
                bool v1 = v1Paths.Contains(path);
                string productName = v1 || v2 ? null : GetNpUsbProductName(path);
                discovered.Add(new DeviceItem
                {
                    Path = path,
                    Kind = v2 ? DeviceKind.NpV2 : v1 ? DeviceKind.XeNp : DeviceKind.NpUnavailable,
                    DisplayName = v2 ? "WILD-X V2 / NP2 USB" :
                        v1 ? "WILD-X V1 / NP1 USB (384 AP + 384 LFP)" :
                        (String.IsNullOrWhiteSpace(productName) ? "Unidentified NP USB device" : productName) +
                        " (NP service unavailable; recording disabled)"
                });
            }

            return discovered;
        }

        private static string GetNpUsbProductName(string path)
        {
            // This is presentation only. A Windows product name must never
            // select the NP1 or NP2 decoder without a verified protocol.
            try
            {
                string[] parts = (path ?? String.Empty).Split('#');
                if (parts.Length < 3 || !parts[1].Equals("vid_1209&pid_b386", StringComparison.OrdinalIgnoreCase))
                    return null;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\USB\VID_1209&PID_B386\" + parts[2], false))
                {
                    string name=key==null?null:key.GetValue("FriendlyName") as string;
                    return name==null?null:System.Text.RegularExpressions.Regex.Replace(name,
                        @"(?<![A-Za-z0-9.])(?:XE[_ -]?NP|XENP)(?![A-Za-z0-9])","WILDX",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
            catch (Exception) { return null; }
        }

        private void CompleteDeviceRefresh(List<DeviceItem> discovered, string failure)
        {
            try
            {
                if (failure != null)
                {
                    lblRecInfo.Text = "Device scan failed: " + failure;
                    return;
                }

                string selected = devicePath;
                comboDrives.BeginUpdate();
                try
                {
                    comboDrives.Items.Clear();
                    foreach (DeviceItem item in discovered)
                        comboDrives.Items.Add(item);

                    for (int i = 0; i < comboDrives.Items.Count; i++)
                    {
                        DeviceItem item = comboDrives.Items[i] as DeviceItem;
                        if (item != null && String.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase))
                        {
                            comboDrives.SelectedIndex = i;
                            break;
                        }
                    }

                    if (comboDrives.SelectedIndex < 0 && comboDrives.Items.Count == 1)
                        comboDrives.SelectedIndex = 0;
                }
                finally
                {
                    comboDrives.EndUpdate();
                }

                if (comboDrives.Items.Count == 0)
                {
                    devicePath = null;
                    comboDrives.Text = "No Neuropixels USB device found";
                    lblRecInfo.Text = "No supported device found. Connect a device, then press Refresh.";
                }
                else
                {
                    lblRecInfo.Text = comboDrives.SelectedIndex >= 0
                        ? selectedDeviceKind == DeviceKind.NpUnavailable
                            ? "NP USB interface enumerated, but its recording protocol did not respond. Recording is disabled; when idle, reconnect the device and Refresh."
                            : "Device ready. Choose a destination folder, then start recording."
                        : "Select a device and destination folder.";
                }
            }
            finally
            {
                Interlocked.Exchange(ref deviceRefreshActive, 0);
                comboDrives.Enabled = !running && !ztestRunning;
                btnRefresh.Enabled = !running && !ztestRunning;
                RefreshSystemInfo();
                UpdateStartEnabled();
            }
        }

        private void btnSaveFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose a folder for the USB512 stream files";
                dialog.SelectedPath = saveFolder ?? String.Empty;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    saveFolder = dialog.SelectedPath;
                    llblSaveFolder.Text = saveFolder;
                    UpdateStartEnabled();
                }
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (running || systemInfoActive!=0 || String.IsNullOrWhiteSpace(devicePath) || String.IsNullOrWhiteSpace(saveFolder))
                return;
            if (selectedDeviceKind == DeviceKind.NpUnavailable)
            {
                MessageBox.Show(this, "This NP USB device did not answer its protocol-status request. When the device is idle, reconnect it and Refresh before recording.",
                    "NP service unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                runFolder = Path.Combine(saveFolder,
                    (selectedDeviceKind == DeviceKind.NpV2 ? "WILD_X_V2_NP2_" :
                     selectedDeviceKind == DeviceKind.XeNp ? "WILDX_" :
                     selectedDeviceKind == DeviceKind.Ce64Msc ? "WILD64_" : "USB512_") +
                    DateTime.Now.ToString(selectedDeviceKind==DeviceKind.XeNp?"yyyy-MM-dd_HH-mm-ss_fff":"yyyy-MM-dd_HH-mm-ss"));
                runFolder=NpSegmentFolder(runFolder);
                if(selectedDeviceKind==DeviceKind.XeNp && Directory.Exists(runFolder))
                    runFolder+="_"+Guid.NewGuid().ToString("N").Substring(0,8);
                Directory.CreateDirectory(runFolder);
                npRecordingSettings=null;
                npRunCalibration = null;
                if(selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2) {
                    var calibration = NpCalibrationStore.Get(devicePath);
                    calibration.RequireModel(selectedDeviceKind==DeviceKind.NpV2,7);
                    if(calibration.Count!=0){calibration.Save(runFolder);npRunCalibration=calibration;}
                }
                if(selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2) {
                    bool np2=selectedDeviceKind==DeviceKind.NpV2;
                    npRecordingSettings=new NpRecordingSettings(runFolder,NpConfigurationDialog.HelperPath,"live_usb",np2,DateTime.UtcNow,devicePath,np2?NpV2FirmwareForm.Uid(devicePath):NpV1Management.UidFromPath(devicePath));
                    npRecordingSettings.Requested(np2?(int?)npV2RequestedSampleHz:npRequestedMode==null?null:(int?)npRequestedMode.RequestedApHz,
                        np2?npV2RequestedProbeMask==0?null:(int?)npV2RequestedProbeMask:npRequestedMode==null?null:(int?)npRequestedMode.ProbeMask,
                        npMergeProbeDat,np2?false:npUpsampleAp15To20);
                    if(npRunCalibration!=null)npRecordingSettings.Context("adc_calibration_file",NpCalibrationSet.ManifestName);
                    npRecordingSettings.Write();
                }
                var requests=selectedDeviceKind==DeviceKind.XeNp?NpRequestsForDevice(devicePath):null;
                if(requests!=null && requests.Count>0) {
                    foreach(var request in requests.Values) {
                        File.WriteAllText(Path.Combine(runFolder,"requested-configuration-probe"+(request.Port+1)+".npconfig.json"),request.Json);
                        npRecordingSettings.Request(request.Port+1,request.Json,request.Selection);
                    }
                    if(requests.Count==1)foreach(var request in requests.Values)
                        File.WriteAllText(Path.Combine(runFolder,"requested-configuration.npconfig.json"),request.Json);
                    File.WriteAllText(Path.Combine(runFolder,"requested-configuration-status.txt"),
                        "HardwareState=NotVerifiedForThisRecording\r\nThis is the editor request, not an applied-device receipt.\r\n"+
                        "If supported by the firmware, the post-stop applied receipt is saved separately as neuropixels_probeN_metadata.json.\r\n");
                }
                if(npRecordingSettings!=null)npRecordingSettings.Write();
                if(selectedDeviceKind!=DeviceKind.NpV2)OpenOutputFiles(runFolder);
            }
            catch (Exception ex)
            {
                CloseOutputFiles();
                MessageBox.Show(this, "Could not create the stream files.\r\n\r\n" + ex.Message,
                    "USB512", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ResetCaptureState();
            // WILD64 has seven concise recording-status rows between y=193 and
            // y=288. Bound only that presentation so it cannot paint over the
            // stale-bank indicator or IMU panel; the other stream summaries
            // keep their historical unbounded layout.
            lblRecInfo.MaximumSize = selectedDeviceKind == DeviceKind.Ce64Msc ?
                new Size(ClientSize.Width - 10, 91) : Size.Empty;
            ce64StopRequested = false;
            captureCameraEnabled = selectedDeviceKind == DeviceKind.XeNp ? npCameraEnabled :
                selectedDeviceKind != DeviceKind.NpV2 && selectedDeviceKind != DeviceKind.Ce64Msc &&
                (requestedStreams & USB512WinUsbDevice.StreamMaskCamera) != 0;
            activeStreams = 0;
            captureStartedAtUtc = DateTime.UtcNow;
            if(npRecordingSettings!=null)npRecordingSettings.Context("recording_start_time_utc_host",captureStartedAtUtc.ToString("O",System.Globalization.CultureInfo.InvariantCulture));
            running = true;
            if(calibrationButton!=null)calibrationButton.Enabled=false;
            if (selectedDeviceKind != DeviceKind.Ce64Msc && selectedDeviceKind != DeviceKind.NpV2)
                StartGoodChannelCounter();
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            btnDfu.Enabled = false;
            btnStreams.Enabled = false;
            btnRefresh.Enabled = false;
            comboDrives.Enabled = false;
            lblRecInfo.Text = "Opening WinUSB stream…";
            llblSavefile.Text = Path.Combine(runFolder,
                selectedDeviceKind == DeviceKind.NpV2 ? "neuropixels_ap.dat" :
                selectedDeviceKind == DeviceKind.XeNp ? "neuropixels_ap.dat" : "amplifier.dat");
            if (selectedDeviceKind == DeviceKind.Ce64Msc)
                llblSavefile.Text += "  (+ analogin.dat, timestamp.dat)";
            if (selectedDeviceKind == DeviceKind.Ce64Msc)
                lblRecInfo.Text = "Opening WILD64 MSC stream...";

            captureThread = new Thread(selectedDeviceKind == DeviceKind.NpV2 ?
                (ThreadStart)NpV2CaptureLoop : selectedDeviceKind == DeviceKind.XeNp ?
                (ThreadStart)XeNpCaptureLoop : selectedDeviceKind == DeviceKind.Ce64Msc ?
                (ThreadStart)Ce64MscCaptureLoop : CaptureLoop);
            captureThread.IsBackground = true;
            captureThread.Name = "USB512 WinUSB capture and logger";
            captureThread.Priority = ThreadPriority.Highest;
            captureThread.Start();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopCapture();
        }

        private void btnDfu_Click(object sender, EventArgs e)
        {
            if (selectedDeviceKind == DeviceKind.NpV2 || selectedDeviceKind == DeviceKind.NpUnavailable || String.IsNullOrWhiteSpace(devicePath)) {
                OpenV2Firmware();
                return;
            }
            if (systemInfoActive!=0 || !CanOpenFirmwareManagement(selectedDeviceKind,running,ztestRunning,devicePath))
                return;

            if (selectedDeviceKind == DeviceKind.XeNp)
            {
                // V1 uses its own protected APP updater, not USB512's DFU opcode.
                if(deviceRefreshActive!=0 || !npRecoveryCancelled)return;
                firmwareManagementActive=true;UpdateStartEnabled();
                try{using (var dialog = new NpFirmwareForm(devicePath)) dialog.ShowDialog(this);}
                finally{firmwareManagementActive=false;UpdateStartEnabled();}
                RefreshDeviceList();
                return;
            }

            if (selectedDeviceKind == DeviceKind.Ce64Msc)
            {
                RequestCe64Dfu();
                return;
            }

            DialogResult result = MessageBox.Show(
                this,
                "USB512 will disconnect from WinUSB and re-enumerate as the STM32 ROM DFU bootloader.\r\n\r\n" +
                "Use STM32CubeProgrammer over USB to program it. A normal reset returns to USB512 firmware.\r\n\r\n" +
                "Enter DFU mode now?",
                "Enter STM32 DFU mode",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
                return;

            btnDfu.Enabled = false;
            btnStart.Enabled = false;
            btnRefresh.Enabled = false;
            try
            {
                using (USB512WinUsbDevice device = USB512WinUsbDevice.Open(devicePath))
                {
                    device.EnterDfu();
                }

                devicePath = null;
                comboDrives.SelectedIndex = -1;
                comboDrives.Text = "USB512 entering STM32 DFU…";
                lblSpeed.Text = "STM32 DFU requested";
                lblRecInfo.Text = "USB512 is now re-enumerating as STM32 Bootloader (DFU). Use STM32CubeProgrammer over USB.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not request STM32 DFU mode.\r\n\r\n" + DescribeCaptureFailure(ex),
                    "USB512", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStartEnabled();
                btnRefresh.Enabled = true;
            }
        }

        // The MSC request is intentionally limited to a separately opened,
        // manifest-validated Idle device. No capture handle can survive a DFU
        // reset, and the using block below closes PhysicalDrive before the UI
        // begins waiting for the STM32 ROM bootloader's PnP identity.
        private void RequestCe64Dfu()
        {
            DialogResult result = MessageBox.Show(
                this,
                "WILD64 will leave USB mass-storage mode and re-enumerate as the STM32 ROM DFU bootloader.\r\n\r\n" +
                "Recording must be stopped. The SD-card filesystem is not modified. Use STM32CubeProgrammer over USB after DFU appears.\r\n\r\n" +
                "Enter DFU mode now?",
                "Enter WILD64 STM32 DFU mode",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
                return;

            btnDfu.Enabled = false;
            btnStart.Enabled = false;
            btnRefresh.Enabled = false;
            comboDrives.Enabled = false;
            try
            {
                Ce64MscDeviceInfo selected = null;
                foreach (Ce64MscDeviceInfo candidate in Ce64MscDiscovery.Enumerate())
                {
                    if (String.Equals(candidate.PhysicalDrivePath, devicePath, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = candidate;
                        break;
                    }
                }
                if (selected == null)
                    throw new IOException("WILD64 MSC device is no longer present.");

                using (Ce64MscRawBlockDevice device = Ce64MscRawBlockDevice.Open(selected))
                {
                    device.ValidateManifest();
                    Ce64MscStatus status = device.ReadStatus();
                    if (status.State != Ce64MscState.Idle || status.Error != 0 ||
                        status.PendingFrames != 0 || status.ImuPendingRecords != 0)
                        throw new InvalidOperationException("WILD64 must be Idle, error-free, and fully drained before requesting DFU (C64S " +
                            status.State + ", error " + status.Error + ", pending " + status.PendingFrames + "/" + status.ImuPendingRecords + ").");
                    // Firmware ACKs ENTER_DFU while still Idle, then holds the
                    // deferred system-memory jump for at least 500 ms. Require
                    // that exact ACK rather than accepting a disconnect, which
                    // could otherwise be an unrelated storage transport fault.
                    device.RequestDfuFromIdle(TimeSpan.FromSeconds(2), CancellationToken.None);
                }

                devicePath = null;
                comboDrives.SelectedIndex = -1;
                comboDrives.Text = "WILD64 entering STM32 DFU...";
                lblSpeed.Text = "WILD64 DFU acknowledged";
                lblRecInfo.Text = "WILD64 accepted ENTER_DFU while Idle; its raw disk handle is closed. Waiting for STM32 BOOTLOADER DFU (VID_0483&PID_DF11)...";
                BeginDfuDetection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not request WILD64 STM32 DFU mode.\r\n\r\n" + DescribeCaptureFailure(ex),
                    "WILD64", MessageBoxButtons.OK, MessageBoxIcon.Error);
                comboDrives.Enabled = true;
                btnRefresh.Enabled = true;
                UpdateStartEnabled();
            }
        }

        private void BeginDfuDetection()
        {
            Thread monitor = new Thread(delegate()
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    if (IsStm32DfuPresent())
                    {
                        PostToUi(delegate
                        {
                            comboDrives.Text = "STM32 BOOTLOADER DFU detected (VID_0483&PID_DF11)";
                            lblSpeed.Text = "STM32 DFU detected";
                            lblRecInfo.Text = "WILD64 is in STM32 BOOTLOADER DFU mode. Use STM32CubeProgrammer over USB; reset to return to WILD64 MSC mode.";
                        });
                        return;
                    }
                    Thread.Sleep(250);
                }
                PostToUi(delegate
                {
                    comboDrives.Enabled = true;
                    btnRefresh.Enabled = true;
                    comboDrives.Text = "WILD64 DFU requested; STM32 DFU not yet detected";
                    lblRecInfo.Text = "DFU command was sent but VID_0483&PID_DF11 was not observed within 10 seconds. Check the USB connection, then use Refresh or STM32CubeProgrammer.";
                });
            });
            monitor.IsBackground = true;
            monitor.Name = "STM32 DFU detection";
            monitor.Start();
        }

        private static bool IsStm32DfuPresent()
        {
            try
            {
                using (System.Management.ManagementObjectSearcher search =
                    new System.Management.ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_PnPEntity"))
                using (System.Management.ManagementObjectCollection devices = search.Get())
                {
                    foreach (System.Management.ManagementObject device in devices)
                    {
                        object value = device["PNPDeviceID"];
                        string pnp = value == null ? String.Empty : Convert.ToString(value);
                        if (!String.IsNullOrEmpty(pnp) &&
                            pnp.IndexOf("USB\\VID_0483&PID_DF11", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch (System.Management.ManagementException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        private void StopCapture()
        {
            NpCancelRecovery();
            if (!running && captureThread == null)
                return;

            if (selectedDeviceKind == DeviceKind.NpV2)
            {
                // The capture owner cancels/drains and posts completion. Keep
                // every UI/device owner busy until that callback releases it.
                ce64StopRequested = true;
                btnStop.Enabled = false;
                lblRecInfo.Text = "Stopping V2 NP USB; preserving completed blocks...";
                return;
            }

            // CE64 must freeze, drain, and then disarm on its capture thread.
            // Closing the physical-drive handle here would discard its final
            // valid partial sector and turn a user stop into data loss.
            if (selectedDeviceKind == DeviceKind.Ce64Msc)
            {
                ce64StopRequested = true;
                Thread gracefulThread = captureThread;
                if (gracefulThread != null && gracefulThread != Thread.CurrentThread && gracefulThread.Join(5000))
                    return;
                // A hung storage request cannot be drained safely. Only then
                // take the existing forced-close cancellation path.
                running = false;
                Ce64MscRawBlockDevice stuckDevice;
                CancellationTokenSource stuckCancel;
                lock (deviceLock) { stuckDevice = activeCe64Device; stuckCancel = ce64Cancellation; }
                if (stuckCancel != null) stuckCancel.Cancel();
                if (stuckDevice != null) stuckDevice.Abort();
                FinishCaptureOnUi("CE64 stop timed out; raw handle aborted");
                return;
            }

            running = false;
            USB512WinUsbDevice device;
            XeNpWinUsbDevice xeNpDevice;
            Ce64MscRawBlockDevice ce64Device;
            CancellationTokenSource ce64Cancel;
            lock (deviceLock)
            {
                device = activeDevice;
                xeNpDevice = activeXeNpDevice;
                ce64Device = activeCe64Device;
                ce64Cancel = ce64Cancellation;
            }
            if (device != null)
                device.Stop();
            // XE_NP polls overlapped completions every 50 ms. Let its owner
            // STOP, drain completed reads and close once, without a UI race.
            if (ce64Cancel != null) ce64Cancel.Cancel();
            if (ce64Device != null) ce64Device.Abort();

            Thread thread = captureThread;
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(3000);

            // The XE_NP owner reports success/failure only after its raw/decode
            // queue has drained. Do not overwrite an integrity error with Stop.
            if(selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2) {
                if(thread!=null && thread.IsAlive)lblRecInfo.Text=selectedDeviceKind==DeviceKind.NpV2?"Stopping V2 NP USB; preserving completed blocks...":"Stopping WILDX; draining captured data...";
                return;
            }

            if(thread!=null) {
                if(thread.IsAlive)lblRecInfo.Text="Stopping USB; draining buffered recording data...";
                return; // The capture owner publishes completion after closing files.
            }

            FinishCaptureOnUi("Stopped");
        }

        // XE_NP is intentionally separate from CaptureLoop.  Its firmware packet
        // header and start/stop requests are not USB512 stream packets, so sharing
        // USB512's replay and camera recovery state would corrupt both protocols.
        private void XeNpCaptureLoop()
        {
            int readBytes=64*1024, readDepth=64;
            int bufferCount=256;
            double hostQueueSeconds=0;
            string failure=null;
            XeNpWinUsbDevice device=null;
            USB512WinUsbQueuedReader reader=null;
            XeNpStatus profile=null;
            var parser=new NpCaptureSession();
            if(npRunCalibration!=null)parser.SampleCorrection=npRunCalibration.Correct;
            NpRecordingSettings settings=npRecordingSettings;
            NpCameraRecording camera=null;
            npCaptureSession=parser;
            NpModeCapabilities modeCaps=null;
            NpCaptureMode resolvedMode=null;
            NpCaptureRequest requestedMode=npRequestedMode;
            string modeResolution="firmware default";
            NpUsbSensors sensors=null;
            DateTime lastSensorPreview=DateTime.MinValue;uint lastSensorPreviewValid=0U;
            Exception workerFailure=null;
            Thread worker=null;
            BlockingCollection<byte[]> free=null;
            BlockingCollection<ReceiveBlock> completed=null;
            bool finalStatusRead=false;
            bool synchronousV1=false;
            bool usbStartIssued=false;
            int captureGc0=0,captureGc1=0,captureGc2=0;
            var previousGcLatency=System.Runtime.GCSettings.LatencyMode;
            bool captureGcLatencySet=false;
            uint firmwareFault=0;
            bool stoppedForProbeMetadata=false;
            var captureConfigurations=new List<WildNpConfiguration.NpConfigRequest>();
            var previewDefinitions=new NpPreviewDefinition[4];
            npFreshStartAvailable=false;
            string probeMetadataStatus="not queried";
            var probeMetadataLines=new List<string>();
            long maxReaderGapTicks=0,maxCompletionTicks=0,maxWorkerTicks=0,receivedBytes=0,readCount=0,previewDrops=0;
            int peakQueuedBlocks=0,minReadBytes=int.MaxValue;
            try
            {
                devicePath=NpV1Management.EnsureStreaming(devicePath);
                if(!running)return;
                var retained=NpRequestsForDevice(devicePath);
                if(retained!=null)captureConfigurations.AddRange(retained.Values);
                device=XeNpWinUsbDevice.Open(devicePath);
                lock(deviceLock) { activeXeNpDevice=device; }
                if(requestedMode==null) {
                    /* Default to every port the attached firmware presently
                     * advertises, not a hard-coded GPIO connector. This is a
                     * best-effort 30 kHz request; the capability resolver below
                     * selects a lower valid profile (for example 3 × 15 kHz)
                     * when the aggregate 30 kHz mode is not advertised. */
                    NpModeCapabilities defaults=device.GetModeCapabilities();
                    if(defaults!=null && defaults.AvailableMask!=0) {
                        requestedMode=new NpCaptureRequest(defaults.AvailableMask,30000);
                        modeResolution="automatic request: "+NpCaptureRequest.Ports(defaults.AvailableMask)+" at 30000 Hz";
                    }
                }
                string reconnectUid;
                modeCaps=device.SelectCaptureMode(requestedMode,out resolvedMode,out modeResolution,out reconnectUid);
                if(reconnectUid!=null) {
                    lock(deviceLock) { activeXeNpDevice=null; }
                    device.Dispose();device=null;
                    devicePath=NpV1Management.WaitForStreaming(reconnectUid);
                    if(!running)return;
                    device=XeNpWinUsbDevice.Open(devicePath);
                    lock(deviceLock) { activeXeNpDevice=device; }
                    modeCaps=device.GetModeCapabilities();
                    if(modeCaps==null || !modeCaps.Supports(resolvedMode) ||
                       modeCaps.ActiveMask!=resolvedMode.ProbeMask || modeCaps.ApHz!=resolvedMode.ApHz)
                        throw new IOException("Selected V1 did not acknowledge the resolved NP profile after its clock reset. No START sent.");
                    modeResolution+="; selected UID reconnected and active profile verified after clock reset";
                }
                profile=device.GetStatus();
                npFreshStartAvailable=profile.SupportsFreshStart;
                int captureRateHz=modeCaps==null?profile.ApSampleRateHz:modeCaps.ApHz;
                if(modeCaps==null)profile.RequireCaptureProfile(captureRateHz,profile.Layout);
                if(modeCaps==null && requestedMode!=null)
                    modeResolution="requested "+requestedMode.Describe()+"; firmware does not advertise mode selection, active profile is "+profile.ApSampleRateHz+" Hz";
                synchronousV1=profile.UsesV1SynchronousReads;
                if(synchronousV1) {
                    readBytes=128*1024;
                    readDepth=16;
                }
                // Only a user-initiated new recording may acknowledge an old
                // fault, and only with firmware advertising fresh START reset.
                if(!profile.SupportsFreshStart)profile.ThrowIfFault();
                if(modeCaps!=null)captureConfigurations.RemoveAll(r=>(modeCaps.ActiveMask&(1<<r.Port))==0);
                if(captureConfigurations.Count>0){
                    if(modeCaps==null || !modeCaps.SupportsStartupAdmission || !profile.SupportsFreshStart)
                        throw new IOException("This firmware cannot confirm requested configuration before releasing capture. No START sent.");
                    // SET mode and any core reset/reopen are complete. Staging
                    // here cannot be lost to the selected mode transition.
                    NpConfigurationUsb.StageForCapture(device,captureConfigurations,modeCaps.ActiveMask);
                    if(!running)return;
                }
                int queueProbeCount=modeCaps==null?1:modeCaps.Active.ProbeCount;
                int queueApHz=modeCaps==null?profile.ApSampleRateHz:modeCaps.ApHz;
                bufferCount=NpHostQueuePolicy.BufferCount(readBytes,queueProbeCount,queueApHz);
                hostQueueSeconds=NpHostQueuePolicy.CapacitySeconds(readBytes,bufferCount,queueProbeCount,queueApHz);
                npHostQueueCapacityBytes=(long)readBytes*bufferCount;
                npHostQueueCapacityBlocks=bufferCount;
                npHostQueueCapacitySeconds=hostQueueSeconds;
                Interlocked.Exchange(ref npHostQueueQueuedBlocks,0);
                free=new BlockingCollection<byte[]>(bufferCount);
                completed=new BlockingCollection<ReceiveBlock>(bufferCount);
                for(int i=0;i<bufferCount;i++)free.Add(new byte[readBytes]);
                activeStreams=USB512WinUsbDevice.StreamMaskNeuropixels;
                if(captureCameraEnabled) {
                    camera=new NpCameraRecording(runFolder);
                    activeStreams|=USB512WinUsbDevice.StreamMaskCamera;
                    parser.OnCamera=delegate(byte[] bytes,int offset,int length,byte flags,ushort sequence) {
                        bool valid=camera.Write(bytes,offset,length,flags,sequence);
                        Interlocked.Exchange(ref cameraPackets,camera.Packets);
                        Interlocked.Exchange(ref cameraPayloadBytes,camera.PayloadBytes);
                        Interlocked.Exchange(ref cameraFrames,camera.Frames);
                        Interlocked.Add(ref totalPayloadBytes,length);
                        if(valid)AssemblePreviewFrame(bytes,offset,length,flags);else ReleasePreviewAssembly();
                    };
                }
                if(npRecordMiscSignals)sensors=new NpUsbSensors(runFolder);
                parser.OnMisc=delegate(byte[] bytes,int offset,int length) {
                    if(sensors==null)return;
                    sensors.Feed(bytes,offset,length);
                    Interlocked.Increment(ref metadataPackets);
                    Interlocked.Exchange(ref npImuSamples,sensors.ImuSamples);
                    var now=DateTime.UtcNow;
                    if((now-lastSensorPreview).TotalMilliseconds>=100 || sensors.Valid!=lastSensorPreviewValid) {
                        lastSensorPreview=now;lastSensorPreviewValid=sensors.Valid;NpImuDisplay display=sensors.GetDisplay();
                        PostToUi(delegate {imuTelemetry.SetNpSample(display,false);});
                    }
                };
                parser.Configure(runFolder,profile,modeCaps,new NpCaptureOptions(npMergeProbeDat,npUpsampleAp15To20));
                if(settings!=null) {
                    settings.Requested(requestedMode==null?null:(int?)requestedMode.RequestedApHz,
                        requestedMode==null?null:(int?)requestedMode.ProbeMask,npMergeProbeDat,npUpsampleAp15To20);
                    settings.DeviceEvidence("usb_path",devicePath);
                    settings.DeviceEvidence("status_protocol_version",profile.Version);
                    settings.Context("mode_resolution",modeResolution);
                    settings.Context("physical_ports_known",parser.PhysicalPortsKnown);
                    settings.Streams(npRecordMiscSignals,null,captureCameraEnabled);
                    settings.Context("misc_signals_requested",npRecordMiscSignals);
                    settings.PlannedLayout(parser.ProbeMask,parser.MergedDat,parser.Probes.Count>1 && !parser.MergedDat);
                    settings.Write();
                }
                npActiveProbeCount=parser.Probes.Count;npPreviewMask=parser.ProbeMask;
                xeNpPreviewRateHz=captureRateHz;
                foreach(var request in captureConfigurations)
                    previewDefinitions[request.Port+1]=NpPreviewDefinition.FromSelection(request.Selection);
                // Run synchronously on the UI thread BEFORE any USB START.
                // A first preview open during acquisition must not allocate
                // its window/history and suspend the reader for a full GC.
                if(IsHandleCreated) {
                    Invoke((Action)delegate {
                        PrepareNpPreviewForCapture(captureRateHz);
                        if(captureCameraEnabled)PrepareCameraPreviewForCapture();
                        neuropixelsPreview.SetPhysicalPortReporting(parser.PhysicalPortsKnown);
                        // Prepare display-only site pages from cached requests
                        // before START. Publish only after the existing receipt.
                        neuropixelsPreview.PrepareProbeDefinitions(previewDefinitions);
                        llblSavefile.Text=(parser.MergedDat?Path.Combine(runFolder,"neuropixels_ap.dat"):
                            parser.Probes.Count>1?Path.Combine(runFolder,"probe"+parser.Probes[0].Probe,"neuropixels_ap.dat"):
                            Path.Combine(runFolder,"neuropixels_ap.dat"))+"  (+ analogin.dat)";
                    });
                }
                PostToUi(delegate {
                    lblRecInfo.Text=modeResolution+"\r\nRecording "+parser.Probes.Count+" NP probe(s): native "+captureRateHz+
                        " Hz AP; saved "+parser.OutputApHz+" Hz AP / "+parser.OutputLfpHzExact+" Hz LFP"+
                        (parser.MergedDat?"; merged "+parser.OutputChannelCount+" channels":"; individual probe DAT files");
                });
                // Allocate/prime every native request BEFORE START. Outstanding
                // URBs keep draining during brief scheduler/GC pauses. Only the
                // highest-priority reader harvests/rearms; it never decodes,
                // writes files, formats UI strings or waits for the renderer.
                // Normal WinUSB queuing sustained the full three-probe wire
                // rate in hardware tests. Keep AUTO_CLEAR_STALL available;
                // RAW_IO bypasses WinUSB's queue/error handling and is not
                // needed for this rate.
                reader=device.CreateQueuedReader(readBytes,readDepth,false);
                // Cache callbacks once, not once per packet (~46,000/s).
                parser.OnSamples=delegate(int probe,short[] ap,int apFrames,short[] lfp,int lfpFrames) {
                    Interlocked.Add(ref neuropixelsApRows,apFrames);
                    Interlocked.Add(ref neuropixelsLfpRows,lfpFrames);
                    NeuropixelsPreviewForm preview=neuropixelsPreview;
                    if(preview!=null)preview.PushSamples(probe,ap,apFrames,lfp,lfpFrames);
                };
                parser.OnPayload=delegate(int length) {
                    Interlocked.Add(ref neuropixelsPayloadBytes,length);
                    Interlocked.Exchange(ref neuropixelsPackets,parser.Packets);
                    Interlocked.Add(ref totalPayloadBytes,length);
                };
                worker=new Thread(delegate() {
                    try {
                        foreach(ReceiveBlock block in completed.GetConsumingEnumerable()) {
                            Interlocked.Decrement(ref npHostQueueQueuedBlocks);
                            long began=Stopwatch.GetTimestamp();
                            try {
                                parser.Feed(block.Buffer,block.Count,block.ReceiveUtc);
                                NeuropixelsPreviewForm preview=neuropixelsPreview;
                                if(preview!=null) {
                                    previewDrops=preview.DroppedPreviewRows;
                                    preview.UpdateRecordingReport(Interlocked.Read(ref neuropixelsPayloadBytes),
                                        parser.Packets,Interlocked.Read(ref neuropixelsApRows),
                                        Interlocked.Read(ref neuropixelsLfpRows),parser.Resynchronizations);
                                }
                                UpdateUiStatistics();
                            } finally {
                                Interlocked.Exchange(ref sequenceGaps,parser.SequenceGaps);
                                maxWorkerTicks=Math.Max(maxWorkerTicks,Stopwatch.GetTimestamp()-began);free.Add(block.Buffer);
                            }
                        }
                        parser.Complete();
                    } catch(Exception ex) { Interlocked.CompareExchange(ref workerFailure,ex,null); }
                });
                worker.IsBackground=true;worker.Name="WILDX raw and NP1 decoder";
                worker.Priority=ThreadPriority.AboveNormal;worker.Start();
                if(!running)return;
                // The synchronous V1 USB reader shares the GUI's CLR heap.
                // Finish startup collections BEFORE acquisition, then suppress
                // discretionary blocking Gen2 collections during recording.
                // This reduces pauses; it is not a guarantee against OS stalls
                // or memory pressure. All firmware overflow guards stay active.
                GC.Collect(2,GCCollectionMode.Forced,true);
                GC.WaitForPendingFinalizers();
                System.Runtime.GCSettings.LatencyMode=System.Runtime.GCLatencyMode.SustainedLowLatency;
                captureGcLatencySet=true;
                captureGc0=GC.CollectionCount(0);captureGc1=GC.CollectionCount(1);captureGc2=GC.CollectionCount(2);
                usbStartIssued=true;
                profile=device.Start(XeNpMcoDivider);
                if(profile.ApSampleRateHz!=parser.ApHz)
                    throw new IOException("Actual READY NP rate differs from the configured parser. Capture was not released.");
                if(device.StartupAdmission!=null) {
                    NpModeCapabilities admitted=device.GetModeCapabilities();
                    if(admitted==null || !admitted.SupportsStartupAdmission ||
                       admitted.ActiveMask!=device.StartupAdmission.AcceptedMask || admitted.ApHz!=modeCaps.ApHz ||
                       (admitted.WireId!=modeCaps.WireId && !(modeCaps.WireId==0x77 && modeCaps.ActiveMask==3 && modeCaps.ApHz==20000 &&
                         admitted.WireId==0x76 && (admitted.ActiveMask==1 || admitted.ActiveMask==2) && admitted.ApHz==20000)) ||
                       admitted.BatchPackets!=modeCaps.BatchPackets)
                        throw new IOException("NP accepted profile does not match the startup receipt.");
                    modeResolution+="; "+device.StartupAdmission.Describe();
                    if(admitted.ActiveMask!=modeCaps.ActiveMask) {
                        parser.ReconfigureBeforeFirstData(runFolder,profile,admitted,new NpCaptureOptions(npMergeProbeDat,npUpsampleAp15To20));
                        npActiveProbeCount=parser.Probes.Count;npPreviewMask=parser.ProbeMask;
                        if(IsHandleCreated)Invoke((Action)delegate {
                            PrepareNpPreviewForCapture(profile.ApSampleRateHz);
                            neuropixelsPreview.SetPhysicalPortReporting(parser.PhysicalPortsKnown);
                            neuropixelsPreview.PrepareProbeDefinitions(previewDefinitions);
                            llblSavefile.Text=(parser.MergedDat?Path.Combine(runFolder,"neuropixels_ap.dat"):
                                parser.Probes.Count>1?Path.Combine(runFolder,"probe"+parser.Probes[0].Probe,"neuropixels_ap.dat"):
                                Path.Combine(runFolder,"neuropixels_ap.dat"))+"  (+ analogin.dat)";
                        });
                    }
                    modeCaps=admitted;
                    File.WriteAllText(Path.Combine(runFolder,"neuropixels_startup_admission.txt"),
                        "session="+device.StartupAdmission.SessionId+"\r\n"+device.StartupAdmission.Describe()+"\r\n");
                }
                // A fresh-session receipt confirms a new probe-reset epoch.
                // Do this before any queued bytes reach the decoder workers.
                if(profile.SupportsFreshStart)
                    foreach(var probe in parser.Probes)probe.Decoder.DeclareRecordingReset();
                npFreshStartAvailable=profile.SupportsFreshStart;
                if(modeCaps!=null) {
                    if(profile.ApSampleRateHz!=modeCaps.ApHz)
                        throw new IOException("Actual firmware clock rate does not match the selected NP mode.");
                    NpModeCapabilities active=device.GetModeCapabilities();
                    if(active==null || active.ActiveMask!=modeCaps.ActiveMask || active.ApHz!=modeCaps.ApHz || active.WireId!=modeCaps.WireId || active.BatchPackets!=modeCaps.BatchPackets)
                        throw new IOException("NP firmware profile changed at START; capture rejected.");
                }
                if(profile.SupportsFreshStart)PostToUi(delegate {
                    lblRecInfo.Text=modeResolution+"\r\nRecording WILDX session "+profile.SessionId+
                        (profile.PreviousSessionFault!=0?" (new session after prior fault)":"")+": "+
                        parser.Probes.Count+" probe(s), "+profile.ApSampleRateHz+" Hz AP / "+NpCaptureMode.LfpHzText(profile.ApSampleRateHz)+" Hz LFP per probe";
                });
                // Release only after decoder reset declarations, accepted-mode
                // validation, file layout and preview preparation are complete.
                if(captureConfigurations.Count>0)
                    NpConfigurationUsb.ConfirmApplied(device,captureConfigurations,modeCaps.ActiveMask,runFolder);
                if(settings!=null) {
                    foreach(var request in captureConfigurations)if((parser.ProbeMask&(1<<request.Port))!=0)settings.ConfirmRequest(request.Port+1);
                    settings.Actual(profile.ApSampleRateHz,parser.OutputApHz,parser.ProbeMask,parser.MergedDat,
                        parser.Probes.Count>1 && !parser.MergedDat,"validated START/READY profile and accepted probe mask");
                    settings.DeviceEvidence("session_id",profile.SessionId);
                    settings.DeviceEvidence("status_protocol_version",profile.Version);
                    settings.DeviceEvidence("psb_clock_hz",profile.PsbClockHz);
                    settings.DeviceEvidence("mco_divider",profile.McoDivider);
                    settings.Context("mode_resolution",modeResolution);
                    // Save XML/rates while the firmware still holds capture.
                    // Legacy firmware gets metadata only after STOP: never
                    // introduce filesystem work into its active read service.
                    if(modeCaps.SupportsStartupAdmission)settings.Write();
                }
                if(captureConfigurations.Count>0 && IsHandleCreated)
                    Invoke((Action)delegate {neuropixelsPreview.ConfirmProbeDefinitions();});
                foreach(var request in captureConfigurations)
                    foreach(var probe in parser.Probes)if(probe.Probe==request.Port+1)
                        probe.Decoder.DeclareFixedWord((ushort)(request.Selection[4]==2?0x11:0x22));
                device.CompleteStartupAdmission();
                DateTime firstDeadline=DateTime.UtcNow.AddSeconds(60), lastData=DateTime.UtcNow;
                bool receivedAny=false;
                long lastReadEnd=Stopwatch.GetTimestamp();
                long lastCompletion=0;
                while(running) {
                    Exception processing=Volatile.Read(ref workerFailure);
                    if(processing!=null)throw new IOException("NP1 recording worker failed: "+DescribeCaptureFailure(processing),processing);
                    byte[] buffer;
                    if(!free.TryTake(out buffer))throw new IOException("NP1 disk/decoder queue cannot keep up; recording stopped without dropping queued data.");
                    maxReaderGapTicks=Math.Max(maxReaderGapTicks,Stopwatch.GetTimestamp()-lastReadEnd);
                    int bytesRead=reader.ReadInto(buffer);
                    DateTime receiveUtc=DateTime.UtcNow;
                    lastReadEnd=Stopwatch.GetTimestamp();
                    if(bytesRead<=0) {
                        free.Add(buffer);
                        if(!running)break;
                        XeNpStatus status=device.GetStatus();status.ThrowIfFault();
                        if(!receivedAny && DateTime.UtcNow>=firstDeadline)
                            throw new TimeoutException("No qualified NP1 data within 60 seconds; no automatic restart was attempted."+device.StartupDiagnosticSummary());
                        if(receivedAny && DateTime.UtcNow-lastData>TimeSpan.FromSeconds(5))
                            throw new TimeoutException("NP1 stream stopped producing data; recording is incomplete.");
                        continue;
                    }
                    if(!receivedAny) {
                        DateTime firstReceive=receiveUtc;string thisFolder=runFolder;
                        PostToUi(delegate {NpFirstReceive(firstReceive,thisFolder);});
                    }
                    receivedAny=true;lastData=receiveUtc;npLastReceiveUtc=lastData;
                    if(lastCompletion!=0)maxCompletionTicks=Math.Max(maxCompletionTicks,lastReadEnd-lastCompletion);
                    lastCompletion=lastReadEnd;
                    receivedBytes+=bytesRead;readCount++;minReadBytes=Math.Min(minReadBytes,bytesRead);
                    // A buffer taken from the fixed pool guarantees a free
                    // queue slot; this Add cannot wait behind preview/disk.
                    completed.Add(new ReceiveBlock(buffer,bytesRead,receiveUtc));
                    int queuedNow=Interlocked.Increment(ref npHostQueueQueuedBlocks);
                    peakQueuedBlocks=Math.Max(peakQueuedBlocks,queuedNow);
                }
            }
            catch(Exception ex) { failure=DescribeCaptureFailure(ex); }
            finally
            {
                running=false;
                if(device!=null) {
                    // Preserve a V1 exporter fault before a host STOP can
                    // change the legacy status. No control polling during data.
                    if(synchronousV1) {
                        try {
                            XeNpStatus beforeStop=device.GetStatus();
                            firmwareFault=beforeStop.RecordingFault;beforeStop.ThrowIfFault();
                            if(receivedBytes>0 && !beforeStop.Streaming)
                                throw new IOException("V1 stream stopped before host STOP; recording is incomplete.");
                        } catch(Exception ex) { if(failure==null)failure=DescribeCaptureFailure(ex); }
                    }
                    try {
                        device.Stop();
                        XeNpStatus final=device.GetStatus();finalStatusRead=true;
                        if(final.RecordingFault!=0)firmwareFault=final.RecordingFault;
                        final.RequireStopped();
                        stoppedForProbeMetadata=!final.Streaming;
                    } catch(Exception ex) { if(failure==null)failure=DescribeCaptureFailure(ex); }
                }
                if(reader!=null) {
                    try {
                        // Preserve already completed native reads on a normal
                        // stop. Do not jump over an errored/cancelled request.
                        for(int i=0;i<readDepth && Volatile.Read(ref workerFailure)==null;i++) {
                            byte[] tail;
                            if(!free.TryTake(out tail,1000))throw new IOException("NP1 stopped queue did not drain");
                            int count=reader.DrainStoppedInto(tail);
                            if(count<0) { free.Add(tail);break; }
                            if(count==0) { free.Add(tail);continue; }
                            DateTime receiveUtc=DateTime.UtcNow;npLastReceiveUtc=receiveUtc;
                            completed.Add(new ReceiveBlock(tail,count,receiveUtc));receivedBytes+=count;
                        }
                    } catch(Exception ex) { if(failure==null)failure=DescribeCaptureFailure(ex); }
                    reader.Dispose();
                }
                if(completed!=null)completed.CompleteAdding();
                if(worker!=null)worker.Join();
                Exception processing=Volatile.Read(ref workerFailure);
                if(processing!=null && failure==null)failure=DescribeCaptureFailure(processing);
                if(failure==null) {
                    if(parser.Probes.Count==0)failure="No NP recording profile was established.";
                    foreach(var probe in parser.Probes)if(probe.ApRows==0 || probe.LfpRows==0)
                        failure="Probe "+probe.Probe+" produced no complete AP/LFP rows; recording is not qualified.";
                }
                if(device!=null && stoppedForProbeMetadata && parser.Probes.Count>0) {
                    try {
                        foreach(var probe in parser.Probes) {
                            NpProbeManifest manifest=device.GetProbeManifest((byte)(probe.Probe-1));
                            probeMetadataLines.AddRange(manifest.Save(runFolder));
                            if(settings!=null)settings.Receipt(manifest);
                        }
                        probeMetadataStatus="saved applied firmware receipt for "+parser.Probes.Count+" probe(s)";
                    } catch(Exception ex) {
                        // Metadata enhancement failure must never relabel an
                        // already validated sample stream as corrupt.
                        probeMetadataStatus="unavailable: "+ex.Message;
                        probeMetadataLines.Add("firmware_probe_metadata="+probeMetadataStatus);
                    }
                } else if(parser.Probes.Count>0) {
                    probeMetadataStatus="not queried because the firmware did not reach a confirmed stopped state";
                    probeMetadataLines.Add("firmware_probe_metadata="+probeMetadataStatus);
                }
                if(device!=null)device.Dispose();
                if(free!=null)free.Dispose();if(completed!=null)completed.Dispose();
                lock(deviceLock) { activeXeNpDevice=null; }
                // CloseOutputFiles is best-effort for the legacy transports.
                // A recording must report a final flush failure explicitly.
                try {
                    if(parser.Probes.Count>0)parser.Flush();
                } catch(Exception ex) { if(failure==null)failure="Recording flush failed: "+ex.Message; }
                try {parser.Dispose();}catch(Exception ex){if(failure==null)failure="Probe file close failed: "+ex.Message;}
                try {parser.WriteLfpTimes();}catch(Exception ex){failure=(failure==null?"":failure+"; ")+"LFP time generation failed: "+ex.Message;}
                if(sensors!=null)try{sensors.Dispose();}catch(Exception ex){if(failure==null)failure="Sensor recording flush failed: "+ex.Message;}
                if(camera!=null)try{camera.Dispose();}catch(Exception ex){if(failure==null)failure="Camera recording flush failed: "+ex.Message;}
                CloseOutputFiles();
                if(settings!=null)try {
                    settings.Finished(failure==null && finalStatusRead,failure);
                    settings.Streams(npRecordMiscSignals,sensors==null?(bool?)false:sensors.ImuSamples>0,captureCameraEnabled);
                    settings.Context("imu_valid_samples",sensors==null?null:(object)sensors.ImuSamples);
                    settings.Context("probe_metadata_status",probeMetadataStatus);
                    settings.Context("lfp_time_output",parser.LfpTimeOutputs);
                } catch(Exception ex) {
                    probeMetadataLines.Add("recording_settings_error="+ex.Message);
                    probeMetadataStatus+="; setup snapshot unavailable: "+ex.Message;
                }
                try {
                    parser.WriteStatus(runFolder,failure==null && finalStatusRead);
                    if(probeMetadataLines.Count>0)File.AppendAllLines(Path.Combine(runFolder,"neuropixels_probes.txt"),probeMetadataLines);
                    File.WriteAllLines(Path.Combine(runFolder,"neuropixels_capture_status.txt"),new[] {
                        "complete="+(failure==null && finalStatusRead),
                        "failure="+(failure??"none"),
                        "firmware_recording_fault="+firmwareFault,
                        "fresh_start_supported="+(profile!=null && profile.SupportsFreshStart),
                        "firmware_session_id="+(profile==null?0:profile.SessionId),
                        "previous_session_fault="+(profile==null?0:profile.PreviousSessionFault),
                        "firmware_session_reset_count="+(profile==null?0:profile.SessionResetCount),
                        "firmware_probe_metadata="+probeMetadataStatus,
                        "usb_read_depth="+readDepth,
                        "raw_io=False",
                        "usb_read_buffer_bytes="+readBytes,
                        "host_queue_target_seconds="+NpHostQueuePolicy.TargetSeconds,
                        "host_queue_capacity_bytes="+((long)readBytes*bufferCount),
                        "host_queue_capacity_seconds="+hostQueueSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
                        "host_queue_capacity_seconds_basis=NP wire rate only; additional camera traffic reduces the reserve",
                        "usb_start_issued="+usbStartIssued,
                        "usb_read_mode="+(!usbStartIssued?"not started":synchronousV1?"V1 ordered queued":"V2 queued"),
                        "planned_usb_read_mode="+(profile==null?"unknown":synchronousV1?"V1 ordered queued":"V2 queued"),
                        "preview_preparation_ms="+npPreviewPreparationMs.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
                        "preview_open_count="+npPreviewOpenCount,
                        "preview_open_max_ms="+npPreviewOpenMaxMs.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
                        "preview_open_gen2_collections="+npPreviewOpenGen2,
                        "gc_gen0_during_capture="+(usbStartIssued?GC.CollectionCount(0)-captureGc0:0),
                        "gc_gen1_during_capture="+(usbStartIssued?GC.CollectionCount(1)-captureGc1:0),
                        "gc_gen2_during_capture="+(usbStartIssued?GC.CollectionCount(2)-captureGc2:0),
                        "gc_latency_mode="+(captureGcLatencySet?"SustainedLowLatency":"unchanged"),
                        "quad_interleaved="+parser.IsQuadInterleaved,
                        "usb_received_bytes="+receivedBytes,"usb_read_count="+readCount,
                        "usb_smallest_read_bytes="+(readCount==0?0:minReadBytes),
                        "host_peak_queued_blocks="+peakQueuedBlocks,"host_queue_capacity="+bufferCount,
                        "host_max_between_read_calls_ms="+(maxReaderGapTicks*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
                        "host_max_usb_completion_interval_ms="+(maxCompletionTicks*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
                        "host_max_worker_block_ms="+(maxWorkerTicks*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
                        "preview_skipped_rows="+previewDrops,
                        "probe_count="+parser.Probes.Count,
                        "probe_mask="+parser.ProbeMask,
                        "requested_probe_mask="+(requestedMode==null?0:requestedMode.ProbeMask),
                        "requested_ap_sample_rate_hz="+(requestedMode==null?0:requestedMode.RequestedApHz),
                        "mode_selection="+modeResolution,
                        "resolved_probe_mask="+(resolvedMode==null?parser.ProbeMask:resolvedMode.ProbeMask),
                        "resolved_ap_sample_rate_hz="+(resolvedMode==null?(profile==null?0:profile.ApSampleRateHz):resolvedMode.ApHz),
                        "layout_and_fixed_words=see neuropixels_probes.txt (per probe)",
                        "ap_sample_rate_hz="+(profile==null?0:profile.ApSampleRateHz),
                        "lfp_sample_rate_hz="+(profile==null?"0":NpCaptureMode.LfpHzText(profile.ApSampleRateHz)),
                        "lfp_rate_numerator="+(profile==null?0:profile.ApSampleRateHz),
                        "lfp_rate_denominator=12",
                        "output_ap_sample_rate_hz="+parser.OutputApHz,
                        "output_lfp_sample_rate_hz="+parser.OutputLfpHzExact,
                        "output_dat_layout="+(parser.MergedDat?"merged":"individual"),
                        "output_dat_channel_count="+parser.OutputChannelCount,
                        "raw_lane_bin_saved=False",
                        "ap_upsampled_15_to_20khz="+parser.UpsampledAp,
                        "raw_bytes="+Interlocked.Read(ref neuropixelsPayloadBytes),
                        "ap_rows="+Interlocked.Read(ref neuropixelsApRows),
                        "lfp_rows="+Interlocked.Read(ref neuropixelsLfpRows),
                        "pending_quad_lane_packets="+parser.PendingLanePackets,
                        "misc_packets="+parser.MiscPackets,
                        "misc_1250_records="+(sensors==null?0:sensors.Misc1250Records),
                        "misc_1250_mode=800 us device-time grid; zero-order hold of latest sensor values; not 1250 Hz acquisition",
                        "analogin_file=analogin.dat",
                        "analogin_schema=CE64-compatible; headerless 32-byte records of 16 little-endian 16-bit words at 1250 Hz",
                        "analogin_words=1:digin(0 unavailable), 2-4:accel(mg), 5-7:gyro(0.1 dps), 8-10:mag(uT), 11:slow_adc(0x8000 unavailable), 12:imu_sequence_test, 13-14:closed_loop(0 idle), 15-16:host_pc_time_ms",
                        "analogin_pc_time=local milliseconds since midnight; anchored once at first valid IMU observation and advanced on the device-time grid; not hardware synchronized",
                        "raw_misc_bin_saved=False",
                        "imu_samples="+(sensors==null?0:sensors.ImuSamples),
                        "imu_sequence_gaps="+(sensors==null?0:sensors.ImuSequenceGaps),
                        "imu_queue_overflow="+(sensors==null?0:sensors.Overflow),
                        "imu_missed_sample_estimate="+(sensors==null?0:sensors.MissedEstimate),
                        "sensor_valid="+(sensors==null?0:sensors.Valid),
                        "sensor_acquisition=timestamped latest-value polling; not hardware FIFO lossless acquisition",
                        "imu_error="+(sensors==null?0:sensors.ImuError),
                        "environment_error="+(sensors==null?0:sensors.EnvironmentError),
                        "camera_requested="+captureCameraEnabled,
                        "camera_file="+(camera==null?"none":"misc.dat"),
                        "camera_frame_index="+(camera==null?"none":"misc.frames.csv"),
                        "camera_format=CE64 NanEye packed raster; 320 rows of 500 bytes; complete frame 160000 bytes; all received payload bytes preserved",
                        "camera_complete="+(camera!=null && camera.Complete),
                        "camera_packets="+(camera==null?0:camera.Packets),
                        "camera_payload_bytes="+(camera==null?0:camera.PayloadBytes),
                        "camera_frames="+(camera==null?0:camera.Frames),
                        "camera_sequence_gaps="+(camera==null?0:camera.SequenceGaps),
                        "camera_framing_errors="+(camera==null?0:camera.FramingErrors),
                        "camera_pending_frame_bytes="+(camera==null?0:camera.PendingFrameBytes),
                        "camera_acquisition=best effort; camera completeness is independent of neural completeness; preview is lossy",
                        "usb_packets="+parser.Packets,"usb_sequence_gaps="+parser.SequenceGaps,
                        "validated_frames="+parser.FramesChecked,
                        "frame_errors="+parser.FrameErrors,
                        "counter_errors="+parser.CounterErrors,
                        "startup_discarded_bytes="+parser.StartupDiscardedBytes
                    });
                } catch(Exception ex) { if(failure==null)failure="Could not save recording status: "+ex.Message; }
                // Final sidecars must exist before copying/indexing the segment,
                // including a failed segment that will immediately reconnect.
                if(settings!=null)try{settings.Finished(failure==null && finalStatusRead,failure);settings.Write();}
                    catch(Exception ex){if(failure==null)failure="Could not save recording metadata: "+ex.Message;}
                if(captureGcLatencySet)System.Runtime.GCSettings.LatencyMode=previousGcLatency;
                string message=failure==null?"Stopped":"Capture error: "+failure;
                NpImuDisplay finalImu=sensors==null?null:sensors.GetDisplay();
                PostToUi(delegate {
                    if(cameraPreview!=null && !cameraPreview.IsDisposed)cameraPreview.KeepAliveWhileRecording=false;
                    if(finalImu!=null)imuTelemetry.SetNpSample(finalImu,true);FinishCaptureOnUi(message);
                });
            }
        }

        [ThreadStatic] private static byte[] xeNpShortScratch;
        private static void WriteShorts(FileStream stream, short[] samples, int count)
        {
            if (stream == null || count == 0)
                return;
            int size=checked(count*sizeof(short));
            if(xeNpShortScratch==null || xeNpShortScratch.Length<size)xeNpShortScratch=new byte[size];
            Buffer.BlockCopy(samples,0,xeNpShortScratch,0,size);
            stream.Write(xeNpShortScratch,0,size);
        }

        // CE64's MSC stream is intentionally independent from the WinUSB
        // recorder. The data aperture is raw int16 frames, so packet/replay
        // parsing would corrupt it; loss is instead reported by C64S counters.
        private void Ce64MscCaptureLoop()
        {
            string failure = null;
            Ce64MscRawBlockDevice device = null;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            bool ce64SessionArmed = false;
            uint apertureOffset = 0;
            uint sessionSequence = 0;
            ulong lastDropped = 0;
            ulong lastUnderflow = 0;
            ulong lastImuDropped = 0;
            ulong lastImuUnderflow = 0;
            long bytesWritten = 0;
            uint imuApertureOffset = 0;
            Ce64MscCapabilities caps = null;
            Ce64MscCompressedDecoder compressedDecoder = null;
            bool compressedV2 = false;
            ulong compressedLosslessBlocks = 0;
            ulong compressedLossyBlocks = 0;
            uint compressedCurrentShift = 0;
            uint compressedMaxShift = 0;
            ulong compressedFirstFrame = 0;
            try
            {
                Ce64MscDeviceInfo selected = null;
                foreach (Ce64MscDeviceInfo candidate in Ce64MscDiscovery.Enumerate())
                    if (String.Equals(candidate.PhysicalDrivePath, devicePath, StringComparison.OrdinalIgnoreCase))
                    { selected = candidate; break; }
                if (selected == null)
                    throw new IOException("WILD64 MSC device is no longer present.");

                device = Ce64MscRawBlockDevice.Open(selected);
                lock (deviceLock) { activeCe64Device = device; ce64Cancellation = cancellation; }
                if (ce64StopRequested) return;
                device.ValidateManifest();
                caps = device.ReadCapabilities();
                if (!Ce64MscProtocol.IsSupportedRawEphysLayout(caps.MaxChannels, caps.FrameBytes, caps.FramesPerSector))
                    throw new NotSupportedException("The MSC device did not advertise a supported 32/64/96/128-channel int16 ephys layout.");
                uint requestedChannels = caps.MaxChannels;
                uint frameBytes = caps.FrameBytes;
                uint framesPerSector = caps.FramesPerSector;

                // C64S counters intentionally survive DISARM. Reset them only
                // at this idle boundary, before ARM, so this file's loss check
                // cannot inherit a previous failed-stop imbalance.
                device.ClearStatsWhenIdle(TimeSpan.FromSeconds(2), cancellation.Token);

                // Normal USB512 recording deliberately remains the established
                // raw v1 path.  A v2-capable peer still exposes the legacy raw
                // aperture, but its C64Z 10/20 kHz experiments are harness-only
                // until explicitly selected there.  This also keeps amplifier.dat
                // exact unquantized int16 samples with no compression sidecars.
                compressedV2 = false;
                bool hasImu = caps.ImuApertureAvailable;
                uint requestedRateHz = caps.FixedRateHz;
                if (requestedRateHz == 0)
                    throw new NotSupportedException("WILD64 did not advertise a fixed ephys rate.");
                if (!Ce64MscProtocol.IsAllowedEphysRate(requestedRateHz))
                    throw new NotSupportedException("MSC firmware advertised unsupported " + requestedRateHz +
                        " Hz. Supported raw ephys rates are 1.25 kHz multiples through 20 kHz.");
                ulong requiredBytesPerSecond = (ulong)requestedRateHz * frameBytes;
                if (hasImu)
                    requiredBytesPerSecond += (ulong)caps.ImuRateHz * caps.ImuFrameBytes;
                if ((ulong)caps.SafeBytesPerSecond < requiredBytesPerSecond)
                    throw new InvalidOperationException("MSC firmware does not advertise enough safe USB payload for its fixed " +
                        requestedRateHz + " Hz ephys plus " + caps.ImuRateHz + " Hz IMU transport.");
                const byte ce64FormatFlags = 0;
                Ce64MscStatus arm = device.Execute(Ce64MscCommand.Arm, ce64FormatFlags, (byte)requestedChannels, requestedRateHz,
                    TimeSpan.FromSeconds(2), cancellation.Token);
                ce64SessionArmed = true;
                if (arm.RateHz != requestedRateHz || arm.Channels != requestedChannels)
                    throw new InvalidOperationException("MSC device rejected its advertised fixed ephys stream configuration.");
                Ce64MscStatus startedStatus = device.Execute(Ce64MscCommand.Start, ce64FormatFlags, (byte)requestedChannels, requestedRateHz, TimeSpan.FromSeconds(2), cancellation.Token);

                const uint ce64ReadWatermarkSectors = 32;
                sessionSequence = startedStatus.SessionSequence;
                uint negotiatedRateHz = startedStatus.RateHz;
                if (negotiatedRateHz != requestedRateHz || startedStatus.Channels != requestedChannels)
                    throw new IOException("MSC START did not preserve the negotiated ephys configuration.");
                ce64PreviewRateHz = checked((int)negotiatedRateHz);
                ce64PreviewChannels = checked((int)requestedChannels);
                ce64FrameBytes = checked((int)frameBytes);
                ce64FramesPerSector = checked((int)framesPerSector);
                lastDropped = startedStatus.Dropped;
                lastUnderflow = startedStatus.Underflow;
                lastImuDropped = startedStatus.ImuDropped;
                lastImuUnderflow = startedStatus.ImuUnderflow;
                UpdateImuFreshnessFromStatus(startedStatus);
                Stopwatch statusTimer = Stopwatch.StartNew();
                DateTime started = DateTime.UtcNow;
                DateTime previousUiAtUtc = started;
                long previousUiRecordedBytes = 0;
                // C64S statistics may survive a previous session on older
                // firmware. Keep an explicit post-START baseline even though
                // ClearStatsWhenIdle normally makes these values zero.
                ulong startProduced = startedStatus.Produced;
                ulong startConsumed = startedStatus.Consumed;
                ulong startImuProduced = startedStatus.ImuProduced;
                ulong startImuConsumed = startedStatus.ImuConsumed;
                ulong previousUiConsumed = startedStatus.Consumed;
                ulong previousUiImuConsumed = startedStatus.ImuConsumed;
                PostToUi(delegate
                {
                    EnsureCe64ScopeRate(negotiatedRateHz, requestedChannels);
                    lblRecInfo.Text = GetCe64MscDeviceName(requestedChannels) + " MSC: " + requestedChannels + " channels at " + negotiatedRateHz +
                        " Hz. Writing amplifier.dat, analogin.dat, and timestamp.dat.";
                });
                while (running && !ce64StopRequested && !cancellation.IsCancellationRequested)
                {
                    // DATA reads consume the producer ring. Never ask firmware
                    // for more than C64S says is complete, or it deliberately
                    // returns zero-filled underflow sectors.
                    Ce64MscStatus status = device.ReadStatus();
                    UpdateImuFreshnessFromStatus(status);
                    if (status.State == Ce64MscState.Error || status.Error != 0)
                        throw new Ce64MscProtocolException("CE64 reported stream error " + status.Error + ".", status.Error);
                    if (status.State != Ce64MscState.Streaming)
                        throw new IOException("CE64 left Streaming state during capture.");
                    if (status.SessionSequence != sessionSequence)
                        throw new IOException("CE64 MSC session changed during capture; the raw data boundary is not recoverable.");
                    if (status.Dropped > lastDropped)
                        throw new IOException("CE64 dropped " + (status.Dropped - lastDropped) + " acquisition frame(s); stopping before writing a discontinuous raw stream.");
                    if (status.Underflow > lastUnderflow)
                        throw new IOException("CE64 returned " + (status.Underflow - lastUnderflow) + " underflow sector(s); stopping before writing firmware filler.");
                    if (hasImu && status.ImuDropped > lastImuDropped)
                        throw new IOException("WILD64 dropped " + (status.ImuDropped - lastImuDropped) + " IMU record(s); stopping before writing a discontinuous auxiliary stream.");
                    if (hasImu && status.ImuUnderflow > lastImuUnderflow)
                        throw new IOException("WILD64 returned " + (status.ImuUnderflow - lastImuUnderflow) + " IMU underflow sector(s); stopping before writing firmware filler.");

                    if (compressedV2)
                    {
                        Ce64MscCompressedStatus compressed = status.Compressed;
                        if (compressed == null || compressed.ActiveFormat != Ce64MscProtocol.CompressedV2Format ||
                            compressed.FifoOverflowBlocks != 0 || compressed.CodecErrorBlocks != 0 || compressed.SourceOverrunBlocks != 0)
                            throw new IOException("WILD64 compressed FIFO overflow, source overrun, codec error, or format discontinuity.");
                        if (compressed.FifoPendingBytes >= Ce64MscProtocol.SectorBytes)
                        {
                            int sectors = (int)Math.Min(Ce64MscProtocol.CompressedV2RunningMaxSectors,
                                compressed.FifoPendingBytes / Ce64MscProtocol.SectorBytes);
                            byte[] encoded = device.ReadCompressedSectors(sectors, cancellation.Token);
                            compressedDecoder.Append(encoded, encoded.Length);
                            Ce64MscCompressedBlock decoded;
                            while (compressedDecoder.TryDecode(out decoded))
                            {
                                bool negotiatedQ4 = requestedRateHz == Ce64MscProtocol.Wild64CompressedV2ExperimentalRateHz &&
                                    decoded.Mode == 4 && (caps.Compressed.Flags & Ce64MscProtocol.CompressedV2FlagQ4Supported) != 0 &&
                                    caps.Compressed.Q4BlockFrames == Ce64MscProtocol.CompressedV2Q4BlockFrames;
                                int requiredStreamingBlockFrames = negotiatedQ4 ? Ce64MscProtocol.CompressedV2Q4BlockFrames :
                                    Ce64MscProtocol.CompressedV2MaxBlockFrames;
                                if (decoded.Mode == 4 && !negotiatedQ4)
                                    throw new IOException("C64Z Q4 block was received outside its negotiated 20 kHz experimental contract.");
                                if (decoded.SampleCount != requiredStreamingBlockFrames)
                                    throw new IOException("C64Z emitted a short block while streaming; fixed sample cadence was interrupted.");
                                WriteCe64Ephys(decoded.Decoded, decoded.Decoded.Length, ref bytesWritten);
                                WriteCe64CompressionAudit(decoded, compressedFirstFrame);
                                compressedFirstFrame += decoded.SampleCount;
                                if (decoded.IsLossy) compressedLossyBlocks++; else compressedLosslessBlocks++;
                                compressedCurrentShift = decoded.QuantShift;
                                compressedMaxShift = Math.Max(compressedMaxShift, (uint)decoded.QuantShift);
                            }
                        }
                        else
                            cancellation.Token.WaitHandle.WaitOne(2);
                    }
                    else
                    {
                    uint completeSectors = status.PendingFrames / framesPerSector;
                    // Let the corrected firmware ring accumulate a modest
                    // batch before issuing BOT traffic. A 32-sector watermark
                    // (128 frames) avoids repeated one-sector command/data
                    // turnaround while remaining far below its configured
                    // acquisition capacity. Stop leaves a sub-watermark tail
                    // rather than speculatively asking firmware for filler.
                    if (completeSectors >= ce64ReadWatermarkSectors)
                    {
                        int sectors = (int)Math.Min(Math.Min(128U, completeSectors), 0x1000U - apertureOffset);
                        byte[] raw = device.ReadStreamSectors(apertureOffset, sectors, cancellation.Token);
                        apertureOffset += (uint)sectors;
                        if (apertureOffset == 0x1000) apertureOffset = 0;
                        if (!running) break;
                        WriteCe64EphysSectors(raw, sectors, (uint)sectors * framesPerSector,
                            frameBytes, framesPerSector, ref bytesWritten);
                    }
                    else
                    {
                        // A C64S poll is itself a BOT transfer. Polling at a
                        // fixed 2 ms cadence consumed USB-FS time while waiting
                        // for a 25.6 ms, 32-sector data batch. Sleep toward the
                        // known watermark, capped so STOP stays responsive.
                        cancellation.Token.WaitHandle.WaitOne(
                            GetCe64StatusPollDelayMilliseconds(status, negotiatedRateHz, framesPerSector));
                    }
                    }

                    if (hasImu)
                        ReadCe64ImuWhileStreaming(device, caps, status, ref imuApertureOffset, cancellation.Token,
                            ref lastImuDropped, ref lastImuUnderflow);
                    PostLatestImuTelemetry();

                    if (statusTimer.ElapsedMilliseconds >= 500)
                    {
                        DateTime now = DateTime.UtcNow;
                        long recordedBytes = GetRecordedOutputBytes();
                        double intervalSeconds = Math.Max(0.001, (now - previousUiAtUtc).TotalSeconds);
                        UpdateCe64RecordingInfo(status, caps, started, now, intervalSeconds,
                            previousUiRecordedBytes, recordedBytes, startProduced, startConsumed,
                            startImuProduced, startImuConsumed, previousUiConsumed, previousUiImuConsumed,
                            compressedV2, compressedLosslessBlocks, compressedLossyBlocks, compressedCurrentShift, compressedMaxShift);
                        previousUiAtUtc = now;
                        previousUiRecordedBytes = recordedBytes;
                        previousUiConsumed = status.Consumed;
                        previousUiImuConsumed = status.ImuConsumed;
                        statusTimer.Restart();
                    }
                }
            }
            catch (OperationCanceledException) { if (running) failure = "CE64 capture cancelled."; }
            catch (Exception ex) { if (running) failure = DescribeCaptureFailure(ex); }
            finally
            {
                if (device != null)
                {
                    if (ce64SessionArmed)
                    {
                        try
                        {
                            Ce64MscStatus stopped = StopCe64WithRetry(device, 3, TimeSpan.FromSeconds(2));
                            Exception drainFailure = null;
                            try
                            {
                                if (compressedV2)
                                    DrainCe64StoppedCompressedData(device, ref compressedDecoder, sessionSequence,
                                        ref bytesWritten, ref compressedFirstFrame, ref compressedLosslessBlocks,
                                        ref compressedLossyBlocks, ref compressedCurrentShift, ref compressedMaxShift);
                                else
                                    DrainCe64StoppedData(device, stopped, caps, ref apertureOffset, sessionSequence,
                                        lastDropped, lastUnderflow, ref bytesWritten);
                                if (caps != null && caps.ImuApertureAvailable)
                                    DrainCe64StoppedImuData(device, ref imuApertureOffset, sessionSequence, lastImuDropped, lastImuUnderflow);
                            }
                            catch (Exception ex) { drainFailure = ex; }
                            try
                            {
                                device.Execute(Ce64MscCommand.Disarm, 0, 0, 0,
                                    TimeSpan.FromSeconds(2), CancellationToken.None);
                            }
                            catch (Exception disarmFailure)
                            {
                                if (drainFailure == null) drainFailure = disarmFailure;
                            }
                            ce64SessionArmed = false;
                            if (drainFailure != null) throw drainFailure;
                        }
                        catch (Exception cleanupFailure)
                        {
                            if (String.IsNullOrEmpty(failure)) failure = DescribeCaptureFailure(cleanupFailure);
                        }
                    }
                    device.Dispose();
                }
                cancellation.Dispose();
                lock (deviceLock) { activeCe64Device = null; ce64Cancellation = null; }
                running = false;
                CloseOutputFiles();
                string completed = String.IsNullOrEmpty(failure) ? "Stopped CE64 MSC capture" : "CE64 MSC capture error: " + failure;
                PostToUi(delegate { FinishCaptureOnUi(completed); });
            }
        }

        private static int GetCe64StatusPollDelayMilliseconds(Ce64MscStatus status, uint rateHz,
            uint framesPerSector)
        {
            uint watermarkFrames = 32U * framesPerSector;
            if (status == null || rateHz == 0 || status.PendingFrames >= watermarkFrames)
                return 2;

            uint framesUntilWatermark = watermarkFrames - status.PendingFrames;
            double milliseconds = framesUntilWatermark * 1000.0 / rateHz;
            // Retain one millisecond of lead time. Windows timer resolution is
            // variable, so the deliberate 2..10 ms range is not exact timing.
            return Math.Max(2, Math.Min(10, (int)Math.Ceiling(milliseconds - 1.0)));
        }

        // A read-triggered STOP is normally immediate, but a busy Windows BOT
        // stack can delay it. Reissue the harmless alias with status evidence;
        // only return after Armed, so callers can never DISARM a still-running
        // acquisition session.
        private Ce64MscStatus StopCe64WithRetry(Ce64MscRawBlockDevice device, int attempts, TimeSpan timeout)
        {
            Exception lastFailure = null;
            Ce64MscStatus lastStatus = null;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    Ce64MscStatus stopped = device.ExecuteFastStop(timeout, CancellationToken.None);
                    if (stopped.State == Ce64MscState.Armed)
                        return stopped;
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                }
                try
                {
                    lastStatus = device.ReadStatus();
                    if (lastStatus.State == Ce64MscState.Armed && lastStatus.Error == 0)
                        return lastStatus;
                }
                catch (Exception statusFailure)
                {
                    lastFailure = statusFailure;
                }
                if (attempt < attempts)
                    Thread.Sleep(100);
            }
            string statusText = lastStatus == null ? "unavailable" :
                lastStatus.State + "/error=" + lastStatus.Error + "/pending=" + lastStatus.PendingFrames;
            throw new IOException("WILD64 fast STOP did not confirm Armed after " + attempts +
                " attempt(s); final C64S " + statusText + ".", lastFailure);
        }

        // STOP freezes production but the armed MSC aperture remains readable.
        // Drain whole sectors efficiently, then consume a final 1..3-frame
        // padded sector while writing only its valid sample bytes to disk.
        private void DrainCe64StoppedData(Ce64MscRawBlockDevice device, Ce64MscStatus stopped,
            Ce64MscCapabilities caps,
            ref uint apertureOffset, uint expectedSession, ulong expectedDropped, ulong expectedUnderflow,
            ref long bytesWritten)
        {
            if (caps == null || !Ce64MscProtocol.IsSupportedRawEphysLayout(
                caps.MaxChannels, caps.FrameBytes, caps.FramesPerSector))
                throw new InvalidOperationException("MSC stopped-session layout is unavailable.");
            if (stopped.State != Ce64MscState.Armed)
                throw new IOException("CE64 did not enter Armed state for the stopped-session drain.");
            while (true)
            {
                Ce64MscStatus status = device.ReadStatus();
                if (status.State != Ce64MscState.Armed || status.Error != 0 || status.SessionSequence != expectedSession)
                    throw new IOException("CE64 stopped-session status is invalid during drain.");
                if (status.Dropped != expectedDropped || status.Underflow != expectedUnderflow)
                    throw new IOException("CE64 loss counter changed during stopped-session drain.");

                uint validFrames = status.PendingFrames;
                if (validFrames == 0)
                {
                    if (status.Produced != status.Consumed + status.Dropped)
                        throw new IOException("CE64 stopped-session counters do not balance after drain.");
                    return;
                }

                uint sectorsNeeded = (validFrames + caps.FramesPerSector - 1U) / caps.FramesPerSector;
                int sectors = (int)Math.Min(Math.Min(128U, sectorsNeeded), 0x1000U - apertureOffset);
                uint framesThisRead = Math.Min(validFrames, (uint)sectors * caps.FramesPerSector);
                byte[] raw = device.ReadStreamSectors(apertureOffset, sectors, CancellationToken.None);
                WriteCe64EphysSectors(raw, sectors, framesThisRead, caps.FrameBytes,
                    caps.FramesPerSector, ref bytesWritten);
                apertureOffset += (uint)sectors;
                if (apertureOffset == 0x1000) apertureOffset = 0;
            }
        }

        private void DrainCe64StoppedCompressedData(Ce64MscRawBlockDevice device,
            ref Ce64MscCompressedDecoder decoder, uint expectedSession, ref long bytesWritten,
            ref ulong firstFrame, ref ulong losslessBlocks, ref ulong lossyBlocks,
            ref uint currentShift, ref uint maxShift)
        {
            if (decoder == null) throw new InvalidOperationException("C64Z decoder was not initialized.");
            while (true)
            {
                Ce64MscStatus status = device.ReadStatus();
                Ce64MscCompressedStatus compressed = status.Compressed;
                if (status.State != Ce64MscState.Armed || status.Error != 0 || status.SessionSequence != expectedSession ||
                    compressed == null || compressed.ActiveFormat != Ce64MscProtocol.CompressedV2Format ||
                    compressed.FifoOverflowBlocks != 0 || compressed.CodecErrorBlocks != 0 || compressed.SourceOverrunBlocks != 0)
                    throw new IOException("WILD64 stopped compressed session is invalid or discontinuous.");
                uint pendingBytes = compressed.FifoPendingBytes;
                if (pendingBytes == 0)
                {
                    decoder.RequireDrained();
                    if (decoder.DecodedFrames != status.Produced)
                        throw new IOException("C64Z decoded frame count does not match firmware production count.");
                    if (compressed.NextBlockSequence != losslessBlocks + lossyBlocks)
                        throw new IOException("C64Z decoded block count does not match firmware block production.");
                    return;
                }
                int sectors = (int)Math.Min(128U, (pendingBytes + Ce64MscProtocol.SectorBytes - 1U) / Ce64MscProtocol.SectorBytes);
                byte[] encoded = device.ReadCompressedSectors(sectors, CancellationToken.None);
                decoder.Append(encoded, checked((int)Math.Min((uint)encoded.Length, pendingBytes)));
                Ce64MscCompressedBlock decoded;
                while (decoder.TryDecode(out decoded))
                {
                    WriteCe64Ephys(decoded.Decoded, decoded.Decoded.Length, ref bytesWritten);
                    WriteCe64CompressionAudit(decoded, firstFrame);
                    firstFrame += decoded.SampleCount;
                    if (decoded.IsLossy) lossyBlocks++; else losslessBlocks++;
                    currentShift = decoded.QuantShift;
                    maxShift = Math.Max(maxShift, (uint)decoded.QuantShift);
                }
            }
        }

        // MSC DATA is already chronological [frame][channel] int16 LE. Copying
        // it to the display queue is intentionally separate from the exact raw
        // file write so preview cannot affect recording bytes or channel phase.
        private void WriteCe64Ephys(byte[] raw, int validBytes, ref long bytesWritten)
        {
            int frameBytes = ce64FrameBytes;
            int channels = ce64PreviewChannels;
            if (raw == null || validBytes <= 0 || validBytes > raw.Length || frameBytes <= 0 ||
                validBytes % frameBytes != 0)
                throw new InvalidDataException("MSC ephys write is not frame-aligned.");
            amplifierFile.Write(raw, 0, validBytes);
            bytesWritten += validBytes;
            Interlocked.Add(ref amplifierPayloadBytes, validBytes);
            Interlocked.Add(ref totalPayloadBytes, validBytes);

            ScopeForm scope = scopeForm;
            if (scope != null && !scope.IsDisposed && scope.MatchesStream(channels, ce64PreviewRateHz))
            {
                short[] preview = new short[validBytes / sizeof(short)];
                Buffer.BlockCopy(raw, 0, preview, 0, validBytes);
                scope.PushData(preview);
            }
        }

        // Every C64M DATA LBA is 512 bytes.  96-channel CE128 frames occupy
        // 384 bytes for two frames, leaving a 128-byte zero tail; compact that
        // tail before persisting amplifier.dat so its rows remain [frame][ch].
        private void WriteCe64EphysSectors(byte[] raw, int sectors, uint validFrames,
            uint frameBytes, uint framesPerSector, ref long bytesWritten)
        {
            if (raw == null || sectors < 1 || raw.Length < sectors * Ce64MscProtocol.SectorBytes ||
                frameBytes == 0 || framesPerSector == 0 || validFrames == 0 ||
                validFrames > (uint)sectors * framesPerSector)
                throw new InvalidDataException("MSC sector payload is malformed.");

            int packedBytesPerSector = checked((int)(frameBytes * framesPerSector));
            int validBytes = checked((int)(validFrames * frameBytes));
            if (packedBytesPerSector == Ce64MscProtocol.SectorBytes && validBytes == sectors * Ce64MscProtocol.SectorBytes)
            {
                WriteCe64Ephys(raw, validBytes, ref bytesWritten);
                return;
            }

            byte[] packed = new byte[validBytes];
            uint remainingFrames = validFrames;
            int destination = 0;
            for (int sector = 0; sector < sectors && remainingFrames != 0; sector++)
            {
                uint frames = Math.Min(framesPerSector, remainingFrames);
                int bytes = checked((int)(frames * frameBytes));
                Buffer.BlockCopy(raw, sector * Ce64MscProtocol.SectorBytes, packed, destination, bytes);
                destination += bytes;
                remainingFrames -= frames;
            }
            WriteCe64Ephys(packed, packed.Length, ref bytesWritten);
        }

        private void WriteCe64CompressionAudit(Ce64MscCompressedBlock block, ulong firstFrame)
        {
            StreamWriter audit = ce64CompressionCsv;
            if (audit == null) return;
            string modeName = block.Mode == 0 ? "rice_lossless" :
                block.Mode == 1 ? "rice_quantized" : block.Mode == 2 ? "q8_absolute_quantized" :
                block.Mode == 3 ? "q7_absolute_quantized" : "q4_absolute_quantized";
            audit.WriteLine(block.Sequence.ToString() + "," + firstFrame.ToString() + "," + block.SampleCount.ToString() + "," +
                block.Mode.ToString() + "," + modeName + "," + block.QuantShift.ToString() + "," + block.PayloadBytes.ToString() + "," +
                block.Decoded.Length.ToString() + "," + (!block.IsLossy ? "1" : "0"));
        }

        private void WriteCe64CompressionMetadata(Ce64MscCapabilities caps, uint rateHz)
        {
            if (String.IsNullOrEmpty(runFolder) || caps == null || caps.Compressed == null) return;
            string path = Path.Combine(runFolder, "compression_metadata.txt");
            using (StreamWriter metadata = new StreamWriter(path, false))
            {
                metadata.WriteLine("protocol=C64Z v2");
                metadata.WriteLine("fixed_rate_hz=" + rateHz.ToString());
                metadata.WriteLine("channels=" + Wild64Channels.ToString());
                metadata.WriteLine("block_frames=" + caps.Compressed.BlockFrames.ToString());
                metadata.WriteLine("sampling_contract=exact fixed 10000 Hz, every frame, all 64 channels; compression never decimates, changes rate, or drops channels");
                metadata.WriteLine("lossy_content_semantics=only int16 precision may be quantized; amplifier.dat and preview contain the exact reconstructed quantized int16 samples");
                metadata.WriteLine("mode_0=rice_lossless; quant_shift=0");
                metadata.WriteLine("mode_1=rice_quantized; quant_shift=1..7; low bits are cleared");
                metadata.WriteLine("mode_2=q8_absolute_quantized; quant_shift=8; low eight bits are cleared");
                metadata.WriteLine("mode_3=q7_absolute_quantized; quant_shift=9; packed signed 7-bit samples, low nine bits are cleared");
                metadata.WriteLine("mode_4=q4_absolute_quantized; experimental 20000 Hz only; quant_shift=12; packed signed 4-bit samples, low twelve bits are cleared");
                metadata.WriteLine("lossless_lossy_totals=derive by summing compression.csv lossless column");
                metadata.WriteLine("max_quant_shift=derive from compression.csv quant_shift maximum");
            }
        }

        private void ReadCe64ImuWhileStreaming(Ce64MscRawBlockDevice device, Ce64MscCapabilities caps, Ce64MscStatus status,
            ref uint apertureOffset, CancellationToken cancel, ref ulong expectedDropped, ref ulong expectedUnderflow)
        {
            const uint recordsPerSector = Ce64MscProtocol.Wild64ImuFramesPerSector;
            uint readWatermarkSectors = GetCe64ImuReadWatermarkSectors(caps);
            uint completeSectors = status.ImuPendingRecords / recordsPerSector;
            if (completeSectors < readWatermarkSectors)
                return;
            int sectors = (int)Math.Min(Math.Min(128U, completeSectors), 0x0800U - apertureOffset);
            byte[] raw = device.ReadImuSectors(apertureOffset, sectors, cancel);
            WriteCe64Imu(raw, raw.Length);
            apertureOffset += (uint)sectors;
            if (apertureOffset == 0x0800) apertureOffset = 0;
            // The next C64S poll validates these counters before another read.
            expectedDropped = status.ImuDropped;
            expectedUnderflow = status.ImuUnderflow;
        }

        // Target a roughly 80--102 ms native-IMU batch.  Rounding to the
        // nearest sector keeps a 200 Hz completion stream at one 16-record
        // sector (80 ms) while retaining eight sectors at the legacy 1.25 kHz
        // rate (102.4 ms). This avoids a fixed 128-record delay making the
        // real-time panel appear stale on a deliberately lower-rate IMU.
        private static uint GetCe64ImuReadWatermarkSectors(Ce64MscCapabilities caps)
        {
            if (caps == null || caps.ImuRateHz == 0 || caps.ImuFramesPerSector == 0)
                return 1;
            double targetSectors = caps.ImuRateHz * 0.100 / caps.ImuFramesPerSector;
            return (uint)Math.Max(1, Math.Min(128,
                (int)Math.Round(targetSectors, MidpointRounding.AwayFromZero)));
        }

        // STOP leaves both apertures readable in Armed. Unlike ephys's running
        // watermark, drain every residual MGR_misc record and omit only padded
        // bytes in its final partial sector.
        private void DrainCe64StoppedImuData(Ce64MscRawBlockDevice device, ref uint apertureOffset, uint expectedSession,
            ulong expectedDropped, ulong expectedUnderflow)
        {
            while (true)
            {
                Ce64MscStatus status = device.ReadStatus();
                if (status.State != Ce64MscState.Armed || status.Error != 0 || status.SessionSequence != expectedSession)
                    throw new IOException("WILD64 stopped-session IMU status is invalid during drain.");
                if (status.ImuDropped != expectedDropped || status.ImuUnderflow != expectedUnderflow)
                    throw new IOException("WILD64 IMU loss counter changed during stopped-session drain.");
                uint validRecords = status.ImuPendingRecords;
                if (validRecords == 0)
                {
                    if (status.ImuProduced != status.ImuConsumed + status.ImuDropped)
                        throw new IOException("WILD64 stopped-session IMU counters do not balance after drain.");
                    return;
                }
                uint sectorsNeeded = (validRecords + Ce64MscProtocol.Wild64ImuFramesPerSector - 1) /
                    Ce64MscProtocol.Wild64ImuFramesPerSector;
                int sectors = (int)Math.Min(Math.Min(128U, sectorsNeeded), 0x0800U - apertureOffset);
                uint recordsThisRead = Math.Min(validRecords,
                    (uint)sectors * Ce64MscProtocol.Wild64ImuFramesPerSector);
                byte[] raw = device.ReadImuSectors(apertureOffset, sectors, CancellationToken.None);
                WriteCe64Imu(raw, checked((int)(recordsThisRead * Ce64MscProtocol.Wild64ImuFrameBytes)));
                apertureOffset += (uint)sectors;
                if (apertureOffset == 0x0800) apertureOffset = 0;
            }
        }

        private void WriteCe64Imu(byte[] raw, int validBytes)
        {
            if (validBytes <= 0 || validBytes % AnalogInSampleBytes != 0)
                throw new InvalidDataException("WILD64 IMU write is not 32-byte record-aligned.");
            WritePayload(analogInFile, raw, 0, validBytes);
            WriteAnalogInTimestamps(raw, 0, validBytes);
            CaptureLatestImuSample(raw, 0, validBytes);
            // CE64 MSC does not enter the normal WinUSB logger loop, whose
            // periodic UI statistics call normally publishes this sample.
            // Publish the just-captured native record independently; this is
            // display-only and cannot affect the raw file write above.
            PostLatestImuTelemetry();
            int records = validBytes / AnalogInSampleBytes;
            Interlocked.Add(ref analogInPayloadBytes, validBytes);
            Interlocked.Add(ref totalPayloadBytes, validBytes);
            Interlocked.Add(ref analogInPackets, records);
        }

        private void CaptureLoop()
        {
            string failure = null;
            USB512WinUsbDevice device = null;
            bool reconnecting = false;
            int reconnectFailures = 0;
            try
            {
                StartLogger();
                while (running)
                {
                    USB512WinUsbQueuedReader reader = null;
                    byte[] pendingCaptureBuffer = null;
                    bool abandonDeviceForRecovery = false;
                    try
                    {
                        device = OpenCaptureDevice();
                        lock (deviceLock)
                        {
                            activeDevice = device;
                        }
                        bool resumedExistingStream = reconnecting &&
                            device.TryGetActiveStreams(out activeStreams);
                        reader = device.CreateQueuedReader(ReceiveBufferBytes, UsbReadDepth);
                        if (resumedExistingStream)
                        {
                            // The new handle has no knowledge of which reads
                            // Windows discarded. Abort its fresh requests and
                            // ask firmware for the chronological retained tail
                            // before accepting any data into the logger.
                            reader.RecoverForSequenceGap();
                        }
                        else
                        {
                            // Arm reads before producers: the ephys ring holds
                            // only about 49 ms of raw data.
                            RecordClockSync(device.SyncTimeOfDay());
                            activeStreams = device.StartStreams(
                                requestedStreams,
                                requestedCameraProfile,
                                requestedCameraRateHz);
                        }
                        captureCameraEnabled = (activeStreams & USB512WinUsbDevice.StreamMaskCamera) != 0;
                        if (reconnecting && !resumedExistingStream)
                        {
                            integrityAssemblyLength = 0;
                            integrityPreviousSequences.Clear();
                            integrityReplayPendingStreams.Clear();
                            integrityReplayInFlight = false;
                            completedReceiveBlocks.Add(ReceiveBlock.CreateStreamRestartMarker());
                        }
                        PostToUi(delegate
                        {
                            lblRecInfo.Text = (resumedExistingStream ? "Recovered WinUSB with retained replay; recording " :
                                (reconnecting ? "Recovered WinUSB; recording " : "Recording ")) +
                                DescribeActiveStreams();
                        });
                        reconnecting = false;
                        reconnectFailures = 0;
                        Stopwatch keepAliveTimer = Stopwatch.StartNew();

                        while (running)
                        {
                            if (keepAliveTimer.ElapsedMilliseconds >= 1000)
                            {
                                device.KeepDeviceAwake();
                                keepAliveTimer.Restart();
                            }
                            if (!receiveBufferPool.TryTake(out pendingCaptureBuffer))
                                throw new IOException("USB512 host RAM backlog is full; disk/decoder throughput is below the incoming rate. Recording is incomplete.");
                            int bytesRead;
                            try
                            {
                                bytesRead = reader.ReadInto(pendingCaptureBuffer);
                            }
                            catch (Exception readFailure)
                            {
                                if (!IsRecoverableTransportFailure(readFailure))
                                    throw;

                                // ReadInto has already frozen the retained
                                // device tail.  Keep this handle alive and
                                // complete the tested prepare/reset/replay
                                // sequence before considering a slow reopen.
                                // Reopening first can age out the finite
                                // chronological history on a USB hub.
                                reader.PrepareForReopen();
                                try
                                {
                                    reader.RecoverForSequenceGap();
                                }
                                catch (Exception firstRecoveryFailure)
                                {
                                    // A hub may acknowledge the control
                                    // request before its old bulk URB has
                                    // completely retired.  Give that teardown
                                    // one short turn, then retry the existing
                                    // prepared replay before abandoning this
                                    // still-open device handle.
                                    Thread.Sleep(2);
                                    try
                                    {
                                        reader.RecoverForSequenceGap();
                                    }
                                    catch
                                    {
                                        throw firstRecoveryFailure;
                                    }
                                }
                                Interlocked.Exchange(ref transportRecoveries, reader.RecoveryCount);
                                receiveBufferPool.Add(pendingCaptureBuffer);
                                pendingCaptureBuffer = null;
                                PostToUi(delegate
                                {
                                    lblRecInfo.Text = "WinUSB pipe recovered in place; recording " +
                                        DescribeActiveStreams();
                                });
                                continue;
                            }
                            Interlocked.Exchange(ref transportRecoveries, reader.RecoveryCount);
                            if (bytesRead <= 0)
                            {
                                receiveBufferPool.Add(pendingCaptureBuffer);
                                pendingCaptureBuffer = null;
                                continue;
                            }

                            int sequenceValidation =
                                ValidateReceivedSequences(pendingCaptureBuffer, bytesRead);
                            if (sequenceValidation != 0)
                            {
                                integrityAssemblyLength = 0;
                                if (sequenceValidation > 0)
                                {
                                    reader.RecoverForSequenceGap();
                                    Interlocked.Exchange(ref transportRecoveries, reader.RecoveryCount);
                                }
                                receiveBufferPool.Add(pendingCaptureBuffer);
                                pendingCaptureBuffer = null;
                                continue;
                            }

                            ReceiveBlock completed = new ReceiveBlock(pendingCaptureBuffer, bytesRead);
                            // Preserve a read already accepted when Stop arrives.
                            // The capture owner's final drain closes the files.
                            while (!completedReceiveBlocks.TryAdd(completed, 50))
                            {
                                string writerError = Volatile.Read(ref loggerFailure);
                                if (writerError != null) throw new IOException("USB512 logger failed: " + writerError);
                            }
                            pendingCaptureBuffer = null;
                            UpdateReceiveQueuePeak(completedReceiveBlocks.Count);
                            if (!running)
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (pendingCaptureBuffer != null)
                        {
                            receiveBufferPool.Add(pendingCaptureBuffer);
                            pendingCaptureBuffer = null;
                        }
                        if (!running || !IsRecoverableTransportFailure(ex))
                        {
                            failure = DescribeCaptureFailure(ex);
                            break;
                        }

                        // Preserve the chronological device tail before this
                        // poisoned WinUSB handle is disposed. The subsequent
                        // handle starts that frozen replay, rather than letting
                        // firmware advance while Windows closes the old pipe.
                        if (reader != null)
                        {
                            try { reader.PrepareForReopen(); }
                            catch { }
                        }
                        reconnecting = true;
                        abandonDeviceForRecovery = true;
                        int retryDelay = (reconnectFailures < 3) ? 0 :
                            Math.Min(100, 5 << Math.Min(reconnectFailures - 3, 4));
                        reconnectFailures++;
                        PostToUi(delegate
                        {
                            lblRecInfo.Text = "WinUSB pipe recovery: reopening device…";
                        });
                        if (retryDelay != 0)
                            Thread.Sleep(retryDelay);
                    }
                    finally
                    {
                        if (reader != null)
                        {
                            if (abandonDeviceForRecovery)
                                reader.AbandonForRecovery();
                            else
                                reader.Dispose();
                        }
                        if (device != null)
                        {
                            if (abandonDeviceForRecovery)
                                device.DisposeForRecovery(reader);
                            else
                                device.Dispose();
                            device = null;
                        }
                        lock (deviceLock)
                        {
                            activeDevice = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failure = DescribeCaptureFailure(ex);
            }
            finally
            {
                if (completedReceiveBlocks != null)
                    completedReceiveBlocks.CompleteAdding();
                if (loggerThread != null && loggerThread != Thread.CurrentThread)
                    loggerThread.Join();
                loggerThread = null;
                if (String.IsNullOrEmpty(failure) && !String.IsNullOrEmpty(loggerFailure))
                    failure = loggerFailure;
                lock (deviceLock)
                {
                    activeDevice = null;
                }
                running = false;
                StopGoodChannelCounter();
                FlushAmplifierDecoder();
                CloseOutputFiles();
                if (receiveBufferPool != null) { receiveBufferPool.Dispose(); receiveBufferPool = null; }
                if (completedReceiveBlocks != null) { completedReceiveBlocks.Dispose(); completedReceiveBlocks = null; }

                string completedMessage = String.IsNullOrEmpty(failure) ? "Stopped" : "Capture error: " + failure;
                completedMessage += "; permanent packet loss: " + sequenceGaps.ToString("N0") + " packet(s)";
                PostToUi(delegate { FinishCaptureOnUi(completedMessage); });
            }
        }

        private void StartLogger()
        {
            receiveBufferPool = new BlockingCollection<byte[]>(ReceiveQueueDepth);
            completedReceiveBlocks = new BlockingCollection<ReceiveBlock>(ReceiveQueueDepth);
            receiveQueuePeak = 0;
            loggerFailure = null;
            for (int i = 0; i < ReceiveQueueDepth; i++)
                receiveBufferPool.Add(new byte[ReceiveBufferBytes]);

            loggerThread = new Thread(LoggerLoop);
            loggerThread.IsBackground = true;
            loggerThread.Name = "USB512 stream logger";
            loggerThread.Priority = ThreadPriority.AboveNormal;
            loggerThread.Start();
        }

        private void LoggerLoop()
        {
            try
            {
                foreach (ReceiveBlock completed in completedReceiveBlocks.GetConsumingEnumerable())
                {
                    if (completed.IsStreamRestartMarker)
                    {
                        ResetLoggerTransportState();
                        continue;
                    }
                    ProcessUsbRead(completed.Buffer, completed.Count);
                    receiveBufferPool.Add(completed.Buffer);
                    UpdateUiStatistics();
                }
            }
            catch (Exception ex)
            {
                loggerFailure = DescribeCaptureFailure(ex);
                running = false;
            }
        }

        // A persistent WinUSB pipe failure can require reopening the interface.
        // START_STREAMS then begins a fresh device sequence epoch; reset only
        // framing/identity state on the logger thread while keeping the same
        // output files and aggregate recording counters alive.
        private void ResetLoggerTransportState()
        {
            previousSequences.Clear();
            packetAssemblyLength = 0;
            streamCameraFrameLength = 0;
            ResetAmplifierDecoder();
        }

        private void UpdateReceiveQueuePeak(int depth)
        {
            int current;
            do
            {
                current = receiveQueuePeak;
                if (depth <= current)
                    return;
            }
            while (Interlocked.CompareExchange(ref receiveQueuePeak, depth, current) != current);
        }

        // Decode the compact stream as a continuous byte stream rather than
        // assuming every WinUSB ReadPipe completion is transfer-aligned.
        private void ProcessUsbRead(byte[] data, int bytesRead)
        {
            int offset = 0;
            while (offset < bytesRead)
            {
                if (packetAssemblyLength == 0 && bytesRead - offset >= PacketHeaderBytes)
                {
                    int transferLength = GetExpectedTransferLength(data, offset);
                    if (transferLength < PacketHeaderBytes || transferLength > FullTransferBytes)
                        return;
                    if (bytesRead - offset >= transferLength)
                    {
                        ProcessTransfer(data, offset, transferLength);
                        offset += transferLength;
                        continue;
                    }
                }

                int neededForHeader = PacketHeaderBytes - packetAssemblyLength;
                if (neededForHeader > 0)
                {
                    int copy = Math.Min(neededForHeader, bytesRead - offset);
                    Buffer.BlockCopy(data, offset, packetAssembly, packetAssemblyLength, copy);
                    packetAssemblyLength += copy;
                    offset += copy;
                    if (packetAssemblyLength < PacketHeaderBytes)
                        return;
                }

                int expectedLength = GetExpectedTransferLength(packetAssembly, 0);
                if (expectedLength < PacketHeaderBytes || expectedLength > FullTransferBytes)
                {
                    packetAssemblyLength = 0;
                    return;
                }
                int packetCopy = Math.Min(expectedLength - packetAssemblyLength, bytesRead - offset);
                Buffer.BlockCopy(data, offset, packetAssembly, packetAssemblyLength, packetCopy);
                packetAssemblyLength += packetCopy;
                offset += packetCopy;
                if (packetAssemblyLength < expectedLength)
                    return;

                ProcessTransfer(packetAssembly, 0, expectedLength);
                packetAssemblyLength = 0;
            }
        }

        private int GetExpectedTransferLength(byte[] data, int offset)
        {
            if ((ushort)(data[offset] | (data[offset + 1] << 8)) != PacketMagic)
                return 0;

            byte streamId = data[offset + 2];
            byte flags = data[offset + 3];
            ushort sequence = (ushort)(data[offset + 4] | (data[offset + 5] << 8));
            int payloadLength;
            if ((flags & USB512WinUsbDevice.FlagPayloadLengthInSequence) != 0)
            {
                payloadLength = sequence;
            }
            else if (streamId == USB512WinUsbDevice.StreamImu)
            {
                payloadLength = AnalogInRecordsPerPacket * AnalogInSampleBytes;
            }
            else if (streamId == USB512WinUsbDevice.StreamCamera &&
                      (flags & USB512WinUsbDevice.FlagFrameEnd) != 0)
            {
                int finalPayload = CameraFrameBytes % FullPacketPayloadBytes;
                payloadLength = finalPayload == 0 ? FullPacketPayloadBytes : finalPayload;
            }
            else
            {
                payloadLength = FullPacketPayloadBytes;
            }
            return PacketHeaderBytes + payloadLength;
        }

        // Zero means this block is safe to commit.  Positive requests retained
        // replay; negative discards later packets while that replay is active.
        private int ValidateReceivedSequences(byte[] data, int bytesRead)
        {
            int offset = 0;
            while (offset < bytesRead)
            {
                if (integrityAssemblyLength == 0 && bytesRead - offset >= PacketHeaderBytes)
                {
                    int transferLength = GetExpectedTransferLength(data, offset);
                    if (transferLength < PacketHeaderBytes || transferLength > FullTransferBytes)
                        return 1;
                    if (bytesRead - offset >= transferLength)
                    {
                        int validation = ValidateReceivedTransfer(data, offset);
                        if (validation != 0)
                            return validation;
                        offset += transferLength;
                        continue;
                    }
                }

                int neededForHeader = PacketHeaderBytes - integrityAssemblyLength;
                if (neededForHeader > 0)
                {
                    int copy = Math.Min(neededForHeader, bytesRead - offset);
                    Buffer.BlockCopy(data, offset, integrityAssembly,
                        integrityAssemblyLength, copy);
                    integrityAssemblyLength += copy;
                    offset += copy;
                    if (integrityAssemblyLength < PacketHeaderBytes)
                        return 0;
                }

                int expectedLength = GetExpectedTransferLength(integrityAssembly, 0);
                if (expectedLength < PacketHeaderBytes || expectedLength > FullTransferBytes)
                    return 1;

                int packetCopy = Math.Min(expectedLength - integrityAssemblyLength,
                    bytesRead - offset);
                Buffer.BlockCopy(data, offset, integrityAssembly,
                    integrityAssemblyLength, packetCopy);
                integrityAssemblyLength += packetCopy;
                offset += packetCopy;
                if (integrityAssemblyLength < expectedLength)
                    return 0;

                int assembledValidation = ValidateReceivedTransfer(integrityAssembly, 0);
                if (assembledValidation != 0)
                    return assembledValidation;
                integrityAssemblyLength = 0;
            }
            return 0;
        }

        private int ValidateReceivedTransfer(byte[] data, int offset)
        {
            if ((ushort)(data[offset] | (data[offset + 1] << 8)) != PacketMagic)
                return 1;

            byte streamId = data[offset + 2];
            byte flags = data[offset + 3];
            if ((flags & USB512WinUsbDevice.FlagPayloadLengthInSequence) != 0)
                return 0;

            ushort sequence = (ushort)(data[offset + 4] | (data[offset + 5] << 8));
            ushort previous;
            if (integrityPreviousSequences.TryGetValue(streamId, out previous))
            {
                ushort delta = (ushort)(sequence - previous);
                if (delta != 0 && delta <= 0x8000 && delta != 1)
                {
                    integrityReplayPendingStreams.Add(streamId);
                    if (!integrityReplayInFlight)
                    {
                        integrityReplayInFlight = true;
                        return 1;
                    }
                    return -1;
                }
                if (delta == 0 || delta > 0x8000)
                    return 0;
            }
            integrityPreviousSequences[streamId] = sequence;
            integrityReplayPendingStreams.Remove(streamId);
            if (integrityReplayPendingStreams.Count == 0)
                integrityReplayInFlight = false;
            return 0;
        }

        private void ProcessTransfer(byte[] data, int offset, int transferLength)
        {
            if (transferLength < PacketHeaderBytes)
                return;

            ushort magic = (ushort)(data[offset] | (data[offset + 1] << 8));
            if (magic != PacketMagic)
                return;

            byte streamId = data[offset + 2];
            byte flags = data[offset + 3];
            ushort sequence = (ushort)(data[offset + 4] | (data[offset + 5] << 8));
            int payloadOffset = offset + PacketHeaderBytes;
            int payloadLength = transferLength - PacketHeaderBytes;
            int missingPackets = 0;
            if ((flags & USB512WinUsbDevice.FlagPayloadLengthInSequence) == 0)
                missingPackets = TrackSequence(streamId, sequence);
            // A recovered USB transfer can repeat a prefix which reached the
            // logger before WinUSB reported its pipe reset.  The per-stream
            // sequence is the authoritative identity: never write a repeated
            // payload and never interpret it as a 65k-packet forward gap.
            if (missingPackets < 0)
                return;

            switch (streamId)
            {
                case USB512WinUsbDevice.StreamEphys:
                    if (missingPackets != 0)
                        InsertAmplifierGap(missingPackets);
                    ProcessAmplifierPayload(data, payloadOffset, payloadLength);
                    amplifierPackets++;
                    amplifierPayloadBytes += payloadLength;
                    break;

                case USB512WinUsbDevice.StreamImu:
                    WritePayload(analogInFile, data, payloadOffset, payloadLength);
                    WriteAnalogInTimestamps(data, payloadOffset, payloadLength);
                    CaptureLatestImuSample(data, payloadOffset, payloadLength);
                    analogInPackets++;
                    analogInPayloadBytes += payloadLength;
                    break;

                case USB512WinUsbDevice.StreamCamera:
                    if ((flags & USB512WinUsbDevice.FlagFrameStart) != 0)
                    {
                        streamCameraFrameLength = 0;
                    }
                    WritePayload(cameraFile, data, payloadOffset, payloadLength);
                    cameraPackets++;
                    cameraPayloadBytes += payloadLength;
                    streamCameraFrameLength += payloadLength;
                    if ((flags & USB512WinUsbDevice.FlagFrameEnd) != 0)
                    {
                        cameraFrames++;
                        streamCameraFrameLength = 0;
                    }
                    AssemblePreviewFrame(data, payloadOffset, payloadLength, flags);
                    break;

                case USB512WinUsbDevice.StreamMicrophone:
                    WritePayload(adcFile, data, payloadOffset, payloadLength);
                    adcPackets++;
                    adcPayloadBytes += payloadLength;
                    break;

                case USB512WinUsbDevice.StreamMisc:
                    WritePayload(metadataFile, data, payloadOffset, payloadLength);
                    metadataPackets++;
                    break;

                case USB512WinUsbDevice.StreamNeuropixels:
                    WritePayload(neuropixelsFile, data, payloadOffset, payloadLength);
                    neuropixelsPackets++;
                    neuropixelsPayloadBytes += payloadLength;
                    break;
            }

            totalPayloadBytes += payloadLength;
        }

        private void ProcessAmplifierPayload(byte[] data, int offset, int length)
        {
            if (amplifierPrimingBytesRemaining > 0)
            {
                int discard = Math.Min(amplifierPrimingBytesRemaining, length);
                amplifierPrimingBytesRemaining -= discard;
                offset += discard;
                length -= discard;
            }

            while (length > 0)
            {
                int copy = Math.Min(amplifierAssembly.Length - amplifierAssemblyLength, length);
                Buffer.BlockCopy(data, offset, amplifierAssembly, amplifierAssemblyLength, copy);
                amplifierAssemblyLength += copy;
                offset += copy;
                length -= copy;

                if (amplifierAssemblyLength == amplifierAssembly.Length)
                {
                    DecodeAndPublishAmplifier(amplifierAssembly, amplifierAssemblyLength);
                    amplifierAssemblyLength = 0;
                }
            }
        }

        private void ResetAmplifierDecoder()
        {
            amplifierAssemblyLength = 0;
            amplifierPrimingBytesRemaining = IntanPipelinePrimingBytes;
            amplifierHasPendingBlock = false;
            amplifierFrameBlockCount = 0;
            Array.Clear(amplifierPendingLanes, 0, amplifierPendingLanes.Length);
            Array.Clear(amplifierScratchLanes, 0, amplifierScratchLanes.Length);
            Array.Clear(amplifierFrame, 0, amplifierFrame.Length);
        }

        private void InsertAmplifierGap(int missingPackets)
        {
            // Packet headers and their per-stream sequence make the exact
            // missing length known: each normal ephys packet carries 506 raw
            // bytes.  Feed matching zero bytes through the same assembler so
            // every later channel and sample keeps its original time position.
            // This is intentionally a visible data dropout, never a phase shift
            // or a hidden compression of the recording timeline.
            for (int packet = 0; packet < missingPackets; packet++)
                ProcessAmplifierPayload(amplifierGapPayload, 0, amplifierGapPayload.Length);
        }

        private void FlushAmplifierDecoder()
        {
            int completeBytes = amplifierAssemblyLength -
                (amplifierAssemblyLength % IntanAcquisitionBlockBytes);
            if (completeBytes > 0)
                DecodeAndPublishAmplifier(amplifierAssembly, completeBytes);

            // The final raw block cannot be decoded without its following carry
            // block.  Do not synthesize a value for it or publish a partial row.
            amplifierAssemblyLength = 0;
            amplifierHasPendingBlock = false;
            amplifierFrameBlockCount = 0;
        }

        // The WinUSB transport only changes packet framing.  The output here is
        // still the pre-WinUSB console's packed, multiplexed 512-channel int16
        // rows and uses its original physical-channel port map.
        private void DecodeAndPublishAmplifier(byte[] raw, int rawLength)
        {
            int rawBlockCount = rawLength / IntanAcquisitionBlockBytes;
            if (rawBlockCount == 0)
                return;

            // Every recovered block needs a following raw block.  The retained
            // pending block means all later calls recover their first block too.
            int recoveredBlockCount = rawBlockCount - (amplifierHasPendingBlock ? 0 : 1);
            int completeFrames = (amplifierFrameBlockCount + recoveredBlockCount) /
                IntanBlocksPerFrame;
            short[] output = completeFrames > 0
                ? new short[completeFrames * AmplifierChannels]
                : null;
            int outputFrameCount = 0;

            for (int block = 0; block < rawBlockCount; block++)
            {
                ReadIntanRawBlock(raw, block * IntanAcquisitionBlockBytes, amplifierScratchLanes);

                if (amplifierHasPendingBlock)
                {
                    RecoverAndAppendIntanBlock(amplifierPendingLanes, amplifierScratchLanes);
                    if (amplifierFrameBlockCount == IntanBlocksPerFrame)
                    {
                        Buffer.BlockCopy(amplifierFrame, 0, output,
                            outputFrameCount * AmplifierFrameBytes, AmplifierFrameBytes);
                        outputFrameCount++;
                        amplifierFrameBlockCount = 0;
                    }
                }

                ushort[] completedBlock = amplifierPendingLanes;
                amplifierPendingLanes = amplifierScratchLanes;
                amplifierScratchLanes = completedBlock;
                amplifierHasPendingBlock = true;
            }

            if (outputFrameCount == 0)
                return;

            byte[] outputBytes = new byte[output.Length * sizeof(short)];
            Buffer.BlockCopy(output, 0, outputBytes, 0, outputBytes.Length);
            WritePayload(amplifierFile, outputBytes, 0, outputBytes.Length);

            short[] latestFrame = new short[AmplifierChannels];
            Buffer.BlockCopy(output, (outputFrameCount - 1) * AmplifierFrameBytes,
                latestFrame, 0, AmplifierFrameBytes);
            lock (lastDecodedLock)
            {
                lastDecodedBlock = latestFrame;
            }

            ScopeForm scope = scopeForm;
            if (scope != null && !scope.IsDisposed)
                scope.PushData(output);
        }

        // Each native raw block is a 16 x 16 bit matrix: word zero supplies the
        // MSB and word fifteen supplies the LSB of each OSPI lane.  This staged
        // transpose is equivalent to the old nested bit loops but fast enough
        // for the full 20 kHz, 512-channel stream and allocates no per-block data.
        private static void ReadIntanRawBlock(byte[] raw, int offset, ushort[] lanes)
        {
            for (int word = 0; word < IntanLanes; word++)
            {
                int wordOffset = offset + word * sizeof(ushort);
                lanes[word] = (ushort)(raw[wordOffset] | (raw[wordOffset + 1] << 8));
            }

            for (int index = 0; index < 8; index++)
                SwapTransposeBits(lanes, index, index + 8, 8, 0x00FF);
            for (int baseIndex = 0; baseIndex < IntanLanes; baseIndex += 8)
            {
                for (int index = 0; index < 4; index++)
                    SwapTransposeBits(lanes, baseIndex + index, baseIndex + index + 4, 4, 0x0F0F);
            }
            for (int baseIndex = 0; baseIndex < IntanLanes; baseIndex += 4)
            {
                for (int index = 0; index < 2; index++)
                    SwapTransposeBits(lanes, baseIndex + index, baseIndex + index + 2, 2, 0x3333);
            }
            for (int index = 0; index < IntanLanes; index += 2)
                SwapTransposeBits(lanes, index, index + 1, 1, 0x5555);

            // The staged bit transpose reverses the lane index.  Restoring it
            // yields lanes 0..15 in exactly the ordering used by OspiToInt2.
            for (int lane = 0; lane < IntanLanes / 2; lane++)
            {
                ushort laneValue = lanes[lane];
                lanes[lane] = lanes[IntanLanes - 1 - lane];
                lanes[IntanLanes - 1 - lane] = laneValue;
            }
        }

        private static void SwapTransposeBits(ushort[] values, int first, int second,
            int shift, ushort mask)
        {
            ushort transferred = (ushort)((values[first] ^ (values[second] >> shift)) & mask);
            values[first] = (ushort)(values[first] ^ transferred);
            values[second] = (ushort)(values[second] ^ (transferred << shift));
        }

        private void RecoverAndAppendIntanBlock(ushort[] previous, ushort[] following)
        {
            int channelOnChip = amplifierFrameBlockCount;
            for (int lane = 0; lane < IntanLanes; lane++)
            {
                // OCTOSPI captures the last serial bit(s) after the end of
                // this 32-byte transaction.  Complete the current Intan word
                // with the high bit(s) from the following raw block; zero-fill
                // here would leave every A lane shifted by one bit and every B
                // lane shifted by two bits.
                int carryBits = lane < 8 ? 2 : 1;
                ushort value = (ushort)(
                    ((previous[lane] << carryBits) & 0xFFFF) |
                    (following[lane] >> (IntanLanes - carryBits)));
                int outputIndex = LegacyIntanPortMap[lane] * IntanBlocksPerFrame + channelOnChip;
                amplifierFrame[outputIndex] = unchecked((short)value);
            }
            amplifierFrameBlockCount++;
        }

        private void StartGoodChannelCounter()
        {
            goodChannelRunning = true;
            goodChannelThread = new Thread(GoodChannelLoop);
            goodChannelThread.IsBackground = true;
            goodChannelThread.Name = "USB512 good channel count";
            goodChannelThread.Priority = ThreadPriority.BelowNormal;
            goodChannelThread.Start();
        }

        private void StopGoodChannelCounter()
        {
            goodChannelRunning = false;
            Thread thread = goodChannelThread;
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(500);
            goodChannelThread = null;
        }

        // Port0A through Port3B are consecutive 64-channel banks after the
        // legacy physical-channel remap.  Preserve the original indicator's
        // only rule: a good channel is a nonzero decoded int16 sample.
        private void GoodChannelLoop()
        {
            const int bankSize = 64;
            const int totalBanks = AmplifierChannels / bankSize;
            while (goodChannelRunning)
            {
                short[] frame = null;
                lock (lastDecodedLock)
                {
                    if (lastDecodedBlock != null)
                        frame = (short[])lastDecodedBlock.Clone();
                }

                if (frame != null)
                {
                    int[] goodCounts = new int[totalBanks];
                    for (int bank = 0; bank < totalBanks; bank++)
                    {
                        int firstChannel = bank * bankSize;
                        for (int channel = firstChannel; channel < firstChannel + bankSize; channel++)
                        {
                            if (frame[channel] != 0)
                                goodCounts[bank]++;
                        }
                    }

                    PostToUi(delegate
                    {
                        lblStaleBanks.Text = string.Format(
                            "Good channels (nonzero): Port0A:{0} Port0B:{1} Port1A:{2} Port1B:{3}\r\nPort2A:{4} Port2B:{5} Port3A:{6} Port3B:{7}",
                            goodCounts[0], goodCounts[1], goodCounts[2], goodCounts[3],
                            goodCounts[4], goodCounts[5], goodCounts[6], goodCounts[7]);
                    });
                }

                Thread.Sleep(300);
            }
        }

        private int TrackSequence(byte streamId, ushort sequence)
        {
            ushort previous;
            int missingPackets = 0;
            if (previousSequences.TryGetValue(streamId, out previous))
            {
                ushort delta = (ushort)(sequence - previous);
                if (delta == 0 || delta > 0x8000)
                {
                    // Duplicate or a stale packet from a replayed transfer.
                    // The current sequence position remains authoritative.
                    return -1;
                }
                if (delta != 1)
                {
                    missingPackets = delta - 1;
                    sequenceGaps += missingPackets;
                }
            }
            previousSequences[streamId] = sequence;
            return missingPackets;
        }

        private static void WritePayload(FileStream stream, byte[] data, int offset, int length)
        {
            if (stream != null && length > 0)
                stream.Write(data, offset, length);
        }

        // timestamp.dat is a packed sequence of UInt32 little-endian values,
        // one for every 32-byte analogin.dat record.  The value is the
        // PC-synchronized milliseconds-since-midnight copied from CE64's
        // existing PCtime field, so the acquisition protocol needs no wider
        // high-speed packet header.
        private void WriteAnalogInTimestamps(byte[] data, int offset, int length)
        {
            if (timestampFile == null || length < AnalogInSampleBytes)
                return;

            int records = length / AnalogInSampleBytes;
            int requiredTimestampBytes = records * TimestampBytesPerRecord;
            if (timestampWriteBuffer.Length < requiredTimestampBytes)
                timestampWriteBuffer = new byte[requiredTimestampBytes];
            for (int record = 0; record < records; record++)
            {
                int sourceOffset = offset + record * AnalogInSampleBytes + AnalogInPcTimeOffsetBytes;
                Buffer.BlockCopy(data, sourceOffset, timestampWriteBuffer,
                    record * TimestampBytesPerRecord, TimestampBytesPerRecord);
            }
            timestampFile.Write(timestampWriteBuffer, 0, requiredTimestampBytes);
        }

        // IMU records are fixed 32-byte, little-endian CE64-compatible samples:
        // digital, accel XYZ, gyro XYZ, magnetometer XYZ, VDD, test, reserve,
        // PC time. Keep only the newest sample for the low-rate UI display.
        private void CaptureLatestImuSample(byte[] data, int offset, int length)
        {
            int records = length / AnalogInSampleBytes;
            if (records == 0)
                return;

            ImuSample sample = ReadImuSample(data, offset + (records - 1) * AnalogInSampleBytes);
            DateTime observedAtUtc = DateTime.UtcNow;
            lock (imuSampleLock)
            {
                ImuSample previous = latestImuSample;
                bool hadPrevious = hasLatestImuSample;
                bool tupleChanged = false;
                // Scan the whole batch, not merely its newest entry: a tuple
                // can vary and return to the same endpoint between UI updates.
                for (int record = 0; record < records; record++)
                {
                    ImuSample candidate = ReadImuSample(data, offset + record * AnalogInSampleBytes);
                    if (!hadPrevious || !HasSameImuTuple(previous, candidate))
                        tupleChanged = true;
                    previous = candidate;
                    hadPrevious = true;
                }
                latestImuSample = sample;
                hasLatestImuSample = true;
                if (tupleChanged)
                    imuTupleChangedAtUtc = observedAtUtc;
            }
        }

        private static ImuSample ReadImuSample(byte[] data, int sampleOffset)
        {
            return new ImuSample
            {
                Ax = ReadInt16LittleEndian(data, sampleOffset + 2),
                Ay = ReadInt16LittleEndian(data, sampleOffset + 4),
                Az = ReadInt16LittleEndian(data, sampleOffset + 6),
                Gx = ReadInt16LittleEndian(data, sampleOffset + 8),
                Gy = ReadInt16LittleEndian(data, sampleOffset + 10),
                Gz = ReadInt16LittleEndian(data, sampleOffset + 12),
                Mx = ReadInt16LittleEndian(data, sampleOffset + 14),
                My = ReadInt16LittleEndian(data, sampleOffset + 16),
                Mz = ReadInt16LittleEndian(data, sampleOffset + 18),
                PcTime = ReadUInt32LittleEndian(data, sampleOffset + AnalogInPcTimeOffsetBytes)
            };
        }

        private static bool HasSameImuTuple(ImuSample left, ImuSample right)
        {
            return left.Ax == right.Ax && left.Ay == right.Ay && left.Az == right.Az &&
                left.Gx == right.Gx && left.Gy == right.Gy && left.Gz == right.Gz &&
                left.Mx == right.Mx && left.My == right.My && left.Mz == right.Mz;
        }

        private void UpdateImuFreshnessFromStatus(Ce64MscStatus status)
        {
            // Older v1 peers leave this reserved extension all-zero. Once any
            // extension field is nonzero, the completion counter—not HAL tick
            // or raw tuple variation—is the authoritative sensor-live signal.
            bool extensionPresent = status.ImuCompletionCount != 0 || status.ImuLastCompletionTick != 0 ||
                status.ImuRetryRecoveryCount != 0;
            if (!extensionPresent)
                return;
            DateTime observedAtUtc = DateTime.UtcNow;
            lock (imuSampleLock)
            {
                if (!hasAuthoritativeImuFreshness || status.ImuCompletionCount != lastImuCompletionCount)
                    imuCompletionChangedAtUtc = observedAtUtc;
                hasAuthoritativeImuFreshness = true;
                lastImuCompletionCount = status.ImuCompletionCount;
                lastImuCompletionTick = status.ImuLastCompletionTick;
                lastImuRetryRecoveryCount = status.ImuRetryRecoveryCount;
            }
        }

        private ImuFreshness GetImuFreshnessLocked(DateTime now)
        {
            ImuFreshness freshness = new ImuFreshness
            {
                IsAuthoritative = hasAuthoritativeImuFreshness,
                CompletionCount = lastImuCompletionCount,
                RetryRecoveryCount = lastImuRetryRecoveryCount
            };
            DateTime changedAtUtc = hasAuthoritativeImuFreshness ? imuCompletionChangedAtUtc : imuTupleChangedAtUtc;
            freshness.UnchangedFor = changedAtUtc == DateTime.MinValue ? TimeSpan.Zero : now - changedAtUtc;
            return freshness;
        }

        private void PostLatestImuTelemetry()
        {
            ImuSample sample;
            bool available;
            ImuFreshness freshness;
            DateTime now = DateTime.UtcNow;
            lock (imuSampleLock)
            {
                if (lastImuTelemetryPostedAtUtc != DateTime.MinValue &&
                    (now - lastImuTelemetryPostedAtUtc).TotalMilliseconds < 100)
                    return;
                lastImuTelemetryPostedAtUtc = now;
                sample = latestImuSample;
                available = hasLatestImuSample;
                freshness = GetImuFreshnessLocked(now);
            }
            PostToUi(delegate { imuTelemetry.SetSample(sample, available, freshness); });
        }

        private static short ReadInt16LittleEndian(byte[] data, int offset)
        {
            return unchecked((short)(data[offset] | (data[offset + 1] << 8)));
        }

        private static uint ReadUInt32LittleEndian(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) |
                (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        // Preview assembly is only enabled while its window is open.  The writer
        // has already appended the exact payload to misc.dat before this code runs.
        private void AssemblePreviewFrame(byte[] data, int offset, int length, byte flags)
        {
            CameraPreviewForm preview;
            lock (previewLock)
            {
                preview = cameraPreview;
            }
            if (preview == null || preview.IsDisposed)
                return;

            lock (cameraAssemblyLock)
            {
                bool isStart = (flags & USB512WinUsbDevice.FlagFrameStart) != 0;
                bool isEnd = (flags & USB512WinUsbDevice.FlagFrameEnd) != 0;
                if (isStart)
                {
                    ReleasePreviewAssembly();
                    byte[] previewBuffer;
                    if (!preview.TryAcquireRawFrameBuffer(out previewBuffer))
                        return;
                    cameraAssembly = previewBuffer;
                    cameraAssemblyLength = 0;
                    cameraAssemblyOpen = true;
                    cameraAssemblyPreview = preview;
                }

                if (!cameraAssemblyOpen || cameraAssembly == null)
                    return;

                if (length > CameraFrameBytes - cameraAssemblyLength)
                {
                    ReleasePreviewAssembly();
                    return;
                }

                Buffer.BlockCopy(data, offset, cameraAssembly, cameraAssemblyLength, length);
                cameraAssemblyLength += length;
                if (!isEnd)
                    return;

                if (cameraAssemblyLength == CameraFrameBytes)
                {
                    byte[] completed = cameraAssembly;
                    CameraPreviewForm owner = cameraAssemblyPreview;
                    cameraAssembly = null;
                    cameraAssemblyLength = 0;
                    cameraAssemblyOpen = false;
                    cameraAssemblyPreview = null;
                    if (owner != null)
                        owner.PublishFrame(completed);
                }
                else
                {
                    ReleasePreviewAssembly();
                }
            }
        }

        private void ReleasePreviewAssembly()
        {
            lock (cameraAssemblyLock)
            {
                byte[] buffer = cameraAssembly;
                CameraPreviewForm owner = cameraAssemblyPreview;
                cameraAssembly = null;
                cameraAssemblyLength = 0;
                cameraAssemblyOpen = false;
                cameraAssemblyPreview = null;
                if (owner != null)
                    owner.ReturnRawFrameBuffer(buffer);
            }
        }

        private void UpdateUiStatistics()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - lastUiUpdate).TotalMilliseconds < UiUpdateIntervalMilliseconds)
                return;
            lastUiUpdate = now;

            var current = new RateSnapshot
            {
                Timestamp = now,
                TotalPayloadBytes = totalPayloadBytes,
                AmplifierPayloadBytes = amplifierPayloadBytes,
                AnalogInPayloadBytes = analogInPayloadBytes,
                CameraPayloadBytes = cameraPayloadBytes,
                CameraFrames = cameraFrames,
                AdcPayloadBytes = adcPayloadBytes,
                NeuropixelsPayloadBytes = neuropixelsPayloadBytes,
                NeuropixelsApRows = Interlocked.Read(ref neuropixelsApRows),
                NeuropixelsLfpRows = Interlocked.Read(ref neuropixelsLfpRows)
            };
            rateSnapshots.Enqueue(current);
            while (rateSnapshots.Count > 1 &&
                   (now - rateSnapshots.Peek().Timestamp).TotalMilliseconds > RateAveragingWindowMilliseconds)
            {
                rateSnapshots.Dequeue();
            }

            RateSnapshot windowStart = rateSnapshots.Peek();
            double rateWindowSeconds = Math.Max(0.001, (now - windowStart.Timestamp).TotalSeconds);
            double payloadRateMBytes = (current.TotalPayloadBytes - windowStart.TotalPayloadBytes) /
                                       rateWindowSeconds / 1024.0 / 1024.0;
            double amplifierPayloadRateMBytes =
                (current.AmplifierPayloadBytes - windowStart.AmplifierPayloadBytes) /
                rateWindowSeconds / 1024.0 / 1024.0;
            double amplifierRate = (current.AmplifierPayloadBytes - windowStart.AmplifierPayloadBytes) /
                                   (double)AmplifierFrameBytes / rateWindowSeconds;
            double cameraPayloadRateMBytes =
                (current.CameraPayloadBytes - windowStart.CameraPayloadBytes) /
                rateWindowSeconds / 1024.0 / 1024.0;
            double adcPayloadRateMBytes = (current.AdcPayloadBytes - windowStart.AdcPayloadBytes) /
                                          rateWindowSeconds / 1024.0 / 1024.0;
            double analogInRate = (current.AnalogInPayloadBytes - windowStart.AnalogInPayloadBytes) /
                                   AnalogInSampleBytes / rateWindowSeconds;
            double cameraRate = (current.CameraFrames - windowStart.CameraFrames) / rateWindowSeconds;
            double adcRate = (current.AdcPayloadBytes - windowStart.AdcPayloadBytes) /
                                           (double)AdcSampleBytes / rateWindowSeconds;
            double neuropixelsRate = (current.NeuropixelsPayloadBytes - windowStart.NeuropixelsPayloadBytes) /
                                      rateWindowSeconds / 1024.0;
            double neuropixelsApRate = (current.NeuropixelsApRows - windowStart.NeuropixelsApRows) /
                                       rateWindowSeconds;
            double neuropixelsLfpRate = (current.NeuropixelsLfpRows - windowStart.NeuropixelsLfpRows) /
                                        rateWindowSeconds;
            if(selectedDeviceKind==DeviceKind.XeNp) {
                neuropixelsApRate/=Math.Max(1,npActiveProbeCount);
                neuropixelsLfpRate/=Math.Max(1,npActiveProbeCount);
            }
            long frames = cameraFrames;
            long gaps = sequenceGaps;
            long recoveries = Interlocked.Read(ref transportRecoveries);
            long recordedBytes = GetRecordedOutputBytes();
            int npQueueCapacityBlocks=npHostQueueCapacityBlocks;
            int npQueuedBlocks=Math.Max(0,Volatile.Read(ref npHostQueueQueuedBlocks));
            long npQueueCapacityBytes=npHostQueueCapacityBytes;
            double npQueueCapacitySeconds=npHostQueueCapacitySeconds;
            double npQueueUsedMiB=npQueueCapacityBlocks==0?0:
                (npQueueCapacityBytes/1048576.0)*Math.Min(npQueuedBlocks,npQueueCapacityBlocks)/npQueueCapacityBlocks;
            double npQueueFreeSeconds=npQueueCapacityBlocks==0?0:
                npQueueCapacitySeconds*Math.Max(0,npQueueCapacityBlocks-npQueuedBlocks)/npQueueCapacityBlocks;
            string npRamReserve=selectedDeviceKind==DeviceKind.XeNp && npQueueCapacityBlocks>0?
                "\r\nRAM reserve "+npQueueUsedMiB.ToString("F1")+" / "+
                (npQueueCapacityBytes/1048576.0).ToString("F0")+" MiB queued; "+
                npQueueFreeSeconds.ToString("F2")+" s free ("+npQueuedBlocks+" / "+npQueueCapacityBlocks+" blocks)":"";
            string remainingDiskSpace = GetRemainingDiskSpace();
            string clockSyncStatistics = GetClockSyncStatistics(now);
            TimeSpan elapsed = (captureStartedAtUtc == DateTime.MinValue) ?
                TimeSpan.Zero : now - captureStartedAtUtc;
            string elapsedText = ((int)elapsed.TotalHours).ToString("00") + ":" +
                elapsed.Minutes.ToString("00") + ":" + elapsed.Seconds.ToString("00");
            ImuSample imuSample;
            bool hasImuSample;
            ImuFreshness imuFreshness;
            lock (imuSampleLock)
            {
                imuSample = latestImuSample;
                hasImuSample = hasLatestImuSample;
                imuFreshness = GetImuFreshnessLocked(now);
            }
            PostToUi(delegate
            {
                imuTelemetry.SetSample(imuSample, hasImuSample, imuFreshness);
                /* Keep UI statistics independent of composite-format parsing.
                 * This callback runs on the WinForms thread; a malformed
                 * String.Format template here otherwise terminates the UI
                 * update and is reported only as TargetInvocationException. */
                lblSpeed.Text = "Sampling (" + rateWindowSeconds.ToString("F1") +
                    " s average): amplifier " + (amplifierRate / 1000.0).ToString("F2") +
                    " kS/s | analog-in " + (analogInRate / 1000.0).ToString("F2") +
                    " kS/s | camera " + cameraRate.ToString("F1") +
                    " fps | ADC " + (adcRate / 1000.0).ToString("F1") +
                    " kS/s | NP raw " + neuropixelsRate.ToString("F1") +
                    " KiB/s | NP AP " + (neuropixelsApRate / 1000.0).ToString("F2") +
                    " kS/s | LFP " + (neuropixelsLfpRate / 1000.0).ToString("F2") + " kS/s"+
                    (selectedDeviceKind==DeviceKind.XeNp?" (per-probe average, "+npActiveProbeCount+" probes)":"");
                lblRecInfo.Text = "Payload " + payloadRateMBytes.ToString("F2") +
                    " MiB/s | elapsed " + elapsedText + " | recorded " +
                    (recordedBytes / 1000000000.0).ToString("F2") +
                    " GB | disk free " + remainingDiskSpace + "\r\nsequence gaps " +
                     gaps.ToString("N0") + " (permanent packet loss); USB pipe recoveries " +
                     recoveries.ToString("N0") + "; " + clockSyncStatistics +
                     npRamReserve+
                     "\r\nPackets: amp " + amplifierPackets.ToString("N0") +
                    ", analogin " + analogInPackets.ToString("N0") +
                    ", camera " + cameraPackets.ToString("N0") + " (" +
                    frames.ToString("N0") + " complete), ADC " +
                    adcPackets.ToString("N0") + ", misc " +
                    metadataPackets.ToString("N0") + ", NP " +
                    neuropixelsPackets.ToString("N0") + ", NP IMU samples " + Interlocked.Read(ref npImuSamples).ToString("N0");
            });
        }

        // The MSC recorder deliberately bypasses the WinUSB packet/logger path,
        // but its recording summary should retain the same operator-facing
        // semantics: measured rate, elapsed time, bytes on disk, available disk,
        // and explicit integrity counters.  File positions are used rather than
        // producer counters for "written", because they include the native IMU
        // payload and its timestamp sidecar exactly as persisted by this process.
        private void UpdateCe64RecordingInfo(Ce64MscStatus status, Ce64MscCapabilities caps,
            DateTime startedAtUtc, DateTime now, double intervalSeconds,
            long previousRecordedBytes, long recordedBytes, ulong startProduced, ulong startConsumed,
            ulong startImuProduced, ulong startImuConsumed, ulong previousConsumed,
            ulong previousImuConsumed, bool compressedV2, ulong losslessBlocks, ulong lossyBlocks,
            uint currentQuantShift, uint maxQuantShift)
        {
            long amplifierBytes = GetOutputFilePosition(amplifierFile);
            long imuBytes = GetOutputFilePosition(analogInFile);
            long savedFrames = caps.FrameBytes == 0 ? 0 : amplifierBytes / caps.FrameBytes;
            long savedSamples = savedFrames * (long)caps.MaxChannels;
            long savedImuRecords = imuBytes / AnalogInSampleBytes;
            double persistedRateMebibytes = Math.Max(0, recordedBytes - previousRecordedBytes) /
                intervalSeconds / 1024.0 / 1024.0;
            // The primary data-rate value above is the exact on-disk rate,
            // including IMU and timestamp sidecars. The sampling rates are
            // C64S consume deltas over this UI window, with post-START baselines
            // retained for the session totals below.
            double ephysRateFromStatus = CounterDelta(status.Consumed, previousConsumed) / intervalSeconds;
            double imuRateFromStatus = CounterDelta(status.ImuConsumed, previousImuConsumed) / intervalSeconds;
            ulong sessionProduced = CounterDelta(status.Produced, startProduced);
            ulong sessionConsumed = CounterDelta(status.Consumed, startConsumed);
            ulong sessionImuProduced = CounterDelta(status.ImuProduced, startImuProduced);
            ulong sessionImuConsumed = CounterDelta(status.ImuConsumed, startImuConsumed);
            TimeSpan elapsed = now - startedAtUtc;
            string elapsedText = ((int)elapsed.TotalHours).ToString("00") + ":" +
                elapsed.Minutes.ToString("00") + ":" + elapsed.Seconds.ToString("00");
            string imuStatus = GetCe64ImuRecordingStatus(status, caps, now);
            ulong totalCompressedBlocks = losslessBlocks + lossyBlocks;
            double quantizedPercent = totalCompressedBlocks == 0 ? 0.0 : 100.0 * lossyBlocks / totalCompressedBlocks;
            string compressionStatus = compressedV2 && status.Compressed != null ?
                "C64Z lossless/quantized blocks " + losslessBlocks.ToString("N0") + "/" + lossyBlocks.ToString("N0") +
                " (" + quantizedPercent.ToString("F1") + "%) | content precision only; fixed 10k x 64" +
                "\r\nshift " + currentQuantShift.ToString() + "/" + Math.Max(maxQuantShift, status.Compressed.MaxQuantShift).ToString() +
                " | FIFO " + status.Compressed.FifoPendingBytes.ToString("N0") + "/" + status.Compressed.FifoHighWaterBytes.ToString("N0") +
                " | source " + status.Compressed.SourcePendingFrames.ToString("N0") + "/" + status.Compressed.SourceCapacityFrames.ToString("N0") +
                " | R " + status.Compressed.RiceLastCycles.ToString("N0") + "/" + status.Compressed.RiceMaxCycles.ToString("N0") +
                " Q8 " + status.Compressed.Q8LastCycles.ToString("N0") + "/" + status.Compressed.Q8MaxCycles.ToString("N0") +
                " Q7 " + status.Compressed.Q7LastCycles.ToString("N0") + "/" + status.Compressed.Q7MaxCycles.ToString("N0") :
                "raw v1";
            string folderName = String.IsNullOrEmpty(runFolder) ? "n/a" : Path.GetFileName(runFolder);
            string diskFree = GetRemainingDiskSpace();

            PostToUi(delegate
            {
                lblSpeed.Text = GetCe64MscDeviceName(caps.MaxChannels) + " | " + caps.MaxChannels + " channels | measured ephys " +
                    (ephysRateFromStatus / 1000.0).ToString("F2") +
                    " kframes/s | IMU " + imuRateFromStatus.ToString("F1") +
                    " records/s | elapsed " + elapsedText;
                // Keep seven short, deliberate rows: lblRecInfo has 91 pixels
                // before lblStaleBanks at y=288. The output-folder
                // and primary-file links above remain the full clickable paths.
                lblRecInfo.Text = "Data " + persistedRateMebibytes.ToString("F2") +
                    " MiB/s | " + elapsedText + " | written " +
                    (recordedBytes / 1024.0 / 1024.0).ToString("F2") + " MiB | free " + diskFree +
                    "\r\nEphys " + (ephysRateFromStatus / 1000.0).ToString("F2") +
                    " kf/s | saved " + savedFrames.ToString("N0") + " frames / " +
                    savedSamples.ToString("N0") + " samples" +
                    "\r\nC64S P/C/Q " + sessionProduced.ToString("N0") + "/" +
                    sessionConsumed.ToString("N0") + "/" + status.PendingFrames.ToString("N0") +
                    " | drop/underflow " + status.Dropped.ToString("N0") + "/" + status.Underflow.ToString("N0") +
                    "\r\nIMU " + imuStatus + " | P/C/Q " + sessionImuProduced.ToString("N0") + "/" +
                    sessionImuConsumed.ToString("N0") + "/" + status.ImuPendingRecords.ToString("N0") +
                    " | saved " + savedImuRecords.ToString("N0") + " | D/U " +
                    status.ImuDropped.ToString("N0") + "/" + status.ImuUnderflow.ToString("N0") +
                    "\r\n" + compressionStatus +
                    "\r\nOut " + folderName + ": amplifier.dat + analogin.dat + timestamp.dat";
            });
        }

        private static ulong CounterDelta(ulong current, ulong baseline)
        {
            // A changing session sequence is already fatal in the capture loop.
            // Treat a counter reset as zero here to keep a transient UI refresh
            // from rendering a huge unsigned wraparound value.
            return current >= baseline ? current - baseline : 0;
        }

        private string GetCe64ImuRecordingStatus(Ce64MscStatus status, Ce64MscCapabilities caps, DateTime now)
        {
            if (caps == null || !caps.ImuApertureAvailable)
                return "aperture unavailable";

            bool hasSample;
            ImuFreshness freshness;
            lock (imuSampleLock)
            {
                hasSample = hasLatestImuSample;
                freshness = GetImuFreshnessLocked(now);
            }

            if (freshness.IsAuthoritative)
            {
                string state = freshness.UnchangedFor.TotalSeconds >= 1.0 ?
                    "STALE " + freshness.UnchangedFor.TotalSeconds.ToString("F1") + "s" : "live";
                return state + " C" + freshness.CompletionCount.ToString("N0") +
                    " R" + freshness.RetryRecoveryCount.ToString("N0");
            }

            if (!hasSample)
                return "awaiting records";
            string fallbackState = freshness.UnchangedFor.TotalSeconds >= 2.0 ?
                "tuple stale " + freshness.UnchangedFor.TotalSeconds.ToString("F1") + "s" :
                "tuple changing";
            return fallbackState;
        }

        // The logger is the sole writer for these streams.  Position therefore
        // includes every record accepted by the file buffers without forcing a
        // Flush during capture, which would reduce sustained USB throughput.
        private long GetRecordedOutputBytes()
        {
            if(selectedDeviceKind==DeviceKind.XeNp && npCaptureSession!=null)return npCaptureSession.FileBytes;
            return GetOutputFilePosition(amplifierFile) +
                   GetOutputFilePosition(analogInFile) +
                   GetOutputFilePosition(timestampFile) +
                   GetOutputFilePosition(cameraFile) +
                   GetOutputFilePosition(adcFile) +
                   GetOutputFilePosition(metadataFile) +
                   GetOutputFilePosition(neuropixelsFile) +
                   GetOutputFilePosition(neuropixelsApFile) +
                   GetOutputFilePosition(neuropixelsLfpFile);
        }

        private static long GetOutputFilePosition(FileStream file)
        {
            if (file == null)
                return 0;

            try { return file.Position; }
            catch (IOException) { return 0; }
            catch (ObjectDisposedException) { return 0; }
        }

        private string GetRemainingDiskSpace()
        {
            string folder = !String.IsNullOrEmpty(runFolder) ? runFolder : saveFolder;
            if (String.IsNullOrEmpty(folder))
                return "n/a";

            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(folder));
                if (String.IsNullOrEmpty(root))
                    return "n/a";

                double freeGigabytes = new DriveInfo(root).AvailableFreeSpace / 1000000000.0;
                return string.Format("{0:F1} GB", freeGigabytes);
            }
            catch (ArgumentException) { return "n/a"; }
            catch (IOException) { return "n/a"; }
            catch (UnauthorizedAccessException) { return "n/a"; }
        }

        private void RecordClockSync(USB512ClockSyncResult result)
        {
            lock (clockSyncLock)
            {
                double offset = result.EstimatedOffsetMilliseconds;
                double absoluteOffset = Math.Abs(offset);
                if (clockSyncCount == 0)
                {
                    clockSyncMinimumOffsetMilliseconds = offset;
                    clockSyncMaximumOffsetMilliseconds = offset;
                }
                else
                {
                    clockSyncMinimumOffsetMilliseconds = Math.Min(clockSyncMinimumOffsetMilliseconds, offset);
                    clockSyncMaximumOffsetMilliseconds = Math.Max(clockSyncMaximumOffsetMilliseconds, offset);
                }
                clockSyncCount++;
                clockSyncLastOffsetMilliseconds = offset;
                clockSyncAbsoluteOffsetTotalMilliseconds += absoluteOffset;
                clockSyncMaximumAbsoluteOffsetMilliseconds =
                    Math.Max(clockSyncMaximumAbsoluteOffsetMilliseconds, absoluteOffset);
                clockSyncLastRoundTripMilliseconds = result.RoundTripMilliseconds;
                clockSyncRoundTripTotalMilliseconds += result.RoundTripMilliseconds;
                clockSyncLastAtUtc = DateTime.UtcNow;
            }
        }

        private string GetClockSyncStatistics(DateTime now)
        {
            lock (clockSyncLock)
            {
                if (clockSyncCount == 0)
                    return "RTC sync: awaiting first readback";

                double meanAbsoluteOffset = clockSyncAbsoluteOffsetTotalMilliseconds / clockSyncCount;
                double meanRoundTrip = clockSyncRoundTripTotalMilliseconds / clockSyncCount;
                double ageSeconds = Math.Max(0.0, (now - clockSyncLastAtUtc).TotalSeconds);
                return string.Format(
                    "RTC sync: offset {0:+0.0;-0.0;0.0} ms (|mean| {1:F1}, max {2:F1}, range {3:+0.0;-0.0;0.0} to {4:+0.0;-0.0;0.0}) | RTT {5:F1} ms (mean {6:F1}) | age {7:F1} s",
                    clockSyncLastOffsetMilliseconds, meanAbsoluteOffset,
                    clockSyncMaximumAbsoluteOffsetMilliseconds, clockSyncMinimumOffsetMilliseconds,
                    clockSyncMaximumOffsetMilliseconds, clockSyncLastRoundTripMilliseconds,
                    meanRoundTrip, ageSeconds);
            }
        }

        private void ResetCaptureState()
        {
            npPreviewPreparationMs=0;npPreviewOpenMaxMs=0;npPreviewOpenCount=0;npPreviewOpenGen2=0;
            previousSequences.Clear();
            integrityPreviousSequences.Clear();
            integrityReplayPendingStreams.Clear();
            integrityReplayInFlight = false;
            integrityAssemblyLength = 0;
            ReleasePreviewAssembly();
            packetAssemblyLength = 0;
            ResetAmplifierDecoder();
            lock (lastDecodedLock)
            {
                lastDecodedBlock = null;
            }
            lock (imuSampleLock)
            {
                latestImuSample = new ImuSample();
                hasLatestImuSample = false;
                imuTupleChangedAtUtc = DateTime.MinValue;
                hasAuthoritativeImuFreshness = false;
                lastImuCompletionCount = 0;
                lastImuCompletionTick = 0;
                lastImuRetryRecoveryCount = 0;
                imuCompletionChangedAtUtc = DateTime.MinValue;
                lastImuTelemetryPostedAtUtc = DateTime.MinValue;
            }
            imuTelemetry.SetNpText(null);
            Interlocked.Exchange(ref npImuSamples,0);
            imuTelemetry.SetSample(new ImuSample(), false, new ImuFreshness());
            streamCameraFrameLength = 0;
            totalPayloadBytes = 0;
            sequenceGaps = 0;
            transportRecoveries = 0;
            amplifierPackets = 0;
            analogInPackets = 0;
            cameraPackets = 0;
            cameraFrames = 0;
            adcPackets = 0;
            metadataPackets = 0;
            neuropixelsPackets = 0;
            neuropixelsApRows = 0;
            neuropixelsLfpRows = 0;
            amplifierPayloadBytes = 0;
            analogInPayloadBytes = 0;
            cameraPayloadBytes = 0;
            adcPayloadBytes = 0;
            neuropixelsPayloadBytes = 0;
            lastUiUpdate = DateTime.MinValue;
            rateSnapshots.Clear();
            lock (clockSyncLock)
            {
                clockSyncCount = 0;
                clockSyncLastOffsetMilliseconds = 0.0;
                clockSyncMinimumOffsetMilliseconds = 0.0;
                clockSyncMaximumOffsetMilliseconds = 0.0;
                clockSyncAbsoluteOffsetTotalMilliseconds = 0.0;
                clockSyncMaximumAbsoluteOffsetMilliseconds = 0.0;
                clockSyncLastRoundTripMilliseconds = 0.0;
                clockSyncRoundTripTotalMilliseconds = 0.0;
                clockSyncLastAtUtc = DateTime.MinValue;
            }
        }

        private void OpenOutputFiles(string folder)
        {
            if (selectedDeviceKind == DeviceKind.XeNp)
            {
                // Files are opened after firmware mode negotiation, before START.
                npCaptureSession=null;
                return;
            }
            if (selectedDeviceKind == DeviceKind.Ce64Msc)
            {
                amplifierFile = OpenOutput(Path.Combine(folder, "amplifier.dat"));
                // The optional MSC misc aperture uses this exact established
                // USB512 record format. It remains empty on v1 devices without
                // that advertised aperture.
                analogInFile = OpenOutput(Path.Combine(folder, "analogin.dat"));
                timestampFile = OpenOutput(Path.Combine(folder, "timestamp.dat"));
                return;
            }
            amplifierFile = OpenOutput(Path.Combine(folder, "amplifier.dat"));
            analogInFile = OpenOutput(Path.Combine(folder, "analogin.dat"));
            timestampFile = OpenOutput(Path.Combine(folder, "timestamp.dat"));
            cameraFile = OpenOutput(Path.Combine(folder, "misc.dat"));
            adcFile = OpenOutput(Path.Combine(folder, "adc.dat"));
            metadataFile = OpenOutput(Path.Combine(folder, "metadata.dat"));
            neuropixelsFile = OpenOutput(Path.Combine(folder, "neuropixels_raw.bin"));
        }

        private static FileStream OpenOutput(string path)
        {
            return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        }

        private void CloseOutputFiles()
        {
            if (ce64CompressionCsv != null)
            {
                try { ce64CompressionCsv.Dispose(); }
                catch { }
                ce64CompressionCsv = null;
            }
            CloseFile(ref amplifierFile);
            CloseFile(ref analogInFile);
            CloseFile(ref timestampFile);
            CloseFile(ref cameraFile);
            CloseFile(ref adcFile);
            CloseFile(ref metadataFile);
            CloseFile(ref neuropixelsFile);
            CloseFile(ref neuropixelsApFile);
            CloseFile(ref neuropixelsLfpFile);
        }

        private static void CloseFile(ref FileStream file)
        {
            if (file == null)
                return;
            try { file.Flush(); }
            catch { }
            try { file.Dispose(); }
            catch { }
            file = null;
        }

        private void btnPreview_Click(object sender, EventArgs e)
        {
            if(selectedDeviceKind==DeviceKind.XeNp){
                if(!NpControlsAvailable)return;
                firmwareManagementActive=true;UpdateStartEnabled();
                try{using(var media=new NpMediaUsbForm(devicePath,saveFolder))media.ShowDialog(this);}
                finally{firmwareManagementActive=false;UpdateStartEnabled();}
                return;
            }
            CameraPreviewForm form;
            bool created = false;
            lock (previewLock)
            {
                form = cameraPreview;
                if (form == null || form.IsDisposed)
                {
                    form = new CameraPreviewForm(SetCameraSensorExposure);
                    form.FormClosed += CameraPreview_FormClosed;
                    cameraPreview = form;
                    created = true;
                }
            }
            if (created)
                form.Show(this);
            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;
            form.BringToFront();
            form.Activate();
        }

        private void PrepareNpPreviewForCapture(int rate)
        {
            PrepareNpPreviewForCapture(rate,npPreviewMask,false,10);
        }

        private void PrepareNpPreviewForCapture(int rate,int mask,bool apOnly,int adcBits)
        {
            var clock=Stopwatch.StartNew();
            bool show=npPreviewShowRequested || (neuropixelsPreview!=null && !neuropixelsPreview.IsDisposed && neuropixelsPreview.Visible);
            if(neuropixelsPreview!=null && !neuropixelsPreview.IsDisposed && !neuropixelsPreview.MatchesProfile(rate,mask,apOnly,adcBits)) {
                neuropixelsPreview.KeepAliveWhileRecording=false;neuropixelsPreview.Close();neuropixelsPreview=null;
            }
            if(neuropixelsPreview==null || neuropixelsPreview.IsDisposed) {
                neuropixelsPreview=new NeuropixelsPreviewForm(rate,mask,apOnly,adcBits);
                neuropixelsPreview.StartPosition=FormStartPosition.Manual;
                neuropixelsPreview.Location=new Point(Right+10,Top);
                if(IsHandleCreated)neuropixelsPreview.Owner=this;
            }
            neuropixelsPreview.PrepareForCapture();
            if(show && !neuropixelsPreview.Visible)neuropixelsPreview.Show(this);
            npPreviewShowRequested=false;
            npPreviewPreparationMs=clock.Elapsed.TotalMilliseconds;
        }

        private void btnScope_Click(object sender, EventArgs e)
        {
            if (selectedDeviceKind == DeviceKind.XeNp || selectedDeviceKind == DeviceKind.NpV2)
            {
                bool np2=selectedDeviceKind==DeviceKind.NpV2;
                if(running && (neuropixelsPreview==null || neuropixelsPreview.IsDisposed)) {
                    npPreviewShowRequested=true;
                    lblRecInfo.Text="Preparing NP preview before capture starts...";
                    return;
                }
                var opening=Stopwatch.StartNew();int gen2=GC.CollectionCount(2);
                if (neuropixelsPreview == null || neuropixelsPreview.IsDisposed)
                {
                    // Firmware status supplies the actual rate: NP1 PSSI uses
                    // 30 kHz AP / 2.5 kHz LFP, while the NP2 USB profile is
                    // an AP-only 12-bit serial stream.
                    int rate=np2?(int)npV2RequestedSampleHz:xeNpPreviewRateHz;
                    int mask=np2?(int)(npV2RequestedProbeMask==0U?1U:npV2RequestedProbeMask):npPreviewMask;
                    neuropixelsPreview = new NeuropixelsPreviewForm(rate,mask,np2,np2?12:10);
                    neuropixelsPreview.StartPosition = FormStartPosition.Manual;
                    neuropixelsPreview.Location = new Point(Right + 10, Top);
                    neuropixelsPreview.Show(this);
                }
                else
                {
                    if(!neuropixelsPreview.Visible)neuropixelsPreview.Show(this);
                    neuropixelsPreview.BringToFront();
                    neuropixelsPreview.Focus();
                }
                if(running) {
                    npPreviewOpenCount++;
                    npPreviewOpenMaxMs=Math.Max(npPreviewOpenMaxMs,opening.Elapsed.TotalMilliseconds);
                    npPreviewOpenGen2+=GC.CollectionCount(2)-gen2;
                }
                return;
            }
            int previewChannels = selectedDeviceKind == DeviceKind.Ce64Msc ? ce64PreviewChannels : AmplifierChannels;
            int previewRateHz = selectedDeviceKind == DeviceKind.Ce64Msc ? ce64PreviewRateHz : AmplifierConfiguredRateHz;
            string previewTitle = selectedDeviceKind == DeviceKind.Ce64Msc ?
                GetCe64MscDeviceName((uint)previewChannels) + " " + previewChannels + "-channel Ephys Preview (" +
                (previewRateHz / 1000.0).ToString("0.###") + " kHz)" : "USB512 Scope";
            if (scopeForm != null && !scopeForm.IsDisposed && !scopeForm.MatchesStream(previewChannels, previewRateHz))
            {
                scopeForm.Close();
                scopeForm = null;
            }
            if (scopeForm == null || scopeForm.IsDisposed)
            {
                scopeForm = new ScopeForm(previewChannels, previewRateHz, previewTitle);
                scopeForm.StartPosition = FormStartPosition.Manual;
                scopeForm.Location = new Point(Right + 10, Top);
                scopeForm.Show(this);
            }
            else
            {
                scopeForm.BringToFront();
                scopeForm.Focus();
            }
        }

        // Called on the UI thread after C64C/ARM/START has established the
        // actual fixed rate. A preview opened before capture may still carry
        // the legacy rate, so recreate it before accepting newly negotiated
        // samples rather than silently dropping them on MatchesStream().
        private void EnsureCe64ScopeRate(uint rateHz, uint channels)
        {
            if (scopeForm == null || scopeForm.IsDisposed)
                return;
            if (scopeForm.MatchesStream(checked((int)channels), checked((int)rateHz)))
                return;
            scopeForm.Close();
            scopeForm = null;
            btnScope_Click(this, EventArgs.Empty);
        }

        private static string GetCe64MscDeviceName(uint channels)
        {
            return channels > Ce64MscProtocol.Wild64Channels ? "CE128" : "WILD64";
        }

        private void SetCameraSensorExposure(byte rowsInReset, byte rampGain)
        {
            lock (deviceLock)
            {
                if (activeDevice == null)
                    throw new InvalidOperationException("USB512 is not recording.");
                activeDevice.SetCameraExposure(rowsInReset, rampGain);
            }
        }

        private void CameraPreview_FormClosed(object sender, FormClosedEventArgs e)
        {
            lock (previewLock)
            {
                if (Object.ReferenceEquals(cameraPreview, sender))
                    cameraPreview = null;
            }
            ReleasePreviewAssembly();
        }

        private void llblSaveFolder_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (!String.IsNullOrEmpty(saveFolder) && Directory.Exists(saveFolder))
                System.Diagnostics.Process.Start("explorer.exe", saveFolder);
        }

        private void llblSavefile_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (!String.IsNullOrEmpty(runFolder) && Directory.Exists(runFolder))
                System.Diagnostics.Process.Start("explorer.exe", runFolder);
        }

        private void UpdateStartEnabled()
        {
            if(calibrationButton!=null)calibrationButton.Enabled=!running && !firmwareManagementActive && (selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2);
            if(npAutoRecover!=null)npAutoRecover.Visible=selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2;
            if (btnNpLogger != null) btnNpLogger.Enabled = !running && !ztestRunning;
            if(btnSdInfo!=null)btnSdInfo.Enabled=!running && selectedDeviceKind==DeviceKind.XeNp && !String.IsNullOrWhiteSpace(devicePath);
            btnStart.Enabled = !running && npRecoveryCancelled && selectedDeviceKind != DeviceKind.NpUnavailable && !String.IsNullOrWhiteSpace(devicePath) && !String.IsNullOrWhiteSpace(saveFolder);
            btnStop.Enabled = running && (selectedDeviceKind != DeviceKind.NpV2 || !ce64StopRequested);
            btnDfu.Text="Firmware Update";
            btnDfu.Enabled = !running && !ztestRunning && !firmwareManagementActive && systemInfoActive==0 && deviceRefreshActive==0 && npRecoveryCancelled;
            if (btnImpedance != null) btnImpedance.Enabled = !running && !ztestRunning && selectedDeviceKind == DeviceKind.Ce64Msc && !String.IsNullOrWhiteSpace(devicePath);
            if (btnStreams != null)
            {
                bool np=selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2;
                var requests=np?NpRequestsForDevice(devicePath):null;
                bool request=selectedDeviceKind==DeviceKind.NpV2?!String.IsNullOrWhiteSpace(devicePath) && NpV2RequestsForDevice(devicePath).Count>0:requests!=null && requests.Count>0;
                btnStreams.Text=request?"Recording settings*":"Recording settings";
                btnStreams.Enabled = !running && !ztestRunning && (!np || npRecoveryCancelled) && selectedDeviceKind != DeviceKind.Ce64Msc && selectedDeviceKind != DeviceKind.NpUnavailable;
            }
            UpdateSystemInfoControls();
            UpdateWildXControls();
        }

        private void PrepareCameraPreviewForCapture()
        {
            lock(previewLock) {
                if(cameraPreview==null || cameraPreview.IsDisposed) {
                    cameraPreview=new CameraPreviewForm(SetCameraSensorExposure);
                    cameraPreview.FormClosed+=CameraPreview_FormClosed;
                    cameraPreview.Owner=this;
                }
                cameraPreview.KeepAliveWhileRecording=true;
            }
        }

        private static bool CanOpenFirmwareManagement(DeviceKind kind,bool recording,bool impedance,string path)
        {
            return !recording && !impedance && !String.IsNullOrWhiteSpace(path) &&
                kind!=DeviceKind.NpV2 && kind!=DeviceKind.NpUnavailable &&
                (kind!=DeviceKind.XeNp || NpV1Management.UidFromPath(path)!=null);
        }

        private string DescribeActiveStreams()
        {
            List<string> streams = new List<string>();
            if ((activeStreams & USB512WinUsbDevice.StreamMaskEphys) != 0)
                streams.Add("20 kHz amplifier");
            if ((activeStreams & USB512WinUsbDevice.StreamMaskImu) != 0)
                streams.Add("1,250 Hz IMU");
            if ((activeStreams & USB512WinUsbDevice.StreamMaskCamera) != 0)
                streams.Add(requestedCameraProfile == USB512WinUsbDevice.CameraProfileSafe30Hz ?
                    "30 Hz safe camera" : requestedCameraRateHz + " Hz camera");
            if ((activeStreams & USB512WinUsbDevice.StreamMaskMicrophone) != 0)
                streams.Add("240 kHz ADC");
            if ((activeStreams & USB512WinUsbDevice.StreamMaskNeuropixels) != 0)
                streams.Add("Neuropixels (firmware-selected rate/layout)");
            return "WinUSB streams (" + String.Join(", ", streams.ToArray()) + ").";
        }

        private void FinishCaptureOnUi(string message)
        {
            if(neuropixelsPreview!=null && !neuropixelsPreview.IsDisposed)
                neuropixelsPreview.KeepAliveWhileRecording=false;
            captureThread = null;
            running = false;
            try {if(NpFinishRecovery(message))return;}
            catch(Exception ex) {npRecoveryCancelled=true;message="Capture error: recovery journal failed: "+ex.Message;}
            FinishCapturePresentation(message);
        }

        private void FinishCapturePresentation(string message)
        {
            btnRefresh.Enabled = true;
            comboDrives.Enabled = true;
            UpdateStartEnabled();
            lblRecInfo.Text = message + (String.IsNullOrEmpty(runFolder) ? String.Empty : ". Files: " + runFolder);
            if(selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2) {
                lastCaptureReport=NpReportForm.CaptureText(SystemInfoDeviceName(),devicePath,message,runFolder);
                captureReportButton.Enabled=true;
                if(!String.IsNullOrEmpty(runFolder) && Directory.Exists(runFolder))try{
                    File.WriteAllText(Path.Combine(runFolder,"capture_report.txt"),lastCaptureReport);
                    if(npRecordingSettings!=null)npRecordingSettings.Write();else NpRecordingSettings.RefreshBundle(runFolder);
                    if(npSessionFolder!=null)NpRecordingSettings.RefreshBundle(npSessionFolder);
                }
                    catch(Exception ex){lastCaptureReport+="\r\nReport persistence failed: "+ex.Message;}
            }
            if (message.StartsWith("Capture error:", StringComparison.Ordinal))
            {
                if(selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2) {
                    string statusFile=selectedDeviceKind==DeviceKind.NpV2?"neuropixels_np2usb_status.txt":"neuropixels_capture_status.txt";
                    MessageBox.Show(this,message+"\r\n\r\nRecording stopped to protect continuity. Treat these files as incomplete; see "+statusFile+" for firmware and host-queue diagnostics.",
                        "Neuropixels recording",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    return;
                }
                MessageBox.Show(this, message + "\r\n\r\n" +
                    "Confirm that USB512 is connected and bound to the WinUSB driver, then press Refresh.",
                    "USB512 capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string DescribeCaptureFailure(Exception exception)
        {
            System.ComponentModel.Win32Exception win32 = exception as System.ComponentModel.Win32Exception;
            if (win32 == null)
                return exception.Message;

            return string.Format("{0} (Win32 error {1})", win32.Message, win32.NativeErrorCode);
        }

        // Win32 31 is ERROR_GEN_FAILURE from the WinUSB bulk pipe.  A timed-out
        // retained-replay handshake is the same persistent pipe condition from
        // the capture loop's perspective: dispose the old WinUSB interface and
        // reopen it instead of ending the recording.
        private static bool IsRecoverableTransportFailure(Exception exception)
        {
            System.ComponentModel.Win32Exception win32 = exception as System.ComponentModel.Win32Exception;
            if (win32 != null)
            {
                return win32.NativeErrorCode == 2 ||      // device path renewed
                       win32.NativeErrorCode == 6 ||      // stale WinUSB handle
                       win32.NativeErrorCode == 31 ||     // ERROR_GEN_FAILURE
                       win32.NativeErrorCode == 121 ||    // timeout
                       win32.NativeErrorCode == 1167;     // device not connected
            }
            return exception is TimeoutException;
        }

        private USB512WinUsbDevice OpenCaptureDevice()
        {
            try
            {
                return USB512WinUsbDevice.Open(devicePath);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                IList<string> paths = USB512WinUsbDevice.EnumerateDevicePaths();
                if (paths.Count == 0)
                    throw;
                devicePath = paths[0];
                return USB512WinUsbDevice.Open(devicePath);
            }
        }

        private void PostToUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopCapture();
            if(captureThread!=null && (captureThread.IsAlive || selectedDeviceKind==DeviceKind.NpV2)) {
                e.Cancel=true;
                lblRecInfo.Text="Stopping USB; waiting for buffered data to finish saving...";
            }
            base.OnFormClosing(e);
        }

        // Compact, direct display for the newest IMU packet. It intentionally
        // shows board raw counts: the stream does not include the calibration
        // constants needed to safely label physical units.
        private sealed class ImuTelemetryPanel : Control
        {
            private string npText;
            private NpImuDisplay npDisplay;
            private bool npStopped;
            private DateTime npReceivedUtc;
            private readonly System.Windows.Forms.Timer npRefresh=new System.Windows.Forms.Timer();
            internal void SetNpText(string text){npDisplay=null;npRefresh.Stop();npText=text;Invalidate();}
            internal void SetNpSample(NpImuDisplay display,bool stopped) {
                npText=null;npDisplay=display;npStopped=stopped;npReceivedUtc=DateTime.UtcNow;
                if(stopped)npRefresh.Stop();else npRefresh.Start();Invalidate();
            }
            private const double TupleUnchangedWarningSeconds = 2.0;
            private const double CompletionUnchangedWarningSeconds = 1.0;
            private ImuSample sample;
            private bool hasSample;
            private ImuFreshness freshness;

            internal ImuTelemetryPanel()
            {
                DoubleBuffered = true;
                BackColor = Color.FromArgb(31, 34, 39);
                ForeColor = Color.Gainsboro;
                Font = new Font("Segoe UI", 8.25F);
                npRefresh.Interval=250;
                npRefresh.Tick+=delegate {if(npDisplay!=null)Invalidate();};
            }
            protected override void Dispose(bool disposing) {
                if(disposing)npRefresh.Dispose();base.Dispose(disposing);
            }

            internal void SetSample(ImuSample newest, bool available, ImuFreshness currentFreshness)
            {
                sample = newest;
                hasSample = available;
                freshness = currentFreshness;
                if (freshness.UnchangedFor < TimeSpan.Zero)
                    freshness.UnchangedFor = TimeSpan.Zero;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                graphics.Clear(BackColor);
                if(npDisplay!=null){DrawNp(graphics);return;}
                if(npText!=null) {graphics.DrawString(npText,Font,Brushes.Gainsboro,new RectangleF(8,7,Width-16,Height-14));return;}
                using (Font titleFont = new Font(Font, FontStyle.Bold))
                using (Brush muted = new SolidBrush(Color.FromArgb(165, 175, 185)))
                {
                    graphics.DrawString("ONBOARD 9-AXIS IMU", titleFont, Brushes.White, 8, 7);
                    if (!hasSample)
                    {
                        graphics.DrawString("Waiting for IMU packets...", Font, muted, 8, 26);
                        return;
                    }

                    bool stale = freshness.UnchangedFor.TotalSeconds >=
                        (freshness.IsAuthoritative ? CompletionUnchangedWarningSeconds : TupleUnchangedWarningSeconds);
                    string status;
                    if (freshness.IsAuthoritative)
                    {
                        status = stale ?
                            "WARNING | BMX completion unchanged " + freshness.UnchangedFor.TotalSeconds.ToString("F1") +
                            " s | retries " + freshness.RetryRecoveryCount.ToString("N0") :
                            "LIVE | BMX completions " + freshness.CompletionCount.ToString("N0") +
                            " | retries " + freshness.RetryRecoveryCount.ToString("N0");
                    }
                    else
                    {
                        status = stale ?
                            "WARNING | 9-axis tuple unchanged " + freshness.UnchangedFor.TotalSeconds.ToString("F1") +
                            " s (may be stationary)" :
                            "LIVE | " + sample.PcTime.ToString("N0") + " ms | raw counts";
                    }
                    graphics.DrawString(status, Font, stale ? Brushes.Goldenrod : Brushes.MediumSeaGreen, 8, 25);
                    int groupWidth = Math.Max(1, (Width - 16) / 3);
                    DrawGroup(graphics, 8, 45, groupWidth - 6, "ACCEL",
                        sample.Ax, sample.Ay, sample.Az, 16384, Color.DeepSkyBlue);
                    DrawGroup(graphics, 8 + groupWidth, 45, groupWidth - 6, "GYRO",
                        sample.Gx, sample.Gy, sample.Gz, 16384, Color.Orange);
                    DrawGroup(graphics, 8 + (2 * groupWidth), 45, groupWidth - 6, "MAG",
                        sample.Mx, sample.My, sample.Mz, 4096, Color.MediumOrchid);
                }
            }

            private void DrawNp(Graphics graphics) {
                bool stale=!npStopped && (DateTime.UtcNow-npReceivedUtc).TotalSeconds>=1;
                uint valid=stale?0U:npDisplay.Valid;
                using(var bold=new Font(Font,FontStyle.Bold)) {
                    graphics.DrawString("ONBOARD IMU | NP physical units",bold,Brushes.White,8,7);
                    string state=npStopped?"STOPPED":stale?"STALE":(valid&1)==0?"NOT READY":"LIVE";
                    string status=state+" | sample "+npDisplay.Sequence+" | device "+npDisplay.DeviceMs+" ms"+
                        " | IMU error "+npDisplay.ImuError+" | queue loss "+npDisplay.Overflow;
                    graphics.DrawString(status,Font,npStopped||stale||(valid&1)==0?Brushes.Goldenrod:Brushes.MediumSeaGreen,8,25);
                }
                int groupWidth=Math.Max(1,(Width-16)/3);
                DrawNpGroup(graphics,8,45,groupWidth-6,"ACCEL (g) +/-2",0,2,Color.DeepSkyBlue,(valid&1)!=0);
                DrawNpGroup(graphics,8+groupWidth,45,groupWidth-6,"GYRO (deg/s) +/-200",3,200,Color.Orange,(valid&1)!=0);
                DrawNpGroup(graphics,8+2*groupWidth,45,groupWidth-6,"MAG (uT) +/-100",6,100,Color.MediumOrchid,(valid&2)!=0);
                graphics.DrawString("Bar limits are display ranges; numbers retain full values. BME error "+npDisplay.EnvironmentError,
                    Font,Brushes.Gray,8,119);
            }

            private void DrawNpGroup(Graphics graphics,int x,int y,int width,string heading,
                int first,double fullScale,Color color,bool valid) {
                using(var bold=new Font(Font,FontStyle.Bold))
                using(var baseline=new Pen(Color.FromArgb(85,100,110)))
                using(var fill=new SolidBrush(color)) {
                    graphics.DrawString(heading,bold,Brushes.White,x,y);
                    int left=x+17,valueLeft=x+width-55,barWidth=Math.Max(16,valueLeft-left-5);
                    int center=left+barWidth/2;
                    for(int i=0;i<3;i++) {
                        int row=y+19+i*17;double v=npDisplay.Values[first+i];
                        graphics.DrawString(new[]{"X","Y","Z"}[i],Font,Brushes.Gray,x,row-2);
                        graphics.DrawLine(baseline,left,row+5,left+barWidth,row+5);
                        graphics.DrawLine(baseline,center,row+1,center,row+9);
                        if(valid) {
                            int extent=(int)Math.Round(Math.Max(-1,Math.Min(1,v/fullScale))*barWidth/2.0);
                            if(extent!=0)graphics.FillRectangle(fill,Math.Min(center,center+extent),row+2,Math.Abs(extent),7);
                        }
                        graphics.DrawString(valid?v.ToString("F2",System.Globalization.CultureInfo.InvariantCulture):"N/A",
                            Font,valid?Brushes.Gainsboro:Brushes.Gray,valueLeft,row-2);
                    }
                }
            }

            private void DrawGroup(Graphics graphics, int x, int y, int width, string heading,
                short xValue, short yValue, short zValue, int fullScale, Color color)
            {
                short[] values = { xValue, yValue, zValue };
                string[] labels = { "X", "Y", "Z" };
                using (Font headingFont = new Font(Font, FontStyle.Bold))
                using (Brush muted = new SolidBrush(Color.FromArgb(165, 175, 185)))
                using (Pen baseline = new Pen(Color.FromArgb(85, 100, 110)))
                using (Brush fill = new SolidBrush(color))
                {
                    graphics.DrawString(heading, headingFont, Brushes.White, x, y);
                    int barLeft = x + 17;
                    int valueLeft = x + width - 43;
                    int barWidth = Math.Max(16, valueLeft - barLeft - 5);
                    int center = barLeft + (barWidth / 2);
                    for (int index = 0; index < 3; index++)
                    {
                        int rowY = y + 19 + (index * 17);
                        graphics.DrawString(labels[index], Font, muted, x, rowY - 2);
                        graphics.DrawLine(baseline, barLeft, rowY + 5, barLeft + barWidth, rowY + 5);
                        graphics.DrawLine(baseline, center, rowY + 1, center, rowY + 9);
                        double normalized = Math.Max(-1.0, Math.Min(1.0, values[index] / (double)fullScale));
                        int extent = (int)Math.Round(normalized * (barWidth / 2.0));
                        graphics.FillRectangle(fill, Math.Min(center, center + extent), rowY + 2,
                            Math.Max(1, Math.Abs(extent)), 7);
                        TextRenderer.DrawText(graphics, values[index].ToString(), Font,
                            new Point(valueLeft, rowY - 3), ForeColor);
                    }
                }
            }
        }
    }
}
