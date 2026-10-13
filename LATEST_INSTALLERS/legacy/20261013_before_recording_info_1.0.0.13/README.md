# WILDX 1.0.0.12 SR/IMU package — 2026-10-13

[Install the checked console](WILDX_console-Setup-1.0.0.12-SR_IMU_20261013.exe).
The assembly version remains 1.0.0.12; the filename distinguishes this payload
from the earlier same-version installer retained under `legacy`.

USB recording arms sensor headers independently of optional IMU file saving.
Idle USB accelerometer/gyro display polling runs at 4 Hz. The checked GUI is
SHA256 `53E5AFDB5A4C238C7A1AAC8C80F86C63D716B57F55BD1D0E4CD052C78FD9963B`.
All 13 extracted installer runtime files exactly matched the frozen payload.
Existing configuration/updater helpers were reused; nothing was rebuilt.

Matching V2 firmware: recovery 6408DBB8 / USB BFF0A8A2 / logger E07FB882 in
[XE_NP V2 LATEST_FIRMWARE](https://github.com/zifangzhao/XE_NP/tree/V2/LATEST_FIRMWARE/V2_NP2).
The exact pair passed a short real three-probe 20 kHz USB capture with zero
reported RAW/DAT drops, 584 distinct IMU observations and 125 valid AG display
callbacks. Magnetometer was unavailable (error 9); six-axis evidence only.

Shared configuration/IMRO, diagnostics and updater functionality are retained.
The existing overrun recovery produces separate segments and cannot restore
lost intervals. No endurance, actual damaged-shank, all-rate, rendered-GUI,
clean install/uninstall or fused boot-cycle qualification is claimed.
Triple 30 kHz USB remains unsupported. Installing this installer does not flash
firmware. See [exact payload/pair manifest](release-manifest.json).
