# V1/V2 SD-USB download parity — 2026-10-25

Current WILD-X 1.0.0.141 already provides comparable download performance on both connected generations. The same host reader and bounded NBC1/NPB1 command/validation policy serve V1 and V2; no firmware port or new product build was needed.

| Measured median | V1 | V2 | Difference relative to V2 |
| --- | ---: | ---: | ---: |
| Same 256 MiB validated SD-byte prefix, three runs each | 46.57 MB/s | 45.17 MB/s | 3.10% |
| Full buffered GUI export, three runs each | 44.83 MB/s | 43.33 MB/s | 3.46% |

The fixed-prefix comparison used the same published GUI/SDK, buffers, checksum/SHA consumer and PC, with sequential runs and separate onboard cards. It counts stored SD bytes, including metadata/padding inside the extent and excluding USB framing. Full export counts logical native record bytes and includes decode/output work; expanded DAT sizes would give an unfair comparison because V1 and V2 formats differ. The V2 export result is reused from the immediately preceding accepted 141 run; its matched-size transport tests were rerun for this request.

All three prefix SHA256 values repeated on each device. V1's six alternating complete exports matched data/descriptor/footer hashes, with zero new firmware errors. The complete 36-entry V1 and 296-entry V2 catalogs were preserved. V1 freshly confirmed NBC1 version1, max4096 packets,1020 sectors/packet and idle remaining0. Firmware CRC identity was not advertised by the optional USB inventory, so this is a current connected-service performance check rather than a fresh readback attestation of every installed firmware component.

Both share high-speed USB, 510 KiB payload packets, explicit bounded requests, two buffers in the bulk-read stage plus existing export read-ahead, validation before data publication, worker joins before handle release, and safe once-only retirement without ambiguous-command replay. Board-specific read adapters remain appropriate: V1 fuses FIFO reads with hardware CRC across two private banks; V2 overlaps SD IDMA, a separate CRC stage and USB across three owned loans. Copying V2's raw-ring address into V1 would be unsafe and offers no demonstrated benefit. Equivalent service behavior and measured throughput matter more than identical internal buffer addresses.

A test-observer-only final-field mismatch occurred after V1's successful data checks. The corrected observer and separate final inventories confirmed original idle states: V1 downloadRole3/Busy0/Pending0, V2 streamingMode2/Busy0/Target0. All handles closed and both hardware leases were returned. No recording, SD write, card reset/format/delete, firmware change or product compilation occurred.

Evidence and method details: [paired check summary](../ReleaseSources/1.0.0.141/checks/v1-v2-parity/summary.json), [methods](../ReleaseSources/1.0.0.141/checks/v1-v2-parity/methods.md). Rates vary with card and PC; the measured difference is approximately 3–4%, not a claim of bit-for-bit identical timing on all hardware. The published 141 installer and its payload hashes are unchanged.
