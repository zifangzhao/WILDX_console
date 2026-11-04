# Closed-loop recording qualification

The requested profiles use the WILD Ripple detector and log events. V1 consumes native 1,250 Hz LFP at 15 kHz AP. V2 decodes the selected P3 channels and downsamples 20 kHz AP to 1,250 Hz. Ordinary NP and IMU recording remains the priority.

| Profile | USB | BLE-controlled SD |
|---|---|---|
| V1, P1 + P2, 15 kHz | One and two detectors passed | One and two detectors passed |
| V2, P1 + P2 + P3, 20 kHz | Closed-loop disabled; the earlier two-detector run failed | Ordinary recording and one detector passed; two detectors disabled after stress failure |

Release 1.0.0.234 selects V2 APP `680A1685` / USB `80E18959`, with ordinary SD recording repaired and one SD Ripple detector enabled. Two SD detectors and V2 USB closed-loop remain disabled. V1's qualified firmware and detector options are unchanged. Release 232 retained the previous V2 firmware while this repair was developed.

Each mode includes a detector-off baseline. USB passing runs last approximately 60 seconds; SD runs last approximately 31 seconds and require a complete readback through the matching USB service. These bounded checks do not establish endurance or every card/PC combination. Closed-loop stays off for other rates/probe masks, unqualified detector counts and other filter presets. The same optional setup path serves USB and BLE; unsupported legacy firmware can still record normally.

## V1

APP `5F90FD3B` and USB `8454D202` passed all six runs. The saved NP prefixes have no reported missing AP/LFP samples, counter/frame errors or midstream gaps. USB queue peaks were 4–5 of 2,048 blocks. Full SD readbacks validated 775,421,952–776,994,816 bytes per session. Normal Stop, restart, catalog listing and matching-service download passed.

The observed scheduled IMU rows have no later missing slots or reported sensor errors. One initial slot is explicitly unavailable in every run. RTC-to-NP intervals are 108–111 microseconds; retained-copy jitter bound is 42 microseconds. Complete IMU source-window coverage is false for USB with two detectors and for all SD runs, and terminal coverage remains unknown. A verified common NP prefix is distinct from coverage of every retained IMU row.

The bench used P1/channel 0 for one detector and P1/channel 0 plus P2/channel 0 for two, Ripple/filter 6, gain 2, rectification enabled, and a one-second training interval starting immediately. Saved event words match native LFP replay bit for bit, including wrong-channel/probe negative controls. CPU utilization was not measured. Original firmware, options and profile were restored, and the hardware leases were released.

## V2 original failure

APP `997160AF` and USB `1D86BC8B` use the larger 22-slot USB ring. The ring reuses an existing SD-download arena and its existing MPU mapping. Detector-off and one-detector USB runs each verified 47,152 NP blocks and retained all 75,443 observed IMU rows, with no reported NP or scheduled-IMU queue, scheduler or AUX loss.

The two-detector USB run failed after 2,112 verified blocks with a source-ring overwrite and incomplete IMU phase evidence. The largest host completion gap was 157.687 ms. These counters do not establish whether that host gap preceded the producer fault. The failed run is retained as a failure and is not qualified by retrying it.

The fresh SD detector-off readback found 26 NP gaps, 1,690 dropped source blocks and 1,757 IMU queue drops. Its 28.47744 seconds of stored source plus 2.16320 seconds of explicitly missing source account for 30.64064 elapsed seconds. The largest NP gap was 195.84 ms and the largest observed write interval was 225.84 ms. These observations establish recording loss under stalls, not a specific card-versus-writer cause. SD runs with detectors were blocked by this failed ordinary baseline.

The bench selects P3 ADC channel 256, then channels 256 and 257 for two USB detectors, using the same Ripple/training/gain settings as V1. Channel availability is still checked against firmware capabilities. Full terminal IMU coverage and total CPU/power use are not established.

The old interrupted SD recording reserved the rest of the V2 bench card. The user explicitly authorized erasing that exact card. The USB format submission was rejected before a transaction was queued, confirmed by fresh status and an unchanged catalog without resetting. A distinct normal BLE Format succeeded and cleared the catalog. This setup does not qualify automatic recovery after a battery pull or the USB format submission path.

## Reproducible evidence

The release qualification receipts contain exact firmware, host and SDK hashes, per-run results, card identity, restoration and compact-payload references. Generated full neural test payloads are removed only after selected-channel replay fixtures, bounded windows, hashes and metadata are retained. Firmware restore backups and user recordings outside the explicitly formatted V2 card remain protected.

The host regression runs with `test_closed_loop_workflow.ps1`. Its small synthetic fixtures are included under `Tests/Fixtures/closed-loop`; it checks profile/count/transport/filter admission, legacy fallback, explicit disable and no-replay handling without hardware or user preference writes.

## SD repair follow-up

Development APP `F6B24C02` makes inactive closed-loop offer/service dispatch a cold-selected no-op. Its private IMU queue grows from 128 to 512 rows, providing 409.6 ms of coverage at 1,250 Hz instead of 102.4 ms. The queue adds 18,432 bytes; descriptor SRAM use is 41,536 of 65,536 bytes. Recording, wire, timer and Stop formats are unchanged.

Focused queue/runtime/provider/batch/phase/wire checks pass. An actual append-writer test retains 1,024 NP blocks through a 225 ms DATAEND hold. With enough reserved extent, manual Stop drains all 282 queued IMU rows. A deliberately undersized finite extent correctly reports accepted rows it cannot persist. These checks establish queue and ownership behavior, not physical card performance.

The fresh 60-second detector-off SD capture and complete production readback pass: 47,548 NN blocks / 60.86144 source seconds, no NP gaps/drops/overflow/discard, and zero IMU queue, scheduler, bad-sector or accepted-unsaved counts. Retained phase, first source and first slot validate; 11,978 distinct sensor updates have no invalid snapshots or sequence gaps. Full IMU source-window and final-tail coverage remain unverified.

PSRAM spill/restore was exercised 144 times each, with FIFO peak three. The maximum write was 32.49 ms, so this run does not reproduce the previous 225.84 ms cold write and does not establish the cause of the earlier NP failure. One- and two-detector ordinary SD retests also pass full NP/IMU readback and selected-native channel checks. The initial read-only BLE timeout cleared without a recording failure; no reset or formatting was used.

Test-only APP `C929D92B` adds a one-shot 225 ms owner-retirement hold. Capture, C14, sensor scheduling, and real error handling continue; the four NN/AUX owners and command remain pinned, preventing another SD launch. Its actual-writer native test retains all 1,024 NP blocks, exercises FIFO peak 43, and releases mask `0xF` once after 225.024 ms. The hardware detector-off run exercised 225.114 ms with FIFO peak 42. Its complete 8,472-block readback passes NP continuity, observed IMU phase/first-source checks and zero reported NP/IMU loss.

The next same-APP Start failed before the new writer initialized: fault 5, metadata reservation. A stopped polled write at CPU 128 MHz / SD 27 MHz underruns; HAL reports READY while the host DPSM remains active. The failing sector is 16, before publication of a new catalog entry. Original errors and stack/catalog snapshots are retained. Recovery first reconciled card status and the owned failed host data path; no healthy write was reset or replayed.

Production APP `680A1685` includes the recording changes above and fixes stopped metadata polling: use divider 8 through CPU 128 MHz, then restore and read back the exact live clock before capture. Live DMA recording remains at 108 MHz. Error retirement requires fresh card-ready proof and idle source/DMA owners; original write errors remain failures, uncertain state remains pinned, and no write is replayed. Both native runtime variants pass the actual metadata-function checks, including programming-only busy periods that must not issue CMD12.

Same-APP 0/1/2-detector recordings on `680A1685` passed normal Start, Stop and direct restart, with complete readbacks of 8,320 / 8,548 / 8,228 NP blocks and 13,312 / 13,674 / 13,161 IMU rows. Reported NP and IMU losses are zero. The unchanged recording objects reuse the longer `F6B24C02` checks: 60.86 seconds with detectors off and approximately 31 seconds with each detector count. A nominal two-detector pass does not override the following stress failure.

Diagnostic APP `2D471189` adds the same controlled hold to the metadata-fixed image. One detector passed its complete NP/grid/event/native replay checks: all 13,629 scheduled IMU rows have valid accelerometer, gyro and magnetometer data with zero sensor errors and changing values. One startup SNS2 snapshot has accelerometer/gyro only; its unavailable magnetometer sequence/time zero is excluded from magnetometer cadence. The following 2,140 native snapshots have both sensors valid. Two detectors failed the hold with 107 NP gaps and 283 dropped source blocks, despite normal Start/Stop. That failed configuration is disabled without a retry-to-green. Diagnostic images are not bundled.

USB `80E18959` changes the optional SD-test expected-APP identity to `680A1685` and corrects two no-ticket status fields to the existing SDK's unknown-value sentinels; its other 116 objects are unchanged. App 234 enables one V2 SD detector only at three probes / 20 kHz / Ripple, and retains legacy recording fallback. Its 183 focused checks pass; the export SDK and app recording implementation are unchanged from release 232. Local app 233 admitted two SD detectors and was never packaged or published.

These are bounded same-card checks, including one controlled 225 ms hold per tested count, not endurance or all-card qualification. The hold delays ownership return; it is not a measurement of physical card latency. Physical sensor conversion phase and final scheduled-IMU tail remain unknown. Instrumented detector decode plus service is approximately 7.8% for one detector, including preemption/measurement overhead; total CPU and power are not established. The helper checks relative NP/IMU data, not a new GUI IMRO or absolute RTC qualification. Exact new source inputs and inherited object identities are archived; unresolved historical source claims remain explicitly partial rather than being called a clean rebuild.
