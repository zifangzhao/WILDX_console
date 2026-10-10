# V2 resident USB self-test integration — 2026-10-10

The diagnostic owner added the selected-device actions, USB client and report parser to `USB512_interface/USB512_interface/NpLogger/NpV2FirmwareForm.cs`, `Neuropixels/USBConsoleV2/NpV2UsbConsole.cs` and `Neuropixels/Tests/test_np_v2_usb_console.cs`. The frozen wire contract is `D:/Code/XE_NP_V2/Core/Inc/xe_np_usb_selftest.h`: OUT `0x78`, commands 1 Run+hold, 2 Hold, 3 manual SD1.8, 4 Release and 5 manual SD3.3; IN `0x79`, 256-byte version1 report, magic `58535432`.

The source handoff build SHA256 was `BFB2C018AF282495B8715290D691972C0F01AB40B13B0C2CF1C822FF4B2DFB3F`. Its parser rejected the actual retained Run report because it required skipped checks to be in the attempted-check mask. The focused retained-fixture test reproduced that rejection before this correction.

## Corrections after handoff

- Passed/failed results must be a subset of attempted checks. Skipped checks remain outside that mask and disjoint from attempted results; malformed ABI, unknown bits and overlapping results still fail.
- A terminal receipt must match the requested command and advance its sequence using wrap-safe ordering. Older, unchanged or wrong-command reports cannot complete an action.
- The ABI4 service's UID0 and UID1 XOR UID2 attest the selected path's exact MCU UID before a diagnostic write. Full report UID must match once a snapshot exists, including subsequent polls.
- The untouched cold-boot report has zero sequence/command/UID and no component results. It is admitted only as an initial unrun report after service attestation, so the first explicit action remains possible. An action report with zero UID is refused.
- Queued/running diagnostics cannot admit another action even if a separate status read still says idle. UI status distinguishes unrun, in progress, rejected/error, failed and skipped results.

The three source files above contain these corrections. `README.md` and this record document their availability and limits. Diagnostic writes remain explicit and idle-only. No automatic retry, release, mode change or reset was added.

## Focused evidence

One incremental Release Compile/Copy build passed with no product warnings/errors. It regenerated no installer and did not rebuild the configurator/updater. Updated `WILDX_console.exe` SHA256: `F429191FD6635CD76E03C637CB1EF757EAED16881BFE2B00F1B739E969FFAD64`.

The focused client/parser fixture passed **32 checks** for each actual retained report. The actual Release executable was also loaded through reflection and accepted both raw reports, attested their complete UID and admitted their fresh matching terminal state. Neither route invoked device discovery, native transport, USB, SWD or hardware actions.

| Retained fixture | Sequence / command / state | Attempted / passed / failed / skipped |
| --- | --- | --- |
| `D:/Code/XE_NP_V2/_artifacts/v2_usb_selftest_hil_20261010/run/report.bin` | 1 / 1 / Done | `03BF` / `009F` / `0320` / `3C40` |
| `D:/Code/XE_NP_V2/_artifacts/v2_usb_selftest_hil_20261010/hold_3v3/report.bin` | 5 / 5 / Held | `03BF` / `009F` / `0320` / `3C40` |

UID in both reports: `001B0019434250062039314E`. Hold retains the last Run's component results; its new sequence/command describes the manual action, without rerunning those checks. Passed bits remain separate from failed/skipped bits.

Reproduction after compiling the existing test executable:

```powershell
.\Neuropixels\_artifacts\selftest-tests\NpV2UsbConsoleTests.exe --self-test-report-only `
  D:\Code\XE_NP_V2\_artifacts\v2_usb_selftest_hil_20261010\run\report.bin
.\Neuropixels\_artifacts\selftest-tests\NpV2UsbConsoleTests.exe --self-test-report-only `
  D:\Code\XE_NP_V2\_artifacts\v2_usb_selftest_hil_20261010\hold_3v3\report.bin
```

The normal existing USB fixture includes the updated checks. Unrelated broad test suites were not repeated for this correction.

## Actual hardware limits

The firmware owner reports resident-only image `C3C76C43` flash-verified on the exact new-board UID, preserving APP/loader/settings. The production diagnostic Run was invoked through its SWD fallback and completed in 251 ms. Its private SRAM pattern, image CRCs, PSRAM ID/saved-restored pattern and BMX ID passed; SD initialization failed at CMD0, BME returned NACK, and USB control was not configured. These results do not qualify full memory capacity/timing, sustained recording, analog quality or the external skipped NP/ADC/camera/BLE functions.

Windows still reports Code43 and no exact-UID B386 interface. Therefore real EP0/GUI invocation has no retained hardware pass, and this is not a complete validated firmware/GUI release. PF2/PD10 were deliberately left high and PG0 low by the firmware owner for the user's multimeter measurement. Those are requested GPIO levels, without measured voltage/current claims. This desktop review sent no Hold, Release, reset or other hardware command. The installer, version and Git state were not promoted or pushed.

Firmware-owned guide/evidence: `D:/Code/XE_NP_V2/Note/Tools/V2SdLogger/USB_QUICK_SELFTEST.md` and `PROGRESS_PLAN.md` entry223. The firmware owner maintains that record; it was not edited by this desktop integration.
