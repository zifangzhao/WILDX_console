# Restore existing BLE ROM DFU visibility — 2026-10-12

## Confirmed correction

The normal workspace instantiated `NpBleForm()`; ROM DFU and protected loader entry existed only when `maintenance=true`. It now instantiates `NpBleForm(true)`, exposing the original commands in BLE Mode. No transport, capture, storage, firmware or updater implementation changed. Version is 1.0.0.4 so the corrected installer is distinguishable from previous same-version packages.

## Historical evidence correction

The earlier blanket statement that V2 ROM entry was hardware-unvalidated was wrong. `XE_NP/Note/Tools/V2SdLogger/BLE_MODES_HIL_20260928.md` confirms BLE opcode 5/mode 3 acknowledgement followed by VTOR `0x0BF90000` and PC `0x0BF924CC` on MCU `001A0020434250062039314E`, ST-Link004B, APP20F19D61. That run did not establish USB enumeration or programming. This old-board result is not a new qualification of the current assembled firmware. The V1 2026-09-29 record separately confirms an actual DFU enumeration/return round-trip; it is not V2 evidence.

## Build and check

- One incremental Release component build reused the previous USB/UI cache and exporter. The published GUI source base is `6fa0099008cac95f9ac927e7c930e00d7f167dd0`; five compile inputs were pinned to that commit, and the same recorded F779 transport was retained. Unrelated private SD/BLE edits remain untouched and excluded.
- `Tests/NpDfuVisibilityTests.cs` passed 41 actual-assembly checks: normal-page buttons, disconnected disablement, unchanged opcode 5/destination 3, and stopped/fault/pending/capability admission. Main-form Load/Shown, scanning and command dispatch were not invoked.
- NSIS packaged once. Independent extraction verified all 13 runtime files. The 12 non-GUI payloads match the previous checked installer byte-for-byte.
- GUI SHA256: `06C9C3E75378D34AD3152FF0108ED34B0EB0B729A60D27C58E08D1A07D170122`.
- Installer: `WILDX_console-Setup-1.0.0.4.exe`, 2,245,892 bytes, SHA256 `DADB2995694C6CF5340FBD3ABA5FF6AAA755C0A4447DCAF3FCF4531E692B6CD2`.
- Firmware, installer execution and hardware access: unchanged/not performed. This GUI check is not a new DFU enumeration, flash/update, recording or end-to-end pair test.

## Use and remaining scope

From recovery, use Firmware Update → SD / BLE. Then connect to the stopped recorder in BLE Mode and choose ROM DFU. BOOT0 is not required for this existing reset trampoline. Do not replay a mode mutation on an uncertain outcome or change protection option bytes.

Resident USB and the B387 recovery loader still lack a direct ROM-DFU command. Recovery remains APP-only `.xenp`; fused HEX programming requires ROM enumeration or SWD. No new direct USB DFU implementation is claimed. Removable build/extraction output is under `_artifacts/dfu_visibility_20261012`; delivery and manifest are under LATEST_INSTALLERS, with the prior installer recoverably moved to legacy. No Git push or firmware promotion occurred in this task.
