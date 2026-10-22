# WILD-X 1.0.0.76

[Installer](WILDX_console-Setup-1.0.0.76.exe).

Calibration → Calibrate folders scans a selected folder and its subfolders for
merged/per-probe AP/LFP DATs. Load the factory profiles first, review the file
preview and choose Calibrate ready files. Copy mode mirrors a separate output
tree; Overwrite originals is an explicit option using staged atomic replacement.
Original probe/IMRO/timing metadata and exact calibration files/hashes accompany
each result. Already-calibrated, ambiguous or unsupported files are reported.

15,060 actual-app synthetic checks passed, including ADC/gain correction,
recursive scan/UI, physical probe order/serial mapping, cancellation and failed
overwrite recovery. The offscreen GUI render was inspected. No user recording,
physical device or installed-application qualification was performed.
SDK, firmware, helpers and acquisition paths are unchanged from73.

[Exact manifest](release-manifest.json) / [workflow and limits](../Note/BATCH_POST_CALIBRATION_20261022.md)
/ [compiled source inputs](../ReleaseSources/1.0.0.76/README.md).
The previous installer is retained under `legacy/20261022_before_1.0.0.76`.
