# CE64 USB FS MSC protocol — draft v1

CE64 remains a normal SD-backed MSC disk. Raw-v1 firmware appends a `0x2000`
sector (4 MiB) virtual trailer; C64Z-v2 firmware appends `0x4000` sectors
(8 MiB), which contains its additional compressed aperture. The host probes
the v2 trailer first, then v1, and accepts a location only when `C64M` CRC and
the manifest's trailer-sector field agree. It never uses an outside-capacity
sentinel LBA.

The device must identify as product/inquiry **`WILD64 USB Device`** and expose
a stable USB serial. The PC makes this current identity primary: USBSTOR must
contain `VEN_WILD64` with an empty or space/underscore-normalized `PROD` field.
It retains `VEN_CE64&PROD_WILD64_USB_MODE` and **`CE64 Stream+SD`** / **`CE64
STREAM SD`** as compatibility fallbacks. It then opens the matching `\\.\PhysicalDriveN`, calculate
the trailer base from disk length, and validate the manifest before issuing any
command. `PhysicalDriveN` alone is not a device identity.

Normal LBAs below `base` pass through to the SD card and mount normally in
Windows, so ordinary file copy remains the preferred SD downloader. The host
also exposes bounded raw-SD reads for tooling; it rejects a requested range
that would overlap the virtual trailer.

| Relative LBA | Direction | Function |
| --- | --- | --- |
| `+0x0000` | read | versioned `C64M` manifest |
| `+0x0001` | read | signature/identity |
| `+0x0030` | read | legacy-compatible start alias |
| `+0x0040` | read | legacy-compatible stop alias |
| `+0x0050` | read | guarded, deferred legacy reset alias |
| `+0x0080` | read | `C64S` status |
| `+0x0081` | write | CRC-protected `C64Q` command mailbox |
| `+0x0082` | read | capabilities |
| `+0x0800..+0x0fff` | read | optional native MGR_misc / IMU aperture |
| `+0x1000..+0x1fff` | read | 2 MiB sequential stream aperture |

Manifest `C64M`: version u8@4, format u8@5=1, sector bytes u16@6, trailer
sectors u32@8, status/command/capabilities relative LBAs u32@12/@16/@20, data
first/last relative LBAs u32@24/@28, legacy-signature relative LBA u32@32, and
optional IMU first/last relative LBAs u32@36/@40. Both IMU fields are zero
when absent; when present they must be `0x0800..0x0fff`.
Status `C64S`: version@4, state@5 (`DISABLED=0`, `IDLE=1`, `ARMED=2`,
`STREAMING=3`, `ERROR=4`), flags u16@6, error u32@8, acknowledged
command sequence@12, session sequence@16, rate@20, channels@24, byte rate@28,
pending frames@32, produced/consumed/dropped/underflow u64@40/@48/@56/@64,
IMU pending records@72, and IMU produced/consumed/dropped/underflow
u64@80/@88/@96/@104. The v1 IMU-freshness extension reports successful BMX I2C
completion count u64@112, last successful completion HAL tick u32@120, and
retry/recovery count u32@124. `CLEAR_STATS` resets all three fields.
Command `C64Q`: version@4, opcode@5 (`ARM=1`, `START=2`, `STOP=3`,
`DISARM=4`, `CLEAR_STATS=5`, `ENTER_DFU=6`), flags u8@6, channels u8@7, rate
u32@8, sequence u32@12. `ENTER_DFU` requires error-free `IDLE` with both
pending counters zero and all other command fields zero. Firmware returns its
matching ACK while it remains `IDLE`, then defers the system-memory STM32 DFU
jump long enough for the host to close its raw PhysicalDrive handle. A host
must require that ACK; an unrelated MSC disconnect is not a successful DFU
request. Capabilities `C64C`: version@4, frame bytes@8, frames/sector@12, safe
bytes/second@16, maximum channels@20, fixed ephys rate@24, IMU frame bytes@28,
IMU frames/sector@32, and IMU rate@36. Every structured sector carries IEEE
CRC-32 at 508..511 over bytes 0..507.

The DATA aperture is different: it contains raw 512-byte payload sectors with
no header or CRC—four frames each, with 64 little-endian int16 values per
frame. The host reads aligned batches (up to 128 sectors) and wraps through the
aperture; it uses the status produced/consumed/dropped/underflow counters to
detect loss. A short raw transfer, bad structured-sector magic/CRC, status
session change, or counter discontinuity is a reconnect/data-gap boundary and
is never silently filled.

`STOP` freezes the current session and moves it to `ARMED`; DATA-aperture reads
remain valid in that state solely to drain the stopped producer ring. Hosts
drain all `PendingFrames`, using ceil(frames/4) sectors for a final 1--3-frame
tail but append only `validFrames * 128` bytes from that padded sector. Before
`DISARM`, require `PendingFrames == 0` and `Produced == Consumed + Dropped`.

For a live MSC capture, issue STOP through the read-triggered legacy alias at
`+0x0040`, then poll `C64S` for `STREAMING -> ARMED`; do not wait for a C64Q
acknowledgement. This avoids a command-mailbox write being delayed behind BOT
data traffic while the producer continues running. C64Q STOP remains defined
for compatibility, but the host reserves C64Q writes for ARM/START/DISARM and
clears persistent counters with `CLEAR_STATS` only from an error-free `IDLE`
state before ARM.
For an advertised IMU aperture, likewise drain ceil(records/16) sectors,
append only valid records from a padded tail, then require IMU pending zero and
`ImuProduced == ImuConsumed + ImuDropped` before disarming.

Use a raw physical-disk handle with `FILE_FLAG_NO_BUFFERING` and
`FILE_FLAG_WRITE_THROUGH` plus page-aligned buffers. A mounted volume/drive
letter is unsuitable because its filesystem cache and partition bounds can
return stale mailbox contents. Raw writes may require elevation; volume lock
and dismount are intentionally not default behaviour because they disrupt the
user and are unnecessary when the trailer is outside every partition.

Uncompressed 64 × 10 kHz × int16 is 1.280 MB/s payload, above practical USB
full-speed MSC/BOT throughput. WILD64 production is fixed at 10 kHz with a
versioned compressed transport; raw v1 compatibility is fixed at 5 kHz. The
host accepts only 1250, 5000, 10000, or 20000 Hz from C64C and never silently
substitutes a lower rate, channel count, or frame sequence.

## WILD64 v1 host policy

Raw v1 WILD64 compatibility is fixed at **5000 Hz**. The production v2 mode is
fixed at **10000 Hz** and uses a versioned lossless/lossy compressed-block
transport; it never changes sample cadence or channel count. IMU records are
transported on real BMX160 I2C completions at **200 Hz** (6,400 bytes/s), not
by repeating the same snapshot on every 1.25 kHz misc tick. The GUI and
diagnostic harness use the advertised rates and reject a target whose aggregate
payload exceeds its C64C safe-bandwidth field.

Ephys sectors are chronological `[frame][channel]` rows. Hosts must not
compensate or reorder channel banks: `amplifier.dat` is the transport wire order
byte-for-byte. When the manifest advertises IMU LBAs, that separate aperture
contains 16 raw 32-byte native MGR_misc records per sector: digital word@0,
accel XYZ@2/@4/@6, gyro XYZ@8/@10/@12, magnetometer XYZ@14/@16/@18,
battery/test/DSP words, and PC time u32@28. Hosts append records byte-for-byte
to `analogin.dat` and PC times to `timestamp.dat`; the independent IMU counters
govern its independent consumer cursor.

When any IMU-freshness extension field is nonzero, completion-count advancement
is authoritative: hosts warn after one second unchanged and display the
retry/recovery count. Older v1 peers leave all extension fields zero; only then
may hosts use unchanged 9-axis tuple duration as a warning-only fallback (a
stationary sensor can be valid).

## C64Z v2 compressed 10 kHz transport

Only a v2 `C64M` trailer (`0x4000` sectors) may advertise compressed aperture
`+0x2000..+0x3fff`; a v1 manifest leaves offsets 44/48 zero. `C64C` extension
`C64V` at byte 40 then locks rate 10000, 32 frames/block, 64 channels, Rice
`k=7`, maximum payload 4096 bytes, and fixed-absolute fallback capabilities.
New firmware advertises C64C flags bit 5 plus packed Q7 shift/bytes-per-frame
at 88 (`9 << 16 | 56`); old Q8 peers remain compatible. Host
access is enabled only when both manifest and capabilities agree.

`C64Z` records are byte-packed across sequential 512-byte reads, never
sector-aligned. The 40-byte header has magic, version=2, mode, quant shift,
Rice K, first block sequence, sample/channel/payload/header sizes, decoded
byte count, payload CRC32, decoded CRC32, then header CRC32 over bytes 0..31
and zero reserved bytes. The host validates header CRC before buffering a
payload, requires monotonic block sequence and all CRCs, and reconstructs
frame-major int16 samples. Mode 0 is lossless Rice; mode 1 is Rice after exact
low-bit masking; mode 2 is signed Q8 absolute samples for compatible older
peers; mode 3 is packed signed Q7 absolute samples (7-bit LSB-first two's
complement values, reconstructed by sign-extension then `<< 9`). C64C flags
bit 4 may permit decoded CRC=0 for mode 2 only; bit 5 permits mode 3 and its
decoded CRC=0. The opt-in 20 kHz experiment requires C64C flag bit 6, rate at
92 = 20000, packed Q4 field at 96 (`12 << 16 | 32`), and Q4 block frames at
100 = 64; it emits mode 4 in 64-frame blocks while streaming (a 1..63 frame
tail is permitted only after STOP) with
signed 4-bit LSB-first packed samples reconstructed by sign-extension then
`<< 12`, and may omit decoded CRC only with bit 6. Header/payload CRC and every
Rice decoded CRC remain mandatory.
`amplifier.dat` always
receives the reconstructed samples, while `compression.csv` records every
block's sequence, sample range, mode, and quant shift.

While `STREAMING`, every block must contain exactly 32 frames. Only the final
`ARMED` drain may carry a 1..31 frame final block. A host treats any sequence,
CRC, payload, status-overflow, source-overrun, produced-frame, or final block
count mismatch as a hard discontinuity; it does not reduce the fixed 10 kHz
rate, omit a channel, or fill a missing frame. RUNNING reads consume only full
512-byte FIFO chunks and are capped at the normal 128 PhysicalDrive sectors,
always using the immediately pre-read FIFO floor; after STOP, the host checks
pre-read pending bytes and appends only that valid tail before zero padding.

For live and terminal diagnosis, C64S v2 also exposes codec last/max/budget
cycles at 200/204/208, plus Rice last/max at 212/216 and Q8 last/max at
220/224, plus source-ring capacity frames at 228 and Q7 last/max cycles at
232/236, and Q4 last/max cycles at 240/244. These telemetry counters
reset on START and CLEAR_STATS; zero values remain valid for firmware
predating the extension. A reporting v2 peer requires source capacity at least
512 frames (the expected WILD64 capacity is 1000).

## C64I/C64R impedance measurement

The optional C64C extension at bytes 104..128 is `C64I`, version 1, with
info LBA `0x83`, result LBAs `0x84..0x85`, two result sectors, the nominal test
frequency, and flags. Flags bit 0 means the reported magnitude is **nominal,
not calibrated absolute impedance**; bits 1/2 advertise magnitude/phase; bit
3 advertises cancellation. C64Q opcode 7 (`START_ZTEST`) and opcode 8
(`CANCEL_ZTEST`) require zero flags/channels/rate. C64S state 5 is ZTEST.
Only an error-free C64S Idle state with no pending ephys/IMU data may start a
test.

C64I has state@5 (`0 Idle, 1 Preparing, 2 Measuring, 3 Complete, 4 Cancelled,
5 Error`), error@6, request sequence@8, completed/total channels@12/@16,
frequency/sample/steps/cycles@20/@24/@28/@32, elapsed ms@36, result
first/count@40/@44, epoch@48, cached result-sector CRCs@52/@56, flags@60, and
diagnostic I/O detail@64. On a transport fault `ioDetail` packs reason in bits
0..7, profile configuration index in 8..15, and one-based SPI transfer ordinal
in 16..31; the optional register snapshot follows (SPI2 @68/@72/@76, SPI4
@80/@84/@88, RCC @92/@96, GPIOA @100/@104, GPIOB @108/@112). The host logs
these diagnostic words without retrying a failed START.

After and only after C64I `Complete` with nonzero cached CRCs, the host reads
both C64R sectors. Each sector has `C64R`, version@4=1, **status@5=3
(`Complete`)**, error@6=0, matching request sequence@8, first channel@12
(0 or 32), count@16=32, matching epoch@20, and 32 entries beginning @24:
`u32 magnitude_ohm, i16 phase_deci_degrees, u16 flags`. Entry bit 0 is valid;
bit 1 means the weakest Zcheck current range approached the ADC limit, so the
reported magnitude is a lower-bound estimate (the impedance report prefixes
the displayed value with `>`). Bits 4..5 identify the nominal ZCHK current
scale (0=0.1 pF, 1=1 pF, 2=10 pF).
The sector CRC at 508 must agree with C64I's matching cached CRC. A cancelled
test never permits a C64R read. The smoke harness supports `--ztest` for a
controlled full run, `--ztest-cancel` for cancellation validation, and the
strictly read-only `--ztest-read-last` for retained completed results.

USB512's impedance report contains a **Continuous** checkbox next to **Refresh
measurement**. Leaving it unchecked requests one new sweep; checking it before
Refresh starts repeated sweeps. Clearing the checkbox in an auto-refreshing report
stops continuous mode. Continuous mode still uses complete, CRC-validated 64-channel
sweeps: it starts the next sweep only after C64I is Complete, both C64R sectors
validate, and C64S has returned to clean Idle. The open report refreshes after
each complete sweep. **Stop continuous**, closing the report, or the main
**Stop impedance** button sends the same protocol-defined cancellation when a
sweep is active and never overlaps capture or DFU. A completed single-sweep
report has **Refresh measurement**, which closes that report and starts a new
full validated sweep; continuous reports identify that refresh is automatic.

## Selected-electrode EIS spectrum

Firmware which advertises the `C64E` capability extension at C64C byte 132
supports an idle-only spectrum on one selected electrode. The current locked
profile contains 64 logarithmically spaced points from 100 Hz through 10 kHz
at a fixed 30 kHz timer/SPI measurement cadence. USB512 starts it by sending
C64Q opcode 9 with the electrode ID (0..63) in channels@7; flags/rate and all
reserved bytes remain zero. Opcode 10 cancels the active spectrum and uses
zero channels. Recording, ordinary impedance, EIS and DFU are mutually
exclusive.

`C64E` info is at LBA+0x0086: version@4, state@5, error@6,
request sequence@8, selected electrode@12, completed/total points@16/@20,
sample cadence@24, min/max frequency@28/@32, elapsed ms@36, first result
LBA/result sectors@40/@44, epoch@48, cached result CRCs@52/@56, flags@60,
and sector CRC@508. The two `C64P` result sectors begin at LBA+0x0087.
Their header is version/state/error@4/@5/@6, request@8, first point@12,
point count@16, epoch@20. Each of 32 entries is 12 bytes from byte 24:
frequency u16 Hz, magnitude u32 ohm, phase i16 0.1 degree, flags u16 and a
reserved u16. Each result sector has its own CRC@508 and must match the cached
CRC in C64E before USB512 displays it.

Result flag bit0 is valid, bit1 is a lower-bound/clipping warning, bit2 marks
fewer than six waveform samples per cycle and bit3 fewer than four. Bits4..5
identify the nominal ZCHK current range. The DDS excitation permits all 64
log points without requiring integer divisors of 30 kHz. Frequencies above
5 kHz are visibly quality-marked; 10 kHz is the current practical ceiling at
three samples/cycle, not the 15 kHz Nyquist edge. Magnitude and phase are
nominal and uncalibrated until verified with resistor/RC fixtures across the
full band.
