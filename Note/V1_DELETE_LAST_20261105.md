# V1 crash-safe Delete Last

App 1.0.0.246 adds guarded V1 USB and BLE Delete Last. V2 retains the
checked APP99CC44F9/USBEDEB6C3A pair and its existing transaction. The earlier
241 SD Info/preview/port-label changes are retained.

## Transaction and compatibility

V1 FS2 uses mirrored redo intents at sectors114/115 before changing either
allocation checkpoint at112/113. The intent binds the physical card CID,
capacity, format epoch, operation nonce, exact canonical checkpoint preimages,
desired checkpoint and the last/previous descriptor identities. Checkpoint and
journal writes are read back and verified. Earlier descriptors and recording
payload are never rewritten by deletion or its recovery.

A completed deletion leaves ordinary version2 checkpoints readable by older
firmware. An interrupted deletion requires updated firmware recovery before
another allocation. The new PC decoder can expose a validated, explicitly
read-only pending-deletion view without writing sectors; a PC reader lacks an
independent physical CID query. Ambiguous/conflicting/stale states cannot
authorize rollback. Format epochs are random nonces, not ordered versions.

Recovery completes the confirmed transaction, clears both intents, then allows
a new recording. A stopped incomplete/failed last record can be removed when
its allocation and required descriptors are valid. Active recording, unknown
allocation, missing/conflicting required descriptors or changed card/target
are rejected. No full payload validation runs on the device and no new work
is added to the steady NP sample path.

USB and BLE share the NDU1 confirmation envelope. USB advertises capability
0x40000; BLE advertises status0x10000000 and uses commandA7. BLE GET/inspection
requests are8bytes (operation0/1, nonce); confirmation is128bytes. V1 flags4/8
identify incomplete/failed last records. Confirmation is sent once; a missing
acknowledgement requires status/recovery inspection rather than replay.

## Development acceptance

| Check | Current evidence |
|---|---|
| Actual storage header / sector persistence model |1,689,632 assertions;6,156 delete cuts,5,643 recovery cuts,1,109 corruption/read-fault cases and append after recovered deletion, including stopped OPEN/FAILED, previous OPEN and a stale bounded descriptor grant. Earlier descriptor/payload sentinels unchanged. |
| Shared wire engine |3,057 checks for complete128-byte snapshot comparison, UID/CID/capacity/epoch/nonce, queued lifecycle, failure/cancellation, grant authority and no replay. |
| Actual app246 |6,798 USB/BLE framing/correlation and native-boundary/torn-write PC checks;1,893 legacy BLE transport checks reused from244.220 maintenance and390 legacy FS2/catalog checks reused from242 with unchanged corresponding code.208 stable input identities; checked SDK unchanged. |
| Firmware and hardware |Accepted APP4A677F83/USB11646986, exact originals/settings restored. USB/BLE normal deletion passed. VEE hold at CP0/CP1 plus fresh reset recovered once; all 74 protected earlier fingerprints matched. Post-recovery NP/AP/LFP and 39,110 valid IMU rows passed; RTC-to-first-NP replay <1ms. Final cold-ready/Inspect delta passed. |
| Packaging |One NSIS invocation and one fresh49-file extraction comparison passed for1.0.0.246. Exact identities are in ReleaseSources/1.0.0.246. |

## Integration checks

The USB image's actual startup reservation was 4 KiB, while the new cold call
chain needs 6,432 bytes. Comparing that chain with an assumed 8 KiB budget missed
an overwrite of USB state. The corrected image reserves 8 KiB, adding 4 KiB of
general RAM without moving the primary NP, IMU, download or DMA buffers. APP
already reserves 8 KiB. A measured download-idle interrupt configuration gives
a conservative known total of 7,380 bytes, leaving 812 bytes; this is not an all-mode
or arbitrary-interrupt stack bound.

Successful polling SD reads leave a HAL context flag despite READY/Error0.
The new initializer wrapper retires only that known successful read after
physical-quiet and card-transfer checks. It covers cold startup, SD Info,
explicit reset, format and queued deletion. Unknown/error states stay blocked;
automatic recovery does not depend on a later job or host read. The 889 focused
caller checks pass; 85 retirement checks are reused for identical helper code.

The lower CE32 parser also needed explicit A7 routing for 8-byte GET/inspection
and 128-byte confirmation. It reuses the existing 129-byte command reservation,
adds no payload buffer, preserves legacy command lengths and masks rescue
sequences inside a binary confirmation. The 802 actual-parser/queue checks pass.

The corrected 8 KiB USB capture code (C9DF45FE) passed a 30-second two-probe 15 kHz,
closed-loop-off recording: no reported midstream NP packet gaps or host/raw
queue drops; 37,740 observed IMU rows all had valid AG/MAG data and no errors.
The established unavailable startup grid slot remains explicit. The final pair APP4A677F83/USB11646986 retains the checked capture path. The 03C0FADD durability, recording and BLE evidence is reused for the final readiness-only USB delta; its actual cold mode-cycle/Inspect check passed.

Native fault injection proves the defined sector persistence model. Physical
VEE rail interruption and whole-device USB power removal are separate tests.
GPIO requests alone do not measure rail voltage. No SD-controller internal
atomicity, all-card or endurance claim is inferred from these checks.

## Physical result and scope

Normal USB deletion removed only the earlier owned test tail (75 to 74). The checkpoint interruption removed an owned incomplete tail once (76 to 75); both recovered physical checkpoints matched independently reconstructed AfterCP and both journals were zero. The RAM sector captured at CP1 entry is old CP1; production control flow proves the preceding CP0 write/readback completed. A new recording used reclaimed LBA 132202496 and passed full production download/decode. Normal BLE 8-byte inspection / 128-byte confirmation and final owned-tail cleanup returned the card to 74 protected entries.

The initial live VEE pulse was reasserted by the writer; a later live-boundary helper caused an NP fault without establishing a sustained rail cut. Neither is power-loss evidence. The qualified physical interruption is the quiet delete-checkpoint VEE hold plus MCU reset. Native torn-write cases cover the defined sector-persistence model separately. Whole-device power loss, electrical 0 V and arbitrary controller behavior are not claimed.

Large generated neural payloads were removed after compact evidence was secured (1.55 GB reduced to about 795 KB of fixtures). Exact acceptance/restoration and compact HIL evidence are in [the release sources](../ReleaseSources/1.0.0.246/README.md).
