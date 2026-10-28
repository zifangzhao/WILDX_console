# V1 USB 1.0.108 / APP B1758369

Adds cached, capability-advertised BLE IMU access and finishes sensor initialization before SD capture. Existing compact preview and recording paths are retained. APP accelerometer/gyro: 200 Hz, ±8 g / ±2000 degrees/s; magnetometer: 100 Hz. Unchanged USB retains its existing 25 Hz magnetometer profile.

The APP was incrementally compiled, linked and sealed with focused native checks. It has not been flashed or hardware-tested for this update. Pre-arm, new first-sync and closed-loop firmware are excluded. V1's writable SD path already had no automatic full payload reread after Stop; critical finalization and host validation remain.

Includes matching fused/component images with retained configuration excluded. Exact current identities and limits are in the catalog and [release evidence](../../ReleaseSources/1.0.0.158/firmware/V1/). The older acceptance.json describes the previous APP82 release.
