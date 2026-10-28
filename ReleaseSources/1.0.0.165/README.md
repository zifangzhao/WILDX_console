# WILD-X 1.0.0.165 — V2 firmware handover

The installer bundles the validated V2 APP `EC5C2198`, USB service `A71BDECC` and recovery `6408DBB8`, with the matching firmware catalog. Installing the console does not flash a connected logger. V1 and V1-camera bundles remain unchanged; this release adds no new V1 hardware qualification.

Console 165's actual BLE mode handler, Data Download listing, download and native DAT export were exercised on one V2 logger with three probes at 20 kHz and a Delkin V90 card. A roughly 10-second SD recording completed with 7,812 blocks; export produced 199,986 AP rows per probe and reported no observed source gaps, discarded ticks or dropped blocks. The previous 320 catalog entries remained intact, and the new recording appeared in the 321-entry download list. The short USB capture completed with 7,280 verified transfers and zero reported raw or decoded drops.

The corrected private test selects BLE destination 2 for USB streaming; destination 1 selects update mode. Console 165's production callers already used the correct selector. The tested route is BLE USB2 acknowledgement, same-device USB streaming, then the actual Data Download `ListAsync`/`EnsureDownload` path to USB mode3. This handover required no runtime firmware change, SWD attachment/reset, physical reconnect or power cycle. Exact component images were tested; the complete fused image was not freshly flashed.

Saved SD sensor evidence contained 1,925 CRC-valid accelerometer/gyro observations and 967 distinct magnetometer observations over 9,999 ms. Observed saved AG/MAG gaps reached 7/18 ms. The 12,500-row `analogin.dat` is a 1,250 Hz held-value compatibility grid. These measurements describe saved producer observations, not complete physical sensor conversion coverage. An environmental-sensor error remained recorded; environmental sensing was not qualified.

Normal Stop drains accepted writes and commits footer/catalog metadata. Footer offset 112 contained 1,953 **written RUs**; it is not a verified-payload-block counter. Independent PC export checked the stored framing and continuity of the new test recording. It does not exclude every possible ADC bit error or whole-superframe loss.

Pre-arm and first-sync transport opt-in remain disabled by default. No first-sample UTC, NP/IMU conversion-phase lock or physical sample lock is claimed. Results cover this short profile/card test, not all modes, cards, endurance, recovery or BLE update-mode behavior.

| Artifact | SHA256 |
|---|---|
| `WILDX_console.exe` | `2A776C032C2DB84EFE7A5D44281E02245C2B98F3AC61147A8AAC7B2593425881` |
| `NpV2SdDatExport.dll` | `4638FD2DBA09BC673DB0D0BFA5174DB1BF2609B684E736D74A4D406C297AD526` |
| `WILDX_console-Setup-1.0.0.165.exe` | `B19AD0D3C7F59FE67F355AB0EF56D176F68BCF699E4054D57CB099E5CF8FB581` |

See `validation-summary.json` for sanitized evidence and source receipt hashes. The source freeze contains 192 source/support files and 16 separately snapshotted packaging documents. Only the snapshot's two AssemblyInfo version attributes were reconstructed 166→165; the workspace was unchanged. This is a documented controlled source bridge, not an as-built forensic or byte-rebuild claim. Final installer extraction matched 48 runtime files with no unexpected runtime files. Packaging reused the existing 165 binaries and firmware; the installer was not executed.

Firmware source and retained-object provenance remain in [release 158](../1.0.0.158/firmware/). Package identities are listed in [package-verification.json](package-verification.json); [packaging-receipt.json](packaging-receipt.json) records the rejected private staging package and final clean packaging pass.
