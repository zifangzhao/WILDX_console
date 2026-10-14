# WILDX 1.0.0.23

[Download the installer](WILDX_console-Setup-1.0.0.23.exe).

GPIO signals now appear in a table: **ID / Status LED / Count / Freq (Hz)**. The initial five rows contain a header and IDs 0–3. Additional received channel IDs add sorted rows, scrolling vertically as needed. Each channel retains its own LED, count and one-second RTC event frequency. GPIO session metadata saves the explicit channel-ID order.

Software checks passed for additional IDs, consistent counts/rates, metadata, table layout, scrolling, reset and capture ownership. All 13 extracted runtime files match the checked payload; all 12 non-GUI files are unchanged from 1.0.0.22. No hardware or installer execution; existing V1 firmware support is unchanged.

[UI render with sample values](../docs/screenshots/usb-recording-gpio.png), [validation](../Note/GPIO_CHANNEL_TABLE_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
