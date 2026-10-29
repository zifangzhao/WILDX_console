# Shared V1/V2 recording-time plan

2026-10-29. Implements the user's single-start-time direction for USB and BLE-controlled SD recordings. This is the shared development contract; it is not a claim that either new recording path has passed validation.

Current scope: the user explicitly resumed V1 deterministic scheduling development and validation in the V1 chat. V1 can complete the authorized implementation and scoped validation work package with its existing device/data protections. V2 currently has a separate source-only sensor-timestamp removal request; V2 grid integration and hardware do not resume implicitly. Coordinate shared serialization before either implementation changes a binary format.

## Remove optional sensor fine timestamps

Remove the per-observation fine-timestamp producer, its calls and carrier/sidecar packing from new firmware. A default-off feature flag is insufficient for the user's revised request; do not keep a diagnostic switch that reactivates this mechanism. Preserve existing sensor core ABIs, sequence/validity/error fields, relative indices and sparse gaps. Keep historical files and readers intact.

Retain the independent one-time RTC/T0 and accepted PC-anchor proof, including the kind5 source-root record required by the existing wire contract. Removing sensor-observation timing does not remove the recording origin. No new grid, buffer allocation or recording-format extension is part of this source-removal slice.

## One recording origin

- With NP enabled, `T0` is the first validated NP STARTBYTE of the lowest selected probe, mapped to the MCU hardware RTC. Without NP, one actual selected primary producer start must define T0; do not invent an NP reference for camera-only recording. The RTC uses HSE-derived timekeeping; no silent LSI substitution qualifies this clock.
- Retain the existing `recording_start_utc.json`, `schema: 1`, `format: neuropixels.recording_start_utc`. Reuse `recording_start_unix_us`, `utc_valid`, `uncertainty_us`, `clock_reference`, and source/probe identity fields. A point and its uncertainty describe one origin; uncertainty bounds are not additional recording clocks.
- Store the accepted PC synchronization anchor once under `pc_sync_anchor`, with `schema: 1`, `transport`, `pc_time_sent_unix_us`, `device_rtc_at_apply_unix_us`, `calibration_generation`, `declared_uncertainty_us`, `value_semantics`. The PC value is the exact value carried by the accepted synchronization message. Use `value_semantics: payload_reference_utc` when that value refers to the payload's apply/reference instant rather than actual host send time. Unknown values stay null. Reuse the frozen UTC2/+5 mapping and the one-time RTC/+7 evidence where available; do not add periodic-anchor logging to implement this initial contract.
- BLE supplies the requested PC anchor. Preserve existing USB/legacy calibration where needed for compatibility, explicitly label its actual transport, and never invent BLE evidence. An unavailable BLE anchor must not become a new USB recording Start gate.
- Calibrate before recording. Freeze the selected anchor for the session; do not adjust the device clock during capture. PC/network error is separate from local RTC-to-NP uncertainty. Periodic in-recording calibration is outside this plan.

The current wire receipts already carry the one-time RTC/source context. Reuse them before adding any new wire layout. The existing kind5 final source-root attestation remains required for that wire contract: emit it after the NPSTART receipt pages and before RTC pages. Kind2 NPAR and kind3 NPHR are not replacements. Removing per-observation timestamps must not remove this one-time origin proof.

## Sample indices replace repeated absolute timestamps

For each file, store its layout, exact rational rate and genuine first source offset once. Preserve NP block sequence/source counters, per-probe fixed phase and sparse discontinuities. Derive time using:

`sample_time = T0 + first_source_offset + segment_offset + sample_index / sample_rate`

Do not assign new independent UTC clocks to each stream. Keep enough relative offset/index information to locate gaps and streams that actually begin later. A camera/audio or external GPIO source with an independent clock needs a demonstrated counter/edge relationship; a nominal rate alone cannot prove that relationship.

## IMU snapshot grid

- Target **1250 Hz**, matching the user's WILD-compatible request: one slot every 800 µs. NP rates 7.5/15/20/30 kHz contain respectively 6/12/16/24 NP samples per slot. A 250 Hz producer proposal does not replace this target.
- Sample the latest cached IMU values at a real HSE-derived slot boundary. Repeated or missed underlying BMX160 conversions are acceptable, as the user confirmed. This grid timestamps the cached snapshot, not physical sensor conversion.
- The slot handler performs bounded cached copies and bookkeeping only: no I2C, RTC/calendar reads, serialization, CRC scans or storage waits. NP acquisition keeps priority. Carry a slot ordinal and compact validity/repeat/missing information; no per-slot wall-clock timestamp.
- Align slot phase to the recording origin. If T0 becomes known after early slot instants, mark the unavailable prefix and start at its true ordinal; do not backfill values or label a block-finalization snapshot as a historical slot-edge sample. Store any genuine fixed phase offset once.
- Reuse existing buffers/carriers where possible. V1 has tight ordinary RAM headroom, so assess buffer ownership and worst-case slot batching at every supported NP rate before choosing packing. Do not take capacity from the NP capture pool without evidence that recording remains intact.
- Preserve the current fine-timer ownership. In particular, adding TIM5 compare interrupts would violate V2's present DIER/CCMR/CCER qualification. Choose an isolated HSE timer or explicitly update that owner's contract and targeted checks.
- Keep `analogin.dat`'s 16-word little-endian stride and current WILD-equivalent scaling. Relative indices and gap metadata determine timing; do not add new per-sample timestamp files. Review the legacy in-row local-time words before changing their interpretation.

One shared slot layout/capability declaration must be agreed before firmware adds a new format. The normal legacy 140/164/512-byte sensor cores and readers remain supported. A firmware version lacking a real fixed-slot producer must not be labeled as having one merely because the host can create a held-value display grid.

## Carrier and RAM decision checkpoint

There is no accepted new wire carrier yet. Internal slot bookkeeping is separate from the serialized wire format; developing a bounded queue does not establish timer accuracy, transport capacity or recording compatibility.

- V1's existing foreground snapshot queue entries occupy 2240 bytes, plus cache/bookkeeping, and are not ISR-safe as they stand. The proposed private 32-slot SPSC helper occupies 1824 bytes in native tests. Reuse requires explicitly mutually exclusive lifetimes; legacy consumers are active and cannot simply be overlaid. At 1250 Hz, 32 slots cover only 25.6 ms; overflow must retain missing ordinals and must not stall NP.
- V2's 116-byte SD sensor tail has only about 11.3 kB/s capacity at one probe/7.5 kHz. Thirteen 32-byte rows would require 416 bytes in a block, so the existing tail cannot be the sole raw-grid carrier. Retain existing sensor cores instead of silently repurposing their layout.
- V2's optional EP82 IMU path is a candidate batching transport, not an accepted combined NP/1250 Hz path. Its current default-off/20 Hz producer and earlier unresolved combined-transport issue need separate implementation evidence before use. The existing optional media SD sink excludes concurrent NP capture and is not a ready replacement.
- The shared inner encoding is now NSG1 v1 raw cached values, bounded to 12 slots/480 bytes. This codec is source-tested, independent of its transport. Do not add a 32–128 KiB ring merely because it was a sizing example. V2 transport fit is still open; its present sensor tail cannot carry this raw stream.
- V1 SD padding `[62144,65024)` is a proposed newly declared extension, not free usable space: active spill/bounce paths currently skip it. Both paths, interrupted-record recovery and the host's strict padding check require matched changes. For current NP format6 only, reserve header/footer marker/version at104/108; the chunk descriptor repeats them and stores exact occupied bytes at124. Keep unrelated bytes zero and recompute metadata CRC. Old exporters will reject the new nonzero padding; old recordings remain supported by the updated exporter.
- V1 USB can carry the inner codec in a distinct `NPGRID01` 506-byte body, but all four current metadata slots have existing consumers. Keep at least two ordinary packets, preserve pending timing/closed-loop data and measure the resulting capacity. A packet's byte capacity alone is not proof that replacing it preserves recording. Explicit idle grid-mode selection with safe legacy fallback is required before normal use; the existing exact recording-origin feature masks are unchanged.

### V1 control and wrapper reservation

This reservation is for V1 only; it does not claim those selectors are free in V2 or resume V2 implementation.

- USB vendor request `0x81`: `C0`, value/index0, length32 returns `NSGC`, u32 version1 at4, flags at8 (supported1, selected2, capture-frozen4), UID words at12/16/20, control revision at24, CRC32 over bytes0..27 at28. `40`, value0/1, index0, length0 selects legacy/NSG1 while actually idle. All other tuples are rejected. The existing `0x80` timing INFO feature selections remain unchanged.
- BLE V1 `NLD1` version1 operation10 selects mode0/1 in word4; words5..14 are zero, word3 is a nonzero transaction ID, CRC is at60. Use the existing matching `NLR1` receipt. STATUS flags reserve supported `0x20000000` and selected `0x40000000`; closed-loop `0x80000000` remains unchanged. Missing/unsupported read-only capability does not select grid. A lost mutation receipt is unknown and needs state reconciliation, not assumed failure or blind replay.
- Select only while idle without queued/scheduled capture, configuration, format, download or transfer ownership. Freeze mode on capture admission. Disconnect/reset handling must clear the appropriate transport's idle selection without erasing another transport's selection or changing a frozen capture. Advertise support only when the actual timer, codec and sink are ready. Unsupported peers retain legacy recording without a new general Start gate.
- USB grid body: `NPGRID01` at0..7, grid-record sequence u32 at8, firmware session at12, frame length at16, NSG1 frame at20, zeros through501, CRC over bytes0..501 at502. Maximum body frame ends at500. Dispatch before the legacy sensor parser. Maintain outer stream sequence across packet types and keep legacy ordinary-record sequence contiguous for its own records. Parent session/generation and source-window binding remain required.

These interfaces require a matched new host/writer and targeted development validation. They do not activate a grid in installer189 or establish compatibility with older exporters reading new grid records.

## Shared inner batch codec

`Neuropixels/Timing/np_imu_grid_wire.h` and `NpImuGridWire.cs` implement the same little-endian NSG1 v1 encoding. Maximum frame size is 480 bytes: a 48-byte header and up to twelve 36-byte rows. Rows contain nine signed i32 values in the existing SNS physical units (acceleration micro-g, gyro milli-dps, magnetometer nano-tesla).

Header: magic `NSG1` at0, version1 at4, encoding1 at5, header size48 at6, first slot u64 at8, slot count u16 at16, row bytes36 at18, observed/AG-valid/MAG-valid/AG-repeat/MAG-repeat/error u16 masks at20/22/24/26/28/30, payload bytes u32 at32, zero flags at36, nonzero capture generation at40, CRC32 at44. CRC covers bytes0..43 then payload48..end. Only observed rows occur in the payload, in ascending mask-bit order. Unknown mask bits, invalid subsets, overflow, wrong lengths and CRC failures are rejected. UID/token, parent generation and allowed NP source window remain carrier/importer admission checks; the codec cannot establish a clock origin by itself.

The pure codec passed 49 managed checks, 291 native checks and 34 shared vectors with exact byte agreement. No application/SDK build, producer integration, timer or hardware validation is implied. Transport declaration and carrier parsers are separate development work.

## Host output and compatibility

Normal exports use the shared origin plus file/rate/index/gap metadata. New recordings must not produce per-observation fine-timestamp archives from the removed mechanism. Retain historical readers for existing files. Any retained one-time recording-origin diagnostics are distinct from the removed sensor producer; preserve existing recordings and test evidence.

Both USB and SD exporters consume the same validated origin. Old firmware and old recordings retain their current explicitly labeled estimates/unknown timing. Neither Start-click time nor packet arrival time substitutes for missing device evidence.

## Parallel ownership and development acceptance

- V1 owner: resumed deterministic scheduler/queue development and scoped validation, plus startup/APP readiness and the retained one-time origin proof. Keep the optional observation timestamp mechanism removed.
- V2 owner: source-only sensor timestamp removal and retained HSE/RTC origin compatibility. Shared format review may continue; V2 grid implementation and hardware need their own resumed scope.
- WILDX integrator: shared field semantics, normal export/diagnostic selection, index-based reconstruction and old-recording compatibility. Binary packing changes require this common agreement once, not separate incompatible V1/V2 versions.
- During development, validate the affected USB/SD recording paths using [the recording checklist](RECORDING_VALIDATION.md): NP continuity, actual grid cadence/slot gaps, no NP impact, correct one-time anchor and PC provenance. Reuse existing raw recordings for parser corrections. Publication reuses the completed evidence.
