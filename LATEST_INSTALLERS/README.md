# WILD-X1.0.0.82

[Installer](WILDX_console-Setup-1.0.0.82.exe).

Calibration now searches one remembered factory folder (including subfolders),
indexes CSVs by their internal probe serial, and finds matching NP1 ADC/gain or
NP2 gain files automatically. The main page shows SN/IMRO matching; manual CSV
and gain fields remain under Manual setup. Current recording settings or saved
recording metadata supply the IMRO/gain context.

Calibrate folders resolves each recording separately by saved SN and channel
settings/IMRO, including merged/per-probe AP/LFP and more than three serials in
a recursive tree. It retains matched IMROs and hashes with factory snapshots.
Existing copy/overwrite and double-correction protections remain. Older files
without SN need one explicit assignment; missing or conflicting inputs are
reported rather than guessed.

All27 probes in the supplied factory directory loaded (12 NP1 pairs,15 NP2
files;39 CSVs); their file hashes were unchanged.15,366 software checks passed,
including real-file lookup, recursive synthetic correction, serial/gain/order,
overwrite, metadata persistence, SD sample-byte comparisons and rendered UI.
No user recordings were calibrated. SDK, firmware and helpers are unchanged.
The installer was extracted/hash-verified, not executed; no new hardware tests.

[Exact manifest](release-manifest.json) / [workflow and limits](../Note/CALIBRATION_LIBRARY_20261022.md)
/ [exact compiler inputs](../ReleaseSources/1.0.0.82/README.md).
Previous78 is retained under legacy/20261022_before_1.0.0.82.
