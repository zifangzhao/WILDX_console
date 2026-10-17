# V1 outdated-card formatting recovery

The user reports a greyed-out Format action for an outdated/unreadable mounted
V1 SD card and requested full-chain testing with the V1 developer.

Existing V1 APP source accepts NLD1 op5, key FRMT, on a stopped writable card
without requiring a mounted catalog or zero historical recorder fault. Active,
draining, scheduled/preparing and pending ownership remain excluded. Resident
USB91 NPD1 is read-only and has no format command; the console points users to
BLE Mode → Recordings or a removable SD reader.

Host changes separate stopped storage ownership from healthy acquisition
readiness. Format accepts quiescent state0/3/4 with Writes, unchanged capacity
above8192 sectors and no Active/Pending/Waiting/Preparing flags. Completion
requires the fresh mutation ID, error0, same capacity, mounted catalog and
nextStart8192, followed by a fresh empty native94 catalog. A retained recorder
fault is displayed rather than interpreted as a new format failure. Reads can
inspect finalized faulted storage; Delete retains its healthy-state guard.
Format stays available after a catalog-list failure. Source-confirmed manual
profile selection/START can rearm a finalized capture; firmware reinitializes
the capture and clears the old fault after its normal admission checks. These
are explicit actions. Scheduled START, MODE, MOUNT/RECOVER and NPC configuration
retain their separate fault-zero guards. No automatic retry/reset was added.

Final checked private GUI: `_artifacts/v1_format_rearm_20261017/bin/WILDX_console.exe`,
SHA256 `4963C0075C75051279307545C53F93FB21B82D1A4011E140A52045B15E5AE345`.
Exporter remains A04D630F; separate download-readiness edits are excluded by the
checked HEAD override. Initial format-only build5EC was followed by one
incremental build for the source-confirmed manual-rearm dependency. Software
checks passed:4396 actual-assembly BLE workflow,17 fake-GATT native transactions,
1945 simulated V2 catalog. The preceding build passed6593 synthetic-card
maintenance checks; its download/card code is unchanged in the final build.
The new faulted/unmounted-format
case reproduces the rejection on the prior1C2C GUI. No installer or publication;
the single published installer remains unchanged.

V1 developer read-only preflight found local ST-Link3439 / MCU
001D00065942500B20313354, APPBC6B506E / USB40D39075, card serial10B35487,
500695040 sectors (238.75GiB), mounted121 records, next309215232, State0/Fault0,
Flags00387F91. This healthy card does not match the reported problem. That
preflight performed no format, recording, mode switch, reset or firmware write.

The user subsequently authorized erasing both SWD-connected device SD cards:
“it's fine to erase those two swd connected device sd”. V1 developer owns the
first mutation slot for the exact3439/001D...13354/10B35487 card above and the
bounded format/record/readback chain, using GUI4963. The second mapped device
is V2 ST-Link004B00213532511131333430 / MCU001A0004434250062039314E, Samsung512
A2 card CID1B534D464634593730AA125486A1A306,1001390080 sectors. Its existing
hardware owner is finishing an already-started recovery/restoration before
reporting fresh Idle. Second-card formatting remains serialized after the V1
slot release. No new firmware programming or publication is authorized by this
formatting request.

First V1 attempt ended before mutation: GUI4963 connected, but status had
changed to finalized State3/Fault0, media profile0/0,2144 chunks,122 records,
next309510144. Native94 timed out. The fresh read-only SWD/STATUS nonce fence
proved the same approved MCU/CID/capacity; the actor that created the additional
record is unknown. No FORMAT/START/STOP/mode request was sent. All clients closed
and the V1 slot was released. Root found no remaining WILD/USB512/CE32/test GUI
process in its scoped process inventory. A fresh recovery-format attempt must
not require a successful pre-format catalog read; after a native timeout it
must use a new attested connection. Post-format empty-catalog proof remains
mandatory.

The V2 owner restored and independently verified its normal APP136172A5 /
USB2E07799E / recovery, fresh Idle1/error0/fault0 and the same Samsung card,
front112115712, then closed all clients. Root released the second mutation slot
to that owner for one normal format. The canonical HIL runner had an obsolete
State0-only formatting guard despite firmware supporting State0 or Idle1. Root
corrected only its format branch/comment to use production CanPrepareStorage,
Writes and capacity checks, then unchanged capacity/freshLastId completion.
20 extracted actual-guard checks passed against GUI4963. Reversing just those
edits reconstructs the original6A1D8DD5 helper hash exactly, proving unrelated
owner edits were preserved. New helper SHA256 A4C229C78D767E0BDD96E518F8AABFDCDDB9EE9B178FE70DD5AFAE6B0F1DE7E2;
receipt `_artifacts/v2_format_guard_20261017/checked-guard.json`.
Both physical formats subsequently passed. V2 sent one op7/id2, preserved the
exact card identity/capacity and returned Idle1/error0/fault0 with next8192.
Native Format write/readback/Mount caches showed154 entries before and zero
after, zero log/journal sectors and the expected verified identity. This is
native cached readback evidence, not a separate raw-card read or secure payload
erase. Evidence is `D:/Code/XE_NP_V2/_artifacts/v2_card_format_20261017`.

V1 fresh recovery-format attempt passed its exact target/nonce fence and sent
one FORMAT, confirmed mounted empty via native94/next8192. It sent one SELECT
P12/15k and followed the normal firmware APP handoff. First post-handoff STATUS
timed out; a read-only snapshot confirmed the selected profile and APP Idle.
A continuation-only fresh connection then sent one START and one STOP, with
5.107 seconds monitored Running, Complete/fault0,2104 committed chunks and one
record. The first USB management readiness checkpoint was rejected; a later
read-only snapshot confirmed Role3/Busy0/Pending0. No mutation was replayed.
Actual checked4963 NpDownloadStream/catalog/strict NpCe64Export subsequently
passed the entire saved extent. P1/P2 each contained82864 AP rows; frame/counter/
relock/LFP-phase errors were zero. Startup discard614/568 bytes remained
explicit. Session1792257768, start8192, reserved end285696, next286720.
One normal APP-return request followed; fresh BLE status confirmed Idle/fault0,
P12/15k and exactly one retained validation record. All clients closed. No
firmware was changed by this coordinated format/record/readback test.

These are staged physical API results with two preserved handoff-readiness
failures, not seamless single-run/installed-UI/live-USB/all-mode qualification.
V1 evidence: `C:/Frank/Code/XE_NP/_artifacts/v1_ble_sd_recovery_20261017`.
The coordinated hardware hold was released after both developers closed clients.

Final UI-only build5903FC0550DD8F67887E38E529EDA9DB6E878D4FB2EC4A7F633CB4EDEBFB5ABD
under `_artifacts/v1_format_ui_20261017` removes the automatic catalog read on
opening Recordings. Format is reachable immediately; Refresh reads the list
only when requested. It passed4397 actual-assembly workflow,17 fake-GATT and
1945 simulated V2 checks. Physical evidence remains on4963; format/record/
download protocols were unchanged by this last UI edit. No installer rebuilt
or pushed; the latest-release root still holds one existing published package.

Full-chain acceptance for this bounded P12/15k case is complete. Post-format
rearm from retained State4/Fault is established in source/software checks; the
physical format used finalized State3/Fault0 and did not inject a recorder fault.
