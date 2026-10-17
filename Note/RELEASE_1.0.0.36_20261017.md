# WILDX 1.0.0.36 — BLE recording settings and Stop confirmation

BLE Recording settings reuses the USB dialog: separate probe checkboxes and AP
sample rate, with the stacked probe editor inside. The main BLE page shows a
summary instead of the combined profile list, external probe button and Apply
button. Device detection and firmware capability admission remain authoritative.
V1 two-probe 20 kHz is available only for P1+P2 when advertised. Media controls remain
available for firmware that advertises camera/microphone recording; host DAT
merge/resample/misc options are not presented for an SD capture.

Start sends SELECT only when settings need changing, waits for a fresh matching
stopped/nonpending status, stages explicit probe requests and sends one START.
A Stop request during host preparation cancels the pending start after the
current transfer finishes. It does not cancel a partially written BLE frame.
Uncertain SELECT/START/STOP commands are never blindly replayed.

Stop remains clickable during host startup work. It reads current status,
waits up to 30 seconds for an earlier device command before sending one STOP if
needed, and then observes status for up to 30 seconds to confirm stopping. A
timeout or connection change is reported explicitly, not as confirmed stopped.
V1 failed state4 is a stopped outcome only after active/pending/preparing/armed
ownership clears. It retains fault2 and incomplete-data status rather than
waiting for Complete3. V2 inactive failed state255 is handled separately from
normal Complete5. Faults are never cleared merely to make Stop appear successful.

V1 state4/fault2 means NP_SD_FAILED/NP_SD_SOURCE: probe initialization, capture
startup or acquisition-stream failure; media source failures share this code.
SD_WRITE is fault4. The exact underlying cause on the two reported external
V1 devices is not established. The V1 developer separately observed a successful
P1+P2/native20k capture and STOP on his known bench device, but saved-data strict
readback was not completed. That result does not validate these external units
or this new installer. No hardware, flash, format, delete or firmware build was
performed by this console release task.

Focused offline verification against the final executable passed:

| Checks | Count |
| --- | ---: |
| Actual BLE settings, supported combinations and fault/stopped text | 23 |
| Actual Apply/Start/Stop handlers with fake transport | 38 |
| Actual media flow with fake GATT, including queued Stop | 41 |
| Actual media framing/export | 116 |
| Actual BLE continuous-recording layout | 13 |
| Actual runtime profile population/routing | 27 |
| Actual V1/V2 BLE workflow | 4397 |
| Native BLE transactions with fake GATT | 17 |
| V2 catalog contracts with simulated firmware | 1945 |
| Actual V1 existing-firmware profile compatibility | 103 |
| Shared USB/BLE dialogs and import workflow | 106 |

The actual [BLE page](../docs/screenshots/ble-main.png),
[V1 settings](../docs/screenshots/ble-recording-v1.png) and
[V2 settings](../docs/screenshots/ble-recording-v2.png) were rendered without
device discovery or Shown hardware handlers. The entire project suite was not
repeated; relevant compatibility and changed control paths were checked.

GUI SHA256: 0AF9CD0732D4EFC1FFECBA0136F473E84E3C680E832013E1D5FF82AE0500FA58.
Installer SHA256: 37A546811A5BBB551A2B62BE12566F7A67907BFD0FDBE53978254425E1694005.
Independent extraction verified all 37 runtime/firmware files. The 36 inherited
files, including exporter, helpers and all firmware images, match 1.0.0.35 exactly.
No firmware upgrade is required for the console changes. One final installer
remains in LATEST_INSTALLERS, with 1.0.0.35 and its original metadata archived.
The experimental USB download-readiness source and its test remain excluded.

All [1.0.0.35 firmware target limits](RELEASE_1.0.0.35_20261017.md) and
[1.0.0.34 qualification limits](RELEASE_1.0.0.34_20261017.md) remain applicable.
