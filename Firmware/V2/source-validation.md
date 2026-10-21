# V2 compact BLE preview validation

The V2 developer supplied the exact APP15FAE47B/USBE323A06A/recovery6408DBB8
bundle and final acceptance receipt. The matching console is frozen GUI63,
SHA256 `8653385637E334133CF9C4B0D218F76AF43DD006C55644CBFDB766CB5302AE2C`.
The fused HEX SHA256 is
`1289F8D164412186A0AAA0F6578E1A3CC8EF0DE405AEABE613ED09E38D9CE57B`.
All six component/update/fused files are hash-identified in `source-manifest.json`.

The actual hidden form displayed compact kind5 packets at 500 Hz/64 rows with
a two-second plot window, first P1 then P2/channel383. It consumed 100 packets;
the independent observer saw 96, with 6,121 valid and 23 invalid rows. Neither
reported packet gaps, backwards packets or host queue drops. Invalid rows were
retained as missing. Selection/calibration downtime was 9,629.9402 ms. Twelve
recording clock heartbeats succeeded. The bounded pressure-retry policy passed
software checks; this successful run did not exercise a pressure retry.

Normal Stop completed: produced/written/readback-verified blocks all 10,140,
terminal error/fault zero, source counters equal, no CPU/SD fault, engines quiet.
Acquisition lasted 25,959 ms; drain 35 ms and readback verification 35,095 ms.
The 61.047-second adapter wall time includes verification and is not acquisition
duration. Stopped P2 compact raw bits matched stored SD data. Protected loader,
settings and exact APP/USB identities were checked. Normal USB boot/status and
mode return to APP/SD-ready passed; no new USB capture/export test is claimed.

This is a cached-component integration. Only the preview unit was newly compiled
at O3 and its object reused over the working APP8A839098 baseline. The other 62
object hashes, 14 RAM reservations and 148 vector meanings were preserved relative
to working8A. Compared with public B60291EE, working8A already differs in seven
objects: cold-init-retry main, source/power, live_sd runtime-window, dual_sd_meta
append, dual_sd_catalog, dual_sd_catalog_port and dual_sd_app triple15. Catalog
control retains the existing degraded-warning START fix. Private overflow-drain
is off; no private source-gap SDK/exporter enters this bundle. `source-build.json`
records frozen inputs. This is not a clean whole-source build or a claim that
the public source commit alone reproduces the APP.

Limits: dual20k failed source overwrite before preview; triple/P3 preview,
endurance, analog quality, independent DAT export and visible installed UI
remain unqualified. The first GUI63 connection attempt failed before commands;
a guarded normal reset restored BLE advertising. New V1 GUI64 recovery and V2
IMU startup investigation remain separate from this exact checked pair.
