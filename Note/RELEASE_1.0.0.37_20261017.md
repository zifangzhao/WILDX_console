# WILDX 1.0.0.37 — online recording time

BLE Mode now displays **Recording time HH:MM:SS** above recorder status and
includes it in the BLE report. V1/V2 neural time reuses the existing native
committed-data duration decoders, including the selected physical probe count
and AP rate. V1 media uses microphone sample count when audio is enabled,
otherwise camera frame count. No new device commands or firmware are required.

The display updates in whole seconds with normal status polling. It represents
recorded-data progress, which can lag wall time while buffers drain; dropped
camera frames are not invented to extend the duration. It works on reconnect
without relying on the host observing the original START. The final counter
value remains after Stop/fault, fresh counters reset it for a new capture, and
hours do not wrap after 24. A failed status poll retains the last known time
alongside the existing status-read error.

USB already has a persistent Elapsed / Recorded size / Disk free display.
Its delivered-GUI regression passed 16 checks, including V1/V2 first-data start,
no preparation time, live file growth, Stop retention, fresh recordings, stale
callbacks, >24 hours and resized rendering. The new BLE display passed 28
actual-app checks with synthetic statuses; media flow passed 44 source checks
with fake GATT and 116 existing actual-app framing/export checks. The actual
[render](../docs/screenshots/ble-recording-time.png) uses synthetic V1 status,
not a hardware recording. No hardware, firmware build, flash or card actions
were performed. The full project suite was not repeated.

GUI SHA256: 8A5558A467AB7EA69C95C0299CE7EC97A8B51FF226CF2A2889A2ADC2CE269BD7.
Installer SHA256: 026BB8A64BAE62DE0C0DF587CFEA82ADAF9829276F9D3E70B6BDE9AF834D6622.
Independent extraction verified all 37 payload files. All 36 inherited files,
including exporter, helpers and firmware, match 1.0.0.36 exactly. One final
installer remains in LATEST_INSTALLERS; 1.0.0.36 and its exact metadata are archived.

The [1.0.0.36 control fixes and unresolved V1 source-fault limits](RELEASE_1.0.0.36_20261017.md)
remain applicable. Experimental USB download-readiness edits remain excluded.
