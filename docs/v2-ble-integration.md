# V2 BLE integration — 2026-10-10

Source integration against the frozen ABI4 / APP `ED881256` engineering release. No installer generation, version bump, firmware edit, push or hardware operation was performed.

## Behavior

- The BLE control version is read from the selected device using the common read-only handshake. A fresh, CRC-valid, correlated status transaction confirms the version before profiles or actions are enabled. Disconnect clears the detected version. Unsupported or invalid replies cannot select a fallback protocol.
- V2 initialization collects 64 valid clock observations while stopped, then sends exactly one anchor commit. Active attach and periodic clock checks only observe the existing mapping. Changed generation, invalid receipts or excessive offset leave PC time unconfirmed. Ambiguous commits are not replayed; status and Stop remain available.
- NP Preview uses the maintained native SuperSync / unsigned 12-bit ADC decoder. Complete calibration precedes the disposable rolling lease. Packets must match connection, request, session, rate, probe and channel. The queue holds at most eight packets; decoding and painting occur outside the receive callback. Preview yields to control transactions, renews at 750 ms, and expires on close/pause without an SD Start/Stop command. Display rate is 500 samples/s; gaps are preserved, with no interpolation, filtering, LFP or analog-quality claim.

## Changed source

`USB512_interface/USB512_interface/Ble/`:

- `NpBleConnection.cs`: common automatic version detection, independent preview routing, epoch guards, serialized writes, nonblocking preview admission and bounded calibration capture.
- `NpBleForm.cs`: automatic version-specific controls, V2 initialization / periodic clock checks, confirmed-clock Start gate and owned preview window.
- `NpBleWire.cs`: independent fixed-length BA preview framing alongside CRC-checked B8/B9 control receipts; truncated marked previews cannot hold complete controls.
- `NpBleClock.cs`: stopped-only initialization and read-only mapping verification.
- `NpBlePreviewForm.cs`: bounded consumer and three-lane plot.
- `NpV2Preview.cs`, `NpV2PreviewWire.cs`: unchanged maintained decoder copies, recorded in extraction provenance.

The project includes the new files. `Tests/BleFeaturesTests.cs` is called by `test.ps1`; the firmware task's `Tests/V2ReleaseCompatibility.ps1` was left unchanged.

## Validation

Release build: passed with zero product warnings or errors. Built `WILDX_console.exe` SHA256: `DE26BC5962D51B25B420A35688B89C7FFF8103D77AEBFFB8CA2C06570270CD49`. Offline regressions:

| Gate | Result |
| --- | --- |
| Standalone protocol / automatic version detection / catalogs / UI | 249 checks passed |
| BLE clock / preview / cancellation / queue / UI | 1,355 checks passed |
| Exact MCU UID selector / ABI4 mode mapping | Passed |
| NP1 APP updater mock transport | 5,854 checks passed |
| Actual Release management forms | 632 checks passed |
| NP1 live / SD startup decoder | 6,125,428 checks passed |
| NP2 SD-to-DAT export | 705,740 checks passed |

The inherited mock-download compilation reports its existing CS0649 warning for an unassigned cancellation field. Product builds have no warnings.

The unchanged release compatibility gate was run with PowerShell 7.6.5 against:

```powershell
.\Tests\V2ReleaseCompatibility.ps1 `
  -ReleaseRoot D:/Code/XE_NP_V2/LATEST_FIRMWARE/V2_NP2 `
  -EvidenceRoot D:/Code/XE_NP_V2/_artifacts/v2_six_ru_batch_hil_20261010
```

- `run20`: 1,106 real notifications; 83 CRC-valid V2 status replies; Recording and Complete (5) preserved.
- `run30`: 27,465 real notifications; 718 CRC-valid V2 status replies; Recording and Failed (255) preserved.
- Bundled exporter accepts the actual Complete footer/UTC with a descriptor reconstructed from the footer and rejects a resealed failed footer. This is metadata compatibility evidence, without a payload/DAT comparison.
- Bundled updater's pure package loader accepts the exact 146,756-byte APP `ED881256` container, SHA256 `04A167A036EACA6F0F6B5BE3A28CA9890304F61AAC5AA02E72B271D937C76132`. No updater hardware entry point was invoked.

That gate's final message still describes clock/preview as missing; it predates this integration and was retained as owned by the firmware task. The dedicated checks above and below validate the added source build.

Real preview replay is reproducible after `test.ps1` compiles its test executable:

```powershell
.\_artifacts\standalone-tests\BleFeaturesTests.exe `
  .\USB512_interface\USB512_interface\bin\Release\WILDX_console.exe `
  .\_artifacts\standalone-tests `
  D:\Code\XE_NP_V2\_artifacts\v2_six_ru_batch_hil_20261010
```

| Recorded trace | BA frames | Complete calibrations | Rolling packets decoded with captured phase | Disposable errors/gaps |
| --- | ---: | ---: | ---: | ---: |
| run20 | 502 | 1 | 485 / 485 | 0 |
| run30 | 13,663 | 1 | 13,646 / 13,646 | 0 |

This run passed 722,349 assertions including fixture, trace-format, geometry and ADC-range checks. It replays saved notifications through the actual built parser and decoder; it does not establish GATT delivery timing or compare the preview against recorded DAT samples. UI renders are under `_artifacts/standalone-tests`, with device Load/Shown handlers disabled.

## Hardware and release limits

The firmware owner's retained 20 kHz dual-probe capture is 31.50336 seconds with native readback. The 30 kHz run failed after 815.6128 seconds from source retention overflow; it is incomplete, with experimental 192 MHz operation. Long-duration lossless recording, NP2013 test-pattern interpretation and independent analog quality remain unqualified. ROM DFU has no retained hardware qualification, and the exact fused release still needs end-to-end USB/update qualification.

The hardware owner should check WILDX cold connect/version detection, stopped clock initialization, active attach, periodic observation under preview load, SD Start/Stop/drain, preview selection/pause/close and reconnect before replacing the existing WILD entry points. The old `LATEST_INSTALLERS` candidate is unchanged and predates the clock/preview additions; neither its manifest nor this source build is a validated installer/firmware pair.
