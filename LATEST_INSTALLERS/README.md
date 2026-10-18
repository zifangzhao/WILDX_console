# WILDX 1.0.0.43

[Download the installer](WILDX_console-Setup-1.0.0.43.exe).

Fixed unnecessary automatic firmware prompts. USB and BLE now prompt only
when a verified installed build matches a known older bundled predecessor.
Current, missing, partially reported, unknown or private builds stay silent.
Manual firmware management remains available.

The previous release's false prompt was reproduced by the new regression.
70 notice checks and 590 actual-app firmware identity/bundle/UI checks passed,
plus verification of all 40 extracted payload files. Runtime helpers,
exporter and firmware are unchanged. No hardware or firmware writes occurred.

See [release details](../Note/RELEASE_1.0.0.43_20261018.md),
[manifest](release-manifest.json), and [checksums](SHA256SUMS.txt).

Setup includes **Run WILD-X**. This root contains one final installer; the
previous release and original metadata are
[archived here](legacy/20261018_before_1.0.0.43/README.md).
