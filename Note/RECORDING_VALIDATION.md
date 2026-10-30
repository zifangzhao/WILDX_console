# Recording validation during development

Revised 2026-10-29. The developer runs this checklist with changes to recording, clocks, sensors, storage or transport. This is development acceptance. UI-only changes use relevant software/control checks without an incidental hardware recording.

Select affected families, modes and claims once. Complete implementation, one incremental build, focused checks and applicable bounded recordings as one authorized work package. Reuse evidence for unchanged bytes and conditions. V1 and V2 source/build/analysis run concurrently with separate owners/outputs; hardware retains existing leases and isolation.

## Recording checklist

| Check | Required evidence | Execution budget when needed |
|---|---|---:|
| [ ] Build and focused checks | One build per changed component; relevant production control/decoder/ownership checks pass. Reuse unchanged helpers; run the actual embedded self-test after a helper lifecycle change. | 3–5 min per affected build/check work package |
| [ ] Device and setup | Exact device/firmware/profile, idle state, streams and safe restore inputs recorded. Existing data/settings preserved. Measure configuration and Start delays separately. | 2–4 min, depending on backup/flashing needs |
| [ ] USB recording | Bounded capture after first data; save NP continuity, expected channel layout, sample counts, sizes and loss counters. Include V2 three probes/20 kHz for the current work. | 60 s capture |
| [ ] BLE-controlled SD recording | Start is device-confirmed; NP/IMU are saved; Stop commits accepted writes/footer/catalog; recording can be listed and downloaded. Preserve incomplete recordings for recovery. | 30 s capture |
| [ ] NP and IMU data | PC validates the bounded output. Check NP continuity and independent IMU validity/cadence; IMU must not add NP losses. Distinguish the 1250 Hz held-value grid from physical sensor conversion rate/phase. | 3–5 min readback/analysis |
| [ ] First sample and stream alignment | Follow the [single-origin contract](RECORDING_TIME_CONTRACT.md): one HSE-derived RTC T0 for NP STARTBYTE, one accepted PC sync anchor, exact rates/relative offsets and slot/sample indices. Check the <1 ms local interval separately from PC uncertainty. Do not require separate stream UTC timestamps or infer physical sensor conversion lock. | 1–2 min analysis; new physical experiments budgeted separately |
| [ ] Stop/restart and metadata | Confirm Stop/drain and affected interruption/restart behavior. Saved timestamps, probe IDs/IMRO and calibration-applied metadata match the data. No automatic full on-device payload scan after Stop. | Reuse the captures above; 1–2 min for an affected restart/metadata check |
| [ ] Legacy and preview behavior | Relevant old-firmware fallbacks pass. Preview failure cannot stop/degrade recording; NP and IMU retain priority. Physically test preview when affected. | Reuse focused software evidence; state any physical test budget |
| [ ] Safe finish and handoff | Required restoration/idle/data-preservation checks complete. One short result records artifacts, actual elapsed/queue times, passed checks and remaining limits. | 2–4 min when restoration is needed |

The 60/30-second captures are baseline development samples, not endurance proof. Extend only for a specific defect, required observation interval or explicit qualification. Do not run every profile or rescan old recordings on every change. Omit a mode only when the change does not affect it, recording the reason.

A relevant failure sends the change back to development. Software checks or another mode passing do not establish that the failing mode works. Missing fine timing in old firmware remains explicitly unknown and must not become a new GUI recording Start gate.

## Current development result

2026-10-29. WILD-X1.0.0.199 passed the four scoped recording checks below. Exact independent reviews and source/component identities are retained in [the release validation summary](../ReleaseSources/1.0.0.199/validation-summary.json). These development results are reused during publication.

| Check | Result |
|---|---|
| V1 USB, two probes20kHz/60s | RTC-to-first-NP full interval111us; closed AP/LFP and native/grid IMU checks passed; no reported NP continuity faults |
| V1 BLE-to-SD, two probes20kHz/30s | Full960MiB readback passed; local interval111us;46prior recordings preserved |
| V2 ordinary USB, three probes20kHz/60s | Local interval120us;47,196 NP blocks verified with no reported drops; ordinary IMU snapshots saved |
| V2 BLE-to-SD, three probes20kHz/30s | Full1.09GB readback passed; local interval121us; NP loss counters zero and5,741 valid IMU observations |
| Build/package reuse | One combined199 app/SDK build; checked189 companion runtimes reused; one successful NSIS invocation and one fresh56-file payload comparison |

The local intervals are separate from PC/network synchronization uncertainty. V1 has one initial unavailable grid slot and no later observed grid gap in these captures; full phase and final-tail coverage remain unknown. V1 SD has a valid global T0, while canonical per-stream source_tick remains null. The ordinary V2 USB path uses sparse native observations and a held-value compatibility grid, not lossless200Hz.

Further grid/headroom and long-capture experiments are separate, default-off development work; they are not release pre-flight or an all-mode/endurance claim. V2 optional catalog INSPECT failed; normal listing/download/export passed. Exact original firmware/options and saved idle/profile restoration proofs passed. The composed fused images use exact checked components and were not newly flashed as whole images.

## Reduce validation time

- One owner completes authorized implementation/build/acceptance without coordinator approval round trips at each internal phase. Retain actual identity/destructive-action constraints and hardware ownership checks.
- Parallelize independent V1/V2 work. Reuse build caches, unchanged helpers and passed evidence. Repeat an audit only for a concrete unresolved finding.
- Validate one bounded new recording on the PC. Do not make firmware Stop or publication scan old sessions/the entire card.
- Record actual elapsed and queue time per step in the existing development result. Report the exact blocker and next checkpoint on an overrun; finish active flashing, write drains and restoration safely.
- Hand the accepted artifacts and evidence to packaging/publication. Do not repeat recording validation when asked to push those artifacts.

