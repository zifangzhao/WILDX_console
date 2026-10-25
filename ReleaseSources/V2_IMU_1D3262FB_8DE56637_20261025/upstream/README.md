# V2 IMU startup release — 2026-10-25

Recovery6408DBB8 / USB1D3262FB / BLE-SD APP8DE56637.

[Fused HEX](V2_fused_RECOVERY_6408DBB8_USB_1D3262FB_APP_8DE56637_IMU_STARTUP_20261025.hex) combines the exact checked components, excluding retained
configuration. Component HEX and USB-update `.xenp` files are alongside it.
All HEX files use LF. The installed board was checked as components; the fused
file was assembled/checked without a separate whole-image flash test.

Use with [WILD-X138](../../LATEST_INSTALLERS/README.md). That installer is the
unchanged published installer: it does **not** embed this new V2 firmware.
Select this release's matching local firmware files when updating; do not
accept an older bundled firmware offer. Console sources remain in WILDX_console.

Checked: final USB three probes20kHz and packed one probe30kHz after restart,
fresh nine-axis IMU; BLE three probes20kHz with coherent IMU views, Start/Stop,
catalog append/reconnect, full SD-to-DAT export and independent integrity checks.
These are short selected-mode checks, not all-card/all-mode/endurance qualification.
First-sample PC timing is unavailable in this legacyUTC1 APP. One USB-return
incident required a data-cable reconnect; the subsequent final-build return passed.

[Evidence](../../Note/V2_USB_TIMING_SIDEBAND_20261024.md),
[exact scope and hashes](package-receipt.json), [source provenance](source-manifest.json).
