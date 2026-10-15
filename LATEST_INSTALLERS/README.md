# WILDX 1.0.0.33

**USB quality fixes and capture-panel split:** [Download installer](WILDX_console-Setup-1.0.0.33-USB_QUALITY_FIX_20261015.exe).

The original Capture activity area is split into recording information on the left and USB status/testing on its right. Device health, GPIO/RTC and Network time retain their existing right column. **Continuous** runs until Stop or the first transfer/integrity error; uncheck it for the short test.

V1 acquisition firmware can use its existing synthetic SRAM test where supported, without a card or probe recording. SD-download mode still needs a readable, idle card; its timing includes storage. V2 retains the supported download/diagnostic test path. The app does not switch modes or update firmware automatically. Stopping an interrupted SD test now retires that test's own pending read.

The exact checked application and exporter were packaged without rebuilding or changing their 1.0.0.33 version. The build includes the recorded concurrent V2 application inputs. Its 113 software checks passed; no hardware test was performed. All 13 extracted installer payload files match the checked files. The installer retains **Run WILD-X**.

[USB quality development and delivery record](../Note/USB_HEALTH_DEVELOPMENT_20261015.md), [feature-build manifest](usb-quality-manifest.json).

The previous general installer remains available below.

[Download installer](WILDX_console-Setup-1.0.0.33.exe).

Data Download now offers **Delete last recording…** and **Format card…**, with confirmation of the selected card. Delete retains earlier recordings; format clears the native recording catalog and retains settings.

These actions require a removable **SD card reader**. Current logger USB download firmware is read-only. Neither action securely erases payload data.

Card-maintenance and application regression checks passed using synthetic cards. All 13 extracted installer files were verified; no physical card was modified. V1 BLE camera/microphone recording, V1/V2 storage source labels, and the installer’s **Run WILD-X** option are retained.

[Release notes](../Note/CARD_MAINTENANCE_20261014.md), [BLE recording instructions](../Note/BLE_MEDIA_RELEASE_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
