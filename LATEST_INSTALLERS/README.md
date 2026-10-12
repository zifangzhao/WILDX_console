# Current installer — WILDX 1.0.0.6, 2026-10-12

[Install WILDX 1.0.0.6](WILDX_console-Setup-1.0.0.6.exe), paired with USB firmware **A33B9912**. Two probes at 30 kHz now use existing lossless packed6 (36 MB/s instead of 48 MB/s). Three probes at 30 kHz exceed this USB representation's bandwidth limit and are rejected before setup with an explanation; choose 20 or 15 kHz. No silent downsampling.

NP2013 USB preview retains each connector's device-confirmed IMRO channel map. Load the map in Recording settings → NP2 IMRO / channel maps, enable Apply before next V2 capture, and Keep request. Loaded-only maps remain explicitly unverified; DAT hardware-channel order is unchanged.

The normal **BLE Mode** page retains the existing **ROM DFU** and **Firmware loader** controls from 1.0.0.4. The original stopped/fault/pending-command checks and BLE opcode 5/destination 3 are unchanged.

From recovery: choose **Firmware Update → SD / BLE**, then connect in **BLE Mode → ROM DFU**. This is not a new resident-USB-to-ROM command. The protected recovery loader still accepts APP-only `.xenp` updates, not fused HEX.

One incremental Release build; 29 focused rate-admission/old-codec checks passed. Exact GUI6/USB A33B9912 pair passed headless dual30 and triple20 captures, each 8192 blocks/10.487 seconds, zero detected drops/errors and every DAT byte matched RAW replay. New USB/old GUI5 also passed dual30 for 41.944 seconds, 32768 blocks. All 13 staged payload hashes checked; 12 dependencies/updater unchanged. Installer was not executed or independently extracted this cycle. No long endurance, custom IMRO/interactive preview, analog/LFP, SD/BLE APP or full fused-update qualification. The other board's photographed `source=8` failure remains a separate issue.

See [current evidence](../Note/NP2_USB_RATE_FIX_20261012.md), [retained mapping evidence](../Note/NP2_PREVIEW_MAPPING_20261012.md), [DFU evidence](../Note/DFU_VISIBILITY_20261012.md), [manifest](release-manifest.json) and [checksum](SHA256SUMS.txt). Previous GUI5 remains recoverable under [legacy](legacy/20261012_before_usb_rate_fix_1.0.0.6). These are checked local deliverables; this fix has not yet been committed/pushed.

## Previous 1.0.0.3 evidence — 2026-10-11

[Previous WILDX 1.0.0.3 setup](legacy/20261012_before_dfu_1.0.0.4/WILDX_console-Setup-1.0.0.3.exe) packages the exact tested console: nonblocking V2 Stop, capture ownership through drain/completion, cancellation before capture, UID-pinned mode recovery, and classic DAT/preview support.

Paired firmware is [V2 USB1138F666 with retained loader/APP](https://github.com/zifangzhao/XE_NP/tree/V2/LATEST_FIRMWARE/V2_NP2). Firmware is unchanged; installation does not flash a board. Firmware Update provides protected WinUSB APP recovery/update, **not STM32 ROM DFU**.

Actual GUI tests passed P1/30 kHz and all three probes/20 kHz, preview, repeated Start/Stop and preparation cancellation. Every saved DAT byte matched RAW replay. Prior bounded USB mode tests are retained. This is not hours-long endurance, clean-install, fused boot/update, analog-quality or SD/BLE qualification.

The version remains 1.0.0.3 to preserve the tested executable. Identify this updated package by SHA256 `34CA66023882429E7E1C29CBEA60B66257EC504ED2DED40A85B564E8AF04D8F0`, not version alone. All 13 independently extracted payloads match the pinned binaries. Published updater helpers are unchanged; private BLE candidates are excluded.

See [previous evidence and limitations](../Note/USB_UI_RELEASE_20261011.md), [previous manifest](legacy/20261012_before_dfu_1.0.0.4/release-manifest.json) and [previous checksum](legacy/20261012_before_dfu_1.0.0.4/SHA256SUMS.txt).
