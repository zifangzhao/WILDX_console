# WILDX 1.0.0.24

[Download the installer](WILDX_console-Setup-1.0.0.24.exe).

BLE Control now uses aligned device/address and recording-profile/storage-bound fields, with labels above each field. Connection actions have their own row. Buttons share a height and spacing, action rows wrap at narrow widths, long status messages wrap within the panel, and empty status details take no space.

Actual UI checks passed across standalone and embedded window widths, enlarged layout, long messages and the Cancel connection label. The existing 106 shared workflow checks passed. All 13 extracted runtime files match the checked payload; all 12 non-GUI files are unchanged from 1.0.0.23. No hardware or installer execution; existing V1 firmware support is unchanged.

[UI render with sample values](../docs/screenshots/ble-mode.png), [validation](../Note/BLE_LAYOUT_ALIGNMENT_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
