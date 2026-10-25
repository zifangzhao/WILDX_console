# WILD-X 1.0.0.137

Restores the double-buffered USB download path missing from published 133 and
private 136. One worker reads and validates the next buffer while the exporter
decodes and writes the current buffer. Existing firmware is supported; this
speed improvement does not require a firmware update.

| Same V1 recording, 335,544,320 logical bytes | Time | Throughput |
| --- | ---: | ---: |
| 136 serial export | 9.558 s | 35.11 MB/s |
| 137 buffered export | 7.710 s | 43.52 MB/s |

Throughput increased 23.96%. AP/LFP/analogin DAT and saved binary metadata hashes
matched exactly. No USB errors, retries or recoveries occurred. This was one
bounded existing recording on one PC/card, using USB412D4D02. Final bundled
USB4F7B521A reuses the same download objects and passed a smaller complete
readback; it was not separately speed-benchmarked. The buffer's 4,558 byte, seek,
extent, cancellation, error and worker-lifetime checks ran on actual 137.

The bundle includes V1 USB 1.0.108 / 4F7B521A and SD/BLE APP CC50D8D2, with matching
fused boot+USB+APP and separate components. Actual 137 USB recordings passed
P1+P2/20 kHz for 70 seconds and P1/30 kHz for 60 seconds with no reported frame,
counter, USB-sequence or host-drop errors. IMU was valid, with maximum measured
gap 7 ms; optional timing was enabled. A short APP P1+P2/15 kHz BLE Start/Stop
test and full 264-chunk readback passed, preserving all 34 previous catalog
entries. Exact scopes and earlier failed candidates remain in the firmware
acceptance evidence. V2 firmware is unchanged.

Best-effort recovery reports missing data explicitly; it does not make
recording lossless. Hard DMA/ownership faults remain hard. R2/UTC2 timestamps
describe digital software clock evidence and the first retained source row,
not physical ADC/filter/GPIO synchronization. Unbracketed IMU tails remain
uncalibrated. These checks do not establish all-mode or endurance performance.

One GUI compilation produced 137; the checked 136 SDK was reused unchanged.
Only the download form, prefetch class and assembly version changed from 136.
`compiled-sources.zip` and `source-inputs.json` preserve the actual GUI/SDK
compiler inputs. `firmware/` preserves the admitted V1 source archive, build
identities, validation receipts and proprietary toolchain reference hashes.
No firmware or application was rebuilt during installer packaging.

The installer retains the Run WILD-X finish option and is extracted and
hash-verified before publication. See `package-verification.json` for the
complete payload ledger and exact installer identity.
