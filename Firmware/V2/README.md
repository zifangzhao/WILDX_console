# V2 — deterministic IMU and SD append storage

APP7F238587 / USB74FC7104 / recovery6408DBB8. Three probes at20kHz completed the checked120-second SD recording and normal BLE/USB download handoff. StoredNN continuity and150003 scheduledIMU observations passed; localRTC interval120us, separate PC calibration uncertainty20205us.

NPSD5 explicitly selects SD16 schema3 append storage. Original neural IDs are decoded in logical order; IMU batches keep physical admission order. Real gaps and incomplete segments remain explicit. Cached1250Hz copies do not imply physical sensor conversion-phase lock, complete source overlap, final-tail timing, unlimited stalls or endurance.

Use the Full bundle forAPP+USB+recovery. Omitted device configuration is preserved by the updater. APP-only capsules remain supported; no USB capsule or raw body is installed. Exact scope is in acceptance.json. Historical firmware baseline plus tested incremental overlay is documented in source-manifest.json; full clean rebuild equivalence is not claimed.
