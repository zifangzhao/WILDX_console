# WILDX 1.0.0.1 — NP2 IMRO UI / example validation

## Scope and implementation

USB Mode → Recording settings and BLE Mode now expose **NP2 IMRO / channel maps…**. The dialog loads `.imro` or native JSON through the existing bundled parser and opens the existing NP2 editor. The Ponytail workflow reused those components instead of adding another parser or dependencies. Each physical connector retains its own host request. Cancel discards unaccepted changes; a failed import never replaces a valid request. Requests are keyed to the USB MCU UID or BLE peer, not shared between unrelated devices.

This is a **host-file feature**, not a firmware channel-upload implementation. V2 does not accept these channel selections; imports are never reported as applied. USB recording saves per-connector requested/unverified sidecars outside the capture hot path. BLE imports do not program the probe or modify SD recordings. Save/reopen the map to move it between BLE and USB workflows. NP1 device staging remains unchanged.

## Actual checks

One incremental Release build of the changed application passed. MSBuild also invoked its existing helper targets; delivery reuses the exact previous 1.0.0.0 helper payloads, not the incidental rebuilds. The targeted test was rerun against those frozen delivery helpers. All 12 non-application runtime payloads are unchanged from 1.0.0.0.

`Tests/Np2ImroTests.cs` passed **98** checks against the actual Release executable and bundled parser, including legacy/commercial single-/four-shank types, selected-connector routing, exact imported bytes, native JSON, invalid/truncated/NP1-as-NP2 rejection, unverifiable applied-state rejection, independent requests, cancellation, visible/unclipped controls and requested/unverified recording provenance. The real WinForms dialog was rendered offscreen and visually inspected. An initial fixture array cast and invalid positive-fixture hardware state were corrected; the application did not need another build.

All **19 standard NP1/NP2 examples** found in Downloads passed their appropriate paths:

- NP2 user examples: `probe_1 (1).imro`, `probe_2 (1).imro`, `probe_3.imro` (type2013).
- NP1 user examples: `probe_1.imro`, `probe_2.imro` (type0), through existing `--prepare-v1`; never misidentified as NP2.
- Vendor type21: `botRow448_ref0`, `botRow256_ref0`, `bank2_ref0`, `bank1_ref0`, `bank0_ref1`, `bank0_ref0`, `bank01_checker_ref0`.
- Vendor type24: `shank2_botRow0_ref3`, `shank2_botRow0_ref0`, `shank0_botRow0_ref1`, `shank0_botRow0_ref0`, `hStripe_botRow592_ref0`, `hStripe_botRow272_ref0`, `hStripe_botRow0_ref0`.

Vendor filenames above have the `NPtype21_`/`NPtype24_` prefix and `.imro` suffix. Source: `C:/Users/antfe/Downloads/Release_v20250930-api4/Release_v20250930-api4/IMRO_Tables`. Four `NPUHD2_*` files are type1110, a different probe family; **unsupported**, not claimed passed or converted to NP2. All input files were read only; vendor files are not redistributed.

The three user-supplied type2013 files in `ProbeConfig` are byte-identical to the passed Downloads inputs (verified SHA256), and are published unchanged as project examples. Their filenames do not select a connector; choose the physical connector explicitly in the host dialog. Vendor files are not redistributed.

Reproduce the focused test by compiling `Tests/Np2ImroTests.cs` with the Framework Roslyn compiler and references `System.Drawing.dll`, `System.Windows.Forms.dll`, `System.Web.Extensions.dll`. Run it with the Release executable, output directory, `Neuropixels/NPConfigurator/catalog.json`, then optional example paths. Removable evidence: `_artifacts/np2_imro_20261010` (`build.log`, `test_frozen_helpers.log`, `results/np2-imro-dialog.png`, packaging logs).

## Delivery and remaining limits

Checked application SHA256: `0C3879C82CAAACE3622B284A977CE54A2C73D89907C75C826C2DAD4E35530D0A`.

Installer `WILDX_console-Setup-1.0.0.1.exe`, 2,227,110 bytes, SHA256 `8F73BC875A1BE81BF2EB817FA6E58F83645AF74F24246130F1B2D744F6F6ECE0`. One successful NSIS compile; an empty staging-field preflight was corrected before installer creation. Independent extraction confirmed all **13** payloads. The installer was not executed. Previous 1.0.0.0 delivery and identities were archived recoverably under `LATEST_INSTALLERS/legacy/1.0.0.0_20261010`.

Firmware remains loader97C3716A / USB DFB8F25E / APP ED881256. No firmware source/build/flash/reset, SD access or hardware operation was performed. Prior release evidence is retained, not repeated or represented as fresh end-to-end qualification. Current V2 channel upload, UHD import, live GUI acquisition/update and full hardware/integrity qualification remain open. Next firmware work must explicitly design/validate NP2 configuration receipts before any imported map can be labeled applied.
