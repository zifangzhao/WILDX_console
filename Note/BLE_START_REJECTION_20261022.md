# BLE recording start diagnostics — console 1.0.0.94

The user's V2 report shows repeated START (operation 2) rejections with error
8 and fault 0. In the bundled APP D44907A5, this synchronous rejection comes
from catalog admission flags, before probe acquisition starts. The photo does
not contain the raw flags needed to distinguish FULL from recovery-required.
Staged IMRO messages do not establish a probe startup fault.

## Console changes

- Retain CRC/transaction-validated rejected status instead of clearing the
  device state. Reject receipts from a changed peer, connection epoch or
  protocol version. Transport failures still leave the outcome unknown.
- Explain V2 FULL and recovery-required separately; keep the condition visible
  after read-only polling. Error 8 without those flags does not fabricate a
  diagnosis. Existing Recover does not free a finalized incomplete allocation.
- Stop pending per-probe application checks after a definite START rejection.
  Lost acknowledgements remain uncertain and are not replayed.
- Mark the host BLE metadata request rejected, retaining its error/flags/ID.
  Do not attach an existing or later recording to the rejected request.
- Include raw status flags, allocation bounds and transaction IDs in Report.
- Put existing catalog recovery in BLE Mode alongside recording controls.
- Show V1 clock synchronization and per-port upload progress. Existing B8 wire
  behavior, qualification warning/acceptance flag, and old firmware support
  remain. The earlier V1 pre-START disconnect is not claimed fixed.

Manual V2 START remains an ordinary firmware request. No new cached GUI start
gate, automatic START retry, automatic recovery, deletion or format was added.

## Build and evidence

The local executable is `_artifacts/ble_start_errors_20261022/gui94/compile-bin/WILDX_console.exe`.
Its exact source/binary hashes are in `gui94/build-receipt.json`.
The frozen GUI 1.0.0.93 compiler graph was reused, replacing only five BLE
sources. The previously checked calibration backup policy is included.
SDK SHA256 remains `86EF0F858AE6CDB188C9C525ED50894EA204D6DB15C1C38BC1EB472CDBC7F546`.

- PASS 60 rejection/status/peer/no-replay checks, including actual-assembly
  parser and rejected recording metadata checks.
- PASS 104 actual-source recording flow checks with fake transport, including
  unqualified V1 acceptance, single START, staged-map rejection, pending Stop,
  lost receipts, changed connections, and no extra V2 host admission gate.

No hardware was accessed. This executable has not been packaged or published.

## Coordinated V2 firmware work

The V2 developer changed only the proven pre-arm, zero-payload
cancellation case. A distinct CRC-protected FAILED footer reason permits a
metadata-only retained entry without reserving the remaining card. Ordinary
FAILED/error-8 records and all ambiguous/nonempty extents remain protected.
This prevents future empty cancellations from fencing the card; it does not
retroactively reclaim the user's existing ambiguous incomplete session.

Firmware source/evidence is owned by the existing Neuropixel V2 chat and
`D:/Code/XE_NP_V2/Note/V2_EMPTY_CANCEL_SOURCE_FIX_20261022.md`.
No new firmware image is built, bundled, flashed or published by this console
change. Existing recorded samples and SD catalogs have not been modified.

The frozen SDK also passed 11 checks using the actual native encoder's
`native-r4/capability1` descriptor/footer/UTC fixture. It preserves every
metadata byte and the FAILED/empty-cancellation status, creates no DAT, reads
no payload, and refuses strict-complete export, a short catalog bound, and a
corrupt footer before creating output. Evidence is in
`_artifacts/ble_start_errors_20261022/empty-export-r4.log`; input hashes are in
the GUI build receipt. This is software compatibility evidence, not a
recording, firmware image build, or hardware recovery result.

The final native run (`native-r5`, both BLE capability 0/1) passed actual APP
empty-cancellation and catalog tests for all-remaining-capacity reservations,
followed by admission of a second continuous START. Each variant covers four
positive cases, 16 guards, seven malformed/unproven footer cases and 20 modeled
publication interruption scenarios. This tests the portable source paths;
there is no new ARM image or physical-device evidence. Final run-log SHA256:
`8FB62B3528739E59D03640140C86B2E580CECBB4363A4E90D56EE5D3459F28BE`.

Only the four isolated firmware production hunks may be integrated into the
checked APP/USB baselines later. The developer's private current Core tree
contains unrelated work and must not be built wholesale for this change.

The console changes are included in the packaged GUI1.0.0.96 installer.
The separate V2 firmware source correction is not built or bundled.
