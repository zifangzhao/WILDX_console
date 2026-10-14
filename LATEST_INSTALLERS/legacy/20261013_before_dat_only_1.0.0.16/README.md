# WILDX 1.0.0.15

[Download the installer](WILDX_console-Setup-1.0.0.15.exe).

Recording settings contains the probe configuration page. All three probes
have stacked cards with direct IMRO/JSON loading, editing, application intent
and device receipts. Loaded maps show physical shanks and selected electrode
sites. Hover identifies the site and acquisition channel. The duplicate USB
probe configuration button has been removed.

USB and BLE live previews use a Display HPF checkbox and editable frequency.
USB starts with 300 Hz and BLE with 100 Hz, both disabled. Cutoffs must be
positive and below half the display sample rate. An invalid entry bypasses
filtering and shows an error. Recording data is unchanged.

The live combined DAT output from 1.0.0.14 is retained: each sample contains
all 384 channels from each active probe, in physical connector order. RAW,
probe/configuration/calibration files and timing/error metadata remain together.
SD downloads still export per-probe DAT.

Validation: 65 probe configuration checks, 3,896 display-filter checks,
106 shared-workflow checks and 294 standalone checks. All 13 packaged runtime
files were independently extracted and verified; 12 non-GUI files match
1.0.0.14. No hardware access or installer execution was performed.

[Validation](../Note/PROBE_CONFIGURATION_UI_20261013.md),
[manifest](release-manifest.json) and [checksums](SHA256SUMS.txt).
