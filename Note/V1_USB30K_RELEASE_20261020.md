# V1 USB30k startup release — 2026-10-20

Release: `V1_USB30K_USBF2D80C4F_APP654824DE_20261020`.
Publication target: existing WILDX `main`. No firmware/console rebuild or
additional hardware operation occurred during this packaging request.

The exact tested USB image is promoted without resealing. It adds30k-only,
provision-gated PA8 low-voltage drive and moves USB30k run-register verification
before capture DMA. Two5s P1/native30k starts,60.0034s P1/native30k, and a5s
P12/native20k control passed. Zero frame/counter/LFP-phase/relock/USB-sequence
errors;60s check:23,403,541 frames and11,965 IMU samples without gaps/errors.
This is bounded bench validation with default map and preview off, not laptop,
custom-map, other-probe or endurance qualification. Individual causal effects
of the two startup changes were not isolated.

## Frozen delivery

- USB service SHA256: `141B8E99DE4FC13E955C601008F9E7F5869EBEFFF62F15A259CB7717AC7057A5`.
- DFU/boot+USB SHA256: `BE9122F07CDA4169A7E8C49B0D40A6B033FB4648D362A5F3A299DE752C8C7E3B`.
- Fused SHA256: `0B38F0ECF60A7D9B99DEF8C6DE3C430063627636ACD770D0579A3EC9BE78F995`.
- Unchanged public APP654824DE SHA256: `26C36C71A58CA7C05E7FBDDD68348C844C8603465D4123A77047FAB38A6B32AF`.
- Installer: `WILDX_console-Setup-1.0.0.51-V1-USB30k-USBF2D80C4F.exe`.
  SHA256: `84A5D828F5AE54E6AE23A639A5ED63B637941E1AD848C82DBEAFE8933F4952E5`.

Fused assembly uses the current verified boot+USB input with only its USB
component replaced, and the exact current verified APP. Direct merge checks
partition ownership/overlap, manifests, entry vectors and flash/stack bounds.
Retained configuration is excluded; fused HEX is LF-normalized. Existing
3016/4096-byte known stack evidence and linker uncertainty are reused.
One installer pass;41 extracted file hashes match; actual WILDX loader accepts
catalog identities and full fused coverage. GUI51 SHA40570080…FA5C72 and32
other payload files are unchanged, including all V2/camera binaries.

## Preserved paths and limitations

Boot and SD/BLE APP are byte-identical to the current release. Download,
SD initialization, UHS200mA allowance, fallback, MSP/HAL and transport objects
are among73 unchanged linked objects. Prior F8 download evidence is inherited:
42.57MB/s known1GiB read and forced SDR50 fallback. These speeds were not newly
measured on F2.200mA is negotiated allowance, not a measured current clamp.
The public APP/USB pair was not newly installed together; the bench private
APP60EC609E was preserved, and the entire flash/options were restored after
capture tests. No new BLE/SD recording, camera, power or card-removal check.

The separate SD-reset report on another unit is not claimed fixed here.
GUI59 preview work and source-only directory retry are excluded. Exact source
delta and source/build/validation identities are included in `Firmware/V1`;
unrelated maintained-source edits remain untouched.

Evidence: `_artifacts/v1_usb30k_recheck_20261020/hil_r1/acceptance.json`,
`release-acceptance.json`, and `release51/package-verification.json`.
