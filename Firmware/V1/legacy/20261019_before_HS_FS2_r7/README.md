# V1 NP SD/BLE release — 2026-10-18

Old V1 Neuropixels board, 91k/91k divider. APP **FA2016E7**, USB **02FF93A9**,
WILDX **1.0.0.44**. V1Camera and V2 are separate targets.

- [APP-only](V1_SD_BLE_APP_FA2016E7_20261018.hex): preserves USB, recovery and settings.
- [USB service](V1_USB_service_02FF93A9_20261018.hex): separate service programming workflow.
- [Recovery + USB](V1_DFU_boot_USB02FF93A9_20261018.hex): separate service programming workflow.
- [Complete fused image](V1_fused_boot_USB02FF93A9_APPFA2016E7_20261018.hex): checked recovery/USB/APP; configuration excluded.

**Large USB downloads remain unresolved:** 522,272-byte replies on the current
hubbed bench still fail with Windows 31 after two bounded retries. Small CRC-bound
reads, checked cleanup and APP return pass; failed/partial bytes are rejected.
This is a bounded operational SD/BLE development release, not complete USB,
all-probe-mode, camera or implant qualification.

P1+P2/15 kHz ran 810.105 seconds/18.84 GiB with BLE status/preview, reconnect and
clean Stop/catalog. Short P1+P2/20 kHz also passed. Independent readback checked
two windows of 128 chunks each: 65,535 frames/probe/window with zero known
protocol-field/counter/cadence errors. Full-extent readback remains unverified.
APP/USB installation was independently read back; boot/configuration were unchanged.
Physical Format, power-cut, other profiles/custom maps and fused programming were
not qualified in this cycle. Recovery is explicit/read-only; uncertain extents remain protected.

Use only APP-only files through the APP updater. This does not install the USB
service. Fused/service files require their separate documented workflow; never
mass-erase or use V1 files on V2. Fused coverage is 2,088,960 bytes, excluding
configuration; mass erase would still erase configuration.

[Exact owner handoff](source-manifest.json), [owner evidence/limits](source-README.md)
and [console build receipt](source-console-build-receipt.json) are retained. The
console comes from a dirty tree with two frozen overrides, without a complete
precompile snapshot. Exact source reproducibility is not claimed. Main GUI
construction passed; optional resource/icon loss was accepted by the owner.

Superseded artifacts are [archived](legacy/app122_before_sd_hardening_20261018/README.md).


## User-requested unverified high-speed image

[APP654824DE + HS USB02FF93A9 fused image and exact component files](unverified/20261019_APP654824DE_USB02FF93A9_HS/README.md) are published as **unverified**. High-speed download Windows31 remains unresolved. This candidate is separate from the current stable catalog and installer.
