# Project delivery

Deliver the requested result and help the user understand the important decisions and limits.

- Follow [the release pre-flight checklist](Note/RELEASE_PREFLIGHT.md). Publication, packaging and engineering qualification are separate work scopes.
- A push-only request publishes the existing checked release: check its identity, intended staged paths and remote commit. Do not start new builds, tests, agents or hardware work to push it.
- A packaging request reuses checked binaries and recorded qualification. Stage an explicit runtime/firmware allowlist, package once and verify one fresh extraction. Do not recursively copy repository firmware archives into an installer.
- For changed software, use one incremental build per changed component and one relevant regression set. Increment the application version on every application compilation. Reuse passing evidence for unchanged bytes and conditions.
- New firmware qualification covers the affected modes and claims. A failed candidate stays local; retain the previous published firmware for that family when shipping an independent fix. Never relabel an untested mode as passed.
- Run independent V1 and V2 source, build and analysis work concurrently with separate outputs and owners. Keep shared interfaces under one integrator. Existing hardware leases and tool isolation still apply; never bypass a lock to meet a time budget.
- Within the user's authorized scope, one owner completes implementation, build and focused acceptance without repeated coordinator approvals between every internal phase. Escalate actual scope/identity changes, destructive actions or explicit approval constraints; do not invent new permission gates.
- Target push-only publication within 2 minutes (5-minute escalation threshold), and packaging/publication within 3–5 minutes (10-minute escalation threshold) once artifacts and evidence are ready. Record actual stage times in the release receipt. An overrun triggers a concise explanation and revised estimate, not more unrelated work.
- Never interrupt an active flash, accepted write drain or restoration to satisfy a time target. Finish the operation safely and report the overrun.
- Preserve unrelated workspace changes. Stage explicit release paths; keep one current installer in `LATEST_INSTALLERS` and retain older deliverables under `legacy`.
