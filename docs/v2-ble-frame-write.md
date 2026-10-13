# NP2 BLE frame writes — 2026-10-11

The firmware task returned a frozen production transport change: exact 67-byte NP2 SD (`NLD2`) and clock (`NPT2`) envelopes use one acknowledged GATT write when the negotiated PDU is at least70 bytes and FFF2 supports Write. V1, configuration, preview, unknown frames, small-PDU and fast-only transports retain their 20-byte delivery behavior. Cancellation, connection epoch checks, the owned five-second deadline and no-replay policy are preserved.

Verified frozen handoff hashes:

- `USB512_interface/USB512_interface/Ble/NpBleConnection.cs`: `2DD24EBE1C3A2460F0CDA3C776B961DDBE1F989B3F8C09529C6779920D0AAA5F`
- `Tests/NpBleFrameWriteTests.cs`: `AA56D7F00A16B2F9563F855BA99A14AABDCE1DFA3DCD74F551A48CB80CDE509B`
- Candidate `WILDX_console.exe`: `3E93DBCFC91AD108E7D070661B90D70C27DADAD794937C649338C0CA0347D6D7`, 1,183,232 bytes/version1.0.0.3.

The owner compiled the incremental Release component once. Its retained `_artifacts/np2_ble_frame_write_20261011/targeted.log` reports **653 actual-source modeled checks** for the extracted chunk policy/write loop, without radio or MTU hardware qualification by the fixture.

This workflow integration registers the fixture in `test.ps1`, using absolute source/compiler arguments like the warm-link fixture. The updated PowerShell script passes syntax parsing. The frozen source, fixture and binary were preserved; no rebuild, fixture/broad-suite rerun or hardware action occurred here.

## Retained production-path evidence

The firmware owner used the exact candidate through the headless production GUI-assembly transport on peer `F5D68A6BCC7C`:

| Check | Count | Median / p95 / max |
| --- | ---: | --- |
| STATUS | 20 | 37 /38 /52 ms |
| CAPS, existing frame format | 4 | 82 /112 /112 ms |
| Clock timing reads | 8 | 74 /75 ms; maximum not specified in the handoff |

Initial production clock sync:16 observations, one commit, **643 ms**, generation1 and bound18.676 ms. One subsequent Observe heartbeat passed with bound41.977 ms. The normal APP `5E50B79F` and SD contents were unchanged; the intended RAM time anchor was the only mutation in that sync action.

A prior STATUS-only ACK20/ACK67 diagnostic used a negotiated PDU247. Its ACK20 arm included15ms pacing, so it is not a measurement of the current console's fast20 path or a direct console speedup ratio. The production-path timings above are retained observations, without a universal latency guarantee.

Connection discovery remains intermittent: the sync run's first FFF0 result was Unreachable, the existing discovery retry succeeded, and connection took11,984 ms. Neither discovery nor active-recording behavior is qualified by this handoff.

Firmware-owned evidence: `D:/Code/XE_NP_V2/_artifacts/v2_ble_frame_size_20261011`, shared progress section280. GUI evidence: `_artifacts/np2_ble_frame_write_20261011`. No firmware, SD write, version bump, installer/package, push or complete firmware/installer-pair qualification occurred. Published releases are unchanged; the [previous cancellation record](v2-ble-connect-cancel.md) retains its own frozen build evidence.
