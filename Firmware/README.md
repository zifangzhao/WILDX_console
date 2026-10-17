# Bundled firmware

`releases.json` selects the firmware shipped with WILD-X. The application checks
every listed SHA-256 before offering a package. Installed identities and bundled
versions are displayed separately. Merely connecting never flashes a device.

Both firmware owners reconfirmed these as the latest approved distribution
packages on 2026-10-17. Their development limits remain in effect.

| Family | Published components | Artifact publication commit |
| --- | --- | --- |
| V1 old NP / 91k+91k target | APP1.0.121-idle-sleep-test / BC6B506E; USB1.0.91-usb20-paired-restore-test / 40D39075; matching fused recovery/APP/USB HEX | e831a5f10c28c1df596766b195d4a48aeb38991d |
| V2 NP2 development | APP136172A5; USB2E07799E; recovery6408DBB8 | e7b0d08cc3d24dc0776532d2a599462528301df7 |

The original manifests, READMEs and filenames remain under each family folder.
Publication commits identify the distributed artifacts; they do not establish
a clean, complete source snapshot of every incremental firmware build input.
The V2 source validation note and checksum list are included unchanged.
The separate published V1 camera40 candidate is under `V1Camera/`, with its own
manifest and hashes. It requires explicit manual target selection and is never
substituted for the Neuropixels APP121 / USB91 bundle.
V1 camera firmware is a separate target and is not an automatic predecessor of
the old-NP package, even when both filenames contain `1.0.121`. V2 has no enabled
target ID in this catalog until its firmware owner freezes that identity.

Use **Use bundled APP…** in Firmware Update for the selected exact MCU UID.
V1 uses the protected APP-only HEX path; the current USB91 requires entering
Recovery first. V2 uses its protected APP `.xenp` updater. APP update preserves
resident USB/recovery and settings. Service-only, recovery and fused HEX files
are included for their documented separate programming workflows; they must
never be sent through the APP updater.

The V1 fused image excludes configuration, includes and reprograms recovery,
and requires SWD without mass erase. Its components match the standalone APP
and USB service; fused programming is not newly hardware-qualified. Git text
normalization is disabled for firmware payloads to retain published file hashes.

Current published firmware does not expose exact installed image identities.
It displays **Not reported** and can show a one-time package-review notice per
device/release/application session. This is not an assertion that its firmware
is older. The optional NPF1 reader is gated by advertised capabilities; firmware
implementation and common allocation review remain separate. No unknown BLE
opcode is sent to old devices.

Automatic “update available” classification requires an explicitly known target,
verified code identity and an exact published predecessor descriptor: image ID,
base, length, code CRC, raw build ID, flags and minimum loader ABI. ABI, static USB
`bcdDevice`, legacy BLE version tags, filenames and installation generation do
not establish release ordering. Unknown/private builds remain review-only.

For a later bundle refresh, copy exact owner-approved published artifacts,
preserve their limitations/source commits, update hashes and descriptor tuples,
retain only reviewed predecessor identities, then run `test_firmware_bundle.ps1`.
The project copies this folder into the output, and NSIS includes it in future
installers. This change does not publish an installer or flash a device.
