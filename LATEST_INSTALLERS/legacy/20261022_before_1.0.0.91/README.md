# WILD-X 1.0.0.90

[Installer](WILDX_console-Setup-1.0.0.90.exe).

Fixes V2 BLE Stop's false30-second timeout. Stop is sent once; the console
keeps polling with elapsed finalization text and reports a fresh same-peer
completion or stopped fault automatically. Lost replies and disconnection
remain unconfirmed; no Stop replay or invented progress. Keep power connected
until a terminal device status. Long SD verification is unchanged.

102 actual-source fake-transport checks passed. One GUI-only incremental build
and one installer pass; all40 extracted payloads match. Firmware, SDK, helpers,
firmware catalog and download/format UI remain exactly88. No hardware access,
recording, flash, installed-pair or newly-flashed-fused qualification.

[Evidence and limits](../Note/V2_BLE_STOP_FINALIZATION_20261022.md) /
[Exact manifest](release-manifest.json) /
[Compiler inputs](../ReleaseSources/1.0.0.90/README.md).
Previous88 is retained under legacy/20261022_before_1.0.0.90.
