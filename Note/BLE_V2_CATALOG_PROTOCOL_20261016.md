# V2 BLE catalog protocol proposal — 2026-10-16

Source-audited proposal coordinated with the V2 firmware developer. The host
implements and tests this contract; firmware has not implemented or advertised
it yet. STATUS capability bit22 (`0x00400000`) gates every host catalog request.
There is no V1 0x94/0x95 fallback. No hardware qualification or publication is
implied by the simulated contract tests.

Use the existing B9 64-byte body: LE32 request magic `0x32444C4E`, response magic
`0x32524C4E`, version2 at4, operation at8, nonzero transaction at12, and IEEE CRC32
over bytes0–59 at60. INFO/ENTRY are read-only and do not consume mutation IDs.

| Operation | Request fields after transaction |
| --- | --- |
| 9 INFO | bytes16–59 zero |
| 10 ENTRY | token16, count20, zero-based ordinal24; bytes28–59 zero |
| 11 DELETE_LAST | token16, count20, last ordinal24, start28, directory end32, cookie36, confirmation key40=`0x4C44324E`; bytes44–59 zero |

All responses have error at16. Nonzero errors use the existing STATUS layout,
never the INFO/ENTRY success layouts below. Bad magic/version/CRC has no reply.
Errors include1 malformed/reserved,2 busy,3 parameter/range,4 unsupported,
6 transaction conflict,8 unsafe/unmounted catalog,9 stale snapshot,10 catalog I/O.

| Offset | INFO success | ENTRY success |
| --- | --- | --- |
| 20 | token | token |
| 24 | count | count |
| 28 | next start LBA | ordinal |
| 32 | card sectors | start LBA |
| 36 | CID CRC32 | directory end LBA |
| 40 | catalog flags | entry cookie |
| 44 | last ordinal | session |
| 48 | last start | sample rate |
| 52 | last end | probe mask |
| 56 | last cookie | entry flags |

Empty INFO has count0, last ordinal `FFFFFFFF`, last start/end/cookie0, and next
start8192. Token, cookie and CID CRC values of zero are valid. Ordinal maps to
directory block=ordinal/63, slot=ordinal%63. First start8192; later starts align
the preceding end upward to8192. Malformed entries must not be skipped.

Catalog flags: FULL16, RECOVERY17, RECOVERABLE18, QUARANTINABLE19; allowed mask
`0x000F0000`. RECOVERABLE/QUARANTINABLE qualify RECOVERY. INFO is eligible only
in mounted APP states1/5/7, with no reservation/queued mode/physical owner.
Active/pending requests return busy before SD I/O. Mounted FULL/recovery can be
listed; FULL alone does not prohibit delete. State0 is not a mounted exception.

Entry flags: format bits0–7 (0 unknown,8 media,11/12/13 NP2); footer state8–15
(0 unknown,1 RESERVED,3 COMPLETE,4 FAILED); LAST16, DELETE_ALLOWED17,
METADATA_VALID18, INCOMPLETE19; upper bits zero. Delete requires a validated
COMPLETE/FAILED last entry and no recovery/queued reservation/physical owner.
The host requires the firmware's DELETE_ALLOWED flag and rechecks the complete
snapshot after user confirmation.

DELETE error0 is a queued acknowledgment using the normal STATUS payload, not
completion. Poll fresh STATUS until not busy, require the same last mutation
ID and no execution error, then refresh INFO/entries. Verify one fewer entry,
the same card identity and unchanged earlier entry metadata/cookies; the new
tail may gain LAST/DELETE_ALLOWED. A timeout or error never replays deletion.

Firmware snapshot CRCs cover exactly15 LE32 words:

```
cookie = [0x4543324E, cardSectors, CID0, CID1, CID2, CID3,
          ordinal, start, directoryEnd, directoryEventEnd,
          CRC32(descriptor[0..507]), CRC32(footer[0..507]),
          descriptorVersionAt8, footerVersionAt8, 0]
token  = [0x5343324E, cardSectors, CID0, CID1, CID2, CID3,
          count, nextStart, tailDirectorySectorLBA(128 if empty),
          CRC32(fullTailSector), lastOrdinal(FFFFFFFF if empty),
          lastStart(0 if empty), lastEnd, lastCookie, catalogFlags]
```

Host tokens/cookies are opaque: INFO does not expose the complete CID and sectors
needed to recompute them. Firmware invalidates its snapshot cache on bind,
format, reserve, finish, recover and delete, and revalidates card/count/tail/cookie
when executing a queued delete.

Owner-approved correction on2026-10-16: descriptor/footer fingerprints exclude
their embedded CRC at508. Hashing all512 bytes of a valid sealed sector yields
the constant IEEE CRC residue2144DF1C and misses validly re-sealed metadata edits.
The focused native regression proves the first508-byte form changes both cookie
and token and rejects stale/queued deletion without writes.

Mandatory firmware gate before advertising capability: fix and test journal
recovery for an empty target directory page, including sole-entry deletion and
63-entry page transitions, with power-loss stage coverage. Reuse metadata journal
publication/readback; no CMD20/CMD38 or payload erase. Host software checks cannot
substitute for this firmware gate.
