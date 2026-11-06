# V2 Delete Last recovery validation

Status: completed with recovery failures; original firmware/settings restored.
The selected V2 implementation does not meet the V1 crash-safe recovery standard.

| Check | Result |
|---|---|
| Normal USB Delete Last | Passed; only the identified new tail removed. |
| USB valid-journal interruption, VEE hold plus reset | Automatic replay failed; explicit guarded repair passed. |
| Torn metadata persistence model, shared by USB/BLE | Failed availability in 3,066 of 6,156 deterministic cut cases; earlier sentinels unchanged. These counts are not real-world failure probabilities. |
| Normal BLE Delete / BLE delete interruption | Not qualified: read-only admission timed out twice before any delete; interruption test not attempted. |
| Earlier recordings | All 22 original entry identities and bounded fingerprints matched after repair. |
| Saved NP/IMU and post-recovery append | Passed within the documented sensor startup and timing limits below. |

Selected production pair: APP 99CC44F9 / USB EDEB6C3A, with unchanged host 1.0.0.246.
Both entry points use the same FE15 catalog implementation: USB A7 with its
confirmed NDU1 snapshot, and BLE B9/version 2 operation 11 with the catalog token
and exact target fields. BLE is not using legacy opcode 0x95 here.

## Native persistence result

The actual catalog DeleteLast/Mount/Recover code and included shared CE64
header were exercised using the existing sparse-card model. Counts 1, 3 and 64
cover empty-after-delete, multiple entries and a directory-page boundary.

- 6,156 interruption cases: four metadata writes, every persisted prefix from
  0 through 512 bytes, and three directory shapes.
- 2,510,812 checks completed. Earlier descriptors, footers, payload sentinels
  and prior directory pages remained unchanged in every case.
- 3,090 cases mounted/recovered and allowed a subsequent append.
- 3,066 cases blocked both Mount and Recover without further writes. These
  are partial journal publication or partial final clearing, with a nonzero
  invalid journal at sector 112.

Publication writes shadow 113, journal 112, the target directory sector, then
clears 112. A valid journal can replay its CRC-checked shadow. An invalid
nonzero journal fails closed; there is no redundant intent that authorizes
automatic repair of those partial-publication/clear states. This preserves
earlier bytes in the model but can make the catalog unavailable after a crash.
It is a recovery-availability failure, not evidence that earlier payload was
overwritten.

This differs from the V1 release: V1 persists redundant redo intents before
updating its allocation checkpoints. V2 currently has one journal sector and
one shadow page. A readable shadow alone cannot safely authorize recovery of
an invalid journal. Normal Delete Last success therefore does not establish
the same interruption-recovery guarantee for the two families.

Exact source pins, test code and results are retained in
`C:/Frank/Code/USB_PCconsole/_artifacts/v2_delete_last_native_20261105`.
Compact source-evidence.zip SHA256:
`E2885121664207F4C4A39A47B1FD850A4D0388B6DC979F131DA16CBF071C7A76`.
The callback model does not establish actual SD-controller atomicity.

## Hardware scope

Only newly created, identified test sessions were targeted for deletion. Fresh
card fingerprints preceded mutations; earlier recordings and settings were protected.
The baseline contains 22 earlier recordings on UID 001A0004434250062039314E.
Descriptor, header, footer and bounded payload fingerprints were captured
before creating the new test recording. This is not a full reread of all
earlier user payloads.

USB deletion, failed BLE admission, interruption at a durable USB journal
boundary, post-recovery NP/IMU readback, and original restoration were tracked
by the exclusive V2 hardware owner under
`C:/Frank/Code/XE_NP/_artifacts/v2_delete_last_20261105`.

The first new saved session, 3750497822, passed production host 246 export
at 20 kHz, three probes, closed-loop off: 8,480 NP blocks with no reported
gaps, drops or overflow; exact three DAT extents of 217,087 rows each;
13,567 valid 1,250 Hz IMU grid rows; 2,126 CRC-valid native observations.
Native accelerometer/gyro and magnetometer sequence rates were approximately
196.1 Hz and 97.9 Hz. Grid rows are cached scheduled snapshots, not 1,250 Hz
physical sensor conversions. Unknown terminal slot/native conversion phase
remain explicit; this is not a new sub-millisecond timing qualification.

Readback receipt:
`C:/Frank/Code/USB_PCconsole/_artifacts/v2_delete_last_native_20261105/exports/normal-usb-record-quality.json`
SHA256: `4B27A0A06C2F199CC430C2CEC0A644B326FC01D43BD1072FAA3BF8F2AF6A7810`.

Normal USB Delete Last passed: the acknowledged operation removed only owned
session 3750497822, returning the catalog from 23 to 22 entries and frontier
36,462,592. All 22 earlier descriptor, header, footer and bounded payload
fingerprints matched, and journal 112 was cleared. The confirmed 128-byte
request, final response and preservation receipt are retained in
`C:/Frank/Code/XE_NP/_artifacts/v2_delete_last_20261105/normal-usb-delete`.

The next BLE-controlled saved recording passed production export continuity:
8,592 NP blocks with no reported loss; 13,746 valid scheduled IMU rows and no
reported queue/scheduler loss. Its first native sensor observation lacks MAG;
the remaining 2,159 observations have valid AG/MAG data and no valid MAG
sequence skips. This startup limitation is retained as
`PASS_WITH_STARTUP_MAG_LIMIT`, not an unconditional sensor-start pass:
`C:/Frank/Code/USB_PCconsole/_artifacts/v2_delete_last_native_20261105/exports/normal-ble-record-quality-startup-limit.json`
SHA256: `B2C48B926C6AF5FBBF0670C84A8B5BB34F56FD422A6B41D2E448BABBE0297630`.

Two initial BLE Delete admission attempts timed out in read-only device
discovery/status before any catalog mutation was delivered. Both followed
immediate APP mode transitions, so readiness remains a possible confound.
The verified new tail was left intact for the USB interruption case. These failures are not deletion
acknowledgements, nor evidence of a successful BLE delete. Further retries
were stopped. Normal BLE Delete and its hardware interruption test remain
unqualified; no BLE delete command was delivered.

## USB interrupted-delete observation

Only owned session 428460056 was targeted. The exact firmware write boundary
was captured after writing shadow 113 and valid journal 112, before directory
128. With the CPU halted, PF2/VEE was commanded and read back low for five
seconds, then the MCU was reset. The interrupted command ended with unknown
outcome and was not replayed.

Before any explicit Recover command after reset:

- Raw journal 112 remained valid, nonzero and identical to the captured own
  intent.
- Shadow 113 contained count 22; physical directory 128 still contained
  count 23 and owned session 428460056.
- Production `ReadAvailable` returned 24 browsing rows. That inconsistent
  view is retained as evidence and is not accepted as physical replay proof.

Automatic completion did not occur in this measured case. Fresh production
A1 inspection confirmed pending flag `0x00800000`. After exact own-intent
matching, the actual host 246 guarded A2 Repair completed with error 0,
flags 0 and frontier 36,462,592. This is successful explicit repair, not an
automatic-recovery pass. All 22 earlier entry identities and bounded hashes
matched the original baseline after this explicit repair. The post-recovery
recording also passed: owned session 1632733015, 8,544 NP blocks with no
reported loss, exact three AP file extents, 13,664 valid scheduled IMU rows,
2,148 valid native sensor observations and no reported queue/scheduler loss.
Native AG/MAG sequence rates were approximately 196.6/98.1 Hz. Terminal slot
and physical conversion phase remain unknown as in the initial readback.
The boundary and repair evidence is under
`C:/Frank/Code/XE_NP/_artifacts/v2_delete_last_20261105/cut-usb`.

Post-recovery readback receipt:
`C:/Frank/Code/USB_PCconsole/_artifacts/v2_delete_last_native_20261105/exports/post-recovery-record-quality.json`
SHA256: `A88307AC151804BA3145DED55B4AF8CCF8388953E6151668E1CEE3628924D8D6`.

The V2 circuit can back-power storage through its 1.8 V path. PF2/VEE enable
readback alone does not prove loss of storage power. A commanded enable hold
plus MCU reset is a reset-recovery test; genuine power-loss qualification
requires independent rail evidence or removal of all power sources. The
user selected "Use VEE plus MCU reset only for now". Hardware interruption
results are therefore limited to that reset/enable simulation; no electrical
0 V or whole-device power-cut qualification is claimed.

The checked published pair was installed temporarily and the original pair
restored. No firmware source edits, installer packaging or publication are part
of these results. Native failures remain explicit even if full-write-boundary
hardware recovery succeeds.

## Restoration and closeout

The final new test session was removed through production USB A7. The physical
directory contains the original 22 entries, journal 112 is zero, and all
original entry identities/header/footer/bounded payload hashes match.

Original APP 7F238587 / USB 74FC7104, protected settings and options were
restored and verified. Whole-flash SHA256:
`8A0214C598D3E5B93504B16CF2395D8C52F61BE4F27A4AC0967D59D4BE7AB1C8`.
Options SHA256:
`BB193EEA36446A7B27DA8170120C52F563BC306E860139A757089FC129C1BD3F`.
The device is USB mode 2, idle, SD/DMA quiet, debugger disabled; originally
absent host preferences are absent again. GLOBAL and exact-UID leases were
released at 2026-11-06 02:29:00 UTC. No user recordings were deleted or card
format performed.

Receipts under `C:/Frank/Code/XE_NP/_artifacts/v2_delete_last_20261105`:

- `final-preservation.json` SHA256:
  `E7944DB459D9BE93DB09CC5FB7664463A24D8E16529CC89114BEE43BFDE7C6D6`.
- `restoration-complete.json` SHA256:
  `1F0FA0854F6F8E1C228D1010B758F1B4CE3824E703AF003930AB87704C6023AC`.
- `owner/lease.json` SHA256:
  `03C5AD30DE69A5F32446A71DF6D758B38D50D7BD76E5B6094557725058D3C41A`.

The V2 developer received the confirmed failures and owns a separate firmware
repair. These results qualify no replacement image from that follow-on work.

Final machine-readable `qualification.json` status is
`QUALIFICATION_FAILED_CURRENT_PUBLISHED_PAIR`, SHA256:
`6D8B34E3839CC741B9CFBAF2BC58A71B31975FA28191A9FAE2A78E845F52DD05`.
Generated AP payloads totaling 1,510,886,400 bytes were removed after bounded
evidence was secured. Restore backups and compact evidence remain; no full
generated neural DAT remains. `cleanup.json` SHA256:
`E5C26C877ACDE258373527B3C000B9E892D136FB01B27CAD214FC9B2785260F7`.
