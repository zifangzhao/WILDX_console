# Connected V1 APP121 camera40 validation — 2026-10-14 EDT

Target: MCU00190027434250062039314E, ST-Link001A00423235510D37333439,
VDD1.78V. The user confirmed **camera only**, with no NP1 probes attached.
Normal USB management proved SD-download role3/busy0/pending0 before SWD.

## Results

| Gate | Result | Evidence |
| --- | --- | --- |
| Published APP121 installation | PASS | Programmer verification and independent2MiB readback; APP CRC99BA4F27 |
| Boot/configuration/USB83 preservation | PASS at installation | Protected ranges match pre-flash backup; USB CRC82C6E21A |
| New60s SD camera40+ADC200k+IMU recording | PASS bounded integrity gate | Normal BLE START/STOP, finalized fault0, full505413632-byte USB readback |
| SD camera timing/completeness | PASS | 2417/2417 valid images including startup,25ms scheduled intervals,0 missing/duplicate IDs/cadence misses/faults |
| SD ADC continuity | PASS declared200kHz grid | 12082038 produced/saved samples,0 index/timestamp gaps/faults/overruns; duration agrees within810us |
| SD native IMU | PASS snapshot admission/freshness | 7645/7645 valid AG snapshots,max age7ms,0 new I/O errors/timeouts/missed estimates |
| Actual WILDX1.0.0.27 USB capture/render/save | PASS | 1095 rendered complete frames,175260720 raw bytes,0 USB packet-sequence gaps |
| USB camera aggregate cadence | PASS | Requested/effective40Hz,measured40000mHz,0 missed deadlines/faults/recoveries |
| USB camera completeness / zero-drop gate | FAIL | 1096 captured,1095 delivered,11 firmware-reported dropped frames |
| USB native IMU save | PASS | 10964 snapshots,10963 valid AG records,5439 distinct fresh AG sequences,0 queue/record/IMU sequence gaps,IMU error0 |
| USB derived analogin.dat | PASS | 34260 held-grid rows; native samples/ticks also retained |
| NP1 logging | NOT TESTED | No probes attached |

## SD recording evidence and limits

One normal60s BLE recording ran with no SWD attached. All2417 camera slots
are valid, including startup;2218 lie after5s. Saved schedule intervals are25ms.
Physical completion intervals after5s are23..27ms, mostly25ms; schedule does
not establish exact exposure timing. ADC descriptor/header rate is200kHz with
an exact declared5us sample grid; this is not independent ADC frequency or
microphone acoustic-response calibration.

Full native readback passed transport/header/format checks and the rate-aware
40Hz timing audit. Native payload has no independent source checksum.
The original decoder's separate lossless result remains failed because it
assumes50Hz and lacks valid START/STOP UTC anchors; it was not altered or
promoted to a whole-recording lossless qualification.

IMU data are latest-value snapshots, not a lossless native FIFO. Camera/microphone
chunk interleaving yields394 source/collection tick reversals in physical file
order, as in the existing format. Original ticks/bytes are preserved; do not
interpret physical file order as a native chronological FIFO. AG data are valid;
the environmental sensor remains error7.

## USB result and limitation

The single actual UI run was bounded to30 host seconds including startup.
Fresh valid IMU ticks span27406ms; this is not30 seconds of steady acquisition.
USB packets lack per-frame source timestamps, so the11 drops cannot be assigned
to the accepted startup interval. Aggregate40Hz and zero deadline misses do not
prove complete frame delivery. The prior zero-drop APP120 result is not
substituted for this result. The independent APP SD run above remains a separate
passing gate.

analogin.dat is a derived1250Hz held-value grid, not native1250Hz IMU sampling.
Native snapshots and their device ticks/sequences are retained alongside it.

## Installation, process and final state

Used the published APP-only HEX; no rebuild or fused-stack update.
Whole-flash SHA256 after installation:
`d1794d1646d975d15e56d390d787041c99948104b647a41a689db978a5eba48a`.
An initial offline prediction incorrectly treated programming as a byte overlay.
CubeProgrammer erases8KiB pages;189 old bytes beyond the sealed APP end were
correctly erased. An erase-aware comparison against the already-collected
readback passed. No second flash or duplicate full readback was performed.
An early verification attempt saw the bootloader VTOR; normal readiness was
confirmed before independent readback. An older host helper rejected40fps
before hardware access; the maintained helper was compiled once, then one USB
capture ran. Download role does not serve CAM1; camera access used idle streaming.

Original USB7/7500 profile was restored. Original APP mask3/15kHz,
rates[30000,0,0,1250,0,0,0,0],rows0/gain3/profile0/auto1 were restored and confirmed
after the SD test. The new recording occupies LBA8265728..9252864; catalog
next-free LBA9256960. Previous catalogued recordings were preserved.
Final management role3/SD download,busy0,pending0. No format/delete command was
sent. No owned debugger/capture process remains. APP121 stays installed with
USB83. No publication was requested in this validation turn.

Artifacts: `_artifacts/v1_connected_validation_20261014`: `flash_verified.json`,
`usb-hil/camera-timing-hil-result.txt`, `usb-hil/imu-hil-result.txt`,
`sd-ble/recording.json`, `sd-readback/download.json`, `sd-analysis.json`,
`sd-native`, and USB native files listed by `usb-hil/hil-result.txt`.
