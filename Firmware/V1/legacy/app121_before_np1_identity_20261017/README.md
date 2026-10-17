# V1 USB91 + SD/BLE APP release — 2026-10-16

Use these firmware components with the already-published
[WILDX 1.0.0.33 installer](https://github.com/zifangzhao/WILDX_console/raw/refs/heads/main/LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.33-USB_QUALITY_FIX_20261015.exe).
The installer contains no firmware and never flashes automatically.

- [USB91 service-only HEX](V1_USB_service_candidate_v1.0.91-usb20-paired-restore-test_20261016T211951Z.hex): fixes native 20 kHz two-probe USB capture and retains the USB89 configuration-start fix.
- [Unchanged SD/BLE APP HEX](V1_SD_BLE_app_only_v1.0.121-idle-sleep-test_20261016T150611Z.hex): APP CRC BC6B506E, ABI258, old NP V1 91k/91k battery-divider configuration.
- [Latest fused bootloader + USB91 + SD/BLE APP HEX](V1_fused_bootloader_USB91_SD_BLE_v1.0.121-idle-sleep-test_20261016T211951Z.hex): the same published bootloader and exact checked APP/USB91 components, with retained configuration excluded. SHA256 `ec10d6d227bbfc5e2da8f15cc4e4138e438fa68d8810015d7463ea0e51fca2d6`.

For a provisioned V1 already running APP BC6B506E, program only USB91 with
STM32CubeProgrammer/SWD, **without mass erase**. Its range is
0x0817E000..0x081FFFFF; the loader, SD/BLE APP, retained configuration and card
remain untouched. An APP-only updater cannot install the USB service.
Never use these images on V2. The fused image includes/reprograms recovery:
use STM32CubeProgrammer/SWD **without mass erase** on the matching V1 target.
It is not accepted by the protected APP-only updater and is not an
implant/power-loss-qualified update method. Excluding configuration addresses
does not make a mass erase safe. For an already-provisioned unit, a component
update remains the smallest change.
Do not overwrite USB91 with firmware bundled in an older WILD installer.

## Existing evidence and limits

The exact WILDX33 executable (SHA256 D1271EC76DF47FEA84BA83395121832D861F7AC6C56132F369D0525529A11CF7)
passed production-API/strict-decoder tests with USB91 on the mapped V1:
5 seconds, then a restarted 30.0057 seconds, P1+P2 native 20 kHz, actual wire0x77,
600170 AP rows per probe. Frame, counter, LFP-phase, relock and USB sequence
errors were zero. IMU:5984 samples, zero sequence gaps/error; normal Stop.
Initial startup alignment discard remains explicit. This is not an endurance
or unknown analog-payload-bit fidelity qualification.

The unchanged APP previously passed short BLE-controlled P12/20k and P123/15k
recordings with full committed-chunk readback and reconnect using WILD3.4.2.204.
Those BLE hardware results do not constitute a WILDX33 BLE+USB91 whole-pair HIL
test. The published WILDX installer has software/package checks and supports
BLE recording control, profiles, time and probe configuration. New WILDX V1
waveform/file-management controls and removal of its duration input are separate
unfinished development and are not included here.

The original component publication made no builds or hardware accesses. On
2026-10-17 the matching fused HEX was packaged from those same published
components: component SHA256, manifest/code CRCs/vectors, exact loader/APP
preservation, configuration exclusion, corrupt-input rejection and HEX
round-trip passed. The standalone APP manifest is copied exactly; no firmware
was recompiled and no hardware was flashed or tested for this packaging change.
Existing acquisition evidence and limits above are unchanged.

Every future latest V1 firmware delivery must include a matching fused HEX
alongside component HEX files. A USB-service-only release must refresh the
fused image too; an obsolete fused image must never be presented as latest.
Superseded USB89 service/fused images are retained under
[legacy/usb89_before_paired_restore](legacy/usb89_before_paired_restore/).
See [manifest.json](manifest.json) for exact artifact identities and scope.
