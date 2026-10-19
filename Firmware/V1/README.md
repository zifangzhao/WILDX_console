# WILDX V1 store2 rc1 — 2026-10-19

Frozen checked console **1.0.0.44**, APP **654824DE**, USB **FACEA0D2**, ABI **258**.
This is a distinct NP/store2 candidate with the executable's embedded version
preserved. No application or firmware rebuild, version bump, re-merge or reseal.
V1Camera and V2 firmware/targets remain unchanged.

FS2 requires an **explicit fresh format and epoch**. It does not silently migrate
legacy recordings; older writers must refuse FS2 media. Formatting erases data.
USB download uses the checked role3 **full-speed compatibility mode**, about
**0.83 MiB/s**. Capture roles retain high speed. Normal high-speed download remains
unqualified; this is not an electrical USB diagnosis.

The exact current pair passed five minutes of P1+P2/20 kHz, live BLE reconnect,
and three saved-data windows totaling 72 MiB. The roughly 10 GB capture was not
fully read back. Shorter strict NP/IMU exports and production USB download/export
passed. Actual transient descriptor-read unavailability was exercised through
Mount and START remount, followed by safe append and full new-extent readback.
This is not physical-removal, camera, implant or all-mode endurance qualification.
P1+P2/30 kHz sustained recording failed on this card. IMU uses latest-value
snapshots; independent ADC-bit integrity is not established. Earlier tests of
other modes retain their original binary scope. APP60EC609E is excluded.

The four firmware files, checked EXE and companion DLL are copied byte-for-byte
from the owner's frozen delivery. Fused firmware was already accepted by the
actual r7 loader; configuration is excluded. Use the APP-only file with the APP
updater. APP-only updates do not install USB/recovery. Service/recovery/fused
images require their separate programming workflow; do not mass-erase.

Owner evidence is preserved under Firmware/V1. Original candidate flags report
its pre-installer state; the new release manifest and publication receipt record
this publication separately. No hardware access, application execution, new
qualification cycle or source integration is performed by this packaging step.
The console build input hash ledger is retained; clean source reproducibility
and inclusion of subsequent dirty edits are not claimed.


- [APP-only](V1_SD_BLE_app_only_store2_rc1.hex)
- [USB service](V1_USB_service_role3_FS_store2_rc1.hex)
- [Recovery + USB](V1_recovery_USB_role3_FS_store2_rc1.hex)
- [Complete fused image](V1_fused_bootloader_USB_SD_BLE_store2_rc1.hex)
- [Frozen owner handoff](source-candidate.json)
- [Exact-pair five-minute evidence](current_endurance20_evidence_r1.json)
- [Production USB download evidence](usb_fs_current_evidence_r1.json)
- [Degraded Mount/START-remount evidence](s2pg_degraded_evidence_r1.json)
- [Previous V1 release](legacy/20261019_before_store2_rc1/README.md)
