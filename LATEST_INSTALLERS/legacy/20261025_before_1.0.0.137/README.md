# WILD-X 1.0.0.133

[Download installer](WILDX_console-Setup-1.0.0.133.exe).

Fixes overlapping BLE background polls and stale IMU readings, prioritizes IMU over waveform preview, and saves raw periodic clock observations. Includes the PC-time correction and SD-test workflow from128.

Existing firmware remains supported. BLE IMU needs compatible device firmware; the private V2 IMU APP and high-resolution timing firmware are not bundled. AP drift correction and joint physical NP/GPIO timing remain under development.

All 43 packaged files were extracted and hash-verified. [Manifest](release-manifest.json) / [source and validation](../ReleaseSources/1.0.0.133/README.md).
