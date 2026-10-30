# USB SD card test development validation

Status: matching V1 and V2 firmware/recording acceptance recorded by the coordinator in the final manifest. See the exact receipt hashes below.

Host221 contains the bounded USB logger rate sweep and gap/write statistics. It keeps the selected probe mask fixed, verifies each stored test recording before a pass, and advances to a lower rate only after a verified throughput-loss result and a closed, quiet, writable state on the same card. Uncertain delivery is never replayed.

The compiled204-input host source archive and focused wire/controller/UI evidence are recorded in ReleaseSources/1.0.0.221. The SDK is unchanged from209. The128-check actual215 UI result is reused only because its Form source is byte-identical in221.

V1 probe availability follows firmware-advertised masks; three-probe modes remain limited to15/7.5kHz. V2 source-gap distributions and V1 lowerbounds remain distinct. Missing values are not zero. Normal recording, test recording and temporary PC-reader tests have different scopes.

Family acceptance references: V1: SHA256 9E92F20C5BDE0AA926AC99C67DCD0BDD09810FD03DF3607346C2CDF03571E0EA; V2: SHA256 BA44D8BD976D23A1A5E132B7AAF10C1F836F364E327A5CF62CC61654112189BF. This note is separate from the pre-existing mixed-work Note/RECORDING_VALIDATION.md, which this preparation never stages or copies.

## Actual development checks

Both production-controller sweeps completed without manual fallback, Start replay or reset during their recording/readback flow. Each used 10 seconds per rate, fixed connected probes, and full saved-data readback before advancing.

| Family | Highest-rate result | Automatic next result | Saved IMU at passing rate |
|---|---|---|---|
| V1, two probes |30 kHz: 473 loss events (lower bound), longest SD write 61 ms |20 kHz: sealed loss counters and skipped-lane bytes zero; full readback passed; longest write 62 ms |12,594 grid rows, 12,593 observed; one initial unavailable slot, no later observed slot gap |
| V2, three probes |30 kHz: 684 saved NP gaps, max 18.773 ms, p95 3.413 ms, p99 15.36 ms; longest write 39 ms |20 kHz: zero recorded NP gaps/drops/overflows/discard, full readback passed; longest write 29 ms |12,506 grid rows, no observed gaps/queue/scheduler loss; 1,971 distinct valid native observations |

V1 preserved 52 prior entries on its first verification and 53 on the second. V2 preserved 18 and 19 respectively. Exact firmware/options/profile restoration is recorded by the separate family owners. These are short warm-handoff checks, not endurance or all-profile qualification. V1 exact neural gap durations remain unavailable. The cold/ambiguous-reset profile defect was fixed using an independently checked backup-register transaction; native interruption checks and source review pass. Physical cold interruption remains untested. Full IMU source-window/final-tail coverage and physical sensor conversion phase remain unknown.
