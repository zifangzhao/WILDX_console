# BLE warm connection timeout recovery — 2026-10-11

This record describes the first frozen timeout-recovery handoff. A subsequent [connection-cancellation handoff](v2-ble-connect-cancel.md) updates the transport/form and fixture; the hashes and 202-check result below remain historical.

The firmware task returned a frozen transport change: expiration of the owned warm FFF0 service-discovery deadline now reaches the existing one fresh exact-peer fallback with a fresh, finite cancellation budget. Explicit Disconnect/Dispose remains terminal; a physical link-event epoch change remains recoverable. No recorder command is replayed, and UI/firmware/protocol/version behavior is unchanged.

Frozen handoff hashes:

- `USB512_interface/USB512_interface/Ble/NpBleConnection.cs`: `54A70A79BE93764241080C241FAE7260CBF07663CC2CF1156B7432F7AE0D3197`
- `Tests/NpBleWarmLinkTests.cs`: `A7C5A217AC3D8E5B14F31B69FD14CD632BAA88D4D4F45BAD65DAF826D4355357`
- Release `WILDX_console.exe`: `66D85642208A584C9938907288CF3DDEFDBF2097A6E9F6A11C33995941280D63`, version1.0.0.3.

The firmware owner reports **202 actual-source fixture checks**, including 15 recovery scenarios and the existing warm identity checks, plus one incremental affected-component compile. The fixture extracts the production ConnectAsync/Disconnect/ConnectionChanged methods and models GATT/cancellation; it constructs no radio or WinRT device. The initial relative-path compilation invocation failed, then the same fixture passed using absolute Windows paths.

This workflow integration adds that frozen fixture to `test.ps1` with absolute source/compiler arguments. It does not modify the transport, fixture or Release executable, and does not rerun tests/build/hardware. PowerShell syntax parsing of the updated script passed. README now distinguishes these source changes from the existing installer.

The firmware owner also reports one read-only, same-instance reconnect on the exact existing V2 board: cold advertisement4.778s/connection7.722s, warm reconnect380ms with first FFF0 Success51ms, matching before/after STATUS/P1/P2 CAPS, unchanged Idle/card-ready state and explicit disconnect. **That run did not reach the 20-second timeout branch.** The timeout recovery is model-tested, without a forced hardware reproduction or a proven cause for historical Windows Unreachable failures. No reset/sync/profile/configuration/Start/Stop, firmware or SD write was sent in that HIL run.

The firmware task owns the shared progress/hardware evidence. No installer, packaging, publication, version bump, push or complete firmware/installer-pair qualification occurred in this integration. The earlier [sync16 evidence](v2-ble-sync16.md) describes its own frozen build and remains historical to this transport update.
