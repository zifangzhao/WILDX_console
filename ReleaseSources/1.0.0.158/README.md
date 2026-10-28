# WILD-X 1.0.0.158 — IMU and SD finalization

This update prioritizes IMU delivery and a shorter SD Stop. The normal installer includes the matching V1/V2 APP firmware, existing USB services, boot/recovery images and catalog. Install the console, then use **Advanced → Firmware update → Use bundled APP** for each logger. Installing the console alone does not change firmware on a connected logger. Older firmware remains usable; unsupported optional IMU queries are skipped.

V1 gains a capability-advertised, coherent cached BLE IMU query and lets sensor initialization finish before SD acquisition starts. V2 stores optional compact sensor snapshots in SD blocks and exports them to `analogin.dat`, with a validity file and encoding metadata. Neural payload bytes remain unchanged; invalid optional sensor metadata is reported separately. The 1,250 Hz analog grid holds sensor readings between updates. It is not the native sensor conversion rate.

V2 Stop drains accepted writes and commits the footer/catalog without automatically rereading every recorded payload block. V1's writable recording path already omitted that full reread. Critical storage checks and PC download/export validation remain. A finalized recording is a completed storage transaction; it does not claim an independent full payload readback.

The APP sensor profile is accelerometer/gyro 200 Hz, ±8 g and ±2000 degrees/s, with magnetometer 100 Hz. The retained USB firmware keeps its existing working IMU profile, including 25 Hz magnetometer. USB/boot components were deliberately reused for this update.

Pre-arm and the new NP-start-byte synchronization are deferred and disabled in this release. Current timing metadata retains its stated uncertainty; this release makes no new claim that physical IMU conversion is locked to the first NP byte. The closed-loop firmware prototype is excluded.

Focused software checks cover IMU units/ranges/grid, sparse SD snapshots, exact neural DAT parity, optional sensor-error isolation, BLE priority/retry, recording flow, catalog behavior and Stop ownership. Full firmware links and sealed image/partition checks are recorded under `firmware/`. The new APP images were not flashed or tested on hardware for this update. No recordings or cards were changed.

See [package identities](package-verification.json), [host build inputs](build-receipt.json) and [software checks](checks/software-validation.json). `compiled-sources.zip` contains the host build inputs; firmware overlays and retained-object provenance are supplied separately under `firmware/`.
