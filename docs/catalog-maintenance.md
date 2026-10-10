# Shared V1/V2 catalog maintenance — 2026-10-10

Desktop catalog reading already recognized `NP_CE64_1`, `NP_PSSI_6` and `NP_PSSI_7` under `CE_FILESYSTEM_V1`. Removable-card inspection and Quick Format used the narrower legacy `IsNpIdentity` check and therefore refused the two PSSI identities.

`NpCardFormat.Inspect` and `NpCardFormat.QuickFormat` now use the existing `IsCatalogIdentity` check. These are the only two production-line changes. The outer directory/journal reset is shared, and the existing identity sector is restored byte for byte. `IsNpIdentity` / `ClassifyIdentity`, firmware append policies, and `NPSDLOG1` / `N2SDLOG1` recording dispatch were not changed.

Changed files for this correction:

- `USB512_interface/USB512_interface/NpLogger/NpDownloadCatalog.cs`
- `Tests/StandaloneTests.cs`
- `README.md`
- `docs/catalog-maintenance.md`

One incremental Release build passed with zero warnings/errors. The actual built application's pure Quick Format path passed **27 focused checks** using memory streams:

- All three supported tags reach the same reset path and return an empty catalog.
- Only directory head 128 and journal sectors 112/113 are cleared. All identity, settings, recording metadata, payload and other bytes are preserved.
- Identity is invalidated before resetting the catalog and published last.
- Legacy writer identity eligibility remains limited to `NP_CE64_1`.
- Unknown/future tags, a bad filesystem magic, a changed confirmed identity, changed capacity or absent confirmation are refused before any write.

The new checks also run in the normal standalone suite. This correction was validated with the focused entry point only; the unrelated full suites were not rerun.

```powershell
.\build.ps1
$compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /warnaserror /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /out:_artifacts\standalone-tests\StandaloneTests.exe Tests\StandaloneTests.cs
.\_artifacts\standalone-tests\StandaloneTests.exe `
  .\USB512_interface\USB512_interface\bin\Release\WILDX_console.exe `
  .\_artifacts\standalone-tests --catalog-format-only
```

Built executable SHA256: `67DF5605EDDE6A3858EF71F771B223636A3465093A922D87DA9FA080DF01BBFD`.

The existing UI still requires explicit destructive confirmation with No selected by default. USB logger endpoints and external fixed disks remain excluded from formatting. Busy-workspace/source gates, physical removable-media checks, system/boot/read-only/offline refusal, reinspection after confirmation and volume locking are unchanged. These native/WMI/UI paths were reviewed in source, without a physical disk or prompt being opened; the focused test exercises only memory-stream reset and identity admission. No firmware, hardware, version bump, installer packaging or push occurred. The candidate installer is unchanged and does not include this correction.
