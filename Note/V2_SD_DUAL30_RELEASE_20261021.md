# V2 dual30 recording-first release — 2026-10-21

Exact pair: APP660A6DA4, USBE323A06A, recovery6408DBB8, GUI1.0.0.70
`DD7A948B343C90E094C7347A17A690B645FC776E33EFB887416C60AA7C4AA12A`, SDK
`BF32A4602C775E78AFE00F09856908116EEE9A6EEF41E144013EC3EC5000048E`.

Device004B / MCU001A0004434250062039314E; user-confirmed Delkin V90,501301248
sectors. Two real NP2 probes at30kHz:600003ms capture,19ms drain,468752 produced/
committed/native-readback blocks, all gap/drop/missing/overflow/discard counters0.
CPU192/SD108MHz,1.8V, classic format12, ringpeak6/9, no PSRAM spills/restores.
36.4MB/s recording payload;49.0MB/s effective write-service, not standalone peak.

Independent full21871550464-byte snapshot passed transport CRC/sequence/epoch.
Both DATs have18000076 complete384-channel rows /13824058368bytes, one contiguous
segment, zero loss. Exact BF32 Result matches native counts/phases/ticks.
Actual console70 reader plus `NpCe64Export.ExportCalibrated` additionally exported
the saved10s/7816-block session:300134 rows/probe, native/Result/context/warning
checks pass. No new recording was needed for this pairing check. E323 lacks
optional guarded read-recovery capability0x6B; ordinary read-only download passed.

GUI70 is one incremental compile from checked69, only the two held V2 result/
warning/reporting deltas plus exact BF32; merge/IMRO/UI/V1 recording code retained.
54 reporting /118 UI /551 V1 runtime checks reused, not repeated. Source-delta
inverse comparison preserves69 outside those seams. WinPS receipt serialization
was repaired receipt-only; no recompile. Exact99-source archive is in
`ReleaseSources/1.0.0.70`.

Components were not rebuilt/resealed during packaging. Fused HEX excludes
configuration, checks ownership/manifests/vectors/flash/stack bounds and pads
unused firmware space erased. Fused SHA256:
`0BFECE90D3D0B098C645DB5370052C806EC6FB21FE0F1947F1718DFCE61EEFB1`.
Installer SHA256:
`383E6D2D48DE78830E2C6F706E2A77FB8D9048A119922FFFDCC9B2E7AA0FD1C9`.
One NSIS pass;40 extracted runtime files match, configurator PDB intentionally
not distributed. Actual firmware catalog and V2 updater APP parser passed.
An initially wrong V1-only image-parser harness was corrected, not a bad V2 image.

Limits: no ADC payload CRC/analog quality, arbitrary-bit-error, all11 modes/cards,
BLE-control, installed visible UI, newly-flashed-fused or unlimited-endurance
claim. No power optimization this release. V1/V1Camera packages and catalog
entries retained unchanged. Main permanent hardware log:
`XE_NP/V2: Note/V2_RECORDING_FIRST_RESTORE_20261021.md`.

Publication independently confirmed: WILDX/main
`74d3daeae26ad72e46cf554861f0cfd966095d92`; paired XE_NP/V2
`6f875d7aa157f25b8ff3caefef8edcc50357e79c`. Receipts record the immutable
pre-publication assembly; these commits contain the exact checked artifacts.
No later tests/rebuilds/HW changes for this documentation update. Next power
work is a separate1probe7.5k baseline/integrity gate before any reduction.
