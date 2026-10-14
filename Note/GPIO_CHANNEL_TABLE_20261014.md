# GPIO channel table — 1.0.0.23

The GPIO / RTC panel now contains a table with **ID / Status / Count / Freq (Hz)** columns. The first five rows are the header and IDs 0–3. IDs match the raw packet's zero-based channel value. Status retains the 350 ms activity LED; count and one-second RTC event frequency retain their existing behavior. The panel still extends to Network time.

Additional received channel IDs add numerically sorted rows, with vertical scrolling. The existing protocol's byte field permits host IDs 0–255; the board must actually provide those inputs. No firmware change or capability-advertisement claim is made. Channel data is collected by ID rather than four fixed positions. Counts, rates and IDs are copied together under the logger lock, preventing mismatched columns when channels appear. Resetting the recording/device returns to IDs 0–3; final counts remain visible after Stop.

`gpio_rtc_session.json` now includes `channel_ids`; count and recent-rate arrays follow that exact order. Raw event/clock files and neural DAT formats are unchanged. Sparse IDs are retained without inserting imaginary rows for every unused ID. Rate memory is allocated per observed channel and bounded by the byte ID range.

Software checks passed: 42 GPIO protocol/clock/file/frequency/channel checks; 37 actual-GUI table/warning/LED/rate checks; 16 recording-metrics checks; 294 standalone protocol/catalog/layout checks; 134 capture ownership checks. Cases include IDs 5/7/255, sparse sorting, consistent snapshots, saved ID mapping, the initial 5×4 table, added rows, 16-channel scrolling without horizontal clipping, reset and multiple window widths. [The rendered UI](../docs/screenshots/usb-recording-gpio.png) uses illustrative values.

The isolated GUI starts from `c94c0d7ea2e22dd6ddb54b8001ae89de08bcbd78` with only GPIO table/channel handling and version overlays. Compilation has no warnings or errors. MSBuild recovered from one Defender file-copy lock using its normal retry. All twelve non-GUI runtime files remain identical to 1.0.0.22; all thirteen installer runtime files are independently extracted and hash-verified. Pending BLE/media work is excluded. No physical COM/USB/BLE, firmware, real recording or installer execution. Hardware qualification remains pending; existing V1 firmware support is unchanged.

Source and package hashes are in [the manifest](../LATEST_INSTALLERS/release-manifest.json). Private artifacts are in `_artifacts/gpio_channel_table_20261014`; focused checks run with `test_gpio_rtc.ps1`.

- GUI SHA-256: `1A4AE577230AFE4E5BCE1A692A14267EBD5A78C54884C082740674C5F6B29513`
- Installer SHA-256: `C0E7ED680A5C8B49D7EC0F5E6E50F6A0349F20688CDA0AB1993259D935076265` (2,333,427 bytes).
