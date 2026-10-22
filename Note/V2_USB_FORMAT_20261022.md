# V2 USB card formatting

Release: WILD-X1.0.0.91 with V2 USB B6099A0E, APP660A6DA4 and recovery6408DBB8.
No connected card has been formatted, and no firmware has been flashed for
this change. New USB firmware qualification is software-only.

The released V2 USB service E323A06A has catalog inspection/repair capability
0x10, but no USB format command. The Data Download form also admitted only V1
USB loggers for Format. A missing source could leave an old recording list
visible, and changing the serial prefix on re-enumeration lost the selection.

## Console workflow

- Format is available for an explicitly selected V1/V2 USB logger or removable
  SD reader. BLE/capture/download ownership continues to block USB maintenance.
- V2 checks NPS2 ABI4, the selected UID, idle download mode, no pending operation,
  and the new format capability 0x20 before requesting format information.
  Catalog repair capability 0x10 does not enable the protocol.
- Older firmware offers the bundled full APP + USB + boot update. This selects
  the full-update option; it never flashes or formats automatically. After an
  update, the user must press Format and confirm the selected card again.
- Confirmation names the MCU UID, full SD CID and capacity, warns that all
  recordings become unavailable, and defaults to No. Format initializes the
  native recording catalog; it is not FAT/exFAT formatting or a secure erase.
- The confirmed receipt/card is checked again before one format OUT. Success
  requires the matching transaction, unchanged UID/CID/capacity, completed
  state, zero error, flags3 and empty nextLBA8192. Lost acknowledgement, lost
  receipt and mismatched completion do not resend Format.
- Re-enumeration follows one matching V2 UID across bare/npv2 serial prefixes
  and interface GUID changes. A different UID or ambiguous interface is never
  substituted. Removing/changing the selected source clears its stale list
  and progress; source-less actions are disabled.
- V1 uses its existing guarded format implementation and the same confirmation
  text. Read-only downloads and metadata-only repair remain separate actions.

## Firmware protocol agreement

The V2 developer confirmed the V1-compatible NFS1/NSF1 framing, query C0/79/64B
and format40/7A/64B, with explicit NPS2 capability0x20. The request binds full
UID, full CID, capacity, fresh transaction, destructive key and CRC. This
agreement does not assert that E323A06A implements it.

The V2 routing contract distinguishes IN79/64B format information from the
existing IN79/256B self-test. OUT7A/64B format is admitted only in download mode;
the existing NPC2 streaming configuration remains separate. Foreground card
admission must refresh physical CID and CSD before writing, rather than rely
on HAL's cached identity.

APP660A6DA4 and recovery6408DBB8 remain byte-identical. No Stop, capture,
USB speed-selection or full-speed fallback change is included in the USB
format firmware. The separate Stop console fix from90 is retained.

## Validation and exact artifacts

Final GUI91 SHA256:
`248CA3C63F29AF777D5BE5E06D65715806584F42DB472331791913174912BDA6`.
It uses frozen published90 inputs plus only the three format/reconnection GUI
files and the automatically incremented version. SDK/helpers are unchanged.

131703 software checks passed on GUI91:23 source/reconnection/ambiguity,104
V2 format admission/one-shot/UI,10 firmware seal/coverage/preservation,
121 metadata/legacy/export/layout,105 catalog-repair EP0,11 repair/recovery
UI,85 existing V1 format,30 mismatch/prompt,130632 actual-app parser and fake
DFU programmer checks,592 firmware bundle/identity/UI. The large DFU count
includes assertions per emitted HEX record; it is not a hardware test count.

The firmware developer's actual-adapter/real-catalog native-r3 regression
also passed. It covers fresh physical CID/CSD, invalid/interrupted catalog,
native V2 metadata publication/readback, payload canaries, one-shot requests,
ownership, cancellation/epoch/fault cases, and command routing. Only one USB
stream translation unit was compiled, using all six exact E323 include
preimages and86 unchanged linked objects.148 vectors,8 RAM reservations and
an8KiB stack were checked; ordinary RAM grows152B. Compiler outlining/global
offset effects were audited. No APP/recovery rebuild or hardware action.

The first delivered fused HEX omitted355744B of unused USB-slot/manifest-page
padding and was rejected by the existing app parser. Packaging was corrected
without rebuilding or resealing USB. The corrected fused has2088960 explicit
data bytes (2MiB excluding retained8KiB config); non-USB bytes, USB seal/body
and settings omission passed independent actual-app checks. The rejected
sparse variant is not distributed.

- USB CRC B6099A0E,176608B body SHA256
  `F69B5458A7CAE90E3C28DF7FFFD17B60DD87438721D5E8F610A530B7E2A6C74C`.
- Full fused SHA256
  `E0683376C7DAB7F36ACBB8D5E67EAD31840C9258402A9FEA7A0D57451B88193A`.
- USB-only HEX SHA256
  `2B9EF98132EB61773515DC6807EA142ECEBF9E64CFAF6CFE2A670930C3A59ACF`.
- USB .xenp SHA256
  `6CF99E583545F43308B62042EE3F69FCFC7AABCDF46C3B0AEF0F6EFB3D5AFEB2`.
- Exact USB compiler-input archive SHA256
  `C6C410D3C55674E048C6DD00AAEAD4BCE0D37E5DEEEA66F04DF843CA05D65D89`.

Exact GUI/SDK sources, USB compiler archive/build receipt and native log are
in `ReleaseSources/1.0.0.91`. Firmware identities are in
`Firmware/V2/package-receipt.json`; old USB/full assets are recoverable under
`Firmware/legacy/V2/20261022_before_USB_FORMAT`. Catalog sourceCommit retains
the prior APP/base reference; the USB archive identifies the exact compiler
inputs. No new physical formatting, flashing, recording/download-speed,
all-mode or newly-flashed-pair qualification is claimed.

## Intermediate console check

Unpublished GUI89 SHA256:
`FD08CBF5F244C1378B7DBEF607E16C7F876498080B93F2525233FB17C3DC8BD7`.
Only NpDownloadForm, NpUsbCatalogRepair and NpV2FirmwareForm differ from the
published88 compiler inputs, plus the automatically incremented assembly
version. SDK/helpers/firmware are unchanged at this stage.

449 software checks passed:23 source/reconnection/ambiguity,104 actual-app V2
format admission/receipt/one-shot/UI,121 metadata/legacy/export/layout,
105 V1/V2 catalog-repair EP0,11 repair/recovery UI,85 existing V1 format
transaction checks. USB boundaries are fakes; no discovery, device I/O, real
DFU, erase, flash or hardware qualification was performed.

Reproduce the new checks using `test_v2_usb_format.ps1` with an exact compiled
console. Existing `test_usb_catalog_repair.ps1`, `test_v1_usb_format.ps1`, and
`test_probe_info_layout.ps1` cover the retained workflows. Exact final compiler
inputs, firmware receipts and installer extraction evidence will accompany
the final release. GUI89 was not packaged or published;90 separately shipped
the Stop fix, and91 retains it.
