# WILDX 1.0.0.14 - live combined DAT

[Download the installer](WILDX_console-Setup-1.0.0.14.exe).

With **Merge active probes into one DAT per signal band** enabled, V2 writes
`neuropixels_ap.dat` during recording. Each sample contains probe1 channels
1..384, then probe2 channels 1..384, then probe3 channels 1..384; only active
physical probes are included. There is no post-recording merge or duplicate
full-session probe DAT folders. V1 retains its live AP/LFP output.

RAW and all probe/configuration/calibration metadata stay alongside data.
Small unmatched tails retain their source ticks. Each recovery segment owns
its own combined DAT; native phase offsets are recorded without correction.
SD downloads still export per-probe DAT.

The actual app passed 16,943,326 live DAT checks, including file growth before
Stop and exact sample/probe/channel bytes, plus calibration, recovery, UI and
sensor regressions. All 13 runtime files were independently extracted and
verified. Twelve non-GUI files match 1.0.0.13. No firmware or device operation
was performed; live hardware throughput and prior SR/IMU limits remain pending.

[Validation](../Note/LIVE_COMBINED_DAT_20261013.md),
[manifest](release-manifest.json) and [checksums](SHA256SUMS.txt).
