# Download and calibration estimates — WILD-X 1.0.0.86

Elapsed time and approximate remaining time now cover the shared V1/V2 USB or
card-reader download, V1 fast archive transfer, compatible transfer plus decode,
saved-archive decoding, SD DAT merging, WAV creation/verification, MP4 encoding,
single-DAT calibration, and recursive batch calibration.

Each phase starts its own estimate because USB, disk merging, calibration, and
video encoding have different rates. Native download also estimates remaining
transfer/decode work across the selected recordings, weighted by native bytes.
This selected-download estimate excludes later merge and media conversion work;
those stages display their own estimates. Batch calibration uses total DAT bytes.

The estimator waits for nonzero progress and at least three seconds of work.
It then extrapolates observed elapsed time against completed/remaining work.
Estimates can change as throughput or processing cost changes. Unknown-length
interrupted-prefix recovery displays time unknown rather than inventing a size.
Metadata/flush/close stages show Finalizing. Progress stays below 100% until the
corresponding operation returns successfully. Elapsed time remains on completion.

Single-DAT calibration observes its existing partial output at bounded intervals
through the existing cancellation callback; the SDK API and correction math are
unchanged. Merge progress includes both merged rows and preserved unpaired tails.
The download status grows to fit wrapped ETA/path text at narrower window widths.

Validation on the exact 1.0.0.86 GUI:

- 48 actual-app ETA, single/batch calibration, cancellation, V1/V2 merge, WAV
  creation/verification, two-frame MP4 conversion, and rendered status checks.
- 14 existing download ETA formatter regressions.
- 174 calibration library/probe selection checks, including all 27 probes and
  unchanged hashes for the 39 factory CSVs in the supplied shared directory.
- 15,060 batch calibration, 139 probe metadata/native SD/layout, and 22 metadata
  persistence regressions. Total: 15,457 checks.
- Synthetic files only. Local FFmpeg selected `hevc_qsv` for the two-frame video.
  No logger, radio, card, firmware, or user recording was accessed for these
  operation tests; the factory directory was read-only.

GUI SHA256: `D1B0F7C77A41E2FEFF15E341D9E07A06725CC8932B4072FEE60C476CC9D61E4A`.
SDK SHA256: `86EF0F858AE6CDB188C9C525ED50894EA204D6DB15C1C38BC1EB472CDBC7F546`.
All other 39 payload files, including SDK, firmware and helpers, are unchanged
from 1.0.0.84. Decoder/camera tone and USB read/retry behavior are unchanged.
The exact released 84 inputs were reused with only these GUI progress changes;
unrelated working-tree changes remain excluded. The installer was extracted and
hash-verified without execution. No new device qualification is claimed.
