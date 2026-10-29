# WILD-X 1.0.0.165

[Download installer](WILDX_console-Setup-1.0.0.165.exe). Includes the matching V1/V2 firmware and catalog; no separate update ZIP.

V2 passed short USB and SD recordings with three probes at 20 kHz, IMU export, and the app's BLE-to-USB-to-download switching path without an SWD reset. The earlier switching failure came from an incorrect mode selector in the private test helper; production callers already selected USB correctly.

V2 APP EC5C2198, USB A71BDECC and recovery 6408DBB8 are included. V1 and camera firmware remain unchanged. Install the console, then use **Advanced → Firmware update → Use bundled APP** where needed; installation alone does not flash devices.

[Test scope and source provenance](../ReleaseSources/1.0.0.165/README.md). Pre-arm and first-sample synchronization remain disabled while testing continues.
