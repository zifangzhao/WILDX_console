# WILDX 1.0.0.42

[Download the installer](WILDX_console-Setup-1.0.0.42.exe).

SD downloads now use the recording's stored start time for folder names, even
without a session label: `wild_yyyyMMdd_HHmmss.fff`. A label adds `<label>_`.
This covers V1/V2 neural, camera/media and NP1 fast archive downloads.
Dates use the PC's local time zone. Missing or invalid clocks produce
`time_unknown` with session/LBA identity; download time is never substituted.
Repeated names receive numeric suffixes, preserving existing files.

No firmware update is required. The previous preview/download fixes are included.
62 naming/time/UI and 139 recovery checks passed, plus GUI compilation and
verification of all 40 extracted payload files. Runtime helpers, exporter and
firmware are unchanged. No hardware download was performed.

See [release details](../Note/RELEASE_1.0.0.42_20261017.md),
[manifest](release-manifest.json), and [checksums](SHA256SUMS.txt).

Setup includes **Run WILD-X**. This root contains one final installer; the
previous release and original metadata are
[archived here](legacy/20261017_before_1.0.0.42/README.md).
