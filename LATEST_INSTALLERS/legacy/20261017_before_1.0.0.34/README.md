# WILDX 1.0.0.33

[Download the current installer](WILDX_console-Setup-1.0.0.33-V2_SD_META13_20261016.exe).

This folder contains one final published installer, its [release manifest](release-manifest.json), and [checksums](SHA256SUMS.txt). Before publishing a replacement, move the previous installer and its release metadata into `legacy/`. Keep development builds and alternate packages outside this root.

The current package was published on 2026-10-16. It includes USB quality testing, the split capture activity panel, and strict V2 classic v13 / existing v12 SD export support. All 13 extracted runtime files matched the checked payload. The installer retains **Run WILD-X**, uses the existing installation directory, and does not flash firmware. See [validation and qualification limits](../Note/V2_SD_META13_COMPATIBILITY_20261016.md).

Older packages are preserved in [legacy](legacy/); the two superseded 1.0.0.33 packages and their manifests are [archived here](legacy/20261017_before_single_final_release/README.md). Newer source changes, including GPIO continuity, have not yet been packaged in this published installer.
