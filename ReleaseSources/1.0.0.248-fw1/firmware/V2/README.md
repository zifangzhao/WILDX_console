# V2 idle-power firmware —2026-11-06

APP C88F3B27 + USB E581FE41 + unchanged recovery6408DBB8. Use the fusedHEX for the complete image, or the sealed APP XENP with the matching service. Retained configuration is excluded. Runtime WILDX248 production APIs passed; use the separately identified canonical firmware-payload-refresh installer for bundled updates.

Same-board/card BLE/SD idle A/B/A measured21.900→20.676→21.711mA: bracketed1.129mA/5.682mW/5.18% lower USB input. Active sensor polling, NP/SD/PSRAM clocks and buffers remain unchanged. One ordinary3P20k CLoff recording/full export passed8612NPblocks,13778gridrows and2189native snapshots with reported gaps/queue losses0. This is bounded data acceptance, not all-mode/endurance/analog qualification. All22older recordings preserved; bench originals restored.

See acceptance.json and Note/V2_POWER_EFFICIENCY_20261106.md for timing/native/provenance and measurement limits. Source/evidence archives are separate; no private fullflash/preferences backup is published.
