# V2 compact BLE preview — 2026-10-20

APP **15FAE47B**, USB **E323A06A**, recovery **6408DBB8**, paired with WILDX
**1.0.0.63**. Use the matching fused HEX for the complete firmware, or the
appropriate component/update file. Retained settings are omitted from the
fused image. Installing the console does not flash a logger.

The actual console form received compact 500 Hz, 64-row waveform packets during
dual-probe 15 kHz SD recording, including switching to P2/channel383. Normal
Stop completed with 10,140 produced, written and readback-verified blocks and
zero terminal capture/SD faults. Invalid preview rows remain marked missing.
See [validation and limits](source-validation.md), `acceptance.json`, and
`package-receipt.json` for the exact checked scope and file hashes.

Dual-probe 20 kHz failed with source overwrite before preview. Triple-probe/P3
preview, endurance, analog quality, independent DAT export, visible installed
UI and new USB recording/export performance are not qualified by this release.
Previous files remain under `legacy/20261020_before_APP_15FAE47B/`.
