# WILDX 1.0.0.48 — V1 download/export

[Download installer](WILDX_console-Setup-1.0.0.48-V1-download-export.exe) — **1.0.0.48**.

V1's PC-side SD export decoder is faster, with the same framing, continuity
and corruption checks. Existing file-only validation passed 3,101 cases and
retained 21 byte-identical output files. Saved-record full export was 2.78x
faster; this is **not a verified fix for the live 11 MB/s download rate**.

The exact checked 48 executable is packaged without rebuilding. All other
47 runtime/firmware files match release 47, including V1 HS/SD/BLE component
and fused images, V1 camera, V2 and the checked exporter DLL. No firmware
update is required for this PC decoder change. Existing USB catalog-repair
support and firmware requirements are unchanged.

Finish → Run uses Windows shell launch so the app's administrator prompt can
appear. Three isolated callback/launch cases passed; real WILD-X/UAC was not
run during packaging. All 48 extracted payload files match the frozen inputs.

No new device, streaming, SD-recording, endurance or installed-pair qualification.
New unbuilt timing diagnostics are excluded. [Scope and limits](../Note/RELEASE_1.0.0.48_V1_DOWNLOAD_EXPORT_20261019.md) · [Manifest](release-manifest.json) · [Previous installer](legacy/20261019_before_download_export_48/README.md).
