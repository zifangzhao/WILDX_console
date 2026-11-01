# Single-pass SD download

WILDX 1.0.0.222 changes V2 SD16/schema3 download to one forward pass over the committed physical payload. It validates the original NN headers, handles AUX in physical order, and feeds the existing neural verifier/decoder in logical order. It no longer downloads the payload to build an index and then downloads it again to decode. No firmware update is required.

Normal downloads reorder deferred neural groups in RAM, capped at 8 MiB plus one current group and the existing decoder/output buffers. The footer-bounded index metadata is separate. Larger disorder or a missing logical prefix spills only deferred groups to a temporary destination file; consumed slots are reused and the temporary file is removed on success, cancellation or failure. There is no full raw staging pass or persistent neural raw/lane8 output. Incomplete recordings retain their incomplete status and explicit gaps.

## Development acceptance

One SDK build and one application build were used. The app compilation advanced from 221 to 222. The SDK passed 279 focused checks against the actual published 221 DLL: byte-identical output for ordered/reordered/incomplete cases, forward-only exactly-once payload reads, strict metadata/header/SuperSync/duplicate rejection, and cancellation/I/O/corruption cleanup after actual spilling. A reversed 48-group case used 8,386,560 bytes of queued RAM and spilled only two groups, with no temporary files left behind. An additional 37 legacy checks confirmed identical output for V2 formats 11, 12, 14 and SD16/schema2. Transport tests passed 800 assertions; actual application rate and ETA checks passed 254 and 14 checks respectively.

The bounded read-only comparison used the same existing complete recording on V2 UID `001A0004434250062039314E`: session 666227150, three probes at 20 kHz, 7,816 NN blocks, 365,163,520 committed physical payload bytes. Both runs used the same unchanged firmware/card and exclusive GLOBAL/device leases.

| Measurement | Published 221 | New 222 |
|---|---:|---:|
| Native export through close | 25.884704 s | 11.816476 s |
| USB source bytes copied including metadata/prefetch | 787,169,792 | 371,454,976 |
| SDK physical payload passes | 2 | 1 |

This sample finished 2.19 times faster, reducing native export time by 54.35%. All nine compared neural/IMU/descriptor/footer files matched byte for byte. The new decoder consumed the committed payload exactly once; its extra USB traffic comprises small metadata reads and bounded transport/prefetch overread. It needed only 1,118,208 bytes of deferred neural RAM and no spill. The 20-entry catalog stayed unchanged; the device was idle and both leases were released after each run.

These are native export measurements on one short existing recording, excluding subsequent combined-DAT merging. They do not establish a sustained speed for every PC/card or the remote 164.6 GB Yoshi recording. That recording's index was analyzed without rereading its payload; its observed disorder fits about 4 MiB of deferred neural data. Read/decode/output/USB timing components may overlap and must not be summed as independent CPU costs. Payload validation remains enabled; arbitrary ADC bit errors still lack an independent recording payload checksum.

V1 and earlier V2 formats keep their existing download paths; the shared burst change only completes timing counters. The checked 221 firmware/catalog and companion runtimes are reused unchanged. No new recording, flashing, formatting, deletion, mode change or whole-card validation was performed for this host-only fix.

Exact source/build/software/hardware acceptance receipts are retained with release 222. Publication reuses that development evidence and checks installer identity separately.
