# USB/UI release receipt — 2026-10-11

## Frozen delivery

Installer: `LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.3.exe`, 2248179 bytes, SHA256 `34CA66023882429E7E1C29CBEA60B66257EC504ED2DED40A85B564E8AF04D8F0`.

Console: 1.0.0.3, 1,184,256 bytes, SHA256 `986AF3369664B19A740EF34C5DC53AA673D9AC535F4235E7638FDE52496DF68F`. Exporter `71F0EA0CAE654A3F262E788C6E1CE2338E5D26AFFDA9A754A8E9FFC05F24324E` reused. No application/firmware rebuild, installer execution or hardware access during packaging. NSIS first selected newer unqualified updater helpers from bin/Release; that package was rejected. Corrected packaging uses the prior published updater exactly. All 13 final extracted runtime files match the frozen payloads; no PDB is installed.

Firmware remains loader97C3716A / USB1138F666 / published APP ED881256. Images and hashes are pinned in [release-manifest.json](../LATEST_INSTALLERS/release-manifest.json). USB-only HEX preserves loader, APP and settings. The fused HEX is engineering commissioning only with SWD recovery available. No new full-stack boot/update test is claimed.

## Previously completed checks — reused, not repeated for packaging

- 25 actual-source pre-arm cancellation/full-UID checks; 134 actual-assembly UI ownership/Stop/Close checks.
- 5,046 actual GUI hardware checks on MCU001A0004434250062039314E, ST-Link004B, direct root USB. Per-port IMRO application/readback, the production capture owner and offscreen preview renderer were exercised.
- P1/30 kHz: 11.517 s, 4,492 verified blocks, 344,984 DAT rows, 264,947,712 DAT bytes, complete, 17.973 MB/s payload.
- All three/20 kHz: 11.331 s, 8,836 verified blocks, 226,201 rows per probe, 521,167,104 DAT bytes total, complete, 35.934 MB/s payload.
- Both completed captures: zero reported errors/warnings/raw/decoded/display drops. Every one of 786,114,816 saved DAT bytes matched actual-app RAW replay.
- Same-form repeated Start/rate/mask change and preparation Stop: zero bytes/no host-arm, incomplete setup files, informational explicit cancellation. Firmware deliberately enters FAILED on configuration cancellation; existing reset-separated USB mode recovery restored idle.
- Retained headless USB matrix: P1/P2 solo15/20/30, dual15/20, triple15/20; longest triple20 run59.185 s/35.986 MB/s.

Source and full chronology: [XE_NP V2 progress section309](https://github.com/zifangzhao/XE_NP/blob/V2/Note/Tools/V2SdLogger/PROGRESS_PLAN.md). Removable evidence: XE_NP V2 `_artifacts/v2_usb_ui_20261011`.

## Scope and provenance

Only validated USB/UI source and fixtures are published with this package. Private SD/BLE development is not promoted. The tested build used an isolated Compile-item override for the exact recovered F779-era BLE input, SHA256 `2DD24EBE1C3A2460F0CDA3C776B961DDBE1F989B3F8C09529C6779920D0AAA5F`; the private current BLE candidate was excluded. Binary hashes, not a build of the current dirty/default source tree, identify this delivery. Existing configurator runtime was exercised in the IMRO checks; updater runtime is copied byte-identically from the prior published installer.

The current untracked offline replay fixture's source hash differs from the recorded handoff hash, so it is not included in this publication. Existing completed replay results are reused without a new build/test cycle. This does not affect the frozen GUI, installer or the three other hash-matching source/UI fixtures.

New actual GUI tests cover P1/30k and triple20k, not every matrix profile. Multi-probe30 USB remains warned/unqualified; USB7.5k, P3-only and other pairs remain unsupported. Transport interruption produces incomplete data and requires a new recording; no gapless auto-reconnect. Air/no-CAL analog/electrode quality, manual visual usability, hours-long endurance, clean install/uninstall, full firmware update, and SD/BLE modes are not qualified by these USB tests. Replay tests file routing/serialization against the app decoder, not independent ADC physics.

Superseded deliverables are recoverably retained in `LATEST_INSTALLERS/legacy/20261011_before_usb_ui_1.0.0.3`. No unrelated cleanup or hardware operation occurred.
