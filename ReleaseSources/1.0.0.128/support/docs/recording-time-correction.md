# Post-recording PC-time correction

Integrated in local WILD-X build **1.0.0.125**. This process uses **PC time only**.
Network time is reserved for a separate process and a separate output file.

NP clock queries, acquisition anchors, USB arrival observations and GPIO logging
now share one process-wide QPC/PC epoch (`NpHostClock`). GPIO retains raw QPC and
wall-clock observations alongside the existing query sequence and device RTC.
A Windows wall-clock adjustment does not step the recording's monotonic clock.

With compatible timing firmware, `first_sample_timestamps.json` identifies the
first decoded source row, DAT layout and source segments. Correction uses that
origin for the intercept and the USB arrival fit for relative rate when enough
observations exist. All anchored AP/LFP streams use the median qualified AP rate ratio from that recording; LFP keeps its exact 12:1 relation. The individual arrival fits are retained separately for diagnostics.
This removes USB delivery delay from the selected digital-source intercept;
it does not measure probe ADC/filter/serialization or receiver acceptance delay.

Compatible USB firmware also supplies optional high-resolution counter snapshots
before capture and after all transfers retire. `pc_mapping` identifies the result:
`dwt_cycle_affine_interval` uses matched counter domains, device UID, frequency
and capture generation; `hal_millisecond_affine_interval` retains the coarse
mapping. HAL milliseconds choose the counter wrap count only in the fine path.
Raw replies, query IDs and host send/receive brackets are retained. An unsupported
query or inconsistent/reset clock falls back automatically; no clock queries run
in the bulk recording loop. The fine path currently applies to live USB, while
BLE/SD retain their existing timing evidence.

For SD exports with valid SDUTC2 metadata, the same engine generates timestamps
at the nominal sample rate. SD metadata alone does not measure recording-wide
drift. Source gaps cause timestamp jumps; missing time is never compressed away.
After an optional DAT merge, segment offsets and each probe's phase are retained.
For merged DATs, the timestamp vector references the lowest physical port;
`probe_offsets` in the report gives the offset to apply to each other 384-channel
group. The file's channel order remains unchanged. Per-probe pre-merge timestamp
files are retained as provenance, including samples moved to unmatched tails.

Old firmware remains supported. Without a qualified source anchor, live USB uses
an explicitly labeled arrival estimate. Old SD data without timing evidence gets
an unavailable result, never an invented epoch from a folder name or analog row 0.

## Automatic recording workflow

V1 and V2 live USB recordings retain low-rate observations tying saved neural
sample indices to the original host USB read-completion observations. The native
reader passes its QPC timestamp through the existing queues; decoder execution
time is not substituted for read-completion time. Evidence writing runs on the
decoder side, with a maximum of four regular observations per second per output
stream plus its final observation. Arrival journaling needs no firmware upgrade.
The source-anchor query is optional; unsupported firmware uses the arrival
fallback. Both merged and separate probe outputs are supported.

After Stop, all recording workers and DAT files drain and close. Correction then
writes these files in the original recording folder tree:

- `time_observations.jsonl`: retained sample/arrival observations, including the
  session, relative DAT path, sample index, channel count, rational nominal rate,
  receive QPC, PC UTC mapping, raw wall-clock observation and PC-clock API.
- `<DAT filename>.timestamps.calibrated.dat`: headerless little-endian **float64
  Unix UTC seconds**, one value per complete row of the corresponding DAT.
  Merged files have one timestamp per combined sample row, not per channel.
- `timestamp_calibration.json`: source hash, models, fit coverage, estimated rate
  correction in ppm relative to the PC reference, retained/rejected observations,
  residual statistics, PC anchor bracket and limitations.

V1 produces AP and LFP timelines; V2 produces AP timelines. The sample files,
original `time.dat`/`timestamp.dat` and device timestamps retain their original
bytes. Existing gap masks and probe-phase metadata remain authoritative. This
does not calibrate independent camera or audio clock domains.

GPIO logging uses the same process-wide PC epoch. When the GPIO logger closes,
background processing writes `gpio_rtc_events.timestamps.calibrated.csv` and
`gpio_timestamp_calibration.json` beside the raw files. Each event retains its
channel, sequence and RTC time, with PC Unix seconds and lower/upper bounds.
Only events bracketed by consistent clock queries at most ten seconds apart are
mapped; other rows are marked `clock_not_bracketed`. Integer RTC phase uncertainty
is included. There is no extrapolation or inferred physical edge latency.
The standalone script can regenerate these derived GPIO outputs as well.

Private128 additionally retains distinct valid IMU observations and produces
sample-aligned PC timestamp/CSV outputs. Their reference is MCU I2C completion,
with millisecond clock/query uncertainty, rather than physical IMU conversion.
See [IMU timing and the prepared joint-test plan](imu-time-correction.md).
This software-only addition preserves private125's neural clock mapping.

The PC anchor uses `GetSystemTimePreciseAsFileTime`, bracketed by QPC, when the OS
provides it. Subsequent mapping uses the monotonic clock. The older API fallback
is identified in the report. This bracket does not measure the PC's UTC error.

## Standalone script

`Correct-RecordingTime.ps1` is copied beside `WILDX_console.exe`. It loads the same
file-processing engine without starting the GUI, BLE, USB or network-time client.
Windows PowerShell 5.1 is used; PowerShell 7 invokes that installed host.

```powershell
.\Correct-RecordingTime.ps1 -Folder "E:\recordings" -Recurse
```

From a source checkout, supply the executable explicitly:

```powershell
.\Tools\Correct-RecordingTime.ps1 -Folder "E:\recordings" -Recurse `
  -ConsoleExecutable "C:\Users\example\AppData\Local\WILDX_console\WILDX_console.exe"
```

Existing calibrated timestamp files are protected by default. `-ReplaceDerived`
allows regenerating those derived files; it does not replace sample data or
original timestamps. A separately measured fixed USB latency may be supplied
with `-LatencyMicroseconds`. Its default is zero, meaning no fixed-latency
calibration has been applied.

Older recordings without an observation journal or qualified source anchor receive
an `unavailable` report. A single start anchor cannot establish clock drift;
precise arrival history is not reconstructed from DAT values or folder timestamps.

Load an output in Python with:

```python
timestamps = numpy.fromfile("neuropixels_ap.dat.timestamps.calibrated.dat", dtype="<f8")
```

## Model and accuracy

The model is an affine map from saved sample index to PC-referenced time. A
centered least-squares fit uses median/MAD rejection for isolated timing outliers;
it does not require a normal USB latency distribution. At least three
observations spanning one second are needed. Below 60 seconds, only offset is
estimated and the nominal sample period is retained; measured drift is not
claimed. The 5 ppm crystal figure is reported as an assumption comparison, not
used to force the fitted result into a 5 ppm range.

Sessions, timestamp reversals and detected PC wall-clock steps are fitted
separately. Missing coverage between epochs is represented by `NaN`. A maximum
two-second extrapolation is allowed only at the recording's outer edges.

The report distinguishes `digital_source_anchor` from `usb_arrival_estimate`.
USB scheduling, transfer aggregation and decoder framing can introduce a fixed
or time-varying delay. Averaging does not identify an unknown constant delay.
Residuals and the statistical fit interval describe model consistency, not a
bound on absolute ADC sample time, PC UTC correctness, or alignment with GPIO.
The source-anchor intervals describe software brackets and device/PC observations
under a linear clock assumption; the coarse path also includes HAL epoch
quantization. They are not a
hardware-qualified bound on receiver acceptance, probe conversion/filter delay,
GPIO interrupt latency, or long-term clock nonlinearity. Approximately 1 ms
physical AP-to-GPIO/camera/Bpod alignment remains unqualified; historical RM032
recordings cannot acquire a missing anchor retroactively.
Externally supplied `kind: reference` observations can use the same engine, but
no external synchronization is inferred automatically. A calibration failure is
recorded separately and does not turn valid neural data into a recording error.

## Validation

Synthetic tests cover a 4 ppm clock with Gaussian jitter and burst outliers,
known latency correction, short recordings, PC-clock steps, merged/separate
layouts, unsafe paths, partial rows and original-file preservation. The actual
compiled V1/V2 recorder workers also generate and finalize their timestamp
outputs in file-only tests. The existing LFP time-file regression passes.
Standalone and recursive script execution were checked against the compiled app.
The integrated path was tested on connected hardware with private build 114:
V2 at 20 kHz with three probes for 70.36 seconds, and V1 at 15 kHz with two
probes for 70 seconds. V2 produced 1,406,873 AP rows per probe with no reported
drop or decode error. V1 produced 2,105,846 AP rows and 175,486 LFP rows in total,
with no midstream neural gaps; its status correctly retained `lossless=False`
because 942 startup bytes were trimmed. V1's older firmware lacked the optional
source-anchor query and successfully used the labeled arrival fallback.

Final private build 115 was checked by replaying those captures through the
standalone engine, checking all 6,501,951 timestamp values for finite, increasing
values and exact DAT row counts. V2 uses one AP rate ratio across all probes.
The actual SD merge path also passed a synthetic source-gap, probe-phase and
unmatched-tail check. Compiled-path tests cover legacy SD metadata and the
automatic source-anchor outputs. GPIO common-clock/file-lifecycle tests used
simulated serial traffic, not a new physical GPIO synchronization experiment.

The V2 first-row software/PC interval was 5.248 ms wide. Its retained USB-arrival
fit residual was about 0.104 ms RMS, which is not a measurement of absolute
sample timing. Approximately 1 ms physical ADC-to-GPIO alignment is not yet
qualified. Earlier private source-anchor HIL evidence is retained separately
under `_artifacts/first_sample_anchor_20261023/`.

Earlier integration evidence and the private115 source composition are under
`_artifacts/sample_time_closure_20261023/`.

The fine mapping was then tested on both connected devices with actual private
build125: V1 with two probes at native15kHz, and V2 with three probes at20kHz,
70seconds each. V1 generated2,107,989 AP rows and175,663 LFP rows in total;
V2 generated1,406,975 AP rows per probe. No midstream neural loss was reported;
V1 retains `lossless=False` for836 startup bytes trimmed. All seven derived
timestamp streams were generated automatically. Independent verification checks
all6,504,577 timestamps for row counts, finite increasing values, matching host
epoch/source phase, and independently reproduces the counter-based bounds from
the32 raw query replies.

The measured full first-row PC intervals are67–68us for V1 and135us for V2.
The retained coarse-query calculation on the same captures gives5.911ms and
5.353–5.354ms respectively. These are conditional clock intervals, not physical
ADC/GPIO accuracy. V1's test USB was60981517 with unchanged APP60EC609E;
V2's test USB was7EC500CA with unchanged APP70CC573C. The high-resolution path
requires compatible USB firmware; older firmware retains its fallback.
After testing, both original USB images were restored (V1F8E721C2, V202AB1A78).
Complete flash readbacks match the fresh original backups, option settings are
unchanged, and both devices are idle in USB download mode. SD data was preserved.

Current reports, restoration receipts, plot, frozen sources and binary hashes
are under `_artifacts/first_sample_precision_20261023/`. The private executable
is `build125/compile-bin/WILDX_console.exe`; no installer or firmware bundle
was published for this work. The new GPIO derived-output path has software and
simulated-serial validation, while joint physical NP/GPIO validation is still
outstanding. The V2 capture has no valid IMU samples, so it cannot validate IMU
sample timing. Native sensor acquisition and timestamping require separate work.
