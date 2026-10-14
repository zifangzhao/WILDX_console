# WILDX 1.0.0.21

[Download the installer](WILDX_console-Setup-1.0.0.21.exe).

GPIO / RTC now extends from Device health down to Network time, beside Capture activity. Four LEDs show Port 1–4 activity (CH0–CH3), with event counts. LEDs flash for 350 ms after new activity, dim after Stop/error and retain final counts. These indicate trigger events; the protocol does not report sustained input levels.

Software checks passed for LED timing/mapping, counts/Stop/reset, panel alignment and window resizing, recording metrics, capture ownership and existing protocol/catalog behavior. All 13 extracted runtime files match the checked payload; all 12 non-GUI files are unchanged from 1.0.0.20. No hardware or installer execution; existing V1 firmware remains supported.

[UI render with sample values](../docs/screenshots/usb-recording-gpio.png), [validation](../Note/GPIO_PANEL_LEDS_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
