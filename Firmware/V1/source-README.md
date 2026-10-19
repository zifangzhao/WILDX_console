# V1 SD/BLE operational candidate — 2026-10-18

Frozen pair: APP `FA2016E7`, resident USB `02FF93A9`, WILDX `1.0.0.44`.
This is the old V1 NP/91k–91k target, not the separate camera target or V2.
Exact files, hashes and canonical descriptors are in `manifest.json`.

P1+P2/15kHz ran810.105s and saved18.84GiB, including BLE status/preview,
disconnect/reconnect and a clean Stop/catalog publication. A short P1+P2/20kHz
recording also passed Start/progress/Stop/catalog. Independent stopped-device
readback checked the first128chunks of the20k recording and last128chunks of
the15k endurance run:65535frames per probe per window,zero known protocol-field,
counter or cadence errors. This is bounded saved-data evidence,not a full-card
or full-endurance readback.

Storage hardening detects failed/torn metadata publication and preserves
uncertain extents. WILDX offers explicit read-only recovery; it does not silently
repair data, reclaim unknown reservations or automatically format a damaged card.
Guarded USB Format works without a healthy catalog in software tests; no physical
format or power-cut experiment was performed for this candidate.

Known limitation: large USB reads on the current hubbed bench still fail with
Windows31. Small CRC-bound reads and checked cancellation/APP return pass.
Two bounded retries never accept failed/partial bytes. This release must not
claim complete USB-download,all-NP-mode,camera,physical-power-cut or implant
qualification. Broader stability and power work follows this release slice.

The owning packager's offline main-form constructor passed. This Compile-only
EXE omits empty/generated resx sets and the optional custom icon; the existing
system-icon fallback is used. This cosmetic limitation is accepted for the
scoped release; no startup failure or need for firmware changes was established.

The fused HEX is a direct merge of the exact checked current boot+USB and APP.
It has complete firmware coverage with erasedFF padding and excludes retained
configuration. Actual WILDX44 classifies it as full,not partial; no checked
component was rebuilt or resealed for packaging. Fused flashing itself was not
requalified. Never mass-erase,change option bytes or use V1 files on V2.

Owning WILDX developer: package the supplied checked44 EXE once using the
existing validated43 runtime dependencies,with these exact V1 NP assets and
canonical descriptors. Do not Compile or change V1Camera/V2. Archive superseded
V1 assets,update firmware catalog and release limits,and publish to the existing
WILDX release branch. Report the installer identity and remote commit.
