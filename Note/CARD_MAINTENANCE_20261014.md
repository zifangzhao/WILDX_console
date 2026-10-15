# Data Download card maintenance — WILDX 1.0.0.33

Data Download now contains **Delete last recording…** and **Format card…**.
The existing Format button had been reparented into Advanced; Advanced now
links to Data Download instead.

Both actions require a removable physical SD card reader containing a known
Neuropixels/media catalog. Logger USB download firmware advertises read-only
access and exposes no delete/format command, so those sources remain disabled
with a short explanation. No firmware changes are included.

Delete inspects the selected card, reads its current last catalog entry, and
confirms the card identity, session, and LBA extent. Before writing it checks
the disk identity/capacity again, locks/dismounts its volumes, opens the disk
exclusively, and compares the confirmed directory page and tail metadata.
The native `NPC64TX1` shadow/journal protocol publishes one directory-page
update, with a durable flush and readback at every phase. Earlier records,
settings, descriptors, and payload sectors are untouched. A pending/torn
journal is refused; failures are never retried automatically. Interrupted
maintenance can require catalog inspection/recovery before the card is used
again. This release does not qualify firmware recovery for every such state.

Format performs the existing native catalog reset and retains settings. Both
operations remove catalog entries so their space can be overwritten; neither
securely erases payload data. Maintenance cannot be cancelled once started.

Validation:

- GUI release build: zero warnings/errors, isolated release-31 source baseline
  with four updated inputs. The other 79 source inputs are unchanged.
- `Tests/NpCardMaintenanceTests.cs`: 6,593 checks against the actual assembly,
  using sparse synthetic cards. Covers all three supported identity tags,
  V1 neural and failed typed-media entries, V2 complete/failed entries,
  counts 1/2/62/63/64/126/127, repeated deletion, stale confirmations, unknown
  identities, empty catalogs, and injected write/flush/readback failures in
  every publication phase. Includes actual form placement and source/busy
  eligibility checks. No physical media writes or enumeration.
- Existing standalone regression: 294 checks passed, including native format
  byte preservation/refusal cases, BLE receipt parsing, shared catalogs,
  transport ownership, tab navigation, and real form renders.
- V1/V2 USB source labels: five checks passed.
- Installer independently extracted; all 13 runtime payload files verified.
  The 12 non-GUI runtime files match release 31. Installer was not executed.

This is software validation. No physical card was deleted/formatted, and no
USB/BLE/SWD hardware actions were performed.

Version 33 supersedes version 32 to exclude concurrent V2 catalog display edits
from this storage-maintenance release. The V2 candidate source remains in the
local working tree; its new profiles and firmware are not part of this release.
