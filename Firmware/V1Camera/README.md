# V1 camera firmware — explicit selection only

This is the published camera40 candidate, reconfirmed by its owner on
2026-10-17. It is a separate V1 camera-board target. It is not a successor to
the Neuropixels APP121 / USB91 bundle, despite sharing version 1.0.121 and ABI258.

- APP: 1.0.121-camera40-default-candidate / CRC99BA4F27.
- Fused image: matching APP, USB83 / CRC82C6E21A, and recovery; configuration omitted.
- Original artifact publication: `31e1d51571f9ea0b80699c98b547a434f7c11c45`.
- Exact artifacts retained at `e831a5f10c28c1df596766b195d4a48aeb38991d`
  under `LATEST_FIRMWARE/legacy/camera40_20261014` in XE_NP.

In WILDX 1.0.0.35 or later, open **Advanced → Firmware Update**, select
**V1 Camera40 — candidate** under **Firmware target**, then **Use bundled APP…**
after confirming the physical camera target. **V1 NP (91k/91k)** remains the
default; shared versions/ABI never select a target automatically. Only the APP-only HEX
belongs in the compatible protected APP updater. Fused programming uses the
documented separate factory/recovery workflow without mass erase.

Forty fps is the highest tested steady camera rate. Saved rates are not migrated;
select and apply 40 fps explicitly. A short BLE SD camera40 / ADC200k / latest-value
IMU capture passed. Strict USB completeness failed with 11 reported camera drops.
Rates 41–49 and 50 fps remain unqualified. No endurance, implant, NP-probe or new
installer-pair qualification is implied.

See `release.json` for hashes, `source-manifest.json` and `source-README.md` for
unchanged owner distribution records, and `source-validation.md` for evidence.
