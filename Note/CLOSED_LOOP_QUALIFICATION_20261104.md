# Closed-loop recording qualification

The requested profiles use the WILD Ripple detector and log events. V1 consumes native 1,250 Hz LFP at 15 kHz AP. V2 decodes the selected P3 channels and downsamples 20 kHz AP to 1,250 Hz. Ordinary NP and IMU recording remains the priority.

| Profile | USB | BLE-controlled SD |
|---|---|---|
| V1, P1 + P2, 15 kHz | One and two detectors passed | One and two detectors passed |
| V2, P1 + P2 + P3, 20 kHz | One detector passed locally; two failed | Detector-off baseline failed; one/two blocked |

The release enables only the V1 rows. V2 retains published APP `71A5126E` / USB `2556B4DD` and closed-loop remains disabled. The local V2 firmware pair is not published because its ordinary SD baseline failed.

Each mode includes a detector-off baseline. USB passing runs last approximately 60 seconds; SD runs last approximately 31 seconds and require a complete readback through the matching USB service. These bounded checks do not establish endurance or every card/PC combination. Closed-loop stays off for other rates/probe masks, unqualified detector counts and other filter presets. The same optional setup path serves USB and BLE; unsupported legacy firmware can still record normally.

## V1

APP `5F90FD3B` and USB `8454D202` passed all six runs. The saved NP prefixes have no reported missing AP/LFP samples, counter/frame errors or midstream gaps. USB queue peaks were 4–5 of 2,048 blocks. Full SD readbacks validated 775,421,952–776,994,816 bytes per session. Normal Stop, restart, catalog listing and matching-service download passed.

The observed scheduled IMU rows have no later missing slots or reported sensor errors. One initial slot is explicitly unavailable in every run. RTC-to-NP intervals are 108–111 microseconds; retained-copy jitter bound is 42 microseconds. Complete IMU source-window coverage is false for USB with two detectors and for all SD runs, and terminal coverage remains unknown. A verified common NP prefix is distinct from coverage of every retained IMU row.

The bench used P1/channel 0 for one detector and P1/channel 0 plus P2/channel 0 for two, Ripple/filter 6, gain 2, rectification enabled, and a one-second training interval starting immediately. Saved event words match native LFP replay bit for bit, including wrong-channel/probe negative controls. CPU utilization was not measured. Original firmware, options and profile were restored, and the hardware leases were released.

## V2

APP `997160AF` and USB `1D86BC8B` use the larger 22-slot USB ring. The ring reuses an existing SD-download arena and its existing MPU mapping. Detector-off and one-detector USB runs each verified 47,152 NP blocks and retained all 75,443 observed IMU rows, with no reported NP or scheduled-IMU queue, scheduler or AUX loss.

The two-detector USB run failed after 2,112 verified blocks with a source-ring overwrite and incomplete IMU phase evidence. The largest host completion gap was 157.687 ms. These counters do not establish whether that host gap preceded the producer fault. The failed run is retained as a failure and is not qualified by retrying it.

The fresh SD detector-off readback found 26 NP gaps, 1,690 dropped source blocks and 1,757 IMU queue drops. Its 28.47744 seconds of stored source plus 2.16320 seconds of explicitly missing source account for 30.64064 elapsed seconds. The largest NP gap was 195.84 ms and the largest observed write interval was 225.84 ms. These observations establish recording loss under stalls, not a specific card-versus-writer cause. SD runs with detectors were blocked by this failed ordinary baseline.

The bench selects P3 ADC channel 256, then channels 256 and 257 for two USB detectors, using the same Ripple/training/gain settings as V1. Channel availability is still checked against firmware capabilities. Full terminal IMU coverage and total CPU/power use are not established.

The old interrupted SD recording reserved the rest of the V2 bench card. The user explicitly authorized erasing that exact card. The USB format submission was rejected before a transaction was queued, confirmed by fresh status and an unchanged catalog without resetting. A distinct normal BLE Format succeeded and cleared the catalog. This setup does not qualify automatic recovery after a battery pull or the USB format submission path.

## Reproducible evidence

The release qualification receipts contain exact firmware, host and SDK hashes, per-run results, card identity, restoration and compact-payload references. Generated full neural test payloads are removed only after selected-channel replay fixtures, bounded windows, hashes and metadata are retained. Firmware restore backups and user recordings outside the explicitly formatted V2 card remain protected.

The host regression runs with `test_closed_loop_workflow.ps1`. Its small synthetic fixtures are included under `Tests/Fixtures/closed-loop`; it checks profile/count/transport/filter admission, legacy fallback, explicit disable and no-replay handling without hardware or user preference writes.
