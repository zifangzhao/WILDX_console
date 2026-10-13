using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace USB512_interface
{
    public partial class Form1
    {
        private int systemInfoActive, systemInfoGeneration, idleNpSensorPosted;
        private bool firmwareManagementActive;
        private System.Threading.Timer idleNpSensorTimer;
        private NpSystemInfo systemInfo;
        private TextBox systemInfoSummary;
        private Button btnSystemInfoRefresh;
        private void InitializeSystemInfo()
        {
            ClientSize = new Size(ClientSize.Width, 660);
            var panel = new GroupBox { Text = "Device firmware and SD card", Location = new Point(5, 452), Size = new Size(559, 195), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnSystemInfoRefresh = new Button { Text = "Refresh info", Location = new Point(10, 18), Size = new Size(105, 24) };
            btnSystemInfoRefresh.Click += delegate { RefreshSystemInfo(); };
            var details = new Button { Text = "Details", Location = new Point(121, 18), Size = new Size(85, 24) };
            details.Click += delegate {
                using (var dialog = new Form { Text = "System information", ClientSize = new Size(800, 550), StartPosition = FormStartPosition.CenterParent }) {
                    dialog.Controls.Add(new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
                        Text = systemInfo == null ? "Select a device and refresh its information." : systemInfo.Report(GetPackageVersion()) });
                    WildXTheme.Apply(dialog);
                    dialog.ShowDialog(this);
                }
            };
            systemInfoSummary = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(10, 48), Size = new Size(539, 135),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, Text = "Select a device to view firmware and SD-card information." };
            panel.Controls.AddRange(new Control[] { btnSystemInfoRefresh, details, systemInfoSummary });
            Controls.Add(panel);
            idleNpSensorTimer = new System.Threading.Timer(delegate {
                if (Interlocked.CompareExchange(ref idleNpSensorPosted, 1, 0) != 0) return;
                try { PostToUi(delegate { Interlocked.Exchange(ref idleNpSensorPosted, 0); RefreshIdleNp2Sensor(); }); }
                catch { Interlocked.Exchange(ref idleNpSensorPosted, 0); }
            }, null, 250, 250);
            FormClosed += delegate { idleNpSensorTimer.Dispose(); };
        }

        private bool CanRefreshIdleNp2Sensor()
        {
            return selectedDeviceKind == DeviceKind.NpV2 && !String.IsNullOrWhiteSpace(devicePath) &&
                !running && !ztestRunning && !firmwareManagementActive && !DownloadBusy && !BleInUse &&
                deviceRefreshActive == 0 && npRecoveryCancelled && !npRecoveryAttempt;
        }

        private void RefreshIdleNp2Sensor()
        {
            if (!CanRefreshIdleNp2Sensor() || Interlocked.CompareExchange(ref systemInfoActive, 1, 0) != 0) return;
            string selectedPath = devicePath; int generation = systemInfoGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                NpImuDisplay display = null;
                try {
                    using (var device = NpV2UsbDevice.Open(selectedPath)) {
                        NpV2ServiceStatus service = device.GetServiceStatus();
                        if (service.Mode != 2U || service.Busy != 0U) return;
                        NpV2CaptureStatus capture = device.GetCaptureStatus();
                        if (capture.State != 0U || capture.SourceState != 0U) return;
                        using (var monitor = new NpUsbSensorMonitor(device, String.Empty, selectedPath, false, delegate(NpImuDisplay value) { display = value; }))
                            monitor.FeedSnapshot(device.GetSensorSnapshot(), 0);
                    }
                } catch { }
                finally {
                    try { PostToUi(delegate {
                            Interlocked.Exchange(ref systemInfoActive, 0);
                            if (display != null && generation == systemInfoGeneration && String.Equals(selectedPath, devicePath, StringComparison.OrdinalIgnoreCase) && CanRefreshIdleNp2Sensor())
                                imuTelemetry.SetNpSample(display, false);
                        }); }
                    catch { Interlocked.Exchange(ref systemInfoActive, 0); }
                }
            });
        }
        private string SystemInfoDeviceName()
        {
            return selectedDeviceKind == DeviceKind.XeNp ? "NP V1" : selectedDeviceKind == DeviceKind.NpV2 ? "NP V2" :
                selectedDeviceKind == DeviceKind.Ce64Msc ? "WILD64 MSC" : selectedDeviceKind == DeviceKind.USB512 ? "USB512" : "Unidentified NP USB";
        }
        private void SystemInfoSelectionChanged()
        {
            Interlocked.Increment(ref systemInfoGeneration);
            systemInfo = NpSystemInfo.Empty(devicePath, SystemInfoDeviceName());
            systemInfoSummary.Text = String.IsNullOrWhiteSpace(devicePath) ? "Select a device to view firmware and SD-card information." : systemInfo.Summary();
            if (deviceRefreshActive == 0) RefreshSystemInfo();
        }
        private void RefreshSystemInfo()
        {
            if (firmwareManagementActive || running || ztestRunning || deviceRefreshActive != 0 || !npRecoveryCancelled || String.IsNullOrWhiteSpace(devicePath) ||
                selectedDeviceKind == DeviceKind.Ce64Msc || Interlocked.CompareExchange(ref systemInfoActive, 1, 0) != 0) return;
            string selectedPath = devicePath, device = SystemInfoDeviceName();
            int generation = systemInfoGeneration;
            systemInfoSummary.Text = "Reading passive device information...";
            UpdateStartEnabled();
            ThreadPool.QueueUserWorkItem(delegate {
                NpSystemInfo snapshot;
                try { snapshot = NpSystemInfo.Read(selectedPath, device); }
                catch (Exception ex) { snapshot = NpSystemInfo.Empty(selectedPath, device); snapshot.Status = "Information unavailable: " + ex.Message; }
                PostToUi(delegate { CompleteSystemInfo(selectedPath, generation, snapshot); });
            });
        }
        private void CompleteSystemInfo(string selectedPath, int generation, NpSystemInfo snapshot)
        {
            Interlocked.Exchange(ref systemInfoActive, 0);
            if (!running && generation == systemInfoGeneration && String.Equals(selectedPath, devicePath, StringComparison.OrdinalIgnoreCase)) {
                systemInfo = snapshot;
                systemInfoSummary.Text = snapshot.Summary();
            }
            UpdateStartEnabled();
        }
        private void UpdateSystemInfoControls()
        {
            bool busy = systemInfoActive != 0 || firmwareManagementActive;
            btnSystemInfoRefresh.Enabled = !busy && !running && !ztestRunning && deviceRefreshActive == 0 && npRecoveryCancelled &&
                selectedDeviceKind != DeviceKind.Ce64Msc && !String.IsNullOrWhiteSpace(devicePath);
            if (busy) {
                btnStart.Enabled = btnDfu.Enabled = btnNpLogger.Enabled = btnSdInfo.Enabled = btnStreams.Enabled = btnImpedance.Enabled = false;
            }
            comboDrives.Enabled = btnRefresh.Enabled = !busy && !running && !ztestRunning && deviceRefreshActive == 0;
        }
    }
}
