# V1 camera40 firmware and UI candidate

Packaged 2026-10-14 EDT: APP121 / USB83, WILD 3.4.2.204 and WILDX 1.0.0.27.
This is a packaged candidate, not a newly hardware-validated pair.

| Use | File |
| --- | --- |
| Existing V1: update SD/BLE APP only, preserving USB and settings | [APP121 HEX](V1_SD_BLE_app_only_v1.0.121-camera40-default-candidate_20261015T012531Z.hex) |
| Factory/recovery: bootloader + USB83 + APP121 | [Fused HEX](V1_fused_bootloader_USB_SD_BLE_v1.0.121-camera40-default-candidate_20261015T012531Z.hex) |
| Online/BLE camera and SD profile UI | [WILD 3.4.2.204](../LATEST_INSTALLERS/WILD_console_media_prototype_Setup_3.4.2.204.exe) |
| USB camera/IMU capture UI | [WILDX 1.0.0.27](../LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.27.exe) |

The firmware's missing/invalid-settings fallback and both camera UIs default to
**40 fps**. WILD offers 16/25/40/50 fps; WILDX retains its explicit rate control.
Saved device settings are preserved: if the device reports 50 fps, select and
apply 40 fps before recording. No automatic migration of existing profiles.
ADC remains 200 kHz when enabled; existing IMU logging remains unchanged.

## Evidence and limits

- Subsequent connected-board validation installed APP121 and passed a fresh60s
  SD camera40+ADC200k+IMU full readback:2417/2417 valid camera images including
  startup,0 camera gaps/cadence misses,12082038 continuous ADC samples and7645
  valid AG snapshots with0 new IMU errors/timeouts/missed estimates. The separate
  WILDX27 USB40/IMU run reported11 camera drops despite0 missed deadlines;
  USB completeness is not qualified. See the [fresh report](../Note/V1_CONNECTED_CAMERA40_VALIDATION_20261014.md).
- On APP120/USB83, a 60-second SD camera40 + ADC200k + IMU run had
  **2208/2208 valid camera slots after the first 5 seconds**, exact 25 ms saved
  schedule, continuous ADC and no new IMU errors/timeouts/missed estimates.
  All 142 invalid images occurred during startup, at 0.100–3.625 seconds.
- 40 fps is the highest tested steady rate, not a proof about untested 41–49 fps.
  Physical completion timestamps have jitter; scheduled slots are not exact
  exposure times. IMU records are latest-value snapshots, not a lossless FIFO.
- APP121 is byte-identical to tested APP120 except three compiler copies of
  the default value, 50 → 40. The native fallback/retention regression passed.
  USB83 code is unchanged; the fused delivery sets its normal recovery-default
  manifest flag. APP-only leaves the installed USB service untouched.
- WILD: 183 protocol/runtime and 2823 actual-form/wire checks passed.
  WILDX: 8 focused actual-form camera-rate checks passed. These are software
  checks, not new hardware or end-to-end installer-pair qualification.
- Prior USB83 + USB UI 40 fps/IMU evidence is retained in the manifest.
  The new firmware and UI versions were **not flashed, installed or hardware
  retested** during packaging. No endurance or whole-recording lossless claim.

Installers do not contain or automatically flash firmware. Both HEX files exclude
configuration and option bytes. Fused recovery updates are non-atomic and are not
qualified for implanted field use. APP121/USB83 is V1 only; do not deploy to V2.

Exact identities: [manifest.json](manifest.json). Evidence and scope:
[development record](../Note/DEVELOPMENT_WORKFLOW.md). Earlier deliveries are
preserved under [legacy](legacy/). These packages are published on the existing
V1 release, WILDX main and WILD_console branches; exact commits are in the manifest.

V2 remains separate: [V2 media delivery](V2_Media/README.md).
