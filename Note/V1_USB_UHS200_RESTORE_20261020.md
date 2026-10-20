# V1 USB SD download restored with200mA setting —2026-10-20

Selected USB **F8E721C2**, version1.0.102-uhs-retained200. Matching fused image
combines the exact checked USB with unchanged current boot and APP654824DE.
WILDX runtime remains1.0.0.51; this is a firmware-only installer refresh.

## Cause and correction

ACMD41S18A0 was treated as proof that1.8V signaling was unavailable. That is
incorrect for a card already using1.8V: CMD0 can retain its voltage and access
function, and later ACMD41 reports S18A0 to keep that voltage. This card returns
fresh CMD6 group1 support001F. SDspec Table4-11 requires support bits2..4 to be
zero when initialized at3.3V; unlike SSR performance fields, this current-session
response provides protocol evidence for the retained1.8V case.

The USB-only recovery requires a complete64-byte native-CRC/error-checked CMD6
query, SDHC/SDXC/transceiver eligibility and an SDR access function0..3. It
lowers group4 tofunction0 before aligning the host selector, verifies actual
selection and unchanged access, compares a512-byte sector before/after, repeats
the query and requires native TRANSFER. Failed evidence rejects initialization.
No S18A/CMD11 success is fabricated. DDR is not admitted. The first speed
candidate retains this checked initialization rather than needlessly resetting.

The original candidate EB2A192D passed two1GiB hashes at45.48/45.60MB/s but
rejected a later retained SDR104 access function3. It is not selected/published.
The final guard includes SDR50/104, with the same evidence checks; this fixes
that reproduced warm-reset failure. Genuine3.3V and native CMD11 paths retain
their existing behavior. APP-disabled preprocessing is unchanged.

## Actual selected-image checks

Exact ST-Link3439 / UID001D00065942500B20313354, cardCID
1B534D4A453259373010B35487A1A300,500695040sectors. Private benchAPP60EC609E was
preserved, not replaced by bundledAPP654824DE. No SD payload/catalog writes,
FORMAT, DELETE, capture, BLE recording or V2 operation was performed.

| Check | Result |
| --- | --- |
| Normal reset then84MiB exact reference +1GiB known hash |1292ms reset;41.96MB/s reference;42.5733MB/s1GiB;PASS |
| SWD fault injection: reject all12SDR104 attempts |16939ms;actual SDR50/30MHz fallback;84MiB byte-identical at14.4586MB/s |
| Normal reset after fallback +reference +1GiB |1298ms reset;41.37MB/s reference;42.5729MB/s1GiB;PASS |
| Catalog checkpoint |epoch000D47F00A459C8D,count31,frontier63471616 unchanged throughout |
| Native/host transfer health |zero SD/USB errors,recovered reads or retry attempts |
| Final passive state |SDR104/120MHz,VSELhigh,ready/idle downloadrole3,error0,retained diagnostic16/16successful |

Both selected-image1GiB SHA256 values are
8EE3854B452C02DA1DC9CE2D2760F5066CE36B824CD9E71EB82C7AD6BC3C301F.
Actual frozen WILDX51 production management/download APIs were used, not a
replacement USB transport. Earlier prototype tests ran during another chat's
offline video batch; the final selected-image checks ran after that batch ended.

Software:196actual tier assertions; complete actual CMD6 helper/mode/power/FIFO,
deadline/error and support-mask mutation tests; actual retained-recovery state,
power-before-selector, access-preservation, sector/status and fail-closed tests;
actual ACMD41 argument contract. First test fixture compilation failed only on
a global-name shadow warning; corrected fixture passes. One USB-main incremental
build per candidate; exact unchanged HAL/MSP/transport objects reused. Linker
known maximum stack3016/4096bytes, with existing cycles/untraceable-pointer
uncertainty retained. Exact installed USB-page flash model and unchanged APP
manifest were checked. Final image is left on the connected V1, idle role3.

## Package and scope

Direct merge only: `Tools/V1Capture/UnifiedUsb/merge_verified_hex.py`, current
verified DFU boot bytes +checked USB +current verified APP. Configuration is
excluded; full firmware coverage/manifests/vectors/flash/stack bounds pass.
No component rebuild or hardware qualification during packaging. One NSIS pass;
all40 extracted payload hashes checked,32 runtime/V2/camera files unchanged.
The actual product catalog/fused loader accepted the result.

- Fused SHA256:61A8F66F7E5CDF9CD8C45881C17328C09DA3B65AFBCD599220865C3CD64C8D76
- USB-only SHA256:BFF2398ED5CC25733DCEA100AE597CFA7AA143FA8463CEB79F632B6FD3B464D9
- Installer SHA256:BF5B6797F75BE7BF132090686DE40897499E8935473A346F7C6A76A0310ABCAB
- Installer:`WILDX_console-Setup-1.0.0.51-V1-UHS200-USBF8E721C2.exe`
- Fused:`V1_fused_BOOT_USBF8E721C2_APP654824DE_uhs200_20261020.hex`

The200mA setting is the lowest card allowance (group4/function0), not a measured
instantaneous current clamp or rail-droop qualification. Physical discharge/
back-power cause is not established. Other cards, genuine cold CMD11 electrical
switching, SD/BLE recording, streaming, camera, endurance and the exact bundled
APP/USB installed pair are not newly qualified; unchanged components reuse
their existing evidence. No claim that all cards can achieve this rate.

Evidence:`_artifacts/v1_usb_uhs200_restore_20261020/acceptance.json`, selected
`retained-r2`, production receipts in`retained`, delivery and`release51`.

Primary protocol reference:[SD Association Physical Layer7.10,§4.2.3.1 and
Table4-11](https://www.sdcard.org/cms/wp-content/themes/sdcard-org/dl.php?f=Part1_Physical_Layer_Simplified_Specification_Ver7.10.pdf).
