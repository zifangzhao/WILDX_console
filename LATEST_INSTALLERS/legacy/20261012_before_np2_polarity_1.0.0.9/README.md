# Current installer — WILDX 1.0.0.8, 2026-10-12

[Install WILDX 1.0.0.8](WILDX_console-Setup-1.0.0.8.exe).
**Firmware Update → ROM DFU** now requests DFU directly from the selected
recovery loader. Requires new loader6408DBB8, included in the V2 firmware
delivery; old97C3716A needs one SWD update. APP-only `.xenp` cannot update it.
Published as an explicitly software-checked engineering update. The133 focused
GUI/updater checks and recovery software checks pass; board DFU enumeration
and installer execution remain unverified.
USB A33B9912, logger APP and recording/IMRO behavior are unchanged.

## Retained GUI7 recording evidence

[Previous WILDX 1.0.0.7](legacy/20261012_before_recovery_dfu_1.0.0.8/WILDX_console-Setup-1.0.0.7.exe), with unchanged USB firmware **A33B9912**. Its recording evidence below was obtained with the previous protected loader; it is not a new loader6408 test.

Continuous recording retains four-block transfers and Stop/drain ownership. Native outstanding reads increase32→64 to tolerate more host scheduling delay; dual30 credit is327.68ms rather than163.84ms. Harmless warning text no longer marks a fully verified, drained and saved recording incomplete. Actual missing/dropped blocks, USB/verifier/source faults and RAW/DAT writer errors still mark it incomplete; no lost data is concealed.

Headless production-path validation on MCU001A0004434250062039314E/direct-root USB:

- Two real NP2/30k:210-second continuous Stop window,163544 blocks,209.33632 native seconds, zero drops/errors. Both individual DAT files exceed4GiB; every9,646,215,936 DAT bytes matched RAW replay.
- Three real NP2/20k:130-second Stop window,101040 blocks,129.3312 native seconds, zero drops/errors. Every5,959,579,392 DAT bytes matched RAW replay.
- Every selected IMRO request was device-confirmed before capture; per-probe preview callbacks received all DAT rows. Retained GUI6/USB triple15 continuous180-second check passed105012 blocks; every6,193,857,024 DAT bytes matched GUI7 replay.

The completion policy44/host-credit21 focused checks passed. All13 payload hashes checked;12 dependency/configurator/updater files unchanged. This installer was packaged once, not executed or independently extracted. GUI7 is a checked local update, **not yet committed or pushed**. The older failed GUI7 r1 recording remains explicitly incomplete and is documented; only the checked r2 executable is packaged.

Load IMRO in **Recording settings → NP2 IMRO / channel maps**, enable **Apply before next V2 capture**, and **Keep request**. Confirmed physical preview mapping and DAT hardware-channel order are unchanged. Two30k uses packed6; three30k remains unsupported in this USB representation, with no silent downsampling.

Normal **BLE Mode → ROM DFU / Firmware loader** controls remain available. ROM DFU uses the existing BLE route, not a new resident-USB ROM command. Recovery accepts APP-only `.xenp`, not fused HEX. No DFU/update, SD/BLE, interactive-rendered preview, analog/LFP or hours-long endurance requalification accompanies this USB-only update. The other board's photographed source8 issue is not claimed resolved.

Shared evidence: `XE_NP/V2/Note/Tools/V2Usb/CONTINUOUS_RECORDING_20261012.md`. See [manifest](release-manifest.json), [checksum](SHA256SUMS.txt) and [previous GUI6](legacy/20261012_before_continuous_1.0.0.7). Detailed recording results are Markdown under `Note`; RAW/DAT and diagnostic receipts remain removable `_artifacts`.
