# WILD-X 1.0.0.86

[Installer](WILDX_console-Setup-1.0.0.86.exe).

Elapsed and estimated remaining time now cover USB/card-reader downloads,
V1 archive transfer and offline decoding, DAT merging, WAV/MP4 conversion,
and single-file or recursive batch calibration. Selected native downloads
also show a queue estimate weighted by recording size. Merge and conversion
have separate phase estimates. The status wraps to fit narrower windows.

Estimates start after progress is observed for at least three seconds.
Unknown-length interrupted-recording recovery shows time unknown. Saving
and flush stages show Finalizing; 100% follows successful completion.
Older firmware remains supported; firmware, SDK, helpers, USB behavior and
camera tone are unchanged from 84.

15,457 software checks passed, including synthetic single/batch calibration,
V1/V2 merge, WAV verification, a two-frame MP4 conversion, cancellation and UI
renders. All 39 factory CSVs for 27 probes were read-only. No logger/card or
user DAT was used for these operation tests. The installer was extracted
and hash-verified without execution; no new device qualification is claimed.

[Exact manifest](release-manifest.json) / [workflow and validation](../Note/OPERATION_TIME_ESTIMATES_20261022.md)
/ [exact compiler inputs](../ReleaseSources/1.0.0.86/README.md).
Previous 84 is retained under legacy/20261022_before_1.0.0.86.
