# WILDX 1.0.0.34 — 2026-10-17

The release packages the BLE preview/recordings workflow, V1 format recovery,
GPIO continuity and firmware/version integration developed since 1.0.0.33.
Advanced now places USB streaming, USB download and SD / BLE beside Firmware
Update and Diagnostics. Firmware dialogs retain recovery entry/exit and flashing.
The installer offers Run WILD-X after installation.

GPIO/RTC logging continues through NP interruption and automatic recovery until
explicit Stop or app closure. New recording folders link to the original GPIO
files. V1 format can recover a quiescent writable card without requiring its
old catalog to be readable. Format still requires confirmation and fresh completion
receipts. Native delete commands are never automatically replayed after uncertainty.
BLE recording is continuous until explicitly stopped; preview and file operations
adapt to advertised firmware capabilities.

## Firmware distribution

Both firmware owners reconfirmed their latest approved distribution artifacts
on 2026-10-17. Exact payloads, original owner manifests/READMEs and qualification
limits are in [Firmware](../Firmware/README.md).

| Target | Components | Artifact publication commit |
| --- | --- | --- |
| V1 old NP, 91k/91k divider | APP121 / BC6B506E; USB91 / 40D39075; matching fused recovery image | e831a5f10c28c1df596766b195d4a48aeb38991d |
| V2 development | APP136172A5; USB2E07799E; recovery6408DBB8 | e7b0d08cc3d24dc0776532d2a599462528301df7 |
| Separate V1 camera40 candidate | APP121 / 99BA4F27; fused USB83 / 82C6E21A and recovery | 31e1d51571f9ea0b80699c98b547a434f7c11c45; exact artifacts retained at e831a5f under legacy/camera40_20261014 |

These commits identify published artifacts, not a newly clean, complete source
snapshot of every incremental firmware build input. No firmware was built or
flashed for this host release. Private V2 power comparators are excluded.

The camera bundle is explicitly selected through its separate folder; it never
replaces the default Neuropixels bundle automatically. Shared ABI/version labels
do not identify the physical board. Only APP images use compatible protected APP
updaters; service/recovery/fused images use the documented separate workflows.

Installed and bundled versions are displayed separately. Missing firmware identity
is shown as Not reported; unknown/private builds are not classified as older.
Firmware updates remain explicit and capability-gated.

## Validation

The Release GUI, exporter and both .NET8 helpers built successfully. The checked
GUI SHA256 is AB45EFC4776482E5094E86047AD74321B3D4D7D3B382ED0D21BE930842F4678F;
exporter A04D630F1EA716FD3EC4D3DE54C3875479315B6647F4B4A3F4C60C26BB519070.
All 92 console compile inputs were recorded and rechecked.

All offline release gates passed. These include standalone/workspace and exact
device selection; actual configurator imports; display HPF; recording metadata;
media parsing; mocked BLE connection deadlines/frame writes; clock/configuration
receipts; V1 APP update/start compatibility; V2 degraded-chain and live DAT sample
layout; IMU lifecycle; recording summaries; GPIO continuity; V1/V2 SD export and
calibration; BLE media/storage; firmware catalog and management-form layout.
Stale test expectations for probe capabilities, an added SD-fixture argument,
USB-health dependencies and the firmware-notice extraction boundary were corrected.

Focused results include 639 management-form checks, 4397 actual-assembly BLE
workflow checks, 17 fake-GATT storage checks, 1945 simulated V2 catalog checks,
590 firmware checks, 69 USB-health checks, 27 runtime-profile checks, 25 AUX
retirement checks and 20 format-admission checks. Numeric sample/bit checks are
software assertions, not independent physical trials. Actual controls were
rendered offscreen without device discovery. Bundled helper execution required
running the offline tests outside the restricted process sandbox.

NSIS packaging succeeded. Independent 7-Zip extraction verified all 37 runtime/
firmware payload files byte-for-byte, including all ten firmware images. The
installer was not executed. Its SHA256 is
0393C1A10B970FC3977FA1DDB6E251E0EBD94C04F9E990D5DD9FBBAA794A8ED8.
The manifest records every payload hash. Exactly one final installer remains
in LATEST_INSTALLERS; earlier final packages retain their original metadata in legacy.

## Physical evidence and limits

No hardware was acquired or changed during this release. The earlier unchanged
V1 firmware staged API chain passed format, profile selection, a short P12/15k
recording, Stop/drain, strict USB export and APP-idle return. Two readiness failures
required fresh read-only continuations. V2 format was separately confirmed by its
owner. See [the preserved full-chain record](V1_SD_FORMAT_RECOVERY_20261017.md).
Those results do not qualify this newly packaged GUI/firmware combination.

V2 BLE file listing/deletion still requires capability22, absent from the bundled
firmware. Probe3 BLE waveform preview remains unsupported. Dual30 previously failed
integrity qualification; triple20 is unqualified. Selectable profiles do not prove
sustained lossless performance. Intermittent V1 discovery/mode readiness and live
USB-start reports remain. Windows31 has not been established as a cable error.
The separate experimental NpDownloadStream readiness change remains local and is
not in this release; the committed, previously checked transport source was used.

The separate camera candidate has a passing short BLE SD40/ADC200k/latest-value
IMU result, but its prior strict USB completeness check failed with 11 camera drops.
Saved camera rates are not migrated. Whole-pair, all-mode, endurance and implant
qualification remain outstanding. Firmware owner notes retain the detailed limits.
