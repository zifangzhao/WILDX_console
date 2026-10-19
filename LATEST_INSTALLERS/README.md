# WILDX BLE connection fix

[Download the fixed installer](WILDX_console-Setup-1.0.0.44-V1-HS-FS2-BLE-fix.exe) (embedded version **1.0.0.44**).

Fixes V1 FS2 devices disconnecting after a successful status read. The console
now restricts the optional firmware identity query to V2, preserving V2 behavior.
The focused regression passed **28 checks**; all **45 extracted installer files**
matched the prepared payload. Setup includes **Run WILD-X**.

Only the GUI executable changed from r7. The high-speed firmware remains APP
**654824DE**, USB **02FF93A9**, recovery and matching fused/component HEX.
The complete matched stack remains **unverified**; this host fix has no new
physical-device test. Earlier scoped evidence and limits remain in the manifest
and [firmware notes](../Firmware/V1/README.md). Full-speed firmware is excluded.

This root contains one final installer. [Previous package](legacy/20261019_before_BLE_connection_fix/README.md).

[Fix and validation](../Note/BLE_V1_FS2_CONNECTION_FIX_20261019.md) ·
[Manifest](release-manifest.json) · [Checksums](SHA256SUMS.txt).
