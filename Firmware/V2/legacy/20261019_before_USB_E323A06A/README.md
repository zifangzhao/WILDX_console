# V2 catalog Start firmware hotfix — 2026-10-19

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
