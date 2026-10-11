# WILDX1.0.0.3 + V2 USB1138F666 — packaged release

User requested packaging and publication of the already-checked USB/IMRO updates.
One existing-NSIS packaging pass reused the Release executable and all runtime
helpers; no application/firmware rebuild, hardware operation or installer launch.
The Ponytail workflow reused the existing updater and packaging recipe rather
than adding a second firmware-update path during delivery.

## Deliverables and checks

- Current installer: `LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.3.exe`.
  2240976 bytes, SHA256 `BD0DC44DCC43403CB8A770B71923A453BDCED72B1287C3247E1ACE01F887CFFF`.
- Checked executable:1163776 bytes, SHA256
  `3C70D2104979AA468E507CD6DF1CCCB1E84CE9FE20E5EC1C9FEAC261C2FC0BC1`.
- Independent full7-Zip extraction matches all13 runtime payloads byte-for-byte.
  Initial7za extractor lacked NSIS support; the installer did not need rebuilding.
- Prior43-case NPC2 codec/applied-receipt checks and4 preference checks reused.
  Per-device/per-port IMRO requests persist, but applied status must be confirmed
  by fresh matching hardware receipts before each USB capture.
- Previous1.0.0.1 installer/manifest retained in `LATEST_INSTALLERS/legacy/1.0.0.1_20261010`.
- Removable evidence: `_artifacts/release_1_0_0_3_20261010`.

## Firmware and operating limits

Matching firmware lives in `zifangzhao/XE_NP`, branch `V2`,
`LATEST_FIRMWARE/V2_NP2`: USB1138F666 plus unchanged published loader97C3716A
and SD/BLE APP ED881256. Both a fused HEX and USB-only HEX are supplied.
The new fused assembly preserves loader/APP bytes and seals; it has not been
newly hardware-qualified as a complete stack. Unvalidated APP37B83E87 is not shipped.

Prior exact-USB hardware evidence passes10 headless continuous cases: P1/P2
solo15/20/30kHz, P1+P2 at15/20kHz, all-three15/20kHz. All selected IMROs
had matching applied receipts, classic384-channel int16 DAT files and preview
callbacks, clean Stop/drain and no reported dropped blocks or framing errors.
Longest run: triple20k,59.185s,35.986MB/s payload,1183231 rows/probe.
See `Note/Tools/V2Usb/NP2_CONFIGURATION_20261010.md` in the firmware repo.

The existing GUI path is **Firmware Update → Recovery loader / Update NP2 APP**.
It is a UID-pinned protected WinUSB APP updater, **not STM32 ROM DFU**.
It cannot install the resident-USB HEX: use addressed SWD programming for that
slot or the fused engineering image. No mass erase or option-byte changes.

No fresh GUI interaction, firmware-update cycle, clean installation, SD/BLE
IMRO qualification, long endurance or analog/electrode qualification is claimed.
Multi-probe30k explicitly warns unqualified;7.5k and P3-alone/other-pair USB
profiles remain absent. The unchanged SD/BLE APP is not a three-probe release.
This is a packaged engineering release, not a newly end-to-end-qualified pair.
