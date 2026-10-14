# WILDX 1.0.0.19

[Download the installer](WILDX_console-Setup-1.0.0.19.exe).

V1 probe boxes are directly editable and default to the active profile. Advertised 20 kHz profiles no longer show the generic experimental popup. Existing V1 firmware remains supported; no firmware upgrade is required.

Metadata saves retry brief file locks and preserve completed replacement files when persistence fails. Stop reports metadata warnings separately from capture errors, with destination paths and native errors in the recording folder/report. The completion UI no longer repeats settings/probe/XML writes. Neural data encoding and live DAT writing are unchanged.

Software checks passed, including real local file locks, legacy settings, AP/LFP decoding, metadata bundles, XML, V2 SD byte equivalence and Stop/recovery ownership. All 13 extracted runtime files match the checked payload; 11 are unchanged from 1.0.0.18. Only GUI and shared metadata companion changed. No hardware or installer execution. Yoshi's hardware retest and exact remote Windows cause remain pending.

See [validation](../Note/V1_METADATA_FIX_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt), and [workflow](../docs/universal-workflow.md).
