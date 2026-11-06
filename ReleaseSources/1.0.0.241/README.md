# Checked development bundle 1.0.0.241

The app and matching firmware catalog are implemented and checked locally.
No installer, Git commit or publication was performed for this work package;
the published installer remains1.0.0.238.

| Family | APP | USB | Focused hardware acceptance |
|---|---|---|---|
| V1 |D05473D6|71AE291B|SD Info cold/after reads;2P15k USB/SD NP+IMU; two-channel625Hz LFP preview with no observed gaps/drops and96/96 source-value matches|
| V2 |99CC44F9|EDEB6C3A|SD Info, quality, completed-cache A1/A7;3P20k SD full readback; delete only the generated recording, preserving22 earlier entries|

Both devices were restored to their exact original firmware/options/settings
and idle modes, with hardware leases closed. Large generated recording payloads
were removed after compact fixtures were secured. Required restore backups
remain with their lab owners.

App240 supplied the functional/physical control evidence. App241 changes only
two SD Info display strings and its version; `labels-and-delta.json` records
the source comparison and actual241 label checks. Actual241 maintenance
regression checks pass220 cases. The SDK is reused unchanged from its recorded
qualification; its hash and receipt are in `app-build.json`.

`catalog-verification.json` records the actual241 loader checking the final
repo/runtime catalogs and all13 referenced assets. `qualification.json` and
the family acceptance/closure files define the supported claims. Raw build
receipts preserve their creation-time pending/test flags; final family
acceptance/restoration receipts supersede those initial states. Their original
bytes/hashes remain unchanged.

V1 FS2 Delete Last remains unavailable. Preview and closed-loop are mutually
exclusive; prior closed-loop qualification is retained for unchanged
processing. Existing startup/unavailable-slot and terminal/source-window
coverage limits remain explicit. Fused files compose checked components and
were not newly flashed as whole images. There is no all-card/endurance or
new camera/audio performance claim.

See `Note/GUI_SUPPORT_IMPLEMENTATION_20261105.md` and
`Note/USB_MAINTENANCE_WIRE_20261105.md` in the repository for behavior and wire
details. `app-source.zip`, `gui-native-evidence.zip` and each family's source
and final closures retain compact source/replay custody; no full recording
payload is included.
