# V2 APP EC5C2198 / USB A71BDECC / recovery 6408DBB8

APP adds sparse SNS2 IMU snapshots to SD recordings and removes the automatic full payload reread after Stop. Accepted writes still drain and the footer/catalog must commit before completion. PC download/export validation remains. Existing two-channel 500 Hz BLE preview and neural payload layout are retained.

APP accelerometer/gyro: 200 Hz, +/-8 g / +/-2000 degrees/s; magnetometer: 100 Hz. The unchanged USB service retains its working 25 Hz magnetometer profile. The host's 1250 Hz analog grid holds native sensor readings; new NP-byte sample locking is deferred.

Four production APP units were rebuilt, linked and sealed; retained USB/recovery and non-APP fused contents remain exact. Memory reservations fit, and focused sensor/header/finalization checks passed. No new hardware test or flashing occurred. Pre-arm, first-sync and closed-loop firmware are excluded. No separate update ZIP is needed.

[Current source and validation evidence](../../ReleaseSources/1.0.0.158/firmware/V2/). Older acceptance and package-receipt files describe the prior APP C250DF48 release.