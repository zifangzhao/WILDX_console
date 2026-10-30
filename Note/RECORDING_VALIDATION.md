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

## Next development package: enforced IMU slots

2026-10-30. The user requires device-scheduled cached IMU snapshots in both
families, ordinary USB and BLE-controlled SD. The rate remains 1250 Hz: one
slot per 6/12/16/24 NP AP samples at 7.5/15/20/30 kHz. This integer relationship
does not make asynchronous sensor conversions synchronous. Acceptance needs
the actual bounded copy, first-source phase and explicit missed slots; a host
held-value grid is insufficient. Release199 above is still the published baseline.

| Development item | Current evidence and remaining work |
|---|---|
| Shared host control/phase writer | Frozen;765 focused and637 legacy checks passed. NSGC2/V1 SD declaration2 enforcement, retained phase, saved-NP overlap and final-tail coverage are distinguished. Included in development app202. |
| V2 ordinary USB | App201/USB6CB4B74B, three probes20kHz/60.413s:47176 NP blocks verified, reported NP loss/error counters zero;75482 observed grid slots with no observed missing slots/queue/scheduler/AUX sequence drops. Actual source-bound retained-grid phase passes; RTC-to-first-NP interval120us. Fresh USB status opened after Stop without reset. |
| V2 experimental USB |600.436s,469080 verified NP blocks,750528 observed grid slots; reported NP/grid drops zero. Diagnostic build only; unknown final tail and post-stop USB availability issue prevent a production claim. |
| V1 enforced USB grid | Diagnostics-free USB1E73A743, ordinary app204, two probes20kHz/60s:75057 observed IMU slots and one initial unavailable slot; no reported frame, counter, transport-packet or IMU queue errors. Retained-copy relative jitter bound42us; RTC-to-first-NP interval112us; retained saved-NP overlap verified. The foreground fine-clock guard race was reproduced and fixed;91 timer/control checks pass. Original flash/options/profile restored. |
| V1 enforced SD grid | Diagnostics-free APPFFF283D9, two probes20kHz/30s, session1793344023:960MiB full readback,37799 observed slots plus one initial unavailable slot, no reported midstream NP/grid faults; RTC-to-first-NP interval111us.48 earlier closed entries preserved; originals restored. App204's strict certified-startup fix passes806 focused and637 legacy checks. The same-session read-only204 export passed stored NP/RTC/retained-grid acceptance, bound42us. Saved-NP full overlap, final tail and physical sensor conversion phase remain unverified. PC calibration uncertainties are separate from these local intervals. |
| V2 SD storage | [SD16 contract](NP2_SD_GRID16_WIRE.md) schema2/NPSD4 and the checked SDK are frozen. r7/r8 startup failures were corrected; exact generated zero-committed tails were journal-removed while preserving the original328 entries. r10's one-RU restore/write serialization sustained21.9MB/s against36.4MB/s incoming and lost NP data. Prefetch/batching/BTC-lifetime and IRQ-flag fixes pass45 focused plus44 legacy scenarios. APP6CCF1248 then completed the extent in30.004s, mean3.97RUs/write, with full NP/IMU/RTC readback passing. A user-requested120s check saved93752NPblocks with all reported NP loss counters zero and local RTC interval121us. It also exposed4 accepted IMU rows not persisted at Stop:150000 observed rows, no listed queue/scheduler loss, explicit producer fault. This grid result remains failed. Finite/manual Stop preparation and final AUX acquisition ordering are being corrected before release. All331 current entries, including3 generated SD16 tests, are preserved. |
| Final recording acceptance | Reuse strict NP/RTC checks with the new enforcement/slot checker. One ordinary60s USB and30s BLE-SD capture per affected family, safe restore and explicit remaining limits. These are development checks, not release pre-flight. |

The finite/manual Stop tail correction is now frozen:222 focused native checks
and44 classic scenarios pass, and all seven source files were applied by exact
identity. V2 APP E20CDBF5 rebuilt all four affected translation units, including
the changed callback caller. Its120s hardware check again preserved93752 NP
blocks without reported NP loss, but saved150000 IMU rows and left3 accepted
rows unpersisted. The final AUX sector contained a full12-row packet: reserving
only one last sector cannot also save the partial tail. This candidate remains
unqualified. A capacity-bounded final carrier reservation is being developed;
it must retain NP progress through the existing ring/spill ownership.
Manual Stop seals the IMU producer before budgeting remaining AUX space while
a bounded NP drain may continue. Full terminal coverage is a separate result.

Do not label version1/legacy held output deterministic, infer completeness from
regular retained rows or release a failed candidate. Repeated cached values are
allowed; missing scheduled copies must remain distinguishable from them.

Development app202 compiled with stable inputs and reused the exact checked
first-sync-enabled SDK. Compilation200 found a GUI-only SD-test accounting
reference to fields absent from the BLE summary;201 fixes that boundary and
uses USB-verified physical counts when available. Both compilation numbers are
retained. Compilation202 additionally skips old independent IMU PC-fit exports
after Stop when the scheduled grid is selected; native journals and legacy
firmware paths are unchanged. The SDK's2093 managed method bodies and bytes are
identical after its fresh current-source build. The bounded V1 diagnostic is helper-enabled only, with no ordinary
raw retention;26 decoder/window component checks pass. Matched device recording
acceptance is still required before publication.

Compilation203 adds explicit NPSD4/schema2/catalog16 support with the checked
SD SDK. Compilation204 changes only the V1 certified-startup export decision and
reuses that exact SDK. Neither compilation is published. The V2 failed candidate
is not release-qualified; a software build or a different mode passing cannot
replace its SD acceptance. Corrected firmware builds must preserve each changed
translation unit's actual defines/forced headers and check the linked catalog,
pool and timer capabilities before hardware use.

Earlier V1 development images contained private stop/lifecycle/lifetime
instrumentation. They remain separate from the diagnostics-free production
components checked above. Compose only the checked components with the unchanged
checked boot; a whole-fused repeat is not an automatic additional gate. Initial
USB0x6E timeouts after programmer access cleared after a justified idle-state
reset; their underlying cause remains unknown, and that recovery is not a new
exporter defect or a general automatic-reset policy.

V2 ordinary USB's first retained IMU copy has a measured NP-relative offset
891..1006us and a101us enforced relative-copy jitter bound around the800us
schedule. This describes cached copies, not sensor conversion phase. The saved
NP overlap verdict remains false and final-tail coverage unknown. The first
helper rejected a V1-only selection key; a120-case checker-only fix and offline
reassessment of the same unchanged capture confirmed the real V2 settings and
runtime proof. No hardware recapture or neural payload rescan was performed.
Exact original V2 flash/options were restored and its service returned idle.

## Reduce validation time

- One owner completes authorized implementation/build/acceptance without coordinator approval round trips at each internal phase. Retain actual identity/destructive-action constraints and hardware ownership checks.
- Parallelize independent V1/V2 work. Reuse build caches, unchanged helpers and passed evidence. Repeat an audit only for a concrete unresolved finding.
- Reuse verified installation readback while the exact owner retains unchanged firmware provenance. Check fresh UID, runtime personality and quiescence at each relevant hardware handoff; do not repeat full-flash uploads before every control/read-only phase. A flash/identity change or concrete verification concern requires a new scoped check. If programmer access is followed by a USB timeout, establish the actual running state before a single justified recovery; a stale Windows device node is not mode proof.
- Validate one bounded new recording on the PC. Do not make firmware Stop or publication scan old sessions/the entire card.
- Record actual elapsed and queue time per step in the existing development result. Report the exact blocker and next checkpoint on an overrun; finish active flashing, write drains and restoration safely.
- Hand the accepted artifacts and evidence to packaging/publication. Do not repeat recording validation when asked to push those artifacts.


## Release204 independent update

The checked application204 and diagnostics-free V1 APPFFF283D9/USB1E73A743 are packaged with the unchanged published199 V2 family (APP194D9B94/USB74A9B98B/recovery6408DBB8). The V2 r14 finite60 observation passed, but its required120 repeat entered a full-FIFO/ring drain deadlock and is not qualified. No r14 image or that result is promoted. Partial-packet software development and safe recovery continue separately. Recording checks were reused; packaging performed one NSIS invocation and one57-file extraction identity comparison, without executing the installer.
