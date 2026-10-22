# WILD-X 1.0.0.72

[Installer](WILDX_console-Setup-1.0.0.72.exe).

BLE scanning starts when the app opens. The first discovered device fills the
address automatically; explicit selection follows the dropdown. Advertisement
updates preserve manually entered addresses. Background scanning keeps USB
controls available. The V1 USB device label is now `NP1 USB`.

17 actual-app selection checks passed with simulated advertisements; startup
wiring and exact source scope were checked. No radio/device or installed-UI
qualification was performed. SDK, firmware and helpers are unchanged from71.

[Exact manifest](release-manifest.json) / [change and validation](../Note/BLE_AUTO_DISCOVERY_20261021.md)
/ [compiled source inputs](../ReleaseSources/1.0.0.72/README.md).
The previous installer is retained under `legacy/20261021_before_1.0.0.72`.
