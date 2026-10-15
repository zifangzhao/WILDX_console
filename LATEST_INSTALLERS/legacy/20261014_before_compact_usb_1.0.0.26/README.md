# WILDX 1.0.0.25

[Download the installer](WILDX_console-Setup-1.0.0.25.exe).

Network time now displays your Windows local time zone and UTC offset. Automatic selection compares Cloudflare, NIST and Microsoft using measured uncertainty and jitter, with per-server retry/backoff and manual override. The target is **±10 ms**; the UI reports when the current estimate exceeds it. Internal UTC values and recording timestamps keep their existing meaning.

USB/BLE neural, camera, scope and V1 media previews use normal independent windows, allowing the console and other windows to come forward. Live-preview resources and media/capture drain guards remain intact.

20,083 clock checks, 37 actual clock-UI checks, 11 preview lifecycle checks and 134 capture ownership assertions passed. The final 20-second public NTP comparison selected Cloudflare; estimated error was ±12.431–15.394 ms, above the 10 ms target on this network. All 13 extracted runtime files match the checked payload; all 12 non-GUI files remain identical to 1.0.0.24. No recording hardware or installer execution.

[Time settings preview](../docs/screenshots/network-time-settings.png), [clock behavior](../docs/network-time.md), [validation](../Note/NETWORK_TIME_LOCAL_PREVIEW_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
