# WILD-X 1.0.0.91

[Installer](WILDX_console-Setup-1.0.0.91.exe).

Data Download now supports guarded V2 USB card formatting and retains the
selected device across USB mode changes. Older V2 USB firmware offers the
bundled full APP + USB + boot update; an APP-only update cannot add formatting.
After updating, press Format again and confirm the exact card. Format removes
all recordings from the native catalog and is not a secure erase.

The V2 Stop finalization fix from90 is retained. SDK, helpers, V1 firmware,
V2 APP and recovery are unchanged. The new V2 USB format firmware has software
checks only; no connected-card formatting, flashing or HIL was performed.

[Exact manifest](release-manifest.json) / [validation](../Note/V2_USB_FORMAT_20261022.md)
/ [exact compiler inputs](../ReleaseSources/1.0.0.91/README.md).
Previous90 is retained under legacy/20261022_before_1.0.0.91.
