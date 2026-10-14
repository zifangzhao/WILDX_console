# Recording status and GPIO/RTC integration — 1.0.0.20

USB Capture activity has a dedicated elapsed-time, recorded-folder-size and destination-disk-space row. V1/V2 time starts at the first received neural data, excluding preparation, and freezes on Stop. File size sums the actual recording folder, including shared auxiliary files; it does not scan the output parent. Fresh timestamped recordings reset the totals. Capture activity is above the live monitor so the values remain accessible at smaller window sizes.

## GPIO workflow

The GPIO / RTC selector lists WILD boards with `VID_0481&PID_5741` using read-only WMI metadata. Discovery does not open a port. One board is automatically selected on first discovery; multiple boards require selection. Off is persisted, as is an explicit board selection. Each recording verifies the selected PNP identity and COM port; an unavailable selection does not silently switch to another board.

Start opens that board before starting the neural acquisition worker. Each fresh recording has its own logger and files. Stop closes the serial producer, joins its receive/query workers, drains accepted file records and then writes the session receipt before the final recording-folder index. GPIO startup/runtime failures remain visible while neural recording continues, as requested. No GPIO firmware update or DFU command is sent.

The GPIO / RTC panel directly below Device health displays COM port and state, saved event count, CH0–CH3 counts, latest event/RTC timestamp and clock RTT. Final values remain after Stop. The port controls are locked during capture.

| File at recording root | Contents |
| --- | --- |
| `gpio_rtc_events.txt` | Existing standalone logger line format: mapped local time or `UNSYNCED`, raw device RTC milliseconds, event sequence, channel and PC receive time |
| `gpio_rtc_sync.csv` | Every host clock query and received reply, sequence, PC send/receive Unix milliseconds, raw device time, accepted RTT/offset and selected offset |
| `gpio_rtc_session.json` | Selected identity/COM/baud, start/stop and first neural host-receive time, channel/event counts, integrity counters, clock status and errors |
| `gpio_rtc_start_error.txt` | Failed startup details, when applicable |
| `recording_warnings.txt` | Timestamped warnings acknowledged during this recording |

`recording_settings.json` references the GPIO files. Optional per-probe settings point to their parent recording root rather than copying partially written logs. `recording_files.json` indexes final auxiliary files, and Capture Report includes GPIO session/startup diagnostics and warnings. Neural DAT encoding and live combined channel order are unchanged.

## Protocol and timing

The maintained standalone reference is `Ephys_station_interface/WILD_GPIO_Logger_CSharp/Program.cs`, commit `af7ea5950d16b4c8f4d60d81950e738b555de8d1`. The integration reuses its framing, 119200 baud, 8N1, DTR/RTS and clock-query algorithm without introducing a separate process or dependency.

- RTC event: `3C 8E channel sequence:u32le device_ms:u64le 3E` (16 bytes).
- Clock query: `3C 90 query_sequence:u32le 3E` (7 bytes).
- Clock reply: `3C 91 query_sequence:u32le device_ms:u64le 3E` (15 bytes).
- Twenty initial queries at 50 ms intervals, followed by one per second. The PC midpoint offset from the lowest-RTT sample among the latest sixteen valid replies maps RTC time. Pending queries are bounded and expired; invalid/unknown/duplicate replies cannot establish a mapping. Events without a valid recent reply (10 s maximum age) remain `UNSYNCED`.

Raw query observations are retained for offline timing work. This RTC-to-PC mapping is not synchronization to the neural ADC clock and has no sub-millisecond accuracy claim. The GPIO event protocol has no advertised CRC. Sequence discontinuities, malformed or partial frames, queue overflow and file/serial errors are reported; no lossless hardware acquisition claim is made. The file queue is bounded at 16,384 records. This release integrates USB recording; BLE-controlled onboard SD recording is not connected to this local GPIO lifecycle.

## Warnings and validation

IMU, environment, queue overflow/monitoring, degraded NP2 map, camera continuity, GPIO and metadata warnings use one popup per category per recording. Initial simultaneous sensor errors are grouped. Repeated packets, increasing counters and final-result duplication do not reopen the same warning. Popups run on the UI thread and do not pause USB worker threads. Existing fatal neural-error handling remains intact.

| Software check | Assertions |
| --- | ---: |
| GPIO framing/recovery, ignored DFU ACK, clock admission/selection, unknown/invalid replies, ordered Stop drain, sequence wrap/gaps, unsynced events, selected identity, independent files, startup/disconnect/queue failure and retained panel values | 25 |
| Delivered GUI warning grouping/dedup/reset, capture-error completion, GPIO startup failure continues neural state, panel placement/layout and shared root file references | 18 |
| Delivered GUI V1/V2 first-data timing, file growth, disk space, frozen Stop, new folders, stale callbacks, >24 hours and resized rendering | 16 |
| Metadata persistence and recording bundles | 22 + 203 |
| Existing V1 firmware/settings | 103 |
| Universal workflow | 106 |
| Stop/close ownership | 134 |
| Independent-recording recovery and folder naming | 125 + 53 |
| Standalone protocol/catalog/UI | 294 |
| Optional USB sensor lifecycle | 5 |

The minimum-size render initially exposed reduced IMU panel height. The final layout preserves that panel's minimum height, places activity above the monitor and passes the existing layout regression. Checks use fake serial transport and ordinary local files, with discovery and hardware access disabled. GUI builds have zero warnings/errors; the recording-bundle direct-source fixture retains its existing unassigned-cancellation warning.

The isolated GUI starts from release commit `fe4e7827347110f77202b0e7c7e763a63f2f3b83`, overlaying only this release's source. Pending BLE connection/media changes and hardware tests are excluded. All twelve non-GUI runtime files, including the shared metadata companion, configurator and updater, remain byte-for-byte identical to 1.0.0.19. All thirteen installer runtime files were independently extracted and hash-verified. The installer was not executed. No logger COM/USB/BLE, SWD, firmware, raw storage or real recording actions were performed. V1/V2 and GPIO hardware qualification remain pending; existing V1 firmware support is retained.

Source-input hashes and installer/payload identity are recorded in [the release manifest](../LATEST_INSTALLERS/release-manifest.json). Build/test/extraction artifacts are retained locally in `_artifacts/recording_summary_20261014`. Run `test_gpio_rtc.ps1` and `test_recording_summary.ps1` against the checked executable; both are included in `test.ps1`.

## Package identity

- GUI SHA256: `11ACCF7C3893D26E5D08546D5C4E73DA35F94B17FEC243A542A5C1565921897E`
- Installer SHA256: `25F61149E5BE13790D7B4318DAD9C0995742A76FE201968CECD0F413613F0394`
- Installer bytes: 2,330,105
