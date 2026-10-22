# V2 BLE catalog / WILD-X1.0.0.92

APPD44907A5 enables BLE Recordings on the checked APP660 recording/Stop baseline.
USB B6099A0E and recovery6408DBB8 are unchanged. INFO/ENTRY are stopped-only and read-only;
degraded/rewind views never offer Delete or silently repair/discard data.

Use `V2_logger_APP_update_D44907A5_BLE_CATALOG_20261022.xenp` for an APP-only recovery update, or `V2_fused_RECOVERY_6408DBB8_USB_B6099A0E_APP_D44907A5_BLE_CATALOG_20261022.hex` through guarded ROM DFU/SWD.
HEX excludes retained settings and includes all other logical flash bytes explicitly.

Stop acknowledges once, then drains and verifies stored data before Complete. GUI90+ observes
this asynchronously without a false30-second failure. Keep power until Complete.

Stopped BLE catalog INFO/ENTRY and explicitly confirmed last-COMPLETE delete checked with actual control/APP/catalog/port native fakes, journal power cuts and degraded/v14 read-only views. APP660 capture/Stop source and59 other objects retained. No hardware access, flash, new recording, all-card/all-mode or installed-pair qualification. USB B6099A0E/recovery6408DBB8 byte-identical; full fused explicit FF except retained settings. GUI92 retains the pending-Stop display fix.
