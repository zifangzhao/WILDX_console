# V1 fast SD download — USB037CF67C / WILDX48

## Result and delivery

USB1.0.95-fast-ready restores the exact checked fast USB initializer and retains
the hardened FIFO/CRC reader. The previous USB22D0792B APP-initialization refresh
measured36.4–36.8MB/s on this card. This candidate measured:

| Check | Actual result |
| --- | --- |
|84MiB raw, exact reference comparison|42.20MB/s,2.087s|
|First independent1GiB read|42.90MB/s,25.029s|
|Second independent1GiB read|43.16MB/s,24.879s|
|Full production P12/20k export|35.01MB/s;21 identical data files|
|Transport/firmware errors and retries|0|
|SD/BLE return|idle0,fault0,P12/15k;31 catalog entries,frontier63471616|

The earlier44.00MB/s was a short raw read, not decoded export.43MB/s sustained
is close to that result and approximately17% faster than the preceding36.8MB/s
release. Export is approximately7% faster than32.78MB/s. No claim above44MB/s.

Two1GiB hashes were identical:
`209CE1222C583003553EF2641DBE79F9F0CD17689BD96AAC187F9528C3EDB954`.
Export settings differ only in software version and metadata creation time.
The focused actual-reader native regression passed exact length/DATAEND/CRC,
late data faults, bounded CMD12/CMD13, delayed/stuck/wrapped DAT0 retirement,
sticky failure and no replay. These checks were not removed to obtain speed.

## What caused the speed difference, and what remains unresolved

The direct binary split isolated the slowdown to the rebuilt initialization
unit: the identical guarded reader is fast with the frozen checked initializer
and slow with the rebuilt one. This is not USB bandwidth, decoder throughput,
or the added read-retirement checks. Separate timing found virtually all extra
time in FIFO/data transfer, not command response or initial FIFO latency.

Restoring FIFO calls, direct FIFO/tick access, HSLV,93.6/120MHz clock policies,
auxiliary-clock candidates and several reset-sequence arrangements did not
explain the regression. In particular, the mutable archived main source also
rebuilt slowly. Therefore a power-sequence-only root cause is NOT established;
intermediate working hypotheses were rejected. The exact checked main/MSP
objects are reused, with identities in the release manifest, rather than
claiming source equivalence. The rejected experiments are not in this release.
The precise compiled-source/configuration discrepancy remains an audit item.

## Frozen inputs and measured scope

Evidence root: `_artifacts/v1_sd_download_fast_20261019_r16`.
Exact tested V1: ST-Link3439,UID001D00065942500B20313354; card500695040 sectors,
CID1B534D4A453259373010B35487A1A300. USB037CF67C service SHA256:
`4FAB88D3537C95D92FD6F53BA9975D4FCFB7C26AC0A0B6CE71B444D3A717FA4E`.
All service-only installs were checked by two independent complete2MiB flash
readbacks against page-erase models. Boot, APP and retained configuration were
preserved. No SD writes, new recording, formatting, deletion or V2 access.

Local HIL preserved private APP60EC609E. The release bundles unchanged verified
APP654824DE and current verified boot. The exact bundled APP/USB pair was not
newly device-tested. No new streaming/camera/capture/endurance/card-removal
qualification is claimed. BLE discovery required a second uncached service
attempt, then fresh status and catalog passed. Other-PC11.2MB/s and0xFFFFFFFE
remain unreplicated and remotely unqualified.

Packaging directly merges current verified boot/DFU, exact checked USB037CF67C
and APP654824DE; retained configuration is excluded. The complete fused image
and component HEX files ship together. WILDX1.0.0.48 executable/runtime are
unchanged, SHA256CB392725EE11C8A607D6FECAA013ADC6383BA419C4FAA1046CBB838A366B5486.
V2 and separate camera firmware stay unchanged. Installer packaging/extracted
hash and actual frozen catalog/fused-loader checks are recorded separately;
no new installed-GUI pair qualification or private host-source integration.

Publication identity is recorded in the artifact publication receipt.
