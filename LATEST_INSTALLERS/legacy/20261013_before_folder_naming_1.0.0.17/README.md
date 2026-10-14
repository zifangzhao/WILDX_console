# WILDX 1.0.0.16

[Download the installer](WILDX_console-Setup-1.0.0.16.exe).

New V2 USB app recordings save decoded AP DAT without the extra
`neuropixels_np2usb.raw` copy. V1 already saves AP/LFP DAT without a neural RAW
copy. Metadata, probe maps, factory calibration files, timing and error reports
remain alongside data. Existing recordings are untouched.

The combined DAT is written live, with each sample containing all 384 channels
from each active probe in physical connector order. Recovery segments remain
separate. Stream verification is retained. DAT-only decoding/writing or wire
verification failures stop capture and mark it incomplete, including when
incoming USB data pauses.

The stacked physical probe maps and editable display HPF from 1.0.0.15 remain.
SD downloads and optional camera saving keep their existing formats.

Software checks: 20,511,105 live DAT assertions, 74,176 V1 checks, 134 USB UI
lifecycle checks, 120 recovery checks and 5 sensor checks. All 13 installer
runtime files were independently extracted and verified; 12 non-GUI runtime
files match 1.0.0.15. No hardware or installer execution was performed.

[Validation](../Note/DAT_ONLY_USB_20261013.md), [manifest](release-manifest.json)
and [checksums](SHA256SUMS.txt).
