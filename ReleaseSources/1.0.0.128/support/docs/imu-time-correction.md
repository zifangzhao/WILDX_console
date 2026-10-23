# Retained IMU samples on the PC clock

Private WILDX **1.0.0.128** adds a distinct-sample journal and post-recording
conversion for V1/V2 live USB neural recording. No firmware change, device access,
new recording, installer or publication was performed for this software step.
The private125 high-resolution **neural** timing implementation is preserved.

The output time refers to the MCU's **I2C-completion timestamp**, paired with the
sensor register counter. It is a proxy for the retained sample's observation,
not the physical ADC conversion instant. The interval includes MCU/PC clock
query brackets and integer-millisecond phase. It does not bound sensor read,
filter/update, or interrupt latency. Network/UTC correction remains separate.

## Saved outputs

When misc signals are enabled, the existing verified sensor consumer additionally
writes `imu_native_samples.jsonl`. Its header identifies the MCU, recording
session and shared host epoch. One following row represents each distinct valid
AG observation, retaining:

- Native 24-bit sensor counter and conditional per-segment unwrapped counter.
- MCU I2C-completion milliseconds, publication/observation milliseconds, sensor
  sequence and available firmware-session/packet sequence.
- Effective validity, raw140-byte snapshot, measured AG/magnetic values,
  transport gaps, queue overflow and firmware missed-conversion estimate.
- Segment/reset reason and retained-observation sequence gaps.
- Host receipt QPC/PC time as provenance; receipt time is not substituted for
  sample time.

`imu_native_samples_status.json` records accepted/written observations,
duplicates, invalid records, resets and any write/flush error. A source journal
error is carried into the conversion report as an incomplete result.

After files close and stopped clock observations are saved, conversion writes:

| File | Contract |
|---|---|
| `imu_samples.timestamps.calibrated.csv` | One row per retained valid observation, including sequence/segment, native counters, IRQ milliseconds, PC midpoint/bounds, validity/status, gaps and measured values. AG units are micro-g and milli-degrees/s; magnetic values are nano-tesla and blank when unavailable. |
| `imu_samples.timestamps.calibrated.dat` | Little-endian float64 **PC Unix seconds**, one value per CSV sample row. Unmapped rows contain NaN. |
| `imu_timestamp_calibration.json` | Source/output hashes, row/coverage counts, MCU/host epoch binding, clock source, interval widths, timestamp reference and limits. |

`mapped_i2c_completion_proxy` means the reported MCU completion time was mapped.
`clock_not_bracketed` or `mcu_reset_unbound` retains the sample with unavailable
PC time. Missing observations are not filled. `analogin.dat` remains the separate
1250Hz held-value compatibility grid; its rows are not distinct acquisitions.

For merged neural DATs, the IMU stream remains independent: it is not replicated
per probe or resampled onto AP rows. Analysis compares its PC seconds and bounds
with neural calibrated timestamps and mapped GPIO events on the shared epoch.

## Mapping and compatibility

The converter uses adjacent, bracketed MCU/PC control observations from
`first_sample_timestamps.json`. It checks the journal's exact MCU UID and host
epoch. The older `clock_observations.csv` format is accepted when its UID, epoch,
units and control-only provenance are available. The native sensor counter has
a nominal25600 ticks/s; the converter retains it and its pairing with IRQms
instead of pretending it is itself a PC/ADC timestamp.

Let the two query integer ticks differ by D ms and the event tick be m ms after
the first. Under a positive affine clock map, unknown query/event millisecond
phases give interpolation fractions `(m-1)/D` through `(m+1)/D`. Query send/receive
brackets, the retained PC epoch bracket and integer rounding are propagated to
the output interval. No extrapolation is performed, including uncertain boundary
milliseconds. Clock resets/mismatched epochs are not stitched together.

Native wrapping is accepted only for short, forward, consistent observations.
A gap of300seconds, a half-counter ambiguity, sequence/session reversal or
inconsistent native/MCU progress starts a new labeled segment. An observed MCU
reset leaves subsequent rows unbound to the previous clock queries. Hidden
resets and physical sample/read phase are not established by this software.

The high-resolution NP counter query does not remove the integer-ms uncertainty
of an IMU IRQ timestamp. This pipeline does **not** assign the NP67–135us
intervals to IMU observations. Old firmware remains usable; missing timing
evidence produces unmapped timestamps, with no neural recording start gate.

The standalone script supports recursive processing:

```powershell
.\Correct-RecordingTime.ps1 -Folder "E:\recordings" -Recurse
```

Existing derived files are protected; `-ReplaceDerived` regenerates only derived
outputs. Legacy `imu_snapshots.bin` can be replayed when retained. Its anonymous
snapshots require a matching recording-status/experiment UID before clock
mapping. This is folder/recording provenance, not a per-snapshot UID receipt.
Original neural/IMU data and timestamps are preserved.

Earlier V1 recordings that retained only `analogin.dat` cannot recover discarded
native counters and distinct observations. The recent V1 native15k HIL is such
a recording; its14,017 valid observations are not available as native replay
input. This change does not add BLE/SD native-sample transport or lossless200Hz
acquisition. Those formats need retained native evidence to support conversion.

## Software evidence

`test_imu_time.ps1` passes **54 checks**, including actual compiled V1 sensor
packet and V2 bulk-header consumers, known fractional-ms clock phases, native
and MCU wraps, resets/ambiguous gaps, sequence gaps, duplicate/invalid rejection,
UID/epoch mismatch, no extrapolation, raw-field disagreement, journal I/O status,
source preservation and derived overwrite protection.

An existing V2 capture (`command_paths_20261023/hardware/v2-record10`) provides
206 valid retained observations. Its recording-status/experiment UID matches
the three clock queries; all206 map with full completion-clock intervals of
4160–4166us. Its software was105 and its recorded profile was three probes20kHz
for10.362s. APP/USB CRCs are not inferred from that experiment. This is a new
file replay, not new hardware evidence or physical timing calibration.

The private125 V2 capture's1399 invalid snapshots produce **zero** sample rows
and a `no_valid_samples` report. Both original raw hashes remain unchanged.
The compiled128 standalone script and compiled V1/V2 consumers are exercised
without loading the GUI or opening a device. Neural regression evidence includes
594 model checks,11 anchored/shared-rate checks and30 actual recorder-worker
file-only checks on126;128 only tightens IMU raw-field/error handling beyond it.

Frozen inputs, reports, script replay and hashes are retained under
`_artifacts/imu_sample_time_20261023/`. This is private software validation.

## Bounded simultaneous NP/GPIO test plan — prepared, not executed

The following requires Frank's direct hardware authorization and an exact GPIO
installed-image identity. No GPIO firmware identity, port or configuration may
be inferred from a source commit or a previously used COM port.

1. **Freeze identities and ownership.** WILDX128 executable/SDK/script hashes;
   V1 UID001D00065942500B20313354 or V2 UID001A0004434250062039314E; the exact
   GPIO board/USB-serial identity, firmware image hash/version and configuration.
   The V1/V2 firmware owners verify their devices. A GPIO owner identifies its
   installed image before opening the test lane. The previous receipts record
   V1 APP60EC609E/USB F8E721C2 and V2 APP70CC573C/USB02AB1A78 after restoration;
   current identities must be read again only after authorization.
2. **Approve the precise test pair and rollback.** The earlier high-resolution
   USB components are V160981517 and V27EC500CA, tested with WILDX125. Testing
   WILDX128 with either is a new integration check. If temporary installation
   is approved, the owning firmware developer first makes a fresh exact-UID
   full-flash/options backup, builds a USB-only preserving page plan, verifies
   readback, resets normally and detaches the debugger. Preserve APP, boot,
   configuration and all SD recordings. No full-speed USB, SD writes/format,
   broad firmware change or silent substitute device.
3. **One process, existing wiring.** Use one WILDX process and the selected GPIO
   logger on the same PC. Choose a fresh timestamped output folder. Use the
   existing connected GPIO inputs only; add no wire, external input or hidden
   synthetic pulses. If no natural GPIO events occur, record zero events and
   leave event-timing coverage unqualified.
4. **Preflight and duration.** Confirm high-speed USB, idle exact NP identity,
   supported timing query, GPIO identity and free disk space. First require
   advancing valid IMU sequence/native time/chip identity and no current sensor
   I/O fault; V2's I/O diagnosis belongs to its developer and is not solved by
   this converter. Start with **70seconds per device**, sequentially: V1 two
   probes/native15kHz, V2 three probes20kHz. Both use their GPIO logger in the
   same process; do not run both NP streams concurrently or add fault injection.
5. **Retain evidence and Stop explicitly.** Save neural source anchors/calibrated
   rows, raw clock/counter queries, native IMU journal/status/CSV/times, GPIO raw
   events/queries/session and calibrated CSV, NP/IMU/GPIO gap/overflow/error
   reports and exact binary receipts. Stop explicitly, drain/close all writers,
   allow correction to finish and retain reports. A spontaneous NP interruption
   must not discard GPIO logging; do not provoke one in this bounded test.
6. **Pass criteria.** NP complete with no reported midstream neural gaps/drops;
   startup trimming remains separately reported. Fine NP mapping must be bound
   to the right UID/domain/generation. All mapped neural/IMU/GPIO outputs must
   identify the same process host epoch. Verify native IMU values/row counts,
   advancing sequence/counter, mapped/unmapped counts, interval widths, resets,
   retained-sample gaps, firmware missed estimates and overflow. Sparse20Hz
   publication cannot pass as lossless200Hz. GPIO event sequences must advance
   without unexplained gaps when natural events exist. Record all unavailable
   coverage rather than inventing success; no physical1ms pass criterion.
7. **Restore and close.** Host closes all NP/GPIO handles before a firmware owner
   restores any temporary USB component from the fresh backup. Compare full
   flash and semantic option fields, restore original idle mode, detach debug,
   close every owned handle and save receipts. Never erase SD data. Keep failed
   or partial evidence and do not silently replay an uncertain device command.

This can demonstrate common-coordinate operation, continuity and clock mapping
over the tested duration. It does not identify unknown fixed probe/filter/
receiver-versus-GPIO interrupt latency or IMU conversion/read/filter delay.
Longer-term stability remains untested; no-added-input joint recording alone
cannot establish an absolute physical AP-to-GPIO error bound.
