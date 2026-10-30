using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace USB512_interface
{
    internal struct USB512ClockSyncResult
    {
        internal int EstimatedOffsetMilliseconds;
        internal double RoundTripMilliseconds;
        internal uint DeviceMillisecondsSinceMidnight;
    }

    internal sealed class USB512WinUsbDevice : IDisposable
    {
        internal const byte RequestStop = 0x41;
        internal const byte RequestGetStatus = 0x42;
        internal const byte RequestSetStreamRate = 0x46;
        internal const byte RequestStartStreams = 0x47;
        internal const byte RequestSetMicrophoneRate32 = 0x48;
        internal const byte RequestSetCameraExposure = 0x4C;
        internal const byte RequestSetCameraProfile = 0x50;
        internal const byte RequestRetryInflight = 0x51;
        internal const byte RequestPrepareInflightReplay = 0x52;
        internal const byte RequestSetTimeOfDayMilliseconds = 0x4D;
        internal const byte RequestGetTimeOfDayMilliseconds = 0x4E;
        internal const byte RequestEnterDfu = 0x4F;

        internal const byte StreamEphys = 1;
        internal const byte StreamImu = 2;
        internal const byte StreamCamera = 3;
        internal const byte StreamMicrophone = 4;
        internal const byte StreamMisc = 5;
        internal const byte StreamNeuropixels = 6;

        internal const byte FlagFrameStart = 0x01;
        internal const byte FlagFrameEnd = 0x02;
        internal const byte FlagPayloadLengthInSequence = 0x04;

        internal const ushort StreamMaskEphys = 1 << 0;
        internal const ushort StreamMaskImu = 1 << 1;
        internal const ushort StreamMaskCamera = 1 << 2;
        internal const ushort StreamMaskMicrophone = 1 << 3;
        internal const ushort StreamMaskMisc = 1 << 4;
        internal const ushort StreamMaskNeuropixels = 1 << 5;

        internal const byte CameraProfileNormal50Hz = 0;
        internal const byte CameraProfileSafe30Hz = 1;

        private const uint PipeTransferTimeout = 0x03;
        private const uint AllowPartialReads = 0x05;
        private const uint AutoClearStall = 0x02;
        // WinUSB considers a device idle when its only outstanding transfers
        // are bulk-IN reads.  USB512 recording is intentionally all bulk-IN
        // after startup, so the default policy can selectively suspend an
        // otherwise active acquisition and surface ERROR_GEN_FAILURE (31).
        private const uint AutoSuspend = 0x81;
        private const int StatusBytesVersion6 = 41 * sizeof(uint);
        private const int StatusFlagsWord = 2;
        private const int StatusLastCodeWord = 3;
        private const int StatusEnabledStreamsWord = 18;
        private const uint StatusFlagStreaming = 0x00000002;
        private const uint StatusFlagReplayActive = 0x00004000;
        private const uint StatusFlagReplayPrepared = 0x00008000;
        private const ushort DfuMagicValue = 0xD1F0;
        private const ushort DfuMagicIndex = 0x2E0F;

        private readonly SafeFileHandle deviceHandle;
        // WinUSB bulk reads may remain queued while EP0 control transfers run,
        // but two simultaneous control transfers on the same interface can
        // race EP0 state.  The RTC sync thread and preview exposure controller
        // are independent callers, so serialize every vendor transfer here.
        private readonly object controlTransferLock = new object();
        private IntPtr winUsb;
        // A broken WinUSB driver can block forever while tearing down a
        // poisoned bulk pipe.  Keep that native cleanup off the capture thread
        // after first closing the OS file handle, so a new shared handle can
        // resume the retained device stream immediately.
        private bool recoveryCleanupQueued;

        private USB512WinUsbDevice(string path, SafeFileHandle deviceHandle, IntPtr winUsb, byte bulkInPipe,
            bool isNeuropixels)
        {
            Path = path;
            this.deviceHandle = deviceHandle;
            this.winUsb = winUsb;
            BulkInPipe = bulkInPipe;
            IsNeuropixels = isNeuropixels;
        }

        internal string Path { get; private set; }
        internal byte BulkInPipe { get; private set; }
        internal bool IsNeuropixels { get; private set; }

        internal static IList<string> EnumerateDevicePaths()
        {
            var paths = new List<string>(USB512WinUsbNative.EnumerateInterfacePaths(
                USB512WinUsbNative.InterfaceGuid));
            foreach (string path in USB512WinUsbNative.EnumerateInterfacePaths(
                USB512WinUsbNative.NeuropixelsInterfaceGuid))
            {
                // The XE_NP probe uses this interface GUID too, but its packet
                // format and control requests are different.  It is enumerated
                // by XeNpWinUsbDevice, not as a USB512 stream source.
                if (!IsNeuropixelsPath(path) && !paths.Contains(path))
                    paths.Add(path);
            }
            // B387 is the independent XE_NP updater and B388 is MSC; neither
            // is a USB512 stream source even when an interface GUID overlaps.
            paths.RemoveAll(delegate(string path) {
                return path.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0 ||
                       path.IndexOf("vid_1209&pid_b387",StringComparison.OrdinalIgnoreCase)>=0 ||
                       path.IndexOf("vid_1209&pid_b388",StringComparison.OrdinalIgnoreCase)>=0;
            });
            return paths;
        }

        internal static USB512WinUsbDevice Open(string path)
        {
            var handle = USB512WinUsbNative.CreateFile(
                path,
                USB512WinUsbNative.GenericRead | USB512WinUsbNative.GenericWrite,
                USB512WinUsbNative.FileShareRead | USB512WinUsbNative.FileShareWrite,
                IntPtr.Zero,
                USB512WinUsbNative.OpenExisting,
                USB512WinUsbNative.FileAttributeNormal | USB512WinUsbNative.FileFlagOverlapped,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile for USB512 WinUSB failed");
            }

            IntPtr winUsb;
            if (!USB512WinUsbNative.WinUsbInitialize(handle, out winUsb, path))
            {
                handle.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize failed");
            }

            try
            {
                USB512WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor;
                if (!USB512WinUsbNative.WinUsbQueryInterfaceSettings(winUsb, 0, out descriptor))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings failed");
                }

                byte bulkInPipe = 0;
                for (byte index = 0; index < descriptor.bNumEndpoints; index++)
                {
                    USB512WinUsbNative.WINUSB_PIPE_INFORMATION pipe;
                    if (USB512WinUsbNative.WinUsbQueryPipe(winUsb, 0, index, out pipe) &&
                        pipe.PipeType == USB512WinUsbNative.USBD_PIPE_TYPE.Bulk &&
                        (pipe.PipeId & 0x80) != 0)
                    {
                        bulkInPipe = pipe.PipeId;
                        break;
                    }
                }
                if (bulkInPipe == 0)
                {
                    throw new InvalidOperationException("USB512 WinUSB bulk-IN endpoint was not found.");
                }

                var device = new USB512WinUsbDevice(
                    path, handle, winUsb, bulkInPipe, IsNeuropixelsPath(path));
                // A continuously-recording device must remain in D0 even
                // during the brief interval between queued bulk-IN reads.
                device.SetAutoSuspend(false);
                device.SetPipeTimeout(10000); // camera VCC/bootstrap startup window
                // The packet stream deliberately contains short transfers: every
                // IMU record and the final chunk of each camera frame is shorter
                // than 512 bytes.  Let the read complete at those boundaries;
                // the logger already frames arbitrary-sized byte completions.
                //
                // The packet stream deliberately contains short transfers: every
                // IMU record and the final chunk of each camera frame is shorter
                // than 512 bytes.  Let the read complete at those boundaries;
                // the logger already frames arbitrary-sized byte completions.
                device.SetAllowPartialReads(true);
                // Let WinUSB clear a transient bulk endpoint STALL internally.
                // Without this policy the same condition reaches every queued
                // request as ERROR_GEN_FAILURE (31), forcing a destructive
                // abort/reset cycle in the recorder.
                device.SetAutoClearStall(true);
                return device;
            }
            catch
            {
                USB512WinUsbNative.WinUsbFree(winUsb);
                handle.Dispose();
                throw;
            }
        }

        internal void ConfigureRates(ushort streams, byte cameraProfile, ushort cameraRateHz)
        {
            if (IsNeuropixels)
                return;
            if ((streams & StreamMaskEphys) != 0)
                VendorOut(RequestSetStreamRate, StreamEphys, 20000);
            if ((streams & StreamMaskImu) != 0)
                VendorOut(RequestSetStreamRate, StreamImu, 1250);
            if ((streams & StreamMaskCamera) != 0)
            {
                VendorOut(RequestSetStreamRate, StreamCamera, cameraRateHz);
                VendorOut(RequestSetCameraProfile, cameraProfile, 0);
            }
            if ((streams & StreamMaskMicrophone) != 0)
                VendorOut(RequestSetMicrophoneRate32, (ushort)(240000 >> 16), (ushort)(240000 & 0xFFFF));
        }

        // Camera is opt-in because a board assembled without the NanEye can
        // validly reject camera startup.  Asking the firmware to start that
        // unavailable source stalls START_STREAMS, which WinUSB reports as
        // ERROR_GEN_FAILURE (31) for the entire recording.
        // Returns true only when the camera source was accepted.  Camera remains
        // the normal start mode; a board whose camera bootstrap is unavailable
        // falls back to the other requested streams instead of losing the whole
        // recording to ERROR_GEN_FAILURE (31).
        internal bool StartDefaultStreams(bool includeCamera)
        {
            ushort streams = (ushort)(StreamMaskEphys | StreamMaskImu | StreamMaskMicrophone);
            if (includeCamera)
                streams |= StreamMaskCamera;
            return (StartStreams(streams, CameraProfileNormal50Hz, 50) & StreamMaskCamera) != 0;
        }

        // Apply a complete source selection atomically at recording start.  A
        // camera-less board may reject the optional camera source; retry the
        // exact requested non-camera subset instead of dropping IMU/ephys/ADC.
        internal ushort StartStreams(ushort requestedStreams, byte cameraProfile)
        {
            return StartStreams(
                requestedStreams,
                cameraProfile,
                cameraProfile == CameraProfileSafe30Hz ? (ushort)30 : (ushort)50);
        }

        internal ushort StartStreams(ushort requestedStreams, byte cameraProfile, ushort cameraRateHz)
        {
            if (IsNeuropixels)
            {
                VendorOut(RequestStartStreams, StreamMaskNeuropixels, 0);
                return WaitForNeuropixelsStarted();
            }

            const ushort supported = StreamMaskEphys | StreamMaskImu | StreamMaskCamera | StreamMaskMicrophone;
            ushort streams = (ushort)(requestedStreams & supported);
            if (streams == 0)
                throw new ArgumentException("Select at least one data stream.", "requestedStreams");
            if (cameraProfile != CameraProfileNormal50Hz && cameraProfile != CameraProfileSafe30Hz)
                throw new ArgumentOutOfRangeException("cameraProfile");
            if (cameraRateHz == 0 || cameraRateHz > 50 ||
                (cameraProfile == CameraProfileSafe30Hz && cameraRateHz > 30))
                throw new ArgumentOutOfRangeException("cameraRateHz");

            ConfigureRates(streams, cameraProfile, cameraRateHz);
            if ((streams & StreamMaskCamera) != 0)
            {
                try
                {
                    VendorOut(RequestStartStreams, streams, 0);
                    // Camera selection may perform a bounded cold bootstrap on
                    // the device.  Give that probe time to complete; all other
                    // stream starts retain the normal short wait below.
                    ushort activeStreams = WaitForStartedStreams(4000);
                    if ((activeStreams & StreamMaskCamera) != 0)
                    {
                        // Send camera control only after firmware has confirmed
                        // that this module was selected.  On a camera-less or
                        // failed-camera start, this avoids an expected EP0 stall
                        // (Win32 error 31) before the normal non-camera fallback.
                        // The request is latched at the next frame boundary, so
                        // it cannot interrupt initial camera bootstrap.
                        SetCameraExposure(0, 1);
                    }
                    return activeStreams;
                }
                catch (Win32Exception exception) when
                    (exception.NativeErrorCode == USB512WinUsbNative.ErrorGenFailure)
                {
                    // A camera-not-ready board stalls the all-stream request.  The
                    // device accepts a fresh non-camera START after that stall.
                }
                streams = (ushort)(streams & ~StreamMaskCamera);
                if (streams == 0)
                    throw new InvalidOperationException(
                        "The requested camera stream is unavailable and no fallback stream was selected.");
            }

            VendorOut(RequestStartStreams, streams, 0);
            return WaitForStartedStreams();
        }

        private ushort WaitForStartedStreams(int timeoutMilliseconds = 1000)
        {
            int attempts = Math.Max(1, timeoutMilliseconds / 10);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                byte[] status = VendorIn(RequestGetStatus, StatusBytesVersion6);
                uint flags = BitConverter.ToUInt32(status, StatusFlagsWord * sizeof(uint));
                uint lastCode = BitConverter.ToUInt32(status, StatusLastCodeWord * sizeof(uint));
                if ((flags & StatusFlagStreaming) != 0)
                    return (ushort)BitConverter.ToUInt32(status, StatusEnabledStreamsWord * sizeof(uint));
                if (lastCode != 0)
                    throw new InvalidOperationException("USB512 rejected the selected stream set (status 0x" + lastCode.ToString("X8") + ").");
                Thread.Sleep(10);
            }
            throw new TimeoutException(string.Format(
                "USB512 did not start the selected streams within {0} ms.",
                timeoutMilliseconds));
        }

        private void WaitForStoppedStreams()
        {
            if (IsNeuropixels)
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    if (!IsNeuropixelsStreaming())
                        return;
                    Thread.Sleep(5);
                }
                return;
            }
            for (int attempt = 0; attempt < 40; attempt++)
            {
                byte[] status = VendorIn(RequestGetStatus, StatusBytesVersion6);
                uint flags = BitConverter.ToUInt32(status, StatusFlagsWord * sizeof(uint));
                if ((flags & StatusFlagStreaming) == 0)
                    return;
                Thread.Sleep(5);
            }
        }

        // The firmware keeps a tick-based clock between these one-Hz updates.
        // Values are local milliseconds since the start of the current day.
        internal USB512ClockSyncResult SyncTimeOfDay()
        {
            if (IsNeuropixels)
            {
                return new USB512ClockSyncResult
                {
                    EstimatedOffsetMilliseconds = 0,
                    RoundTripMilliseconds = 0.0,
                    DeviceMillisecondsSinceMidnight = 0
                };
            }
            // Keep SET and GET adjacent.  Otherwise a preview exposure update
            // could interleave here and distort the RTT/offset measurement.
            lock (controlTransferLock)
            {
                DateTime hostBefore = DateTime.Now;
                long startTicks = Stopwatch.GetTimestamp();
                uint milliseconds = MillisecondsSinceMidnight(hostBefore);
                VendorOut(
                    RequestSetTimeOfDayMilliseconds,
                    (ushort)(milliseconds & 0xFFFFU),
                    (ushort)(milliseconds >> 16));
                uint deviceMilliseconds = GetTimeOfDayMilliseconds();
                DateTime hostAfter = DateTime.Now;
                double roundTripMilliseconds =
                    (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                DateTime hostMidpoint = hostBefore.AddTicks((hostAfter.Ticks - hostBefore.Ticks) / 2);

                return new USB512ClockSyncResult
                {
                    EstimatedOffsetMilliseconds = SignedDayDifference(
                        deviceMilliseconds, MillisecondsSinceMidnight(hostMidpoint)),
                    RoundTripMilliseconds = roundTripMilliseconds,
                    DeviceMillisecondsSinceMidnight = deviceMilliseconds
                };
            }
        }

        internal void Stop()
        {
            if (winUsb == IntPtr.Zero)
            {
                return;
            }
            try
            {
                VendorOut(RequestStop, 0, 0);
                // STOP is latched in the USB IRQ and completed by firmware
                // foreground service.  Let that teardown finish before aborting
                // all queued reads, otherwise the next console start can inherit
                // a stale device-side bulk transfer.
                WaitForStoppedStreams();
            }
            catch
            {
                // A disconnected device is already stopped from the host's view.
            }
            USB512WinUsbNative.WinUsbAbortPipe(winUsb, BulkInPipe);
        }

        // The device acknowledges this guarded command, then leaves WinUSB
        // and jumps to the STM32U595 ROM bootloader.  Its next USB identity is
        // ST's DFU device, so this WinUSB handle is no longer usable.
        internal void EnterDfu()
        {
            if (IsNeuropixels)
                throw new NotSupportedException(
                    "Neuropixels firmware does not implement the USB512 DFU request; use SWD or the STM32 bootloader directly.");
            VendorOut(RequestEnterDfu, DfuMagicValue, DfuMagicIndex);
        }

        // CE64-compatible NanEye REG0 fields.  The firmware latches this
        // request and writes the sensor only at a frame boundary.
        internal void SetCameraExposure(byte rowsInReset, byte rampGain)
        {
            if (rampGain > 3)
                throw new ArgumentOutOfRangeException("rampGain");
            VendorOut(RequestSetCameraExposure, rowsInReset, rampGain);
        }

        internal int Read(byte[] buffer)
        {
            uint transferred;
            if (USB512WinUsbNative.WinUsbReadPipe(winUsb, BulkInPipe, buffer, (uint)buffer.Length, out transferred, IntPtr.Zero))
            {
                return (int)transferred;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == USB512WinUsbNative.ErrorSemTimeout || error == USB512WinUsbNative.ErrorOperationAborted)
            {
                return 0;
            }
            throw new Win32Exception(error, "WinUsb_ReadPipe failed");
        }

        // Keep enough asynchronous requests in the USB stack that a single
        // completed 32 KiB transfer never leaves the bulk-IN endpoint idle
        // while the application prepares the next read.  This is important at
        // the ~29 MB/s mixed-stream rate: a single synchronous ReadPipe loses
        // several percent of the available USB service opportunities.
        internal USB512WinUsbQueuedReader CreateQueuedReader(int bufferBytes, int depth)
        {
            if (winUsb == IntPtr.Zero)
                throw new ObjectDisposedException("USB512WinUsbDevice");
            return new USB512WinUsbQueuedReader(
                winUsb, BulkInPipe, bufferBytes, depth,
                PrepareInflightReplay, RequestInflightReplay, WaitForInflightReplay);
        }

        // Used only after the application has abandoned a poisoned WinUSB
        // handle.  If firmware is still streaming, retain its sequence epoch
        // and ask for the same chronological replay rather than restarting
        // acquisition and manufacturing a recording discontinuity.
        internal bool TryGetActiveStreams(out ushort streams)
        {
            if (IsNeuropixels)
            {
                streams = StreamMaskNeuropixels;
                return IsNeuropixelsStreaming();
            }
            byte[] status = VendorIn(RequestGetStatus, StatusBytesVersion6);
            uint flags = BitConverter.ToUInt32(status, StatusFlagsWord * sizeof(uint));
            streams = (ushort)BitConverter.ToUInt32(
                status, StatusEnabledStreamsWord * sizeof(uint));
            return (flags & StatusFlagStreaming) != 0 && streams != 0;
        }

        // A continuous bulk-IN stream alone is not activity for every WinUSB
        // power-management path. A tiny EP0 status read once per second keeps
        // the interface and an intervening hub in D0.  It is advisory only:
        // during a bulk ERROR_GEN_FAILURE recovery WinUSB can reject that
        // unrelated status request even though queued bulk reads and the
        // device's retained replay are healthy.  Do not turn that transient
        // EP0 contention into a recording restart; the sequenced bulk path
        // remains the authoritative continuity monitor.
        internal void KeepDeviceAwake()
        {
            try
            {
                VendorIn(RequestGetStatus, StatusBytesVersion6);
            }
            catch (Win32Exception exception) when
                (exception.NativeErrorCode == USB512WinUsbNative.ErrorGenFailure ||
                 exception.NativeErrorCode == USB512WinUsbNative.ErrorOperationAborted)
            {
                // The next scheduled status request restores the keep-awake
                // hint.  Do not disturb the live bulk-IN replay for this.
            }
        }

        private void RequestInflightReplay()
        {
            if (IsNeuropixels)
                throw new NotSupportedException("Neuropixels firmware does not retain USB replay history.");
            // This is issued only after WinUsb_AbortPipe and WinUsb_ResetPipe
            // have completed. The queued reader submits clean reads immediately
            // after this command, before waiting for its acknowledgement: a
            // USB IN endpoint needs host requests available to complete the
            // first retained transfer.
            VendorOut(RequestRetryInflight, 0, 0);
        }

        private void WaitForInflightReplay()
        {
            if (IsNeuropixels)
                throw new NotSupportedException("Neuropixels firmware does not retain USB replay history.");
            // The device has finite retained history.  Do not turn a poisoned
            // WinUSB handle into a data loss by spending hundreds of
            // milliseconds polling it: a normal STM32 foreground pass arms
            // replay on its next service turn.  If it is not armed promptly, the
            // caller abandons this handle and uses the fast reopened-handle
            // retained-replay path instead.
            // A high-speed hub can defer the control acknowledgement and first
            // replay IN token beyond 30 ms while it retires the failed URB.
            // Treating that normal recovery latency as a second pipe failure
            // creates a reopen storm, which is what previously overran the
            // retained producer tail.  The 60 ms bound remains inside the
            // 1.5 MiB ephys retention window and is only reached after fresh
            // bulk reads were already posted.
            const int ReplayArmTimeoutMilliseconds = 60;
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < ReplayArmTimeoutMilliseconds)
            {
                byte[] status = VendorIn(RequestGetStatus, StatusBytesVersion6);
                uint flags = BitConverter.ToUInt32(status, StatusFlagsWord * sizeof(uint));
                uint lastCode = BitConverter.ToUInt32(status, StatusLastCodeWord * sizeof(uint));
                if ((flags & StatusFlagReplayActive) != 0)
                    return;
                if (lastCode != 0)
                    break;
                // Sleep(1) commonly rounds to an entire Windows scheduler
                // quantum (~15.6 ms), which can consume this entire recovery
                // budget in two polls. Yield without imposing that delay.
                Thread.Sleep(0);
            }
            throw new TimeoutException("USB512 did not arm retained replay within the no-loss recovery window.");
        }

        // Freeze the current retained window before AbortPipe.  This closes the
        // timing hole where WinUSB spends milliseconds disposing a poisoned
        // handle while firmware continues advancing and overwriting history.
        // Failure is surfaced to the capture recovery path rather than risking
        // an unprotected AbortPipe that could silently discard samples.
        private void PrepareInflightReplay()
        {
            if (IsNeuropixels)
                throw new NotSupportedException("Neuropixels firmware does not retain USB replay history.");
            // The firmware retains more than 84 ms of raw ephys data.  A hub
            // can defer a control completion well beyond its failed bulk URB;
            // keep the working handle for 70 ms before considering a
            // destructive reopen, rather than manufacturing a sequence loss
            // from a merely delayed PREPARE acknowledgement.
            const int PrepareTimeoutMilliseconds = 70;
            VendorOut(RequestPrepareInflightReplay, 0, 0);
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < PrepareTimeoutMilliseconds)
            {
                byte[] status = VendorIn(RequestGetStatus, StatusBytesVersion6);
                uint flags = BitConverter.ToUInt32(status, StatusFlagsWord * sizeof(uint));
                uint lastCode = BitConverter.ToUInt32(status, StatusLastCodeWord * sizeof(uint));
                if ((flags & StatusFlagReplayPrepared) != 0)
                    return;
                if (lastCode != 0)
                    throw new InvalidOperationException(
                        "USB512 rejected the retained-replay preparation request.");
                Thread.Sleep(0);
            }
            throw new TimeoutException(
                "USB512 did not freeze its retained replay before the WinUSB pipe was abandoned.");
        }

        public void Dispose()
        {
            bool closeDeviceHandle = true;
            lock (controlTransferLock)
            {
                if (winUsb != IntPtr.Zero)
                {
                    USB512WinUsbNative.WinUsbFree(winUsb);
                    winUsb = IntPtr.Zero;
                }
                else if (recoveryCleanupQueued)
                {
                    closeDeviceHandle = false;
                }
            }
            if (closeDeviceHandle)
                deviceHandle.Dispose();
        }

        // The bulk pipe has already failed and its replay window has been
        // frozen.  Closing the file handle first cancels every old URB and
        // releases the Windows interface for a new CreateFile immediately.
        // WinUsbFree is then allowed to finish on a background worker; it can
        // block indefinitely on the poisoned driver path, but must never keep
        // the recording thread from reopening the device.  The reader's pinned
        // OVERLAPPED storage remains alive until that free has completed.
        internal void DisposeForRecovery(USB512WinUsbQueuedReader abandonedReader)
        {
            IntPtr abandonedWinUsb;

            lock (controlTransferLock)
            {
                if (winUsb == IntPtr.Zero || recoveryCleanupQueued)
                    return;
                abandonedWinUsb = winUsb;
                winUsb = IntPtr.Zero;
                recoveryCleanupQueued = true;
            }

            // This CloseHandle ordering is intentional.  On ERROR_GEN_FAILURE,
            // WinUsbFree can wait behind a request that AbortPipe did not
            // retire.  Leaving the file open while it waits prevents every
            // later CreateFile from recovering the recording.
            deviceHandle.Dispose();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    USB512WinUsbNative.WinUsbFree(abandonedWinUsb);
                }
                finally
                {
                    if (abandonedReader != null)
                        abandonedReader.ReleaseAfterRecoveryClose();
                }
            });
        }

        private void SetPipeTimeout(uint milliseconds)
        {
            if (!USB512WinUsbNative.WinUsbSetPipePolicy(
                winUsb, BulkInPipe, PipeTransferTimeout, sizeof(uint), ref milliseconds))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_SetPipePolicy timeout failed");
            }
        }

        private void SetAutoSuspend(bool enabled)
        {
            byte value = enabled ? (byte)1 : (byte)0;
            if (!USB512WinUsbNative.WinUsbSetPowerPolicy(
                winUsb, AutoSuspend, sizeof(byte), ref value))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "WinUsb_SetPowerPolicy auto-suspend failed");
            }
        }

        private void SetAllowPartialReads(bool enabled)
        {
            uint value = enabled ? 1U : 0U;
            if (!USB512WinUsbNative.WinUsbSetPipePolicy(
                winUsb, BulkInPipe, AllowPartialReads, sizeof(uint), ref value))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_SetPipePolicy partial reads failed");
            }
        }

        private void SetAutoClearStall(bool enabled)
        {
            uint value = enabled ? 1U : 0U;
            if (!USB512WinUsbNative.WinUsbSetPipePolicy(
                winUsb, BulkInPipe, AutoClearStall, sizeof(uint), ref value))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_SetPipePolicy auto-clear-stall failed");
            }
        }

        private void VendorOut(byte request, ushort value, ushort index)
        {
            lock (controlTransferLock)
            {
                if (winUsb == IntPtr.Zero)
                    throw new ObjectDisposedException("USB512WinUsbDevice");

                var setup = new USB512WinUsbNative.WINUSB_SETUP_PACKET
                {
                    RequestType = 0x40,
                    Request = request,
                    Value = value,
                    Index = index,
                    Length = 0
                };
                uint transferred;
                if (!USB512WinUsbNative.WinUsbControlTransfer(winUsb, setup, new byte[0], 0, out transferred, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "USB512 vendor control transfer failed");
                }
            }
        }

        private uint GetTimeOfDayMilliseconds()
        {
            byte[] response = VendorIn(RequestGetTimeOfDayMilliseconds, sizeof(uint));
            if (response.Length != sizeof(uint))
                throw new InvalidOperationException("USB512 returned an invalid RTC time response.");
            return (uint)(response[0] | (response[1] << 8) |
                          (response[2] << 16) | (response[3] << 24));
        }

        private byte[] VendorIn(byte request, int length)
        {
            lock (controlTransferLock)
            {
                if (winUsb == IntPtr.Zero)
                    throw new ObjectDisposedException("USB512WinUsbDevice");

                byte[] response = new byte[length];
                var setup = new USB512WinUsbNative.WINUSB_SETUP_PACKET
                {
                    RequestType = 0xC0,
                    Request = request,
                    Value = 0,
                    Index = 0,
                    Length = (ushort)length
                };
                uint transferred;
                if (!USB512WinUsbNative.WinUsbControlTransfer(
                    winUsb, setup, response, (uint)response.Length, out transferred, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "USB512 vendor control read failed");
                }
                if (transferred != (uint)response.Length)
                    throw new InvalidOperationException("USB512 vendor control read was truncated.");
                return response;
            }
        }

        private ushort WaitForNeuropixelsStarted()
        {
            // The probe reset/clock qualification and paired-DMA phase lock
            // occur after the control request.  Unlike USB512's synthetic
            // producers, that first capture session can take several seconds.
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                if (IsNeuropixelsStreaming())
                    return StreamMaskNeuropixels;
                Thread.Sleep(10);
            }
            throw new TimeoutException("Neuropixels capture did not start within 10 seconds.");
        }

        /* The Neuropixels firmware exposes its existing diagnostic block on
         * GET_STATUS: word 5 is the stream-active flag.  Keep this deliberately
         * separate from the USB512 status layout. */
        private bool IsNeuropixelsStreaming()
        {
            byte[] status = VendorIn(RequestGetStatus, 6 * sizeof(uint));
            return BitConverter.ToUInt32(status, 5 * sizeof(uint)) != 0;
        }

        private static bool IsNeuropixelsPath(string path)
        {
            return path != null && path.IndexOf("vid_1209&pid_b386",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static uint MillisecondsSinceMidnight(DateTime time)
        {
            return (uint)time.TimeOfDay.TotalMilliseconds;
        }

        private static int SignedDayDifference(uint deviceMilliseconds, uint hostMilliseconds)
        {
            const int HalfDayMilliseconds = 12 * 60 * 60 * 1000;
            const int DayMilliseconds = 24 * 60 * 60 * 1000;
            int difference = unchecked((int)(deviceMilliseconds - hostMilliseconds));
            if (difference > HalfDayMilliseconds)
                difference -= DayMilliseconds;
            else if (difference < -HalfDayMilliseconds)
                difference += DayMilliseconds;
            return difference;
        }
    }

    // Owns pinned request buffers for the full lifetime of every native
    // OVERLAPPED request.  The capture thread copies a completed block to its
    // logger-owned buffer before re-submitting this slot, so disk logging and
    // camera previewing can never race a USB DMA write.
    internal sealed class USB512WinUsbQueuedReader : IDisposable
    {
        private const uint ReadTimeoutMilliseconds = 10000;

        private sealed class ReadSlot : IDisposable
        {
            internal ReadSlot(int bufferBytes)
            {
                Buffer = new byte[bufferBytes];
                Pin = GCHandle.Alloc(Buffer, GCHandleType.Pinned);
                Event = USB512WinUsbNative.CreateEvent(IntPtr.Zero, true, false, null);
                if (Event == IntPtr.Zero)
                {
                    Pin.Free();
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateEvent for WinUSB read failed");
                }
                cleanOverlapped = new USB512WinUsbNative.NATIVE_OVERLAPPED { Event = Event };
            }

            internal readonly byte[] Buffer;
            internal readonly GCHandle Pin;
            internal readonly IntPtr Event;
            // Box once during startup, rather than once on every read rearm.
            private readonly object cleanOverlapped;
            // WinUSB retains this pointer until an asynchronous request
            // completes. A struct field in this managed ReadSlot can move
            // during a GC, leaving WinUSB to write into an old address.
            internal IntPtr OverlappedPointer;
            internal bool Pending;
            internal bool CompletedSynchronously;
            internal uint CompletedBytes;

            internal IntPtr BufferPointer
            {
                get { return Pin.AddrOfPinnedObject(); }
            }

            internal void PrepareOverlapped()
            {
                if (OverlappedPointer == IntPtr.Zero)
                    OverlappedPointer = Marshal.AllocHGlobal(
                        Marshal.SizeOf(typeof(USB512WinUsbNative.NATIVE_OVERLAPPED)));

                Marshal.StructureToPtr(
                    cleanOverlapped,
                    OverlappedPointer, false);
            }

            public void Dispose()
            {
                if (OverlappedPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(OverlappedPointer);
                    OverlappedPointer = IntPtr.Zero;
                }
                if (Event != IntPtr.Zero)
                    USB512WinUsbNative.CloseHandle(Event);
                if (Pin.IsAllocated)
                    Pin.Free();
            }
        }

        private readonly IntPtr winUsb;
        private readonly byte pipeId;
        private readonly ReadSlot[] slots;
        private readonly Action prepareInflightReplay;
        private readonly Action requestInflightReplay;
        private readonly Action waitForInflightReplay;
        private readonly bool strictFifo;
        private bool submitFailed;
        // AbortPipe normally signals every queued read immediately.  A poisoned
        // WinUSB handle can violate that assumption; bound the wait so the
        // console can close the interface and perform its hard recovery.
        // Keep the hard-reopen fallback inside the STM32 retained-stream
        // window. Normal AbortPipe completion is immediate; 25 ms is reserved
        // only for a driver that is already no longer making progress.
        private const int AbortCompletionTimeoutMilliseconds = 25;
        private int nextSlot;
        private bool disposed;

        internal int RecoveryCount { get; private set; }
        internal int CompletionFailureRecoveryCount { get; private set; }
        internal int SubmitFailureRecoveryCount { get; private set; }
        // Optional borrowed-pipe owners need an explicit positive retirement
        // result. Dispose historically retains native slots on uncertainty.
        internal bool RetirementConfirmed { get; private set; }
        internal void Retire()
        {
            if (RetirementConfirmed) return;
            if (disposed) throw new IOException("WinUSB reader disposal did not prove native retirement.");
            RetirePendingReads();
            RetirementConfirmed = true;
        }

        // The capture-thread integrity gate calls this immediately when stream
        // packet numbering detects a missing transfer.  Recover before the
        // logger commits the affected block, while firmware still retains it.
        internal void RecoverForSequenceGap()
        {
            if (disposed)
                throw new ObjectDisposedException("USB512WinUsbQueuedReader");
            RecoverPipe();
        }

        // Used before abandoning a handle that cannot be reset in place. It
        // freezes firmware's replay window while this reader is still valid;
        // Dispose may then take a bounded time without aging acquisition data.
        internal void PrepareForReopen()
        {
            if (disposed)
                return;
            if (prepareInflightReplay != null)
                prepareInflightReplay();
        }

        internal USB512WinUsbQueuedReader(
            IntPtr winUsb, byte pipeId, int bufferBytes, int depth,
            Action prepareInflightReplay, Action requestInflightReplay,
            Action waitForInflightReplay, bool strictFifo = false)
        {
            if (bufferBytes <= 0)
                throw new ArgumentOutOfRangeException("bufferBytes");
            if (depth <= 0)
                throw new ArgumentOutOfRangeException("depth");

            this.winUsb = winUsb;
            this.pipeId = pipeId;
            this.prepareInflightReplay = prepareInflightReplay;
            this.requestInflightReplay = requestInflightReplay;
            this.waitForInflightReplay = waitForInflightReplay;
            this.strictFifo = strictFifo;
            slots = new ReadSlot[depth];
            try
            {
                for (int index = 0; index < slots.Length; index++)
                {
                    slots[index] = new ReadSlot(bufferBytes);
                    if(!Submit(slots[index]))
                        throw new Win32Exception(USB512WinUsbNative.ErrorGenFailure,"Could not arm all WinUSB reads");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Returns zero for the same benign stop/timeout cases as the previous
        // synchronous reader.  A timeout leaves its request in flight and the
        // next call examines a different slot.
        internal int ReadInto(byte[] destination)
        {
            if (disposed || RetirementConfirmed)
                throw new ObjectDisposedException("USB512WinUsbQueuedReader");
            if (destination == null)
                throw new ArgumentNullException("destination");
            if(strictFifo && submitFailed)
                throw new IOException("WILDX USB requeue failed; recording stopped without replay/reset.");

            ReadSlot slot = slots[nextSlot];
            if(!strictFifo)nextSlot = (nextSlot + 1) % slots.Length;
            if (!slot.Pending)
            {
                if (!Submit(slot))
                {
                    SubmitFailureRecoveryCount++;
                    PrepareForReopen();
                    throw new Win32Exception(USB512WinUsbNative.ErrorGenFailure,
                        "WinUSB bulk-IN request failed; reopening interface");
                }
            }

            uint bytesRead;
            if (slot.CompletedSynchronously)
            {
                bytesRead = slot.CompletedBytes;
            }
            else
            {
                // XE_NP polls briefly without retiring or rotating a pending
                // request. Rotating on timeout would reorder the byte stream.
                uint wait = USB512WinUsbNative.WaitForSingleObject(slot.Event, strictFifo?50U:ReadTimeoutMilliseconds);
                if (wait == USB512WinUsbNative.WaitTimeout)
                    return 0;
                if (wait == USB512WinUsbNative.WaitFailed)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject for WinUSB read failed");
                if (wait != USB512WinUsbNative.WaitObject0)
                    throw new InvalidOperationException("Unexpected WinUSB read wait result.");

                // The WinUSB event can be signalled just before its internal
                // completion state is available to a non-blocking query.  Let
                // WinUSB harvest that final state instead of treating the
                // documented ERROR_IO_INCOMPLETE (996) race as a disconnect.
                // The request has already completed the bounded 10-second
                // wait above and the event is manual-reset, as WinUSB requires
                // for a blocking GetOverlappedResult call.
                if (!USB512WinUsbNative.WinUsbGetOverlappedResult(winUsb, slot.OverlappedPointer, out bytesRead, true))
                {
                    int error = Marshal.GetLastWin32Error();
                    if(strictFifo)
                        throw new Win32Exception(error,"WILDX ordered USB completion failed; recording is incomplete");
                    if (error == USB512WinUsbNative.ErrorOperationAborted ||
                        error == USB512WinUsbNative.ErrorSemTimeout)
                    {
                        slot.Pending = false;
                        return 0;
                    }
                    if (error == USB512WinUsbNative.ErrorGenFailure)
                    {
                        CompletionFailureRecoveryCount++;
                        // Do not trust ResetPipe after a native completion
                        // failure. Freeze the device tail, then make the outer
                        // capture loop recover on a new WinUSB handle.
                        PrepareForReopen();
                        throw new Win32Exception(error,
                            "WinUSB bulk-IN completion failed; reopening interface");
                    }
                    throw new Win32Exception(error, "WinUsb_GetOverlappedResult failed");
                }
            }

            slot.Pending = false;
            slot.CompletedSynchronously = false;
            if (bytesRead > (uint)destination.Length || bytesRead > (uint)slot.Buffer.Length)
                throw new InvalidOperationException("WinUSB returned a transfer larger than the supplied buffer.");

            if (bytesRead > 0)
                Buffer.BlockCopy(slot.Buffer, 0, destination, 0, (int)bytesRead);
            if(strictFifo)nextSlot=(nextSlot+1)%slots.Length;

            // Re-arm only after copying; the USB controller may write this
            // pinned memory as soon as the request is submitted.
            if (!Submit(slot))
            {
                SubmitFailureRecoveryCount++;
                if(strictFifo)submitFailed=true;
                // The completed payload was copied before the failed requeue,
                // so it is valid and must still reach the logger. Freeze the
                // tail now; the next read will surface the poisoned interface
                // to the outer capture loop for clean-handle recovery.
                PrepareForReopen();
                return (int)bytesRead;
            }
            return (int)bytesRead;
        }

        // STOP/AbortPipe has completed. Save successful queued transfers in
        // submission order before retiring native buffers; never resubmit here.
        // The first cancelled request defines the end of the valid prefix.
        internal int DrainStoppedInto(byte[] destination)
        {
            if(!strictFifo || disposed)throw new InvalidOperationException("Ordered stopped drain required");
            ReadSlot slot=slots[nextSlot];
            if(!slot.Pending)return -1;
            uint bytes=slot.CompletedBytes;
            if(!slot.CompletedSynchronously) {
                uint wait=USB512WinUsbNative.WaitForSingleObject(slot.Event,AbortCompletionTimeoutMilliseconds);
                if(wait!=USB512WinUsbNative.WaitObject0)
                    throw new IOException("WILDX stopped USB request did not retire");
                if(!USB512WinUsbNative.WinUsbGetOverlappedResult(winUsb,slot.OverlappedPointer,out bytes,true)) {
                    int error=Marshal.GetLastWin32Error();
                    if(error==USB512WinUsbNative.ErrorOperationAborted)return -1;
                    throw new Win32Exception(error,"WILDX stopped USB completion failed");
                }
            }
            if(bytes>destination.Length || bytes>slot.Buffer.Length)throw new IOException("Oversized stopped USB transfer");
            Buffer.BlockCopy(slot.Buffer,0,destination,0,(int)bytes);
            slot.Pending=false;slot.CompletedSynchronously=false;nextSlot=(nextSlot+1)%slots.Length;
            return (int)bytes;
        }

        // Returns false only for ERROR_GEN_FAILURE.  The caller first retires
        // every request from the old pipe, then resets and requeues the whole
        // set.  In particular, do not resubmit just this slot: the remaining
        // requests still refer to the pre-reset endpoint state.
        private bool Submit(ReadSlot slot)
        {
            USB512WinUsbNative.ResetEvent(slot.Event);
            slot.PrepareOverlapped();
            uint transferred;
            if (USB512WinUsbNative.WinUsbReadPipeOverlapped(
                winUsb, pipeId, slot.BufferPointer, (uint)slot.Buffer.Length,
                out transferred, slot.OverlappedPointer))
            {
                slot.Pending = true;
                slot.CompletedSynchronously = true;
                slot.CompletedBytes = transferred;
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (error == USB512WinUsbNative.ErrorIoPending)
            {
                slot.Pending = true;
                slot.CompletedSynchronously = false;
                slot.CompletedBytes = 0;
                return true;
            }
            if (error == USB512WinUsbNative.ErrorGenFailure)
                return false;
            throw new Win32Exception(error, "WinUsb_ReadPipe (overlapped) failed");
        }

        private void RecoverPipe()
        {
            // WinUsb_AbortPipe is synchronous.  Harvesting every completion
            // before WinUsb_ResetPipe and before reusing an OVERLAPPED keeps
            // WinUSB from writing into a slot that has been rearmed for a new
            // read (the failure mode that can otherwise lead to CLR corruption).
            if (prepareInflightReplay != null)
                prepareInflightReplay();
            RetirePendingReads();
            if (!USB512WinUsbNative.WinUsbResetPipe(winUsb, pipeId))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "WinUsb_ResetPipe after bulk-IN failure failed");
            }

            // A pipe reset discards the host's queued transfers. Start the
            // retained replay, then post fresh reads before waiting for its
            // acknowledgement. Waiting first can leave a high-speed IN
            // endpoint with no host credit and cause another ERROR 31/stall.
            if (requestInflightReplay != null)
                requestInflightReplay();

            RecoveryCount++;
            foreach (ReadSlot slot in slots)
            {
                if (slot == null)
                    continue;
                if (!Submit(slot))
                {
                    throw new Win32Exception(USB512WinUsbNative.ErrorGenFailure,
                        "WinUSB bulk-IN pipe did not accept reads after reset");
                }
            }
            if (waitForInflightReplay != null)
                waitForInflightReplay();
        }

        private void RetirePendingReads()
        {
            if (!USB512WinUsbNative.WinUsbAbortPipe(winUsb, pipeId))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "WinUsb_AbortPipe after bulk-IN failure failed");
            }

            foreach (ReadSlot slot in slots)
            {
                if (slot == null || !slot.Pending)
                    continue;

                if (!slot.CompletedSynchronously)
                {
                    uint ignored;
                    uint wait = USB512WinUsbNative.WaitForSingleObject(
                        slot.Event, AbortCompletionTimeoutMilliseconds);
                    if (wait != USB512WinUsbNative.WaitObject0)
                    {
                        // Do not reuse or free this OVERLAPPED while a broken
                        // driver may still reference it. Dispose() retains the
                        // slots on this exception; the caller then closes the
                        // whole WinUSB interface and reopens a clean one.
                        throw new TimeoutException(
                            "WinUSB read did not retire after AbortPipe.");
                    }
                    // The firmware replays a retained chronological window
                    // after reset. Do not pass independently-completed old
                    // reads onward: they can be newer than that replay.
                    bool retired=USB512WinUsbNative.WinUsbGetOverlappedResult(
                        winUsb, slot.OverlappedPointer, out ignored, false);
                    int error=retired?0:Marshal.GetLastWin32Error();
                    if(!retired && error==996)
                        throw new IOException("Cancellation not complete; native request memory retained");
                }
                slot.Pending = false;
                slot.CompletedSynchronously = false;
                slot.CompletedBytes = 0;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try
            {
                if (!RetirementConfirmed) RetirePendingReads();
                RetirementConfirmed = true;
            }
            catch
            {
                // Do not free native request memory if the driver cannot
                // confirm cancellation.  The device handle is about to close,
                // and retaining these few pinned slots is safer than a UAF.
                return;
            }
            foreach (ReadSlot slot in slots)
            {
                if (slot != null)
                    slot.Dispose();
            }
        }

        // Used only with USB512WinUsbDevice.DisposeForRecovery().  Do not call
        // AbortPipe from the capture thread after a native completion failure:
        // the driver can hang there indefinitely.  The device cleanup worker
        // releases these pinned buffers only after WinUsbFree has completed.
        internal void AbandonForRecovery()
        {
            if (disposed)
                return;
            disposed = true;
        }

        internal void ReleaseAfterRecoveryClose()
        {
            foreach (ReadSlot slot in slots)
            {
                if (slot != null)
                    slot.Dispose();
            }
        }
    }

    internal static class USB512WinUsbNative
    {
        internal static readonly Guid InterfaceGuid = new Guid("4F6A8C79-3F02-4B0D-9E2C-1A9B5C7D42E1");
        internal static readonly Guid NeuropixelsInterfaceGuid = new Guid("8F5A4A7B-0AA6-4F8E-A14F-A2F7B3C6AA4E");

        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const uint FileAttributeNormal = 0x00000080;
        // Match the tested Python WinUSB host setup.  The normal ReadPipe calls
        // may still complete synchronously with OVERLAPPED = NULL.
        internal const uint FileFlagOverlapped = 0x40000000;
        internal const int ErrorSemTimeout = 121;
        internal const int ErrorGenFailure = 31;
        internal const int ErrorOperationAborted = 995;
        internal const int ErrorIoPending = 997;
        internal const uint WaitObject0 = 0;
        internal const uint WaitTimeout = 0x00000102;
        internal const uint WaitFailed = 0xFFFFFFFF;
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;

        internal static IList<string> EnumerateInterfacePaths(Guid interfaceGuid)
        {
            var result = new List<string>();
            var info = SetupDiGetClassDevs(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (info == InvalidHandleValue)
            {
                return result;
            }
            try
            {
                for (uint index = 0; ; index++)
                {
                    var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                    interfaceData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                    {
                        if (Marshal.GetLastWin32Error() == 259)
                        {
                            break;
                        }
                        continue;
                    }

                    uint required;
                    SetupDiGetDeviceInterfaceDetail(info, ref interfaceData, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required == 0)
                    {
                        continue;
                    }
                    var detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(info, ref interfaceData, detail, required, out required, IntPtr.Zero))
                        {
                            var path = Marshal.PtrToStringAuto(IntPtr.Add(detail, 4));
                            if (!string.IsNullOrEmpty(path))
                            {
                                result.Add(path);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            internal int cbSize;
            internal Guid InterfaceClassGuid;
            internal int Flags;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct WINUSB_SETUP_PACKET
        {
            internal byte RequestType;
            internal byte Request;
            internal ushort Value;
            internal ushort Index;
            internal ushort Length;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct USB_INTERFACE_DESCRIPTOR
        {
            internal byte bLength;
            internal byte bDescriptorType;
            internal byte bInterfaceNumber;
            internal byte bAlternateSetting;
            internal byte bNumEndpoints;
            internal byte bInterfaceClass;
            internal byte bInterfaceSubClass;
            internal byte bInterfaceProtocol;
            internal byte iInterface;
        }

        internal enum USBD_PIPE_TYPE
        {
            Control,
            Isochronous,
            Bulk,
            Interrupt
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINUSB_PIPE_INFORMATION
        {
            internal USBD_PIPE_TYPE PipeType;
            internal byte PipeId;
            internal ushort MaximumPacketSize;
            internal byte Interval;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NATIVE_OVERLAPPED
        {
            internal IntPtr Internal;
            internal IntPtr InternalHigh;
            internal uint Offset;
            internal uint OffsetHigh;
            internal IntPtr Event;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ResetEvent(IntPtr eventHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        internal static bool WinUsbInitialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle, string target=null) { return USB512_interface.UsbHealthNative.Initialize(deviceHandle,out interfaceHandle,target); }

        internal static bool WinUsbFree(IntPtr interfaceHandle) { return USB512_interface.UsbHealthNative.Free(interfaceHandle); }

        [DllImport("winusb.dll", EntryPoint = "WinUsb_QueryInterfaceSettings", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbQueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, out USB_INTERFACE_DESCRIPTOR descriptor);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_QueryPipe", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbQueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WINUSB_PIPE_INFORMATION pipeInformation);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_SetPipePolicy", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbSetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint value);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_SetPowerPolicy", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbSetPowerPolicy(IntPtr interfaceHandle, uint policyType, uint valueLength, ref byte value);

        internal static bool WinUsbReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength, out uint transferred, IntPtr overlapped) { return USB512_interface.UsbHealthNative.Read(interfaceHandle,pipeId,buffer,bufferLength,out transferred,overlapped); }

        internal static bool WinUsbReadPipeOverlapped(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint transferred, IntPtr overlapped) { return USB512_interface.UsbHealthNative.ReadOverlapped(interfaceHandle,pipeId,buffer,bufferLength,out transferred,overlapped); }

        internal static bool WinUsbGetOverlappedResult(IntPtr interfaceHandle, IntPtr overlapped, out uint transferred, bool wait) { return USB512_interface.UsbHealthNative.Result(interfaceHandle,overlapped,out transferred,wait); }

        [DllImport("winusb.dll", EntryPoint = "WinUsb_ControlTransfer", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbControlTransfer(IntPtr interfaceHandle, WINUSB_SETUP_PACKET setupPacket, [In, Out] byte[] buffer, uint bufferLength, out uint transferred, IntPtr overlapped);

        internal static bool WinUsbAbortPipe(IntPtr interfaceHandle, byte pipeId) { return USB512_interface.UsbHealthNative.Abort(interfaceHandle,pipeId); }

        [DllImport("winusb.dll", EntryPoint = "WinUsb_ResetPipe", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbResetPipe(IntPtr interfaceHandle, byte pipeId);

        [DllImport("winusb.dll", EntryPoint = "WinUsb_FlushPipe", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsbFlushPipe(IntPtr interfaceHandle, byte pipeId);

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
    }
}
