# Release artifact and publication checks

Revised 2026-10-29. Recording tests belong to [recording validation during development](RECORDING_VALIDATION.md). Complete them with the change and reuse their evidence here.

- [ ] Confirm the selected executable, SDK, firmware and catalog match recorded identities and development acceptance evidence. Keep an unqualified candidate local.
- [ ] For a new installer only: stage an explicit runtime/firmware allowlist, package once and compare one fresh extraction against the intended payload. Exclude repository legacy/debug/test files. Reuse this receipt for later push-only requests.
- [ ] Confirm version, checksum, intended staged paths and one current installer in the latest root; archive its predecessor recoverably.
- [ ] Push the intended branch and verify its remote commit.

No recording, flashing, new compilation or repeated recording validation is added here. An unrelated checked update can retain the previous published firmware for a family whose new candidate is still in development.

Targets: push-only 2 minutes, report an overrun at 5 minutes; packaging/publication 3-5 minutes, report an overrun at 10 minutes. Record actual elapsed/queue times in the release receipt. Finish an active write or restoration safely if a target is exceeded.

Release 1.0.0.189 is published at a52fc97129d0c42fe5a7b3768c959b2799effa5c; publication work remaining is zero. Its [52-file payload comparison](../ReleaseSources/1.0.0.189/package-verification.json) and [packaging receipt](../ReleaseSources/1.0.0.189/packaging-receipt.json) remain valid. Pending development validation is tracked only in the recording checklist.
