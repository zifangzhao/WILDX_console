# V2 two-probe30k recording-first release — 2026-10-21

APP **660A6DA4**, USB **E323A06A**, recovery **6408DBB8**, WILD-X **1.0.0.70**.

- Routine update: `V2_logger_APP_update_660A6DA4_DUAL30_20261021.xenp`.
- SWD/already-active ROM DFU: `V2_fused_RECOVERY_6408DBB8_USB_E323A06A_APP_660A6DA4_DUAL30_20261021.hex`.
- Component HEX files and exact identities: `package-receipt.json` / `SHA256SUMS.txt`.

Delkin V90 (256.7GB reported): two real NP2 probes at30kHz passed600s recording,
all468752 native blocks read back, then full21.87GB independent USB download and
two DAT exports. All gap/drop/overflow/discard counters0; each DAT18000076 complete
384-channel rows. Payload36.4MB/s; ringpeak6/9, no PSRAM spills. CPU192/SD108MHz,
1.8V, classic format12 path. Power settings were not reduced.

The actual console70 WinUSB reader and production export bridge separately passed
the saved10s session, including loss reporting and configuration archive. Existing
GUI69 fixes are retained. Optional guarded-download recovery is unsupported by
E323; its ordinary read-only download passed. No visible installed-UI, all-mode/
card, analog quality, unlimited-endurance or newly-flashed-fused qualification.

Fused packaging preserves the exact checked seals and excludes retained settings;
unused firmware space is explicitly erased padding. No component rebuild/reseal.
Fused SHA256: `0BFECE90D3D0B098C645DB5370052C806EC6FB21FE0F1947F1718DFCE61EEFB1`.

Permanent experiment record: `Note/V2_RECORDING_FIRST_RESTORE_20261021.md`.
