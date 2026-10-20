# V1 SD download — USB6CD1917C /1.0.96-sd-dma

## Cause and fix

The service builder copied the earlier receipt's `usb_defines`, but the original
bench supplied `XE_NP_BOARD_USB_OTG_HS_INTERNAL_DMA=1U` separately to **main**.
Recompiling main without that flag silently set `hpcd.Init.dma_enable=0`.
USB then used CPU-driven FIFO transfers, competing with the polled SD reader.
This explains why importing APP SD initialization appeared to reduce speed even
though the CRC and card-retirement guards were not the bottleneck.

Linked evidence: old main wrote1 at hpcd+6; rebuilt main wrote0. The same linked
USB_CoreInit enables GAHBCFG DMAEN/INCR4 only when that field is1. Neither build
used a SINGLE-burst override. The fixed builder records and explicitly supplies
the main-only DMA flag. Live V1 confirmed dma_enable1 and GAHBCFG0x27.

The maintained APP's31 SD initialization functions,120MHz SD kernel, power/reset,
and tuning are retained. Fresh generation reproduced the tested main/MSP source
bytes exactly. No experimental clock/pad/power-sequence change is shipped.
All current CRC/exact-length/DATAEND, bounded CMD12/CMD13/DAT0 retirement,
sticky-error and no-replay checks remain. The actual-reader native regression
passed; the unchanged reader objects and evidence were reused.

The interim USB037CF67C release restored speed by reusing the exact previously
checked main object. This release resolves that rebuild discrepancy and replaces
the interim fallback with the maintained initialization plus corrected DMA flag.

## Connected-device evidence

| Check | Result |
| --- | --- |
|84MiB exact raw-reference comparison|42.35MB/s;2.080s|
|SD portion of84MiB read|1.708s; USB portion1.843s, pipelined|
|First independent1GiB read|43.11MB/s;24.908s|
|Second independent1GiB read|43.42MB/s;24.729s|
|Full WILDX48 P12/20k export|33.69MB/s;21 identical data files|
|Read errors/retries/integrity failures|0|
|Return to SD/BLE|idle0/fault0,P12/15k;31 catalog entries/frontier63471616|

Two1GiB SHA256s matched:
`209CE1222C583003553EF2641DBE79F9F0CD17689BD96AAC187F9528C3EDB954`.
Export settings differed only in software version and metadata creation time.
Earlier44MB/s was a short raw read, not decoded export. Sustained43.1–43.4MB/s
restores near that rate, approximately18% above the preceding36.8MB/s APP-init
release. No claim above44MB/s or of a faster decoder. Interim export35.01MB/s
was slightly higher than this33.69MB/s result; no uniform export improvement
over that interim build is claimed.

Evidence: `_artifacts/v1_sd_download_dma_20261019_r17` acceptance, flash models,
production API receipts, byte-exact source regeneration, build and live-register
receipts. Exact V1 ST-Link3439 /UID001D00065942500B20313354; same500695040-sector
card. Two independent2MiB flash readbacks matched the service-only erase model;
boot/APP/configuration were preserved. No SD writes/format/delete/new recording.

## Release and limits

Latest V1 contains exact checked USB6CD1917C, unchanged verified APP654824DE,
current boot, matching fused HEX and all component HEX files. Fusion is a direct
verified-input merge; retained configuration excluded; complete-image acceptance
checked with the actual frozen WILDX48 loader. WILDX1.0.0.48 runtime remains
CB392725EE11C8A607D6FECAA013ADC6383BA419C4FAA1046CBB838A366B5486. V2 and separate
camera target remain unchanged. Installer extracted-file identities are checked
separately, with no new installed-GUI pair qualification.

Local hardware tests preserved private APP60EC609E. The bundled unchanged
APP654824DE exact pair was not newly device-tested. No fresh streaming, camera,
recording, endurance or physical card-removal qualification. Remote11.2MB/s and
0xFFFFFFFE cases are unreplicated and not remotely qualified. The composed
sources/reused matching objects are hashed; no clean-public-HEAD whole-tree
reconstruction claim. Publication identity is in the artifact receipt.
