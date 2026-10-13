# Universal Neuropixels workflow

V1 and V2 use the same four workspaces: USB Mode, BLE Mode, Data Download and Advanced. The connected device selects the backend; there is no manual BLE generation selector.

USB **Recording settings** uses one dialog for connector selection, AP rate, continuous recording, merged DAT output and misc signals. Available connectors/rates come from firmware. V1 can resolve a request to an advertised lower rate; the warning shows the resolved profile. V2 retains its codec and USB bandwidth checks. The recorder rechecks the actual device profile before releasing capture.

**Probe configuration…** is available directly in USB Mode and in BLE Mode. Both generations use one connector-based IMRO/JSON editor, Save/Cancel behavior and receipt display. Each connector keeps its own map. Saving a request does not claim device application: recording stages maps after the final mode/reset and requires matching applied receipts. V1 maps are always restaged; V2 also supports retaining a host-only map. Clear removes a host request without undoing device settings. V2 device application currently supports NP2013 four-shank maps.

**Record misc signals** controls USB sensor recording for both generations and is on by default. V1 consumes native misc packets; V2 uses its existing optional snapshots/header monitor. Both retain device timestamps, signed signal values, sensor validity and an `analogin.dat` held-value 1,250 Hz grid. That grid is not a 1,250 Hz sensor acquisition claim. Optional V2 snapshot failures do not stop neural RAW capture. The selection is recorded as `misc_signals_requested` in `recording_settings.json`.

USB **Report…** displays the capture outcome and native firmware/host status, and saves `capture_report.txt` in the recording folder. BLE **Report…** exports connection, recorder, clock and map status plus the command log. Native error codes and incomplete-data warnings remain intact.

Advanced **Diagnostics & self-test…** provides the same passive device-info/report workflow for both generations. Powered self-test and manual rail controls are enabled for the resident V2 USB protocol. V1 has no compatible command and displays those actions disabled. A skipped/failed check is not an overall pass. Rail holds, exact UID checks, new report sequence admission and explicit release/reboot requirements remain in force. The firmware manager links to this same diagnostics dialog.

## USB compatibility

The user workflow is shared; existing wire formats remain generation-specific so installed firmware and recorded data remain compatible.

| Operation | V1 | V2 |
| --- | --- | --- |
| Service identity / modes | NPS1 service status | NPS2 service status |
| Configuration transaction | NPC1/NPR1, 64-byte frames, EP0 `0x70/0x71` | NPC2/NPR2, 64-byte frames, EP0 `0x7A/0x7B` |
| Map preparation | NPB1, sites/gains/references | N2B1, NP2013 shank/site maps |
| Start admission | Firmware profile, startup and applied-map receipts | Firmware profile, native block/source and applied-map receipts |
| Neural output | 384 AP + 384 LFP channels per probe | 384 AP channels per probe |
| Stop | Drain captured data before completion | Complete native block boundary and drain before completion |

CRC/transaction matching, native decoder continuity checks, signed polarity, calibration and timing evidence continue through the existing backends. This change does not install firmware or translate one generation's packet stream into the other.

## Validation

`test_universal_workflow.ps1` tests both dialogs with injected capability snapshots, empty/unavailable selections, failed reads, per-connector Save/Cancel, real file-only IMRO compilation, receipt mismatches, unsupported self-test guards and recording ownership. It renders the dialogs offscreen without discovery or hardware traffic. Existing standalone, BLE receipt/preview/clock and USB stop/drain ownership tests also run against the private application build. Hardware qualification of this UI build is pending.
