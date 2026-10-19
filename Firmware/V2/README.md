# V2 USB catalog repair — 2026-10-19

WILDX **1.0.0.47** adds **Repair SD catalog…** in USB Mode and Data Download.
Install USB **E323A06A** (not an APP-only update). Choose the logger, click
Repair, and confirm the freshly inspected card. Keep power connected.

- `V2_USB_only_E323A06A_CATALOG_REPAIR_20261019.hex`: USB service only.
- `V2_USB_update_E323A06A_CATALOG_REPAIR_20261019.xenp`: USB update container.
- `V2_fused_RECOVERY_6408DBB8_USB_E323A06A_APP_B60291EE_CATALOG_REPAIR_20261019.hex`: matching fused HEX; retained settings omitted.

Repair writes only catalog/journal metadata, not recorded payload. It does not
format, rewind or reclaim an unknown allocation. Interrupted recordings may
remain incomplete. Full/unknown-boundary errors remain protected.

Targeted native metadata/EP0,105 actual helper and11 compiled-control checks
passed. No new installed-pair, physical repair, recording or endurance test.
APPB60291EE/recovery6408DBB8 and earlier recording limits are unchanged. V1
repair firmware is not included. See `source-validation.md` for actual scope.
Superseded USB2E/fused files are retained under `legacy/20261019_before_USB_E323A06A`.

## Retained APP catalog Start fix

APP **B60291EE** replaces the stale control object in published APP136172A5 with
the checked control. It permits manual START for a harmless DEGRADED-only
catalog while retaining FULL/recovery/unknown-allocation protection. It does
not format, clear safety flags, recover or overwrite automatically. Actual
user-device APP/flags were not captured; a legitimate error8 may still occur.

The owner reused 62 frozen objects and relinked only the control replacement:
all14 RAM regions and148 semantic vectors match; capture/writer/clocks are
unchanged. The existing native control regression passed. No hardware or new
physical recording test was performed for this hotfix. Parent dual30 failure,
triple20 unresolved and all-mode/endurance limits remain; no in-animal or new
recording qualification is claimed.

GUI **1.0.0.45** is reused unchanged, retaining firmware-controlled manual Start.
V1/camera firmware is unchanged. V2 recovery **6408DBB8** and USB **2E07799E**
are unchanged. The matching APP-only HEX, protected APP .xenp and fused HEX
are bundled. The .xenp is the discoverable APP update in Firmware/releases.json;
the fused image omits retained settings. Separate BLE-listing work is excluded.

[Owner manifest](source-manifest.json) · [Checksums](source-SHA256SUMS.txt)
