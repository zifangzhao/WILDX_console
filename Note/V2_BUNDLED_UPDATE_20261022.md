# V2 bundled firmware and mismatch review — WILD-X 1.0.0.88

The V2 firmware manager now has a primary **Update bundled firmware** button
and two explicit choices: bundled APP only, or full APP + USB + recovery.
Opening the manager reads status for the selected UID. Reported mismatches
offer firmware management once per physical UID/bundle/installed build; this
never starts flashing. Exact matches and unavailable identities stay silent.
Different/private builds are described as different, not assumed older.

The existing V2 APP660A6DA4/USB E323A06A bundle does not advertise NPF1 image
identity over USB or BLE. This was confirmed with the V2 developer from the
matching firmware sources. Its board-ID allowlist remains empty. The GUI shows
**Version comparison unavailable** for this legacy firmware and allows manual
bundled updates. Automatic mismatch review requires actual version descriptors;
no version is inferred from a UID, device name, or current GUI settings.

APP only uses the unchanged protected V2 helper. That helper and recovery loader
cannot write the USB-service .xenp package. Full firmware instead uses the
existing guarded STM32CubeProgrammer ROM-DFU workflow, with V2-specific image
admission and an explicit power/recovery warning and confirmation. It requires
an idle V2 USB service and the selected full MCU UID. A recovery-only device
must first return to its V2 USB service. No new backend, mass erase, option-byte
change, APP/USB role bypass, or firmware protocol is introduced.

Only the exact catalog-SHA-pinned V2 fused HEX is admitted to that route.
Its V2 APP and USB manifests, vectors, CRCs, complete logical flash geometry,
and omitted configuration page are checked. The existing backend backs up the
reported 2/4 MiB physical flash, programs changed pages only, preserves the
8 KiB configuration page [0801C000,0801E000) and upper flash, checks the full
MCU UID around programming, verifies complete readback, exits through the
recovery reset vector, and reacquires the same idle V2 UID. Failure does not
cause an automatic retry or reset. The previous V1 image admission remains.

All firmware binaries, SDK and updater helpers are unchanged from 1.0.0.86.
Only the firmware catalog adds the checked full V2 asset; GUI code adds the
workflow and review behavior. The fused image is still the previously published
0BFECE90D3D0B098C645DB5370052C806EC6FB21FE0F1947F1718DFCE61EEFB1 image.

Validation on exact GUI 1.0.0.88:

- 30 production-source mismatch/review/decline/deduplication/legacy checks.
- 130,632 actual-app V1/V2 image admission, catalog hashes, guarded fake-DFU
  records, preserved configuration/upper flash, wrong UID, bad readback,
  unchanged-image refusal-to-reflash and rendered V2 control checks.
- 592 firmware bundle/wire identity/passive snapshot/UI checks.
- 139 probe metadata/native SD/layout, 22 metadata persistence, and 48 operation
  ETA/calibration/merge/media regressions. Total: 131,463 software checks.
- Fake programmer and synthetic files only. No attached device, real flash,
  real DFU transition, installed update, or newly-flashed-fused qualification.
  The installer is extracted/hash-verified without execution.

GUI SHA256: `866DD486D2ECEA0792DB87E942B2FD9C39E7849087411C3430090E17F58E563E`.
Exact compiler inputs and hashes are under ReleaseSources/1.0.0.88. Compilation
87 was intermediate; 88 is the final release. The separately reported V2 Stop
finalization question is not changed by this update.
