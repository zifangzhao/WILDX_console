# NP2013 USB preview follows the applied IMRO map — 2026-10-12

## Confirmed improvement

The existing physical preview handled only NP1 NPB1. V2 never handed its NP2 requests or applied receipts to that preview, so NP2 showed C0–C383 rather than configured electrodes.

The minimal fix reuses the existing immutable display pages, 64-channel gather queue and renderer. Before START, normalized NP2013 requests from the unchanged shared configurator are converted to connector-specific channel/site/shank definitions; selected sites are checked against the compiled N2B1 shift bits. The vendor routing catalog is not duplicated. Definitions become visible only after all staged requests pass the existing matching applied-receipt checks, before USB host arm. Unconfigured/host-only maps remain explicitly unverified, and every new capture clears old applied definitions.

Labels include zero-based shank, electrode and hardware channel. Pages are sorted by shank, electrode row descending, then left-to-right. Geometry matches shared ProbeInterface export: four 1280-site shanks, aligned 32 µm columns, 15 µm rows and 250 µm shank pitch. DAT and decoded recording inputs retain hardware-channel order. No extra per-sample parsing, device command or firmware work is added.

## Actual evidence

- One incremental Release GUI build, version 1.0.0.5, reused the existing build cache, published BLE/SD source inputs, F779 connection transport and exporter. Unrelated private development was preserved and excluded.
- `Neuropixels/test_np_preview_mapping.ps1` passed 136,981 assertions. It checks existing NP1 behavior, actual shared-compiler NP2013 N2B1/normalized requests and geometry exports, single/multiple-shank maps, high sites, all three connector definitions, mapped sample gather, unmodified recording inputs, wrong-port rejection, immutable maps, applied-only display, new-capture reset, source-visible receipt ordering and offscreen rendering.
- The first regression attempt exposed a fixture-only `JavaScriptSerializer` ArrayList/object-array cast; the fixture was corrected and the targeted regression rerun successfully. The production GUI did not require a rebuild.
- Rendered NP2 four-shank map inspected offline. Native combo/edit text is not printed by the hidden WinForms screenshot; selected values and labels were asserted directly. No main application, scan, USB/BLE connection or hardware operation was invoked.
- One successfully generated NSIS installer; independent extraction matched all 13 staged files. The 12 non-GUI runtime files are identical to 1.0.0.4. An initial invocation stopped before installer creation because the manifest path was relative to the NSIS directory; the absolute input path was corrected.
- GUI SHA256: `D7B511B34DEA5199CBA6A41C7CF228B9613558C163DBC0B4E701FBB081E2BFA0`.
- Installer: `WILDX_console-Setup-1.0.0.5.exe`, 2,246,853 bytes, SHA256 `41C30299C69D16808F4C28BE212FEA97890E08CDE5ADD0F8CC520476EFF72A52`.

## Delivery and remaining task plan

The checked installer/manifest are in canonical WILDX and V2 `LATEST_INSTALLERS`. The previous 1.0.0.4 package/metadata remain recoverable under `legacy/20261012_before_preview_1.0.0.5`. Source changes stay in WILDX, not the firmware repository. No installation or Git publication in this task.

1. Install 1.0.0.5; retain/apply a map per connector and select that probe/page in USB preview. Loaded-only maps intentionally do not claim hardware application.
2. Diagnose the photographed setup failure separately from detailed `recording_settings.json` and `neuropixels_np2usb_status.txt`: `source=8` covers shift/status verification failure, including a status-read I²C error. It is not fixed by this PC mapping change. No claim of a connector/solder fault is justified without those details.
3. Firmware, USB bulk transport, DAT writes, SD recording and the separate BLE preview are unchanged. This software check is not a new live recording, ADC-quality, endurance or full firmware/GUI pair qualification. Existing BLE DFU visibility remains; no direct resident-USB-to-ROM DFU command was added.
