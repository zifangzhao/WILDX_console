# USB mode controls — 2026-10-17

Normal USB mode controls now live directly on Advanced, beside Firmware Update and Diagnostics & self-test. USB streaming, USB download and SD / BLE use the selected V1/V2 device. Normal mode buttons have been removed from the firmware dialogs. Firmware update, recovery entry, guarded ROM DFU and explicit recovery exit remain in the firmware workflow.

V1 reuses its existing management protocol, exact UID validation, fresh idle/pending checks and bounded mode verification. The extracted mode helper is shared with recovery exit. V2 reuses the bundled exact-UID helper and its existing --stream / --download / --sd operations. The UI adds no new firmware requests or firmware upgrade requirement.

Mode actions use the existing exclusive device-operation gate: capture, impedance tests, device/status refresh, recovery, USB tests, BLE ownership and data downloading block them. Closing the app during an active mode transition is deferred. SD / BLE retains confirmation because USB disconnects. No mode transition runs on page entry.

Validation: private incremental GUI build succeeded; 639 offline checks against the actual compiled assembly passed. These cover mode placement and mapping, V1/V2 idle availability without stream-settings reads, ownership/busy gates, closure protection, firmware recovery exit gates and minimum-width control layout. The real Advanced controls were rendered in an offscreen host at 1000 px width; reviewed advanced-usb-modes.png. Device Shown discovery and all hardware operations were excluded.

Private build: _artifacts/usb_mode_controls_20261017/bin/WILDX_console.exe
SHA-256: CDDF1CF02541E37B6965E4E814E96FB564E3333E21BD57C1706C738889484189

All 92 compile inputs were recorded and rechecked. The separate dirty NpDownloadStream readiness edit remains excluded using the previously checked source override. Existing unrelated shared-tree work was preserved. No firmware, device/card state, canonical Release binary, installer, commit or remote was changed. LATEST_INSTALLERS still contains only the existing V2_SD_META13 final installer.