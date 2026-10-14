# GPIO event frequency — 1.0.0.22

Each port now displays a Hz value under its label, beside the activity LED and total event count. The value counts received events in the latest one-second device-RTC window and refreshes with the existing 100 ms activity timer. PC monotonic elapsed time advances the RTC window between event or admitted clock-reply observations; UTC clock corrections are not used to measure elapsed time. Using event RTC timestamps avoids counting a USB delivery batch as simultaneous GPIO events.

The collector uses 1,000 millisecond bins shared across four ports with fixed memory, including repeated events at the same timestamp. A backward RTC change of at least one second clears the recent-rate window without changing total counts or raw logs. Invalid ports or out-of-range RTC values cannot populate the rate collector. This is the rate of received trigger events over a recent window, not a waveform-frequency or lossless-acquisition claim. The first second fills the window; missing events cannot be reconstructed.

Idle values decay to zero as events leave the window. Stop/error immediately displays `0.0 Hz` while retaining total counts. A new recording/device selection resets the display. `gpio_rtc_session.json` adds recent per-channel rates, `event_rate_window_ms` and `event_rate_timebase`; existing event and clock-query files are unchanged.

Software checks passed: 36 GPIO protocol/clock/file/frequency checks; 32 delivered-GUI warning, panel, LED and frequency checks; 16 recording-metrics checks; 294 standalone protocol/catalog/layout checks; and 134 capture ownership checks. Frequency cases cover per-port mapping, device-timed USB batches, exact one-second expiration, idle advancement, RTC rollback, invalid inputs, independent snapshots, dense same-millisecond bursts and saved metadata. The actual GUI checks units, Stop/reset and unclipped labels at 1044/1100/1450-pixel widths. [The render](../docs/screenshots/usb-recording-gpio.png) uses illustrative values.

The isolated GUI starts from `3680b4becc5f36d071fba2c4224dd49420bda10d` with only the frequency collector, display and version overlays. GUI build: zero warnings/errors. All twelve non-GUI runtime files match 1.0.0.21; all thirteen installer runtime files were independently extracted and hash-verified. Pending BLE/media work is excluded. No physical COM/USB/BLE, firmware or real recording operations, and no installer execution. Hardware qualification remains pending; existing V1 firmware support is unchanged.

Source/payload/installer hashes are in [the manifest](../LATEST_INSTALLERS/release-manifest.json). Local artifacts are in `_artifacts/gpio_event_frequency_20261014`; `test_gpio_rtc.ps1` runs the focused checks.

- GUI SHA256: `00D35B3F752230AFBECBA897EDAE623ED31A2AECE0FC971CD6B02A66CE53364B`
- Installer SHA256: `8539A37B6A24BC034619E46F5DE5631C7845375D718473D79BA824DCB7A7F433`
- Installer bytes: 2,331,780
