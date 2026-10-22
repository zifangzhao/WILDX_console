# WILD-X 1.0.0.71

[Installer](WILDX_console-Setup-1.0.0.71.exe).

SD download computes DAT checksums while writing, avoiding the long reread after
the last USB block. Progress stays below 100% until finalization completes.
Download ETA, merged DAT export and recording configuration files are retained.

111 software checks passed, including saved native recordings, independent DAT
checksums, cancellation and the actual application export bridge. No new hardware
or installed-UI qualification was performed for this software change.

Firmware and helpers are byte-identical to version 70: V2 APP 660A6DA4,
USB E323A06A, recovery 6408DBB8. Prior hardware evidence and its limits remain in
the [version 70 release note](../Note/V2_SD_DUAL30_RELEASE_20261021.md).

[Exact manifest](release-manifest.json) / [fix and validation](../Note/SD_DOWNLOAD_FINALIZATION_20261021.md)
/ [compiled source inputs](../ReleaseSources/1.0.0.71/README.md).
The previous installer is retained under `legacy/20261021_before_1.0.0.71`.
