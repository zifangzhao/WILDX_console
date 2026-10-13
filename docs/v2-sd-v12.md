# NP2 SD metadata v11/v12 — 2026-10-11

The canonical WILDX catalog and SD exporter now support the firmware's v12 AU-aware dual-NP2 metadata. They preserve the common CE64 directory, native NP2USB03 payload decoder, strict completion checks and v11 DAT output. No copied firmware-tree GUI code is involved.

The contract was read from `D:/Code/XE_NP_V2/Core/Src/xe_np_np2_dual_sd_meta.c`, its header and existing native tests. Frozen source SHA-256:

- Codec: `DE92D93E8AC22AE9CD220BDA618B86B5F4139CA75B964FEF8EE5C6C6AB9DFA40`
- Header: `091478861AA4EA8020FBBB1310F6E0E0A2D73312DEE25B90D678A79F508CC535`

## Metadata admission

Descriptor and footer retain their existing magic and CRC at byte 508. v12 uses version 12 at byte 8, flags 31 at byte 96 and a little-endian uint32 AU sector count at byte 128. The AU must be at least 1024 sectors and divisible by 1024; non-power-of-two sizes are supported. Bytes 132–507 must be zero. v11 retains version 11, flags 15 and its original zero tail at 128–507.

For v12, payload start is `ceil_AU(ceil_16384(reservation_start + 8192))`. Payload end is the exact whole-four-block-RU limit, while reservation end is `ceil_16384(ceil_AU(payload_end))`. Validation uses the payload limit for block accounting, checks the declared card capacity and the 22-bit VSC command argument in units of 1024 sectors, and performs alignment in 64-bit arithmetic. The footer must retain the same AU and immutable record/CID bytes. v11 continues to derive the RU limit from the reservation end as before.

The catalog preserves the descriptor's format version and routes both 11 and 12 to the NP2 exporter. Failed and VERIFY_PENDING entries remain non-downloadable. Export requires a complete, error-free, nonempty whole-RU footer and sufficient committed bytes. Payload sequence, tick, profile, immutable header, SuperSync and UTC checks still run before DAT files are promoted. Metadata CRC and the sequential-VSC flag do not establish an ADC payload CRC.

## Focused regression

The retained test uses the actual Release application's catalog/export bridge and adjacent DLL's metadata validator. **196 checks passed**, covering:

- Both 20 and 30 kHz, with 3072-sector and 86016-sector AUs. Native geometry examples match: payload/end/reservation `18432 / 19160 / 32768` and `86016 / 86744 / 180224`.
- v11 and v12 catalog admission with the original format version retained; byte-identical DAT output for both probes against the frozen pre-change v11 exporter.
- Real bounded payload relocation near the 32-bit LBA/22-bit command limit, without a two-terabyte backing image; alignment overflow is refused.
- Invalid version/flags/AU, reserved bytes, alignment, card capacity, CID, CRC and truncation; extra committed blocks cannot borrow unused AU padding.
- Structurally valid Failed and VERIFY_PENDING footers are refused by metadata validation, catalog download eligibility and the actual export bridge before an output folder is created.
- Corrupt payload sequence/ticks/profile/SuperSync/header, mismatched UTC and truncation cannot publish a complete DAT file. The valid fixtures include a wrapping UTC device counter.

The focused cases also run through the existing SD test entry point during future normal app-bound checks. This integration ran only the focused selection, not the broad regression suite.

```powershell
.\Neuropixels\test_sd_dat.ps1 -MetadataV12 `
  -ConsoleExecutable .\USB512_interface\USB512_interface\bin\Release\WILDX_console.exe `
  -BaselineExporterPath .\_artifacts\v2_sd_v12_20261011\baseline\NpV2SdDatExport.dll `
  -OutputDirectory .\_artifacts\v2_sd_v12_20261011\tests
```

`-BaselineExporterPath` is optional for subsequent runs; this run included it. The existing test-only CS0649 warning for `NpDownloadStream.Cancelled` remains. Retained evidence is `_artifacts/v2_sd_v12_20261011/targeted-test.log`.

## Build and scope

The final incremental Release build used `ResolveReferences;PrepareResources;Compile;CopyFilesToOutputDirectory`, with `Configuration=Release` and `GenerateNsisInstaller=false`. It passed with zero warnings/errors and retains all four embedded GUI resources. Compile-only targets omit resource preparation and must not be used to deliver the application. Build evidence is `_artifacts/v2_sd_v12_20261011/incremental-build.log`.

| Binary | Bytes | SHA-256 |
| --- | ---: | --- |
| Release `WILDX_console.exe`, version 1.0.0.3 | 1,183,232 | `F7790BE4B7638B0EEC7A4F8FE90D344A525A09F6D5F6703116D692870CD1DF55` |
| Adjacent `NpV2SdDatExport.dll` | 165,888 | `71F0EA0CAE654A3F262E788C6E1CE2338E5D26AFFDA9A754A8E9FFC05F24324E` |
| Frozen baseline `NpV2SdDatExport.dll` | 165,376 | `179E3A6A2D55BAA2C3FF49FB98AE577D1F7A125D0405C82530AC3DF3A07C3B15` |

Production changes are limited to `Neuropixels/SD/NpV2SdDatExport.cs` and `USB512_interface/USB512_interface/NpLogger/NpDownloadCatalog.cs`. The existing `Neuropixels/Tests/TestNpV2SdDatExport.cs` and `Neuropixels/test_sd_dat.ps1` retain the focused regression. README and this document record integration evidence.

There was no hardware access, firmware flash, version bump, installer/package generation or push. The private default-off live-VSC firmware candidate is not a qualified release; these synthetic offline checks do not establish sustained dual 20/30 kHz recording or active BLE reliability. The published installer and its paired firmware identities remain as previously documented.
