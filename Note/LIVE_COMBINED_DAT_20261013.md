# Live combined DAT - WILDX 1.0.0.14

V2 previously wrote per-probe DAT files during capture, then merged them after
Stop. With **Merge active probes into one DAT per signal band** enabled, V2 now
writes the root `neuropixels_ap.dat` during capture. Stop drains the existing
writer; it does not run a second copy/merge operation. Per-probe mode remains
available by clearing the checkbox. V1 retains its existing live AP/LFP writers.

Each signed little-endian int16 sample row has this exact order:

```text
[S0 probe1 channels 1..384][S0 probe2 channels 1..384][S0 probe3 channels 1..384]
[S1 probe1 channels 1..384][S1 probe2 channels 1..384][S1 probe3 channels 1..384]
...
```

Only active physical probes are included, in ascending connector order. Two
probes give 768 channels; three give 1152. Each probe contributes its own first
complete SuperSync row as S0. Native phase offsets are reported, not corrected;
this layout does not establish simultaneous ADC sampling.

The parallel per-probe decoders and original RAW writer are retained. Bounded
row rings feed one existing asynchronous disk writer. All probe/channel blocks
are copied before the decoder reuses its row buffer. Row rings replace part
of the former per-probe write-buffer allocation. Decoded-output failures release
waiting workers and retain RAW draining; failures and missing decoded blocks
remain explicit. Preview failure is isolated from recording.

The live DAT uses normal batched writes, so it becomes readable and grows as
write chunks are published; it is not flushed per ADC sample. Calibration is
applied once per probe before combining rows. Probe JSON/IMRO/configuration,
calibration and recording metadata remain at the recording root. No duplicate
full-session `probeN` DAT folders are created in combined mode. A clean Stop
may leave unmatched complete rows; those small tails and uint64 little-endian
source ticks are preserved in `unpaired_tail`. There is no zero padding.
Recovery creates a separate combined DAT inside each `segment_XXXX`, preserving
unknown gaps rather than joining segments into a continuous timeline.

## Checks and package

The actual app passed:

- 16,943,326 live DAT assertions: file creation/growth before Stop, readable live
  output, exact sample/probe/channel order against the per-probe decoder,
  untouched RAW, correction, preview isolation, tails/source ticks, root
  metadata/XML and bounded queue abort/drain behavior.
- 716,128 existing factory ADC/gain assertions.
- 10,501,835 existing file-only merge/recovery assertions. The fixture now
  preserves reflected exception types and no longer expects the removed
  post-session merge controller.
- 134 USB UI lifecycle and 294 standalone assertions.
- 5 retained sensor lifecycle checks. The already-published SR/IMU source
  baseline is included in this source release; its functionality is retained.

The final cached GUI component build had zero warnings/errors. Helpers, the
SD/calibration companion and firmware were not rebuilt. The isolated fixture
compilation retains its existing unused `NpDownloadStream.Cancelled` warning.
All 13 installer runtime files were independently extracted and verified;
twelve non-GUI runtime files match the previous installer.

Installer `WILDX_console-Setup-1.0.0.14.exe`: 2,304,976 bytes, SHA-256
`318F7CF301D7E2993CCF44DB5B78E979DB2A95F48BE71D3A233AD91B1518F8FD`.
GUI SHA-256:
`4DB86EB2C1DC9BDD61820E56011B28086284FBDE323D807C58330490859D1CE8`.
Private evidence: `_artifacts/np2_live_dat_20261013`.

This live path was tested with synthetic verified wire blocks and real file
writes. No device was opened or firmware flashed in this work. Sustained live
hardware throughput is pending qualification. Retained firmware is recovery
`6408DBB8`, USB `BFF0A8A2`, APP `E07FB882`; previous six-axis/magnetometer error 9,
damaged-shank, rate and endurance limits remain. SD downloads still export
per-probe DAT files.
