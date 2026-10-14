# WILDX 1.0.0.22

[Download the installer](WILDX_console-Setup-1.0.0.22.exe).

Each GPIO port now shows event frequency in Hz under its label. The rolling one-second rate uses device RTC timestamps and refreshes every 100 ms. Device-timed events preserve their spacing when USB delivers a batch. Idle rates decay to zero; Stop/error displays 0.0 Hz while retaining total counts. The session metadata saves recent rates, window and timebase.

Software checks passed for rate calculation, batching, idle expiration, RTC rollback, metadata, GUI labels/layout and capture ownership. All 13 extracted runtime files match the checked payload; all 12 non-GUI files are unchanged from 1.0.0.21. No hardware or installer execution; existing V1 firmware support is unchanged.

[UI render with sample values](../docs/screenshots/usb-recording-gpio.png), [validation](../Note/GPIO_EVENT_FREQUENCY_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
