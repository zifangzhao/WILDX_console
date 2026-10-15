# V1 BLE camera and microphone — WILDX 1.0.0.30

BLE Mode now offers **Camera / Microphone** on V1 loggers advertising media
support. Select Camera, set the frame rate (default 40 fps), optionally select
Microphone (200 kHz), then Apply profile. Once the device confirms its settings
and PC time, use the existing Start recording and Stop recording buttons.
The existing unqualified-profile opt-in remains required when reported by the
firmware. Recording is stored on SD and can be downloaded over USB.

The implementation reuses V1's existing 0x33 rate-vector command, 0x90 native
parameter readback, MEDIA1 counters and selectable-SD Start/Stop protocol.
Firmware advertising these capabilities is supported without a console-driven
upgrade. Device-confirmed rates are retained; applying a profile never starts
recording automatically. Pending/reset profiles, changed connections and
unconfirmed stream/rate readback block Start or profile completion. Lost profile
receipts use read-only confirmation without replaying the settings command.

Background status refresh no longer disables the controls every two seconds.
Foreground actions wait for an in-flight poll instead of being discarded.
Unchanged profile menus keep their items and pending selection. Media recording
does not stage neural probe configurations. SD reservation accounts for the
selected camera rate and microphone. Media export checks actual nominal frame
intervals and capture duration instead of assuming 50 Hz.

Validation against actual source and the built application:

- 40 simulated media-flow checks: independent streams, native fragmented
  replies, pending-state refusal, epoch fencing, lost receipt recovery, no
  automatic Start, edited-rate refusal, menu stability and queued UI actions.
- 116 assembly checks: MEDIA1 ABI/CRC correlation, parameter/control framing
  across fragment widths 1–67, and 16/25/40/50 fps SD export with timing/fault
  failures retained and native byte counts checked.
- Existing regressions: 1,433 BLE feature checks, 12 profile-menu checks,
  653 command-write checks and 294 standalone protocol/catalog checks.
- Actual-form media-row render with synthetic status; no discovery or commands.
- GUI compiled without warnings/errors; all 13 independently extracted payload
  files match. The 12 runtime dependencies are unchanged from release 29.

The new WILDX BLE workflow has not been exercised against hardware in this task.
Prior V1 developer evidence covers the underlying APP121 SD camera40/ADC200k/IMU
protocol through the existing tools; it does not qualify this GUI or USB as
lossless. Live BLE camera/audio preview and V2 BLE media control are not added.

Private evidence: `_artifacts/ble_media_20261014/`.
Run `./test_ble_media.ps1 -ConsoleExecutable <checked-WILDX_console.exe>` for
the source-flow and actual-assembly regressions, without accessing hardware.
