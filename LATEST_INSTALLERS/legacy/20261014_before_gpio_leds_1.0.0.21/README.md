# WILDX 1.0.0.20

[Download the installer](WILDX_console-Setup-1.0.0.20.exe).

USB recording now displays elapsed time, actual recording-folder size and available disk space. GPIO / RTC starts the selected WILD board with recording and saves event, clock-query and session files beside neural data. Its panel below Device health shows COM/state, per-channel counts, latest event and clock status, retaining final values after Stop.

GPIO failures warn and allow neural recording to continue. Sensor, GPIO, metadata and capture warnings appear once per category per recording. Fatal neural errors still stop recording. Existing V1 firmware remains supported; no firmware upgrade is required. BLE SD recording is not yet connected to the local GPIO logger.

Software checks passed with a simulated serial device and ordinary local files. All 13 extracted runtime files match the checked payload; all 12 non-GUI files are unchanged from 1.0.0.19. No hardware or installer execution. Physical V1/V2/GPIO qualification remains pending.

See [validation and workflow](../Note/RECORDING_STATUS_GPIO_RTC_20261014.md), [manifest](release-manifest.json), and [checksums](SHA256SUMS.txt).
