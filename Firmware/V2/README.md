# V2 USB card formatting — 2026-10-22

USB **B6099A0E**, APP **660A6DA4**, recovery **6408DBB8**.

Use WILD-X91's full APP + USB + boot update to install USB formatting. An APP-only
update does not change the USB service. The USB .xenp is a tooling artifact;
the existing APP updater cannot install it. Full fused ROM DFU/SWD is required.
After updating, select the logger in Data Download, press Format card, and
confirm its UID, CID and capacity. Formatting removes all native catalog
recordings and is not a secure erase. Flash settings are preserved.

Actual-adapter/real-catalog software checks cover one-shot requests, physical
CID/CSD refresh, invalid/interrupted catalogs, metadata readback and payload
preservation. The new USB image has not been flashed or tested on a card here;
no new pair, recording, download speed or HIL qualification is claimed.
APP/recovery seals are unchanged. Exact identities and source archive are in
package-receipt.json; see ../../Note/V2_USB_FORMAT_20261022.md for validation.
