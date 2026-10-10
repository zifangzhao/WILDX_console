# Current engineering installer — 2026-10-10

[WILDX 1.0.0.0 setup](WILDX_console-Setup-1.0.0.0.exe) contains the latest checked V2 clock, preview, catalog and USB self-test integration. It contains no firmware and does not update a logger during installation.

All 13 extracted payloads match the checked binaries; retained V2 protocol, SD metadata, updater package and self-test report checks pass. See [release evidence and hardware limits](../Note/RELEASE_20261010.md), [checksums](SHA256SUMS.txt) and [the matched firmware manifest](release-manifest.json).

This is not full hardware or lossless-recording qualification. Self-test actions require newer resident firmware; the paired DFB8F25E USB image does not implement them. The previous unpublished installer is recoverable in [legacy](legacy/20261009_before_v2_integration/candidate-manifest.json).
