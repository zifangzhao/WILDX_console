# WILD-X 1.0.0.246

V1 USB/BLE Delete Last uses confirmed same-card metadata transactions and automatic interrupted-delete recovery. Bounded hardware checks preserved 74 protected earlier entries, verified recovery after a 5-second VEE hold at the CP0/CP1 boundary plus MCU reset, and passed post-recovery two probes at 15 kHz NP/IMU readback. Final USB cold readiness was checked independently; unchanged storage/recording results are reused for that final delta. V2 retains its accepted EDEB/99 pair.

[Qualification](qualification.json) and [development results/limits](../../Note/V1_DELETE_LAST_20261105.md) describe exact conditions. Startup-slot and final-tail/source-window limits remain explicit. The physical test does not establish whole-device power loss or all SD-controller behavior. Fused images are compositions of checked components, not a new whole-image flash claim.

The installer was packaged once and its 49-file payload compared against one fresh extraction. See package-verification.json and approved-payload.json. Host-build.json and host-source-archive contain the exact 246 compiled inputs; unchanged SDK source/build evidence is reused from 235. Historical raw build annotations are preserved and do not identify the current published version.

V1 source-manifest.json identifies the exact final APP/USB pair, the immutable0194 source base and the final explicit overlay. The prior03C overlay preserves its recovery-change evidence; no archive is relabeled as hardware acceptance. Family acceptance and compact HIL closure are separate. The earlier 241 SD Info/preview/port-label work and unchanged V2 evidence remain in ../1.0.0.241.
