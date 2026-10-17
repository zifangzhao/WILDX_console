# V2 power-profile development firmware — 2026-10-16

Published at the user's explicit request as **firmware only**. WILD-X console
development and installer publication are managed separately. This package is
a development baseline, not an all-mode lossless release.

- [Fused HEX](V2_fused_RECOVERY_6408DBB8_USB_2E07799E_APP_136172A5_POWER_PROFILES_DEV_20261016.hex): recovery6408DBB8 + USB2E07799E + APP136172A5.
- [APP-only HEX](V2_logger_APP_only_136172A5_POWER_PROFILES_DEV_20261016.hex).
- [APP update container](V2_logger_APP_update_136172A5_POWER_PROFILES_DEV_20261016.xenp) for the protected updater.

USB auto-boot flags3 / loader ABI4 and all non-APP flash address-bytes match
the preceding package. Settings are omitted. Routine APP updates use the APP
container; fused HEX is for SWD or an already-active ROM DFU, without mass erase.

The APP selects power settings before acquisition: SD idle-clock gating for
single-probe/all7.5kHz modes; continuous SD clock for dual15/20/30 and
triple15/20. Existing CPU/PSRAM/sample clocks, -O3/cache, shallow WFI,
unused-probe powerdown, integrity checks and storage formats are retained.
Deep STOP remains disabled.

Exact APP136172A5 passed short10s capture plus native firmware verification
for dual15 (3904 blocks), dual20 (5208) and triple15 (5856). Peak raw retention
use including unpublished data was31.8%,31.8%,36.4%; RAM peaks5/9,5/9,6/9.
BLE rescue after a recorder fault returned the same MCU to Idle/error0.
Settled idle was about13.5mA at4.97V on the test power path.

**Limits:** dual30 still failed with source5/detail1 on the current Samsung512
card; triple20 remains unresolved. Three-probe30kHz is unsupported. Exact
candidate all-mode endurance/full-DAT/analog qualification is incomplete.
APP hardware checks used resident USB5AD122A7; the fused package reuses the
published USB2E07799E and has not had a new fused cold-boot/pair test. The
triple15 continuous-clock sample consumed more than its prior gated sample;
per-configuration power optimization is still in progress. No new GUI build
or installer validation is implied.

See [publication/evidence note](../../Note/V2_POWER_PROFILES_136172A5_20261016.md)
and [receipt](package-receipt.json). Previous915AE345 files are preserved in
`legacy/20261016_before_136172A5`.
