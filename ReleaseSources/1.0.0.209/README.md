# WILD-X1.0.0.209

BLE recording now uses a destination selected before Start. PC-side session metadata, probe/configuration requests, RTC/clock evidence, sensor observations and GPIO logs stay with that session. A small discovery locator lets a later SD download find the chosen folder while retaining exact device/token checks.

GPIO/RTC has an explicit enable checkbox. Its independent logger preserves continuity across neural interruptions and reconnects; disabling or explicitly stopping it remains a separate action. Preview calibration provenance is retained without relabeling the raw SD recording as calibrated.

Firmware and catalog are byte-identical to207: V1APPFFF283D9/USB1E73A743 and V2APP7F238587/USB74FC7104, with the same checked recording scope. This release changes host folder/UI paths and evidence discovery; it adds no recorder wire protocol or new hardware precision claim. Local deviceRTC uncertainty and PC calibration uncertainty remain separate.

Build208 completed the SDK but found a GUI-only access error. Build209 fixes that call and reuses the identical SDK after all50 shared SDK source inputs matched. Focused session/GPIO/clock/importer checks and the actual209 SDK export boundary passed.
