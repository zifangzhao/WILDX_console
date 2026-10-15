# Compact USB page — 1.0.0.26

The blank area above GPIO came from an 88-pixel action slot and a 30-pixel recovery row, regardless of visible content. Action height now follows the actual wrapped buttons, and a hidden recovery control takes zero height. Device, output folder, session and GPIO labels share an inline column. Compact USB cards use 10-pixel padding; the brand/tab area is shorter. Other workspaces keep their existing card padding.

The normal recording page fits without page scrolling at client sizes 1044×780, 1180×820, 1180×920 and 1450×920. The GPIO table uses a 24-pixel header and 28-pixel rows while retaining four complete default rows. Its three status lines fit. Device-health text, all recording controls, the complete IMU including its footer, recording metrics/path and local-time panel remain visible. The measured hidden-recovery gap is 15 pixels. Long diagnostics can still grow and scroll rather than being clipped.

Software checks passed: 42 compact recording-page assertions and an actual UI render with sample values; 42 GPIO protocol/clock checks with simulated serial; 37 actual GPIO panel/warning checks; 16 recording-metrics checks; 106 shared-workflow checks; 37 actual local-time UI/default checks. The compact fixture includes visible recovery, a typical recording path, window resizing, hidden-row collapse and long warning text. [Render](../docs/screenshots/usb-recording-gpio.png).

The isolated GUI starts from `494785dc013b7fa0638f326550c418a4c775dc0b` with only mainForm.WildX.cs, mainForm.GpioRtc.cs and version overlays. Build: zero warnings/errors. All twelve non-GUI runtime files remain identical to 1.0.0.25; all thirteen installer runtime files were independently extracted and hash-verified. Pending BLE/media/sensor work is excluded. No discovery, COM/USB/BLE recording hardware, public NTP traffic, firmware action, real recording or installer execution occurred. Existing hardware qualification limits remain.

Private artifacts: `_artifacts/compact_usb_20261014`, including RenderPreview.cs, source-inputs.json, build.log and package verification. [Manifest](../LATEST_INSTALLERS/release-manifest.json).

- GUI SHA-256: `5F8112F03FB763FB7D484106FB5E471FE6950D21122E36D4A8E4DAF61CDC4F26`
- Installer SHA-256: `5075C8C9E92EB2A85EFD79A057ADAE58596F277D7AA1402B93F64F35105D8E4E` (2,336,701 bytes).
