using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace USB512_interface
{
    /*
     * The V2 NP2 wire protocol is deliberately isolated from XeNpWinUsbDevice.
     * B386 has historically carried NP1 packet frames too, but NP2USB01/02/03 is
     * a fixed-size continuity-framed raw block stream. Legacy files carry a
     * payload CRC; performance profiles rely on native link CRC and SuperSync.
     * Treating those blocks as NP1
     * packets would make a corrupt recording look decoded rather than fail it.
     */
    internal sealed class NpV2ServiceStatus
    {
        internal const uint MagicValue = 0x3253504EU; // "NPS2"
        internal const uint AbiValue = 4U;
        internal uint Magic, Abi, Mode, Capabilities, Busy, Target, Uid0, UidXor;
        internal bool Valid { get { return Magic == MagicValue && Abi == AbiValue; } }
        internal void RequireUid(string expectedUid)
        {
            uint first, second, third;
            if (!Valid || expectedUid == null || expectedUid.Length != 24 || expectedUid == new string('0',24) || expectedUid.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase) ||
                !uint.TryParse(expectedUid.Substring(0,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out first) ||
                !uint.TryParse(expectedUid.Substring(8,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out second) ||
                !uint.TryParse(expectedUid.Substring(16,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out third) || Uid0 != first || UidXor != (second ^ third))
                throw new IOException("V2 service identity does not match the selected MCU UID.");
        }
        internal void RequireDeviceUid(string expectedUid, string deviceUid)
        {
            RequireUid(expectedUid);
            if (!String.Equals(expectedUid, deviceUid, StringComparison.OrdinalIgnoreCase))
                throw new IOException("V2 USB path does not match the selected full MCU UID.");
        }
        internal string ModeName
        {
            get
            {
                switch (Mode)
                {
                    case 1: return "SD app";
                    case 2: return "USB stream";
                    case 3: return "USB download";
                    case 4: return "recovery";
                    default: return "unknown";
                }
            }
        }
        internal static NpV2ServiceStatus Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 32) throw new IOException("NPS2 status is truncated.");
            return new NpV2ServiceStatus {
                Magic = U32(bytes, 0), Abi = U32(bytes, 4), Mode = U32(bytes, 8), Capabilities = U32(bytes, 12),
                Busy = U32(bytes, 16), Target = U32(bytes, 20), Uid0 = U32(bytes, 24), UidXor = U32(bytes, 28)
            };
        }
        internal static uint U32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
        }
    }

    internal sealed class NpV2UsbSelfTestReport
    {
        internal const uint MagicValue = 0x58535432U;
        internal const uint VersionValue = 1U;
        internal const uint CheckMask = 0x3fffU;
        internal const uint ExternalChecks = 0x3c00U;
        internal const uint Hold = 1U, Sd1V8 = 2U, VoltsUnmeasured = 4U, SdReadOnly = 8U, RebootRequired = 16U;
        internal const uint Idle = 0U, Queued = 1U, Running = 2U, Done = 3U, Held = 4U, Rejected = 5U;

        internal uint Magic, Version, Bytes, Sequence, State, Command, Phase, StartedMs, ElapsedMs, Flags;
        internal uint Checked, Passed, Failed, Skipped, Error, RailsRequested;
        internal uint PfOdr, PdOdr, PgOdr, PcOdr;
        internal uint CpuHz, HclkHz, SdKernelHz, RccCr, RccCcipr1, RccCcipr2;
        internal uint PwrVosr, PwrSvmcr, PwrSvmsr, UsbPhy;
        internal uint UsbConfigured, UsbResets, UsbSetups, UsbDsts;
        internal uint AppValid, UsbImageValid, SramErrors;
        internal uint PsramResult, PsramId0, PsramId1, PsramFlags;
        internal uint SdState, SdError, SdInitStage, SdOcr, SdSectors, SdHz, SdWidth;
        internal uint BmxId, BmxError, BmeId, BmeError, BleStatePin, NpResetPin;
        internal uint SdPower, SdClkcr, SdArg, SdCmd, SdSta, Uid0, Uid1, Uid2, Reserved0, Reserved1;

        internal bool Holding { get { return (Flags & Hold) != 0U; } }
        internal void RequireUid(string expectedUid)
        {
            if(expectedUid==null || expectedUid.Length!=24)throw new IOException("Exact V2 MCU UID required.");
            // Firmware's untouched boot report has no UID until the first action.
            // The service UID must already have been attested before this check.
            if(Sequence==0U && State==Idle && Command==0U && (Uid0|Uid1|Uid2)==0U)return;
            if(!String.Equals(Uid0.ToString("X8")+Uid1.ToString("X8")+Uid2.ToString("X8"),expectedUid,StringComparison.OrdinalIgnoreCase))
                throw new IOException("V2 self-test report does not match the selected MCU UID.");
        }
        internal bool IsTerminalFor(uint command)
        {
            if (Command != command) return false;
            if (State == Rejected) return true;
            if (command == 1U) return State == Done || State == Held;
            if (command == 2U || command == 3U || command == 5U) return State == Held;
            return command == 4U && (State == Idle || State == Done);
        }
        internal bool IsNewTerminalFor(uint command, uint baselineSequence)
        {
            uint advance=unchecked(Sequence-baselineSequence);
            return advance!=0U && advance<0x80000000U && IsTerminalFor(command);
        }

        internal static NpV2UsbSelfTestReport Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 256) throw new IOException("V2 USB self-test report is truncated.");
            uint[] w = new uint[64];
            for (int i = 0; i < w.Length; i++) w[i] = NpV2ServiceStatus.U32(bytes, i * 4);
            var r = new NpV2UsbSelfTestReport {
                Magic=w[0], Version=w[1], Bytes=w[2], Sequence=w[3], State=w[4], Command=w[5], Phase=w[6], StartedMs=w[7], ElapsedMs=w[8], Flags=w[9],
                Checked=w[10], Passed=w[11], Failed=w[12], Skipped=w[13], Error=w[14], RailsRequested=w[15],
                PfOdr=w[16], PdOdr=w[17], PgOdr=w[18], PcOdr=w[19], CpuHz=w[20], HclkHz=w[21], SdKernelHz=w[22], RccCr=w[23], RccCcipr1=w[24], RccCcipr2=w[25],
                PwrVosr=w[26], PwrSvmcr=w[27], PwrSvmsr=w[28], UsbPhy=w[29], UsbConfigured=w[30], UsbResets=w[31], UsbSetups=w[32], UsbDsts=w[33],
                AppValid=w[34], UsbImageValid=w[35], SramErrors=w[36], PsramResult=w[37], PsramId0=w[38], PsramId1=w[39], PsramFlags=w[40],
                SdState=w[41], SdError=w[42], SdInitStage=w[43], SdOcr=w[44], SdSectors=w[45], SdHz=w[46], SdWidth=w[47],
                BmxId=w[48], BmxError=w[49], BmeId=w[50], BmeError=w[51], BleStatePin=w[52], NpResetPin=w[53],
                SdPower=w[54], SdClkcr=w[55], SdArg=w[56], SdCmd=w[57], SdSta=w[58], Uid0=w[59], Uid1=w[60], Uid2=w[61], Reserved0=w[62], Reserved1=w[63]
            };
            uint results = r.Passed | r.Failed | r.Skipped;
            if (r.Magic != MagicValue || r.Version != VersionValue || r.Bytes != 256U || r.State > Rejected || r.Command > 5U ||
                (r.Flags & ~(Hold | Sd1V8 | VoltsUnmeasured | SdReadOnly | RebootRequired)) != 0U ||
                (r.Checked & ~CheckMask) != 0U || (results & ~CheckMask) != 0U ||
                (r.Passed & (r.Failed | r.Skipped)) != 0U || (r.Failed & r.Skipped) != 0U || (r.Skipped & r.Checked) != 0U || ((r.Passed | r.Failed) & ~r.Checked) != 0U)
                throw new IOException("V2 USB self-test report has an invalid ABI, state, or check mask.");
            return r;
        }

        internal string Text()
        {
            return "V2 USB power self-test report\r\n"+
                "sequence="+Sequence+" state="+State+" command="+Command+" phase="+Phase+" elapsed_ms="+ElapsedMs+" error="+Error+"\r\n"+
                "checked=0x"+Checked.ToString("X4")+" ["+CheckText(Checked)+"]\r\n"+
                "passed=0x"+Passed.ToString("X4")+" ["+CheckText(Passed)+"] failed=0x"+Failed.ToString("X4")+" ["+CheckText(Failed)+"] skipped=0x"+Skipped.ToString("X4")+" ["+CheckText(Skipped)+"]\r\n"+
                "flags=0x"+Flags.ToString("X8")+" rails_requested=0x"+RailsRequested.ToString("X8")+"; actual rail voltages are NOT measured by this report.\r\n"+
                "meter expectations: VEE_3V3 U3/C6/SD VDD 3.3 V when PF2 high; VCC_1V8 U8/C13/probe-camera 1.8 V when PD10 high;\r\n"+
                "VDD_3V3 U4/C9 USB/PG 3.3 V always on; VDD_1V8 U6/C10 MCU/sensors/PSRAM 1.8 V always on;\r\n"+
                "VCCIO_SD U14/VCCB/U12S is 3.3 or 1.8 V by PG0 selector; VDD11 is an internal MCU core rail, nominal per MCU (not asserted here).\r\n"+
                "GPIO: PF=0x"+PfOdr.ToString("X8")+" PD=0x"+PdOdr.ToString("X8")+" PG=0x"+PgOdr.ToString("X8")+" PC=0x"+PcOdr.ToString("X8")+"\r\n"+
                "clock cpu="+CpuHz+" hclk="+HclkHz+" sd="+SdKernelHz+" rcc_cr=0x"+RccCr.ToString("X8")+" ccipr1=0x"+RccCcipr1.ToString("X8")+" ccipr2=0x"+RccCcipr2.ToString("X8")+"\r\n"+
                "power vosr=0x"+PwrVosr.ToString("X8")+" svmcr=0x"+PwrSvmcr.ToString("X8")+" svmsr=0x"+PwrSvmsr.ToString("X8")+" usb_phy=0x"+UsbPhy.ToString("X8")+" usb configured="+UsbConfigured+" resets="+UsbResets+" setups="+UsbSetups+" dsts=0x"+UsbDsts.ToString("X8")+"\r\n"+
                "images app="+AppValid+" usb="+UsbImageValid+" sram_errors="+SramErrors+" psram result="+PsramResult+" id=0x"+PsramId0.ToString("X8")+"/0x"+PsramId1.ToString("X8")+" flags=0x"+PsramFlags.ToString("X8")+"\r\n"+
                "SD state="+SdState+" error="+SdError+" init="+SdInitStage+" ocr=0x"+SdOcr.ToString("X8")+" sectors="+SdSectors+" hz="+SdHz+" width="+SdWidth+" power=0x"+SdPower.ToString("X8")+" clkcr=0x"+SdClkcr.ToString("X8")+" arg=0x"+SdArg.ToString("X8")+" cmd=0x"+SdCmd.ToString("X8")+" sta=0x"+SdSta.ToString("X8")+"\r\n"+
                "BMX id/error=0x"+BmxId.ToString("X8")+"/"+BmxError+" BME id/error=0x"+BmeId.ToString("X8")+"/"+BmeError+" BLE_pin="+BleStatePin+" NP_reset="+NpResetPin+"\r\n"+
                "UID=0x"+Uid0.ToString("X8")+"/0x"+Uid1.ToString("X8")+"/0x"+Uid2.ToString("X8")+" reserved=0x"+Reserved0.ToString("X8")+"/0x"+Reserved1.ToString("X8")+"\r\n"+
                "External NP/ADC/camera/BLE checks are expected skipped (mask 0x"+ExternalChecks.ToString("X4")+"); failed/skipped checks are not a pass. Reboot to normal USB stream mode before capture.";
        }

        private static string CheckText(uint mask)
        {
            string[] names = { "clock", "SRAM", "APP", "USB image", "PSRAM", "SD info", "SD read", "BMX", "BME", "USB control", "NP external", "ADC external", "camera external", "BLE external" };
            var result = new StringBuilder();
            for (int bit = 0; bit < names.Length; bit++) if ((mask & (1U << bit)) != 0U)
            {
                if (result.Length != 0) result.Append(", ");
                result.Append(names[bit]);
            }
            return result.Length == 0 ? "none" : result.ToString();
        }
    }

    internal sealed class NpV2CaptureStatus
    {
        internal const uint MagicValue = 0x3155324EU; // "N2U1"
        internal const uint Np2Usb03Sample20KHz = 20000U;
        internal const uint Np2Usb03Sample15KHz = 15000U;
        internal const uint Np2Usb03Sample30KHz = 30000U;
        internal uint[] Words;
        internal bool Valid { get { return Words != null && Words.Length == 32 && Words[0] == MagicValue && Words[1] == 1U; } }
        internal uint State { get { return Words[2]; } }
        internal uint Error { get { return Words[3]; } }
        internal uint Target { get { return Words[4]; } }
        internal uint Filled { get { return Words[5]; } }
        internal uint Submitted { get { return Words[6]; } }
        internal uint Completed { get { return Words[7]; } }
        internal uint ProducerError { get { return Words[12]; } }
        internal uint ProducerMaxBacklog { get { return Words[14]; } }
        internal uint PartialTicks { get { return Words[15]; } }
        internal uint CaptureStartedMs { get { return Words[17]; } }
        internal uint DeviceNowMs { get { return Words[18]; } }
        internal uint PssiHz { get { return Words[19]; } }
        internal uint ActiveWires { get { return Words[20]; } }
        internal uint Format { get { return Words[21]; } }
        internal uint SourceState { get { return Words[22]; } }
        internal uint SourceError { get { return Words[23]; } }
        internal uint ProbeMask { get { return Words[24]; } }
        internal uint LiveProbeMask { get { return Words[25]; } }
        internal uint SampleHz { get { return Format == 3U ? Words[26] : (Format == 1U || Format == 2U ? 30000U : 0U); } }
        internal uint IngressBytesPerTick { get { return Words[27]; } }
        internal uint PackMaxMs { get { return Words[28]; } }
        internal uint CrcMaxMs { get { return Words[29]; } }
        internal uint ValidateMaxMs { get { return Words[30]; } }
        internal uint FinalizeMaxMs { get { return Words[31]; } }
        internal int StoredProbeCount { get { return PopCount3(ProbeMask); } }
        internal bool CurrentProfileSupported
        {
            get
            {
                if (Format == 1U || Format == 2U) return PssiHz == 48000000U && ActiveWires == 0x38U;
                bool performanceMask = ProbeMask == 1U || ProbeMask == 2U || ProbeMask == 3U || ProbeMask == 7U;
                return Format == 3U && (SampleHz == Np2Usb03Sample15KHz || SampleHz == Np2Usb03Sample20KHz || SampleHz == Np2Usb03Sample30KHz) &&
                    PssiHz == SampleHz * 1600U &&
                    performanceMask && (LiveProbeMask & ~ProbeMask) == 0U &&
                    (IngressBytesPerTick == 1U || IngressBytesPerTick == 2U) &&
                    ActiveWires == WireMask(ProbeMask);
            }
        }
        internal string StateName { get { return StateNameOf(State); } }
        internal bool CompleteFor(uint expected)
        {
            return Valid && State == 4U && Error == 0U && ProducerError == 0U && SourceError == 0U &&
                Target == expected && Filled == expected && Submitted == expected && Completed == expected &&
                SourceState == 11U && PartialTicks == 0U;
        }
        internal static string StateNameOf(uint state)
        {
            switch (state)
            {
                case 0: return "idle";
                case 1: return "configuring NP2013";
                case 2: return "capturing";
                case 3: return "draining USB";
                case 4: return "complete";
                case 255: return "failed";
                default: return "state " + state.ToString(CultureInfo.InvariantCulture);
            }
        }
        private static int PopCount3(uint mask) { return (int)((mask & 1U) + ((mask >> 1) & 1U) + ((mask >> 2) & 1U)); }
        private static uint WireMask(uint mask) { return ((mask & 1U) != 0U ? 0x007U : 0U) | ((mask & 2U) != 0U ? 0x038U : 0U) | ((mask & 4U) != 0U ? 0x1C0U : 0U); }
        internal static NpV2CaptureStatus Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 128) throw new IOException("NP2 USB status is truncated.");
            var words = new uint[32];
            for (int i = 0; i < words.Length; i++) words[i] = NpV2ServiceStatus.U32(bytes, i * 4);
            var result = new NpV2CaptureStatus { Words = words };
            if (!result.Valid) throw new IOException("The device did not return NP2 USB status ABI 1.");
            return result;
        }
    }

    internal sealed class NpV2ProfileCapabilities
    {
        internal const uint MagicValue = 0x3143324EU; // "N2C1"
        internal const uint Rate20KMask = 2U;
        internal const uint Rate15KMask = 1U;
        internal const uint Rate30KMask = 4U;
        internal const string Triple30UsbMessage = "Three NP2 probes at 30 kHz require 54 MB/s before headers, above USB 2.0 High-Speed bulk's 53.248 MB/s limit. Select 20 kHz or 15 kHz for three probes, or connectors 1+2 for 30 kHz. The selected rate has not been changed.";
        internal uint Magic, Abi, AvailableProbeMask, SelectedProbeMask;
        internal uint AvailableLiveProbeMask, SelectedLiveProbeMask, RateMask, SampleHz;
        internal bool Valid
        {
            get
            {
                return Magic == MagicValue && Abi == 1U && AvailableProbeMask > 0U && AvailableProbeMask <= 7U &&
                    (SelectedProbeMask & ~AvailableProbeMask) == 0U && (AvailableLiveProbeMask & ~AvailableProbeMask) == 0U &&
                    (SelectedLiveProbeMask & ~SelectedProbeMask) == 0U && SupportsRate(SampleHz);
            }
        }
        internal bool SupportsRate(uint sampleHz)
        {
            return (sampleHz == 15000U && (RateMask & Rate15KMask) != 0U) ||
                (sampleHz == 20000U && (RateMask & Rate20KMask) != 0U) ||
                (sampleHz == 30000U && (RateMask & Rate30KMask) != 0U);
        }
        internal bool SupportsProbeMask(uint probeMask)
        {
            /* Optimized NP2USB03 layouts exist only for one low connector,
             * the raw PSSI8 connector pair, or all three connectors with D8
             * supplied by SPI2. Masks 4/5/6 would require a different codec. */
            bool layout = probeMask == 1U || probeMask == 2U || probeMask == 3U || probeMask == 7U;
            return layout && (probeMask & ~AvailableProbeMask) == 0U;
        }
        internal static bool NeedsMultiProbe30KQualificationWarning(uint probeMask, uint sampleHz)
        {
            return sampleHz == 30000U && (probeMask == 3U || probeMask == 7U);
        }
        internal static void RequireUsbBandwidth(uint probeMask, uint sampleHz)
        {
            if (probeMask == 7U && sampleHz == 30000U) throw new IOException(Triple30UsbMessage);
        }
        internal uint[] SupportedRates
        {
            get
            {
                var values = new List<uint>();
                if ((RateMask & Rate15KMask) != 0U) values.Add(15000U);
                if ((RateMask & Rate20KMask) != 0U) values.Add(20000U);
                if ((RateMask & Rate30KMask) != 0U) values.Add(30000U);
                return values.ToArray();
            }
        }
        internal static NpV2ProfileCapabilities Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 32) throw new IOException("NP2 profile capabilities are truncated.");
            var result = new NpV2ProfileCapabilities {
                Magic = NpV2ServiceStatus.U32(bytes, 0), Abi = NpV2ServiceStatus.U32(bytes, 4),
                AvailableProbeMask = NpV2ServiceStatus.U32(bytes, 8), SelectedProbeMask = NpV2ServiceStatus.U32(bytes, 12),
                AvailableLiveProbeMask = NpV2ServiceStatus.U32(bytes, 16), SelectedLiveProbeMask = NpV2ServiceStatus.U32(bytes, 20),
                RateMask = NpV2ServiceStatus.U32(bytes, 24), SampleHz = NpV2ServiceStatus.U32(bytes, 28)
            };
            if (!result.Valid) throw new IOException("The device returned an invalid NP2 runtime-profile capability record.");
            return result;
        }
    }

    internal sealed class NpV2SourceStatus
    {
        internal const uint MagicValue = 0x3153324EU; // "N2S1"
        internal uint[] Words;
        internal byte[] SerialBytes, ModelBytes;
        internal bool Valid { get { return Words != null && Words.Length == 32 && Words[0] == MagicValue && Words[1] == 1U; } }
        internal uint State { get { return Words[2]; } }
        internal uint Error { get { return Words[3]; } }
        internal uint Flags { get { return Words[4]; } }
        internal uint I2cError { get { return Words[7]; } }
        internal uint I2cOperations { get { return Words[8]; } }
        internal uint ProbeId { get { return Words[14]; } }
        internal uint ChainFailMask { get { return Words[21]; } }
        internal string DegradedWarning {
            get {
                if(Error!=0U || (Flags&16U)==0U)return null; // DEGRADED is bit 4, not bit 16.
                var failed=new List<string>();
                for(int p=0;p<3;p++)for(int shank=0;shank<4;shank++)if((ChainFailMask&(1U<<(p*6+shank)))!=0)failed.Add("P"+(p+1)+" shank "+shank);
                return "DEGRADED: "+(failed.Count==0?"firmware reported degraded source without shank details":String.Join(", ",failed)+" failed shank SR_OUT_OK")+". Consult the applied configuration receipt; SR/mapping may be unverified.";
            }
        }
        internal uint EepromReads { get { return Words[28]; } }
        internal uint EepromMatch { get { return Words[29]; } }
        internal string Serial { get { return Printable(SerialBytes); } }
        internal string Model { get { return Printable(ModelBytes); } }
        internal static NpV2SourceStatus Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 144) throw new IOException("NP2 source status is truncated.");
            var words = new uint[32];
            for (int i = 0; i < words.Length; i++) words[i] = NpV2ServiceStatus.U32(bytes, i * 4);
            var result = new NpV2SourceStatus { Words = words, SerialBytes = new byte[8], ModelBytes = new byte[8] };
            Buffer.BlockCopy(bytes, 128, result.SerialBytes, 0, 8);
            Buffer.BlockCopy(bytes, 136, result.ModelBytes, 0, 8);
            if (!result.Valid) throw new IOException("The device did not return NP2 source status ABI 1.");
            return result;
        }
        private static string Printable(byte[] bytes)
        {
            if (bytes == null) return "";
            var builder = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = bytes[i];
                if (value == 0) break;
                builder.Append(value >= 32 && value <= 126 ? (char)value : '?');
            }
            return builder.ToString();
        }
    }

    // Failure-only interpretation: no polling or work in the capture hot path.
    internal static class NpV2RecordingDiagnostics
    {
        private static readonly string[] SourceErrors={"none","source argument","clock setup/readback","HSLV disabled","I2C bus initialization",
            "I2C transfer/readback","ASIC ID check","EEPROM identity check","shift-register verification","shared capture/DMA binding","source cancellation","I2C operation timeout","probe setup deadline"};
        private static readonly string[] Steps={"ASIC ID read","EEPROM read","release write","release readback","CAL write","CAL readback",
            "shift reset assert","shift reset release","OP reset readback","REC reset readback","chain length low write","chain length low readback",
            "chain length high write","chain length high readback","shift-chain programming","shift-chain status verification","final hold write",
            "final hold readback","final CAL write","final CAL readback","supersync write","supersync readback","record OP write","record OP readback","record release write","record release readback"};
        internal static string SourceStateName(uint state){
            string[] names={"Idle","Power/clock preparation","Power settling","Clock settling","Reset settling","ASIC identification","EEPROM identity","Probe configuration","Analog settling","Probe release/capture binding","Ready","Stopped"};
            return state<names.Length?names[state]:state==255U?"Failed":"Unknown state "+state;
        }
        internal static string ProbeList(uint mask){var names=new List<string>();for(int p=0;p<3;p++)if((mask&(1U<<p))!=0)names.Add("P"+(p+1));return names.Count==0?"unknown":String.Join("+",names);}
        private static string Chain(uint n){return n<4U?"shank "+n:n==4U?"base-even":n==5U?"base-odd":"unknown chain";}
        internal static string Explain(string stage,NpV2CaptureStatus capture,NpV2SourceStatus source,Exception error,string cause){
            uint mask=capture==null?0U:capture.ProbeMask,sourceError=source==null?(capture==null?0U:capture.SourceError):source.Error;
            string probe="not identified; shared path or firmware did not report a probe",comparatorHistory=null;
            bool fatalProbeIdentified=false;
            for(Exception ex=error;ex!=null;ex=ex.InnerException)if(ex.Data.Contains("NP2PhysicalProbe")){
                int port=Convert.ToInt32(ex.Data["NP2PhysicalProbe"],CultureInfo.InvariantCulture);
                if(port>=1 && port<=3){probe="P"+port;stage="Stream framing/supersync verification";}
            }
            if(sourceError!=0U){
                stage="Probe setup / "+(sourceError<SourceErrors.Length?SourceErrors[sourceError]:"source error "+sourceError)+" failed";
                if(source!=null && (source.Flags&0x100U)!=0){
                    uint port=(source.Flags>>9)&3U,state=(source.Flags>>16)&255U,step=source.Flags>>24;
                    stage+=" / "+SourceStateName(state)+(step<Steps.Length?" / "+Steps[step]:"");
                    if(port<3U && (mask==0U || (mask&(1U<<(int)port))!=0)){
                        probe="P"+(port+1)+" (I2C"+(port+2)+")";
                        fatalProbeIdentified=true;
                        if(step>=6U && step<=15U){uint chain=source.Words[18];probe+=" / "+Chain(chain<2U?chain+4U:chain-2U)+", pass "+source.Words[19]+", byte "+source.Words[20];}
                    }
                }
            }else if(capture!=null && capture.ProducerError!=0U){
                string[] names={"none","argument","DMA/hardware fault","DMA interrupt order","DMA position","source stall","PSSI ring overwrite/USB drain backlog","cache maintenance"};
                stage="Capture / "+(capture.ProducerError<names.Length?names[capture.ProducerError]:"producer error "+capture.ProducerError);
                probe="shared PSSI/SPI/DMA path; no individual probe implicated";
            }
            if(source!=null && source.ChainFailMask!=0U){
                var failed=new List<string>();for(int p=0;p<3;p++)for(uint chain=0;chain<6;chain++)if((source.ChainFailMask&(1U<<(p*6+(int)chain)))!=0)failed.Add("P"+(p+1)+" "+Chain(chain));
                if(failed.Count!=0){
                    comparatorHistory=String.Join(", ",failed);
                    if(sourceError==8U && !fatalProbeIdentified)probe=comparatorHistory;
                }
            }
            string report="Stage: "+stage+". Probe: "+probe+". Selected: "+ProbeList(mask)+(capture==null?"":", "+capture.SampleHz.ToString("N0",CultureInfo.InvariantCulture)+" Hz")+".";
            if(capture!=null)report+=" Firmware: capture="+capture.Error+", producer="+capture.ProducerError+", source="+capture.SourceError+".";
            if(source!=null)report+=" I2C=0x"+source.I2cError.ToString("X8")+((source.I2cError&4U)!=0?" (NACK)":"")+", chain-fail=0x"+source.ChainFailMask.ToString("X5")+".";
            if(comparatorHistory!=null)report+=" Comparator failure history: "+comparatorHistory+".";
            return report+(String.IsNullOrWhiteSpace(cause)?"":" Detail: "+cause);
        }
    }

    internal sealed class NpV2UsbRoute
    {
        internal readonly int HubCount;
        internal readonly string LocationPath;
        internal NpV2UsbRoute(int hubCount, string locationPath) { HubCount = hubCount; LocationPath = locationPath; }
        internal string Warning
        {
            get
            {
                if (HubCount == 0) return "Direct PC USB connection detected (no external hub in the Windows path).";
                if (HubCount > 0) return "USB warning: this V2 device is behind " + HubCount.ToString(CultureInfo.InvariantCulture) +
                    " hub" + (HubCount == 1 ? "" : "s") + ". Connect it directly to a PC USB port for recording qualification.";
                return "USB connection path could not be verified. Connect V2 directly to a PC USB port for recording qualification.";
            }
        }
    }

    internal static class NpV2UsbTopology
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DevPropKey { internal Guid Format; internal uint PropertyId; }
        private static readonly Guid LocationPathsFormat = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        private const uint DevPropStringList = 0x2012U;

        [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
        private static extern int LocateDevNode(out uint devInst, string instanceId, uint flags);
        [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW", CharSet = CharSet.Unicode)]
        private static extern int GetDevNodeProperty(uint devInst, ref DevPropKey key, out uint propertyType,
            byte[] buffer, ref uint bufferBytes, uint flags);

        internal static NpV2UsbRoute FromLocationPath(string locationPath)
        {
            if (String.IsNullOrWhiteSpace(locationPath) ||
                locationPath.IndexOf("#USBROOT(", StringComparison.OrdinalIgnoreCase) < 0)
                return new NpV2UsbRoute(-1, locationPath);
            int ports = 0, start = 0;
            while ((start = locationPath.IndexOf("#USB(", start, StringComparison.OrdinalIgnoreCase)) >= 0)
            { ports++; start += 5; }
            return new NpV2UsbRoute(ports > 0 ? ports - 1 : -1, locationPath);
        }

        internal static NpV2UsbRoute Inspect(string interfacePath)
        {
            try
            {
                string[] parts = (interfacePath ?? String.Empty).Split('#');
                if (parts.Length < 4 || !parts[1].Equals("vid_1209&pid_b386", StringComparison.OrdinalIgnoreCase) ||
                    parts[2].Length != 24) return new NpV2UsbRoute(-1, null);
                foreach (char digit in parts[2]) if (!Uri.IsHexDigit(digit)) return new NpV2UsbRoute(-1, null);
                uint devInst;
                if (LocateDevNode(out devInst, @"USB\VID_1209&PID_B386\" + parts[2], 0U) != 0)
                    return new NpV2UsbRoute(-1, null);
                var key = new DevPropKey { Format = LocationPathsFormat, PropertyId = 37U };
                byte[] buffer = new byte[4096]; uint bytes = (uint)buffer.Length, type;
                if (GetDevNodeProperty(devInst, ref key, out type, buffer, ref bytes, 0U) != 0 ||
                    type != DevPropStringList || bytes < 4U || bytes > buffer.Length || (bytes & 1U) != 0U)
                    return new NpV2UsbRoute(-1, null);
                foreach (string location in Encoding.Unicode.GetString(buffer, 0, (int)bytes).Split('\0'))
                    if (location.IndexOf("#USBROOT(", StringComparison.OrdinalIgnoreCase) >= 0)
                        return FromLocationPath(location);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (System.Security.SecurityException) { }
            return new NpV2UsbRoute(-1, null);
        }
    }

    internal sealed class NpV2UsbDevice : IDisposable
    {
        internal const int BlockBytes = 46592;
        private const uint PipeTransferTimeout = 3U;
        private const uint AutoClearStall = 2U;
        private const uint AutoFlush = 6U;
        private const uint RawIo = 7U;
        private const uint MaximumTransferSize = 8U;
        private const uint DigcfPresent = 0x00000002U;
        private const uint DigcfDeviceInterface = 0x00000010U;
        private const int ErrorIoPending = 997;
        private const int ErrorOperationAborted = 995;
        private const int ErrorIoIncomplete = 996;
        private SafeFileHandle handle;
        private IntPtr usb;
        private readonly byte pipe;
        private bool disposed;

        [DllImport("winusb.dll", EntryPoint = "WinUsb_SetPipePolicy", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetPipePolicyByte(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref byte value);
        [DllImport("winusb.dll", EntryPoint = "WinUsb_GetPipePolicy", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPipePolicyUInt(IntPtr interfaceHandle, byte pipeId, uint policyType, ref uint valueLength, out uint value);
        private static bool ReadPipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, IntPtr transferred, IntPtr overlapped) { return UsbHealthNative.ReadDeferred(interfaceHandle,pipeId,buffer,bufferLength,transferred,overlapped); }
        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterfaceData { internal int Size; internal Guid Guid; internal int Flags; internal IntPtr Reserved; }
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid interfaceGuid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr info, IntPtr device, ref Guid interfaceGuid, uint index, ref DeviceInterfaceData data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info, ref DeviceInterfaceData data, IntPtr detail, uint detailBytes, out uint required, IntPtr deviceInfo);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);

        private NpV2UsbDevice(SafeFileHandle file, IntPtr interfaceHandle, byte bulkInPipe)
        {
            handle = file; usb = interfaceHandle; pipe = bulkInPipe;
        }

        internal static IList<string> EnumerateDevicePaths()
        {
            var result = new List<string>();
            /* Keep this direct Unicode SetupAPI enumeration local to V2. The
             * older console helper predates the V2 PID and has been observed to
             * return no B386 path on some 64-bit runtimes despite a usable
             * WinUSB interface. Do not make discovery depend on the NP1 path. */
            Guid guid = USB512WinUsbNative.NeuropixelsInterfaceGuid;
            IntPtr info = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (info != new IntPtr(-1))
            {
                try
                {
                    for (uint index = 0; ; index++)
                    {
                        var data = new DeviceInterfaceData { Size = Marshal.SizeOf(typeof(DeviceInterfaceData)) };
                        if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref guid, index, ref data)) break;
                        uint required;
                        SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                        if (required == 0U) continue;
                        IntPtr detail = Marshal.AllocHGlobal(checked((int)required));
                        try
                        {
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                            if (SetupDiGetDeviceInterfaceDetail(info, ref data, detail, required, out required, IntPtr.Zero))
                            {
                                string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                                if (!IsV2Path(path) || result.Contains(path)) continue;
                                /* PID B386 is shared with the earlier XE_NP
                                 * protocol. Classify by the harmless ABI4
                                 * service-status request, never by PID alone. */
                                try { using (var candidate = Open(path)) { if (candidate.GetServiceStatus().Valid) result.Add(path); } }
                                catch (Exception) { }
                            }
                        }
                        finally { Marshal.FreeHGlobal(detail); }
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(info); }
            }
            /* On affected .NET Framework installations, SetupAPI may omit the
             * present WinUSB B386 interface although CreateFile succeeds. The
             * DeviceClasses key contains the documented symbolic-link spelling.
             * Probe each candidate with the harmless ABI4 status transfer; stale
             * entries and NP1 B386 interfaces are rejected before the UI sees it. */
            if (result.Count == 0)
            {
                try
                {
                    string keyPath = @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{" + guid.ToString() + "}";
                    using (RegistryKey classes = Registry.LocalMachine.OpenSubKey(keyPath, false))
                    {
                        if (classes != null) foreach (string name in classes.GetSubKeyNames())
                        {
                            if (!name.StartsWith("##?#", StringComparison.OrdinalIgnoreCase)) continue;
                            string path = "\\\\?\\" + name.Substring(4);
                            if (!IsV2Path(path) || result.Contains(path)) continue;
                            try { using (var candidate = Open(path)) { if (candidate.GetServiceStatus().Valid) result.Add(path); } }
                            catch (Exception) { }
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
            }
            return result;
        }

        internal static bool IsV2Path(string path)
        {
            return path != null && path.IndexOf("vid_1209&pid_b386", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static NpV2UsbDevice Open(string path)
        {
            if (!IsV2Path(path)) throw new InvalidOperationException("V2 USB requires the B386 WinUSB interface.");
            SafeFileHandle file = USB512WinUsbNative.CreateFile(path,
                USB512WinUsbNative.GenericRead | USB512WinUsbNative.GenericWrite,
                USB512WinUsbNative.FileShareRead | USB512WinUsbNative.FileShareWrite,
                IntPtr.Zero, USB512WinUsbNative.OpenExisting,
                USB512WinUsbNative.FileAttributeNormal | USB512WinUsbNative.FileFlagOverlapped, IntPtr.Zero);
            if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile for V2 WinUSB failed");
            IntPtr interfaceHandle;
            if (!USB512WinUsbNative.WinUsbInitialize(file, out interfaceHandle, path))
            {
                file.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize for V2 failed");
            }
            try
            {
                USB512WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor;
                if (!USB512WinUsbNative.WinUsbQueryInterfaceSettings(interfaceHandle, 0, out descriptor))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings for V2 failed");
                byte bulkIn = 0;
                ushort bulkPacketBytes = 0;
                for (byte index = 0; index < descriptor.bNumEndpoints; index++)
                {
                    USB512WinUsbNative.WINUSB_PIPE_INFORMATION info;
                    if (USB512WinUsbNative.WinUsbQueryPipe(interfaceHandle, 0, index, out info) &&
                        info.PipeType == USB512WinUsbNative.USBD_PIPE_TYPE.Bulk && (info.PipeId & 0x80) != 0)
                    {
                        bulkIn = info.PipeId;
                        bulkPacketBytes = info.MaximumPacketSize;
                        break;
                    }
                }
                if (bulkIn != 0x81) throw new IOException("V2 bulk-IN endpoint 0x81 was not found.");
                if (bulkPacketBytes != 512U) throw new IOException("V2 recording requires a high-speed 512-byte bulk endpoint.");
                return new NpV2UsbDevice(file, interfaceHandle, bulkIn);
            }
            catch
            {
                USB512WinUsbNative.WinUsbFree(interfaceHandle);
                file.Dispose();
                throw;
            }
        }

        internal NpV2ServiceStatus GetServiceStatus()
        {
            NpV2ServiceStatus status = NpV2ServiceStatus.Parse(ReadControl(0x6E, 32));
            if (!status.Valid) throw new IOException("This B386 device is not the V2 ABI4 USB service.");
            return status;
        }

        internal NpV2CaptureStatus GetCaptureStatus() { return NpV2CaptureStatus.Parse(ReadControl(0x71, 128)); }
        internal byte[] GetSensorSnapshot() { return ReadControl(0x4A, 140); }
        // Firmware admits this opt-in only while STOPPED. Legacy clients never
        // issue it; an unsupported optional request must not block AP recording.
        internal void EnableSensorHeaders(bool enabled) { VendorOut(0x7C, (ushort)(enabled ? 1 : 0), 0); }
        internal NpV2SourceStatus GetSourceStatus() { return NpV2SourceStatus.Parse(ReadControl(0x73, 144)); }
        internal NpV2ProfileCapabilities GetProfileCapabilities() { return NpV2ProfileCapabilities.Parse(ReadControl(0x76, 32)); }
        internal byte[] ExchangeConfiguration(byte[] request)
        {
            if (request == null || request.Length != Np2ConfigClient.FrameBytes)
                throw new ArgumentException("NP2 configuration frame must be 64 bytes.");
            WriteControl(0x7A, 0, 0, request);
            uint transaction=NpV2ServiceStatus.U32(request,8); Stopwatch wait=Stopwatch.StartNew();
            while(wait.ElapsedMilliseconds<5000L)
            {
                byte[] reply=ReadControl(0x7B,Np2ConfigClient.FrameBytes);
                try {
                    var receipt=Np2ConfigClient.Parse(reply,request[5],transaction);
                    /* WRITE chunks intentionally share op+transaction. A valid
                     * previous chunk's ACK is stale until its byte offset also
                     * matches (the same rule used by BLE receipt matching). */
                    if(!ConfigurationOffsetMatches(receipt,request)){
                        Thread.Sleep(10);continue;
                    }
                    return reply;
                }
                catch(IOException) { Thread.Sleep(10); }
            }
            throw new TimeoutException("No fresh matching NP2 configuration receipt; configuration outcome is unknown and was not replayed.");
        }
        internal static bool ConfigurationOffsetMatches(Np2ConfigReply receipt,byte[] request)
        {
            return request[5]!=Np2ConfigClient.Write || receipt.Error!=0 ||
                receipt.Received==NpV2ServiceStatus.U32(request,12)+NpV2ServiceStatus.U32(request,16);
        }
        internal NpV2UsbSelfTestReport GetSelfTestReport(string expectedUid)
        {
            GetServiceStatus().RequireUid(expectedUid);
            var report=NpV2UsbSelfTestReport.Parse(ReadControl(0x79,256));report.RequireUid(expectedUid);return report;
        }

        internal static bool CanStartSelfTest(NpV2ServiceStatus service, NpV2CaptureStatus capture, NpV2UsbSelfTestReport report)
        {
            bool retainedHold = report != null && report.Holding && (report.State == NpV2UsbSelfTestReport.Done || report.State == NpV2UsbSelfTestReport.Held);
            return service != null && service.Valid && service.Mode == 2U && (service.Busy == 0U || retainedHold) &&
                capture != null && capture.Valid && capture.State == 0U && capture.SourceState == 0U &&
                report != null && report.State != NpV2UsbSelfTestReport.Queued && report.State != NpV2UsbSelfTestReport.Running;
        }

        internal uint StartSelfTest(uint command,string expectedUid)
        {
            if (command < 1U || command > 5U) throw new ArgumentOutOfRangeException("command");
            NpV2UsbSelfTestReport baseline = GetSelfTestReport(expectedUid);
            if (!CanStartSelfTest(GetServiceStatus(), GetCaptureStatus(), baseline))
                throw new IOException("V2 USB self-test requires idle USB-stream mode; no mode switch, reset, or retry was sent.");
            VendorOut(0x78, checked((ushort)command), 0);
            return baseline.Sequence;
        }

        internal NpV2ProfileCapabilities ConfigureProfile(uint probeMask, uint sampleHz)
        {
            NpV2ProfileCapabilities.RequireUsbBandwidth(probeMask, sampleHz);
            NpV2ProfileCapabilities capabilities = GetProfileCapabilities();
            if (!capabilities.SupportsProbeMask(probeMask))
                throw new IOException("The selected NP2 connector combination has no optimized firmware codec. Select connector 1, connector 2, connectors 1+2, or all three connectors.");
            if (!capabilities.SupportsRate(sampleHz) || sampleHz % 1000U != 0U)
                throw new IOException("The selected NP2 sampling rate is not available in this firmware build.");
            VendorOut(0x75, (ushort)probeMask, (ushort)(sampleHz / 1000U));
            capabilities = GetProfileCapabilities();
            if (capabilities.SelectedProbeMask != probeMask || capabilities.SampleHz != sampleHz)
                throw new IOException("The V2 firmware did not retain the requested NP2 runtime profile.");
            return capabilities;
        }

        internal void RequestStreamMode()
        {
            NpV2ServiceStatus status = GetServiceStatus();
            if (status.Busy != 0U) throw new IOException("V2 is busy and cannot switch to USB-stream mode.");
            VendorOut(0x6F, 2, 0);
        }

        internal void RequestDownloadMode()
        {
            NpV2ServiceStatus status = GetServiceStatus();
            if (status.Busy != 0U) throw new IOException("V2 is busy and cannot switch to USB-download mode.");
            VendorOut(0x6F, 3, 0);
        }

        internal void RequestSdMode()
        {
            NpV2ServiceStatus status = GetServiceStatus();
            if (status.Mode != 2U || status.Busy != 0U) throw new IOException("Enter SD mode only from idle V2 USB stream mode.");
            VendorOut(0x6F, 1, 0);
        }

        internal void PrepareCapturePipe(int maximumRequestBytes)
        {
            if (maximumRequestBytes <= 0 || maximumRequestBytes % 512 != 0)
                throw new ArgumentOutOfRangeException("maximumRequestBytes");
            uint maximumBytes;
            uint maximumLength = 4U;
            if (!GetPipePolicyUInt(usb, pipe, MaximumTransferSize, ref maximumLength, out maximumBytes) ||
                maximumLength != 4U || maximumBytes < maximumRequestBytes)
                throw new IOException("The WinUSB host controller cannot accept the V2 recording request size.");
            uint timeout = 0U;
            if (!USB512WinUsbNative.WinUsbSetPipePolicy(usb, pipe, PipeTransferTimeout, 4, ref timeout))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 USB timeout policy failed");
            byte disabled = 0;
            if (!SetPipePolicyByte(usb, pipe, AutoClearStall, 1, ref disabled))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 USB automatic-stall recovery policy failed");
            if (!SetPipePolicyByte(usb, pipe, AutoFlush, 1, ref disabled))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 USB automatic-flush policy failed");
            byte raw = 1;
            if (!SetPipePolicyByte(usb, pipe, RawIo, 1, ref raw))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 USB RAW_IO policy failed");
            if (!USB512WinUsbNative.WinUsbResetPipe(usb, pipe))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 USB reset-pipe failed");
            if (!USB512WinUsbNative.WinUsbFlushPipe(usb, pipe))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 USB flush-pipe failed");
        }

        internal void Start(uint blocks)
        {
            if (blocks > 1000000U) throw new ArgumentOutOfRangeException("blocks");
            NpV2ServiceStatus status = GetServiceStatus();
            if (status.Mode != 2U || status.Busy != 0U) throw new IOException("V2 must be idle USB-stream mode before recording.");
            NpV2CaptureStatus source = GetCaptureStatus();
            if (source.State != 0U || source.SourceState != 0U)
                throw new IOException("NP2 source is one-shot per boot. Reset V2 into USB stream mode before another recording.");
            if (source.Format == 3U) NpV2ProfileCapabilities.RequireUsbBandwidth(source.ProbeMask, source.SampleHz);
            VendorOut(0x70, (ushort)blocks, (ushort)(blocks >> 16));
        }

        internal void ArmCapture() { VendorOut(0x77, 0, 0); }
        internal void Cancel() { VendorOut(0x72, 0, 0); }
        internal void AbortAndRetire(NpV2ReadSlot[] slots)
        {
            if (usb == IntPtr.Zero) return;
            if (!USB512WinUsbNative.WinUsbAbortPipe(usb, pipe))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 USB abort-pipe failed");
            foreach (NpV2ReadSlot slot in slots)
            {
                if (slot == null || !slot.Active) continue;
                uint ignored;
                bool retired = USB512WinUsbNative.WinUsbGetOverlappedResult(usb, slot.Overlapped, out ignored, true);
                int error = retired ? 0 : Marshal.GetLastWin32Error();
                if (!retired && error == ErrorIoIncomplete)
                {
                    throw new IOException("V2 USB cancellation is incomplete; native request memory will be retained.");
                }
                /* ERROR_OPERATION_ABORTED and terminal controller errors both
                 * certify that this blocking harvest has retired the native
                 * request. Only ERROR_IO_INCOMPLETE keeps its storage alive. */
                slot.Active = false;
            }
        }

        internal void QueueRead(NpV2ReadSlot slot, int bytes)
        {
            if (bytes <= 0 || bytes > slot.Buffer.Length || bytes % BlockBytes != 0) throw new ArgumentOutOfRangeException("bytes");
            if (!USB512WinUsbNative.ResetEvent(slot.Event)) throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 reset-read-event failed");
            Marshal.StructureToPtr(new USB512WinUsbNative.NATIVE_OVERLAPPED { Event = slot.Event }, slot.Overlapped, false);
            slot.RequestedBytes = bytes;
            if (!ReadPipe(usb, pipe, slot.Pointer, (uint)bytes, IntPtr.Zero, slot.Overlapped))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorIoPending) throw new Win32Exception(error, "V2 queue bulk read failed");
            }
            slot.Active = true;
        }

        internal int FinishRead(NpV2ReadSlot slot)
        {
            uint transferred;
            /* The event may be signalled just before WinUSB publishes the final
             * completion state. Harvest it synchronously instead of converting
             * that ERROR_IO_INCOMPLETE race into a recording fault. */
            if (!USB512WinUsbNative.WinUsbGetOverlappedResult(usb, slot.Overlapped, out transferred, true))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 finish bulk read failed");
            slot.Active = false;
            if (!USB512WinUsbNative.ResetEvent(slot.Event)) throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 consume-read-event failed");
            /* A bulk request may legally complete short. Preserve every whole
             * firmware block it returned and let the caller replenish the
             * missing extent instead of treating the short packet as fatal. */
            if (transferred == 0U || transferred % BlockBytes != 0U)
                throw new IOException("V2 unusable short read: " + transferred.ToString(CultureInfo.InvariantCulture) + "/" + slot.RequestedBytes.ToString(CultureInfo.InvariantCulture));
            return checked((int)transferred);
        }

        private byte[] ReadControl(byte request, ushort length)
        {
            byte[] bytes = new byte[length]; uint received;
            var setup = new USB512WinUsbNative.WINUSB_SETUP_PACKET { RequestType = 0xC0, Request = request, Length = length };
            if (!USB512WinUsbNative.WinUsbControlTransfer(usb, setup, bytes, length, out received, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 control IN 0x" + request.ToString("X2", CultureInfo.InvariantCulture) + " failed");
            if (received != length) throw new IOException("V2 control IN 0x" + request.ToString("X2", CultureInfo.InvariantCulture) + " was truncated.");
            return bytes;
        }

        private void VendorOut(byte request, ushort value, ushort index)
        {
            WriteControl(request, value, index, null);
        }

        private void WriteControl(byte request, ushort value, ushort index, byte[] data)
        {
            uint sent;
            ushort length = checked((ushort)(data == null ? 0 : data.Length));
            var setup = new USB512WinUsbNative.WINUSB_SETUP_PACKET { RequestType = 0x40, Request = request, Value = value, Index = index, Length = length };
            if (!USB512WinUsbNative.WinUsbControlTransfer(usb, setup, data ?? new byte[0], length, out sent, IntPtr.Zero) || sent != length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 control OUT 0x" + request.ToString("X2", CultureInfo.InvariantCulture) + " failed");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (usb != IntPtr.Zero) { USB512WinUsbNative.WinUsbFree(usb); usb = IntPtr.Zero; }
            if (handle != null) { handle.Dispose(); handle = null; }
        }
    }

    internal sealed class NpV2ReadSlot : IDisposable
    {
        internal readonly int Index;
        internal readonly byte[] Buffer;
        internal readonly IntPtr Event;
        internal readonly IntPtr Overlapped;
        internal readonly IntPtr Pointer;
        internal bool Active;
        internal int RequestedBytes;
        internal long Issue;
        internal long CompletedAtTicks;
        private GCHandle pinned;

        internal NpV2ReadSlot(int index, int bytes)
        {
            Index = index; Buffer = new byte[bytes];
            pinned = GCHandle.Alloc(Buffer, GCHandleType.Pinned); Pointer = pinned.AddrOfPinnedObject();
            Event = USB512WinUsbNative.CreateEvent(IntPtr.Zero, true, false, null);
            if (Event == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 read event failed");
            Overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(USB512WinUsbNative.NATIVE_OVERLAPPED)));
        }
        public void Dispose()
        {
            /* An active OVERLAPPED may still be referenced by the Windows USB
             * stack. Leak this small slot deliberately if cancellation could
             * not be certified; freeing it would create a native use-after-free. */
            if (Active) return;
            if (Overlapped != IntPtr.Zero) Marshal.FreeHGlobal(Overlapped);
            if (Event != IntPtr.Zero) USB512WinUsbNative.CloseHandle(Event);
            if (pinned.IsAllocated) pinned.Free();
        }
    }

    internal sealed class NpV2RecordingProgress
    {
        internal string Text;
        internal uint ReceivedBlocks;
        internal uint TargetBlocks;
        internal long VerifiedBlocks;
        internal double PayloadMbPerSecond;
        internal DateTime FirstReceiveUtc, LastReceiveUtc;
    }

    internal sealed class NpV2RecordingResult
    {
        internal bool Complete;
        internal bool CancelledBeforeCapture;
        internal string Error;
        internal string Warning;
        internal string Folder;
        internal uint TargetBlocks, ReceivedBlocks;
        internal long VerifiedBlocks, WireBytes, PayloadBytes;
        internal long DecodedDatRows;
        internal bool DecodedDatMerged;
        internal long RawDroppedBlocks, DecodedDroppedLaneBlocks;
        internal uint DecodedDatProbeMask, DecodedDatSampleHz;
        internal string PreviewError, RawWriterError, DecodeError;
        internal double Seconds, DataSeconds, MaxCompletionGapMs, MaxSlotRecycleMs;
        internal int HostBufferBudgetMb, ReadBufferCount, PeakReadBacklog, SharedBufferCount, PeakSharedBacklog;
        internal NpV2CaptureStatus FinalCapture;
        internal NpV2SourceStatus FinalSource;
        internal NpV2WireVerifier Verifier;
        internal DateTime FirstReceiveUtc, LastReceiveUtc;
#if !NP2_CONSOLE_STANDALONE
        internal long OptionalImuRecords,OptionalImuSamples;
        internal string OptionalImuError;
        internal NpImuDisplay OptionalImuDisplay;
#endif
    }

    // File-only finalization, never called while USB capture is active. Keep the
    // per-probe originals: unequal tails and recovery gaps cannot be invented.
    internal static class NpV2DatMerge
    {
        internal static long Run(string folder, uint mask, uint sampleHz, int[] phases, bool segmentComplete, Func<bool> cancelled)
        {
            if (mask < 1 || mask > 7 || (mask & (mask - 1)) == 0 || sampleHz == 0 || phases == null || phases.Length != 3)
                throw new ArgumentException("NP2 merge requires multiple probes and their recorded clock phases.");
            var ports = new List<int>();
            for (int p = 1; p <= 3; p++) if ((mask & (1U << (p - 1))) != 0) {
                if (phases[p - 1] < 0 || phases[p - 1] >= 1600) throw new InvalidDataException("NP2 merge phase is unavailable.");
                ports.Add(p);
            }
            string output = Path.Combine(folder, "neuropixels_ap.dat"), partial = output + ".partial";
            if (File.Exists(output) || File.Exists(partial)) throw new IOException("NP2 merge output already exists; originals were not changed.");
            var inputs = new List<FileStream>();
            var rows = new List<long>();
            long common = Int64.MaxValue;
            bool created = false;
            try {
                foreach (int p in ports) {
                    var input = new FileStream(Path.Combine(folder, "probe" + p, "neuropixels_ap.dat"), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                    inputs.Add(input);
                    if (input.Length % 768 != 0) throw new InvalidDataException("NP2 probe " + p + " has a partial DAT row.");
                    rows.Add(input.Length / 768); common = Math.Min(common, input.Length / 768);
                }
                if (common == 0) throw new InvalidDataException("NP2 merge has no common complete row.");
                const int batchRows = 1024;
                var source = new byte[ports.Count][];
                for (int p = 0; p < source.Length; p++) source[p] = new byte[batchRows * 768];
                var merged = new byte[batchRows * 768 * ports.Count];
                using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan)) {
                    created = true;
                    for (long row = 0; row < common; row += batchRows) {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException("NP2 merge cancelled; per-probe files retained.");
                        int count = (int)Math.Min(batchRows, common - row), bytes = count * 768;
                        for (int p = 0; p < inputs.Count; p++) {
                            int read = 0;
                            while (read < bytes) {
                                int n = inputs[p].Read(source[p], read, bytes - read);
                                if (n == 0) throw new EndOfStreamException("NP2 source DAT changed during merge.");
                                read += n;
                            }
                        }
                        for (int r = 0; r < count; r++) for (int p = 0; p < inputs.Count; p++)
                            Buffer.BlockCopy(source[p], r * 768, merged, (r * inputs.Count + p) * 768, 768);
                        destination.Write(merged, 0, bytes * inputs.Count);
                    }
                    destination.Flush(true);
                }
                if (cancelled != null && cancelled()) throw new OperationCanceledException("NP2 merge cancelled; per-probe files retained.");
                bool calibrated=File.Exists(Path.Combine(folder,"adc_calibration.json"));
                var report = new List<string> {
                    "merge_complete=True", "recording_segment_complete=" + segmentComplete,
                    "file=neuropixels_ap.dat", "channels=" + ports.Count * 384, "rows=" + common,
                    "sample_hz=" + sampleHz, "sample_encoding=" + (calibrated?"signed_calibrated_nominal_adc_counts":NpV2SampleDecoder.SampleEncoding),
                    "channel_order=ascending physical probe; contiguous hardware channels 0..383 per probe",
                    "alignment=first complete SuperSync row per probe; phase offsets retained, not corrected; NOT proof of simultaneous ADC samples",
                    "source_tick_hz=" + sampleHz * 1600UL,
                    "tails=only common complete rows merged; every per-probe source and unmatched tail retained",
                    "gaps=never merged across recovery segments; no padding, interpolation or invented samples"
                };
                if(calibrated)report.Add("adc_calibration_file=adc_calibration.json; per-probe host correction retained; no additional correction during merge");
                for (int p = 0; p < ports.Count; p++) report.Add("probe" + ports[p] + "_first_row_source_tick=" + (1600 - phases[ports[p] - 1]) % 1600 + "; source_rows=" + rows[p] + "; unmerged_tail_rows=" + (rows[p] - common));
                File.Move(partial, output);
                File.WriteAllLines(Path.Combine(folder, "np2_merge_status.txt"), report);
                return common;
            } catch {
                if (created && File.Exists(partial)) File.Delete(partial); // Only our unpublished temporary, never source data.
                throw;
            } finally { foreach (var input in inputs) input.Dispose(); }
        }
    }

    /* NP2 USB blocks preserve three serial 12-bit lanes.  Decode only blocks
     * already accepted by NpV2WireVerifier: 100 ticks form a frame and sixteen
     * frames produce one 384-channel AP row.  This is not the NP1 AP/LFP wire
     * format and no synthetic LFP output is created. */
    internal sealed class NpV2SampleDecoder
    {
        // Undo NP2's inverting amplifier once, after extracting raw ADC bits.
        // Transport, framing and original captures remain byte-identical.
        internal static short NominalCount(int raw12) { return (short)(2048 - raw12); }
        internal const string SampleEncoding = "np2_signed_nominal_v1";
        internal const string SampleTransform = "2048 - raw12";
        internal const int Channels = 384;
        private const int HeaderBytes = 512;
        private readonly Action<uint, uint> onFormat;
        private readonly Action<int, short[]> onRow;
        private readonly int onlyProbe;
        private readonly ulong originTick;
        private readonly ProbeState[] probes = new ProbeState[3];
        private bool initialized;
        private int formatVersion, storedBits, ticksPerBlock;
        private uint probeMask, liveProbeMask, codec, sampleHz, d8PhaseBits;
        private bool rawD8Phase;
        private byte[] unpacked;
        private ulong nextTick;
        private static readonly ushort[] QuadWords = MakeQuadWords();

        private sealed class ProbeState
        {
            internal readonly int Probe, Phase;
            private readonly Action<int, short[]> onRow;
            private readonly short[] row = new short[Channels];
            private readonly byte[] partial = new byte[100];
            private int skip, partialCount, frame;

            internal ProbeState(int probe, int phase, ulong originTick, Action<int, short[]> onRow)
            {
                Probe = probe;
                Phase = phase;
                this.onRow = onRow;
                skip = (int)((1600UL - ((originTick + (ulong)phase) % 1600UL)) % 1600UL);
            }

            internal void Feed(byte[] ticks, int count)
            {
                int at = Math.Min(skip, count); skip -= at;
                if (partialCount != 0)
                {
                    int take = Math.Min(100 - partialCount, count - at);
                    Buffer.BlockCopy(ticks, at, partial, partialCount, take);
                    partialCount += take; at += take;
                    if (partialCount == 100) { DecodeFrame(partial, 0); partialCount = 0; }
                }
                while (at + 100 <= count) { DecodeFrame(ticks, at); at += 100; }
                if (at < count)
                {
                    partialCount = count - at;
                    Buffer.BlockCopy(ticks, at, partial, 0, partialCount);
                }
            }

            private void DecodeFrame(byte[] ticks, int start)
            {
                // Transpose four chronological three-bit ticks at once. Three
                // lookups yield all three 12-bit ADC words, MSB first. Channel
                // order matches V2SdLogger/analyze_np2_pssi_diag.py.
                for (int slot = 0, at = start + 4; slot < 8; slot++, at += 12)
                {
                    int a = QuadWords[ticks[at] | ticks[at + 1] << 3 | ticks[at + 2] << 6 | ticks[at + 3] << 9];
                    int b = QuadWords[ticks[at + 4] | ticks[at + 5] << 3 | ticks[at + 6] << 6 | ticks[at + 7] << 9];
                    int c = QuadWords[ticks[at + 8] | ticks[at + 9] << 3 | ticks[at + 10] << 6 | ticks[at + 11] << 9];
                    int channel = (slot / 2) * 32 + (slot & 1) + frame * 2;
                    row[channel] = NominalCount(((a & 15) << 8) | ((b & 15) << 4) | (c & 15));
                    row[channel + 128] = NominalCount(((a & 240) << 4) | (b & 240) | ((c >> 4) & 15));
                    row[channel + 256] = NominalCount((a & 3840) | ((b >> 4) & 240) | (c >> 8));
                }
                if (++frame == 16) { onRow(Probe, row); frame = 0; }
            }
        }

        internal NpV2SampleDecoder(Action<uint, uint> onFormat, Action<int, short[]> onRow)
            : this(onFormat, onRow, 0)
        { }

        internal NpV2SampleDecoder(Action<uint, uint> onFormat, Action<int, short[]> onRow,
            ulong originSequence, ulong originTick)
            : this(onFormat, onRow, 0, originTick)
        {
            if (originSequence > UInt64.MaxValue / 61440UL || originTick != originSequence * 61440UL)
                throw new ArgumentOutOfRangeException("originTick");
        }

        internal NpV2SampleDecoder(Action<uint, uint> onFormat, Action<int, short[]> onRow, int onlyProbe)
            : this(onFormat, onRow, onlyProbe, 0UL)
        { }

        private NpV2SampleDecoder(Action<uint, uint> onFormat, Action<int, short[]> onRow, int onlyProbe, ulong originTick)
        {
            if (onFormat == null || onRow == null) throw new ArgumentNullException();
            if (onlyProbe < 0 || onlyProbe > 3) throw new ArgumentOutOfRangeException("onlyProbe");
            this.onFormat = onFormat; this.onRow = onRow; this.onlyProbe = onlyProbe; this.originTick = originTick;
        }

        internal uint LiveProbeMask { get { return liveProbeMask; } }
        internal uint SampleHz { get { return sampleHz; } }

        internal void Feed(byte[] bytes, int offset, NpV2WireVerifier verifier)
        {
            if (bytes == null || verifier == null || offset < 0 || offset > bytes.Length - NpV2UsbDevice.BlockBytes)
                throw new ArgumentOutOfRangeException("bytes");
            ulong startTick = U64(bytes, offset + 32);
            if (!initialized && startTick != originTick) throw new InvalidDataException("NP2 DAT first source tick differs from immutable origin.");
            if (!initialized) Initialize(verifier);
            else if (formatVersion != verifier.FormatVersion || storedBits != verifier.StoredBits || ticksPerBlock != verifier.TicksPerBlock ||
                     probeMask != verifier.ProbeMask || liveProbeMask != verifier.LiveProbeMask || codec != verifier.Codec || sampleHz != verifier.SampleHz ||
                     rawD8Phase != verifier.D8UsesRawPhase || d8PhaseBits != verifier.D8PhaseBits)
                throw new InvalidDataException("NP2 decoded DAT contract changed mid-recording.");
            if (startTick != nextTick) throw new InvalidDataException("NP2 DAT source ticks are discontinuous.");
            for (int probe = 0; probe < probes.Length; probe++)
            {
                ProbeState state = probes[probe];
                if (state == null) continue;
                UnpackLane(bytes, offset + HeaderBytes, probe);
                state.Feed(unpacked, ticksPerBlock);
            }
            nextTick = startTick + (uint)ticksPerBlock;
        }

        internal void Complete() { }

        private void Initialize(NpV2WireVerifier verifier)
        {
            if (verifier.FormatVersion < 1 || verifier.FormatVersion > 3 || verifier.SampleHz == 0U || verifier.ProbeMask == 0U ||
                verifier.LiveProbeMask == 0U || (verifier.LiveProbeMask & ~verifier.ProbeMask) != 0U)
                throw new InvalidDataException("NP2 USB stream has no decodable live probe profile.");
            formatVersion = verifier.FormatVersion; storedBits = verifier.StoredBits; ticksPerBlock = verifier.TicksPerBlock;
            probeMask = verifier.ProbeMask; liveProbeMask = verifier.LiveProbeMask; codec = verifier.Codec; sampleHz = verifier.SampleHz;
            d8PhaseBits = verifier.D8PhaseBits; rawD8Phase = verifier.D8UsesRawPhase;
            if ((storedBits != 3 && storedBits != 6 && storedBits != 8 && storedBits != 9) || ticksPerBlock <= 0)
                throw new InvalidDataException("NP2 USB decoder received an unsupported packed lane width.");
            for (int probe = 0; probe < 3; probe++) if ((liveProbeMask & (1U << probe)) != 0U &&
                (onlyProbe == 0 || onlyProbe == probe + 1))
            {
                int phase = verifier.PhaseForProbe(probe);
                if (phase < 0 || phase >= 1600) throw new InvalidDataException("NP2 USB decoder has no validated SuperSync phase.");
                probes[probe] = new ProbeState(probe + 1, phase, originTick, onRow);
            }
            nextTick = originTick;
            initialized = true;
            unpacked = new byte[ticksPerBlock];
            onFormat(liveProbeMask, sampleHz);
        }

        private void UnpackLane(byte[] bytes, int payloadOffset, int probe)
        {
            byte[] output = unpacked;
            if (codec == 5U || (codec == 6U && probe < 2))
            {
                int shift = probe * 3;
                for (int tick = 0; tick < ticksPerBlock; tick++) output[tick] = (byte)((bytes[payloadOffset + tick] >> shift) & 7);
                return;
            }
            if (codec == 6U)
            {
                int sidecar = payloadOffset + ticksPerBlock;
                if (!rawD8Phase)
                {
                    for (int tick = 0; tick < ticksPerBlock; tick++)
                        output[tick] = (byte)((bytes[payloadOffset + tick] >> 6) | (((bytes[sidecar + tick / 8] >> (tick & 7)) & 1) << 2));
                }
                else
                {
                    int headerOffset = payloadOffset - HeaderBytes;
                    for (int tick = 0; tick < ticksPerBlock; tick++)
                    {
                        int bit = tick + (int)d8PhaseBits;
                        int d8 = bit < ticksPerBlock
                            ? (bytes[sidecar + bit / 8] >> (bit & 7)) & 1
                            : (bytes[headerOffset + 184 + (bit - ticksPerBlock) / 8] >> ((bit - ticksPerBlock) & 7)) & 1;
                        output[tick] = (byte)((bytes[payloadOffset + tick] >> 6) | (d8 << 2));
                    }
                }
                return;
            }
            for (int tick = 0, at = payloadOffset; tick < ticksPerBlock; at += 3)
            {
                uint packed = (uint)(bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16);
                if (storedBits == 6)
                {
                    // Legacy NP2USB01 stores two lanes but exposes only lane 2.
                    // V3 packed-dual exposes both, in connector order.
                    int shift = formatVersion == 1 ? 3 : probe * 3;
                    output[tick++] = (byte)((packed >> shift) & 7); output[tick++] = (byte)((packed >> (shift + 6)) & 7);
                    output[tick++] = (byte)((packed >> (shift + 12)) & 7); output[tick++] = (byte)((packed >> (shift + 18)) & 7);
                }
                else
                {
                    output[tick++] = (byte)(packed & 7); output[tick++] = (byte)((packed >> 3) & 7);
                    output[tick++] = (byte)((packed >> 6) & 7); output[tick++] = (byte)((packed >> 9) & 7);
                    output[tick++] = (byte)((packed >> 12) & 7); output[tick++] = (byte)((packed >> 15) & 7);
                    output[tick++] = (byte)((packed >> 18) & 7); output[tick++] = (byte)((packed >> 21) & 7);
                }
            }
        }

        private static ushort[] MakeQuadWords()
        {
            var words = new ushort[4096];
            for (int value = 0; value < words.Length; value++)
                for (int line = 0; line < 3; line++) for (int tick = 0; tick < 4; tick++)
                    words[value] |= (ushort)(((value >> (tick * 3 + line)) & 1) << (line * 4 + 3 - tick));
            return words;
        }
        private static uint U32(byte[] bytes, int offset) { return NpV2ServiceStatus.U32(bytes, offset); }
        private static ulong U64(byte[] bytes, int offset) { return (ulong)U32(bytes, offset) | ((ulong)U32(bytes, offset + 4) << 32); }
    }

    internal sealed class NpV2BufferPolicy
    {
        internal readonly int BudgetMb, ReadBuffers, SharedBlocks, WriteBuffersPerProbe;
        internal const int WriteChunkBytes = 1536 * 1024; // 2048 complete 384-channel rows
        internal NpV2BufferPolicy(int budgetMb)
        {
            if (budgetMb < 64 || budgetMb > 2048) throw new ArgumentOutOfRangeException("budgetMb", "Use 64..2048 MiB for NP2 host buffering.");
            BudgetMb = budgetMb;
            long bytes = budgetMb * 1024L * 1024L;
            ReadBuffers = (int)(bytes / 4 / (4 * NpV2UsbDevice.BlockBytes));
            SharedBlocks = (int)(bytes / 2 / NpV2UsbDevice.BlockBytes);
            WriteBuffersPerProbe = Math.Max(2, (int)(bytes / 4 / 3 / WriteChunkBytes));
        }
        internal static NpV2BufferPolicy ForHost()
        {
            int mb;
            string configured = Environment.GetEnvironmentVariable("NP2_HOST_BUFFER_MB");
            if (!String.IsNullOrWhiteSpace(configured))
            {
                if (!Int32.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out mb))
                    throw new ArgumentException("NP2_HOST_BUFFER_MB must be an integer in MiB (64..2048).");
            }
            else
            {
                mb = Environment.Is64BitProcess ? 512 : 128;
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
                // Reserve at most an eighth of presently available physical RAM
                // by default. Explicit budgets remain available on large hosts.
                if (GlobalMemoryStatusEx(ref memory)) mb = Math.Max(64, Math.Min(mb, (int)Math.Min(2048UL, memory.AvailablePhysical / (8UL * 1024UL * 1024UL))));
            }
            if (!Environment.Is64BitProcess && mb > 256) throw new ArgumentException("NP2 buffers above 256 MiB require a 64-bit process.");
            return new NpV2BufferPolicy(mb);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }

    // A single decoder owns the filling chunk; a separate disk thread owns
    // queued chunks. Pool memory is allocated before USB is armed. No row may
    // alias a chunk that is still being written, including during final drain.
    internal sealed class NpV2BatchedWriter : IDisposable
    {
        private sealed class Chunk
        {
            internal readonly byte[] Bytes; internal int Count;
            internal Chunk(int bytes) { Bytes = new byte[bytes]; }
        }
        private readonly BlockingCollection<Chunk> free, queued;
        private Chunk filling;
        private Stream stream;
        private Thread worker;
        private Exception failure;
        private bool completed;
        private long writtenBytes;
        private int channels=384;
        private readonly int chunkBytes;
        internal long WrittenBytes { get { return Interlocked.Read(ref writtenBytes); } }
        internal int PeakQueuedChunks { get; private set; }
        internal NpV2BatchedWriter(int buffers, int chunkBytes = NpV2BufferPolicy.WriteChunkBytes)
        {
            if (buffers < 2 || chunkBytes < 768 || chunkBytes % 768 != 0) throw new ArgumentOutOfRangeException("buffers");
            this.chunkBytes=chunkBytes;
            free = new BlockingCollection<Chunk>(buffers); queued = new BlockingCollection<Chunk>(buffers);
            for (int i = 0; i < buffers; i++) free.Add(new Chunk(chunkBytes));
        }
        internal void Open(Stream destination, string name, int channels=384)
        {
            if (stream != null || completed) throw new InvalidOperationException("NP2 DAT writer already opened or closed.");
            if(channels<384 || channels>1152 || channels%384!=0 || chunkBytes%(channels*2)!=0)
                throw new ArgumentOutOfRangeException("channels");
            this.channels=channels;
            stream = destination ?? throw new ArgumentNullException("destination");
            filling = free.Take();
            worker = new Thread(WriteLoop) { IsBackground = true, Name = name, Priority = ThreadPriority.Normal };
            worker.Start();
        }
        internal void WriteRow(short[] row)
        {
            ThrowIfFailed();
            if (completed || stream == null || row == null || row.Length != channels) throw new InvalidOperationException("Invalid NP2 DAT writer state or row.");
            if (filling == null)
            {
                while (!free.TryTake(out filling, 10)) ThrowIfFailed();
                filling.Count = 0;
            }
            int bytes=channels*2;
            Buffer.BlockCopy(row, 0, filling.Bytes, filling.Count, bytes); filling.Count += bytes;
            if (filling.Count == filling.Bytes.Length) Publish();
        }
        private void Publish()
        {
            queued.Add(filling); filling = null;
            PeakQueuedChunks = Math.Max(PeakQueuedChunks, queued.Count);
        }
        private void WriteLoop()
        {
            try
            {
                foreach (Chunk chunk in queued.GetConsumingEnumerable())
                {
                    try
                    {
                        if (Volatile.Read(ref failure) == null)
                        { stream.Write(chunk.Bytes, 0, chunk.Count); Interlocked.Add(ref writtenBytes, chunk.Count); }
                    }
                    catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); }
                    finally { free.Add(chunk); }
                }
                FileStream file = stream as FileStream;
                if (file != null) file.Flush(true); else stream.Flush();
            }
            catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); }
            finally
            {
                try { stream.Dispose(); } catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); }
            }
        }
        private void ThrowIfFailed()
        {
            Exception error = Volatile.Read(ref failure);
            if (error != null) throw new IOException("NP2 DAT disk write failed.", error);
        }
        internal void Abort(Exception error) { Interlocked.CompareExchange(ref failure,error,null); }
        internal void Complete()
        {
            if (!completed)
            {
                completed = true;
                if (filling != null && filling.Count != 0) Publish();
                queued.CompleteAdding();
                if (worker != null) worker.Join();
            }
            ThrowIfFailed();
        }
        public void Dispose()
        {
            try { Complete(); } finally { free.Dispose(); queued.Dispose(); }
        }
    }

    // Pair each probe's first complete SuperSync row, then preserve row order.
    // Phase offsets remain explicit; pairing does not correct ADC timing.
    internal sealed class NpV2LiveDatWriter : IDisposable
    {
        const int PendingRows=2048;
        sealed class Ring
        {
            internal readonly short[] Samples=new short[PendingRows*384];
            internal int Head,Count;
            internal long Produced;
        }
        readonly object sync=new object();
        readonly Ring[] rings=new Ring[4];
        readonly NpV2BatchedWriter output;
        readonly string folder;
        readonly int[] phases=new int[3];
        short[] row;
        uint mask,rate;
        Exception failure;
        bool completed;
        internal int ChannelCount {get{return row==null?0:row.Length;}}
        internal long WrittenRows {get{return ChannelCount==0?0:output.WrittenBytes/(ChannelCount*2);}}
        internal NpV2LiveDatWriter(string folder,NpV2BufferPolicy buffers)
        {
            this.folder=folder;
            // Replace one chunk per former probe writer with bounded row rings;
            // the combined writer stays within their existing memory allowance.
            output=new NpV2BatchedWriter(Math.Max(2,buffers.WriteBuffersPerProbe-1),NpV2BufferPolicy.WriteChunkBytes*3);
            for(int p=1;p<=3;p++)rings[p]=new Ring();
        }
        internal void Open(uint mask,uint rate,NpV2WireVerifier verifier)
        {
            this.mask=mask;this.rate=rate;int count=0;
            for(int p=1;p<=3;p++)if((mask&(1U<<(p-1)))!=0){count++;phases[p-1]=verifier.PhaseForProbe(p-1);}
            row=new short[count*384];
            output.Open(new FileStream(Path.Combine(folder,"neuropixels_ap.dat"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,1,FileOptions.SequentialScan),"NP2 combined AP disk writer",row.Length);
        }
        internal void Add(int probe,short[] samples)
        {
            lock(sync) {
                ThrowIfFailed();
                if(completed || probe<1 || probe>3 || (mask&(1U<<(probe-1)))==0 || samples==null || samples.Length!=384)
                    throw new InvalidDataException("Invalid live merged NP2 row.");
                var ring=rings[probe];
                long wait=Stopwatch.GetTimestamp();
                while(ring.Count==PendingRows){
                    if(Stopwatch.GetTimestamp()-wait>=Stopwatch.Frequency*10)throw new IOException("NP2 combined DAT probe queue remained full; decoded output cannot keep up.");
                    Monitor.Wait(sync,10);ThrowIfFailed();
                }
                int tail=(ring.Head+ring.Count)%PendingRows;
                Buffer.BlockCopy(samples,0,ring.Samples,tail*768,768);ring.Count++;ring.Produced++;
                Drain();Monitor.PulseAll(sync);
            }
        }
        void Drain()
        {
            while(true){
                for(int p=1;p<=3;p++)if((mask&(1U<<(p-1)))!=0 && rings[p].Count==0)return;
                int at=0;
                for(int p=1;p<=3;p++)if((mask&(1U<<(p-1)))!=0){var ring=rings[p];Buffer.BlockCopy(ring.Samples,ring.Head*768,row,at*2,768);at+=384;}
                output.WriteRow(row);
                for(int p=1;p<=3;p++)if((mask&(1U<<(p-1)))!=0){var ring=rings[p];ring.Head=(ring.Head+1)%PendingRows;ring.Count--;}
            }
        }
        void ThrowIfFailed(){var error=Volatile.Read(ref failure);if(error!=null)throw new IOException("NP2 live combined DAT disabled.",error);}
        internal void Abort(Exception error){Interlocked.CompareExchange(ref failure,error,null);output.Abort(error);}
        internal void Complete()
        {
            if(completed)return;
            completed=true;
            output.Complete();
            ThrowIfFailed();
            // A clean Stop can leave one probe with extra complete rows. Preserve
            // those small tails and exact source ticks; never pad the main DAT.
            for(int p=1;p<=3;p++)if((mask&(1U<<(p-1)))!=0 && rings[p].Count!=0){
                string tails=Path.Combine(folder,"unpaired_tail");Directory.CreateDirectory(tails);
                string stem=Path.Combine(tails,"neuropixels_ap_probe"+p);
                using(var data=new FileStream(stem+".dat",FileMode.CreateNew,FileAccess.Write))
                using(var ticks=new BinaryWriter(new FileStream(stem+"_source_ticks.dat",FileMode.CreateNew,FileAccess.Write))){
                    var bytes=new byte[768];var ring=rings[p];
                    for(int n=0;n<ring.Count;n++){
                        Buffer.BlockCopy(ring.Samples,((ring.Head+n)%PendingRows)*768,bytes,0,768);data.Write(bytes,0,768);
                        ticks.Write((ulong)((1600-phases[p-1])%1600)+(ulong)(ring.Produced-ring.Count+n)*1600UL);
                    }
                    data.Flush(true);
                }
            }
        }
        internal void WriteReport(bool recordingComplete,bool rawSaved=true)
        {
            if(row==null)return;
            var report=new List<string>{"merge_mode=live","file=neuropixels_ap.dat","channels="+ChannelCount,"rows="+WrittenRows,"sample_hz="+rate,
                "merge_complete="+(Volatile.Read(ref failure)==null),"recording_segment_complete="+recordingComplete,
                "sample_encoding="+(File.Exists(Path.Combine(folder,"adc_calibration.json"))?"signed_calibrated_nominal_adc_counts":NpV2SampleDecoder.SampleEncoding),
                "channel_order=ascending physical probe; contiguous hardware channels 0..383 per probe",
                "alignment=first complete SuperSync row per probe; phase offsets retained, not corrected; NOT proof of simultaneous ADC samples",
                "gaps=one combined DAT per recovery segment; no padding, interpolation or invented samples",
                "tails=unpaired_tail retains unmatched complete rows and uint64 little-endian source ticks","raw_saved="+rawSaved};
            for(int p=1;p<=3;p++)if((mask&(1U<<(p-1)))!=0)report.Add("probe"+p+"_first_row_source_tick="+(1600-phases[p-1])%1600+"; decoded_rows="+rings[p].Produced+"; unpaired_tail_rows="+rings[p].Count);
            File.WriteAllLines(Path.Combine(folder,"np2_merge_status.txt"),report);
        }
        public void Dispose(){output.Dispose();}
    }

    /* Conversion is kept off the ordered raw verifier/logger. In best-effort
     * mode raw persistence always has priority: if decoded DAT/preview work falls
     * behind it is disabled, documented in the receipt, and USB capture keeps
     * draining rather than stopping the probe stream. */
    internal sealed class NpV2DatRecorder : IDisposable
    {
        private sealed class PendingBlock
        {
            internal readonly byte[] Bytes = new byte[NpV2UsbDevice.BlockBytes];
            internal NpV2WireVerifier Verifier;
            private readonly BlockingCollection<PendingBlock> free;
            private int users;
            internal PendingBlock(BlockingCollection<PendingBlock> free) { this.free = free; }
            internal void Prepare(NpV2WireVerifier verifier, int count) { Verifier = verifier; users = count; }
            internal void Release() { if (Interlocked.Decrement(ref users) == 0) free.Add(this); }
        }
        private readonly string folder;
        private readonly Action<int, short[]> preview;
        private readonly Action<int, short[]> correctSamples;
        private readonly BlockingCollection<PendingBlock> free;
        private readonly BlockingCollection<PendingBlock>[] queued = new BlockingCollection<PendingBlock>[4];
        private readonly FileStream rawFile;
        private readonly NpV2BatchedWriter[] apFiles = new NpV2BatchedWriter[4];
        private readonly NpV2LiveDatWriter merged;
        private readonly long[] apRows = new long[4];
        private readonly Thread[] workers = new Thread[4];
        private Exception rawFailure, decodeFailure;
        private Exception previewFailure;
        private long rawDroppedBlocks, decodedDroppedLaneBlocks;
        private int decodeDisabled;
        private bool completed, disposed, formatOpened;
        private uint liveProbeMask, sampleHz;
        internal readonly int InputCapacityBlocks;
        internal int PeakPendingBlocks { get; private set; }

        internal NpV2DatRecorder(string folder, Action<int, short[]> preview)
            : this(folder, preview, NpV2BufferPolicy.ForHost()) { }

        internal NpV2DatRecorder(string folder, Action<int, short[]> preview, NpV2BufferPolicy buffers)
            : this(folder, preview, buffers, null) { }

        internal NpV2DatRecorder(string folder, Action<int, short[]> preview, NpV2BufferPolicy buffers, Action<int, short[]> correctSamples)
            : this(folder,preview,buffers,correctSamples,false) { }

        internal NpV2DatRecorder(string folder, Action<int, short[]> preview, NpV2BufferPolicy buffers, Action<int, short[]> correctSamples, bool mergeProbes)
            : this(folder,preview,buffers,correctSamples,mergeProbes,true) { }

        internal NpV2DatRecorder(string folder, Action<int, short[]> preview, NpV2BufferPolicy buffers, Action<int, short[]> correctSamples, bool mergeProbes, bool saveRaw)
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentNullException("folder");
            this.folder = folder; this.preview = preview;
            this.correctSamples = correctSamples;
            InputCapacityBlocks = buffers.SharedBlocks;
            free = new BlockingCollection<PendingBlock>(InputCapacityBlocks);
            for (int i = 0; i < InputCapacityBlocks; i++) free.Add(new PendingBlock(free));
            if(mergeProbes)merged=new NpV2LiveDatWriter(folder,buffers);
            else for (int p = 1; p <= 3; p++) apFiles[p] = new NpV2BatchedWriter(buffers.WriteBuffersPerProbe);
            if(saveRaw) {
            rawFile = new FileStream(Path.Combine(folder, "neuropixels_np2usb.raw"), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 4 * 1024 * 1024, FileOptions.SequentialScan);
            queued[0] = new BlockingCollection<PendingBlock>(InputCapacityBlocks);
            workers[0] = new Thread(new ThreadStart(RawLoop))
                { IsBackground = true, Priority = ThreadPriority.Normal, Name = "NP2 raw writer" };
            workers[0].Start();
            }
            for (int p = 1; p <= 3; p++)
            {
                int probe = p;
                queued[probe] = new BlockingCollection<PendingBlock>(InputCapacityBlocks);
                workers[probe] = new Thread(new ThreadStart(delegate { DecodeLoop(probe); }))
                    { IsBackground = true, Priority = ThreadPriority.Normal, Name = "NP2 probe " + probe + " DAT writer" };
                workers[probe].Start();
            }
        }

        internal uint LiveProbeMask { get { return liveProbeMask; } }
        internal uint SampleHz { get { return sampleHz; } }
        internal bool MergedDat {get{return merged!=null && ProbeCount(liveProbeMask)>1;}}
        internal long TotalRows { get { if(merged!=null)return merged.WrittenRows;long rows = 0; for (int p = 1; p <= 3; p++) rows += apRows[p]; return rows; } }
        internal long RowsForProbe(int probe) { return probe >= 1 && probe <= 3 ? merged!=null && (liveProbeMask&(1U<<(probe-1)))!=0?merged.WrittenRows:apRows[probe] : 0; }
        internal void WriteMergeReport(bool recordingComplete){if(merged!=null)merged.WriteReport(recordingComplete,rawFile!=null);}
        internal long RawDroppedBlocks { get { return Interlocked.Read(ref rawDroppedBlocks); } }
        internal long DecodedDroppedLaneBlocks { get { return Interlocked.Read(ref decodedDroppedLaneBlocks); } }
        internal string RawError { get { return DescribeWorkerError(Volatile.Read(ref rawFailure)); } }
        internal string DecodeError { get { return DescribeWorkerError(Volatile.Read(ref decodeFailure)); } }
        internal string PreviewError
        {
            get
            {
                Exception error = Volatile.Read(ref previewFailure);
                return error == null ? null : error.GetType().Name + ": " + error.Message;
            }
        }

        internal void Enqueue(byte[] source, int offset, NpV2WireVerifier verifier)
        {
            EnqueueCore(source, offset, verifier, true);
            ThrowIfDatOnlyFailed();
        }

        internal void ThrowIfDatOnlyFailed()
        {
            // DAT-only capture has no fallback file: surface an asynchronous
            // decode/write failure to the USB owner's existing stop/drain path.
            if(rawFile==null) {
                Exception error=Volatile.Read(ref decodeFailure);
                if(error!=null)throw new IOException("NP2 DAT recording failed.",error);
            }
        }

        internal void EnqueueRaw(byte[] source, int offset)
        {
            if(rawFile==null)throw new IOException("USB block verification failed; DAT-only recording cannot continue.");
            EnqueueCore(source, offset, null, false);
        }

        private void EnqueueCore(byte[] source, int offset, NpV2WireVerifier verifier, bool includeDecoded)
        {
            if (completed) throw new InvalidOperationException("NP2 recorder has already completed.");
            bool decode = includeDecoded && Volatile.Read(ref decodeDisabled) == 0;
            if (decode)
            {
                try { EnsureFormat(verifier); }
                catch (Exception ex) { DisableDecode(ex); decode = false; }
            }

            PendingBlock block;
            if (!free.TryTake(out block))
            {
                /* Release CPU and retained copies from all DAT workers first. A
                 * one-millisecond grace period normally recovers a buffer while
                 * remaining far below the firmware/USB headroom. */
                if (decode)
                {
                    DisableDecode(new IOException(rawFile==null?"NP2 DAT decoder queue reached capacity; recording cannot continue.":"NP2 DAT decoder queue reached capacity; decoded output was disabled so raw capture could continue."));
                    decode = false;
                }
                if (!free.TryTake(out block, 1))
                {
                    if(rawFile!=null)Interlocked.Increment(ref rawDroppedBlocks);
                    if (includeDecoded) Interlocked.Add(ref decodedDroppedLaneBlocks, Math.Max(1, ProbeCount(verifier == null ? 0U : verifier.LiveProbeMask)));
                    return;
                }
            }
            int users = 0, published = 0;
            try
            {
                Buffer.BlockCopy(source, offset, block.Bytes, 0, block.Bytes.Length);
                bool raw = rawFile!=null && Volatile.Read(ref rawFailure) == null;
                uint decodeMask = decode ? liveProbeMask : 0U;
                users = (raw ? 1 : 0) + ProbeCount(decodeMask);
                if (rawFile!=null && !raw) Interlocked.Increment(ref rawDroppedBlocks);
                if (includeDecoded && decodeMask == 0U)
                    Interlocked.Add(ref decodedDroppedLaneBlocks, Math.Max(1, ProbeCount(verifier == null ? 0U : verifier.LiveProbeMask)));
                if (users == 0) { free.Add(block); return; }
                block.Prepare(verifier, users);
                PeakPendingBlocks = Math.Max(PeakPendingBlocks, InputCapacityBlocks - free.Count);
                if (raw) { queued[0].Add(block); published++; }
                for (int probe = 1; probe <= 3; probe++) if ((decodeMask & (1U << (probe - 1))) != 0U)
                { queued[probe].Add(block); published++; }
            }
            catch
            {
                // Consumers already published still own their references.
                // Release only destinations that never received this block.
                if (users == 0) free.Add(block);
                else for (int i = published; i < users; i++) block.Release();
                throw;
            }
        }

        internal void Complete()
        {
            if (completed) return;
            completed = true;
            for (int worker = 0; worker <= 3; worker++) if(queued[worker]!=null)queued[worker].CompleteAdding();
            for (int worker = 0; worker <= 3; worker++) if (workers[worker]!=null && workers[worker] != Thread.CurrentThread) workers[worker].Join();
            if(merged!=null)try{merged.Complete();}catch(Exception ex){DisableDecode(ex);}
        }

        private void RawLoop()
        {
            try
            {
                foreach (PendingBlock block in queued[0].GetConsumingEnumerable())
                {
                    try
                    {
                        if (Volatile.Read(ref rawFailure) == null) rawFile.Write(block.Bytes, 0, block.Bytes.Length);
                        else Interlocked.Increment(ref rawDroppedBlocks);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref rawFailure, ex, null);
                        Interlocked.Increment(ref rawDroppedBlocks);
                    }
                    finally { block.Release(); }
                }
            }
            catch (Exception ex) { Interlocked.CompareExchange(ref rawFailure, ex, null); }
            finally
            {
                try { rawFile.Flush(true); }
                catch (Exception ex) { Interlocked.CompareExchange(ref rawFailure, ex, null); }
                try { rawFile.Dispose(); }
                catch (Exception ex) { Interlocked.CompareExchange(ref rawFailure, ex, null); }
            }
        }

        private void DecodeLoop(int probe)
        {
            try
            {
                var decoder = new NpV2SampleDecoder(ValidateFormat, WriteRow, probe);
                foreach (PendingBlock block in queued[probe].GetConsumingEnumerable())
                {
                    try
                    {
                        if (Volatile.Read(ref decodeDisabled) == 0) decoder.Feed(block.Bytes, 0, block.Verifier);
                        else Interlocked.Increment(ref decodedDroppedLaneBlocks);
                    }
                    catch (Exception ex)
                    {
                        DisableDecode(ex);
                        Interlocked.Increment(ref decodedDroppedLaneBlocks);
                    }
                    finally { block.Release(); }
                }
                if (Volatile.Read(ref decodeDisabled) == 0) decoder.Complete();
            }
            catch (Exception ex) { DisableDecode(ex); }
            finally
            {
                try { if(apFiles[probe]!=null)apFiles[probe].Complete(); }
                catch (Exception ex) { DisableDecode(ex); }
                // Count rows actually written; a disk/flush error is preserved
                // in the receipt even when USB was able to continue draining.
                if(apFiles[probe]!=null)Interlocked.Exchange(ref apRows[probe], apFiles[probe].WrittenBytes / 768);
            }
        }

        private void EnsureFormat(NpV2WireVerifier verifier)
        {
            uint mask = verifier.LiveProbeMask, rate = verifier.SampleHz;
            if (!formatOpened) OpenFiles(mask, rate, verifier);
            else if (mask != liveProbeMask || rate != sampleHz)
                throw new InvalidDataException("NP2 DAT output contract changed mid-recording.");
        }

        private void ValidateFormat(uint liveMask, uint rate)
        {
            if (!formatOpened || liveMask != liveProbeMask || rate != sampleHz)
                throw new InvalidDataException("NP2 parallel DAT worker received a mismatched stream contract.");
        }

        private void OpenFiles(uint liveMask, uint rate, NpV2WireVerifier verifier)
        {
            if (liveMask == 0U || liveMask > 7U) throw new InvalidDataException("NP2 DAT output has no live probe lane.");
            liveProbeMask = liveMask; sampleHz = rate;
            int count = ProbeCount(liveMask);
            if(merged!=null)merged.Open(liveMask,rate,verifier);
            else for (int probe = 1; probe <= 3; probe++) if ((liveMask & (1U << (probe - 1))) != 0U)
            {
                string directory = count == 1 ? folder : Path.Combine(folder, "probe" + probe.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                apFiles[probe].Open(new FileStream(Path.Combine(directory, "neuropixels_ap.dat"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read, 1, FileOptions.SequentialScan), "NP2 probe " + probe + " disk writer");
            }
            formatOpened = true;
        }

        private static int ProbeCount(uint mask)
        { return ((int)mask & 1) + (((int)mask >> 1) & 1) + (((int)mask >> 2) & 1); }

        private void WriteRow(int probe, short[] row)
        {
            NpV2BatchedWriter file = probe >= 1 && probe <= 3 ? apFiles[probe] : null;
            if ((file == null && merged==null) || row == null || row.Length != NpV2SampleDecoder.Channels)
                throw new InvalidDataException("NP2 DAT row has no active output file.");
            if(correctSamples!=null)correctSamples(probe,row);
            if(merged!=null)merged.Add(probe,row);else file.WriteRow(row);
            apRows[probe]++;
            /* Preview is deliberately outside the recording-integrity contract.
             * A disposed form or renderer bug may disable display, but must not
             * turn already valid DAT/raw data into an incomplete recording. */
            if (preview != null && Volatile.Read(ref previewFailure) == null)
            {
                try { preview(probe, row); }
                catch (Exception ex) { Interlocked.CompareExchange(ref previewFailure, ex, null); }
            }
        }

        private void DisableDecode(Exception error)
        {
            Interlocked.CompareExchange(ref decodeFailure, error, null);
            Interlocked.Exchange(ref decodeDisabled, 1);
            if(merged!=null)merged.Abort(error);
        }

        private static string DescribeWorkerError(Exception error)
        {
            return error == null ? null : error.GetType().Name + ": " + error.Message;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { Complete(); }
            finally
            {
                free.Dispose();
                for (int worker = 0; worker <= 3; worker++) if(queued[worker]!=null)queued[worker].Dispose();
                for (int probe = 1; probe <= 3; probe++)
                    try { if(apFiles[probe]!=null)apFiles[probe].Dispose(); } catch (Exception ex) { DisableDecode(ex); }
                if(merged!=null)try{merged.Dispose();}catch(Exception ex){DisableDecode(ex);}
            }
        }
    }

    // Only poll EP0 after bulk progress has stopped. Keep normal acquisition
    // bulk-only, but never wait forever for URBs a faulted producer cannot fill.
    internal sealed class NpV2ReadWatchdog
    {
        private double lastProgress, stopAt = -1, nextStatus;
        internal NpV2ReadWatchdog(double now) { lastProgress = now; }
        internal void Progress(double now) { lastProgress = now; }
        internal void RequestStop(double now) { if (stopAt < 0) stopAt = now; }
        internal bool ShouldReadStatus(double now)
        {
            double idleLimit = stopAt < 0 ? 8.0 : 1.0;
            if (now - lastProgress < idleLimit || now < nextStatus) return false;
            nextStatus = now + 1.0;
            return true;
        }
        internal bool StopExpired(double now)
        {
            return stopAt >= 0 && (now - stopAt >= 15.0 ||
                now - Math.Max(stopAt, lastProgress) >= 5.0);
        }
    }

    internal static class NpV2Recording
    {
        internal static bool ConfigurationReadyToArm(uint sourceState, Func<bool> cancelled)
        {
            if (cancelled()) throw new OperationCanceledException("Recording stopped during NP2013 configuration before data began.");
            return sourceState == 10U;
        }
        internal static bool IsCancelledBeforeCapture(Exception error, uint received, bool armRequested, NpV2CaptureStatus capture)
        {
            return error is OperationCanceledException && received == 0U && !armRequested &&
                capture != null && capture.Valid && capture.State != 255U && capture.Error == 0U &&
                capture.ProducerError == 0U && capture.SourceError == 0U && capture.CaptureStartedMs == 0U;
        }
        /* Keep the validated four-block RAW_IO transfer size. Read buffers are
         * copied to a preallocated RAM backlog and immediately rearmed; neither
         * verification, decoding nor disk latency holds a USB request slot. */
#if NP2_USB_HIL_ONE_BLOCK
        /* Diagnostic build only: isolate WinUSB request length from device-side
         * block production. The shipping console retains its four-block reads. */
        private const int QueueDepth = 32;
        private const int BlocksPerRead = 1;
#else
        // Maximum native wait set; four-block transfers retain Stop alignment.
        // Native slots are fixed overhead, separate from the host pool budget.
        private const int QueueDepth = 64;
        private const int BlocksPerRead = 4;
#endif
        private const uint WaitTimeout = 258U;
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds);

        private sealed class Done
        {
            internal readonly byte[] Buffer = new byte[BlocksPerRead * NpV2UsbDevice.BlockBytes];
            internal long Issue; internal int Bytes;
        }

        internal static NpV2RecordingResult Run(string devicePath, string outputRoot, uint targetBlocks, Func<bool> cancelled, Action<NpV2RecordingProgress> progress)
        {
            if (String.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentNullException("outputRoot");
            return RunCore(devicePath, Path.Combine(outputRoot, "NP2USB_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)), targetBlocks, cancelled, progress, null, null);
        }

        internal static NpV2RecordingResult RunInFolder(string devicePath, string folder, uint targetBlocks, Func<bool> cancelled, Action<NpV2RecordingProgress> progress)
        {
            return RunCore(devicePath, folder, targetBlocks, cancelled, progress, null, null);
        }

        internal static NpV2RecordingResult RunContinuousInFolder(string devicePath, string folder, Func<bool> cancelled, Action<NpV2RecordingProgress> progress)
        {
            return RunContinuousInFolder(devicePath, folder, cancelled, progress, null);
        }

        internal static NpV2RecordingResult RunContinuousInFolder(string devicePath, string folder, Func<bool> cancelled,
            Action<NpV2RecordingProgress> progress, Action<int, short[]> decodedApRow)
        {
            return RunCore(devicePath, folder, 0U, cancelled, progress, decodedApRow, null);
        }

        internal static NpV2RecordingResult RunContinuousInFolder(string devicePath, string folder, Func<bool> cancelled,
            Action<NpV2RecordingProgress> progress, Action<int, short[]> decodedApRow, Action<NpV2UsbDevice> confirmConfiguration
#if !NP2_CONSOLE_STANDALONE
            , Action<NpImuDisplay> sensorDisplay=null, bool saveSensors=false
#endif
            , bool mergeProbes=false, bool saveRaw=true
            )
        {
            return RunCore(devicePath, folder, 0U, cancelled, progress, decodedApRow, confirmConfiguration
#if !NP2_CONSOLE_STANDALONE
                ,sensorDisplay,saveSensors
#endif
                ,mergeProbes,saveRaw
                );
        }

        // Borrowed-device cleanup runs once for each successfully opened device,
        // even if setup fails before confirmConfiguration. Retire only owned AUX
        // reads here; do not dispose the shared device or cancel its EP81 pipe.
        internal static NpV2RecordingResult RunContinuousInFolder(string devicePath, string folder, Func<bool> cancelled,
            Action<NpV2RecordingProgress> progress, Action<int, short[]> decodedApRow, Action<NpV2UsbDevice> confirmConfiguration
#if !NP2_CONSOLE_STANDALONE
            , Action<NpImuDisplay> sensorDisplay, bool saveSensors
#endif
            , bool mergeProbes, bool saveRaw, Action<NpV2UsbDevice> beforeDeviceDispose)
        {
            return RunCore(devicePath, folder, 0U, cancelled, progress, decodedApRow, confirmConfiguration
#if !NP2_CONSOLE_STANDALONE
                ,sensorDisplay,saveSensors
#endif
                ,mergeProbes,saveRaw,beforeDeviceDispose);
        }

        private static NpV2RecordingResult RunCore(string devicePath, string folder, uint targetBlocks, Func<bool> cancelled,
            Action<NpV2RecordingProgress> progress, Action<int, short[]> decodedApRow, Action<NpV2UsbDevice> confirmConfiguration
#if !NP2_CONSOLE_STANDALONE
            , Action<NpImuDisplay> sensorDisplay=null, bool saveSensors=false
#endif
            , bool mergeProbes=false, bool saveRaw=true, Action<NpV2UsbDevice> beforeDeviceDispose=null
            )
        {
            if (String.IsNullOrWhiteSpace(devicePath)) throw new ArgumentNullException("devicePath");
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentNullException("folder");
            if (cancelled == null) throw new ArgumentNullException("cancelled");
            if (progress == null) throw new ArgumentNullException("progress");
            if (targetBlocks > 1000000U) throw new ArgumentOutOfRangeException("targetBlocks");
            bool continuous = targetBlocks == 0U;
            Directory.CreateDirectory(folder);
            long needed = continuous ? 256L * 1024L * 1024L :
                checked((long)targetBlocks * NpV2UsbDevice.BlockBytes + 64L * 1024L * 1024L);
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder)));
            if (drive.AvailableFreeSpace < needed) throw new IOException("Insufficient free space for the complete NP2USB recording plus safety margin.");

            var result = new NpV2RecordingResult { Folder = folder, TargetBlocks = targetBlocks, Verifier = new NpV2WireVerifier() };
            NpV2BufferPolicy buffers = NpV2BufferPolicy.ForHost();
            result.HostBufferBudgetMb = buffers.BudgetMb; result.ReadBufferCount = buffers.ReadBuffers;
            var slots = new NpV2ReadSlot[QueueDepth];
            var handles = new IntPtr[QueueDepth];
            var requested = new int[QueueDepth];
            var free = new BlockingCollection<int>(QueueDepth);
            var backlogFree = new BlockingCollection<Done>(buffers.ReadBuffers);
            var work = new BlockingCollection<Done>(buffers.ReadBuffers);
            var warnings = new ConcurrentQueue<string>();
            Exception workerError = null, verificationWarning = null, error = null, beforeDisposeError = null;
            bool started = false, protocolComplete = false, stopRequested = false, cancelSent = false, armRequested = false;
            NpV2CaptureStatus finalCapture = null;
            NpV2SourceStatus finalSource = null;
            string recordingStage="USB device admission";
            NpV2DatRecorder dat = null;
#if !NP2_CONSOLE_STANDALONE
            NpUsbSensorMonitor sensorMonitor=null;
#endif
            Thread worker = null;
            Stopwatch clock = Stopwatch.StartNew();
            long issue = 0;
            long lastCompletionTicks = 0;
            uint queued = 0, received = 0;
            double firstData = -1, lastData = -1, nextProgress = 0;
            ThreadPriority previousPriority = Thread.CurrentThread.Priority;
            var previousGcLatency = System.Runtime.GCSettings.LatencyMode;
            bool gcLatencyChanged = false;
            try
            {
                for (int i = 0; i < buffers.ReadBuffers; i++) backlogFree.Add(new Done());
                for (int i = 0; i < QueueDepth; i++)
                {
                    slots[i] = new NpV2ReadSlot(i, BlocksPerRead * NpV2UsbDevice.BlockBytes);
                    handles[i] = slots[i].Event; free.Add(i);
                }
#if !NP2_CONSOLE_STANDALONE
                var calibration = NpCalibrationSet.Read(folder);
                if(calibration!=null)calibration.RequireModel(true,7);
                dat = new NpV2DatRecorder(folder, decodedApRow, buffers, calibration == null ? null : (Action<int, short[]>)((probe, row) => calibration.Correct(probe, row, 1, null, 0)),mergeProbes,saveRaw);
#else
                dat = new NpV2DatRecorder(folder, decodedApRow, buffers,null,mergeProbes,saveRaw);
#endif
                worker = new Thread(delegate()
                {
                    try
                    {
                        long next = 0;
                        var reorder = new SortedDictionary<long, Done>();
                        foreach (Done done in work.GetConsumingEnumerable())
                        {
                            if (reorder.ContainsKey(done.Issue)) throw new IOException("Duplicate NP2 USB completion.");
                            reorder.Add(done.Issue, done);
                            Done current;
                            while (reorder.TryGetValue(next, out current))
                            {
                                reorder.Remove(next);
                                for (int at = 0; at < current.Bytes; at += NpV2UsbDevice.BlockBytes)
                                {
                                    bool verified = false;
                                    if (verificationWarning == null)
                                    {
                                        try { result.Verifier.Feed(current.Buffer, at); verified = true; }
                                        catch (Exception ex)
                                        {
                                            verificationWarning = ex;
                                            warnings.Enqueue("Stream verification failed: " + DescribeError(ex) + (saveRaw?"; raw capture continued.":"; DAT recording stopped."));
                                        }
                                    }
                                    if (verified) {
#if !NP2_CONSOLE_STANDALONE
                                        if(sensorMonitor!=null)sensorMonitor.FeedBlock(current.Buffer,at);
#endif
                                        dat.Enqueue(current.Buffer, at, result.Verifier);
                                    }
                                    else dat.EnqueueRaw(current.Buffer, at);
                                }
                                backlogFree.Add(current);
                                next++;
                            }
                        }
                        if (reorder.Count != 0) throw new IOException("NP2 USB completion order has a gap.");
                    }
                    catch (Exception ex) { Interlocked.CompareExchange(ref workerError, ex, null); }
                });
                worker.IsBackground = true; worker.Name = "NP2 V2 continuity verifier"; worker.Priority = ThreadPriority.AboveNormal; worker.Start();

                using (var device = NpV2UsbDevice.Open(devicePath))
                {
                    try
                    {
                        NpV2ServiceStatus service = device.GetServiceStatus();
                        if (service.Mode != 2U || service.Busy != 0U) throw new IOException("V2 must be idle USB-stream mode before recording.");
                        NpV2CaptureStatus advertised = device.GetCaptureStatus();
                        finalCapture = advertised;
                        if (!advertised.CurrentProfileSupported)
                            throw new IOException("The V2 firmware reports an unsupported NP2 USB sampling profile (format=" + advertised.Format +
                                ", sample=" + advertised.SampleHz + " Hz, PSSI=" + advertised.PssiHz + " Hz, mask=0x" + advertised.ProbeMask.ToString("X1") + ").");
                        if (advertised.Format == 3U) NpV2ProfileCapabilities.RequireUsbBandwidth(advertised.ProbeMask, advertised.SampleHz);
#if !NP2_CONSOLE_STANDALONE
                        if(advertised.Format==3U)try {
                            sensorMonitor=new NpUsbSensorMonitor(device,folder,NpV2FirmwareForm.Uid(devicePath),saveSensors,sensorDisplay);
                            device.EnableSensorHeaders(true);
                        }catch(Exception ex){result.OptionalImuError=ex.Message;}
#endif
                        Action queueFree = delegate()
                        {
                            if (stopRequested) return;
                            while (continuous || queued < targetBlocks)
                            {
                                int index;
                                if (!free.TryTake(out index)) return;
                                uint count = continuous ? (uint)BlocksPerRead : Math.Min((uint)BlocksPerRead, targetBlocks - queued);
                                int bytes = checked((int)count * NpV2UsbDevice.BlockBytes);
                                NpV2ReadSlot slot = slots[index];
                                long completedAt = Interlocked.Exchange(ref slot.CompletedAtTicks, 0L);
                                if (completedAt != 0L)
                                    result.MaxSlotRecycleMs = Math.Max(result.MaxSlotRecycleMs,
                                        (Stopwatch.GetTimestamp() - completedAt) * 1000.0 / Stopwatch.Frequency);
                                slot.Issue = issue++;
                                requested[index] = bytes; device.QueueRead(slot, bytes); queued += count;
                            }
                        };
                        Action queueStopReads = delegate()
                        {
                            while (targetBlocks == 0U || queued < targetBlocks)
                            {
                                int index;
                                if (!free.TryTake(out index)) return;
                                NpV2ReadSlot slot = slots[index];
                                long completedAt = Interlocked.Exchange(ref slot.CompletedAtTicks, 0L);
                                if (completedAt != 0L)
                                    result.MaxSlotRecycleMs = Math.Max(result.MaxSlotRecycleMs,
                                        (Stopwatch.GetTimestamp() - completedAt) * 1000.0 / Stopwatch.Frequency);
                                slot.Issue = issue++;
                                requested[index] = NpV2UsbDevice.BlockBytes;
                                device.QueueRead(slot, NpV2UsbDevice.BlockBytes); queued++;
                            }
                        };
                        progress(new NpV2RecordingProgress { Text = "Starting " + (continuous ? "continuous " : "") + advertised.SampleHz.ToString("N0", CultureInfo.InvariantCulture) + " sample/s NP2013 configuration (I2C can take about 70 seconds)…", TargetBlocks = targetBlocks });
                        if (cancelled()) throw new OperationCanceledException("Recording stopped before NP2 configuration began.");
#if !NP2_CONSOLE_STANDALONE
                        if(sensorMonitor!=null)sensorMonitor.ObserveIdleClock(device);
#endif
                        recordingStage="Probe initialization/configuration";
                        device.Start(targetBlocks); started = true;
                        /* Configuration can take about 125 seconds with the
                         * internal I2C pull-ups. Keep the bulk queue separate
                         * from that control-only phase, then prime all reads as
                         * soon as the source reaches its read-arm window. */
                        double nextSetupProgress = 0.0;
                        while (true)
                        {
                            finalCapture = device.GetCaptureStatus();
                            if (finalCapture.State == 255U || finalCapture.Error != 0U || finalCapture.ProducerError != 0U || finalCapture.SourceError != 0U)
                                throw new IOException("V2 firmware failed during setup: capture=" + finalCapture.Error + ", producer=" + finalCapture.ProducerError + ", source=" + finalCapture.SourceError + ".");
                            /* SETTLING=8 and RELEASE=9 are observable but the
                             * firmware deliberately rejects host-arm until the
                             * capture port is bound in READY=10. */
                            if (ConfigurationReadyToArm(finalCapture.SourceState, cancelled)) break;
                            double setupElapsed = clock.Elapsed.TotalSeconds;
                            if (setupElapsed > 200.0) throw new TimeoutException("NP2 source configuration did not reach the USB read-arm window within 200 seconds.");
                            if (setupElapsed >= nextSetupProgress)
                            {
                                progress(new NpV2RecordingProgress { Text = "Configuring " + NpV2RecordingDiagnostics.ProbeList(finalCapture.ProbeMask) + "; " + NpV2RecordingDiagnostics.SourceStateName(finalCapture.SourceState) + " (USB reads not armed yet)…", TargetBlocks = targetBlocks });
                                nextSetupProgress = setupElapsed + 5.0;
                            }
                            Thread.Sleep(100);
                        }
                        if (confirmConfiguration != null) confirmConfiguration(device);
                        /* Firmware holds capture in CONFIGURING after the source
                         * becomes ready. Prepare the idle endpoint, release its
                         * one-second read-arm grace interval, then immediately
                         * post the complete queue. EP0 cannot be used after these
                         * RAW_IO requests are pending on this controller pair. */
#if !NP2_CONSOLE_STANDALONE
                        if(sensorMonitor!=null)sensorMonitor.ObserveIdleClock(device);
#endif
                        recordingStage="Host read-arm / USB queue preparation";
                        device.PrepareCapturePipe(BlocksPerRead * NpV2UsbDevice.BlockBytes);
                        GC.Collect(2, GCCollectionMode.Forced, true);
                        GC.WaitForPendingFinalizers();
                        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                        gcLatencyChanged = true;
                        ConfigurationReadyToArm(finalCapture.SourceState, cancelled);
                        armRequested = true; // An attempted host-arm is not a proven pre-capture cancellation.
                        device.ArmCapture();
                        queueFree();
                        double readArmStarted = clock.Elapsed.TotalSeconds;
                        recordingStage="Bulk capture / USB receive";
                        var readWatchdog = new NpV2ReadWatchdog(readArmStarted);
                        bool initialDataWarning = false, dataStallWarning = false, shortReadWarning = false;
                        Thread.CurrentThread.Priority = ThreadPriority.Highest;
                        while (continuous || received < targetBlocks)
                        {
                            if (Volatile.Read(ref workerError) != null) throw new IOException("NP2 verifier/logger failed; recording marked incomplete.", workerError);
                            dat.ThrowIfDatOnlyFailed();
                            if (!stopRequested && cancelled())
                            {
                                if (!continuous) throw new OperationCanceledException("Recording stopped by user.");
                                stopRequested = true;
                                readWatchdog.RequestStop(clock.Elapsed.TotalSeconds);
                                if (firstData < 0)
                                {
                                    /* There is no complete recording to preserve yet. Do not make Stop wait
                                     * through the long NP2013 configuration just to drain empty bulk reads. */
                                    device.Cancel();
                                    throw new OperationCanceledException("Recording stopped during NP2013 configuration before data began.");
                                }
                                progress(new NpV2RecordingProgress { Text = "Draining already-queued USB reads before the final NP2 boundary…", ReceivedBlocks = received, VerifiedBlocks = result.Verifier.Blocks });
                            }
                            queueFree();
                            if (stopRequested && !cancelSent && received == queued)
                            {
                                recordingStage="Stop boundary / final USB drain";
                                /* Before asking firmware to stop, drain the outstanding reads. Do not wait for the verifier
                                 * worker to return every buffer: that gap is long enough to exhaust the small
                                 * lossless PSSI retention ring. The final drain reuses buffers as they return. */
                                /* Issue stop only after the current read set has
                                 * completed, learn the firmware's exact final
                                 * boundary, then post the finite tail drain. */
                                device.Cancel(); cancelSent = true;
                                double stopDeadline = clock.Elapsed.TotalSeconds + 5.0;
                                do
                                {
                                    finalCapture = device.GetCaptureStatus();
                                    if (finalCapture.State == 255U || finalCapture.Error != 0U || finalCapture.ProducerError != 0U || finalCapture.SourceError != 0U)
                                        throw new IOException("V2 firmware failed while selecting the stop boundary: capture=" + finalCapture.Error + ", producer=" + finalCapture.ProducerError + ", source=" + finalCapture.SourceError + ".");
                                    if (finalCapture.Target != 0U) break;
                                    if (clock.Elapsed.TotalSeconds >= stopDeadline) throw new TimeoutException("V2 firmware did not publish a complete stop boundary.");
                                    Thread.Sleep(10);
                                } while (true);
                                targetBlocks = finalCapture.Target; result.TargetBlocks = targetBlocks;
                                if (received > targetBlocks) throw new IOException("The host received data beyond the firmware-certified continuous stop boundary.");
                                /* Preserve the endpoint data toggle and any bytes
                                 * already accepted by WinUSB while draining the
                                 * firmware-certified final boundary. */
                                queueStopReads();
                                progress(new NpV2RecordingProgress { Text = "Stopping at a complete NP2 block and draining final USB data…", ReceivedBlocks = received, VerifiedBlocks = result.Verifier.Blocks });
                            }
                            if (cancelSent) queueStopReads();
                            double elapsed = clock.Elapsed.TotalSeconds;
                            if (readWatchdog.ShouldReadStatus(elapsed))
                            {
                                finalCapture = device.GetCaptureStatus();
                                if (finalCapture.State == 255U || finalCapture.Error != 0U ||
                                    finalCapture.ProducerError != 0U || finalCapture.SourceError != 0U)
                                {
                                    finalSource = device.GetSourceStatus();
                                    throw new IOException("NP2 USB stalled after " + received +
                                        " blocks because firmware reported capture=" + finalCapture.Error +
                                        ", producer=" + finalCapture.ProducerError + ", source=" + finalCapture.SourceError +
                                        ". Accepted data was retained; recording is incomplete.");
                                }
                            }
                            if (readWatchdog.StopExpired(elapsed))
                                throw new TimeoutException("NP2 Stop could not drain pending USB reads within its bounded deadline; accepted data was retained and recording is incomplete.");
                            /* Keep the active recording loop bulk-only; control
                             * status is sampled at setup, stop, or failure. */
                            if (elapsed >= nextProgress)
                            {
                                progress(new NpV2RecordingProgress {
                                    Text = "V2 bulk recording: " + received.ToString("N0") +
                                        (continuous && targetBlocks == 0U ? (cancelSent ? " blocks (finding final boundary…)" : " blocks (continuous; press Stop to finish)") :
                                         "/" + targetBlocks.ToString("N0") + " blocks"),
                                    ReceivedBlocks = received, TargetBlocks = targetBlocks, VerifiedBlocks = result.Verifier.Blocks,
                                    PayloadMbPerSecond = firstData < 0 || elapsed <= firstData ? 0 : result.Verifier.PayloadBytesSeen / (elapsed - firstData) / 1000000.0,
                                    FirstReceiveUtc = result.FirstReceiveUtc, LastReceiveUtc = result.LastReceiveUtc
                                });
                                nextProgress = elapsed + 1.0;
                            }
                            if (firstData < 0 && elapsed - readArmStarted > 15.0 && !initialDataWarning)
                            {
                                warnings.Enqueue("NP2 produced no USB data for 15 seconds after read-arm; waiting in best-effort mode.");
                                progress(new NpV2RecordingProgress { Text = "Waiting for NP2 USB data (best effort; press Stop to finish)…", ReceivedBlocks = received, VerifiedBlocks = result.Verifier.Blocks });
                                initialDataWarning = true;
                            }
                            if (firstData >= 0 && elapsed - lastData > 8.0 && !dataStallWarning)
                            {
                                warnings.Enqueue("NP2 USB data paused for more than 8 seconds; the host kept waiting.");
                                progress(new NpV2RecordingProgress { Text = "NP2 USB data paused; still waiting in best-effort mode…", ReceivedBlocks = received, VerifiedBlocks = result.Verifier.Blocks });
                                dataStallWarning = true;
                            }
                            if (cancelSent && targetBlocks != 0U && received == targetBlocks) break;
                            uint wait = WaitForMultipleObjects((uint)handles.Length, handles, false, 50U);
                            if (wait == WaitTimeout) continue;
                            if (wait >= (uint)handles.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "V2 wait for bulk read failed");
                            NpV2ReadSlot done = slots[(int)wait];
                            int bytesDone;
                            try { bytesDone = device.FinishRead(done); }
                            catch (Win32Exception ex)
                            {
                                NpV2CaptureStatus atFailure = null;
                                try { atFailure = device.GetCaptureStatus(); } catch { }
                                if (atFailure != null) finalCapture = atFailure;
                                throw new IOException("V2 bulk completion failed (Win32 " + ex.NativeErrorCode.ToString(CultureInfo.InvariantCulture) +
                                    "; received=" + received.ToString(CultureInfo.InvariantCulture) +
                                    (atFailure == null ? ")" : "; firmware=" + atFailure.StateName + " " + atFailure.Completed + "/" + atFailure.Target + ")"), ex);
                            }
                            if (bytesDone != requested[done.Index])
                            {
                                int missing = requested[done.Index] - bytesDone;
                                if (missing < 0 || missing % NpV2UsbDevice.BlockBytes != 0)
                                    throw new IOException("NP2 completion exceeded or split its requested block extent.");
                                queued -= checked((uint)(missing / NpV2UsbDevice.BlockBytes));
                                if (!shortReadWarning)
                                {
                                    warnings.Enqueue("WinUSB returned a short transfer; all complete blocks were retained and the missing request extent was replenished.");
                                    shortReadWarning = true;
                                }
                            }
                            long completionTicks = Stopwatch.GetTimestamp();
                            if (lastCompletionTicks != 0L)
                                result.MaxCompletionGapMs = Math.Max(result.MaxCompletionGapMs,
                                    (completionTicks - lastCompletionTicks) * 1000.0 / Stopwatch.Frequency);
                            lastCompletionTicks = completionTicks;
                            Interlocked.Exchange(ref done.CompletedAtTicks, completionTicks);
                            if (bytesDone > 0)
                            {
                                firstData = firstData < 0 ? elapsed : firstData; lastData = elapsed;
                                result.LastReceiveUtc = DateTime.UtcNow;
                                if (result.FirstReceiveUtc == DateTime.MinValue) result.FirstReceiveUtc = result.LastReceiveUtc;
                                readWatchdog.Progress(elapsed);
                            }
                            received += checked((uint)(bytesDone / NpV2UsbDevice.BlockBytes)); result.WireBytes += bytesDone;
                            Done completed;
                            if (!backlogFree.TryTake(out completed))
                                throw new IOException("NP2 host RAM read backlog is full; recording is incomplete. Check sustained disk/CPU throughput or increase NP2_HOST_BUFFER_MB.");
                            Buffer.BlockCopy(done.Buffer, 0, completed.Buffer, 0, bytesDone);
                            completed.Issue = done.Issue; completed.Bytes = bytesDone;
                            // Publish the owned copy even if the next native
                            // rearm fails, then immediately repost the USB slot.
                            result.PeakReadBacklog = Math.Max(result.PeakReadBacklog, buffers.ReadBuffers - backlogFree.Count);
                            work.Add(completed); // A pooled buffer guarantees a queue slot.
                            free.Add(done.Index);
                            queueFree();
                            if (cancelSent) queueStopReads();
                        }
                        // Every target byte has been received. Retire even the
                        // surplus queued reads BEFORE returning to EP0 controls.
                        device.AbortAndRetire(slots);
                        for (int tries = 0; tries < 50; tries++)
                        {
                            recordingStage="Final device-count verification";
                            finalCapture = device.GetCaptureStatus();
                            if (finalCapture.State == 4U || finalCapture.State == 255U) break;
                            Thread.Sleep(10);
                        }
                        finalSource = device.GetSourceStatus();
                        if (finalCapture == null || !finalCapture.CompleteFor(targetBlocks))
                            throw new IOException("NP2 final capture/source counts did not certify a complete recording.");
                        if (finalSource.Error != 0U || finalSource.State != 11U)
                            throw new IOException("NP2 source did not stop cleanly after the requested recording.");
                        protocolComplete = true;
                    }
                    finally
                    {
                        try
                        {
                            if (started && !protocolComplete)
                            {
                                try { device.Cancel(); } catch { }
                                device.AbortAndRetire(slots);
                                // Failure-only query AFTER native ownership retired.
                                if(finalSource==null)try{finalSource=device.GetSourceStatus();}catch{}
                            }
#if !NP2_CONSOLE_STANDALONE
                            if(sensorMonitor!=null && !Array.Exists(slots,slot=>slot!=null && slot.Active))
                                sensorMonitor.ObserveIdleClock(device);
#endif
                            // Optional sidecar files outlive all queued verifier
                            // work; finalization belongs after the worker join.
                        }
                        finally
                        {
                            // Also run if EP81 retirement itself throws, so AUX
                            // can retire its own requests before WinUsbFree.
                            try { if(beforeDeviceDispose!=null)beforeDeviceDispose(device); }
                            catch(Exception ex) { beforeDisposeError=ex; }
                        }
                    }
                }
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                /* The source is one-shot. Close WinUSB before freeing pinned read
                 * buffers, after the bounded worker has consumed every completed
                 * buffer. A non-success result is always retained as incomplete. */
                work.CompleteAdding();
                if (worker != null) worker.Join();
#if !NP2_CONSOLE_STANDALONE
                if(sensorMonitor!=null) {
                    try{sensorMonitor.Dispose();}catch(Exception ex){result.OptionalImuError=ex.Message;}
                    result.OptionalImuRecords=sensorMonitor.Records;result.OptionalImuSamples=sensorMonitor.Samples;
                    result.OptionalImuError=result.OptionalImuError??sensorMonitor.Error;
                    result.OptionalImuDisplay=sensorMonitor.LastDisplay;
                }
#endif
                if (dat != null)
                {
                    try { dat.Complete(); }
                    catch (Exception ex) { if (error == null) error = ex; }
                    result.DecodedDatRows = dat.TotalRows;
                    result.DecodedDatProbeMask = dat.LiveProbeMask;
                    result.DecodedDatSampleHz = dat.SampleHz;
                    result.DecodedDatMerged = dat.MergedDat;
                    result.PreviewError = dat.PreviewError;
                    result.RawDroppedBlocks = dat.RawDroppedBlocks;
                    result.DecodedDroppedLaneBlocks = dat.DecodedDroppedLaneBlocks;
                    result.RawWriterError = dat.RawError;
                    result.DecodeError = dat.DecodeError;
                    result.SharedBufferCount = dat.InputCapacityBlocks;
                    result.PeakSharedBacklog = dat.PeakPendingBlocks;
                    if (result.RawWriterError != null) warnings.Enqueue("Raw writer degraded: " + result.RawWriterError);
                    if (result.DecodeError != null) warnings.Enqueue("Decoded DAT/preview path disabled: " + result.DecodeError);
                    if (result.RawDroppedBlocks != 0) warnings.Enqueue(result.RawDroppedBlocks.ToString(CultureInfo.InvariantCulture) + " raw block(s) could not be persisted.");
                    if (result.DecodedDroppedLaneBlocks != 0) warnings.Enqueue(result.DecodedDroppedLaneBlocks.ToString(CultureInfo.InvariantCulture) + " decoded lane-block(s) were skipped.");
                }
                foreach (NpV2ReadSlot slot in slots) if (slot != null) slot.Dispose();
                free.Dispose(); work.Dispose(); backlogFree.Dispose();
                Thread.CurrentThread.Priority = previousPriority;
                if (gcLatencyChanged) System.Runtime.GCSettings.LatencyMode = previousGcLatency;
            }
            clock.Stop();
            error = error ?? workerError;
            if(beforeDisposeError!=null)error=error==null?beforeDisposeError:new AggregateException("Recording and before-device-dispose cleanup failed.",error,beforeDisposeError);
            result.Error = DescribeError(error);
            result.Warning = JoinWarnings(warnings);
            result.CancelledBeforeCapture = IsCancelledBeforeCapture(error, received, armRequested, finalCapture) &&
                workerError == null && result.Warning == null;
            // Timing/whole-block short-read notices do not imply loss once all
            // counts, framing and completed file writers certify the final drain.
            result.Complete = error == null && verificationWarning == null &&
                result.RawWriterError == null && result.DecodeError == null &&
                result.RawDroppedBlocks == 0 && result.DecodedDroppedLaneBlocks == 0 &&
                received == targetBlocks && result.Verifier.Blocks == targetBlocks &&
                finalCapture != null && finalCapture.CompleteFor(targetBlocks) &&
                finalSource != null && finalSource.Error == 0U && finalSource.State == 11U;
            result.ReceivedBlocks = received; result.VerifiedBlocks = result.Verifier.Blocks;
            result.PayloadBytes = checked((long)received * (NpV2UsbDevice.BlockBytes - 512)); result.Seconds = clock.Elapsed.TotalSeconds;
            result.DataSeconds = finalCapture == null || finalCapture.CaptureStartedMs == 0U ? 0.0 :
                unchecked(finalCapture.DeviceNowMs - finalCapture.CaptureStartedMs) / 1000.0;
            result.FinalCapture = finalCapture; result.FinalSource = finalSource;
            if(dat!=null)try{dat.WriteMergeReport(result.Complete);}catch(Exception ex){result.Complete=false;result.DecodeError=result.DecodeError??ex.Message;}
            if(!result.Complete && !result.CancelledBeforeCapture){
                string stage=result.RawWriterError!=null?"Host RAW file write":result.DecodeError!=null?"Host AP DAT decode/write":verificationWarning!=null?"Stream verification":recordingStage;
                string detail=result.Error??result.RawWriterError??result.DecodeError??DescribeError(verificationWarning)??result.Warning??"Final counts did not certify complete recording.";
                result.Error=NpV2RecordingDiagnostics.Explain(stage,finalCapture,finalSource,verificationWarning??error,detail);
            }
            File.WriteAllLines(Path.Combine(folder, "neuropixels_np2usb_status.txt"), new[] {
                "complete=" + result.Complete,
                "cancelled_before_capture=" + result.CancelledBeforeCapture,
                "error=" + (result.Error ?? "none"),
                "warning=" + (result.Warning ?? "none"),
                "source_degraded_warning=" + (finalSource==null?"unavailable":finalSource.DegradedWarning??"none"),
                "format=" + result.Verifier.FormatName,
                "codec=" + result.Verifier.Codec,
                "application_crc=" + result.Verifier.ApplicationCrcPolicy,
                "raw_saved=" + (saveRaw && File.Exists(Path.Combine(folder,"neuropixels_np2usb.raw"))),
                "raw_file=" + (saveRaw?"neuropixels_np2usb.raw":"none"),
                "decoded_ap_files=" + (result.DecodedDatProbeMask == 0U ? "none" : result.DecodedDatMerged?"neuropixels_ap.dat (live combined AP)":"neuropixels_ap.dat (one per live physical probe)"),
                "decoded_ap_layout="+(result.DecodedDatMerged?"live combined; sample then ascending physical probe, 384 contiguous channels per probe":"per probe"),
                "decoded_ap_probe_mask=0x" + result.DecodedDatProbeMask.ToString("X1", CultureInfo.InvariantCulture),
                "decoded_ap_rows=" + result.DecodedDatRows,
                "raw_dropped_blocks=" + result.RawDroppedBlocks,
                "decoded_dropped_lane_blocks=" + result.DecodedDroppedLaneBlocks,
                "raw_writer_error=" + (result.RawWriterError ?? "none"),
                "decode_error=" + (result.DecodeError ?? "none"),
                "decoded_ap_sample_hz=" + result.DecodedDatSampleHz,
                File.Exists(Path.Combine(folder,"adc_calibration.json"))?"decoded_ap_codes=signed calibrated ADC counts stored in little-endian int16; see adc_calibration.json; no hardware trim or microvolt conversion claim":"decoded_ap_codes=signed nominal ADC counts stored in little-endian int16; no gain/comparator calibration or microvolt conversion",
                "sample_encoding=" + (File.Exists(Path.Combine(folder,"adc_calibration.json"))?"signed_calibrated_nominal_adc_counts":NpV2SampleDecoder.SampleEncoding),
                "sample_transform=" + NpV2SampleDecoder.SampleTransform + (File.Exists(Path.Combine(folder,"adc_calibration.json"))?"; then factory gain correction":""),
                File.Exists(Path.Combine(folder,"adc_calibration.json"))?"sample_range=int16; factory-dependent; no clipping":"sample_range=-2047..2048",
                "sample_polarity=inverted relative to raw ASIC ADC codes; ASIC negative analog gain undone",
                "legacy_dat_encoding=before WILDX 1.0.0.9: unsigned raw 12-bit ADC codes; existing DAT files are not rewritten",
                "decoded_lfp_file=none (NP2 USB provides full-rate AP serial data only)",
                "preview_error=" + (result.PreviewError ?? "none"),
                "recording_mode=" + (continuous ? "continuous_until_stop" : "finite_blocks"),
                "requested_blocks=" + targetBlocks,
                "received_blocks=" + result.ReceivedBlocks,
                "verified_blocks=" + result.VerifiedBlocks,
                "wire_bytes=" + result.WireBytes,
                "payload_bytes=" + result.PayloadBytes,
                "host_total_seconds_including_configuration=" + result.Seconds.ToString("F3", CultureInfo.InvariantCulture),
                "capture_to_final_status_seconds=" + result.DataSeconds.ToString("F3", CultureInfo.InvariantCulture),
                "payload_MB_s=" + (result.DataSeconds <= 0 ? 0 : result.PayloadBytes / result.DataSeconds / 1000000.0).ToString("F3", CultureInfo.InvariantCulture),
                "max_host_completion_gap_ms=" + result.MaxCompletionGapMs.ToString("F3", CultureInfo.InvariantCulture),
                "max_host_slot_recycle_ms=" + result.MaxSlotRecycleMs.ToString("F3", CultureInfo.InvariantCulture),
                "host_buffer_budget_mib=" + result.HostBufferBudgetMb,
                "usb_read_depth=" + QueueDepth,
                "usb_read_bytes=" + (BlocksPerRead * NpV2UsbDevice.BlockBytes),
                "host_read_backlog_capacity=" + result.ReadBufferCount,
                "host_read_backlog_peak=" + result.PeakReadBacklog,
                "host_shared_block_capacity=" + result.SharedBufferCount,
                "host_shared_block_peak=" + result.PeakSharedBacklog,
                "dat_write_chunk_bytes=" + NpV2BufferPolicy.WriteChunkBytes,
                "stored_bits=" + result.Verifier.StoredBits,
                "ticks_per_block=" + result.Verifier.TicksPerBlock,
                "probe_mask=0x" + result.Verifier.ProbeMask.ToString("X1", CultureInfo.InvariantCulture),
                "live_probe_mask=0x" + result.Verifier.LiveProbeMask.ToString("X1", CultureInfo.InvariantCulture),
                "mclk_hz=" + result.Verifier.MclkHz,
                "sample_hz=" + result.Verifier.SampleHz,
                "ingress_bytes_per_tick=" + result.Verifier.IngressBytesPerTick,
                "sync_phases=" + result.Verifier.PhaseText,
                "sync_positions_checked=" + result.Verifier.SyncPositionsChecked,
                "final_state=" + (finalCapture == null ? "unavailable" : finalCapture.StateName),
                "final_capture_error=" + (finalCapture == null ? "unavailable" : finalCapture.Error.ToString(CultureInfo.InvariantCulture)),
                "final_producer_error=" + (finalCapture == null ? "unavailable" : finalCapture.ProducerError.ToString(CultureInfo.InvariantCulture)),
                "final_source_error=" + (finalCapture == null ? "unavailable" : finalCapture.SourceError.ToString(CultureInfo.InvariantCulture)),
                "note=Header contract, sequence, source-tick extent and every NP2 supersync checked on every block; performance profiles intentionally omit all application CRC; air/no-CAL input is not analog-signal qualification."
            });
            if (dat != null) dat.Dispose();
            return result;
        }

        private static string DescribeError(Exception error)
        {
            if (error == null) return null;
            var text = new StringBuilder();
            for (Exception current = error; current != null; current = current.InnerException)
            {
                if (text.Length != 0) text.Append(" -> ");
                text.Append(current.GetType().Name).Append(": ").Append(current.Message);
                Win32Exception win32 = current as Win32Exception;
                if (win32 != null) text.Append(" (Win32 error ").Append(win32.NativeErrorCode).Append(")");
            }
            return text.ToString();
        }

        private static string JoinWarnings(ConcurrentQueue<string> warnings)
        {
            if (warnings == null || warnings.IsEmpty) return null;
            var text = new StringBuilder();
            string warning;
            while (warnings.TryDequeue(out warning))
            {
                if (String.IsNullOrWhiteSpace(warning)) continue;
                if (text.Length != 0) text.Append(" | ");
                text.Append(warning);
            }
            return text.Length == 0 ? null : text.ToString();
        }
    }

    internal sealed class NpV2WireVerifier
    {
        private const int HeaderBytes = 512, PayloadBytes = 46080;
        private const int ThreeBitTicks = 122880, SixBitTicks = 61440;
        private const uint ActiveWires = 0x38U;
        private static readonly byte[] SuperSync = { 0xD3, 0x6A, 0x59, 0xC5, 0x3E, 0xA7, 0x1D, 0xE4, 0xB2, 0x4F, 0x78, 0x96 };
        private static readonly uint[] CrcTable = MakeCrcTable();
        private static readonly byte[] FrameSync = MakeFrameSync();
        private bool first = true;
        private ulong nextSequence, nextTick;
        private readonly ulong originSequence, originTick;
        private int formatVersion;
        private uint probeMask, liveProbeMask, mclkHz, sampleHz, flags, ingressBytes, codecValue, d8PhaseBits, d8LookaheadBytes;
        private readonly int[] phases = { -1, -1, -1 };
        private byte[] staticContract;
        internal long Blocks { get; private set; }
        internal ulong PayloadBytesSeen { get; private set; }
        internal int StoredBits { get; private set; }
        internal int TicksPerBlock { get; private set; }
        internal int FormatVersion { get { return formatVersion; } }
        internal string FormatName { get { return formatVersion == 3 ? "NP2USB03" : formatVersion == 2 ? "NP2USB02" : formatVersion == 1 ? "NP2USB01" : "unknown"; } }
        internal uint ProbeMask { get { return probeMask; } }
        internal uint LiveProbeMask { get { return liveProbeMask; } }
        internal uint MclkHz { get { return mclkHz; } }
        internal uint SampleHz { get { return sampleHz; } }
        internal uint Flags { get { return flags; } }
        internal uint Codec { get { return codecValue; } }
        internal string ApplicationCrcPolicy { get { return (flags & 40U) == 40U ? "none" : "legacy CRC32"; } }
        internal uint IngressBytesPerTick { get { return ingressBytes; } }
        internal uint D8PhaseBits { get { return d8PhaseBits; } }
        internal bool D8UsesRawPhase { get { return (flags & 64U) != 0U; } }
        // The decoded AP writer consumes only the phase already established by
        // this verifier's exact SuperSync check; it never searches or learns a
        // replacement phase from later payload data.
        internal int PhaseForProbe(int probe) { return probe >= 0 && probe < phases.Length ? phases[probe] : -1; }
        internal string PhaseText
        {
            get
            {
                var values = new string[3];
                for (int p = 0; p < values.Length; p++) values[p] = phases[p] < 0 ? "-" : phases[p].ToString(CultureInfo.InvariantCulture);
                return String.Join(",", values);
            }
        }
        internal ulong SyncPositionsChecked { get; private set; }

        internal NpV2WireVerifier() : this(0UL, 0UL) { }

        internal NpV2WireVerifier(ulong originSequence, ulong originTick)
        {
            if (originSequence > UInt64.MaxValue / 61440UL || originTick != originSequence * 61440UL)
                throw new ArgumentOutOfRangeException("originTick");
            this.originSequence = originSequence; this.originTick = originTick;
        }

        internal void Feed(byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset > bytes.Length - NpV2UsbDevice.BlockBytes) throw new InvalidDataException("NP2 USB block bounds are invalid.");
            bool three = EqualAscii(bytes, offset, "NP2USB02"), six = EqualAscii(bytes, offset, "NP2USB01"), v3 = EqualAscii(bytes, offset, "NP2USB03");
            int version = three ? 2 : six ? 1 : v3 ? 3 : 0;
            uint blockProbeMask, blockLiveMask, blockMclk, blockSample, blockFlags, blockIngress, blockD8Phase, blockD8Lookahead, codec;
            int bits, ticks;
            if (v3)
            {
                blockProbeMask = U32(bytes, offset + 76); blockLiveMask = U32(bytes, offset + 80);
                bits = (int)U32(bytes, offset + 156); codec = U32(bytes, offset + 160);
                ticks = codec == 4U ? (bits == 0 ? 0 : PayloadBytes * 8 / bits) : codec == 5U ? 46080 : codec == 6U ? 40960 : 0;
                blockMclk = U32(bytes, offset + 44); blockSample = U32(bytes, offset + 60);
                blockFlags = U32(bytes, offset + 52); blockIngress = U32(bytes, offset + 84);
                blockD8Phase = U32(bytes, offset + 176); blockD8Lookahead = U32(bytes, offset + 180);
                bool profile = codec == 4U && PopCount3(blockProbeMask) == 1 && (blockProbeMask & 4U) == 0U && bits == 3 && ticks == 122880 ||
                               codec == 4U && blockProbeMask == 3U && bits == 6 && ticks == 61440 ||
                               codec == 5U && blockProbeMask == 3U && bits == 8 ||
                               codec == 6U && blockProbeMask == 7U && bits == 9;
                if (blockProbeMask == 0U || blockProbeMask > 7U || (blockLiveMask & ~blockProbeMask) != 0U ||
                    (blockSample != 7500U && blockSample != NpV2CaptureStatus.Np2Usb03Sample15KHz && blockSample != NpV2CaptureStatus.Np2Usb03Sample20KHz && blockSample != NpV2CaptureStatus.Np2Usb03Sample30KHz) ||
                    blockMclk != blockSample * 1600U || (blockIngress != 1U && blockIngress != 2U) ||
                    !profile || (blockIngress == 1U && (blockProbeMask & 4U) != 0U && codec != 6U) ||
                    (blockFlags & 1U) == 0U || (blockFlags & ~255U) != 0U ||
                    (blockFlags & 192U) == 192U ||
                    ((blockFlags & 128U) != 0U && (blockFlags & 32U) == 0U) ||
                    (blockLiveMask != blockProbeMask && (blockFlags & 2U) == 0U) ||
                    (blockLiveMask == 0U && (blockFlags & 4U) == 0U) || U32(bytes, offset + 48) != WireMask(blockProbeMask))
                    throw new InvalidDataException("NP2USB03 capture profile is invalid.");
                if ((blockFlags & 64U) != 0U)
                {
                    if (codec != 6U || blockD8Phase >= 1600U || blockD8Lookahead != (blockD8Phase + 7U) / 8U ||
                        blockD8Lookahead > 200U || !AllZero(bytes, offset + 184 + (int)blockD8Lookahead,
                            508 - 184 - (int)blockD8Lookahead))
                        throw new InvalidDataException("NP2USB03 raw D8 phase/lookahead metadata is invalid.");
                }
                else if (blockD8Phase != 0U || blockD8Lookahead != 0U ||
                    ((blockFlags & 128U) != 0U ?
                        !AllZero(bytes, offset + 176, 256 - 176) || !AllZero(bytes, offset + 396, 508 - 396) :
                        !AllZero(bytes, offset + 176, 508 - 176)))
                    throw new InvalidDataException("NP2USB03 has unexpected D8 phase metadata.");
                for (int p = 0; p < 3; p++) if ((blockProbeMask & (1U << p)) == 0U &&
                    (!AllZero(bytes, offset + 96 + p * 8, 8) || !AllZero(bytes, offset + 120 + p * 8, 8) ||
                     U32(bytes, offset + 144 + p * 4) != 0U || U32(bytes, offset + 164 + p * 4) != 0U))
                    throw new InvalidDataException("NP2USB03 has metadata for an unselected probe.");
            }
            else
            {
                bits = three ? 3 : six ? 6 : 0; ticks = three ? ThreeBitTicks : six ? SixBitTicks : 0;
                blockProbeMask = blockLiveMask = 2U; blockMclk = 48000000U; blockSample = 30000U;
                blockFlags = 1U; blockIngress = 1U; codec = three ? 3U : 2U;
                blockD8Phase = blockD8Lookahead = 0U;
            }
            if (version == 0 || U32(bytes, offset + 8) != (uint)version || U32(bytes, offset + 12) != HeaderBytes ||
                U32(bytes, offset + 16) != NpV2UsbDevice.BlockBytes || U32(bytes, offset + 20) != PayloadBytes ||
                U32(bytes, offset + 40) != (uint)ticks || U32(bytes, offset + 44) != blockMclk ||
                U32(bytes, offset + 52) != blockFlags || !Equal(bytes, offset + 64, SuperSync) ||
                U32(bytes, offset + (v3 ? 156 : 100)) != (uint)bits || U32(bytes, offset + (v3 ? 160 : 104)) != codec ||
                (!v3 && (U32(bytes, offset + 48) != ActiveWires || U32(bytes, offset + 96) != 1U)))
                throw new InvalidDataException("NP2 USB header contract mismatch.");
            if ((blockFlags & 32U) != 0U ? U32(bytes, offset + 508) != 0U : U32(bytes, offset + 508) != Crc(bytes, offset, 508))
                throw new InvalidDataException("NP2 USB header CRC contract mismatch.");
            if ((blockFlags & 8U) != 0U ? U32(bytes, offset + 56) != 0U : U32(bytes, offset + 56) != Crc(bytes, offset + HeaderBytes, PayloadBytes))
                throw new InvalidDataException("NP2 USB payload CRC contract mismatch.");
            ulong sequence = U64(bytes, offset + 24), tick = U64(bytes, offset + 32);
            if (first)
            {
                if (sequence != originSequence || tick != originTick)
                    throw new InvalidDataException("NP2 stream first block/source tick differs from immutable origin.");
                formatVersion = version; StoredBits = bits; TicksPerBlock = ticks;
                probeMask = blockProbeMask; liveProbeMask = blockLiveMask; mclkHz = blockMclk;
                sampleHz = blockSample; flags = blockFlags; ingressBytes = blockIngress; codecValue = codec;
                d8PhaseBits = blockD8Phase; d8LookaheadBytes = blockD8Lookahead;
                staticContract = new byte[448]; Buffer.BlockCopy(bytes, offset + 60, staticContract, 0, staticContract.Length);
                if ((blockFlags & 64U) != 0U) Array.Clear(staticContract, 184 - 60, 200);
                if ((blockFlags & 128U) != 0U) Array.Clear(staticContract, 256 - 60, 140);
            }
            else if (formatVersion != version || StoredBits != bits || TicksPerBlock != ticks ||
                     probeMask != blockProbeMask || liveProbeMask != blockLiveMask || mclkHz != blockMclk ||
                     sampleHz != blockSample || flags != blockFlags || ingressBytes != blockIngress || codecValue != codec ||
                     d8PhaseBits != blockD8Phase || d8LookaheadBytes != blockD8Lookahead ||
                     !EqualContract(bytes, offset + 60, staticContract, (blockFlags & 64U) != 0U, (blockFlags & 128U) != 0U) || sequence != nextSequence || tick != nextTick)
                throw new InvalidDataException("NP2 USB format, metadata, sequence, or source-tick extent changed mid-stream.");
            for (int p = 0; p < 3; p++) if ((blockLiveMask & (1U << p)) != 0U)
            {
                int group = version == 1 ? 1 : version == 2 ? 0 : Rank(blockProbeMask, p);
                if (first)
                {
                    int relativePhase;
                    try { relativePhase = FindPhase(bytes, offset, offset + HeaderBytes, bits, ticks, group, codec, p,
                        blockD8Phase, (blockFlags & 64U) != 0U); }
                    catch(InvalidDataException ex){throw ProbeFault(p,ex.Message);}
                    phases[p] = (relativePhase + 1600 - (int)(tick % 1600UL)) % 1600;
                    if (v3 && (blockFlags & 16U) == 0U && U32(bytes, offset + 164 + p * 4) != (uint)phases[p])
                        throw ProbeFault(p,"phase metadata is incorrect.");
                }
                // Visit the four sync positions directly instead of doing two
                // divisions on every wire tick. This is cheap enough to check
                // EVERY block, including partial boundary frames.
                {
                    int atPhase = (int)((tick % 1600UL + (ulong)phases[p]) % 1600UL);
                    int frame = atPhase / 100;
                    for (int start = -(atPhase % 100); start < ticks; start += 100, frame = (frame + 1) & 15)
                    {
                        for (int bit = Math.Max(0, -start); bit < 4 && start + bit < ticks; bit++)
                        {
                            int i = start + bit;
                            if (GroupBits(bytes, offset, offset + HeaderBytes, i, bits, group, ticks, codec, p,
                                blockD8Phase, (blockFlags & 64U) != 0U) != FrameSync[frame * 4 + bit])
                                throw ProbeFault(p,"supersync mismatch at source tick " + (tick + (ulong)i).ToString(CultureInfo.InvariantCulture) + ".");
                            SyncPositionsChecked++;
                        }
                    }
                }
            }
            nextSequence = sequence + 1UL; nextTick = tick + (uint)ticks; first = false;
            Blocks++; PayloadBytesSeen += PayloadBytes;
        }

        private static InvalidDataException ProbeFault(int probe,string detail)
        {var error=new InvalidDataException("NP2 P"+(probe+1)+": "+detail);error.Data["NP2PhysicalProbe"]=probe+1;return error;}

        private static int FindPhase(byte[] bytes, int headerOffset, int payloadOffset, int bits, int ticks,
            int group, uint codec, int probe, uint d8Phase, bool rawD8Phase)
        {
            int matches = 0, phase = -1, inspect = Math.Min(4800, ticks);
            for (int candidate = 0; candidate < 1600; candidate++)
            {
                bool valid = true;
                for (int i = 0; i < inspect && valid; i++)
                {
                    int position = (i + candidate) % 1600;
                    if (position % 100 < 4 && GroupBits(bytes, headerOffset, payloadOffset, i, bits, group,
                        ticks, codec, probe, d8Phase, rawD8Phase) != Expected(position)) valid = false;
                }
                if (valid) { phase = candidate; matches++; }
            }
            if (matches != 1) throw new InvalidDataException("NP2 supersync phase is not uniquely valid (" + matches + " candidates).");
            return phase;
        }

        private static bool EqualAscii(byte[] bytes, int offset, string text)
        {
            if (offset < 0 || offset > bytes.Length - text.Length) return false;
            for (int i = 0; i < text.Length; i++) if (bytes[offset + i] != (byte)text[i]) return false;
            return true;
        }
        private static bool Equal(byte[] bytes, int offset, byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++) if (bytes[offset + i] != expected[i]) return false;
            return true;
        }
        private static bool EqualContract(byte[] bytes, int offset, byte[] expected, bool rawD8Phase, bool sensorHeaders)
        {
            for (int i = 0; i < expected.Length; i++)
                if ((!rawD8Phase || i < 184 - 60 || i >= 184 - 60 + 200) &&
                    (!sensorHeaders || i < 256 - 60 || i >= 396 - 60) && bytes[offset + i] != expected[i]) return false;
            return true;
        }
        private static byte GroupBits(byte[] bytes, int headerOffset, int payloadOffset, int tick,
            int bits, int group, int ticks, uint codec, int probe, uint d8Phase, bool rawD8Phase)
        {
            if (tick < 0 || tick >= ticks || (bits != 3 && bits != 6 && bits != 8 && bits != 9) || group < 0)
                throw new ArgumentOutOfRangeException("tick");
            if (codec == 5U) return (byte)((bytes[payloadOffset + tick] >> (probe * 3)) & 7U);
            if (codec == 6U)
            {
                byte raw = bytes[payloadOffset + tick];
                if (probe < 2) return (byte)((raw >> (probe * 3)) & 7U);
                uint d8BitIndex = (uint)tick + (rawD8Phase ? d8Phase : 0U);
                uint d8 = rawD8Phase && d8BitIndex >= (uint)ticks
                    ? (uint)((bytes[headerOffset + 184 + (int)((d8BitIndex - (uint)ticks) >> 3)] >> (int)((d8BitIndex - (uint)ticks) & 7U)) & 1U)
                    : (uint)((bytes[payloadOffset + ticks + (int)(d8BitIndex >> 3)] >> (int)(d8BitIndex & 7U)) & 1U);
                return (byte)(((raw >> 6) & 3U) | (d8 << 2));
            }
            if (group >= bits / 3) throw new ArgumentOutOfRangeException("group");
            int bit = tick * bits + group * 3, at = payloadOffset + (bit >> 3), shift = bit & 7;
            uint packed = bytes[at]; if (shift > 5) packed |= (uint)bytes[at + 1] << 8;
            return (byte)((packed >> shift) & 7U);
        }
        private static byte Expected(int position)
        {
            int frame = position / 100, bit = position % 100;
            if (bit == 0) return (byte)(frame == 0 ? 7 : 0);
            if (bit == 1) return 0;
            byte wire = 0;
            for (int line = 0; line < 3; line++)
            {
                int at = frame * 6 + line * 2 + bit - 2;
                wire |= (byte)(((SuperSync[at / 8] >> (at % 8)) & 1) << line);
            }
            return wire;
        }
        private static byte[] MakeFrameSync()
        {
            var values = new byte[64];
            for (int frame = 0; frame < 16; frame++) for (int bit = 0; bit < 4; bit++)
                values[frame * 4 + bit] = Expected(frame * 100 + bit);
            return values;
        }
        private static uint U32(byte[] bytes, int offset) { return NpV2ServiceStatus.U32(bytes, offset); }
        private static ulong U64(byte[] bytes, int offset) { return (ulong)U32(bytes, offset) | ((ulong)U32(bytes, offset + 4) << 32); }
        private static int Rank(uint mask, int probe) { int rank = 0; for (int p = 0; p < probe; p++) if ((mask & (1U << p)) != 0U) rank++; return rank; }
        private static int PopCount3(uint mask) { return (int)((mask & 1U) + ((mask >> 1) & 1U) + ((mask >> 2) & 1U)); }
        private static uint WireMask(uint mask) { return ((mask & 1U) != 0U ? 0x007U : 0U) | ((mask & 2U) != 0U ? 0x038U : 0U) | ((mask & 4U) != 0U ? 0x1C0U : 0U); }
        private static bool AllZero(byte[] bytes, int offset, int length) { for (int i = 0; i < length; i++) if (bytes[offset + i] != 0) return false; return true; }
        private static uint Crc(byte[] bytes, int offset, int length)
        {
            uint crc = 0xFFFFFFFFU;
            for (int i = 0; i < length; i++) crc = CrcTable[(crc ^ bytes[offset + i]) & 255U] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFU;
        }
        private static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1U) == 0 ? 0U : 0xEDB88320U);
                table[i] = value;
            }
            return table;
        }
    }

    internal sealed class NpV2UsbConsoleForm : Form
    {
        private readonly ComboBox devices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        private readonly TextBox destination = new TextBox { Dock = DockStyle.Fill };
        private readonly NumericUpDown blocks = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = 2000, Width = 110 };
        private readonly Button refresh = new Button { Text = "Refresh" }, inspect = new Button { Text = "Read V2 status" };
        private readonly Button browse = new Button { Text = "Output…" }, start = new Button { Text = "Start USB raw recording" }, stop = new Button { Text = "Stop" }, sdMode = new Button { Text = "Enter SD app…" };
        private readonly TextBox status = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9.0F) };
        private volatile bool busy, stopRequested;

        internal NpV2UsbConsoleForm()
        {
            Text = "WILD-X V2 — NP2 USB configuration and recording";
            ClientSize = new Size(860, 560); MinimumSize = new Size(720, 460); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 4, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "V2 USB interface:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            layout.Controls.Add(devices, 1, 0); layout.SetColumnSpan(devices, 2); layout.Controls.Add(refresh, 3, 0);
            layout.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(destination, 1, 1); layout.SetColumnSpan(destination, 2); layout.Controls.Add(browse, 3, 1);
            layout.Controls.Add(new Label { Text = "NP2USB blocks:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            layout.Controls.Add(blocks, 1, 2);
            layout.Controls.Add(new Label { Text = "1 block = 46,592 B wire / 46,080 B payload; at 20 kHz: 3.84 ms packed single / 1.44 ms raw dual / 1.28 ms split triple.", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 2); layout.SetColumnSpan(layout.GetControlFromPosition(2, 2), 2);
            var fixedProfile = new Label { Text = "Performance profile: one probe = packed 3-bit; two probes = raw 8-bit PSSI; three probes = raw PSSI plus SPI-packed D8. Application payload CRC is omitted; continuity and every probe's native SuperSync are checked on the PC.\r\nLegacy NP2USB01/02 at 30,000 samples/s remains readable.", AutoSize = true, ForeColor = Color.Navy, Padding = new Padding(0, 6, 0, 6) };
            layout.Controls.Add(fixedProfile, 0, 3); layout.SetColumnSpan(fixedProfile, 4);
            layout.Controls.Add(inspect, 0, 4); layout.Controls.Add(start, 1, 4); layout.Controls.Add(stop, 2, 4); layout.Controls.Add(sdMode, 3, 4);
            var note = new Label { Text = "USB recording checks the header contract, sequence, source-tick continuity, and native NP2 SuperSync. A fault stops and marks the run incomplete; it never silently drops/restarts data. ‘Enter SD app’ only switches personality and disconnects USB—it does not start SD recording.", AutoSize = true, MaximumSize = new Size(820, 0), Padding = new Padding(0, 6, 0, 6) };
            layout.Controls.Add(note, 0, 5); layout.SetColumnSpan(note, 4);
            layout.Controls.Add(status, 0, 6); layout.SetColumnSpan(status, 4);
            Controls.Add(layout);
            refresh.Click += delegate { RefreshDevices(); };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = destination.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath; };
            inspect.Click += delegate { Inspect(); };
            start.Click += delegate { StartRecording(); };
            stop.Click += delegate { if (busy) { stopRequested = true; stop.Enabled = false; SetStatus("Stop requested; waiting for the capture worker to preserve and close completed blocks…"); } };
            sdMode.Click += delegate { EnterSdMode(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { stopRequested = true; e.Cancel = true; SetStatus("Close deferred while the active V2 recording is stopped safely."); } };
            destination.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            SetBusy(false); RefreshDevices();
        }

        private void RefreshDevices()
        {
            if (busy) return;
            string previous = CurrentPath(); devices.Items.Clear();
            foreach (string path in NpV2UsbDevice.EnumerateDevicePaths()) devices.Items.Add(path);
            if (previous != null) for (int i = 0; i < devices.Items.Count; i++) if (String.Equals((string)devices.Items[i], previous, StringComparison.OrdinalIgnoreCase)) { devices.SelectedIndex = i; break; }
            if (devices.SelectedIndex < 0 && devices.Items.Count == 1) devices.SelectedIndex = 0;
            if (devices.Items.Count == 0 && previous != null) devices.Text = previous;
            SetStatus(devices.Items.Count == 0 ? "No B386 WinUSB V2 interface was discovered. You may paste its \\?\\usb#…B386… interface path here, then use Read V2 status; the ABI4 read validates it before capture." : "Select a B386 path, then read its V2 status before recording.");
        }

        private void Inspect()
        {
            string path = CurrentPath(); if (path == null) return;
            RunBackground("Reading V2 service and fixed-profile status…", delegate
            {
                using (var device = NpV2UsbDevice.Open(path))
                {
                    NpV2ServiceStatus service = device.GetServiceStatus();
                    NpV2CaptureStatus capture = device.GetCaptureStatus();
                    NpV2SourceStatus source = device.GetSourceStatus();
                    return "V2 ABI4 service\r\n  mode=" + service.ModeName + " (" + service.Mode + ")  busy=" + service.Busy + "  capabilities=0x" + service.Capabilities.ToString("X8") +
                        "\r\nNP2 USB\r\n  state=" + capture.StateName + "  error=" + capture.Error + "  target=" + capture.Target + "  completed=" + capture.Completed +
                        "\r\n  PSSI=" + capture.PssiHz + " Hz  active-wires=0x" + capture.ActiveWires.ToString("X2") + "  wire-format=" + capture.Format +
                        "\r\n  sampling-rate=" + capture.SampleHz.ToString("N0", CultureInfo.InvariantCulture) + " samples/s per probe  stored-mask=0x" + capture.ProbeMask.ToString("X1") + "  live-mask=0x" + capture.LiveProbeMask.ToString("X1") +
                        "  ingress=" + capture.IngressBytesPerTick + " B/tick  supported=" + capture.CurrentProfileSupported +
                        "\r\n  worst stage times: pack=" + capture.PackMaxMs + " ms  CRC=" + capture.CrcMaxMs + " ms  validate=" + capture.ValidateMaxMs + " ms  finalize=" + capture.FinalizeMaxMs + " ms" +
                        "\r\nNP2013 source\r\n  state=" + source.State + "  error=" + source.Error + "  flags=0x" + source.Flags.ToString("X8") + "  I2C ops=" + source.I2cOperations + "  I2C error=0x" + source.I2cError.ToString("X8") + (source.DegradedWarning==null?"":"\r\n  "+source.DegradedWarning) +
                        "\r\n  EEPROM identity match=" + source.EepromMatch + "/" + source.EepromReads + "  serial=" + (source.Serial.Length == 0 ? "not yet configured" : source.Serial) + "  model=" + (source.Model.Length == 0 ? "not yet configured" : source.Model) +
                        "\r\n\r\nThis read is non-invasive. Hardware identity is checked when a one-shot capture is started.";
                }
            });
        }

        private void StartRecording()
        {
            string path = CurrentPath();
            if (path == null) { SetStatus("Select exactly one V2 USB interface first."); return; }
            if (!Directory.Exists(destination.Text)) { SetStatus("Choose an existing output folder."); return; }
            uint target = checked((uint)blocks.Value); stopRequested = false;
            RunBackground("Preparing lossless V2 USB recording…", delegate
            {
                NpV2RecordingResult result = NpV2Recording.Run(path, destination.Text, target, delegate { return stopRequested; }, ReportProgress);
                return (result.Complete ? "COMPLETE" : "INCOMPLETE") + " V2 NP2USB recording\r\n  folder=" + result.Folder +
                    "\r\n  blocks received/verified/target=" + result.ReceivedBlocks + "/" + result.VerifiedBlocks + "/" + result.TargetBlocks +
                    "\r\n  payload=" + result.PayloadBytes.ToString("N0") + " B  capture rate=" + (result.DataSeconds <= 0 ? 0 : result.PayloadBytes / result.DataSeconds / 1000000.0).ToString("F3", CultureInfo.InvariantCulture) + " MB/s" +
                    "\r\n  total time incl. configuration=" + result.Seconds.ToString("F3", CultureInfo.InvariantCulture) + " s" +
                    "\r\n  status=" + Path.Combine(result.Folder, "neuropixels_np2usb_status.txt") +
                    "\r\n  error=" + (result.Error ?? "none");
            });
        }

        private void EnterSdMode()
        {
            string path = CurrentPath(); if (path == null) return;
            if (MessageBox.Show(this, "This sends only the V2 mode-switch command. The board will enter its SD application and USB will disappear.\r\n\r\nIt does NOT start a recording, and this USB console cannot inspect an SD session until the SD app has a real USB handoff. Continue?", "Enter V2 SD app", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunBackground("Requesting V2 SD personality…", delegate { using (var device = NpV2UsbDevice.Open(path)) device.RequestSdMode(); return "SD mode command acknowledged. The V2 USB interface should now disconnect. USB absence is expected and does not verify that SD recording started."; });
        }

        private void RunBackground(string initial, Func<string> action)
        {
            if (busy) return; SetBusy(true); SetStatus(initial);
            var thread = new Thread(delegate()
            {
                string message;
                try { message = action(); }
                catch (Exception ex) { message = "V2 operation failed: " + ex.Message; }
                BeginInvoke((Action)delegate { SetStatus(message); SetBusy(false); });
            });
            thread.IsBackground = true; thread.Name = "V2 USB console operation"; thread.Start();
        }

        private void ReportProgress(NpV2RecordingProgress value)
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke((Action)delegate { if (busy) SetStatus(value.Text + "\r\nVerified blocks=" + value.VerifiedBlocks.ToString("N0") + "  payload rate=" + value.PayloadMbPerSecond.ToString("F2", CultureInfo.InvariantCulture) + " MB/s"); });
        }
        private string CurrentPath()
        {
            string path = devices.SelectedItem as string;
            if (String.IsNullOrWhiteSpace(path)) path = devices.Text == null ? null : devices.Text.Trim();
            return NpV2UsbDevice.IsV2Path(path) ? path : null;
        }
        private void SetBusy(bool value) { busy = value; refresh.Enabled = inspect.Enabled = browse.Enabled = start.Enabled = sdMode.Enabled = !value; stop.Enabled = value; }
        private void SetStatus(string text) { status.Text = text; status.SelectionStart = status.TextLength; status.ScrollToCaret(); }
    }

#if !NP2_CONSOLE_STANDALONE
    /* V2 is presented through the console's existing device selector, NP setup
     * dialog and Start/Stop controls.  Keep the protocol implementation in this
     * linked source file, but do not create a second top-level NP interface. */
    public partial class Form1
    {
        private uint npV2RequestedProbeMask;
        private uint npV2RequestedSampleHz = 20000U;

        private void NpV2ObserveReceive(DateTime first, DateTime last, string folder)
        {
            ObserveRecordingSummary(first,folder);
            if (npSessionFolder == null || folder != runFolder || first == DateTime.MinValue) return;
            if (npLastReceiveUtc == DateTime.MinValue) NpFirstReceive(first, folder);
            npLastReceiveUtc = last;
        }

        private static string NpV2ProfileText(NpV2ServiceStatus service, NpV2CaptureStatus capture, NpV2SourceStatus source, NpV2ProfileCapabilities capabilities)
        {
            return "Device: WILD-X V2 NP2\r\n" +
                "Service: " + service.ModeName + "  busy=" + service.Busy + "  ABI=" + service.Abi + "\r\n" +
                "Selectable connectors: 0x" + capabilities.AvailableProbeMask.ToString("X1") +
                "  selected: 0x" + capabilities.SelectedProbeMask.ToString("X1") +
                "  rate=" + capabilities.SampleHz.ToString("N0", CultureInfo.InvariantCulture) + " samples/s\r\n" +
                "Wire profile: NP2USB0" + capture.Format + "  PSSI/MCLK=" + capture.PssiHz.ToString("N0", CultureInfo.InvariantCulture) +
                " Hz  ingress=" + capture.IngressBytesPerTick + " B/tick\r\n" +
                "Capture: " + capture.StateName + "  source=" + source.State + "  fault=" + capture.Error + "/" + capture.SourceError + "/" + source.Error +
                "  runtime configuration=" + capabilities.Valid+(source.DegradedWarning==null?"":"\r\n"+source.DegradedWarning);
        }

        private string NpV2EnsureIdleStreamMode()
        {
            string selectedUid = npRecoveryAttempt ? npSessionDeviceUid : NpV2FirmwareForm.Uid(devicePath);
            if (npRecoveryAttempt) {
                string found = null;
                foreach (string candidatePath in NpV2UsbDevice.EnumerateDevicePaths())
                    if (String.Equals(selectedUid, NpV2FirmwareForm.Uid(candidatePath), StringComparison.OrdinalIgnoreCase)) { found = candidatePath; break; }
                if (found == null) throw new IOException("Selected NP2 USB device is no longer connected; waiting for the same UID.");
                devicePath = found;
            }
            NpV2ServiceStatus identity;
            bool ready;
            using (var device = NpV2UsbDevice.Open(devicePath))
            {
                identity = device.GetServiceStatus();
                identity.RequireDeviceUid(selectedUid, NpV2FirmwareForm.Uid(devicePath));
                NpV2CaptureStatus capture = device.GetCaptureStatus();
                NpV2SourceStatus source = device.GetSourceStatus();
                ready = identity.Mode == 2U && identity.Busy == 0U && capture.State == 0U && source.State == 0U;
                if (ready) return devicePath;
                if (identity.Busy != 0U) throw new IOException("V2 is busy and cannot be reset into USB-stream mode.");
                device.RequestStreamMode();
            }
            PostToUi(delegate { lblRecInfo.Text = "Switching WILD-X V2 to USB-stream mode…"; });
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.Elapsed.TotalSeconds < 30.0)
            {
                if (ce64StopRequested || !running) throw new OperationCanceledException("Recording stopped while switching V2 USB mode.");
                Thread.Sleep(200);
                foreach (string candidatePath in NpV2UsbDevice.EnumerateDevicePaths())
                {
                    try
                    {
                        using (var candidate = NpV2UsbDevice.Open(candidatePath))
                        {
                            NpV2ServiceStatus status = candidate.GetServiceStatus();
                            status.RequireDeviceUid(selectedUid, NpV2FirmwareForm.Uid(candidatePath));
                            if (status.Mode != 2U || status.Busy != 0U) continue;
                            NpV2CaptureStatus capture = candidate.GetCaptureStatus();
                            NpV2SourceStatus source = candidate.GetSourceStatus();
                            if (capture.State == 0U && source.State == 0U) { devicePath = candidatePath; return candidatePath; }
                        }
                    }
                    catch (Exception) { }
                }
            }
            throw new TimeoutException("WILD-X V2 did not re-enumerate in idle USB-stream mode within 30 seconds.");
        }

        private void NpV2CaptureLoop()
        {
            string message;
            NpRecordingSettings settings=npRecordingSettings;
            bool complete=false;string metadataFailure=null;
            string appliedDegradedWarning=null;
            try
            {
                uint requestedMask = npV2RequestedProbeMask;
                if (requestedMask == 0U && npV2RequestedSampleHz == 30000U)
                    using (var device = NpV2UsbDevice.Open(devicePath)) requestedMask = device.GetProfileCapabilities().SelectedProbeMask;
                NpV2ProfileCapabilities.RequireUsbBandwidth(requestedMask, npV2RequestedSampleHz);
                NpV2UsbRoute route = NpV2UsbTopology.Inspect(devicePath);
                if (route.HubCount != 0 && IsHandleCreated && !npRecoveryAttempt)
                {
                    DialogResult proceed = DialogResult.No;
                    Invoke((Action)delegate
                    {
                        proceed = MessageBox.Show(this, route.Warning +
                            "\r\n\r\nThis USB path has not been qualified for continuous lossless NP2 recording. Continue anyway?",
                            "WILD-X V2 USB connection warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    });
                    if (proceed != DialogResult.Yes) throw new OperationCanceledException("Recording cancelled to use a direct PC USB port.");
                }
                string streamPath = NpV2EnsureIdleStreamMode();
                var stagedConfigurations=new List<Np2ConfigRequest>();
                var previewDefinitions=new NpPreviewDefinition[4];
                using (var device = NpV2UsbDevice.Open(streamPath))
                {
                    NpV2ProfileCapabilities capabilities = device.GetProfileCapabilities();
                    uint mask = npV2RequestedProbeMask == 0U ? capabilities.SelectedProbeMask : npV2RequestedProbeMask;
                    capabilities = device.ConfigureProfile(mask, npV2RequestedSampleHz);
                    HashSet<int> apply=NpV2ApplyRequestsForDevice(devicePath);
                    Dictionary<int,string> requested=NpV2RequestsForDevice(devicePath);
                    if(apply!=null)foreach(int connector in apply)
                    {
                        if((capabilities.SelectedProbeMask&(1U<<(connector-1)))==0U)continue;
                        string json;
                        if(!requested.TryGetValue(connector,out json))throw new IOException("NP2 apply was requested for connector "+connector+" without a retained configuration.");
                        var request=NpConfigurationDialog.PrepareV2Request(json,connector);
                        previewDefinitions[connector]=NpPreviewDefinition.FromNp2Request(request.Json,request.Selection);
                        Np2ConfigurationUsb.StageForCapture(device,request);stagedConfigurations.Add(request);
                    }
                    if(settings!=null) {
                        settings.DeviceEvidence("usb_path",streamPath);
                        uint selected=capabilities.SelectedProbeMask;
                        settings.PlannedLayout((int)selected,npMergeProbeDat,!npMergeProbeDat && (selected&(selected-1))!=0);
                        settings.Context("merge_policy", npMergeProbeDat ? "live combined AP DAT; sample then physical probe, 384 contiguous channels per probe; recovery segments separate" : "per-probe files only");
                        settings.Context("planned_ap_sample_rate_hz",capabilities.SampleHz);
                        settings.Streams(false,null,false);
                        settings.Context("misc_signals_requested",npRecordMiscSignals);
                        settings.Context("raw_usb_saved",false);
                        settings.Context("imu_delivery",npRecordMiscSignals?"sparse verified bulk snapshots are monitored and saved; not lossless native 200 Hz acquisition":"sparse verified bulk snapshots are monitored only; saving disabled by recording settings");
                        settings.Context("imu_timing","IMU I2C completion tick; analogin.dat is a 1250 Hz held-value grid from the first valid IMU snapshot, not AP sample zero");
                        settings.Context("clock_observations_file",npRecordMiscSignals?"clock_observations.csv; offline linear fit on Stop; RAW/AP samples are not retimed":"not written; monitoring uses sparse verified bulk snapshots only");
                        var stagedPorts=new List<int>();
                        var hostOnlyPorts=new List<int>();
                        foreach(var request in stagedConfigurations)stagedPorts.Add(request.Port+1);
                        foreach(var request in NpV2RequestsForDevice(devicePath))
                            if((capabilities.SelectedProbeMask&(1U<<(request.Key-1)))!=0)
                            {
                                Np2ConfigRequest staged=null;
                                foreach(var candidate in stagedConfigurations)if(candidate.Port+1==request.Key){staged=candidate;break;}
                                settings.Request(request.Key,request.Value,staged==null?null:staged.Selection);
                                if(staged==null)hostOnlyPorts.Add(request.Key);
                            }
                        settings.Context("np2_configuration_staged_ports",stagedPorts.ToArray());
                        settings.Context("np2_configuration_retained_host_only_ports",hostOnlyPorts.ToArray());
                        settings.Write();
                    }
                    if (IsHandleCreated) Invoke((Action)delegate
                    {
                        npPreviewMask = (int)capabilities.SelectedProbeMask;
                        PrepareNpPreviewForCapture((int)capabilities.SampleHz, npPreviewMask, true, 12);
                        neuropixelsPreview.PrepareProbeDefinitions(previewDefinitions);
                        llblSavefile.Text = npMergeProbeDat ? Path.Combine(runFolder,"neuropixels_ap.dat")+" (live combined AP)" : "Per-probe AP DAT";
                    });
                }
                string sensorFolder=runFolder;
                NpV2RecordingResult result = NpV2Recording.RunContinuousInFolder(streamPath, runFolder,
                    delegate { return ce64StopRequested || !running; },
                    delegate(NpV2RecordingProgress value)
                    {
                        Interlocked.Exchange(ref neuropixelsPayloadBytes, value.VerifiedBlocks * 46080L);
                        Interlocked.Exchange(ref neuropixelsPackets, value.VerifiedBlocks);
                        NeuropixelsPreviewForm preview = neuropixelsPreview;
                        if (preview != null) preview.UpdateRecordingReport(Interlocked.Read(ref neuropixelsPayloadBytes),
                            value.VerifiedBlocks, Interlocked.Read(ref neuropixelsApRows), 0, 0);
                        string thisFolder = runFolder;
                        PostToUi(delegate { NpV2ObserveReceive(value.FirstReceiveUtc, value.LastReceiveUtc, thisFolder); if (running) lblRecInfo.Text = value.Text + (appliedDegradedWarning==null?"":"\r\n"+appliedDegradedWarning) + "\r\nVerified=" + value.VerifiedBlocks.ToString("N0") + "  payload=" + value.PayloadMbPerSecond.ToString("F2", CultureInfo.InvariantCulture) + " MB/s"; });
                    },
                    delegate(int probe, short[] row)
                    {
                        Interlocked.Increment(ref neuropixelsApRows);
                        NeuropixelsPreviewForm preview = neuropixelsPreview;
                        if (preview != null) preview.PushSamples(probe, row, 1, Array.Empty<short>(), 0);
                    },
                    stagedConfigurations.Count==0?null:(Action<NpV2UsbDevice>)delegate(NpV2UsbDevice device)
                    {
                        var receipts=new List<string>();var degradedWarnings=new List<string>();var appliedVerification=new byte[3];
                        foreach(var request in stagedConfigurations)
                        {
                            Np2ConfigReply reply=Np2ConfigurationUsb.ConfirmApplied(device,request);
                            appliedVerification[request.Port]=reply.Verification;
                            string warning=Np2ConfigClient.AppliedWarning(reply);if(warning!=null)degradedWarnings.Add(warning);
                            receipts.Add("P"+(request.Port+1)+": applied; ID="+BitConverter.ToString(reply.ConfigurationId).Replace("-","")+"; CRC32="+reply.PayloadCrc.ToString("X8")+"; verification="+reply.Verification+"; transaction="+reply.Transaction+(warning==null?"":"; "+warning));
                        }
                        if(degradedWarnings.Count!=0) {
                            appliedDegradedWarning=String.Join("\r\n",degradedWarnings);
                            PostToUi(delegate {WarnRecordingOnce("probe-configuration",appliedDegradedWarning);});
                        }
                        if(settings!=null){
                            const string receiptFile="requested-configuration-applied-receipts.txt";
                            File.WriteAllLines(Path.Combine(runFolder,receiptFile),receipts);
                            foreach(var request in stagedConfigurations)settings.ConfirmRequestVerification(request.Port+1,appliedVerification[request.Port]);
                            settings.Context("np2_configuration_applied_receipt_file",receiptFile);
                            settings.Context("np2_configuration_applied_receipts",receipts.ToArray());
                            var appliedPorts=new List<int>();foreach(var request in stagedConfigurations)appliedPorts.Add(request.Port+1);
                            settings.Context("np2_configuration_applied_ports",appliedPorts.ToArray());
                            if(degradedWarnings.Count!=0)settings.Context("np2_degraded_configuration_warnings",degradedWarnings.ToArray());
                        }
                        // Publish only after every staged request has its matching
                        // applied receipt, while capture is still held before arm.
                        if(IsHandleCreated)Invoke((Action)delegate {
                            if(neuropixelsPreview!=null && !neuropixelsPreview.IsDisposed)neuropixelsPreview.ConfirmProbeDefinitions();
                        });
                    }, new Action<NpImuDisplay>(delegate(NpImuDisplay display) { PostToUi(delegate { if(runFolder==sensorFolder)ObserveNpImu(display,false); }); }), npRecordMiscSignals,npMergeProbeDat,saveRaw:false);
                npV2RecoverySafe = NpV2Retryable(result); npV2RecoveryComplete = result.Complete;
                PostToUi(delegate { if(runFolder==sensorFolder) {
                    if(result.OptionalImuDisplay!=null)ObserveNpImu(result.OptionalImuDisplay,true);
                    if(result.OptionalImuError!=null)WarnRecordingOnce("sensor-monitor","Sensor monitoring error: "+result.OptionalImuError);
                }});
                string completedFolder = runFolder;
                PostToUi(delegate { NpV2ObserveReceive(result.FirstReceiveUtc, result.LastReceiveUtc, completedFolder); });
                complete=result.Complete;metadataFailure=result.Error??(result.Complete?null:result.Warning);
                if(settings!=null) {
                    settings.Streams(npRecordMiscSignals && result.OptionalImuSamples>0,npRecordMiscSignals && result.OptionalImuSamples>0,false);
                    settings.Context("optional_imu_snapshots",result.OptionalImuRecords);
                    settings.Context("optional_imu_error",result.OptionalImuError??"none");
                    settings.Context("np2_cancelled_before_capture",result.CancelledBeforeCapture);
                    if(result.DecodedDatSampleHz!=0 && result.DecodedDatProbeMask!=0)
                        settings.Actual((int)result.DecodedDatSampleHz,(int)result.DecodedDatSampleHz,(int)result.DecodedDatProbeMask,result.DecodedDatMerged,!result.DecodedDatMerged && result.DecodedDatProbeMask!=1 && result.DecodedDatProbeMask!=2 && result.DecodedDatProbeMask!=4,"validated decoded NP2 wire format; "+(result.DecodedDatMerged?"live combined AP rows":"per-probe AP rows"));
                    if(result.FinalCapture!=null) {
                        settings.Context("np2_capture_status_words",result.FinalCapture.Words);
                        settings.Context("neural_software_arm_completed_ms",result.FinalCapture.CaptureStartedMs);
                        settings.Context("neural_timing_anchor","software arm completion, 1 ms resolution; not a first ADC-conversion timestamp");
                    }
                    if(result.FinalSource!=null)settings.Context("np2_source_status",NpRecordingSettings.Fields(
                        "words",result.FinalSource.Words,"serial",result.FinalSource.Serial,"model",result.FinalSource.Model,
                        "identity_scope","firmware source-status receipt; physical per-port identity is not advertised"));
                    settings.Context("dat_files",result.DecodedDatMerged?"neuropixels_ap.dat live combined AP; 384 contiguous channels per active physical probe; LFP absent":"neuropixels_ap.dat; probeN subfolders when multiple probes are live; LFP absent");
                }
                if (result.Complete)
                    message = "Stopped WILD-X V2 NP2 USB recording cleanly: " + result.VerifiedBlocks.ToString("N0") + " verified blocks; " +
                        result.DecodedDatRows.ToString("N0") + " AP rows at " + result.DecodedDatSampleHz.ToString("N0") + " samples/s";
                else if (result.CancelledBeforeCapture)
                    message = "Stopped WILD-X V2 NP2 USB preparation before capture began; no USB blocks were captured. Setup files remain incomplete.";
                else if (result.Error == null && result.ReceivedBlocks != 0U)
                    message = "Stopped WILD-X V2 NP2 recording with incomplete DAT output. " +
                        (result.Warning ?? "See neuropixels_np2usb_status.txt for incomplete integrity details");
                else
                    message = "Capture error: V2 NP2 USB recording could not continue: " + (result.Error ?? "no USB blocks were captured");
                if(!result.Complete && !result.CancelledBeforeCapture && result.Error==null && result.ReceivedBlocks!=0U)
                    PostToUi(delegate {WarnRecordingOnce("dat-integrity",message);});
                string degradedWarning=appliedDegradedWarning??(result.FinalSource==null?null:result.FinalSource.DegradedWarning);
                if(degradedWarning!=null){message+="\r\n"+degradedWarning;PostToUi(delegate {WarnRecordingOnce("probe-configuration",degradedWarning);});}
            }
            catch (OperationCanceledException ex) { metadataFailure=ex.Message;message = ex.Message; }
            catch (Exception ex) { npV2RecoverySafe = NpV2Retryable(ex); metadataFailure=DescribeCaptureFailure(ex);message = "Capture error: " + metadataFailure; }
            if(settings!=null)try {settings.Finished(complete,metadataFailure);settings.Write();}
                catch(Exception ex){message+="; metadata warning: "+NpRecordingSettings.ReportWriteFailure(runFolder,ex);}
            PostToUi(delegate { FinishCaptureOnUi(message); });
        }
    }
#endif
}
