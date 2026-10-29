# WILD-X 1.0.0.189

[Download installer](WILDX_console-Setup-1.0.0.189.exe). Matching firmware and catalog are included; no separate update ZIP is needed.

Includes the V2 hardware RTC update (APP 46DF5009, USB 62910614, recovery 6408DBB8), saved first-sample timing evidence, IMU export and shorter Stop finalization. A 30-second SD recording with three probes at 20 kHz passed neural and IMU checks. The first NP start was bounded to a 120 µs full interval relative to the device RTC; PC synchronization uncertainty is separate.

V1 firmware remains APP B1758369 / USB 4F7B521A while its new RTC firmware is tested. Installing the console does not flash devices. Use **Advanced → Firmware update** to select the bundled firmware.

[Validation and limits](../ReleaseSources/1.0.0.189/README.md). V2 USB acquisition with this firmware and alignment of every stream remain under investigation.
