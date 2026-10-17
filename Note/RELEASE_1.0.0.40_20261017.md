# WILDX 1.0.0.40

Data Download automatically prepares the selected V1/V2 logger for USB SD
download when listing or downloading. It confirms the selected MCU UID,
requires an idle device, sends at most one mode request, waits for the same
logger to return in download mode, then opens the read-only data stream.
An already-ready download device is not switched again. Compatible standalone
download firmware remains supported. SD card readers do not receive logger
mode commands. The NP1 fast archive session listing uses the same V1 helper.

This also fixes a V2 unfinalized-tail case behind “NP2 committed extent escapes
the catalog entry.” The V2 writer initially publishes only payload-start in
the catalog. Its stopped checkpoint can save a CRC-valid FAILED/VERIFY_PENDING
footer containing substantial committed data before the catalog end changes.
An interrupted final publication can similarly leave a valid COMPLETE footer.

Read-only browsing now admits that exact combination only at the final
published catalog entry, after normal directory termination, with a zero
journal, matching card capacity, valid descriptor/footer geometry and CRCs,
and the original catalog end equal to payload-start. It derives the download
end from the committed footer count. The immutable reservation remains a hard
upper bound. No sample count is inferred from card allocation or a user's
size estimate. A later entry, arbitrary shorter fence, bad footer, pending
journal or changed capacity prevents this exception.

The original catalog end and derived read end remain separate. The download
saves a `download_catalog.txt` receipt with both values and stored state/fault,
alongside the unchanged descriptor/footer and recording metadata. A FAILED
source stays incomplete. Listing shows the committed recording size in GiB.
The card is not repaired or written; strict delete/format/allocation paths
continue to use the original catalog reader.

Validation: 40 actual-app read-only catalog checks include a virtual card
with more than 100 GB of committed metadata, not a 100 GB payload transfer. The
actual-app NP2 export matrix passed 396 checks, including byte-identical small
open-tail exports. Actual-source mode checks passed 36 for V1 and 19 for V2
using fake transports/helpers. The GUI build and independent installer
extraction passed. The reported physical session has not been downloaded or
hardware validated here; the screenshot alone does not establish its exact
stored bounds. If a different mismatch remains, the error now includes its
catalog/payload/committed/reserved LBAs, state, fault and block count.

Runtime helpers, exporter DLL and firmware payloads are inherited unchanged
from 1.0.0.39. The unrelated local USB readiness experiment remains excluded.
Only one final installer is present in `LATEST_INSTALLERS`; the previous
installer and original metadata are archived byte for byte.
