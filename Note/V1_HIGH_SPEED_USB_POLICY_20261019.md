# V1 USB policy — updated 2026-10-19

Use high-speed USB. Current published-bundle preparation uses USB76E544AB for
the battery-only virgin-cancel startup fix, unchanged APP654824DE and recovery.
It is software-validated, not device-tested, at the human's explicit request.
Reuse the owner-provided matching fused image. Forced full-speed FACEA0D2 and
the private APP60EC609E power trial are excluded. V1Camera and V2 stay separate.

The earlier corrected direct-path raw HS result (USB02FF93A9/r7, private power
trial APP preserved) is historical evidence, not new service acceptance.
See [current fix and limits](V1_BATTERY_BLE_STARTUP_FIX_20261019.md).
