# WILDX 1.0.0.30

[Download installer](WILDX_console-Setup-1.0.0.30.exe).

V1 BLE camera/microphone recording is now available in the shared BLE page:

1. Connect the logger and select **Camera / Microphone** in Recording profile.
2. Select Camera and its frame rate (default 40 fps), and optionally Microphone.
3. Click **Apply profile** and wait for device confirmation.
4. Use **Start recording** / **Stop recording**. Data are saved to SD for USB download.

The existing bench checkbox is required if the firmware reports an unqualified profile. Device rates are read back. Status refresh no longer dims the controls or rebuilds an unchanged profile menu. The installer retains **Run WILD-X** on its final page.

Software checks passed and all 13 extracted payload files were verified. This GUI workflow has not received a new hardware test; live BLE media preview and V2 media control are not included.

[Release notes](../Note/BLE_MEDIA_RELEASE_20261014.md), [manifest](release-manifest.json), [checksums](SHA256SUMS.txt).
