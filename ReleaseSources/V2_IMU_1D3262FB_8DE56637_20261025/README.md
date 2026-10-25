# Standalone V2 firmware update for WILD-X 1.0.0.138

Canonical firmware is now Recovery 6408DBB8 / USB 1D3262FB / APP 8DE56637,
copied from XE_NP commit `3280df8fea774eb3fce1650a6fd1c84ce4383817` on branch
`V2`. Every firmware blob, receipt and source archive was checked against that
published commit and its sealed hashes. The previous canonical V2 bundle is
archived under `Firmware/V2/legacy/20261025_before_1D3262FB_8DE56637`.

[Download the standalone update](../../Firmware/Updates/WILDX138_V2_1D3262FB_8DE56637_20261025.zip).
Extract it, close WILD-X and run `Install-V2Firmware.cmd`. This updates the
installed firmware files and matching catalog, preserving V1. Reopen WILD-X
and use Firmware Update to flash the V2 device as a separate action.

The published 1.0.0.138 installer and application/SDK are unchanged. That
installer contains the older V2 firmware catalog, so reapply this standalone
update after reinstalling it. No application compilation, device flashing,
hardware access or installer execution was performed for this catalog update.

The firmware developer's bounded final-image checks passed USB three-probe
20 kHz recording and one-probe 30 kHz recording after a restart/rate change,
with 186/189 fresh IMU records and no reported data loss. APP BLE/SD recording
passed Start/Stop, catalog preservation and complete export of 8,468 blocks
with 216,780 AP rows per probe. Four saved during-recording B9 snapshots with
valid AG/MAG replayed through the actual 138 display and journal; the older
observer's failed gate was a stale Boolean cast, preserved in the diagnosis.

First-sample PC time is unavailable for legacy UTC1 recordings. The optional
GUI startup-anchor query can time out for five seconds without rejecting
recording. One earlier USB return required a cable reconnect; a later final
image return passed. The fused image was assembled from checked components
and was not separately flashed whole. These short selected-mode checks do
not qualify all cards, all modes, endurance, analog quality or physical timing.

The standalone updater was exercised against a verified temporary copy of
the 138 payload. Checks confirmed both catalog families load, the matching
V2 pair does not produce a stale update offer, V1 files/metadata and GUI/SDK
hashes stay intact, reapplication is idempotent, and wrong-application or
corrupt-payload cases fail before modifying the installed catalog.

`upstream/` and `compiled-sources.zip` retain the firmware owner's original
receipts/source archive. Their publication flags describe the pre-publication
receipt state. `integration.json` records this canonical integration;
`checks/` contains the actual-138 compatibility results.
