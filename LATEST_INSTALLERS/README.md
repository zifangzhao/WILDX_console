# Current engineering installer — 2026-10-10

[WILDX 1.0.0.1 setup](WILDX_console-Setup-1.0.0.1.exe) adds NP2 IMRO/JSON loading and channel-map editing in USB Recording settings and BLE Mode. It contains no firmware and does not update a logger during installation.

All 13 extracted payloads match the checked binaries. The actual Release dialog passed 98 targeted checks, including 17 NP2 and two NP1 examples. Four UHD/type-1110 examples are not standard NP1/NP2 and remain unsupported. See [release evidence and limits](../Note/NP2_IMRO_RELEASE_20261010.md), [checksums](SHA256SUMS.txt) and [the firmware/runtime manifest](release-manifest.json).

NP2 imports are **host requests only**, not settings applied by current V2 firmware. USB recordings save them as requested/unverified metadata; BLE imports do not modify SD recordings. Self-test actions require newer resident firmware; DFB8F25E does not implement them. Full hardware/lossless qualification remains open. The previous release is recoverable in [legacy](legacy/1.0.0.0_20261010/release-manifest.json).
