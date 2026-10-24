# BLE SD clock evidence — private132

This implements the independent host preparation for long-recording drift
measurement. It does not correct AP rate or generate AP timestamps. Repeated
AP-source-position/MCU pairs and their firmware contract still need approval.

The existing read-only B9 NPT2 op0 exchange now retains versioned JSONL evidence:
raw64B request/reply, transaction ID, host send/receive QPC and shared-PC Unix
coordinates, MCU Rx/Tx milliseconds, clock-mapping generation, transport and
host epochs, firmware mapping validity/uncertainty, and available exact MCU UID.
Missing boot domain/acquisition generation/AP pairs remain explicitly null.
The device's clock-mapping generation is not relabeled as an acquisition epoch.

The connection journal is ble_clock_exchange_evidence.jsonl beside the existing
clock_observations.csv under the connection's LocalAppData/WILDX/Sync folder.
When a host BLE recording receipt exists, its observations are mirrored into
ble_clock_<journal-session>.jsonl in that receipt folder. Each row retains the
host context; on-card recording association remains explicitly unverified.
Reconnect opens a new transport journal. Started requests without a final
receipt, closure, cancellation, timeout and deferred coverage are explicit.
Metadata I/O errors cannot fail recording/control commands.

The bounded periodic collector runs only with a fresh (<=5s), device-confirmed
healthy recording status and advertised clock capability256. It uses state1
for V1 and state3 for V2, at most one new query per5s, with a750ms cancellation
budget and15s failure backoff. Existing unsupported firmware remains usable.
The query never commits/sets the clock, changes profile, Starts/Stops, or replays
a recording command. Existing stopped time-sync behavior is not changed.

Priority is recording/control/health, then IMU, then periodic clock evidence,
then waveform preview. IMU and foreground work cancel/retire the optional clock
request; fixed UART writes finish coherently before cancellation is honored.
The existing foreground status polling no longer invokes its normal clock UI
heartbeat during V2 active capture; the background collector owns that read.
No clock observation is taken from waveform arrival or SD download arrival.

New files: Ble/NpBleClockEvidence.cs (schema, lifecycle, file-only policy),
Ble/NpBleConnection.ClockEvidence.cs (read-only lane/context/priority),
Ble/NpBleForm.ClockEvidence.cs (confirmed-status scheduling).
Hooks are in NpClockJournal.cs and existing BLE connection/form/metadata/IMU
lifecycle files. Neural workers, sample decoders, timestamp correction engines,
IMU sensor parser and the SDK binary remain unchanged from private130.

Validation: private132 passes43 actual-compiled evidence/schedule/priority
checks and366 actual-compiled IMU checks. The actual-source preview regression
passes6,107 checks with a radio double. Tests preserve raw brackets and source
bytes, reject commits, retain invalid/unfinished receipts, split reconnect
epochs, exercise absent device mapping/identity, preserve original data, enforce
V1/V2 state/capability/freshness, and verify IMU/waveform priority without radio.

No hardware access, V2 firmware edit, firmware install, installer, publication
or Slack post was performed. Private129/130 and their evidence are preserved.
V2 power restoration and SD292 full data readback remain on hold with the device
owner. Future work requires Frank's V2 approval and a finalized acquired-source
pair contract plus exact SD/DAT association, coverage/uncertainty semantics and
matched hardware qualification. No AP-drift or physical timing-accuracy claim
is made from this preparatory evidence.
