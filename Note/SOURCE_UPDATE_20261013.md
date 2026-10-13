# WILDX source update — 2026-10-13

This update publishes the completed 1.0.0.11 application source, tests and documentation. The existing published 1.0.0.9 installer and its firmware manifest remain the release package. The local display-filter-only 1.0.0.11 installer predates these changes and is not promoted here.

The source includes the shared V1/V2 recording, probe configuration and diagnostics workflow; one BLE connection with accessible tabs and automatic generation detection; factory CSV calibration online/offline; recording bundles with probe files, exact configuration bytes, calibration and timing/error reports; v11/v12 SD export and supported dual/triple profiles; display filtering; and segmented USB recovery that preserves gaps.

Probe admission retains the checked full-verification and unused-shank degraded receipts. Fatal setup/identity/I2C errors remain failures, and reports prioritize the actual fatal probe/stage over older comparator warnings. A separate in-progress policy for unverified selected-shank receipts is excluded from this source snapshot.

## Software evidence

The completed application build `recording_bundle_20261013` has version 1.0.0.11, executable SHA256 `3BDDAB281874022B4049A38A261B7977E5EDE0B08FE6688BFF4B5B11876BD190` and companion SHA256 `C5D398F5839F647B20291A97F809809E214624DA86F6A692CE9A1108B3FEB654`. The app build completed without warnings or errors. Hardware access and installer generation were disabled.

| Check | Passed assertions |
| --- | ---: |
| Recording bundles | 193 |
| Recording settings | 102 |
| Shared workflow | 88 |
| BLE features | 1,433 |
| Standalone regression | 294 |
| SD export/decoder suite | 2,584,820 |
| Neuroscope XML | 107 |
| BLE configuration receipts | 32 |
| Merge metadata | 40 |
| USB lifecycle | 134 |
| Probe error/degraded-chain matrix | 1,131 |

The probe matrix covers all 256 receipt-byte values on all three ports, ID/CRC/model/state/error mismatches, exact probe/shank warnings, fatal-context precedence and BLE unconfirmed/degraded results. It was rerun against the isolated source baseline for this push without creating a device or transport. Other completed-build results are retained from the previous validation; no new application, exporter or helper build was needed for this source publication.

Reproduce application checks with `build.ps1` and `test.ps1`; run the targeted probe fixture with `test_np2_degraded_chains.ps1`. Isolated fixtures retain the existing unused-field warning described in the prior validation.

## Qualification limits

Physical qualification of this application build, faulty-probe behavior, sustained USB/BLE workflows, and clean installation remain pending. Digital configuration verification does not establish analog signal quality. Segmented transport recovery does not establish a root-cause fix for repeated Win32 31 failures or a gap-free recording. No firmware update, hardware test, new installer or firmware/app pair promotion is part of this push.
