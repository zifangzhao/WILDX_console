# Current installer — WILDX 1.0.0.4, 2026-10-12

[Install WILDX 1.0.0.4](WILDX_console-Setup-1.0.0.4.exe). The normal **BLE Mode** page now exposes the existing **ROM DFU** and **Firmware loader** controls. The original stopped/fault/pending-command checks and BLE opcode 5/destination 3 are unchanged.

From recovery: choose **Firmware Update → SD / BLE**, then connect in **BLE Mode → ROM DFU**. This is not a new resident-USB-to-ROM command. The protected recovery loader still accepts APP-only `.xenp` updates, not fused HEX.

One incremental Release build and 41 offscreen actual-assembly checks passed; all 13 extracted installer payloads match. Dependencies, updater and firmware are unchanged. Historical V2 ROM entry was confirmed on 2026-09-28; USB enumeration/upload was not validated in that run. No new hardware access or update-cycle qualification was performed.

See [current evidence](../Note/DFU_VISIBILITY_20261012.md), [manifest](release-manifest.json) and [checksum](SHA256SUMS.txt). The previous installer and metadata are recoverable under [legacy](legacy/20261012_before_dfu_1.0.0.4).

## Previous 1.0.0.3 evidence — 2026-10-11

[Previous WILDX 1.0.0.3 setup](legacy/20261012_before_dfu_1.0.0.4/WILDX_console-Setup-1.0.0.3.exe) packages the exact tested console: nonblocking V2 Stop, capture ownership through drain/completion, cancellation before capture, UID-pinned mode recovery, and classic DAT/preview support.

Paired firmware is [V2 USB1138F666 with retained loader/APP](https://github.com/zifangzhao/XE_NP/tree/V2/LATEST_FIRMWARE/V2_NP2). Firmware is unchanged; installation does not flash a board. Firmware Update provides protected WinUSB APP recovery/update, **not STM32 ROM DFU**.

Actual GUI tests passed P1/30 kHz and all three probes/20 kHz, preview, repeated Start/Stop and preparation cancellation. Every saved DAT byte matched RAW replay. Prior bounded USB mode tests are retained. This is not hours-long endurance, clean-install, fused boot/update, analog-quality or SD/BLE qualification.

The version remains 1.0.0.3 to preserve the tested executable. Identify this updated package by SHA256 `34CA66023882429E7E1C29CBEA60B66257EC504ED2DED40A85B564E8AF04D8F0`, not version alone. All 13 independently extracted payloads match the pinned binaries. Published updater helpers are unchanged; private BLE candidates are excluded.

See [previous evidence and limitations](../Note/USB_UI_RELEASE_20261011.md), [previous manifest](legacy/20261012_before_dfu_1.0.0.4/release-manifest.json) and [previous checksum](legacy/20261012_before_dfu_1.0.0.4/SHA256SUMS.txt).
