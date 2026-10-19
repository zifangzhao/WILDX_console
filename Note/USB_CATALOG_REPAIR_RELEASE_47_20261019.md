# USB catalog repair release — 2026-10-19

GUI1.0.0.47 + V2 USB E323A06A + unchanged APPB60291EE/recovery6408DBB8.
Both USB Mode and Data Download expose **Repair SD catalog…**. Fresh device/
card inspection and confirmation are required. No format, rewind, payload
repair, recording restart or command replay is performed. APP-only updates
cannot add this USB-service command; matching component and fused HEX supplied.

Actual-source helper/EP0-boundary regression:105 checks pass. Actual compiled
repair-button/ownership regression:11 checks pass. Firmware actual adapter/
catalog and EP0/HAL/LL native tests pass; these model USB/SD boundaries. No new
hardware flash, installed-pair, physical repair, recording or endurance test.
V1 capability0x20 is distinct from V2's0x10; old V1 ROM-DFU bit0x10 cannot
cause catalog traffic. V1 repair backend remains separate/not enabled.

One successful cached GUI compile follows an initial stale-exporter failure;
automatic versions46/47 retain that history. The exact already-published
exporter A396530AAD81A5C8BA6FDC8086FC1FB2642A2E4E34C52E50392AE8763FE32A2F
is reused. Configurator, updater and other dependencies were not rebuilt.
GUI47 SHA256:1CEBD4CB651A201B5D80680ABF9AB93FDD2017BE084D4A453C3C969F0B77B2E5.

Firmware V2 release commit1a73610f87794c58c6c50fe942827a3086caa4fb.
Firmware and APP capture/writer/clock limits remain inherited; this does not
qualify all recording modes or claim a power improvement. No incidental device
tests or unrelated source integration accompany installer publication.

Public source publication uses a49-line bounded overlay and the new helper/
tests. Private pre-existing Format/Recover/export changes remain unstaged.
The shipped GUI includes that pre-existing context, so clean-public-HEAD binary
equivalence is not claimed. The compiled UI fixture exercises the shipped
GUI's existing Recover/Format fields, not a fresh clean-HEAD application.
The package's inherited top-level firmware README retains its previous text;
the bundled V2 README and firmware catalog identify the actual new components.

Removable package/evidence: `_artifacts/release_USB_catalog_repair_20261019`;
shared-helper evidence: `_artifacts/v1_v2_usb_catalog_parity_20261019/helper-final`.
