# BLE preview and SD management validation — 2026-10-16

Human request: make preview and SD management work for both V1/V2 over BLE,
coordinating with both firmware developers. Each developer owns its hardware;
the console chat owns host changes. Preserved recordings must not be erased.
No packaging/publication is part of this validation request.

| Path | Current evidence | Remaining work |
| --- | --- | --- |
| V1 connection + catalog | Hardware: unchanged APP BC6B506E / USB91 40D39075, UID001D00065942500B20313354;121 records across2 native94 pages, fault0; all retained | Intermittent discovery/reconnect remains; see below |
| V1 AP/LFP preview | B040 actual-form hardware pass: AP/LFP on both P12/15000 probes; recording advanced without fault; status at most170ms; Stop completed with preview open; full new-record readback passed | Targeted P12/15000 run only, not all profiles or endurance |
| V1 delete-last | E93F hardware pass after direct human approval: exactly one native95, both95+90 replies drained;122 to121 entries, all121 prior entries unchanged; state0/fault0 | Existing firmware lacks atomic expected-count/CID protection and has incomplete native95 pending guards; host/bench fences do not create firmware guarantees |
| V1 mount/format/recover | E93F Format bytes passed the actual V1 command engine: opcode/key/CRC/ID wrap and active/pending/write-disabled/stale refusals; no card operation | Safe physical-media scope; no format of the121 preserved records |
| V2 P1/P2 preview | Existing host software checks pass | Verify live taps in actual runtime profiles; current tap is in non-DIRECT path |
| V2 P3 preview | Current host/firmware rejects probe3; P3 needs separate D8 lane handling | Agree an explicit capability/normalization marker, implement and verify source ownership + decoding |
| V2 list/delete-last | Host INFO9/ENTRY10/DELETE11 passes1945 simulated checks. Isolated firmware commands pass defaultOFF/localON native checks,71 owner gates and16 deletion cut cases; owner-approved first508-byte metadata fingerprints reject re-sealed stale snapshots. Empty-page recovery gate also passed | Owner integration, ARM/firmware and end-to-end checks remain; capability22 defaultOFF |
| V2 mount/format/recover | Existing capability/state/receipt checks | Developer validation using disposable media/recordings only |

Frozen host artifacts (version1.0.0.33, no firmware bundled):

- B040 at `_artifacts/ble_v2_catalog_20261016/bin/WILDX_console.exe`, SHA256
  `B04043B674853588791F77EB526E205D53CC24002CE3E04E194C96DCF51B623D`.
- Corrected E93F at `_artifacts/ble_validation_20261016/bin/WILDX_console.exe`,
  SHA256 `E93F0B8ABFFA5B701D625F29EA4D5513F0E2E52233D78595A88FF081A2124A4D`.
- Both use exporter SHA256
  `A04D630F1EA716FD3EC4D3DE54C3875479315B6647F4B4A3F4C60C26BB519070`.
- Final source-check build at `_artifacts/ble_verified_20261016/bin/WILDX_console.exe`,
  SHA256 `08C5EE62C4B569012181653D03D2FFC4949683D58730CD04B4194E5777FA8352`.
  Only named V1 mode destinations were added after E93F, preserving wire values;
  its4334 actual-assembly,17 native transaction and1945 simulated V2 checks passed.
  This build has not been used for hardware tests or packaged/published.

E93F changes: initialize V1 preview to the first active probe; V1 FORMAT uses
operation5 with word9=`0x46524D54` and otherwise zero geometry/reserved words;
V1 format/mount derive IDs from fresh device status; shared mount/format verifies
the acknowledged LastId again at completion. No wire changes to V2 from B040.
The updated selector regression reproduces the inactive-probe bug on B040.

V1 developer interoperability test: actual E93F Format helper bytes were fed
to `Firmware/V1/Core/Inc/np_sd_control.h` npl_command with a stubbed apply
boundary. `Tools/V1Capture/Ble/test_host_format.c` passed, including rejection
of the old wrong opcode and missing FRMT, and no extra apply for cached IDs.
The developer also added/tested native95 pending-fence hardening in source;
it has not been built into or flashed over the existing APP.

E93F compiled without warnings/errors and passed4326 actual-assembly workflow,
17 fake-GATT native transaction and1945 simulated V2 contract checks. Its91
source hashes/snapshot are frozen alongside the build. These are software
checks, not hardware qualification. Subsequent targeted E93F hardware results
are recorded below.

V1 hardware catalog evidence reported by its owner:
`C:/Frank/Code/XE_NP/_artifacts/v1_ble_workflow_20261016/read_only`.
Live scan witnessed a Random address; a separate address-only/Unspecified
reconnect failed during endpoint lookup before recorder commands. The production
UI passes the observed address type. Keep that reconnect caveat separate from
catalog/preview correctness.

Later short independent scans could not see CA53BA either. Nonhalting SWD showed
healthy idle APP BC6B506E, state0/fault0/mailbox0/control0, no Cortex fault,
and advancing WFI counters. One guarded stopped APP restart/normal SD entry
did not restore advertising in the short scan. Typed and untyped AEP lookups both
timed out; there is no evidence a host selector change fixes this. A later12s
unfiltered scan saw CA53BA again at -82dBm. The human was told no reconnect was
then needed. This reappearance is not a proven reconnect fix.

The V1 developer subsequently exercised B040's actual preview form on a bounded
new40s P12/15000 test reservation (distinct from the new GUI continuous Start).
AP/LFP on both active probes passed; recording progressed with fault0; status
latency was at most170ms; Stop completed with preview open. The catalog gained
exactly one new test entry. Full readback of its2416 committed chunks passed:
1,236,991 frames per probe, zero sync/heartbeat/fill/fixed/counter/cadence errors,
valid descriptor/misc sequence and complete footer. The121 earlier catalog entries
were unchanged; their payloads were not reread. Evidence is under
`C:/Frank/Code/XE_NP/_artifacts/v1_ble_workflow_20261016`.

The readback harness initially selected Mode3 (ROM DFU) instead of Mode4 (USB
Download). This was a harness error; the production GUI mapping was correct.
The developer corrected it and returned through normal USB control without any
firmware programming. Named destination constants now document that mapping.

Physical delete was initially rejected by automatic approval review for missing
direct human permission. No command was sent during that rejection. The human
then approved deletion directly in the V1 chat. A fresh independent exact-target
SWD pending/ownership/DMA fence and full122-entry production catalog recheck
preceded exactly one deletion of only the new test record (session1792188014,
start309215232,end309540864). Native95 returned121; native90 was drained; fresh
native94 verified all121 original entries unchanged and next start309215232.
Final state0/fault0, clients disposed, existing firmware unchanged. Evidence:
`C:/Frank/Code/XE_NP/_artifacts/v1_ble_workflow_20261016/E93_disposable_delete_approved_r2`.
The first GATT discovery attempt failed before recorder commands; a fresh
connection succeeded. No physical format test was performed. V2 was untouched.

V2 source-only recovery handoff: isolated inputs matched the developer's four
frozen hashes. Only `Core/Src/xe_np_np2_dual_sd_catalog.c` and its existing native
fixture changed. A canonical zero journal at the traversed terminal page is
replayed explicitly; earlier entries are never repaired as a side effect.
Mount remains read-only and clears stale cached tail state. Corrupt shadows,
unreachable targets and invalid retained prefixes refuse recovery writes.
The new fixture reproduced the old Mount failure before the fix, then passed
all original cases plus publication/recovery cuts, warm remount, preservation
and idempotence cases. Patch SHA256:
`3E8D65C1E1ED92E9957745CAB7BD67C0837797B9320BD75DD83846661A981E8A`.
Receipt, scoped patch and native logs:
`C:/Frank/Code/XE_NP/_artifacts/v2_ble_catalog_recovery_20261016`.
No live V2 source edit, ARM build, hardware, capability or preview ABI change.

The subsequent command slice is source-only under
`C:/Frank/Code/XE_NP/_artifacts/v2_ble_catalog_commands_20261016`.
Use receipt-crc508.json and01 followed by02-ble-catalog-commands-crc508.patch;
the corrected command patch SHA256 is
`360E559A7CB87633BCC8E16FF47BC6A8AF07ED3386284E938A89613C2CE02200`.
The first full-sector fingerprint proposal produced a constant CRC residue for
sealed metadata; its original patch/log remains historical, superseded evidence.
The owner approved excluding embedded checksums. The focused corrected regression
passed; no repeated broad suite or incidental live integration was performed.
