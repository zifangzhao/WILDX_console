# BLE connection cancellation — 2026-10-11

This record describes the frozen cancellation handoff. The subsequent [NP2 frame-write handoff](v2-ble-frame-write.md) uses a later executable; the cancellation hash and checks below remain historical evidence.

The firmware owner returned the frozen `NpBleConnection.cs`, `NpBleForm.cs` and `NpBleWarmLinkTests.cs` changes. ConnectAsync now tracks its owned discovery cancellation source under the existing receive lock. Local Disconnect cancels the pending await; explicit cancellation is distinguished from a phase timeout, and cleanup/fresh-budget replacement retains the concurrent-close guards. The form exposes **Cancel connection** during its connecting lease, without changing recorder command policy or sending a reset.

Candidate Release executable: 1,183,232 bytes, version1.0.0.3, SHA256 `9E145F7F0E074957E1E19EACBD201940C2C282CA005000FD0476F82B904A6600`. This integration verified that exact hash and preserved the source/fixture/executable. No product rebuild or test rerun was needed.

The existing `test.ps1` already compiles/runs `Tests/NpBleWarmLinkTests.cs` with absolute source/compiler paths. Therefore the expanded cancellation fixture is included automatically. `Tests/NpBleConnectCancelHardware.cs` remains a separate, explicit hardware checker; it is not added to the regular offline workflow. No recording test or hardware check was added as a GUI release gate.

## Retained checks and limits

The firmware owner's `_artifacts/np2_ble_cancel_connect_20261011/targeted.log` records **225 actual-source fixture checks**, using modeled GATT plus source assertions for form routing. Its incremental optimized Release build passed. These are retained checks from that owner, without duplicate execution here.

The owner reports a real pending FFF0 wait canceled and retired in **5 ms**, with connectedfalse and the transaction lease free. A subsequent normal exact-peer STATUS/CAPS/reconnect check passed; final status was Idle/card ready. This measures cancellation of the application's pending await and cleanup. It does not establish cancellation or completion time for the underlying Windows radio/GATT operation.

Cold discovery remained about6.6 seconds. One warm reconnect took3.484 seconds; that timing is not attributed to this change. Historical intermittent Unreachable and recording/reconnect quality remain separate qualification work.

Firmware-owned hardware evidence: `D:/Code/XE_NP_V2/_artifacts/v2_ble_cancel_connect_20261011`; shared progress section279. No firmware change/flash, SD mutation, recording, installer/package, version bump or push occurred in this desktop integration. Published releases remain unchanged; the candidate is not a validated complete installer/firmware pair. The earlier [warm timeout record](v2-ble-warm-timeout.md) retains its own frozen hash and model-tested timeout evidence.
