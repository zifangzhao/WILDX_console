# WILDX 1.0.0.51 — BLE preview display

[Download installer](WILDX_console-Setup-1.0.0.51-BLE-preview-20261020.exe).

Delivers the previously private fixed-position sweep and500Hz paced display of
original BLE samples. Window/gain/channel controls remain; real missing samples
remain gaps. No interpolation, extra BLE traffic or MCU recording work.

5916 actual-source software checks reused for identical preview source. One
isolated incremental build and one installer pass; all40 extracted files match.
The39 non-GUI files, including firmware, are unchanged from48. No new RF,
recording, firmware or installed-pair qualification. Subsampling can alias;
this is not a qualified LFP reconstruction.

[Evidence/limits](../Note/V2_BLE_PREVIEW_RELEASE_20261020.md) ·
[Manifest](release-manifest.json) · [Checksums](SHA256SUMS.txt).
Previous48 remains in `legacy/20261020_before_ble_preview51`.
