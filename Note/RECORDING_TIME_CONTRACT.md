# Shared V1/V2 recording-time plan

2026-10-29. Implements the user's single-start-time direction for USB and BLE-controlled SD recordings. This is the shared development contract; it is not a claim that either new recording path has passed validation.

Current scope: the user paused new IMU-grid/buffer work, builds, tests and hardware. The separately requested source-only removal of optional sensor fine timestamps and synchronization of this plan may proceed. The grid below is deferred design, not authorization to resume it.

## Remove optional sensor fine timestamps

Remove the per-observation fine-timestamp producer, its calls and carrier/sidecar packing from new firmware. A default-off feature flag is insufficient for the user's revised request; do not keep a diagnostic switch that reactivates this mechanism. Preserve existing sensor core ABIs, sequence/validity/error fields, relative indices and sparse gaps. Keep historical files and readers intact.

Retain the independent one-time RTC/T0 and accepted PC-anchor proof, including the kind5 source-root record required by the existing wire contract. Removing sensor-observation timing does not remove the recording origin. No new grid, buffer allocation or recording-format extension is part of this source-removal slice.

## One recording origin

- `T0` is the first validated NP STARTBYTE of the lowest selected probe, mapped to the MCU hardware RTC. The RTC uses HSE-derived timekeeping; no silent LSI substitution qualifies this clock.
- Retain the existing `recording_start_utc.json`, `schema: 1`, `format: neuropixels.recording_start_utc`. Reuse `recording_start_unix_us`, `utc_valid`, `uncertainty_us`, `clock_reference`, and source/probe identity fields. A point and its uncertainty describe one origin; uncertainty bounds are not additional recording clocks.
- Store the accepted PC synchronization anchor once under `pc_sync_anchor`, with `schema: 1`, `transport`, `pc_time_sent_unix_us`, `device_rtc_at_apply_unix_us`, `calibration_generation`, `declared_uncertainty_us`, `value_semantics`. The PC value is the exact value carried by the accepted synchronization message. Use `value_semantics: payload_reference_utc` when that value refers to the payload's apply/reference instant rather than actual host send time. Unknown values stay null. Reuse the frozen UTC2/+5 mapping and the one-time RTC/+7 evidence where available; do not add periodic-anchor logging to implement this initial contract.
- BLE supplies the requested PC anchor. Preserve existing USB/legacy calibration where needed for compatibility, explicitly label its actual transport, and never invent BLE evidence. An unavailable BLE anchor must not become a new USB recording Start gate.
- Calibrate before recording. Freeze the selected anchor for the session; do not adjust the device clock during capture. PC/network error is separate from local RTC-to-NP uncertainty. Periodic in-recording calibration is outside this plan.

The current wire receipts already carry the one-time RTC/source context. Reuse them before adding any new wire layout. The existing kind5 final source-root attestation remains required for that wire contract: emit it after the NPSTART receipt pages and before RTC pages. Kind2 NPAR and kind3 NPHR are not replacements. Removing per-observation timestamps must not remove this one-time origin proof.

## Sample indices replace repeated absolute timestamps

For each file, store its layout, exact rational rate and genuine first source offset once. Preserve NP block sequence/source counters, per-probe fixed phase and sparse discontinuities. Derive time using:

`sample_time = T0 + first_source_offset + segment_offset + sample_index / sample_rate`

Do not assign new independent UTC clocks to each stream. Keep enough relative offset/index information to locate gaps and streams that actually begin later. A camera/audio or external GPIO source with an independent clock needs a demonstrated counter/edge relationship; a nominal rate alone cannot prove that relationship.

## Deferred IMU snapshot grid

- Target **1250 Hz**, matching the user's WILD-compatible request: one slot every 800 µs. NP rates 7.5/15/20/30 kHz contain respectively 6/12/16/24 NP samples per slot. A 250 Hz producer proposal does not replace this target.
- Sample the latest cached IMU values at a real HSE-derived slot boundary. Repeated or missed underlying BMX160 conversions are acceptable, as the user confirmed. This grid timestamps the cached snapshot, not physical sensor conversion.
- The slot handler performs bounded cached copies and bookkeeping only: no I2C, RTC/calendar reads, serialization, CRC scans or storage waits. NP acquisition keeps priority. Carry a slot ordinal and compact validity/repeat/missing information; no per-slot wall-clock timestamp.
- Align slot phase to the recording origin. If T0 becomes known after early slot instants, mark the unavailable prefix and start at its true ordinal; do not backfill values or label a block-finalization snapshot as a historical slot-edge sample. Store any genuine fixed phase offset once.
- Reuse existing buffers/carriers where possible. V1 has tight ordinary RAM headroom, so assess buffer ownership and worst-case slot batching at every supported NP rate before choosing packing. Do not take capacity from the NP capture pool without evidence that recording remains intact.
- Preserve the current fine-timer ownership. In particular, adding TIM5 compare interrupts would violate V2's present DIER/CCMR/CCER qualification. Choose an isolated HSE timer or explicitly update that owner's contract and targeted checks.
- Keep `analogin.dat`'s 16-word little-endian stride and current WILD-equivalent scaling. Relative indices and gap metadata determine timing; do not add new per-sample timestamp files. Review the legacy in-row local-time words before changing their interpretation.

One shared slot layout/capability declaration must be agreed before firmware adds a new format. The normal legacy 140/164/512-byte sensor cores and readers remain supported. A firmware version lacking a real fixed-slot producer must not be labeled as having one merely because the host can create a held-value display grid.

## Host output and compatibility

Normal exports use the shared origin plus file/rate/index/gap metadata. New recordings must not produce per-observation fine-timestamp archives from the removed mechanism. Retain historical readers for existing files. Any retained one-time recording-origin diagnostics are distinct from the removed sensor producer; preserve existing recordings and test evidence.

Both USB and SD exporters consume the same validated origin. Old firmware and old recordings retain their current explicitly labeled estimates/unknown timing. Neither Start-click time nor packet arrival time substitutes for missing device evidence.

## Parallel ownership and development acceptance

- V1 owner: authorized source-only removal of the optional observation timestamp mechanism, preserving one-time origin emission. Startup/APP validation and grid/timer/buffer work remain paused.
- V2 owner: coordinate the same removal and retained HSE/RTC origin contract. New grid/timer/buffer and carrier work remain paused.
- WILDX integrator: shared field semantics, normal export/diagnostic selection, index-based reconstruction and old-recording compatibility. Binary packing changes require this common agreement once, not separate incompatible V1/V2 versions.
- During development, validate the affected USB/SD recording paths using [the recording checklist](RECORDING_VALIDATION.md): NP continuity, actual grid cadence/slot gaps, no NP impact, correct one-time anchor and PC provenance. Reuse existing raw recordings for parser corrections. Publication reuses the completed evidence.
