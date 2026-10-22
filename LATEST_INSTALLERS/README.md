# WILD-X 1.0.0.84

[Installer](WILDX_console-Setup-1.0.0.84.exe).

Calibration lists factory probes with V1/NP1 and V2/NP2 filters. When a recording
has no saved SN, select its probe from the matching factory files. AP/LFP files
in that recording share the choice; other recordings prompt independently.
Known SNs still match automatically. Skip leaves a recording unresolved.

Recorded gains or an IMRO supply NP1 gain settings. Missing-SN choices are
marked as user selections in calibration receipts; original recording metadata
is preserved. Existing merged/unmerged, copy/overwrite, and validation behavior
remains available. Older firmware remains supported; no upgrade is needed.

15,395 software checks passed. All 27 probes in the shared calibration folder
loaded and all 39 CSV hashes remained unchanged. SDK, firmware, helpers, and
other payload files are unchanged from 82. No user DAT was processed and no
hardware tests were performed. The installer was extracted and hash-verified.

[Exact manifest](release-manifest.json) / [workflow and validation](../Note/PROBE_CALIBRATION_PICKER_20261022.md)
/ [exact compiler inputs](../ReleaseSources/1.0.0.84/README.md).
Previous 82 is retained under legacy/20261022_before_1.0.0.84.
