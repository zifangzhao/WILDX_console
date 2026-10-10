# V1 firmware flash software candidate

Flash firmware… accepts ordinary addressed HEX and validates its contents before
any mode change. Pure APP-only input requires the complete 128-byte V1 manifest
plus its exact declared payload, ABI, vectors and CRCs. Complete fused input
requires every byte in the matched 2 MiB firmware layout except the entire 8 KiB
configuration page, valid APP/USB (and legacy download where present) manifests,
vectors and CRCs. Partial APP+USB, service-only, overlaps, invalid checksums,
missing records and out-of-layout/configuration addresses are rejected.

The V1 recovery loader lacks a separate authenticated board manifest. Full
firmware and the APP-only DFU fallback therefore require the reviewed ABI258
loader anchor SHA-256 03cbca29117ea3c54f797f5cd9698e786bc5d0ffa73853febad3431b425e3a7f
over 0x08000000..0x0801BFFF, from the qualified 1.0.36 firmware source. This admits
the current released HEX while failing closed for unrecognized recovery code.
Future loader builds require a reviewed anchor update; a filename is insufficient.

APP-only input uses the existing resident transactional updater when advertised
and idle. It validates again before BEGIN/erase, writes sequentially, checks
COMMIT/AppValid and restores the original idle USB mode. No automatic fallback is
made after that route starts. Otherwise the bounded DFU route is offered.

DFU programming requires installed STM32CubeProgrammer CLI. The GUI can locate
its executable. It freezes the selected bytes before confirmation; generated
operation HEX is locked against mutation until verification ends. No shell is
used. Before a new DFU request, pre-existing/multiple DFU targets are rejected.
An already-DFU retry is possible only for the previously selected full V1 UID.
Every connection is serial-pinned and reads the actual 12-byte MCU UID from
0x0BFA0700, comparing three little-endian words with that 24-digit selected UID.
The 12-character DFU serial is never treated as the full MCU UID. Unexpected
flash size, unrecognized CLI enumeration, unreadable UID and identity changes
stop the operation without substituting another USB device.

Before programming, a full reported 2 or 4 MiB physical flash backup supplies omitted bytes on touched 8 KiB
pages. Only image-touched pages whose complete expected contents differ from that
fresh backup are emitted; matching recovery and spare pages are left untouched.
An identical image creates no operation HEX and sends no programming command.
It still requires a fresh full-flash readback, exact UID checks, same-UID DFU exit
and verified idle reconnection. The configuration erase page is never emitted. APP DFU preserves resident
services/recovery and updates its manifest generation/header CRC for the known
ABI258 loader. The UID is checked again immediately before download. CLI writes
only the normalized addressed HEX, with byte verification; no mass erase,
unprotect, option-byte or TrustZone regression command exists in the flow.
The post-write UID and complete physical flash readback must match the intended bytes,
including configuration and every omitted byte. Only then is DFU exited, and
completion requires that exact UID to return with a verified idle V1 status.

Request ROM DFU… remains a separate request-only action. Main-window recording,
passive info, enumeration and auto-recovery cannot overlap the modal management
operation. Failures stop without automatic retry, erase fallback or success claim.
Logs and before/after readbacks are retained per operation under the user's local
USB_PCconsole/FirmwareOperations folder.

Full fused programming can rewrite recovery and is not atomic or implant-field-update
qualified. Power loss can require physical recovery; the GUI explicitly confirms
this risk. Software/fake CLI validation does not establish ROM UID readability,
erase behavior or DFU exit/reconnection on real hardware. No real DFU request,
flash, device reset, install or push is part of the software candidate validation.

App-only installer upgrades archive historical V1 delivery files only when their
literal filename, length and SHA-256 match the retained-installer ownership catalog.
This includes the obsolete bundled README and manifest. Recognized files move to
`legacy/firmware/V1-installer-owned-<UTC>-<GUID>` under the installation directory,
with byte checks and a recovery receipt. Unknown or modified files and other-device
bundles are preserved. The v1.0.7 pair lacks retained-installer ownership evidence
and is deliberately not moved. Hash/preflight errors stop the upgrade before app
replacement. App-only upgrades install no firmware or active firmware selection;
Flash firmware… still requires explicit selection of a validated external HEX.
