# V2 SD16 deterministic IMU carrier

Integrator allocation, 2026-10-30. This freezes the producer/reader contract;
it does not qualify a firmware build or authorize publication. Versions11–15
keep their exact existing layouts and readers. NSG1/NSGP bodies and CRCs stay
unchanged. All integer fields are little-endian.

SD16 is an explicit per-session opt-in through NPSD wrapper version4, family2
only. This allocation uses the existing64-byte layout, operations, payload,
token/UID binding and CRC; source RecordFeatures remains25 and the existing
continuous64 capability mask remains29. Version4 itself selects the new carrier.
Versions1..3 retain their exact bytes, clock semantics and legacy storage
formats; feature25 alone must never select16. No new BLE opcode is allocated.

The new client probes version4 CAPS once during connection warm-up, after valid
version3 capabilities. Only a successful matching version4 reply permits a
version4 START/STATUS. Unsupported old firmware retains the cached version3
path. Start does not probe, replay or automatically send a legacy replacement
after an attempted version4 START. Version4 is rejected for family1.

An admitted SD16 NP session owns and starts its grid automatically; no preview
selection is needed. USB retains its separately negotiated NSGC2 consumer. SD
must attach its real first-source context, drain or account for queued slots on
Stop and retire that context before restart. A genuine optional timing failure
must remain explicitly unavailable rather than fabricating samples or blocking
NP merely to meet the timing claim.

## Fixed physical groups

Version16 uses ordered groups of four unchanged46592-byte NP records (364
sectors), then exactlyK512-byte NPGRID02 sectors. NP sequences in groupg are
4g through4g+3. No append-order bit64 or implicit in-data alignment padding.

| AP Hz | K: one / two / three probes |
|---:|---|
|7500|5 /3 /2|
|15000|3 /2 /1|
|20000|2 /1 /1|
|30000|2 /1 /1|

K is capacity, not a losslessness guarantee. Pack actual cached-copy slots;
never backfill values or delay NP to wait for AUX. Bounded staging overflow
leaves missing ordinals/drop counts. IRQ work stays cached copy/bookkeeping.
The1250Hz grid divides NP rates by6/12/16/24 respectively.

NPGRID02 retains wrapper version1, generation@12, preceding NP sequence@16,
inner length@24, reserved0@26, headerCRC@28 and payload@32. Each sector's
preceding sequence must equal4g+3. A data sector contains one unchanged NSG1
batch. Only SD16 additionally admits an empty sector: length0, valid same
wrapper/context/headerCRC, reserved0 and all bytes32..511 zero. Empty sectors
advance no sample ordinal. This exception does not extend USB or NSG1 parsing.
The once-only NSGP lives in the footer, consuming noK sector.

## Metadata

Keep N2SDLOG1/N2SDEND1 and their full-sector CRC@508 over bytes0..507.
Version@8 is16. Descriptor flags@96 are15/31/47 for base geometry11/12/13,
optionally OR128 for closed-loop metadata. Other flags are unsupported.

| Offset | Both descriptor and footer |
|---:|---|
|208..215|ASCII `N2GRP016`|
|216 u32|Extension schema2 for new producers; schema1 remains readable as the earlier prototype|
|220 u32|Base reservation/AU geometry11,12 or13|
|224 u32|Order0: sequential fixed groups|
|228 u32|K from the exact profile table|
|232 u32|Group sectors364+K|
|236 u32|NP records/group4|
|240 u32|NPGRID02 wrapper version1|
|244 u32|Enforced schedule contract2|
|248 u32|Grid rate1250Hz|
|252 u32|Reserved0|

Compare descriptor/footer bytes8..99 and208..255 exactly. Descriptor256..507
is zero. Footer dynamic fields are:

| Offset | Footer meaning |
|---:|---|
|256 u64|Committed physical sectors|
|264 u64|Committed AUX sector positions, including empty positions|
|272 u32|Grid flags:1 final-known,2 saturated drop counters,4 actual grid fault; other bits0|
|276/280 u32|Queue-drop / scheduler-miss counts|
|284 u32|Nonempty NSG1-sector count|
|288..371|One unchanged84-byte NSGP, or allzero if unavailable|
|372 u32|Empty-sector count|
|376 u64|Final exclusive slot ordinal; UINT64_MAX when unknown|
|384 u64|Observed slots committed in the nonempty sectors|
|392 u64, schema2|Known accepted IMU rows not persisted at retirement, a lower bound; not queue overflow or scheduler misses|
|400 u32, schema2|1 if a pending record's additional row count is unknown; otherwise0|
|404 u32, schema2|Reserved0|
|408..507, schema2|Reserved0|

Schema1 requires392..507 zero and does not report retirement-tail counts. For
schema2, zero known rows plus unknown-pending1 is valid. These values are grid
delivery quality, independent of the physical committed tuple, retained phase
and final scheduled ordinal. A normal bounded tail discard is not relabeled as
queue overflow. Invalid optional tail details unqualify their coverage claim;
they do not invalidate intact NP payload. New producers use schema2 explicitly
rather than silently writing into schema1 reserved bytes.

Final-known and final-exclusive sentinel must agree. Saturation flag2 means
one or both32-bit drop counters equalUINT32_MAX. An unknown cutoff alone is
not grid fault4. Data/empty counts and observed/drop/phase metadata describe
grid quality; inconsistency disables its qualification rather than changing
NP geometry or guessed stream positions.

Let N=footer108 (NP records), G=footer112 (groups). Require N%4=0 and G=N/4.
Footer256=G*(364+K); footer264=G*K; footer116=payloadStart+footer256; footer120
remains N*profileTicks. Validate all arithmetic, physical end, reservation,
card bounds, session/profile/full CID and CRC before reading payload. Only
whole card-confirmed groups advance the committed tuple; DMA BTC/source reuse
alone is not card confirmation. A partial final group stays uncommitted.

Plan the reservation with stride364+K, including all AUX positions. Descriptor
limit@64=payloadStart+maximumGroups*(364+K), with base-AU/16384 reservation-end
rounding@68. Where the existing single/triple typed configuration is present,
its max-NP-blocks@44=4*maximumGroups. Preserve the existing dual-probe path with
no typed configuration sector; its capacity is proved by the descriptor's
stride/reservation. Do not invent a dual configuration format or reconstruct
capacity through old91-sector-per-NP formulas. No on-device post-record
payload validation is needed.

## Existing closed-loop and timing data

Descriptor104..127 is zero; AU@128 follows the base geometry. With CL bit128,
descriptor132/136/140 retain configCRC/baseGeometry/CL marker; otherwise zero.
Descriptor144..207 is zero. Footer132..143 is zero; footer144..207 retains the
existing64-byte CL summary only when bit128 is set. Preserve configuration
1888..2047 and NP header event bytes184..255. Validate CL directly under16;
do not pass16 through the old15 whole-metadata normalizer.

NSGP and NSG1 need the independently validated +5/+6 recording context and
one real NP STARTBYTE/root binding. Missing/bad optional phase or AUX body
does not invalidate intact NP records. Because AUX positions are fixed, skip
exactly512 bytes for a bad AUX sector and report its grid loss; never magic
scan NP payload. A corrupt whole footer remains critical metadata corruption.
Retained-grid phase, overlap with saved NP and terminal coverage are separate.
One root analogin.dat owner is selected; native/environment data stays saved.

## Safe development rollback

Before writing a test16 session, extend the actual strict catalog descriptor/
footer dispatch and focused BLE op11 DeleteLast fixture. Do not bypass recovery.
Bind cleanup to the sole owned completed last session, fresh card/CID/token/
count/ordinal/start/end/cookie and stopped owners. Prove journal/directory-only
writes and unchanged prior entries/frontier, then remove only that test entry
before restoring an old APP. If cleanup is uncertain, retain candidate recovery
firmware/data. No format, guessed rewind or earlier-session deletion.
