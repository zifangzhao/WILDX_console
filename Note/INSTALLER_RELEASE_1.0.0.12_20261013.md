# WILDX installer 1.0.0.12

Packaged 2026-10-13 from canonical WILDX source, based on commit
`8867d4ae128c7ce541ca4a350f5a11d3f85753b1` plus the changes delivered in this
release. The complete current GUI source was compiled together; no V2 source
files were substituted with earlier snapshots.

Installer: `LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.12.exe`, 2,302,421 bytes.
SHA-256: `D205863C8F814D94EAEFC6267B0FD1BB4594A9611B697C012C0A854B9BA60705`.

GUI SHA-256:
`DDCFE1E0D76AEDE5DE13637A508281D2322B87863F6C399E5B8541E444296384`.
SD/calibration companion SHA-256:
`C5D398F5839F647B20291A97F809809E214624DA86F6A692CE9A1108B3FEB654`.
All 13 payload identities are in the release manifest.

## Build and package evidence

One isolated Release GUI component build used
`ResolveReferences;PrepareResources;Compile;CopyFilesToOutputDirectory`, private
output/intermediate directories, disabled project-reference/installer builds
and the checked C5D companion. The build had zero warnings/errors. Production
GUI/exporter outputs were hashed before and after and were unchanged.

The NPConfigurator helper matches the prior published installer. Verification
found an older updater in the private bundle directory; the four exact updater
files were restored from the independently extracted, SHA-verified published
1.0.0.9 installer. Its selector self-test passed for exact MCU UID selection,
ABI-4 mode mapping and UID-bound mode/status replies. Eleven runtime files
(configuration, OpenTK and both helper sets) match that prior release; only the
GUI and SD/calibration companion changed.

The existing NSIS recipe produced the installer. 7-Zip independently extracted
it without running installation. All 13 runtime files matched the checked
payload byte-for-byte, the app version was 1.0.0.12, and no PDB files were
included. The prior published 1.0.0.9 package and manifest are retained in
`LATEST_INSTALLERS/legacy/20261013_before_shared_workflow_1.0.0.12`.

Private build/check/extraction logs are under
`_artifacts/release_1_0_0_12_20261013`; these generated files are not published.

## Focused release checks

| Check | Assertions | Scope |
| --- | ---: | --- |
| Media preview | 19,224 | Actual app, PCM/WAV validation, waveform and no-auto-start UI |
| Shared workflow | 88 | Actual app, common V1/V2 controls and capability gates |
| Recording bundles | 203 | Actual app, metadata/files and unverified mapping receipts |
| Factory calibration | 716,128 | Actual app, online/RAW, offline DAT and companion SD export |
| Standalone | 294 | Actual app, receipt/catalog/generation/UID handling and offscreen layout |
| BLE features | 1,433 | Actual app, stopped-only clock commits, preview/session/queue lifecycle |
| USB UI lifecycle | 134 | Actual app, modeled preparing/Stop/close/completion ownership |
| V2 targeted recovery | 120 | Actual app, exact fault policy, fresh files, UID/profile, unknown gap, retry bound and Stop |
| Degraded-chain policy | 1,467 | Canonical source, all receipt values/ports, fatal precedence and warnings |
| **Total** | **739,091** | Hardware-free checks |

The bundle/calibration fixture compilation retains the existing unused-field
warning for its isolated `NpDownloadStream.Cancelled` fixture. The release GUI
itself compiled without warnings. The unchanged broader SD/camera/merge suites
retain their earlier evidence; they were not counted as new release runs.

## Hardware and firmware limits

No devices were opened, recorded, reset or flashed during this packaging run.
No firmware/SDK/helper build or firmware release promotion was performed.
The manifest retains the published V2 firmware baseline for traceability,
without claiming qualification of a new firmware/app pair.

The V1 camera test on UID `00190027434250062039314E` used an earlier private
build: camera startup timed out with zero frames. Successful camera delivery
and live automatic V2 overrun recovery remain unqualified. The V1 developer
owns the camera hardware follow-up. Live USB audio is unavailable with the
tested resident contract; exported microphone WAV preview is supported.

The V2 developer separately observed a triple-probe 15 kHz short recording with
17,252 received/verified blocks and zero host drops. A triple-probe 20 kHz
240-second attempt failed with a shared PSSI ring overwrite after 174,332
received/verified blocks. That is retained negative evidence, not an endurance
pass for this installer. Degraded-shank behavior was exercised with injected
receipts; damaged-shank hardware was not qualified.

Recovery starts independent segments and records `lossless=False`, without
padding, invented gap duration or concatenation across gaps. No WinUSB error 31
root-cause repair, gap-free capture or full sustained recording qualification
is claimed. Clean install/uninstall and helper runtime discovery on a separate
Windows machine remain pending.
