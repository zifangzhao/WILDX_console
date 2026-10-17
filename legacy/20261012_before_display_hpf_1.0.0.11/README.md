# Current installer — WILDX 1.0.0.10, 2026-10-12

[Install WILDX 1.0.0.10](WILDX_console-Setup-1.0.0.10.exe).

NP2 USB Recording settings now offers **Merge probes into one AP DAT after
Stop**. Merging runs on the PC only after capture/recovery ends, with no new
copying or joining in the active capture path. Each segment's merged DAT uses
ascending physical probe order, 384 hardware-order channels per probe (768 for
two, 1152 for three). Original per-probe DAT and RAW files are retained. Common
complete rows are merged; unmatched tails remain in the originals. Timing
offsets are recorded in `np2_merge_status.txt`, not corrected or presented as
proof of simultaneous sampling. NP2 has no separately acquired LFP stream.

**NP auto-recover (gaps allowed)** now supports NP2 USB. Recognized transport
failures can start a fresh segment on the same MCU UID with the selected
rate/probes/IMRO intent retained. Stop cancels pending retries. Recovery is
bounded; configuration, firmware, disk, decoding and integrity faults are not
automatically retried. Segments are never joined across gaps; exact missing
samples and gap duration remain unknown. Read `session_events.csv`,
`session_status.txt`, each segment's capture status and merge report.

The actual Release GUI passed 10,501,794 merge/recovery software checks and 40
merged/per-probe JSON/XML checks. No physical reconnect/capture qualification,
installer execution or new firmware/GUI pair qualification was performed.

The signed NP2 convention from 1.0.0.9 is preserved: `2048 - raw12`, int16LE
nominal counts. Firmware, DFU updater, configurator and SD exporter are unchanged.
Triple 30 kHz USB remains unsupported; merging/recovery does not increase
supported acquisition bandwidth. This GUI feature does not add SD/BLE recovery.

Source/evidence: `Note/NP2_MERGE_RECOVERY_20261012.md` in the canonical WILDX
repository. Local package is software-checked, not yet published this cycle.
