# V1 NP identity-parity candidate — 2026-10-17

APP **1.0.122-np1-identity-test / 8FD89229**, unchanged USB91 **40D39075**.
For the V1 old NP board with 91k/91k battery divider. Not for V2 or the camera target.

- [Fused bootloader + USB91 + SD/BLE APP HEX](V1_fused_bootloader_USB91_SD_BLE_v1.0.122-np1-identity-test_20261017T202533Z_r2.hex) — SHA256 `BEC699EBE16981BAFEFD3FEB1AFA8790D5AF0839B74DC556E3A51E6B3146694C`.
- [APP-only HEX](V1_SD_BLE_app_only_v1.0.122-np1-identity-test_20261017T202533Z.hex) — SHA256 `A70A35D70ABB34D7D8F11D3BD9AC868C38C1BBB1CE847E3336B3C7491F230F3F`.
- [Unchanged USB91 service HEX](V1_USB_service_candidate_v1.0.91-usb20-paired-restore-test_20261016T211951Z.hex) — SHA256 `29DC58DE50D8089695F5FDA07A6F56DBA6BAE7047D47DBD919D2B1F0AADEF5BA`.

This corrects NP1 identity admission parity with USB: both exact
`PRB_1_4_0480_1_C` and `PRB_1_4_0480_1` use the NP1 configuration path.
Confirmed EEPROM absence still selects NRIC, including its custom-map path.
Capture clocks, DMA/rings, SD scheduling and UART are unchanged.

The fused image includes/reprograms recovery and requires matching-target SWD
programming **without mass erase**. It excludes retained configuration; that
exclusion does not protect configuration against a mass erase. Do not send the
fused or service HEX through an APP-only updater. The APP-only HEX preserves
resident USB/recovery; with USB91, enter Recovery for the protected APP updater.

## Evidence and limits

The firmware owner reports passing canonical identity and maintained AUTO/map
admission software checks. The incremental APP build recompiled one capture
object and reused 74 checked parent objects. After APP-only installation on
local V1 UID 001D00065942500B20313354, independent APP/manifest readback matched,
and protected boot/configuration and USB were byte-identical.

**BLE recording remains unvalidated.** Two local attempts ended before
connection; no START was sent. They do not demonstrate a new recording, Stop,
custom-map application on the reported failing recorder, saved-data integrity,
or resolution of state 4 / fault 2. Fused-image programming has not been
hardware validated. This is a development candidate published at the user's
request for parallel validation, not an endurance or implant-qualified release.

The matching fused image was mechanically merged from the current published
APP121 fused image's exact boot/USB-owned bytes and the sealed APP122 image.
No new firmware compilation or hardware action was performed during fusion or
WILDX artifact publication. The owner had already built and APP-only installed
the candidate during the preceding investigation.

[Corrected packaging handoff](source-manifest.json) and [owner README](source-README.md)
are retained exactly. The [original APP122 handoff](legacy/rejected_sparse_fused122_20261017/source-manifest.json)
also records the retired sparse fused package; its `published:false` was the
pre-publication handoff state. Source base
`e92b8cbb9dabdc99ce7a140d09e7c822b1da7150` had uncommitted changes; it is not a
clean source commit for this binary. The [build/input receipt](source-build-receipt.json)
records the exact parent image, rebuilt unit, dependency hashes and flags;
the [canonical identity header](source-probe_identity.h) has SHA256
`13E869B1F12C47685014B3AA1B7D513D652D2D1C21C0E4F726BE36D8661B2361`.

Previous APP121 artifacts and exact source docs are preserved in
[legacy/app121_before_np1_identity_20261017](legacy/app121_before_np1_identity_20261017/README.md).
The existing WILDX 1.0.0.37 installer still contains APP121. This firmware-only
publication does not rebuild an installer or modify the frozen local .38 GUI.

Publication integrity checks passed exact payload hashes/lengths, HEX checksums,
configuration exclusion, sealed APP manifest/code CRC, fused APP bytes and
unchanged boot/USB address bytes. The existing catalog/identity/prompt fixture
passed 590 offline checks using the frozen 1.0.0.37 GUI with this repository's
new catalog. Prior V1 archive bytes, installer files, V2/camera payloads and the
frozen .38 executable were checked unchanged. These are packaging/catalog
checks; no firmware or GUI rebuild and no hardware action occurred here.

## Fused packaging correction, revision 2

The first APP122 fused file was rejected as partial by the published console.
The replacement `_r2.hex` explicitly fills unused firmware addresses with `FF`,
covering 2,088,960 bytes (all flash except the protected 8 KiB configuration
page). Exact APP122, recovery and USB91 bytes are unchanged. The actual frozen
WILDX 1.0.0.37 integrity checker now accepts it as a complete fused image,
including its existing recovery, manifest, code CRC and vector checks.
No integrity guard was relaxed and no firmware/GUI was rebuilt.
The [retired sparse package](legacy/rejected_sparse_fused122_20261017/README.md)
is preserved only as rejected packaging evidence. Use the `_r2.hex` link above.
BLE recording and fused programming still require hardware validation.
