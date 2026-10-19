# WILDX V1 battery-only BLE startup fix

[Download the latest installer](WILDX_console-Setup-1.0.0.44-V1-battery-BLE-fix.exe) (embedded version **1.0.0.44**).

Includes V1 high-speed USB **76E544AB**, fixing the virgin USB-cancel state that
could block SD/BLE startup on battery power. **SOFTWARE-VALIDATED, NOT
DEVICE-TESTED**, published at the user's request without on-device tests.

The current fixed GUI, APP **654824DE**, recovery/boot, exporter and V2/camera
firmware are unchanged. The exact matching component/fused HEX files are
included. The separate V2 BLE-listing candidate is not included in this bundle.
Setup includes **Run WILD-X**. All **46 extracted payload files** matched.

No B34D50 end-to-end fix or complete matched-stack qualification is claimed.
APP-only updates do not install the USB fix. Preserve configuration and SD
recordings; the fused image excludes retained configuration. High-speed USB
is retained; forced full-speed is excluded.

This root contains one final installer; [previous package](legacy/20261019_before_battery_ble_fix/README.md).
[Fix and limits](../Note/V1_BATTERY_BLE_STARTUP_FIX_20261019.md) ·
[Firmware](../Firmware/V1/README.md) · [Manifest](release-manifest.json) · [Checksums](SHA256SUMS.txt).
