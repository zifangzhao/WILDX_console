# Release pre-flight checklist

Revised 2026-10-29. This is the operative WILDX release checklist. It applies to publication work; engineering qualification has its own scope and timer.

## Select the work scope once

| Request | Work admitted | Elapsed target / escalation threshold |
|---|---|---|
| Push an existing release | Check recorded identities, stage intended files, push and confirm remote commit | 2 min / 5 min |
| Package and push checked artifacts | Runtime staging, one installer build, one extraction comparison, release metadata and push | 3–5 min / 10 min |
| Change software | One incremental build and one focused regression for affected behavior, then the packaging checklist | Build/check target set before starting; report after 5 min without progress |
| Qualify changed firmware | One bounded plan covering affected modes, recording integrity, IMU and the particular timing claim | Separate engineering budget; does not hold an unrelated maintenance release |

The publication timer starts when the selected binaries and their required acceptance evidence are ready. Also record time waiting for missing inputs so that qualification delays remain visible. Do not hide engineering time inside the publication timer.

## Current publication checklist: 1.0.0.189

The release is already on `main`, commit `a52fc97129d0c42fe5a7b3768c959b2799effa5c`. **Publication time remaining: 0 minutes.** Budgets below are targets for a subsequent packaging run, not measured durations of release 189.

| Done | Step and acceptance | Owner / concurrency | Next-run budget | Current time remaining |
|---|---|---|---:|---:|
| [x] | Pin EXE/SDK versions and hashes, selected firmware identities, and the recorded acceptance scope. Use the previous published family bundle when its new candidate has not passed. | Integrator; V1/V2 owners can confirm their own bundles concurrently | 10 s | 0 |
| [x] | Stage only the explicit NSIS runtime files, current catalog-referenced firmware and necessary support files. Reject `legacy`, debug files, test outputs and unrelated images **before** NSIS. Check current and previous firmware identities remain in the catalog. | Integrator; can overlap source archival | 20 s | 0 |
| [x] | Reuse the immutable source snapshot/build receipt and package documents. Hash once; do not reconstruct source or repeat delegated audits. | Packaging owner; parallel with staging | 15 s | 0 |
| [x] | Build the installer once from the checked payload. Do not recompile the console or firmware during packaging. | Packaging owner | 30 s | 0 |
| [x] | Extract once into a new, empty directory. Compare every runtime path, size and SHA256 against the intended payload; reject extras. Preserve the receipt for later push-only requests. | Packaging owner | 30 s | 0 |
| [x] | Record installer SHA, exact catalog, source receipt and tested limits. Keep one current installer in the latest root; archive its predecessor recoverably. | Integrator | 15 s | 0 |
| [x] | Inspect only intended staged paths, commit/push the existing branch and verify that the remote revision equals the release commit. | Integrator | 60 s | 0 |

Nominal serial budget is 180 seconds; source archival can overlap staging. Allow 3–5 minutes for tool and network overhead. At 10 minutes, report the exact unfinished step and revised estimate. Do not silently expand the task or waive a failed identity check.

Release 189 verified 52 payload files with zero unexpected files. GUI and firmware were not rebuilt during packaging; the installer was not executed. Evidence: [package verification](../ReleaseSources/1.0.0.189/package-verification.json), [packaging receipt](../ReleaseSources/1.0.0.189/packaging-receipt.json), [qualification summary](../ReleaseSources/1.0.0.189/validation-summary.json).

## Engineering still pending, separate from publication

These are planning estimates, not measured completion promises. No new hardware phase is started by revising this checklist.

| Work item | Current state | Estimated remaining execution / next checkpoint | Release effect |
|---|---|---|---|
| V2 USB test helper | Narrow cleanup-fixture synchronization fix is source-ready; actual embedded self-test has not run | 3–5 min for one helper build and its embedded self-test | Needed before the next USB hardware run; release 189 remains published |
| V2 USB NP + IMU + RTC | 3 probes, 20 kHz, 60-second capture plan prepared; not started | 12–20 min after helper acceptance, including setup, capture, bounded analysis and safe restoration; estimate assumes normal device startup | USB acquisition with the R6 pair remains unqualified |
| V1 RTC startup diagnosis | Candidate normal USB did not appear after ROM GO; original firmware/settings/catalog restored and USB responds | Completion ETA unknown. First diagnosis checkpoint: 20 min. Report the finding or next discriminating check at that point | Keep published B1758369/4F7B521A; exclude the failed candidate |
| V1 USB + SD RTC qualification | Waiting for a working candidate startup path | 20–30 min after the fix is ready, assuming normal backup/activation/restoration; defect investigation excluded | Needed before claiming new V1 RTC behavior |
| V2 SD NP + IMU + RTC | Completed: 30 s, three probes/20 kHz; neural/IMU readback passed; local RTC-to-NP interval 120 µs full width | 0 | Reuse this exact-pair evidence |
| Remaining enabled-stream alignment | Camera/audio/ADC/external-GPIO physical timing coverage is incomplete | No credible completion ETA yet; define one bounded experiment per affected stream | No all-stream alignment claim in release 189 |

V1 and V2 source/build/analysis work proceed concurrently. Hardware phases use the existing leases and isolated tools. Independent-device overlap requires tools that support it; this process revision does not disable a shared lock. An owner returns one short handoff with changed files, evidence and remaining limits. The integrator does not repeat a completed audit without a concrete unresolved finding.

## Reduce time at the actual bottlenecks

1. **Stop adding qualification to publication.** A push reuses the frozen release and existing extraction receipt. Packaging does not open devices, flash firmware or start recordings. Missing acceptance blocks the affected candidate, not unrelated checked updates.
2. **Remove internal approval round trips.** For work already authorized by the user, the owner performs its source checks, incremental build and targeted acceptance as one work package. Keep actual destructive-action/identity constraints and hardware ownership checks.
3. **Build and test only what changed.** Reuse dependencies and passing receipts. Run the full suite or endurance tests for relevant capture/storage/transport changes, a failing targeted check or explicit qualification. Do not rerun them for release notes or packaging changes.
4. **Use a firmware allowlist before packaging.** Release 189's first private package included repository legacy firmware, forcing a second pass. A clean staging list prevents that repeat. Keep repository history without shipping it.
5. **Time every stage once.** Record `started_utc`, `elapsed_seconds`, `queue_wait_seconds`, `result` and `evidence` in the existing release receipt. Distinguish observed duration from a budget. Avoid creating a separate report or new approval cycle for each stage.
6. **Do not repeat large data scans.** Firmware Stop commits accepted data and metadata without a full post-recording payload validation scan. Qualification may validate one bounded test recording on the PC; publication reuses that result.
7. **Use deadlines to report, not to damage state.** On a budget overrun, stop admitting new work and report the blocker. Finish an active flash, write drain or restoration safely. Do not kill it, replay an uncertain command, skip a comparison or describe a timeout as a pass.

Exact per-step durations were not recorded for release 189. File timestamps suggest about 22 seconds for the rejected NSIS log write window and 3.6 seconds for the final one; these are filesystem proxies, not stopwatch measurements. Extraction reused a log path, so its duration cannot be recovered reliably. The next release must record actual stage timings; no precise historical timing is claimed here.
