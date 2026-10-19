# V2 USB catalog repair — 2026-10-19

WILDX 1.0.0.47 adds **Repair SD catalog…** to USB Mode and Data Download.
This is a real firmware operation, not the read-only Recover recordings scan.
Install USB service **E323A06A**; an APP-only update cannot add this command.
The matching fused image retains recovery6408DBB8 and APPB60291EE, with
retained settings omitted. No hardware was flashed for this change.

## Scope and safeguards

Only idle USB download mode may inspect/repair. Recording, DMA, download,
preview and other active owners prevent admission. The SETUP interrupt queues
work; foreground performs it. Exact device/card/epoch and increasing transaction
numbers are checked. The user confirms a fresh inspection before one repair.
Missing acknowledgments/timeouts are unknown outcomes, never automatic replay.

The adapter reuses the existing catalog recovery implementation. Writes are
restricted to catalog/journal sectors112–113 and128–1151; recording payload,
descriptor and footer sectors cannot be written by this adapter. It does not
format, delete, rewind, reclaim unknown allocations or start a recording.
Genuinely full/unknown-boundary states remain errors, with numeric diagnostics.
Completed metadata repair does not certify an interrupted recording as complete.
An SD operation that fails to retire keeps its ownership fence; explicit
recovery-loader entry is permitted only after physical SD activity stops.

## Evidence

- Actual adapter plus real catalog/meta/run code: focused native regression
  passes v11/v14 metadata, interrupted journal writes, card/epoch changes,
  nonce/replay/admission, payload canaries, cancellation and held-owner guards.
  SD HAL/USB are modeled; this is not physical power-interruption qualification.
- Actual EP0/HAL/LL focused regression passes DMA status-stage bounds, next
  SETUP/canaries and DMA/non-DMA paths. This proves the source fix, not a cause
  for every historical Windows error31.
- One incremental USB build: four changed translation units and83 frozen
  objects; feature-OFF/source-preimage reproduces every published2E07799E byte.
  Eight RAM reservations and148 semantic vectors checked; ordinary RAM grows
  4080bytes. The8KiB startup stack remains bounded. Capture, packing, bulk,
  clocks, HAL and DMA objects are unchanged. Narrow postcheck corrections reuse
  already-compiled objects; no repeated compilation.
- Canonical WILDX1.0.0.47:105 actual helper/EP0-boundary checks and11 actual
  compiled repair-button ownership checks pass. No manual clicks, discovery or
  hardware in these tests. Generation-specific capability checks prevent old
  V1's ROM-DFU flag from being mistaken for repair support. V1 repair firmware
  remains a separate development task.
- The first console compile encountered an old cached exporter. Reusing the
  exact previously-published A396530A… exporter fixed that dependency mismatch;
  no exporter/configurator/updater rebuild. Automatic versions46/47 reflect
  failed/successful compile attempts. Two modeled-receipt fixture inconsistencies
  were corrected without weakening production checks.

Receipts: `_artifacts/v2_usb_catalog_repair_20261019/build/incremental-build.json`,
`delivery/source-manifest.json`; native output
`_artifacts/v2_usb_catalog_repair_native_20261019/r2`. Canonical console source
and its helper/UI evidence remain in the WILDX repository.

The release records the proven object-cache integration. New catalog sources
and a scoped `usb-catalog-integration.patch` are published; unrelated working
AUX/idle/download experiments are not staged. The patch's source baseline is
the exact current-source preimage whose feature-OFF build reproduces published
USB2E, not a claim that a clean public HEAD reproduces this historical cache.

## Delivery limits

USB-only HEX SHA256:
76740C47FBB6A3271C46796150F1A220B8FA02F2ABCBB6F23341B1CA8DE7C0C9.
Fused HEX SHA256:
9DAB56E0BFBF03B48BE289F436F01A4F4A13E6F0EC503DFC69D51384D1118FDF.
USB update container SHA256:
DFFF2AE25A08015214ED661DC912C14F604BFDD3CD93D40E820521F70F5F36CA.
USB generation3/ABI4/automatic-USB flags3; LF-normalized HEX.

Targeted software/component checks only: no new installed-pair, physical USB
repair, recording, throughput, endurance or analog qualification. Existing
APP recording-mode/card/power limits remain; no all-mode lossless or power
improvement claim. This slice does not resume the deferred power experiments.
