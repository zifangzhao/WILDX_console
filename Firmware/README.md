# Bundled firmware

The app verifies every asset in releases.json by SHA-256. Connecting does not
flash a device. Automatic notices require verified known predecessor identities;
current, missing and unrecognized builds do not cause speculative update prompts.

| Target | Bundled components | Scope |
| --- | --- | --- |
| V1 old NP, 91k/91k | APP FA2016E7, USB 02FF93A9, matching fused image | [Bounded SD/BLE release](V1/README.md) |
| V1Camera | Unchanged separate camera40 candidate | [Explicit camera target](V1Camera/README.md) |
| V2 NP2 | Unchanged APP 136172A5, USB 2E07799E, recovery 6408DBB8 | [Development firmware](V2/README.md) |

**V1 large USB downloads remain unresolved:** the current hubbed bench still
fails Windows 31 on large replies after two bounded retries. Small CRC-bound
reads, checked cleanup and APP return pass. This is not full USB qualification.

V1 P1+P2/15 kHz completed 810.105 seconds/18.84 GiB with BLE status/preview,
reconnect and clean Stop/catalog; short P1+P2/20 kHz also passed. Two bounded
saved-data windows passed, not full-extent readback. Physical Format/power-cut,
all profiles/custom maps, camera, fused flashing and implant use are unqualified.

APP-only updates preserve USB/recovery/settings and do not install the USB service.
Service/recovery/fused files require their separate documented workflows and cannot
use the APP updater. The V1 fused image excludes configuration; do not mass-erase.
V1Camera requires explicit target selection. V2 automatic target IDs remain disabled.

Exact owner evidence, firmware hashes and limits are in V1/source-manifest.json.
Dirty-tree/selected-input build receipts are not a clean all-source reproduction
claim. Superseded V1 NP assets remain in V1/legacy. Firmware text normalization
is disabled to preserve exact bytes. V1Camera and V2 files/catalog entries are unchanged.
