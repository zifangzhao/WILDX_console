# WILD-X 1.0.0.88

[Installer](WILDX_console-Setup-1.0.0.88.exe).

V2 Firmware Update now has a primary Update bundled firmware button with
APP-only and full APP + USB + recovery choices. Full updates use the existing
CubeProgrammer DFU backup, exact UID, changed-page programming, preserved
configuration/upper flash, readback and reconnection checks. Firmware updates
still require explicit confirmation; detection never starts flashing.

Reported firmware mismatches prompt once for review. Exact matches remain
silent. Current legacy V2 firmware does not report image versions, so its
comparison is explicitly unavailable; bundled update remains accessible
manually. No board ID or installed version is guessed.

131,463 software checks passed, including actual-app V1/V2 parser, catalog,
fake-programmer failure/preservation cases, mismatch notices, passive identity
and rendered UI. No device or real DFU/flash operation was performed. The new
full V2 route is software-tested only; no newly-flashed-fused qualification.
Firmware binaries, SDK, helpers, recording and decoder behavior are unchanged
from 86. The firmware catalog adds the existing checked full V2 image.

[Exact manifest](release-manifest.json) / [workflow and validation](../Note/V2_BUNDLED_UPDATE_20261022.md)
/ [exact compiler inputs](../ReleaseSources/1.0.0.88/README.md).
Previous 86 is retained under legacy/20261022_before_1.0.0.88.
