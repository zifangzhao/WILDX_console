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

The same complete existing session `407207722` was read throughout. All transfers used high-speed USB and production CRC validation. Six alternating exports produced identical DAT, IMRO, descriptor and footer bytes.

| Path | Throughput |
| --- | ---: |
| Physical bounded extent, validated source only | 31.137 MB/s |
| Serial export, median of three | 30.414 MB/s |
| Buffered export, median of three | 30.095 MB/s |

Buffered source reads occupied approximately 12.93 of 13.10 seconds, placing the limit on the device side. Per-export firmware counters were approximately 8.55 s for SD reads, 4.22 s for CRC and 8.13 s for USB. USB time overlaps prefetch and must not be added to the other counters.

The isolated USB CRC candidate `9C6B72A1` grouped register loads and fed eight words per loop. The generated bulk loop used 15 instructions per 32 bytes versus 24 in the released image. Native checksum equivalence passed 16,569 cases, including alignment, tails and full-size transfers.

Hardware testing rejected this candidate. Raw throughput fell to 29.227 MB/s; serial and buffered export medians were 28.497 and 28.242 MB/s. Every exported DAT/IMRO/descriptor/footer hash matched the baseline, and firmware errors stayed zero. Median CRC time increased from 4245.5 to 5075.5 ms per export; SD time stayed near 8.62 s and overlapping USB time near 8.20 s. This candidate must not enter the bundled catalog.

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
