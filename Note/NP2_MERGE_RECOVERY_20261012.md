# NP2 USB merge options and explicit-gap continuation — 2026-10-12

## Delivered scope

WILDX **1.0.0.10** adds NP2 USB merging and automatic transport recovery in
the canonical universal console repository. No firmware, MCU clocks, DMA,
USB transfer sizing, sample decoder, SD exporter, BLE service, configurator or
DFU updater changed. The Ponytail approach reused NP1's existing session,
timer, cancellation and gap journal rather than adding a recovery framework.

The installer is present in canonical and V2 `LATEST_INSTALLERS`:

- `WILDX_console-Setup-1.0.0.10.exe`, 2,254,004 bytes.
- SHA256 `E0B42B73C6081847D16169549AA992B63373A76C62CAB5B4B29945C08FBA9E28`.
- Checked GUI SHA256 `42B86C0605BFC6D90608E19CFD9A3B788D4864BCDBC8E383442353346A599FAF`.
- Twelve other runtime payload files retain their checked 1.0.0.9 identities.
- Previous 1.0.0.9 deliverables were moved recoverably to each repository's
  `legacy/20261012_before_np2_merge_recovery_1.0.0.10` directory.
- Local software-checked package; not installed, committed or pushed this turn.

Published firmware compatibility remains recovery `6408DBB8`, USB `A33B9912`,
APP `ED881256`. No newly fused firmware or full end-to-end pair qualification
is implied. Private BLE/SD development was excluded using the existing frozen
release inputs; the prior signed NP2 `2048 - raw12` convention is retained.

## User controls and output

In the existing NP2 **Recording settings**, select **Merge probes into one AP
DAT after Stop (source files retained)**. Disable it for per-probe output only.
It shares NP1's merge preference; a single probe already produces one DAT.

During acquisition, the existing parallel per-probe DAT and RAW writers remain
unchanged. Merging runs on a below-normal PC thread only when the overall
capture/recovery session ends. Recognized reconnect retries defer merging,
so offline processing does not delay the next capture attempt. Stop also
cancels offline merging. The app prevents a new capture/update while finalizing.

For each decodable segment, the root `neuropixels_ap.dat` is sample-major signed
int16LE, ordered by ascending physical probe, each a contiguous block of 384
hardware output channels. Two probes give 768 channels; three give 1152.
Probe IMRO/configuration receipts remain in their existing metadata files;
merging does not reorder channels by electrode/site number.

All original `probeN/neuropixels_ap.dat` files, RAW, settings and tails are
retained. Only the common complete row extent is merged. `np2_merge_status.txt`
records rows, channels, rate, source row counts, unmatched tails and each
first complete SuperSync row's source-clock tick. Offsets are **recorded, not
corrected**; matching row indices do not prove simultaneous ADC sampling. No
padding, interpolation, gap repair or separate synthetic LFP stream is added.
Output is prepared under `.partial` and renamed only after its data flush.
Existing final or partial merge output is never overwritten. On merge failure
or cancellation, original files remain available; a failed merge is not called
a successfully finalized merged DAT. Failed segments remain incomplete even
if their structurally clean prefix can be merged.

## Continuation policy

The shared **NP auto-recover (gaps allowed)** checkbox is now visible for NP2
as well as NP1, initially checked. The session pins the exact MCU UID and kind.
Recovery searches only that UID, including its re-enumerated USB personality,
then reuses the selected rate, probe mask and retained IMRO/apply intentions.
It does not substitute a different board or replay unknown configuration ACKs.

Recognized WinUSB open/read/completion errors 2/6/31/121/1167 and disappearance
of the pinned device can qualify for recovery. Returned capture results must
have verified every received block, no RAW/DAT write or decode fault, no drop,
and no reported capture/producer/source firmware fault. Unknown errors,
configuration timeouts/receipts, decoding, disk and firmware faults stop rather
than creating a misleading successful continuation.

Each attempt creates a **fresh `segment_XXXX` folder and decoder state**.
Retries are bounded at 12 consecutive empty/short failures (existing NP1 policy),
with quick retry for a populated segment and two-second empty-attempt backoff.
Stop disarms the timer. Already accepted hub prompts are not repeatedly shown
on internal retries. Settings and Start stay disabled while reconnecting, even
if an asynchronous status refresh updates controls.

`session_events.csv`, `session_status.txt`, the session README and each segment's
capture status record the interruption. Host first/last receive UTC values are
observations, not exact neural timestamps. Exact gap duration and missing sample
count are **unknown**. A recovered session is not marked lossless; incomplete
best-effort stops cannot accidentally acquire a lossless flag. Merged output
is **one file per segment**, never concatenated across the recovery gap.

## Evidence

Artifacts: `_artifacts/np2_merge_recovery_20261012` (removable outputs).
Tests retained as source:

- `Neuropixels/Tests/TestNp2MergeRecovery.cs`: **10,501,794 checks PASS** against
  the actual checked GUI assembly, generated signed DATs and detached controls.
  Two/three-probe interleaving, 15/20/30 kHz metadata, batch boundaries, signs,
  channel/probe order, tails, phase offsets, source preservation, existing-output
  protection, partial/empty input rejection and cancellation are covered.
  Recovery predicates, native exception-code formatting, fresh folders, pinned
  UID, retained profile, incomplete/gap journal, bounded retries and Stop are
  exercised without a main-window constructor, message pump or device call.
- `Neuropixels/Tests/TestNp2MergeMetadata.cs`: **40 checks PASS** against actual
  GUI JSON/XML writer; merged 768/1152-channel roots, retained 384-channel child
  files, native rate, absent NP2 LFP and incomplete-prefix flags checked.
- Initial targeted regression caught Start being re-enabled by a status refresh
  during reconnect. The first GUI build failed that guard test; only the GUI
  was rebuilt after the fix and the final regression passed. No unchanged
  firmware or SD DLL was rebuilt. Fixture-only compile-overload and missing
  fixture DAT corrections did not change production binaries.
- One installer packaging pass; exact delivered installer identities checked
  in both latest directories. Updater/DFU dependencies remain unchanged.

These are software checks, **not physical reconnect or continuous recording
qualification**. No flash/reset/reconnect, card writes/formatting, firmware
build, BLE session, hardware capture, installer execution or independent
installer extraction occurred. Real USB unplug/replug, setup re-entry and
post-reconnect streaming remain unverified. Full UI asynchronous finalization
and a long, disk-space-limited merge have not been manually exercised.
Triple 30 kHz USB remains unsupported; merge/recovery does not improve physical
bandwidth and does not add BLE/SD recovery.

## Progress and next bounded work

Confirmed: NP2 merge UI/output, explicit-gap recovery policy, reconnect control
guard, metadata, Release build and local installer complete. User-facing option
does not add MCU work or per-row merging during live USB capture.

Next: install this GUI for a focused real-device USB recovery/UI check when
requested; verify the same UID/probe/rate/IMRO receipt, fresh resumed segment,
Stop, prefix preservation and post-session merge. Publish the frozen software
package only on a push request, reporting this limited evidence rather than
claiming a newly lossless firmware/GUI pair.

## Follow-up — display high-pass option, WILDX 1.0.0.11

Confirmed on 2026-10-12: NP1/NP2 USB preview has **Display HPF: Off (default),
1 / 10 / 100 / 300 Hz**. BLE preview has Off / 1 / 10 / 100 Hz, below its
500-sample/s display Nyquist limit. This is a first-order causal display filter,
not a device/ASIC configuration change or a zero-phase analysis filter. Off
preserves the previous display behavior. Leave it Off or use a low cutoff for
low-frequency viewing; 300 Hz intentionally removes low-frequency content.

The shared small filter has independent channel state. USB applies it only
when the preview queue drains into display-owned sweep storage on the existing
below-normal renderer; the capture callbacks, DAT/RAW writers and their inputs
are unchanged. BLE applies it only to disposable nominal-count display rows,
not its raw packets. AP/LFP use their actual sample rate. Missing samples,
view/cutoff changes and explicit resets seed a new baseline, never bridge gaps
or mix channels. No dependency, firmware, SD exporter, configurator or DFU
updater changed. The retained 1.0.0.10 merge/recovery source was not modified.

One incremental Release GUI build, one focused regression: **3,875 checks PASS**
against the actual delivered assembly. Coverage: byte-exact Off path, DC removal,
cutoff-frequency response at 500/625/15000/20000/30000 samples/s, signs, independent
channels, mapped AP/LFP queue, recording-input immutability, USB controls/rate,
BLE RAW preservation, invalid-row gaps and resets. A fixture-only follow-up ran
**10 offscreen UI checks** and rendered the two preview windows without repeating
the numerical suite. NP2 toolbar image was visually inspected: the selector is
visible and does not overlap the status or other controls. Main app/device
sessions were not opened. No hardware access or new recording qualification.

Evidence: `_artifacts/np_display_hpf_20261012`; runnable check source:
`Neuropixels/Tests/TestNpDisplayHighPass.cs`. The initial hidden-form image was
blank, so only the visual fixture was corrected to show its own preview windows
offscreen; no production rebuild was needed.

One packaging pass produced `WILDX_console-Setup-1.0.0.11.exe`, 2,255,132 bytes:
SHA256 `1660D6CFCE3E16336382969708E060E5E3A734BE2710293F98A601938DA80483`.
GUI SHA256 `66FE330E2C242518A7B4BCAF555C61E084C991C1C0A73E715C93BB2E6557A895`.
Twelve other runtime files match 1.0.0.10. Identical installer copies are in the
canonical and V2 `LATEST_INSTALLERS`; prior local 1.0.0.10 deliverables are
recoverable under `legacy/20261012_before_display_hpf_1.0.0.11`.

Progress: display option, ownership/numerical checks, UI check, Release build
and local installer complete. Next bounded step remains the previously noted
real USB recovery/UI validation when requested. This turn did not install,
commit, push, flash, reset, connect to hardware, modify SD contents or execute
the installer; the new package is not a newly end-to-end-qualified firmware pair.
