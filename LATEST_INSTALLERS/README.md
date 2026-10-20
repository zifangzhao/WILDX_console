# WILDX51 — V1 SD speed-tier candidate

[Download installer](WILDX_console-Setup-1.0.0.51-V1-SD-tiers-USBD75CA6F9-candidate.exe).

User-approved early publication: SOFTWARE CHECKED; DEVICE VALIDATION PENDING.
Same WILDX1.0.0.51 runtime, refreshed V1 USB1.0.97-sd-tiers-candidate/buildD75CA6F9
and matching fused boot+USB+APP654824DE. Use the fused/USB update to change the
USB service; APP-only flashing does not apply this change. V1 NP only, not V2
or the separate camera target. Retained configuration is excluded.

USB fallback tries120/60/30/20MHz bus clocks, with independent selected-mode,
CRC, repeated-read and card-identity validation. Fixes a post-validation voltage
switch. DDR stays disabled. These clocks are not measured download speeds;
the reported9.5MB/s case is not yet proven resolved.

One USB-main build,196 policy assertions and mode-check fault injection passed.
One installer pass; all40 extracted payload files match. No new on-device
speed, streaming, recording or installed-pair qualification. Boot/APP/runtime,
V2 and separate camera firmware remain unchanged. Private GUI/recording work
is excluded. Device validation follows this explicitly approved publication.

[Firmware and validation limits](../Firmware/V1/README.md) ·
[Evidence](../Note/V1_USB_SD_READ_TIERS_20261020.md) ·
[Manifest](release-manifest.json) · [Checksums](SHA256SUMS.txt).
Previous WILDX51/USB6CD1917C is in `legacy/20261020_before_sd_tiers_candidate`.
