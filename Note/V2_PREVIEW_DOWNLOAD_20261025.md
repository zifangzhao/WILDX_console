# V2 preview and SD download — 2026-10-25

Device: `001A0004434250062039314E`, BLE `F5D68A6BCC7C`, ST-Link `004B00213532511131333430`. V1 was not touched. The baseline was the published WILD-X 1.0.0.139 with V2 APP `8DE56637` / USB `1D3262FB`.

## BLE recording and preview

The actual shipped preview window was exercised during a three-probe, 20 kHz SD recording. Probe/channel selection kept Preview checked. Window and display gain controls were also changed.

| Selection | Accepted compact packets | Preview checked | Source-tick gaps | Missing preview rows |
| --- | ---: | --- | ---: | ---: |
| P1 / channel 0 | 49 | Yes | 0 | 0 |
| P1 / channel 17 | 100 | Yes | 0 | 2 |
| P2 / channel 17 | 152 | Yes | 0 | 3 |

This firmware exposes P1/P2 waveform preview while recording all three probes. P3 waveform selection is not part of its existing BLE preview protocol.

Three distinct coherent IMU snapshots contained valid accelerometer/gyro and magnetic data, at native times 606012, 607662 and 636431 ms. Six 900 ms IMU reads timed out during the observer run; this is not lossless native-IMU qualification. Preview gaps remain disposable and explicit.

Session `407757883` was fully downloaded and decoded with the production exporter. All 64,656 committed blocks validated; each probe produced 1,655,193 AP rows. The stored loss accounting reported zero source gaps, missing ticks, dropped blocks, physical overflow events and producer-discarded raw ticks. This does not exclude arbitrary ADC bit errors or establish analog quality. The complete export took 97.886 seconds. The BLE catalog grew from 294 to 295 entries; all previous immutable fields remained unchanged. Last/DeleteAllowed flags correctly moved to the new final entry.

One Stop was acknowledged and finalization completed after approximately 109 seconds. Source review confirmed a complete recording reread during verification, explaining the size-dependent delay. Stop was never replayed. The original P1+P2 / 20 kHz profile and idle USB mode 2 were restored and freshly observed.

## Preview timeout fix

The initial baseline preview took 27.944 seconds to produce its first packet after retries. Firmware sends the calibration snapshot in 17 UART fragments. Their transmit deadlines can use approximately 1.7 seconds before producer/control scheduling and Bluetooth delivery; the previous two-second host deadline was marginal. A 3200-tick raw PSSI snapshot is approximately 100 microseconds at 20 kHz, rather than a 160 ms sample interval.

Version 1.0.0.140 changes only the preview calibration response deadline from two to five seconds, plus the assembly version. Waiting owns no BLE command transaction; recording controls and IMU retain priority. No firmware command, acquisition setting or legacy format changes.

The focused test extracts the actual production capture/write methods and uses the production burst parser. Complete fragments delivered after 2300 ms fail against the released implementation and pass against 140. It also checks control/IMU priority, exactly one submitted request, and cancellation cleanup. Run `test_ble_preview_capture.ps1`.

The actual 140 hardware run produced its first waveform after 3.338 seconds, compared with 27.944 seconds in the earlier 139 run. P1/channel0, P1/channel17 and P2/channel17 accepted 46, 42 and 43 packets; Preview stayed checked throughout, with zero source-tick gaps and 2/0/1 missing disposable rows. All nine IMU observations were valid and fresh, with zero skips or read errors. This is one bounded comparison, rather than a latency guarantee for every Bluetooth link.

Session `407197060` stopped normally with 24,156 committed blocks, state5/fault0; verification took 40.764 seconds. The BLE catalog grew from 295 to 296 entries and preserved the immutable prefix. P1+P2 / 20 kHz was restored; a fresh exact-device inventory confirmed idle USB mode2 after re-enumeration. This new session has not received a full payload readback. The earlier full 139 export used the same unchanged recording firmware; the 140 GUI change affects only the disposable preview wait. The checked 140 installer includes the preview fix and accepted interleaved USB firmware directly.

## Download baseline

The same complete existing session `407207722` was read throughout. All transfers used high-speed USB and production CRC validation. Six alternating exports produced identical DAT, descriptor and footer bytes. This selected session contains no exported IMRO, so those trials do not establish IMRO generation or equality.

| Path | Throughput |
| --- | ---: |
| Physical bounded extent, validated source only | 31.137 MB/s |
| Serial export, median of three | 30.414 MB/s |
| Buffered export, median of three | 30.095 MB/s |

Buffered source reads occupied approximately 12.93 of 13.10 seconds, placing the limit on the device side. Per-export firmware counters were approximately 8.55 s for SD reads, 4.22 s for CRC and 8.13 s for USB. USB time overlaps prefetch and must not be added to the other counters.

The isolated USB CRC candidate `9C6B72A1` grouped register loads and fed eight words per loop. The generated bulk loop used 15 instructions per 32 bytes versus 24 in the released image. Native checksum equivalence passed 16,569 cases, including alignment, tails and full-size transfers.

Hardware testing rejected this candidate. Raw throughput fell to 29.227 MB/s; serial and buffered export medians were 28.497 and 28.242 MB/s. Every exported DAT/descriptor/footer hash matched the baseline, and firmware errors stayed zero. Median CRC time increased from 4245.5 to 5075.5 ms per export; SD time stayed near 8.62 s and overlapping USB time near 8.20 s. This candidate must not enter the bundled catalog.

The useful lesson is that instruction count does not describe peripheral-bus timing. Grouping memory reads can make the subsequent CRC writes slower even when fewer instructions execute.

The interleaved candidate `13ED1F8E` passed all six same-session export trials with hashes identical to the original firmware, CRC self-test1 and zero firmware errors. Validated physical source speed was 32.634 MB/s; serial export median was 31.820 MB/s (+4.62%), and buffered export median was 31.419 MB/s (+4.40%). Median CRC time was 4025.0 ms and SD time 8263.0 ms, versus 4245.5 and 8606.5 ms before. USB time remained near 8.17 s. SD stage variability also contributes to the measured total; this is a bounded result on the connected card rather than a fixed gain promised for every board/card/PC.

The final observer confirmed exact-device idle USB mode2/busy0/target0. APP, recovery, options/settings and SD recordings were preserved. The interleaved variant is accepted; the grouped variant remains rejected. No new capture, SD write or format was used for either speed comparison. Final source and package provenance accompany the installer.

Validated source throughput counts physical extent bytes, including metadata/padding; the export figures count logical native recording bytes. Their numerical difference does not by itself measure PC decode overhead. The buffered source read occupied almost the entire export time, providing the stronger evidence for a device-side limit.

## Observer limits

The first attempt encountered a transient discovery timeout after APP entry. A subsequent direct STATUS/CAPS read and production discovery succeeded. An early test omitted the production PC-clock initialization and was rejected before capture; the GUI already initializes that clock.

The first preview check stopped after ten seconds, before the eventual retry succeeded. The successful preview test's original observer timed out waiting for Stop at sixty seconds; a separate read-only continuation confirmed normal completion. A strict catalog comparison initially included the dynamic Last/DeleteAllowed flags; the immutable-field comparison passed after excluding only those two flags. The baseline download observer also raced USB re-enumeration during cleanup; a separate fresh inventory confirmed the original idle mode. These harness failures are preserved and are not reported as device regressions.

Evidence: `ReleaseSources/1.0.0.140/checks/measurement-summary.json`, `catalog-preservation.json`, the fixed/baseline capture regression logs, and the exact build receipt. Raw captures and full exports remain in `_artifacts/v2_preview_speed_20261025`.

## Delivery

WILD-X 1.0.0.140 contains V2 USB13ED1F8E / APP8DE56637 and their matching catalog. V1 remains APP82E51DEA / USB4F7B521A. Firmware provenance is pinned to XE_NP V2 commit `c8b10f8e4ac831c86732627305c249faa6b9ae4a`. The single installer is 10,525,513 bytes, SHA256 `5C81231C7989E52F3F05FE17342EB3CE96EA8EFFDB33149B77D6C12A3FF85A14`; all 58 extracted payload files were hash-verified. No installer execution, new firmware build or extra hardware check accompanied packaging. Older deliverables are archived under legacy. No separate update ZIP is needed.

## Further SD/CRC/USB pipeline work

The next optimization targets overlap rather than another CRC loop variant. A fresh published140/USB13ED baseline read the same 409,221,120-byte extent in 12.5318 s (32.6547 MB/s), with zero firmware errors. Counters measured SD 8422 ms, CRC 4102 ms and overlapping USB 8402 ms. The board returned to exact idle USB mode2.

The existing two-buffer path is approximately limited by `max(SD + CRC, USB)`: 12.524 s of preparation matches the measured 12.532 s. A complete three-stage pipeline would instead approach `max(SD, CRC, USB)`; the ideal bound is about 8.42 s for this extent, or 48.6 MB/s physical throughput. This is a ceiling from the measured stages, not a hardware speed promise; memory-bus contention and command/cache overhead can reduce it.

The proposed firmware reuses a private portion of the existing raw source ring only in reset-separated download mode, and separates SD IDMA start/poll/retirement. SD can fill one packet while CPU CRC checks a second and USB transmits a third. Packet layout, CRC checks, high-speed USB and old host commands remain unchanged. APP/capture/IMU behavior must remain untouched. No new RAM reservation is planned.

Source review found a HAL abort path with an effectively unbounded wait. The candidate must drain existing reads through genuine hardware retirement, suppress old-epoch responses, and retain busy/buffer ownership until safe. Completed speculative cache must be discardable so it cannot block mode exit indefinitely. The old host constructor already polls idle after 0x62 for about one second before resetting/flushing its pipe and issuing the next read.

A read-only real-device cancellation observer was first validated on published 13ED. It saved immutable prefix hashes, cancelled a pending read at 0/12/25 ms, then reopened through the original 140 constructor at a different LBA. All three hashes matched; retirement took 14/15/15 ms. This baseline supports testing real endpoint/SD ownership in addition to source-extracted native cases. A targeted short P123/20kHz USB+IMU capture is required after the new loan is released.

The first private pipeline candidate, USB `90822A54`, passed the expanded native ownership/cancellation checks and was installed under the V2 developer's exclusive hardware lease. Full flash readback verified that APP, recovery, options and settings were preserved. SD contents were not modified.

The same physical extent took 11.8144 s (34.6374 MB/s), +6.07% over the fresh 13ED baseline. Its SD/CRC/USB counters remained approximately 8.61/4.16/8.42 s, so substantially less overlap was achieved than the planning bound. All 296 catalog entries and all DAT/descriptor/footer hashes in six exports matched the baseline. However, serial export median fell from 31.8196 to 31.1424 MB/s (-2.13%); buffered export rose from 31.4195 to 33.0257 MB/s (+5.11%). Metadata-seek SD time also increased from 13.112 to 19.580 s. These regressions hold this candidate from release while scheduling and speculative-read behavior are investigated. A lower instruction count or additional buffers alone do not establish a useful end-to-end improvement.

The retained main object uses a tight download service loop; disassembly rules out a presumed one-millisecond main-loop wait. The focused source review instead identified full-packet cache cleaning after CRC, outside all three stage counters. Only the 32-byte header is modified by the CPU; the IDMA payload is invalidated and subsequently read-only. The next private repair will restrict cleaning to that header and retain the original one-ahead limit for unproven/nonsequential legacy reads. Full pipeline depth requires observed sequential progression or an explicit bounded read plan. This targets both hidden cache work and wasted catalog speculation.

The focused candidate `A71BDECC` passed native checks and full USB-only flash readback. Its raw speed was 34.5540 MB/s (11.8429 s), with zero reported firmware errors and all 296 catalog entries preserved. Metadata-seek SD time returned to 13.307 s, close to the 13.112 s baseline. Header-only cleaning did not materially improve raw throughput; the hypothesis that full transmit cleaning accounted for most missing overlap is rejected. The candidate remains private pending a useful end-to-end gain.

One read-only SWD trace collected 331 coarse asynchronous samples. Forty-three showed a CRC-pending bank with both physical SD and EP1 engines active. This establishes qualitative overlap, not atomic phase timing. The raw run recorded only 3 ms total header-clean time. The released host has no bounded-burst path: every packet uses a readiness query and a separate read request. A private probe will compare that path with the existing explicitly negotiated NBC1/0x66/0x64 burst extension, using 781 hashed packets, leaving space for legacy two-ahead reads within the preserved recording. Both paths validate production packet CRC/sequence/epoch/LBA and compare full payload SHA256. No GUI/SDK build or new firmware variant is needed for this diagnostic.

Evidence is under `_artifacts/v2_download_pipeline_20261025`, including `candidate90822-summary.json`. USB13ED remains the published fallback until the accepted 141 bundle is published. The private 141 application has now passed the final export checks described below.

## Accepted GUI and firmware pipeline

The same-A71 host test improved from 33.4167 to 44.0185 MB/s (+31.73%) when one synchronous bulk-read task filled a second managed buffer while the previous packet received CRC and SHA256 validation. The complete 407,869,440-byte payload SHA256 matched the legacy reference. These physical-read figures include a test-only SHA256 pass and are distinct from logical export throughput.

The actual 1.0.0.141 GUI uses the negotiated NBC1 extension only in its bounded export wrapper. It sends at most 32 full-packet requests per batch, within the selected recording's full-packet range. It keeps one native read active at most, validates every packet before publishing data, joins the worker before retirement/free, and never replays an ARM/START/CANCEL with an unknown outcome. Nonsequential seeks retire the owned session; the final partial extent uses the legacy reader. Unsupported firmware, synthetic sources and quality tests retain the original path. No new user setting is needed. Run `test_download_burst.ps1` for 183 software-only assertions covering fallback, range/tail behavior, cancellation, ownership and integrity errors.

| Same-session logical export | Published 140 / USB13ED | GUI141 / USBA71 |
| --- | ---: | ---: |
| Buffered GUI export, median of three | 31.4195 MB/s | 43.3317 MB/s |
| Unbuffered diagnostic path, median of three | 31.8196 MB/s | 31.1702 MB/s |

The GUI's buffered path improves 37.91%. All six alternating full exports produced identical three-probe DAT, descriptor and footer hashes; the complete 296-entry catalog is unchanged. This selected session has no IMRO file. The unbuffered diagnostic path does not get the host optimization and remains slightly slower; it is not the GUI's normal download path. Measurements are specific to the connected board/card/PC and do not establish all-card or endurance performance.

The A71 cancellation observer passed at 0/12/25 ms, followed by exact reopened-prefix hashes. A subsequent short P123/20 kHz USB capture verified all 7,280 blocks with zero reported raw/decoded drops. Each probe produced 186,367 rows; all 186 native IMU observations had valid AG+MAG, with 186 unique sensor sequences. The capture observer used GUI140; its capture/IMU code and SDK are identical in 141. The final download observer used the actual141 executable, SHA256 `37A35A19B29D9F348D4D59BAEDAFB8E92270B89A4FB115F30F0E5E8A5E0E5268`. Final exact-device USB mode2/busy0/target0 was confirmed and all observers closed. No SD write, delete or format occurred.

The practical lesson is to pipeline the complete path, including host command submission and validation. Adding a third firmware buffer alone gave about 6%; pairing it with bounded commands and host read/CRC overlap produced the useful end-to-end gain. The initial counter-derived ideal was a planning estimate, because those counters include service and scheduling effects rather than pure hardware durations.

The accepted installer contains all 58 verified payload files, including the source-bound V2 catalog and components. Its size is 10,536,090 bytes, SHA256 `56AC6A67A8DDFC976D7A31023355CAA7B668F7D98309E8161DD0648291B5F0C4`. The extracted actual141 automatic Bundled catalog passed for the current V1/V2 pairs. V2 source is pinned to commit `661e9dd03b7d98c1a16bf50c07f93295537faad8`. Only one current installer is kept at the latest root; publication verification follows.
