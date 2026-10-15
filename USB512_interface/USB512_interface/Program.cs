using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    internal static class Program
    {
        internal static NetworkTimeClock NetworkClock { get; private set; }
        internal static string NetworkTimeSettingsError { get; private set; }
        private const uint ErrorInsufficientBuffer = 122;
        private const uint CpuSetInformationTypeCpuSet = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemCpuSetInformation(
            IntPtr information, uint bufferLength, out uint returnedLength,
            IntPtr process, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessDefaultCpuSets(
            IntPtr process, uint[] cpuSetIds, uint cpuSetIdCount);
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            ConfigureAcquisitionScheduling();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (NetworkClock = new NetworkTimeClock()) {
                try {
                    var settings = Properties.Settings.Default;
                    if (settings.NetworkTimeUpgradeRequired) {
                        settings.Upgrade(); settings.NetworkTimeUpgradeRequired = false; settings.Save();
                    }
                    NetworkClock.Configure(settings.NetworkTimeEnabled, settings.NetworkTimeServer, settings.NetworkTimeAutomatic);
                }
                catch (Exception ex) {
                    NetworkTimeSettingsError = "Using default server because preferences could not be loaded: " + ex.Message;
                    NetworkClock.Configure(true, NetworkTimeClock.DefaultServer, true);
                }
                NetworkClock.Start();
                Application.Run(new Form1());
            }
        }

        // High priority reduces user-mode scheduling latency without using
        // RealTime, which could starve the USB driver's kernel/DPC work.
        private static void ConfigureAcquisitionScheduling()
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch (Exception)
            {
                // Capture threads still request their own priority below.
            }

            try
            {
                uint bytes;
                if (GetSystemCpuSetInformation(IntPtr.Zero, 0, out bytes,
                                               IntPtr.Zero, 0) || bytes == 0)
                    return;
                if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                    return;

                IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
                try
                {
                    if (!GetSystemCpuSetInformation(buffer, bytes, out bytes,
                                                    IntPtr.Zero, 0))
                        return;

                    byte bestEfficiencyClass = Byte.MaxValue;
                    var preferredCpuSets = new List<uint>();
                    int offset = 0;
                    while (offset + 20 <= bytes)
                    {
                        int size = Marshal.ReadInt32(buffer, offset);
                        int type = Marshal.ReadInt32(buffer, offset + 4);
                        if (size < 20 || offset + size > bytes)
                            break;

                        if (type == CpuSetInformationTypeCpuSet)
                        {
                            uint id = unchecked((uint)Marshal.ReadInt32(buffer, offset + 8));
                            byte efficiencyClass = Marshal.ReadByte(buffer, offset + 18);
                            if (efficiencyClass < bestEfficiencyClass)
                            {
                                bestEfficiencyClass = efficiencyClass;
                                preferredCpuSets.Clear();
                            }
                            if (efficiencyClass == bestEfficiencyClass)
                                preferredCpuSets.Add(id);
                        }
                        offset += size;
                    }

                    if (preferredCpuSets.Count != 0)
                    {
                        SetProcessDefaultCpuSets(Process.GetCurrentProcess().Handle,
                                                 preferredCpuSets.ToArray(),
                                                 (uint)preferredCpuSets.Count);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (EntryPointNotFoundException)
            {
                // CPU sets are unavailable on older Windows releases.
            }
            catch (Exception)
            {
                // A topology-query failure must never prevent acquisition.
            }
        }
    }
}
