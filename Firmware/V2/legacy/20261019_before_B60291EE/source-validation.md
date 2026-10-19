# V2 APP136172A5 firmware-only publication — 2026-10-16

The user explicitly requested publishing this development firmware while
WILD-X console remains separate. This publishes the already packaged1361
image, not the newer untested single7500/SD54 optimization candidate.
No development/build/repackaging/hardware test was added to publication.

Fused components: recovery6408DBB8, published USB2E07799E (flags3/ABI4),
APP136172A5. Fused SHA256:
`9EAA0D82519961C2D9B865672DBF00B3E17DC4E518D76FF4F773E3975033D34B`.
All non-APP address-bytes equal the preceding package, settings omitted,
and the independently checked APP seal is preserved. Artifacts are in
`LATEST_FIRMWARE/V2_NP2`; previous915AE345 package is recoverable in legacy.
Console source, installer and its release metadata were not changed.

The APP changes only stopped-startup SD clock-gating policy. Singles and all
7.5kHz modes retain PWRSAV; dual15/20/30 and triple15/20 use continuous clock.
CPU/PSRAM/MCO/SD frequencies, -O3/cache, shallow WFI, unused-probe shutdown,
data formats and integrity checks are retained. Deep STOP remains disabled.

Actual exact-APP bounded10s capture plus native firmware verification:

| Configuration | Produced = verified blocks | Raw retention use incl. unpublished | RAM peak | Mean VBUS mA |
| --- | ---: | ---: | ---: | ---: |
| 2 probes /15kHz | 3904 | 31.8% | 5/9 | 95.280 |
| 2 probes /20kHz | 5208 | 31.8% | 5/9 | 102.05 |
| 3 probes /15kHz | 5856 | 36.4% | 6/9 | 124.95 |

Meter windows are5s interiors, not whole-session means or controlled saving
claims. Triple15 consumed more than the preceding gated sample; its power
setting remains an individual optimization target. BLE rescue from a stopped
source fault passed same-MCU reconnect/Idle/error0 and configuration reset.
Final map-bound read-only guard found exact APP/card/UID, all16 DMA and
capture/SD sources quiet. Settled post-rescue idle was about13.5mA at4.97V.

Limits remain explicit: exact candidate dual30 failed at324 committed/364
produced blocks after478ms, source5/detail1, actual PWRSAV off. Candidate
triple20 was not run; the same-card baseline triple20 also failed. All-mode
lossless/endurance/full stored-DAT/analog qualification is incomplete. APP
hardware tests used resident USB5AD122A7; the packaged published USB2E07799E
has no new fused cold-boot/pair test. This is a development publication,
not a newly qualified firmware/GUI pair. See `package-receipt.json`.

Next authorized work is independent: optimize known-good configurations one
at a time, starting with1 probe/7.5kHz. Retain adequate processing/buffer
headroom and verify stored recording integrity before accepting a saving.
