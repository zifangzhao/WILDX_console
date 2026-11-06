# USB card diagnostics and Delete Last

Common V1/V2 service capability bits in IN6E/32: `0x20000` SD Info;
`0x40000` guarded USB Delete Last. An absent capability forbids the new OUT.
These operations require an idle service, exact UID and exclusive ownership.
They never reset/cancel an active stream or fall back to Format.

## SD Info

`A5 IN384`, value/index0, returns the existing SDIFv1 cache. `A6 OUT0`,
nonzero16-bit sequence in value and index0, schedules one foreground query.
The cache's offset16 echoes that sequence. State1 is pending,2 ready,3 error.
Initialization/queries run outside EP0 and recording; no sectors are written.
Restore the download host and verify the same CID/capacity before publishing
completion. A failed restore invalidates download readiness. Partial fields
retain their validity flags; an error is not a healthy-card result.

The host polls for up to20s, checks final idle identity, and never replays an
ambiguous OUT. Legacy passive capacity/CID/cache queries remain available.
In particular, V1 OUT4C remains camera exposure and is never an SD fallback.
V1 admits active refresh only in idle download mode: the GUI first reads its
capability passively, releases that handle, then uses the existing exact-UID
download transition before refreshing. Old V1 firmware stays passive. V2 also
supports active refresh in idle streaming mode.

## Delete Last (V1/V2)

`A7 IN128` returns a CRC-protected receipt. `A7 OUT0` schedules inspection;
nonce is value | index<<16, nonzero and increasing. `A7 OUT128`, value/index0,
commits an explicitly confirmed completed inspection. It copies that exact
receipt, changing only op to2, nonce to previous+1, inspectedNonce to the
inspection nonce, and CRC. All integers are little-endian u32.

| Offset | Field |
|---:|---|
|0|Magic `0x3155444E` (NDU1)|
|4|Version1|
|8|Operation:0none,1inspect,2delete|
|12|Phase:0idle,1queued,2running,3complete,4failed|
|16|Nonce|
|20|Error|
|24|Device/catalog epoch|
|28|Flags:1 card ready,2 last entry deletable; V1 adds4 incomplete,8 failed; other bits zero|
|32|Capacity in512-byte sectors|
|36|Catalog count|
|40|Allocation frontier|
|44|Last ordinal|
|48,52|Last start/endLBA (V1 end is the authoritative allocation frontier)|
|56,60|Last cookie/token|
|64..79|CID, four words|
|80..91|MCU UID, three words|
|92|SD clock|
|96|Inspected nonce|
|100..123|Reserved zero|
|124|IEEE CRC32 over bytes0..123|

Firmware rechecks fresh card identity, catalog and exact last entry before
writing only journal/checkpoint/directory metadata. V2 requires a completed
last entry; V1 can also remove a stopped incomplete/failed record with valid
allocation and descriptor proof. An empty or active last entry is not deletable. Completion
binds the inspected nonce/card/epoch and retains the removed-entry identity;
count/frontier reflect the remounted catalog. Host requires count-1 and a
frontier between8192 and the removed start. Deleted payload is not securely
erased and can be reused by a later recording. No automatic payload validation.

V1 app246 uses mirrored FS2 redo intents at114/115 to authorize checkpoint
rollback, recover interrupted deletion and preserve earlier recordings. Stable
completed checkpoints retain the existing version2 format. Older firmware
lacks pending-delete recovery. See `V1_DELETE_LAST_20261105.md` for scope.

V1 BLE advertises status bit0x10000000. CE32 commandA7 accepts8-byte requests
with little-endian operation0(GET)/1(inspect), nonce at4 (zero for GET), or the
same128-byte NDU1 confirmation as USB. Replies are128-byte NDU1 receipts.
