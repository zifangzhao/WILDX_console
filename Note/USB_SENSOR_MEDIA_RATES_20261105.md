# USB magnetometer and recording reliability

WILDX 1.0.0.238 selects these checked components. Detailed results and identities
are in the [release qualification](../ReleaseSources/1.0.0.238/qualification.json).

| Family | SD APP, unchanged | New USB service | Checked USB detector profile |
|---|---|---|---|
| V1 | 5F90FD3B | C91871BD | Two probes, 15 kHz, one/two Ripple detectors |
| V2 | 99CC44F9 | D43FE162 | Three probes, 20 kHz, one/two Ripple detectors, P2 ADC0/1 |

Apply the bundled USB firmware to obtain the increased magnetometer rate. An
application update alone cannot change the device sensor configuration. The
installer contains matching component and fused images; fused packaging reuses
the checked components rather than claiming a separate whole-image hardware test.

## What changed

USB now uses the same nominal 200 Hz accelerometer/gyro and 100 Hz magnetometer
profile as SD. The previous USB objects selected 25 Hz MAG. Both configuration
writes and readback expectations have been updated. The 9 XY / 15 Z repetition
settings, compensation, gains and existing data formats are preserved. Bosch
lists 100 Hz as the maximum forced-mode rate for this regular precision preset:
[BMM150 datasheet, Table 3](https://www.bosch-sensortec.com/media/boschsensortec/downloads/datasheets/bst-bmm150-ds001.pdf).

The V2 mandatory IMU provider now has a bounded service opportunity while the
primary USB transfer is active, with coherent producer/queue/headroom guards.
NP submission remains first. Camera/ADC admission and the 256-row IMU queue have
not been enlarged or given higher priority. Grid/phase/end headers and bodies
are submitted together from existing contiguous storage, preserving the exact
wire bytes without an extra payload copy or a second processing pipeline.

A contiguous optional-detector pressure episode now retains its complete
unprocessed source interval and produces one recorded GAP/reset rather than
repeatedly resetting queues and flooding the event carrier. Existing WILD
filters, resampler and full training duration are unchanged. Actual TRAINING
and READY source ticks remain the authority for detector validity. Recording
T0 and NP/IMU start handling are unchanged; no GUI recording Start gate was added.

## Development evidence

| Check | Result |
|---|---|
| V1, detectors off/one/two | Three 60-second checks passed; MAG99.53–99.74 Hz, AG198.89–199.40 Hz; no midstream NP loss or later missing/error IMU rows |
| V1 restart | Two-detector 30-second restart passed, including exact native LFP/event replay |
| V2 detector off | Unchanged inactive-detector path reused from USB72C9F2C6: 47,152 verified NP blocks, 75,443/75,443 retained IMU rows |
| V2 one detector, final D43 | 61.368 s; 47,928 verified NP blocks, 76,685/76,685 IMU rows, MAG99.52 Hz; zero source/queue/carrier loss, exact native-to-DAT replay |
| V2 two detectors, final D43 | 60.378 s; 47,152 verified NP blocks, 75,443/75,443 IMU rows, MAG99.51 Hz; zero source/queue/carrier loss, both native inputs match DAT |
| V2 reset-separated restart | 11.379 s; 8,868 verified NP blocks, 14,189/14,189 IMU rows; strict detector/native checks passed |
| Forced software cases | 133 guarded-admission checks, 238 actual-function transfer/lifecycle checks, 41,470 coalescing/WILD training checks |

The final hardware detector runs passed the original strict zero-gap predicates;
no startup waiver was used. The forced native pressure case separately verifies
that 400 skipped frames in 20 ms produce one complete GAP and one reset, followed
by the unchanged full one-second training at its true source time. Separate
episodes, post-READY interruptions, Stop and different-fault ordering are covered.

Earlier failures remain recorded: USBBAEE8DAD lost 253 IMU rows while NP remained
clean; USBD38D68E4 retained IMU but overran the primary PSSI/USB path; intermediate
USB72C9F2C6 retained NP/IMU but its first detector test flooded 273 startup pressure
notices and dropped 11 carrier records. These candidates were not published.

Both devices' exact original firmware, options, preferences and profiles were
restored and hardware leases closed. More than 20 GB of generated neural and
timestamp payloads were removed after securing compact replay/failure windows,
metadata and required restore backups. User recordings were not deleted.

## Limits and reuse

These are bounded development checks, not endurance or all-profile qualification.
V1 startup trimming and its one unavailable initial grid slot remain explicit.
Both families retain their existing terminal/source-window coverage limits.
The 1250 Hz scheduled cached-value grid is not 1250 Hz physical sensor acquisition
and does not establish sensor conversion phase lock. Instrumented V2 detector
cost was approximately 6.09%/7.67%, excluding other work; this is not total CPU or
power consumption. Any actual later pressure gap remains a logged interruption
requiring fresh training, not a gap-free detection claim from NP time zero.

The SD applications, boots and checked SDK EBC98 are unchanged and reuse existing
qualification. All 204 application build inputs match the checked237 inputs
except the assembly/file version. The new build is238; catalog and package
identities are checked separately. Immutable software-source snapshots preserve
their original pre-hardware status; final acceptance/restoration is recorded in
separate qualification and closure receipts.

USB power removes the battery budget but not bus, CPU or peripheral limits.
Higher AG rates need faster readout/polling and matching diagnostics/metadata;
the present 5 ms poller cannot preserve them by changing ODR alone. The V1 USB
camera-only UI requests1–58 fps, clamped by the device's runtime SPI limit. V1
live USB audio is unavailable; V2 has a200 kHz electrical ADC producer whose host
media integration is separate work. Camera and V2 SPI-selected closed-loop share
SPI1/GPDMA C15 and remain mutually exclusive. No new camera/audio, pulse-output,
network-clock or maximum simultaneous-media claim is made by this update.
