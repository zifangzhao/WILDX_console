# V2 optional BLE IMU snapshots

Private WILDX1.0.0.129 implements the agreed read-only NPSQ/NPSR extension.
Private130 adds the human-requested priority: NP recording/controls, then IMU,
then waveform preview. The900ms host deadline and1s device cache TTL are unchanged.
The V2 firmware owner owns hardware and pair validation. No installer or
publication is authorized by this coordination step. Published128 is unchanged.

The outer command is **B9**, using the retained online wrapper and fixed64B
control body with the existing NP CRC32 at60 over bytes0..59. B8 configuration,
NPT2/NPR2 time commands, and BA waveform preview are unchanged. Firmware uses
the existing AD B9 / DA DA envelope; the host also accepts its existing <> frame.

All fields below are little-endian uint32 unless described as bytes.

| Offset | Request | Reply |
| --- | --- | --- |
| 0 | NPSQ / 0x5153504E | NPSR / 0x5253504E |
| 4 | Version1 | Version1 |
| 8 | Op0 latch + first chunk; op1 read | Echo op |
| 12 | Fresh nonzero transaction ID | Echo ID |
| 16 | Token0 for latch; opaque token for read | Error0..4 |
| 20 | Offset0/28/56/84/112 | Nonzero cached-view token on success |
| 24 | Zero | Echo offset |
| 28 | Zero | Total140 on success |
| 32..59 | Zero | Exact28B slice of frozen SNS1 |
| 60 | CRC32 | CRC32 |

Op0 requires token0/offset0. Op1 requires a nonzero token and offsets28/56/84/112.
Request reserved bytes24..59 are zero. Error1 is bad request,2 unsupported or
not initialized,3 stale/expired token,4 paused/busy. Error replies echo op/ID/
offset with token0, total0 and zero payload. Each new latch replaces the cache;
its nonzero token expires after1000ms or reset. Reads never refresh the view.

Firmware latches one coherent copy of the already-published140B SNS1 version1
structure and refreshes validity on that private copy while preserving actual
published/completion ticks. It does not initiate I2C, write SD, or change neural
capture. The optional stream is sparse; it is not a200Hz FIFO transport.

The host requires five matching chunks, one token/total, exact offset, CRC,
request identity, and an unchanged BLE connection epoch. Partial views never
reach the parser or journal. Its opportunistic semaphore acquisition does not
queue ahead of foreground commands. Cancellation waits for any current fixed
UART command write to finish, then retires the optional view. The form polls at
most1Hz and preempts for status/foreground work. Initial129 skipped polling while
the waveform window was open and disabled probing after a timeout. Version130
removes that blanket pause: the complete IMU read holds a pending-priority flag,
so new waveform requests/renewals yield while buffered plots can still paint.
Only an explicit firmware unsupported reply disables the optional capability.
Timeouts, expired/busy views and transport failures retry automatically with
bounded1/2/4/8s backoff. Recording state and commands are never replayed by this
recovery. NP acquisition and USB processing code are unchanged.

Complete views reuse NpUsbSensors and NpImuSampleJournal. The GUI shows sensor
values/validity. During host-started device-confirmed BLE recording it saves
distinct valid native observations in that recording's host receipt folder,
including exact MCU UID, counters/values and host receipt provenance. It writes
no held-value analogin DAT. BLE receipt time is not substituted for sensor time.
This host lane does not add IMU storage to the on-card neural format.

Headless production API: internal NpBleConnection.ReadImuSnapshot(CancellationToken)
returns Task<byte[]> on an existing caller-owned connection. Null means skipped.
NpBleImuUnavailableException.Error exposes firmware error1..4. For the exact
parser/journal/display handler, construct NpBleImuSession(folder,exactUid), call
Accept(snapshot), and dispose after recording. Folder=null is display-only.
The caller owns profile, recording commands, poll frequency and cancellation.

Build129 passed342 actual-compiled wire/client/parser/journal/priority checks
without radio/device access. Hardware recording integrity remains with the V2
firmware owner's pair tests; the source/binary receipt is under
_artifacts/v2_ble_imu_20261023.

Build130 passed366 actual-compiled IMU protocol/handler/priority/retry checks and
6,107 actual-source waveform checks with a radio double. There is no claim yet
of simultaneous waveform/IMU hardware qualification. The firmware owner has
reported six valid advancing SNS1 views during a P123/20k recording using129,
with no NP fault and562–598ms complete-view times; full NP export/readback is
still pending in that report.
