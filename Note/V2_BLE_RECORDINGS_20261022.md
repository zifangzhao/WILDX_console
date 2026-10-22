# V2 BLE recordings list — 2026-10-22

WILD-X92 fixes the V2 Recordings dialog and bundles the matching APP firmware.
APP **D44907A5** replaces660A6DA4; USB **B6099A0E** and recovery **6408DBB8**
remain byte-identical. Install the bundled APP update to enable BLE listing.
The previous APP660A6DA4 reused objects built with
`XE_NP_NP2_SD_CATALOG_ENABLE` disabled. Its linked image therefore had no
catalog INFO/ENTRY handlers and did not advertise STATUS capability bit22.
The GUI correctly refused unsupported commands; an empty dialog could not be
fixed by refreshing it or by updating only the USB service.

The dialog now automatically reads a supported V2 catalog when opened. While
Stop is finalizing (state4), it reads status every two seconds and lists the
recordings after finalization. It never sends INFO/ENTRY while capture or
finalization owns storage. Old firmware retains the capability guard and gets
an explicit bundled APP update message. V1 recovery remains manual because a
native read timeout on an old card can retire its BLE session.

The unchanged64-byte protocol accepts catalog flags `0x048F0000`, including
DEGRADED and the explicit rewind offer. Rewind requires both DEGRADED and
RECOVERY. Entries remain contiguous by aligned prior directory end; Next must
match the aligned last end. Empty coherent prefixes retain their recovery
flags. No missing extents, writable frontier or complete payload are inferred.
Deletion remains unavailable on degraded/recovery snapshots. INFO/ENTRY are
read-only; neither the dialog nor its automatic refresh formats, repairs,
rewinds, deletes or starts a recording.

The bundled APP masks REWIND_AVAILABLE from outbound STATUS/INFO because its
retained control ABI has no opcode12. A coherent unknown-tail prefix still
carries DEGRADED|RECOVERY. This update does not add a destructive rewind path.

Software validation uses the actual compiled GUI for list initialization,
finalization, metadata flags, old-firmware refusal and V1 compatibility, plus
the existing BLE workflow/fake-GATT/Stop regressions. The SDK and helpers are
unchanged. Firmware source/build/native receipts accompany the release.
No connected-device recording, radio test, card access or flashing was
performed for this change; software checks do not establish hardware
qualification of the new APP.

135,954 GUI/protocol/firmware-parser and preservation assertions passed
(including per-HEX-record assertions). The installer was built once and all40
extracted runtime files matched their recorded hashes. Full fused coverage
excludes retained settings; all non-APP bytes match91, and the APP seal/body
match the APP-only update. Exact firmware source/build/native evidence is in
[V2_APP_D44907A5_20261022](../ReleaseSources/V2_APP_D44907A5_20261022/README.md);
GUI compiler inputs and package hashes are in
[1.0.0.92](../ReleaseSources/1.0.0.92/README.md).
