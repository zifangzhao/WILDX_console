# WILDX V1 BLE startup handoff fix

[Download installer](WILDX_console-Setup-1.0.0.44-V1-BLE-handoff-fix.exe) — embedded version **1.0.0.44**.

Includes high-speed USB **0E542BBD**, unchanged APP **654824DE**, fixed GUI and boot/recovery. V2/camera firmware remains unchanged; the pending V2 catalog candidate is excluded. The owner software regression and scoped local USB/no-host-boundary HIL passed with private APP60EC609E. **The release APP654824DE pair was not newly device-tested.** Remote B34D50 was unavailable; no physical battery power-cycle, recording/streaming/endurance or in-animal qualification. Windows discovery remained intermittent.

All **47 extracted files** matched in one packaging pass. Setup includes **Run WILD-X**. APP-only updates do not replace this USB fix. Preserve configuration and SD data.

One current installer; [previous hotfix archived](legacy/20261019_before_battery_ble_handoff_r2/README.md). [Full scope](../Note/V1_BLE_STARTUP_HANDOFF_FIX_20261019.md) · [Firmware](../Firmware/V1/README.md) · [Manifest](release-manifest.json).
