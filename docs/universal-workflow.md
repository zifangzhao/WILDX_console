# Universal Neuropixels workflow

V1 and V2 use the same four workspaces: USB Mode, BLE Mode, Data Download and Advanced. The connected device selects the backend; there is no manual BLE generation selector.

USB **Recording settings** uses one dialog for connector selection, AP rate, continuous recording, merged DAT output and misc signals. Available connectors/rates come from firmware. V1 can resolve a request to an advertised lower rate; the warning shows the resolved profile. V2 retains its codec and USB bandwidth checks. The recorder rechecks the actual device profile before releasing capture.

V1 connector boxes are directly editable and default to the active profile, so P3 can be deselected without a separate automatic-selection toggle. Available ports are firmware capabilities, not physical-presence confirmation. Fixed legacy firmware keeps its fixed profile. The generic 20 kHz experimental popup is removed; two-probe 30 kHz retains its qualification warning. V2 keeps its device-selection control.

**Probe configuration…** is inside USB **Recording settings** and available in BLE Mode. Both generations use three vertically stacked probe cards. Each card loads its own IMRO/JSON, opens its editor directly, and displays a physical probe map with selected electrodes highlighted. Shanks are shown sideways; width is enlarged for readability, while longitudinal site order and NP1 stagger follow the shared geometry. Hover shows the exact channel/site assignment. Unavailable connectors remain visible with actions disabled. Save/Cancel commits or discards all host edits together. Saving a request does not claim device application: recording stages maps after the final mode/reset and requires matching applied receipts. V1 maps are always restaged; V2 also supports retaining a host-only map. Clear removes a host request without undoing device settings. V2 device application currently supports NP2013 four-shank maps.

**Record misc signals** controls USB sensor recording for both generations and is on by default. V1 consumes native misc packets; V2 uses its existing optional snapshots/header monitor. Both retain device timestamps, signed signal values, sensor validity and an `analogin.dat` held-value 1,250 Hz grid. That grid is not a 1,250 Hz sensor acquisition claim. Optional V2 snapshot failures do not stop neural capture. The selection is recorded as `misc_signals_requested` in `recording_settings.json`.

USB **Report…** displays the capture outcome and native firmware/host status, and saves `capture_report.txt` in the recording folder. BLE **Report…** exports connection, recorder, clock and map status plus the command log. Native error codes and incomplete-data warnings remain intact.

USB **Capture activity** keeps elapsed time, recording-folder size and destination disk free space in a dedicated row. V1/V2 elapsed time begins with the first received neural data, excluding preparation. Stop freezes the time while file-size updates include the final saved files. Each fresh timestamped recording starts new totals.

Select a WILD board in **GPIO / RTC** to automatically log it with USB recording. One matching board is selected automatically on initial discovery; **Off** disables it, and an explicitly selected device is retained by PNP identity. Multiple boards require selection. Discovery does not open COM ports. The **GPIO / RTC** panel below Device health displays status, saved events, channel counts, latest RTC event and clock RTT, retaining final values after Stop. GPIO failures warn while neural recording continues. Event, clock-query and session files stay at the recording root; optional per-probe metadata references those shared files. This integration currently covers USB recording. See [protocol, timing limits and checks](../Note/RECORDING_STATUS_GPIO_RTC_20261014.md).

IMU, environment, sensor queue/monitoring, degraded NP2 configuration, camera continuity and metadata warnings are shown once per category per recording and saved in `recording_warnings.txt`. Repeated packets or increasing counters do not reopen the same warning. Warning dialogs run on the UI thread; acquisition workers continue. Fatal neural capture errors retain their existing Stop/error behavior.

Metadata sidecars use flushed, uniquely named staged files and same-directory replacement with a bounded retry for transient locks. A persistent failure keeps the previous file and completed replacement in the recording folder, writes `metadata_save_error.txt` when possible, and reports the destination/native error. A metadata warning is separate from acquisition continuity errors. Preserve the full folder and resolve any metadata warning before treating the complete recording bundle as qualified; `recording_complete` and neural status describe acquisition, not successful persistence of every sidecar. The completion UI only updates the report/index and does not rewrite finalized settings, probe JSON or XML.

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
