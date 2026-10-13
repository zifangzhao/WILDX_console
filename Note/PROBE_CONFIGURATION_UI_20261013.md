# Probe configuration and live filter UI — 2026-10-13

WILDX 1.0.0.15 removes the duplicate USB probe configuration action. Recording
settings opens one page with three stacked cards. Each connector has its own
IMRO/JSON import, shared configurator editor, Clear, application intent and
receipt action. Unavailable connector actions are disabled. Imports stay in a
private draft until Save; Cancel keeps the prior requests, including the V1
legacy result path.

The loaded IMRO is rendered as physical shanks with selected electrodes in
teal and other sites in gray. The probe is rotated sideways and shaft width is
expanded for visibility. The existing NpPreviewSite geometry supplies contact
coordinates, including NP1 stagger. There is no second channel-to-site routing
table. Hover identifies exact IMRO channel, physical site, shank, bank and gains.
The external editor retains detailed site editing and shared format validation.

Display HPF now uses a checkbox and an editable numeric text field. USB defaults
to 300 Hz and BLE to 100 Hz, both off. The target must be finite, positive and
strictly below half the displayed AP/LFP/BLE sample rate. Invalid text or a band
switch that makes a target invalid bypasses the filter with a visible error;
the typed target is retained. Samples owned by capture and RAW are unchanged.

## Checks on the packaged GUI

- GUI build: 0 warnings, 0 errors.
- 65 IMRO/UI checks, including all three supplied NP2013 files, imports into
  each connector, invalid imports, per-probe isolation, Save/Cancel, metadata,
  physical geometry, all three cards visible at default size and minimum-size
  controls. Includes NP2 single- and four-shank maps.
- 106 common V1/V2 workflow checks, including actual NP1/NP2 compilation,
  unavailable connectors, draft isolation, applied receipts and USB ownership.
- 3,896 actual-GUI HPF checks: transfer response, independent channel state,
  preserved input data, AP/LFP rates, invalid/manual cutoffs, band changes,
  BLE 500 Hz limit, gap handling and offscreen rendering.
- 294 standalone protocol/ownership and main-window layout checks.
- All 13 NSIS runtime files independently extracted and byte-compared. The 12
  non-GUI runtime files match 1.0.0.14. No helper or firmware rebuild.
- Recording/sensor implementations and protected existing build outputs are
  unchanged. The 1.0.0.14 live-DAT evidence remains scoped to that release.

Installer: `WILDX_console-Setup-1.0.0.15.exe`

SHA256: `F1AB3C4811F402C1862C34E282B3BE5EBE4AB7921BCE0C3AEAD5CB6C1F3C91C2`

GUI SHA256: `2FF2C1AF19F9DFFB79BD337AE2D6C8B700F9C9A5D843980CB110114DD02AFE49`

No USB/BLE commands, hardware test, installer execution or clean-install test
was performed. Prior live-DAT throughput and SR/IMU hardware limits remain.

Artifacts: `_artifacts/probe_cards_20261013`; published screenshots in
`docs/screenshots/probe-configuration.png` and `docs/screenshots/live-hpf.png`.
