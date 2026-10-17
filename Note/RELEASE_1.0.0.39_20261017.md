# WILDX 1.0.0.39

This release fixes read-only SD catalog browsing so a corrupt recording does
not hide later readable recordings. Damaged entries remain visible as
unavailable. A bad directory entry can be skipped when a later recording's
start can be established unambiguously from valid metadata. Unknown card
identities remain unsupported; browsing never repairs or writes the card.
Format, delete, recording allocation and firmware recovery retain their strict
checks. Firmware-level BLE mounting of a damaged directory is still being
investigated separately; this software does not change that firmware behavior.

Incomplete V1/V2 neural and V1 camera/audio recordings can now be downloaded.
Only validated, committed data is decoded. Original descriptors, footers,
configuration and recording metadata stay with the output, which remains
marked incomplete. An NP2 allocation with no committed blocks produces
metadata only. Corrupt payloads still fail validation and are not promoted to
finished DAT files. An incomplete camera raster is retained separately as a
partial file.

BLE V1/V2 preview keeps Preview selected when changing channel/probe, offers
a 0.1–10 second viewing window and shows recording information above the
plot. USB neural preview also shows the existing recording summary above its
controls. BLE reports committed recording size and total SD capacity; current
firmware status does not establish live free space. V2 preview supports both
legacy packets and the new compact packet protocol, using actual sample
rates and retaining real gaps. The new producer firmware is not bundled or
claimed to be hardware validated.

V1 BLE configuration is scoped to the connected address. Its read-only
failure report and delayed SELECT status handling are included. These do not
claim to resolve every device capture-source fault.

The installer bundles the corrected full-coverage V1 APP122/USB91 candidate
and current V2 firmware catalog. The V1 identity candidate accepts both
supported NP1 part-number forms; hardware recording validation is pending.
No firmware is installed automatically. Run WILD-X is available after setup.

Validation before packaging used the actual candidate assembly for 28
read-only corrupt-catalog checks, 31 recording-metric checks, 23 V1 report
checks, 116 media checks and 372 NP2 metadata/committed-prefix export checks.
The actual-source V2 display suite passed 5,505 checks, and media flow passed
44. Build and installer extraction passed; these are software checks, not
hardware qualification. Additional device and V1 preview checks were deferred
at the user's request to ship the current software immediately.

The release manifest records all compiled GUI/exporter source hashes and
extracted payload hashes. The unrelated experimental USB download-readiness
change is excluded. The previous final installer and its original metadata
are preserved byte for byte under
`LATEST_INSTALLERS/legacy/20261017_before_1.0.0.39/`.
