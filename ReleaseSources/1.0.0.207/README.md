# WILD-X1.0.0.207

V2 now supports deterministic1250Hz cached IMU copies and explicitly negotiated SD append storage. The PC indexes original neural block IDs, preserves physical IMU order, and retains real later data segments across storage gaps without padding or invented capture-loss counts. Indexing, download/decoding and finalization show separate progress. Older firmware and recordings remain supported.

The matched V2 three-probe20kHz120-second SD recording passed storedNN/IMU/RTC checks:93752 neural blocks,150003 observed IMU slots, and no reported neural, queue, scheduler, AUX or unsaved-row loss. Local deviceRTC-to-firstNP interval was120us; PC calibration uncertainty was20205us and is separate. Normal BLE-to-USB download and finalAPP/idle handoff passed;14 prior catalog records were preserved.

V1 andCamera firmware and the28 checked runtime companions retain their previous identities. New V2 APP7F238587/USB74FC7104/recovery6408DBB8 are bundled. No USB update capsule or raw firmware body is installed.

Upgrade: Advanced > Firmware update window. Select the exact deviceUID and matching bundled family, choose Full bundle: APP + USB + boot, then Update bundled firmware. APP-only does not updateUSB. The updater preserves omitted configuration bytes using the verified backup.

Cached copies can repeat asynchronous sensor conversions. Physical conversion phase, full source overlap, final-tail timing and endurance remain unqualified. Old recordings without the start receipt do not retroactively gain a preciseRTC anchor. Firmware source provenance is the recorded historical baseline plus the exact tested incremental overlay, not a full clean rebuild.
