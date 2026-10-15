# WILDX 1.0.0.33

**Continuous USB quality test:** [Download installer](WILDX_console-Setup-1.0.0.33-USB_QUALITY_CONTINUOUS_20261015.exe).

This feature build adds USB transfer health and a **Continuous** checkbox beside **USB quality test** in recording info. Continuous runs until Stop or the first transfer/integrity error. Uncheck it for the short test. It requires an existing supported SD-download or RAM-diagnostic firmware mode; the app does not change modes automatically. SD-backed timing includes the card.

The exact checked application and exporter were packaged without rebuilding or changing their 1.0.0.33 version. The build includes the recorded concurrent V2 application inputs. Its 84 software checks passed; no hardware endurance test was performed. All 13 extracted installer payload files match the checked files. The installer retains **Run WILD-X**.

[USB quality development and delivery record](../Note/USB_HEALTH_DEVELOPMENT_20261015.md), [feature-build manifest](usb-quality-manifest.json).

The previous general installer remains available below.

[Download installer](WILDX_console-Setup-1.0.0.33.exe).

Data Download now offers **Delete last recording…** and **Format card…**, with confirmation of the selected card. Delete retains earlier recordings; format clears the native recording catalog and retains settings.

These actions require a removable **SD card reader**. Current logger USB download firmware is read-only. Neither action securely erases payload data.

Card-maintenance and application regression checks passed using synthetic cards. All 13 extracted installer files were verified; no physical card was modified. V1 BLE camera/microphone recording, V1/V2 storage source labels, and the installer’s **Run WILD-X** option are retained.

[Release notes](../Note/CARD_MAINTENANCE_20261014.md), [BLE recording instructions](../Note/BLE_MEDIA_RELEASE_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
