# WILDX51 — V1 USB200mA/fallback firmware refresh

Installer: `WILDX_console-Setup-1.0.0.51-V1-SD-power200-USBB5FC4876.exe`.
GUI runtime stays1.0.0.51; this package updates only V1 NP USB firmware and its
matching fused/component images. V2 and camera target bytes are unchanged.

Use `Firmware/V1/V1_fused_BOOT_USBB5FC4876_APP654824DE_sd_power200_20261020.hex`
for the complete matching stack. Retained configuration is excluded.

Bounded local USB download test: explicit reset22.85s,84MiB byte-exact reference,
1GiB known hash at14.51MB/s,zero retries/errors,31-record catalog unchanged.
200mA allowance is explicit for UHS/SDR25; the U5 CMD6 completion check is fixed.
Faster UHS remains unaccepted on this card; this is not a43MB/s release claim.
Recording APP/boot are unchanged; no new streaming/SD recording/camera/endurance
or physical rail-current validation. See the bundled manifest for exact scope.
