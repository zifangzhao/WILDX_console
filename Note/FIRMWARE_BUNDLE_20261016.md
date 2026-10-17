# Firmware display and bundled update workflow — 2026-10-16

Human request: display firmware versions, keep latest firmware in this repository,
and prompt for updates. Firmware owners confirmed exact published artifacts and
their limits. Existing runtime protocols do not identify installed APP/USB builds;
neither ABI258/4, static bcdDevice1.03 nor legacy BLE version1 is a release version.

Implemented:

- `Firmware/releases.json` plus seven exact published V1/V2 firmware images,
  original receipts and READMEs. Every image SHA-256 matches its pinned source
  commit. V1 includes only current APP and USB-only HEX, not an obsolete fused
  image; V2 includes its published development package with limits intact.
- Installed and bundled versions in Advanced, BLE and both firmware managers.
  Friendly installed versions require exact family/build descriptor matches.
  Missing identities stay Not reported; boot identity is never invented.
- Idle connection prompts offer review once per device/release/session, without
  writing firmware. Exact reviewed predecessors can be labelled update available;
  unknown builds are not ordered. Known-current builds do not prompt. Active
  recording, busy/pending services, stale selections and other host owners exclude
  prompts. A response opens the existing manager, not an updater command.
- Bundled APP selection uses each family's existing protected updater, exact UID,
  validation and final confirmation. USB/recovery/fused images stay outside that
  route. Package paths stay within the bundle and file hashes are checked.
- Project output and NSIS now include Firmware. No installer/version/push or
  hardware operation was requested as part of this change.

Optional NPF1 host contract (firmware/common allocation review pending): USB
vendor-IN C0/6A/value0/index0/length192, gated by NPS capability bit7; BLE short
`3C BC 3E`, reply `AD BC` +192 bytes+`DA DA`, gated by NLR Flags bit25.
Existing published firmware has neither advertisement and receives no query.
This host support does not make the currently bundled firmware identity-capable.

The V1 owner proposed the record and audited free IDs; V2 has not frozen its
allocation/target ID yet. V1 target00010001 is explicit old-NP91k firmware
compatibility, not a physical resistor measurement. V2 automatic target matching
is disabled by an empty boardIds list. No firmware source/build/deployment is
silently included in this host change.

Record layout, LE,192 bytes: NPF1 magic at0; u16schema1/u16bytes192 at4/6;
family8; target12; full MCU UID16..27; validity/source flags28. APP descriptor
at32 and USB descriptor96, each64 bytes: imageID/base/length/codeCRC, raw buildID32,
manifest flags/minLoader/generation/reserved. Observed loaderABI160, recovery
protocol164, normalized role168, observed loader build tag16 bytes172, IEEE CRC188
over first188 bytes. Flags0/1 mean APP/USB cached header valid;2/3 code verified
this boot;4 target known;5 loaderABI known;6 recovery protocol known;7 loader build
known;8 cache ready. Unknown descriptors/loader fields are zero; CRC zero is valid.
Header-valid is the firmware's bounded check, not a host recomputation of the
original manifest CRC (the record does not contain the complete headers).

Validation: focused actual-assembly package/identity/framing/query/prompt and
offscreen manager checks; existing BLE workflow/native/simulated V2 checks.
Exact counts and binary/source identities are in the private checked-build receipt
under `_artifacts/firmware_catalog_20261016`. Existing system-info legacy GUI test
fails its minimum120px layout assertion on both the preceding08C5 build and this
build; it is an inherited layout assumption, not a new firmware-query failure.
The new focused fixture independently checks optional-query failure, capability
absence, busy exclusion and continued passive SD info, without hardware.

V2 owner compatibility update: published USB2E07799E passed canonical private
GUI9AE/exporterA04 full SD-to-DAT checks for two current v13 records: a10s private
preparation-sleep recording and a preserved60s APP136172A5 control. Catalog/request
identity, raw-burst CRC/sequence/epoch, all-block framing and DAT accounting passed
without retries or card writes. This evidence concerns that download/export route,
not the new firmware-display GUI or all-mode/endurance qualification. The bundle
retains published USB2E07799E and APP136172A5. Bench USB5AD122A7 derivatives and
private preparation-power APPDD42A3E1 are not substituted into the release bundle.

Later owner qualification: a fresh DOWNLOAD session after an APP136172A5
two-probe15k recording reproduced legacy request0x61 / Windows31 despite checked
card/idle state. The two bounded USB2E export passes above remain valid, but
USB2E07799E is not established as a complete export-reliability fix. The firmware
owner is investigating an independently reproduced EP0 status-DMA destination
defect on that baseline. No new qualified release pair, bundle substitution,
GUI change or additional host test follows from this report.

V1 fused-delivery correction — 2026-10-17: owner publication
`e831a5f10c28c1df596766b195d4a48aeb38991d` supplies the matching USB91 /
APPBC6B506E fused HEX, SHA256
`EC10D6D227BBFC5E2DA8F15CC4E4138E438FA68D8810015D7463EA0E51FCA2D6`.
The local bundle now contains eight firmware images (14 total files), including
this exact Git blob and the corrected owner manifest/README. The APP update
selection and installed-version rules are unchanged. Configuration is excluded;
recovery is included and reprogrammed. Fused delivery requires the documented
SWD workflow without mass erase and is not admitted by the protected APP updater.
Its hardware programming and implant qualification are not established.

The first owner commit was held after Git newline conversion changed the HEX
file hash; only the corrected publication was copied. `.gitattributes` now
disables text conversion for bundled HEX/container payloads. Filtered and raw
Git blob hashes match. Existing actual-GUI bundle/identity/prompt/offline UI
checks passed (590), plus four fused layout/component checks through the
maintained parser and 41 protected APP HEX admission/rejection checks. The
checked GPIO GUI binary was reused without rebuilding. Receipt and frozen
bundle are under `_artifacts/v1_fused_bundle_20261017`. No firmware programming,
installer rebuild or publication occurred; the single latest installer and its
manifest/checksums remain unchanged.
