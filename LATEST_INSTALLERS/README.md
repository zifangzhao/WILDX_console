# Current USB/UI engineering installer — 2026-10-11

[WILDX 1.0.0.3 setup](WILDX_console-Setup-1.0.0.3.exe) packages the exact tested console: nonblocking V2 Stop, capture ownership through drain/completion, cancellation before capture, UID-pinned mode recovery, and classic DAT/preview support.

Paired firmware is [V2 USB1138F666 with retained loader/APP](https://github.com/zifangzhao/XE_NP/tree/V2/LATEST_FIRMWARE/V2_NP2). Firmware is unchanged; installation does not flash a board. Firmware Update provides protected WinUSB APP recovery/update, **not STM32 ROM DFU**.

Actual GUI tests passed P1/30 kHz and all three probes/20 kHz, preview, repeated Start/Stop and preparation cancellation. Every saved DAT byte matched RAW replay. Prior bounded USB mode tests are retained. This is not hours-long endurance, clean-install, fused boot/update, analog-quality or SD/BLE qualification.

The version remains 1.0.0.3 to preserve the tested executable. Identify this updated package by SHA256 `34CA66023882429E7E1C29CBEA60B66257EC504ED2DED40A85B564E8AF04D8F0`, not version alone. All 13 independently extracted payloads match the pinned binaries. Published updater helpers are unchanged; private BLE candidates are excluded.

See [evidence and limitations](../Note/USB_UI_RELEASE_20261011.md), [manifest](release-manifest.json) and [checksum](SHA256SUMS.txt). The superseded package is recoverable under [legacy](legacy/20261011_before_usb_ui_1.0.0.3/release-manifest.json).
