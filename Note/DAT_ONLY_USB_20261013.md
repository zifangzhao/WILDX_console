# DAT-only USB recording — 2026-10-13

WILDX 1.0.0.16 explicitly disables the optional V2 RAW writer for new app
recordings. No RAW file, queue or disk thread is created. The native verifier,
parallel probe decoders, live combined DAT ordering, correction, unmatched tail
rows/source ticks, recovery segments and metadata remain. V1 already uses
AP/LFP DAT without a second neural RAW file; its decoder is unchanged.

The reusable recorder retains an explicit RAW-enabled path for diagnostic
callers and software reference tests. It is not enabled by the WILDX USB UI.
SD archive download, offline input files and camera recording are unchanged.
Existing recordings are not modified.

With no fallback RAW file, decode/write errors propagate to the reader's
stop/drain path. The reader polls asynchronous failures even when USB data
pauses. Unverified blocks cannot silently enter a RAW-only fallback. Final
status and merge reports record `raw_saved=False`; recording settings include
`raw_usb_saved=false`. Incomplete output retains its failure receipt.

## Verification

- 20,511,105 actual-app live DAT assertions: all five supported fixture profiles,
  pre-Stop growth, every decoded sample compared with RAW-enabled reference
  output, exact probe-block order, correction/preview isolation, metadata/XML,
  tail ticks, no RAW file/queue, failure propagation/polling and bounded drain.
- 74,176 existing V1 paired-stream checks; inspected fixture outputs contain no
  extra neural RAW files.
- 134 USB UI Stop/preparing/completion ownership checks; 120 focused recovery
  checks; 5 sensor lifecycle checks.
- Final GUI build: 0 warnings/errors. The first build had one recovered output
  copy retry; the isolated wire fixture retains its existing unused-field warning.
- 13 NSIS runtime files independently extracted and hash-verified; all 12
  non-GUI files match 1.0.0.15. No helper or firmware build.

Installer SHA256: `917ED1EFB832D5FCFFCB8A48B0C985F563AB54F346DBDCAA0B2B1452B908B571`

GUI SHA256: `751F2E53E9D9EC0D47E1FE2AAAA3C3CB671392BE3C5C70B10AA0F155B0106966`

Evidence: `_artifacts/dat_only_20261013`. No device or installer execution;
hardware throughput/endurance and prior SR/IMU qualification limits remain.
