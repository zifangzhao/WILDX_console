# Neural DAT-only output — 1.0.0.73

New V1 SD neural exports write decoded AP/LFP DAT directly without creating
`ephys.bin` or per-probe `.lane8` files. AP/LFP sample values and ordering,
sensor/analog files, timestamps, descriptors, footer, calibration, recording
metadata and saved probe configuration are retained. Optional merged DAT output
still uses the existing per-sample ascending physical-probe layout.

The raw-only NP1 fast-archive entry is removed from the current Data Download
page. V1/V2 live capture already uses DAT-only output; V2 SD export already avoids
neural raw copies. Camera raw files and all existing recordings are unchanged,
as explicitly requested by the user.

Power-cut prefix recovery now tracks per-probe native byte offsets in memory,
including save/restore at whole-chunk boundaries. The raw files are not written
temporarily or deleted after export. Failed/cancelled exports retain their
decoded partial data and accurate incomplete metadata. Original card contents
are never changed by this export policy.

V1 neural free-space estimation is reduced from four to two source-chunk sizes
per committed chunk, keeping a conservative decoded/auxiliary allowance. Optional
merge still reserves additional space for both decoded layouts. Media and V2
estimates are unchanged. Unknown-length explicit recovery still reports unknown
size and requires the user to ensure sufficient space.

Build73 uses the99 recorded GUI compiler/build inputs from published72, changing
only NpCe64Export.cs, NpDownloadForm.cs and the normal assembly revision. Camera
code, live capture paths, SDK F485AFE1…, firmware and helpers are unchanged.
Private V1 SD best-effort decoder work remains outside this release composition.

Validation:963 actual-app checks compare one/two/three-probe completed and
incomplete committed exports byte-for-byte with72, plus recovered prefixes,
torn frame seams, strict decoder failures and cancellation. Every progress
callback also checks that no neural raw file exists. Sensor/analog and binary
metadata bytes match; recording JSON/status retains its existing completeness
and loss semantics plus explicit false raw/lane8 flags. Another218 sparse-card
prefix recovery checks pass. No physical device, card, radio, installed UI or
new firmware/hardware qualification was performed.

Runnable check: `test_neural_dat_only.ps1 -ConsoleExecutable <GUI73.exe>
-BaselineExecutable <GUI72.exe> -OutputDirectory <fresh folder>`.
Local evidence: `_artifacts/neural_dat_only_20261022/checks-r4.log`.

GUI SHA256: `B1D37ACA5DA14C42B6748C92095CBCC755072DC9F87B032A485298FD312A43CD`.
