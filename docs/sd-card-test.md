# SD card recording test

Open **USB Mode → USB → SD card test**. Select the logger and the connected probes. The default is 60 seconds per attempted rate; the range is 10–120 seconds.

The logger test uses real neural recording, starting with the highest legal rate for that probe combination, then 20, 15 and 7.5 kHz as applicable. It keeps the probe selection fixed. The first rate whose stored data passes full readback is the result. Only a verified throughput-loss result, followed by a closed, quiet, writable state on the same card, permits trying a lower rate. Probe/configuration errors, corruption, insufficient space and uncertain command delivery stop the sweep without a speed verdict.

Matching APP and USB firmware with the USB SD-test capability is required. Existing firmware remains usable for normal recording and download; it reports that this new test needs a full firmware update. BLE is not required for this test. Firmware returns to USB download after each finite recording, and the console reads and verifies the stored data. Cancel prevents the next attempt and waits for an already submitted finite recording to finish safely. An unconfirmed Start is never replayed.

Test recordings remain on the card. The test does not format, delete, repair or validate unrelated recordings. Reports and retained request/result bytes are saved under the selected report folder. Temporary decoded readback DAT files are removed after verification. A pass applies to the installed logger/firmware/card, selected probes and tested duration; it is not an endurance guarantee.

The **PC card reader** tab remains available for cards mounted as Windows volumes. It writes and verifies temporary files in free space, then removes only its own files. Its result measures that reader path, not the logger's recording capability.

Each attempted rate also reports write-pause and saved-data gap statistics. Device timing exposes the longest and mean SD write transaction and longest recorder service interval when available; these are not inferred from neural loss. The PC reader test includes longest/p95/p99 write calls.

V2 readback can reconstruct the exact gaps between contiguous stored source segments: count, total missing ticks/time, longest/mean gap, and nearest-rank p95/p99. The JSON keeps the original gap-tick list and source rate. Indexed storage holes are counted separately. A missing, overlapping or inconsistent segment history leaves gap durations unavailable. Device counters and readback counters remain separate.

V1 currently exposes loss-event and skipped-lane-byte lower bounds rather than exact gap positions/durations. Its stored maximum write transaction is displayed when present. A dash means unavailable; it does not mean zero. No gap statistic is a physical SD-card busy-line or electrical timing measurement.
