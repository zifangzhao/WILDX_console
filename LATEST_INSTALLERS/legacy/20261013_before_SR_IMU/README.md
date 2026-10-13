# WILDX 1.0.0.12 - 2026-10-13

[Download the installer](WILDX_console-Setup-1.0.0.12.exe).

This release packages the modern USB Mode, BLE Mode, Data Download and Advanced
workspaces, shared V1/V2 controls, factory ADC/gain calibration and recording
bundles. BLE reads the device version automatically and shares one connection
with Advanced; other tabs remain accessible while BLE is connected.

V1 Media preview adds camera Start/Stop, FPS/exposure and raw saving, plus
waveform/playback of exported microphone WAVs. Live USB microphone streaming
requires firmware support. The tested V1 camera timed out before delivering
frames; the V1 developer owns the remaining hardware test.

V2 degraded-shank receipts preserve requested configuration and explicitly
report unverified mapping. The exact stopped ring-overrun fault can continue in
a fresh segment when its verified prefix has no host writer/drop errors.
Recovery keeps the same UID/profile, records an unknown gap and remains bounded
and cancelable. Live automatic recovery is pending hardware qualification;
this release makes no continuous/lossless or WinUSB error 31 repair claim.

The exact release app passed 739,091 focused software assertions across media,
calibration, shared workflow, recording bundles, BLE, USB lifecycle, recovery
and probe receipt checks. Its main build had zero warnings/errors. The bundled
updater's exact-UID selector self-test passed. All 13 runtime files were
independently extracted and matched their SHA-256 hashes. The configuration
helper and updater retain their previously published binaries. No firmware was
built, flashed or promoted during packaging.

This is a software-checked engineering installer. Full V1/V2 hardware checks
and clean install/uninstall on a separate Windows machine remain pending.
Requirements: 64-bit Windows, .NET Framework 4.8.1 and .NET 8 Desktop Runtime.

[Release manifest](release-manifest.json), [checksums](SHA256SUMS.txt) and
[validation details](../Note/INSTALLER_RELEASE_1.0.0.12_20261013.md).
The prior published 1.0.0.9 installer is preserved under
`legacy/20261013_before_shared_workflow_1.0.0.12`.
