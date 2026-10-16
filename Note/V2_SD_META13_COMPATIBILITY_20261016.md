# V2 metadata13 compatibility delivery — 2026-10-16

Canonical implementation, no duplicate console in XE_NP. Scoped changes:
`Neuropixels/SD/NpV2SdDatExport.cs`, `USB512_interface/USB512_interface/NpLogger/NpDownloadCatalog.cs`,
`Neuropixels/Tests/TestNpV2SdDatExport.cs`, `Neuropixels/test_sd_dat.ps1`.
Strict classicv13 geometry/flags/closed allocation/config/footer validation,
retained older v12 reading, Failed/Reserved quarantine. No new format decoding
inside the MCU hot path. Existing USB-health compile links remain intact.

One canonical incremental Release build and337 actual GUI bridge/catalog/DLL
checks passed, including the newv13 fixtures. Exact GUI SHA256
`9B6021811B172253CEE9C0F270F1D41731A01B442CB0A86DACEEAA04BD966744`;
exporter `A04D630F1EA716FD3EC4D3DE54C3875479315B6647F4B4A3F4C60C26BB519070`.
Source owner confirmed no conflict with current USB-test/layout work at593ef16.

Hardware pairing: V2 APPF4057C04/USB2E07799E/recovery6408DBB8. All11 admitted
SD modes passed10s native/config/timing, plus warm2p30→1p7.5/120s→2p15/10s.
Actual production fullDAT passes:1p7.5/120s899788rows;2p15/10s149912each;
3p20/10s199986each;2p30/10s299980each. Original applied maps and on-card
Completecatalog/footer/config match; USB-session requests=completions/errors0.
Normal APP remount/Idle passed. No all-mode fullDAT/endurance/analog/combined
sensor/new USB-streaming/fused cold-boot or V1 hardware qualification.

One direct NSIS pass reused the checked GUI/exporter and the prior checked
other11 runtime files; all13 independently extracted hashes match. No GUI/helper
rebuild or installer execution during packaging. The dated installer is still
numeric1.0.0.33, same install directory, not version34 or a side-by-side app.
Old general/USB-quality33 packages remain. Full dirty-tree/cached-object source
provenance is not inferred from the current tree.

Installer `WILDX_console-Setup-1.0.0.33-V2_SD_META13_20261016.exe`, SHA256
`1B732353C7CCE6FE53CDBF975E6CB1C05411A2140C50E0576B29EEAC9052996C`.
[Manifest](../LATEST_INSTALLERS/V2_SD_META13_20261016.release-manifest.json).
Firmware/long-form evidence belongs to [XE_NP branchV2](https://github.com/zifangzhao/XE_NP/tree/V2/Note/V2_SD_LOWEST_CLASSIC_RELEASE_20261016.md).
Only1p7500 uses classic, others retain VSC. Fresh~41mA versus warm~80mA is
state-dependent; no guaranteed61% warm/battery saving. Camera removed and
SD IMU/AUX disabled in this firmware role; general console functions retained.
