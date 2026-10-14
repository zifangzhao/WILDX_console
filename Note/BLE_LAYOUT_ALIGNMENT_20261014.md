# BLE layout alignment — 1.0.0.24

The BLE connection row mixed top-aligned native fields and labels with taller buttons. Fixed field widths also caused arbitrary wrapping. Device/address and recording-profile/storage-bound fields now share a two-column native TableLayoutPanel layout with labels above the inputs. Connection actions sit below the fields. Buttons use a consistent 36-pixel minimum height at 96 DPI and common spacing. Narrow action rows wrap. Native inputs retain their natural text height and share a vertical center.

Status, clock and configuration messages stack and wrap at the available width. Empty optional status rows collapse; new details become visible when their text arrives. The form uses DPI scaling. BLE connection, cancellation, recording, firmware and probe behavior are unchanged. The shared theme and GPIO code are unchanged.

Visual checks passed: 1,042 actual-control alignment/containment assertions across standalone widths 820/910/1200 and embedded main-window widths 1044/1180/1450, additional 150% layout scaling, the longer Cancel connection label, long status messages and optional status visibility. [The rendered GUI](../docs/screenshots/ble-mode.png) contains illustrative values; it does not show a hardware session. The existing 106 universal workflow checks passed against the final GUI.

The isolated GUI starts from `7ab8800af089960610f0fa2af9e1d24b1e029fc0`, with only NpBleForm.cs and version overlays. Build: zero warnings/errors. All twelve non-GUI runtime files remain identical to 1.0.0.23; all thirteen installer runtime files were independently extracted and hash-verified. Pending BLE/media/sensor work is excluded. No discovery, COM/USB/BLE commands, firmware operations, recording or installer execution occurred. Existing hardware qualification limits remain.

Private build/render inputs and checks: `_artifacts/ble_layout_20261014`. Source and payload hashes are in [the manifest](../LATEST_INSTALLERS/release-manifest.json).

- GUI SHA-256: `90087AA9212158DD498239E75D3DE03E42716EFB5DE0F91F3BC767E00A373454`
- Installer SHA-256: `73D1B7D18E445EFBBDD602DB46CB06A4C1BC5E1D1BE69537DCCA153D2D121F2D` (2,334,076 bytes).
