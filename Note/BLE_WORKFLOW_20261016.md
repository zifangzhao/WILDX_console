# BLE workflow — 2026-10-16

The human confirmed that waveform preview and file management should be added
to BLE Mode, with continuous recording instead of a duration input, and
explicitly authorized coordination with the V2 developer for BLE file support.
This development is not packaged or published.

Implemented host behavior:

- Start uses the existing continuous protocol: V1 reserves remaining whole-card
  allocation units; V2 requests zero blocks. Capability, profile, capacity and
  readiness guards remain. The recording profile spans the removed timer space.
- V1 preview uses the existing NpV1PreviewBurst decoder: active probe 1–3,
  channel 0–383, AP/LFP, validated origin framing, nominal signed counts and
  configured factory ADC/gain correction. The queue is bounded; missing samples
  remain gaps. Epoch/session checks and the two-second device lease keep this
  decimated display independent of recording. Controls take priority. Closing
  stops renewal and attempts one preview-stop command. No full-rate BLE payload
  download or analog-fidelity claim is made.
- Existing V2 P1/P2 preview is preserved; P3 display remains unsupported by its
  decoder. Preview windows are not always on top.
- Recordings provides ID, allocated size and sector extents. V1 uses existing
  native 0x94 catalog / 0x95 delete-last commands. After confirmation, the host
  rechecks fresh status and the full catalog snapshot. Controls are serialized;
  both delete acknowledgment and post-delete parameters are drained, including
  coalesced replies. Uncertain native replies end the BLE connection so late
  uncorrelated replies cannot satisfy another command. Mutations are not replayed.
- Format/mount use each family's existing checked SD controls. Confirmation names
  the target/card; active/preparing/draining/pending states are refused. V1's
  native ABI has no card CID, transaction ID, payload CRC, or atomic expected-count
  delete; host guards do not create these firmware guarantees. Sizes are catalog
  allocation sizes, not validated sample lengths. Payload export remains USB-based.

V2 host listing/deletion implements the developer's source-audited protocol
proposal: INFO9, ENTRY10 and DELETE_LAST11, gated by STATUS bit22. It validates
correlation/CRC, flags, complete catalog snapshots, entry geometry and deletion
eligibility. Queued ACK is not completion: fresh STATUS must confirm the same
mutation ID with no error, then INFO/entries must show exactly one fewer recording
and unchanged earlier entries/card identity. Timeout, stale snapshot and I/O
failures never replay deletion. See [wire proposal](BLE_V2_CATALOG_PROTOCOL_20261016.md).
Firmware has not implemented/advertised this yet; its empty-page journal recovery
and power-loss tests must pass before capability is enabled. V1 native file
commands are never sent to V2. Existing firmware retains mount/format, with
unsupported list/delete disabled. The initial implementation phase did not access hardware. Subsequent developer-owned V1 checks and the remaining V2 gaps are recorded in [current validation](BLE_VALIDATION_20261016.md).

Software checks: private cached GUI build passed without warnings/errors, using
the checked A04D630F exporter. New tests passed 4,296 actual-assembly preview,
framing, catalog and UI checks; 17 production native-transaction checks against
fake GATT; 1,945 V2 protocol/queued-delete checks against simulated firmware;
and 12 continuous-recording/geometry/layout checks. Existing regressions
passed 1,433 BLE feature, 40 media-flow, 116 media-assembly, 633 V2 profile and
421 preview-decoder checks. The media runner now includes the V2 control source
required by its current profile fixture. Offscreen renders were inspected.
Run `test_ble_workflow.ps1 -ConsoleExecutable <private-GUI.exe>` for new checks.
These initial software checks performed no scan, connection, recording or physical card mutation. See current validation for the subsequent V1 hardware evidence.

Private evidence: `_artifacts/ble_v2_catalog_20261016/checked-build.json`, logs,
91 source hashes and renders. GUI SHA256:
`B04043B674853588791F77EB526E205D53CC24002CE3E04E194C96DCF51B623D`.
Exporter SHA256:
`A04D630F1EA716FD3EC4D3DE54C3875479315B6647F4B4A3F4C60C26BB519070`.
The private build uses HEAD's NpDownloadStream.cs, excluding the separate USB
readiness change. That change passed software checks but failed the V2 legacy
per-read full-DAT path; it is not hardware-qualified as a fix for that path.

Canonical Release bin/obj, versions and existing installers are unchanged.
Pre-existing V2 source edits were preserved. Initial timer-only evidence and
the pre-edit form delta remain under `_artifacts/ble_workflow_20261016`.

Separate immediate V1 release coordination reused the already-published WILDX33
USB_QUALITY_FIX installer (SHA75F9E4E1, GUI D1271EC7). V1 published USB91 service
plus unchanged APP BC6B506E on its existing release branch in commit
`cd9500b1db09bd1e60d67ef327caa1f065a1fc6a`. USB5s/30s evidence and older APP BLE
evidence remain distinct. These new BLE controls and timer removal are not in
that installer. No new WILDX build, package or push was needed for that release.
