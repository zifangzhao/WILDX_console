# V1 recording compatibility — WILDX 1.0.0.18

V1 Recording settings now works in idle SD-download mode using requests already implemented by the existing resident firmware. No firmware upgrade is required. Legacy selectable and fixed-profile capture firmware remain supported.

## Cause and fix

The 1.0.0.17 settings reader always requested live status `0x42`. The V1 split resident's download handler rejects that request, leaving probe controls and Save disabled even when device enumeration says Device ready. Its service status `0x6E` and recording capabilities `0x73` are already available in both idle streaming (role 2) and download (role 3).

Settings now reads and validates the selected UID's service status first, then requests capabilities. Busy, pending, recovery and unsupported roles cannot expose editable settings. Download mode never sends `0x42`, including when capabilities are unavailable. Settings performs read-only requests; Start uses the existing `EnsureStreaming` path and mode command `0x6F` value 2 to wait for the same UID's idle streaming service before capture.

If an older firmware does not implement the optional split status, unsupported-request errors 1/31 retain the existing live-status and capability path. Missing optional capabilities retain the fixed legacy profile. Timeout, access, disconnect and cancellation errors propagate with their Win32 code and cannot become a fabricated legacy profile. Existing V1 control opens disable WinUSB selective suspend before querying, with handle cleanup on failure. Acquisition, probe configuration commands, wire decoding, clocks and DAT layout are unchanged.

The source-only firmware comparison used the retained USB82 source `xe_np_usb_stream.c`, SHA256 `2E95CED6E81AA73BBE8161CC41872B216A931F0BD4EDEAE7F548C426DF622558`, plus its mode and SD-download handlers. This verifies the request routing; it does not identify or qualify Yoshi's rat logger firmware. No firmware image is bundled, selected, flashed or promoted by this release.

## Software validation

All checks passed against the isolated 1.0.0.18 GUI or maintained management source, with native USB boundaries mocked:

| Check | Assertions |
| --- | ---: |
| Actual GUI V1 settings, streaming/download roles, legacy selectable/fixed profiles, probe selection and error handling | 97 |
| Maintained V1 management, unsupported-request fallback, transport errors, wake policy, UID/ABI/ACK/CRC guards and cleanup | 5,886 |
| V1 Start preparation, exact UID, download-to-streaming command, busy/error refusal and no firmware-update operations | 19 |
| Actual GUI V1 paired/legacy decoder, fragmented input, DAT/AP/LFP/timing/metadata and admission | 74,176 |
| USB UI stop/preparation/completion ownership | 134 |
| Universal workflow | 106 |
| Folder labels, stored START times, collision/no-overwrite and shared UI labels | 53 |
| V1/V2 independent recovery recordings and failure policy | 125 |
| Standalone protocol/catalog/UI regressions | 294 |

The GUI component build had zero warnings and errors. All 13 installer runtime files were independently extracted and matched the checked payload. The 12 non-GUI files, including the decoder companion, channel configurator and firmware updater, match 1.0.0.17 byte for byte. The installer was not executed, and no device discovery, recording, firmware operation or physical disk access occurred during these checks.

Reproduce the new checks after building the GUI:

```powershell
.\test_v1_recording_profile.ps1 -ConsoleExecutable <checked-GUI.exe>
.\Neuropixels\test_np_inplace_update.ps1 -OutputDirectory <management-fixture>
.\test_v1_start_transition.ps1 -ManagementFixtureDirectory <management-fixture>
```

The standard `test.ps1` runner also includes both new compatibility checks.

## Build and package identity

The isolated source starts at release 1.0.0.17 commit `71906e1ad2f8dc283777f78c41fdacf239a21f1f` and overlays only `NpModeDialog.cs`, `XeNpWinUsb.cs`, `NpV1Management.cs` and the assembly version. It includes the existing V1 control wake-policy and restricted legacy-fallback fixes. Pending BLE cache, media-export and hardware-test changes are excluded. Private inputs, logs and package verification are retained under `_artifacts/v1_recording_fix_20261014`.

- GUI SHA256: `39480DEAAC670DCF0DC5AD4020D9EE53697D72353B0F0759155D658977775A55`
- Installer SHA256: `950379A4716575A25FC4B12E27E916B7F8DBE6019CF464693B241CEBACFD9353`
- Installer size: 2,311,800 bytes

Yoshi's physical probe recording remains to be confirmed. Software checks establish the compatibility paths and retained decoder behavior, not physical recording reliability. Existing V2 throughput, damaged-shank and endurance qualification limits remain.
