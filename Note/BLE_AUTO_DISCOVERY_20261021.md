# BLE startup discovery and address selection — 1.0.0.72

The main window starts BLE discovery after it is first shown, including when
USB Mode is the selected tab. The embedded BLE form creates its callback handle
before starting the watcher. Manual Scan reuses the same path. Scan failures
appear in BLE status; the app remains available. Discovery does not connect to
or issue commands to any device.

The first discovered compatible device becomes the default selected item and
fills the empty BLE address field. Selecting a different item updates the address.
Refreshing advertised names/RSSI does not overwrite a typed or partially typed
address. An empty address falls back to the current selection when connecting.
Passive discovery no longer owns a connected BLE session or disables USB controls;
active BLE commands and connections retain their existing ownership.

The coordinated device label change uses exactly `NP1 USB` for the V1 USB device
choice. No channel configuration, decoder or firmware descriptor changes.

Build72 uses the99 recorded GUI compiler/build inputs from published71 with only
NpBleForm.cs, mainForm.WildX.cs, the single mainForm.cs label, and the normal
assembly revision changed. SDK F485AFE1… and all40-file bundle dependencies,
firmware and helpers are unchanged except the GUI EXE.

17 actual-app checks exercise first discovery, multiple peers, explicit selection,
typed/partial addresses during repeated advertisements, RSSI/name refresh, busy
and closed forms, and ownership. Startup hook, passive ownership, exact NP1 label
and the four expected source deltas are also source-checked. No radio was started,
device connected, firmware changed or hardware qualification performed.

Run `test_ble_discovery.ps1 -ConsoleExecutable <absolute path to WILDX_console.exe>`.
Local build evidence: `_artifacts/ble_discovery_20261021`.

GUI SHA256: `31A8B2C22273453CFF22DDBAD52F645306DF2051C98D94910EC92D9AB56A903B`.
