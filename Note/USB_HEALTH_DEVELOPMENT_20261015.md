# USB health in recording info — development only

The USB transfer summary is embedded in the existing Capture activity / recording
info card. **USB details…** opens a snapshot report using the existing report
window. There is no additional workspace tab or health panel. The recording
info timer refreshes the summary once per second; recording start clears the
selected target's previous measurement window. Measurements remain in memory.

The observer reuses existing WinUSB submissions and completion harvesting. It
does not enumerate, open, reset, or change device modes to collect statistics.
The existing handle initialization queries Windows `DEVICE_SPEED` once; unknown
speed/query failures remain unavailable, with the native error retained.
SDK `winusbio.h` defines LowSpeed=1, FullSpeed=2, HighSpeed=3. The report uses
"high speed or higher" for code 3 because the public API exposes a coarse
classification, not a measured link bit rate.
[Microsoft API contract](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_querydeviceinformation).

## Measurements and accounting

- Bulk-IN successful/failed completed attempts, successfully transferred bytes,
  average throughput, pending attempts, and intentional cancellations.
- Failed / (successful + failed completed attempts) is the transfer failure
  rate. Submission errors that terminate an attempt count once. Pending 997 and
  completion-not-ready 996 do not count as failed completions. Immediate queued
  success is counted once; the V2 pointer-only submit waits for its byte result.
- Caller-requested AbortPipe marks its in-flight requests. A resulting native
  995 is reported as intentional cancellation; an unexpected 995 is a failure.
  A successful completion racing cancellation still counts as successful.
  An outstanding request whose result was not observed before handle closure
  is reported separately, not assigned a success, failure, or cancellation.
- Min/mean/max submission-to-observation duration and mean/max observed
  completion gap. These are host timings, not physical USB packet gaps.
  Throughput spans first submission through last observed completion, including
  recovery gaps. Pending requests alone do not establish throughput.
- Native Windows error values are retained per endpoint/session. Timeouts,
  device-unavailable results, unexpected aborts, and generic I/O stay separate.
  Error 31 is not called a proven stall or CRC failure. Stall detail is unavailable.
- Validated ordinary SD-download header/sequence/CRC failures have their own
  application-integrity counter. This coverage does not include every capture
  decoder or the private RAW_IO benchmark, and unavailable coverage is explicit.
  Firmware SD errors and producer/buffer overruns are not inferred from WinUSB.

The selected exact Windows device path isolates targets, and endpoints have
separate statistics. Native handle reuse creates a new session. Recovered
sessions within a recording window remain individually visible in details;
endpoint 0x81 is summarized in recording info. Control EP0 and bulk-OUT traffic
are outside this first version's transfer denominator.

[WinUSB pending/completion and timeout semantics](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_readpipe).

## Optional ETW investigation

No ETW collector, session, driver counter, or elevation prompt is started by this
change. Controller transaction errors/retries and ETW observations show
**unavailable / not collected**, not zero.

Windows USB ETW exposes xHCI, UCX, and Hub3 activity, including controller
completion codes; provider availability and event schemas vary with the Windows
USB stack. A later bounded collector can use the documented
`Microsoft-Windows-USB-USBXHCI`, `Microsoft-Windows-USB-UCX`, and
`Microsoft-Windows-USB-USBHUB3` providers. Microsoft's USB Logman procedure uses
an elevated command prompt. General ETW session control can also be granted
through Performance Log Users, subject to provider/file access restrictions.
This implementation does not alter those permissions or system logger settings.
[USB ETW overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-event-tracing-for-windows),
[USB capture procedure](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/how-to-capture-a-usb-event-trace),
[ETW session permissions](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracea).

Before controller counters can be shown, events must be correlated to the exact
device instance/controller port, connection generation, endpoint and transfer
activity. A process ID or controller-wide total is insufficient attribution.
Unattributed events and lost trace events must be reported explicitly. Multiple
provider observations of a single event must be correlated, and controller
observations must not be added to the application failure-rate numerator.
No reliable parser/target mapping is qualified here, so inventing a controller
count would be misleading. Legacy `USB_BUS_STATISTICS_0` is not used.

Bulk hardware can retry a transaction successfully without a reported transfer
failure. Neither WinUSB nor ETW guarantees a count of every physical CRC failure
or retry. A generic transaction error does not prove CRC failure; slow transfers
do not prove a bad cable. Electrical eye/jitter measurements require external
equipment. See xHCI 1.2b sections 4.10.2.3 and 4.10.2.7:
[Intel specification](https://cdrdv2-public.intel.com/625472/625472_xHCI_Rev1_2b.pdf).

## RAM-only firmware diagnostic follow-up

Existing V1 private `Tools/V1Capture/UsbBandwidth/synthetic.inc` generates an
immutable deterministic SRAM pattern with CRC. The console's existing
`NpDownloadStream` / `RawBurst` recognizes the distinct NPT1 bench capability,
and bounds burst requests to 1..4096 rounds. Normal downloads reject this
synthetic identity. This is a private device-to-PC benchmark, not a qualified
production bidirectional diagnostic mode.

The inspected V2 `Core/Src/xe_np_usb_stream.c` already declares bulk-IN and
bulk-OUT endpoints. Its ordinary OUT callback rearms the receive buffer; it
does not establish a pseudorandom-data verification/loopback contract.
Endpoint existence is not permission to repurpose it. Firmware work should
reuse the bounded RAM pattern where applicable and add an explicitly advertised,
idle-only, bounded diagnostic contract with sequence/CRC and separate producer
overrun results. The firmware owner must define/admit PC-to-device and loopback
behavior before host commands are implemented. V1 belongs in `Firmware/V1`;
root `Core` belongs to V2. No firmware source or protocol was changed here.

## Changed files and validation

- New `UsbHealth.cs` and `UsbHealthNative.cs`: accounting and observed WinUSB calls.
- `USB512WinUsb.cs`, `XeNpWinUsb.cs`, `NpLogger/NpDownloadNative.cs`,
  `NpLogger/NpDownloadStream.cs`, and the small native-reader/open hunks in
  `Neuropixels/USBConsoleV2/NpV2UsbConsole.cs`: existing transport routing,
  exact target identity, and the bounded download-integrity hook. Concurrent
  V2 capture/source changes are preserved.
- `mainForm.WildX.cs`: inline summary and existing-report detail action.
- `WILDX_console.csproj`, `Neuropixels/SD/NpV2SdDatExport.csproj`, and five
  explicit source-list scripts (`test_np1_polarity.ps1`, `test_np_camera.ps1`,
  `test_np_start_ready.ps1`, `test_sd_dat.ps1`, `test_wire.ps1`): compile links.
- `Tests/UsbHealthTests.cs`: one focused actual-assembly regression, 37 checks.

The GUI was built incrementally into `_artifacts/usb_health_20261015/bin`,
reusing caches and the existing checked exporter
`14A1196090C63CB44A31A3465A2E01B2E49F55E71C2150FEB2BB483CAC2F870A`.
Two small follow-up compile passes corrected/verified speed labeling against
the public API and local Windows SDK. No companion, firmware, or installer was
rebuilt. Final GUI build log has no warnings/errors; final focused check passed.
The populated inline UI was rendered through an offscreen controls-only host,
without product Load/Shown discovery handlers.

Final development GUI SHA256:
`2F145A35CB82D28164B41C154EF8F81AEC57F8458C233873649E3457AD5E4540`.
Source-input hashes, build/test logs and the synthetic UI render are under
`_artifacts/usb_health_20261015`. The unchanged canonical checked GUI remains
`852FA019BC30D8D971AC2603A838F8CBC0DFDBB034EC1B16DE996942B37DA11A`.

No hardware access, USB scan/read/write, flashing, reset, reconnect, recording,
format, benchmark, ETW capture, installer, version change, commit, or push was
performed. The regression uses synthetic accounting inputs and a kernel-only
last-error preservation check. Real transport timing/throughput overhead,
disconnect/stall behavior, and controller/provider telemetry remain untested.

## USB quality test button — 2026-10-15 follow-up

Recording info now includes **USB quality test…**, which becomes **Stop USB
test** while running. Progress and the result stay in recording info; the
existing report window shows the detailed result. USB details can reopen it.

This host-only test reuses the advertised NPD1 read-only download protocol, or
the existing private NPT1 RAM diagnostic protocol. Acquisition-mode firmware
without that capability is reported as unavailable. The app does not change
USB/firmware modes to make a test available.

The read window is 10 seconds or at most 256 MiB of validated payload,
whichever comes first. Each iteration submits a fresh framed read from LBA 0;
it cannot pass by rereading the host cache. Firmware header/payload CRC,
sequence, LBA and epoch are validated. NPT1 additionally compares every payload
word with the existing deterministic RAM reference pattern. NPD1 is explicitly
labeled **SD-backed**: its timing includes storage and is not an isolated RAM
benchmark or peak HS bandwidth measurement.

The test uses an exclusive device handle. It refuses unavailable cards,
non-idle download services, unsupported identities, and bad request geometry.
It sends no recording/profile/format/mode command, card reset, pending-stream
cancel, ResetPipe or FlushPipe. The only firmware OUT request during the test
is the existing 0x61 read command. Normal download initialization and its
original constructor entry point are retained.

Native bulk requests retain the existing 3-second timeout; control requests
use WinUSB's documented default control timeout. Cancellation is checked before
each read and between partial native completions. A partially received frame
has a separate 15-second application deadline; expiry produces **incomplete**,
not a claimed integrity pass or an invented Windows failure. Cancellation waits
for an in-flight native call to finish. There is no automatic retry or reset
after failure. An interrupted device may need to be returned to an idle download
state before another test. See
[WinUSB timeout policy](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-functions-for-pipe-policy-modification).

Starting is guarded against recording, recovery, discovery, diagnostics,
download, BLE ownership, and an open media preview. While testing, the existing
exclusive-operation UI gate disables competing device actions and tab changes.
The recording-start handler also rejects a programmatic start. Closing requests
cancellation and waits. Diagnostic sessions have a separate accounting scope;
their bytes/failures do not change the recording's health counters.

New source: `UsbQualityTest.cs` and `mainForm.UsbQuality.cs`. Updated:
`NpLogger/NpDownloadStream.cs`, `UsbHealth.cs`, `mainForm.WildX.cs`, the single
start guard in `mainForm.cs`, and compile entries in `WILDX_console.csproj`.
Focused checks: `Tests/UsbHealthTests.cs` and new
`Tests/UsbQualityTransportTests.cs`.

Validation: 57 actual-assembly accounting, ownership, bounds, cancellation,
routing and inline-UI checks passed. Another 17 checks compile the actual
download/quality/accounting sources with a fake native boundary: busy/absent/
unsupported refusal before payload reads, exclusive passive initialization,
fresh reads, successful RAM-reference checking, injected pattern/CRC failures
without retry, partial-read cancellation, diagnostic/recording separation, and
unchanged normal-download initialization. The button was rendered in the
existing recording-info action row. This is 74 software checks, with no USB
enumeration or hardware I/O.

Cached GUI-only development output is under
`_artifacts/usb_quality_button_20261015`; companion binaries were reused. Final
GUI SHA256:
`3905E2DE9AF196F3D9824E19124EF2ABE4166203B10FB1BFEF8817E6C52136E6`.
Build/test logs and source-input hashes are there. No installer, version,
canonical checked GUI, firmware, Git commit, or publication was changed. No
physical device quality result, actual timeout/disconnect behavior, ETW
correlation, or physical cable-error measurement is qualified by these checks.

## Continuous USB quality mode — 2026-10-15 follow-up

The existing recording-info action row now has **Continuous**, checked by
default, beside the quality-test button. Unchecking it keeps the bounded
10-second / 256 MiB test. The selection is locked while a test is running.
Continuous mode has no session time or byte cap: it runs until **Stop USB
test** or the first transfer/integrity error, without automatic retry.

Progress shows elapsed hours/minutes/seconds and validated MiB. A manual stop
reports **stopped** without inventing a fault. Continuous completion stays
inline; USB details shows the report on request without an automatic modal
popup. Active diagnostic counters remain separate from recording counters.

The native bulk timeout remains 3 seconds. Continuous mode applies the
15-second application deadline to each fresh block rather than the whole
session, so a healthy long run does not expire at the previous total deadline.
Cancellation still waits for the current native call. All passive protocol,
exclusive-ownership, and no-mode-change safeguards above remain in place.
No V1/V2 firmware change is required to extend the host loop; the device must
already advertise a supported download or diagnostic protocol.

Validation: 65 actual-assembly checks and 19 actual-source/fake-native checks
passed, 84 total. Simulated continuous input runs for 600 seconds and beyond
256 MiB before stopping on request. Other cases verify first-error termination,
two fresh transport reads before a requested stop, the locked/default checkbox,
and a healthy 48-hour session with a one-second current block. Both bounded
total-deadline expiry and continuous per-block expiry are checked. The controls
were rendered together in the existing recording-info row. These are software
checks; no hardware endurance run or real USB I/O was performed.

One cached GUI-only incremental build is under
`_artifacts/usb_quality_continuous_20261015`, with source-input hashes, clean
build log, test logs, and the offscreen render. GUI SHA256:
`68DF134129D34EC9419CD5FDD4D6F376491EF9A542F4B9F30D255E1C07612640`.
The checked exporter was reused. No installer, version, canonical checked GUI,
firmware, Git commit, or publication was changed.

## Package and push — 2026-10-15

At the human's separate package-and-push request, the checked continuous-mode
GUI above and its checked exporter were packaged once without a rebuild,
version change, firmware build, or repeated software/hardware qualification.
The installer retains the **Run WILD-X** finish-page option.

Delivery file:
`LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.33-USB_QUALITY_CONTINUOUS_20261015.exe`.
SHA256:
`8C4723CAD4F3A6C995C72ACD1E6719721CC758C7B611BA32AF8D4ADA98CA16BD`.
All 13 runtime files independently extracted from that installer match the
checked payload. The 11 supporting runtime files other than the GUI/exporter
match the previous general installer. The installer was not executed.

This is a named feature build of the already-checked 1.0.0.33 bytes. The
general 1.0.0.33 installer is retained. The checked GUI was built from all 86
recorded workspace inputs, including concurrent V2 development inputs; it is
not represented as an isolated general-release-33 plus USB-only binary.
`LATEST_INSTALLERS/usb-quality-manifest.json` records the exact build inputs,
payload, scope and limitations. `Note/USB_QUALITY_BUILD_SOURCE_20261015.patch.gz`
preserves the checked GUI source delta from base commit
`f844ac829aab86af6f317d18c1122e681750c81d`, including those concurrent inputs,
for provenance. Only this task's application source hunks are committed as
source changes; unrelated pending development stays in the working tree.

## Acquisition-mode failure and capture-panel split — 2026-10-15

The human reported two failures from the published feature build on V1 UID
`001B0019434250062039314E`: request 0x60 failed with Windows 31 in acquisition
mode, and a separate download-mode run reported either zero capacity or a busy
service. Neither run received bulk data, so neither is a cable-quality result.
The old combined unavailable-card/busy error did not distinguish those states.

The test now routes unsupported download probes (Windows 1/31 on the initial
0x60 only) to the existing V1 acquisition SRAM test, when the selected device
is V1. A fresh exclusive handle verifies the bulk endpoint and a recognized,
configured, idle 0x42 status before sending the existing synthetic START 0x43.
The firmware must acknowledge and report the synthetic flag. Every A512 packet
checks stream ID, flags, exact sequence and all 506 reference bytes against the
existing firmware's 0/17 SRAM-buffer pattern. It does not start probe capture,
save files, touch the SD card, switch modes, flash or retry a failed block.
STOP 0x41 and an idle-status check retire the attempted test before closing.
Cleanup failure is reported as a failure, including already-validated bytes.
Unsupported firmware is refused rather than given a false pass.

Source evidence is the existing `Core/Src/xe_np_usb_stream.c` synthetic command,
status flags and `FillSyntheticPayload` / `PrepareSyntheticPacket` contract,
and the V1 UnifiedUsb vendor-router fixture. V1 camera source preparation also
preserves the synthetic path. These sources were inspected read-only. The new
acquisition fallback is deliberately V1-only; it does not send legacy commands
to V2. V2 retains the supported download/diagnostic path. This change does not
establish that every installed V1 build implements 0x43.

Download-mode errors now distinguish **busy (with the service state)** from
**no usable card**. A busy service found before the test is not cancelled.
A card-independent V1 test must run in USB-stream mode. After this test has
admitted an idle download service and issued its own read, an interrupted
partial read is cancelled with existing 0x62 and checked for idle for up to
two seconds. It cannot leave its own pending read behind after Stop without
reporting cleanup failure. No card reset, pipe reset or automatic mode change
was added. Short control replies are separate malformed-reply failures; generic
Windows 31 is not by itself described as proof of a firmware or cable fault.

The final requested layout splits only the original **Capture activity** cell
into adjacent **Capture activity** and **USB** cards. Recording time, file size,
free space, capture report and paths remain on the left; USB status, test,
Continuous and Details occupy the right part of that same cell. Device health,
GPIO/RTC and Network time retain their existing separate right column. Labels
wrap and the shared row grows to fit both cards. The earlier right-column USB
placement was corrected after the human clarified the desired split.

Final validation: 69 actual-assembly accounting/ownership/routing/UI checks and
44 actual-source checks with fake native I/O, 113 total, passed. Cases include
both optional-request error codes, V1-only routing, busy/unknown/short-status
refusal, no fallback after timeout, exact pattern/sequence errors without retry,
partial-stop cleanup, unconfirmed STOP failure, separate diagnostic counters,
and both compact/default UI renders. An initial new-fixture failure expected
IOException for a malformed reply; the fixture now also accepts the actual
InvalidDataException and passed. No physical USB, BLE or SWD access occurred.

GUI-only cached development output and source receipts are under
`_artifacts/usb_quality_acquisition_20261015`. Two follow-up incremental compiles
corrected generic-error wording and the human's clarified layout; dependencies
were reused and the final build log has no warnings/errors. Final GUI SHA256:
`D1271EC76DF47FEA84BA83395121832D861F7AC6C56132F369D0525529A11CF7`.
Version remains private 1.0.0.33. No new installer, commit, push, firmware build,
hardware test or deployment was performed in this follow-up.

### Checked-fix delivery — 2026-10-15

At the human's explicit "pack installer and push" request, the checked GUI
`D1271EC76DF47FEA84BA83395121832D861F7AC6C56132F369D0525529A11CF7`
was packaged once, with no GUI/firmware rebuild or repeated qualification.
The existing 69 actual-assembly and 44 fake-native checks are the development
evidence. All 13 independently extracted runtime files match the checked
payload; its 12 non-GUI runtime files match the preceding USB feature build.
The installer was not executed and no hardware was accessed.

Installer:
`LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.33-USB_QUALITY_FIX_20261015.exe`.
SHA256:
`75F9E4E14CAA8C30B5B757AF90AC7CF7FA2291370348D9818101E5FB06233D0C`.
The previous USB feature installer and its manifest/checksums are archived in
`LATEST_INSTALLERS/legacy/20261015_before_usb_quality_fix`; the general
1.0.0.33 installer remains available. The **Run WILD-X** finish option is kept.

`LATEST_INSTALLERS/usb-quality-manifest.json` records the exact payload and
86 checked source inputs. The frozen source delta from base commit
`6716f801978507050e0a9a0a802ad80d472e3b74` is preserved as
`Note/USB_QUALITY_FIX_BUILD_SOURCE_20261015.patch.gz`. It includes the checked
concurrent V2 inputs for provenance; this delivery commits only this task's
six modified application/test files plus release records. Unrelated pending
V2 development is preserved. Firmware compatibility and hardware limitations
remain as recorded above; packaging does not add hardware qualification.
