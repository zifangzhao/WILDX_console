# V2 grid append storage contract

Integrator allocation, 2026-10-30. This is a development contract, not qualification
or publication of firmware. Existing NPSD versions 1–4 and ordered SD16 remain
unchanged. No RAM pool, clock, neural sample layout, NSG1/NSGP body, or USB
streaming interface is enlarged by this contract.

## Purpose

A full external-memory backlog makes an ordered writer spill new data while
restoring older data. The current 3-probe/20 kHz carrier is about 36.4 MB/s;
putting every byte through both external-memory directions would require
72.8 MB/s, before overhead. The current 128 MHz quad SDR interface has an
ideal data-only limit of 64 MB/s. This does not describe normal traffic or
prove the cause of a particular loss; it identifies an unsustainable recovery
state. Real 120-second tests still reported NP loss.

The append mode can write available incoming SRAM owners and an older restored
FIFO owner in the same bounded command. It keeps physical writes sequential,
preserves each neural record's original logical sequence/source counter, and
lets the PC reconstruct logical order. It does not promise lossless capture
through arbitrary card stalls. NP saving remains best effort, and actual gaps
remain explicit. Artificial injected-delay tests are not an acceptance gate.

## Explicit negotiation

- Allocate NPSD wrapper version **5**, family 2 only, for this session mode.
  Keep the existing 64-byte control frame, opcodes, request IDs, UID/token
  ownership, CRC, RecordFeatures 25, and continuous64 mask 29.
- Probe version 5 CAPS once during connection warm-up, after a valid version 4
  reply. Only a matching version 5 reply permits version 5 START/STATUS.
- Versions 1–4 keep their exact existing semantics. Unsupported old firmware
  retains its already confirmed path. Start performs no capability probe,
  mutation replay, or replacement Start after an uncertain outcome.
- Version 4 still selects ordered SD16. Version 5 selects SD16 schema 3,
  order 1. Family 1 rejects version 5. Ordinary USB NSGC2 is unchanged.
- Advertise version 5 only when the writer, AUX ownership, metadata, catalog
  recovery, and manual/finite Stop paths implement this contract.

## Physical storage and original neural identity

Keep whole physical groups `[four 46592-byte NN records][K AUX sectors]`.
The existing profile-specific K table and stride `364 + K` are unchanged.
All four NN records within one group have their original consecutive logical
IDs, beginning at an ID divisible by four. Different physical groups may have
nonconsecutive logical IDs, including groups within the same CMD25 command.

Each accepted command latches every group's original logical ID, SRAM slot,
generation and AUX owner. DMA retirement checks these immutable identities,
not a physical-count-derived logical sequence. Physical DATAEND and card-ready
durability remain distinct. Return source buffers only through the existing
validated lifetime rules. Commit metadata only for whole card-confirmed groups.

Reuse the existing four-group command limit and payload pools. Prefer incoming
direct SRAM owners without starving the oldest external FIFO owner. Keep a real
oldest-FIFO restore destination, prefetch only when real credits allow it, and
include an available oldest restored owner in a bounded command alongside
incoming owners. Do not reserve imaginary future DMA credits, delay NP for AUX,
or wait for an ideal batch while work is ready. The precise work-conserving
policy is part of firmware acceptance, not an inferred bandwidth guarantee.

## Descriptor and footer

Keep format at offset 8 equal to 16 and the full-sector CRC at 508. Preserve the
existing base geometry, profile, CID, reservation, closed-loop and timing fields.
Descriptor/footer bytes 208–255 still match exactly, with:

| Offset | Schema 3 meaning |
|---:|---|
| 208–215 | `N2GRP016` |
| 216 u32 | 3 |
| 220 u32 | Existing base reservation/AU geometry |
| 224 u32 | 1: sequential physical append, indexed logical NN order |
| 228–248 | Existing K, stride, four NN/group, wrapper 1, enforced grid 2, 1250 Hz |
| 252 u32 | Reserved zero |

Descriptor bytes 256–507 remain zero. Footer schema 2 fields through 407 retain
their exact meaning, including the optional IMU retirement-tail counts;
footer bytes 404–407 remain zero.

| Footer offset | Meaning |
|---:|---|
| 108 u32 | N: durable physical NN record count |
| 112 u32 | G: durable physical group count, N/4 |
| 116 u32 | Physical committed end LBA |
| 120 u64 | N × profile ticks: stored tick quantity, not elapsed span |
| 256/264 u64 | G × stride physical sectors / G × K AUX positions |
| 408 u32 | P: gap-free committed logical NN prefix |
| 412 u32 | H: exclusive largest stored logical NN ID |
| 416 u32 | A: completed NN records admitted by finish_block when known; UINT32_MAX otherwise |
| 420 u32 | Bit 0: A known; all other bits zero |
| 424–507 | Reserved zero |

Require P <= N <= H <= reservation NN capacity. P, N and H are whole four-record
groups. H is zero exactly when N is zero. If A is known, require H <= A <=
capacity; A may include an unsealed partial group on failure. If A is unknown,
its sentinel is mandatory. A recovered physical prefix does not prove A.

A COMPLETE footer requires A known, P = N = H = A > 0, all admitted NN owners
drained, and normal existing completion proofs. This is logical coverage of
admitted records; source acquisition gaps are separate. An incomplete footer
seals only the durable physical prefix, never the reservation end. An indexed
high-water is not a committed prefix.

Maintain P using bounded live-owner/FIFO state, and H from original IDs at
durable commit; no session-sized firmware bitmap or post-record payload scan.
Catalog recovery validates sealed metadata and bounds without a new automatic
full NN validation scan.

## AUX ownership and ordering

Preserve original NPGRID02 bodies, CRCs, generation and logical NN parent ID.
The parent is the selected group's final logical NN ID, not its physical
position and not a measurement of IMU conversion time. Never rewrite it while
sorting neural data.

Claim NSG batches only for the next immutable physical write plan, in physical
admission order. A physical ticket is measured in NN-record units: ticket 4g
identifies physical group g. A stage's key is the physical ticket; its original
wrapper parent remains the selected logical NN ID plus three. Validate both
identities. Never retag an already claimed stage.

Freeze the pending NN selection and physical tickets from the first queue claim
through any later acquisition/plan retry. No younger ticket may launch while an
earlier claimed ticket remains pending. Retain or explicitly account for failed
partial claims; a release alone is not queue rollback or durable commit. Commit
tickets only after the whole command's card confirmation. Source Stop and
partial-tail staging use physical tickets/admitted counts, not a logical prefix.

This contract guarantees nonoverlapping increasing real NSG FirstSlot ranges in
physical AUX order. Empty sectors advance no slot. Neural logical sorting must
never reorder whole NN+AUX groups as a single time stream. Existing grid quality,
native conversion-phase, retained source overlap and terminal coverage limits
remain separate. Bad optional AUX does not invalidate intact NN data.

## PC reconstruction and incomplete recordings

Reuse format 14's header-index algorithm, generalized to the fixed SD16 stride.
Validate original NN headers, profile/provenance, aligned consecutive IDs within
each RU, uniqueness, geometry and declared P/H. An index is metadata only; retain
no neural raw file or session-sized neural payload buffer. Current NN headers
are not independently session-token-bound and current CRC-disabled payloads do
not become cryptographically verified by indexing.

Read neural RUs in logical order through existing per-probe decoding,
calibration, hash and merged/unmerged DAT sinks. Process AUX independently in
its real slot order and preserve original wrapper associations. Feed AUX during
the physical index pass, then skip it during logical NN decoding. Validate the
monotonic physical AUX guarantee; a violation is an optional IMU-quality error,
not permission to sort, relabel or silently qualify the samples.

For incomplete recordings with logical storage holes, add an explicit offline
segment path. Preserve original NN IDs/source ticks and profile-specific tick
length; reset partial-frame state across storage gaps and find genuine framing
again. Append retained decoded segments into the selected DAT layout and write
segment/sample-offset metadata. Distinguish storage holes from declared source
acquisition gaps. Do not renumber headers, fabricate capture-loss flags, invent
samples, or discard genuine later segments solely because an earlier group is
missing. Missing first-source evidence stays unavailable.

Expose indexing and decoding progress/ETA as separate stages. Do not display
overall completion while indexing, export, merging or finalization remains.
Record measured performance; no download-speed gain is claimed by this change.

## Development acceptance

Freeze shared fields with both owners before implementation. Independently
implement firmware and SDK/host against golden binary fixtures. Cover ordered
legacy byte preservation, indexed complete data, physical order 0/2/1, duplicate
IDs, partial holes, all probe/rate geometries, AUX physical versus logical order,
closed-loop/timing preservation, finite/manual Stop and safe old-client fallback.

Use one incremental build per changed component and increment the app version
on each app compilation. Then run the affected bounded real recordings with
NP/IMU/RTC and stored-data checks. Preserve actual gaps, retained data and
unqualified limits. A failed candidate stays local; packaging reuses accepted
evidence and keeps the prior published family if this work is not qualified.
