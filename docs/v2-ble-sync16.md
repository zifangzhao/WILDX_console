# V2 initial clock synchronization — 2026-10-11

V2 GUI initialization now requests **16 valid clock observations and one commit**. The firmware task made the single production-line change in `Ble/NpBleClock.cs`, then returned ownership. This desktop integration retains that change, updates the actual-built-assembly regression's expected request counts, and includes the new `Tests/NpBleClockTests.cs` in `test.ps1`.

The shared `NpV2OnlineClock.Synchronize` default remains 64. Timing-quality limits, CRC/correlation, generation consistency, commit-receipt validation, and refusal to replay an ambiguous commit are unchanged. Attaching to an active recorder and periodic checks still use Observe only, without committing time.

## Source and tests

Current source version: **1.0.0.3**. The firmware owner's incremental Release compile passed. The executable SHA256 remains `27B30EA620FE14718AE2E044760478D3C088B13E58DE7DFA47DF4978D1012AB1`; this integration needed no product rebuild.

The normal `.\test.ps1` completed successfully:

| Check | Result |
| --- | --- |
| Standalone protocols/catalogs/UI | 276 passed |
| Built-assembly BLE features | 1,259 passed |
| Production-source clock checks | 127 passed |
| Built-assembly NP2 BLE configuration receipts | 32 passed |
| Exact UID selector / ABI4 mapping | Passed |
| NP1 updater mock transport | 5,854 passed |
| Release management forms | 632 passed |
| NP1 live / SD startup | 6,125,428 passed |
| NP2 SD-to-DAT | 705,740 passed |

The built-assembly clock test includes one synthetic observation timeout: 17 requests yield 16 valid observations followed by exactly one commit. Its subsequent heartbeat adds only one read. The production-source test also checks that the shared default stays 64, poor uncertainty refuses before commit, corrupt or changed-generation replies refuse immediately, and an invalid commit receipt is not replayed. The inherited mock-download compilation still reports its existing CS0649 cancellation-field warning; product compilation had no warnings.

Tests and documentation preserve the separate connection-diagnostic/configuration changes already present in the working tree. No firmware, hardware runner, application-version metadata or installer files were changed by this integration.

## Retained stopped-device evidence

The firmware owner's log at `D:/Code/XE_NP_V2/_artifacts/v2_ble_sync16_20261011/hardware-host.log` identifies this exact executable, peer `F5D68A6BCC7C` and one stopped device:

- Initial and final STATUS: Idle, fault0, probe mask3, 20 kHz, card ready.
- Initialize: 16 op0 reads, one op1 commit, **1,498 ms**, generation1, reported bound30.584 ms.
- Subsequent Observe: one read, no commit, same generation/connection epoch; passed the offset check.
- No profile/configuration/Start/Stop/reset/reconnect command in the sync action; explicit disconnect afterward.

These are retained production-path hardware receipts from the firmware owner. This desktop integration did not repeat them or access hardware. The 1,498 ms is synchronization time after connection, without advertising/discovery time. Intermittent discovery, reconnect/heartbeat/preview during recording, recording endurance and analog quality remain separate qualification work. The failed dual20 burst experiment is not a release candidate; normal APP `5E50B79F` was restored by the firmware owner.

Software receipts: `_artifacts/np2_ble_sync16_20261011`. The firmware owner maintains `D:/Code/XE_NP_V2/Note/Tools/V2SdLogger/PROGRESS_PLAN.md` section271.

## Packaging boundary

The published installer remains `LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.3.exe`, SHA256 `BD0DC44DCC43403CB8A770B71923A453BDCED72B1287C3247E1ACE01F887CFFF`. Its manifest binds a different, earlier executable (`3C70D210...`) and does not include this 16-observation update. No installer/package regeneration, version bump, push or firmware/installer-pair qualification occurred. The historical 64-observation evidence in [the original BLE integration record](v2-ble-integration.md) was kept and labeled as historical.
