# WILD-X V2

[Install 1.0.0.165](WILDX_console-Setup-1.0.0.165.exe). The installer includes APP EC5C2198, USB A71BDECC, recovery 6408DBB8 and the matching catalog.

The app's BLE USB-mode handoff, Data Download listing and native DAT export passed on V2 with three probes at 20 kHz, without SWD attachment/reset or a physical reconnect. Saved SD IMU data was also checked. These are short tests on one logger/card; [results and limits](../ReleaseSources/1.0.0.165/validation-summary.json) are included.

Use **Advanced → Firmware update → Use bundled APP** if the device needs the bundled APP. The firmware bytes are unchanged from release 158; no separate update ZIP is needed.
