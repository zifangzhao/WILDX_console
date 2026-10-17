# V2 BLE preview drain and viewing window — 2026-10-17

Confirmed host-side blocking: the prior form returned from its display timer
when background status/time controls or another preview write were busy. This
also deferred already-received packets. The retained baseline reproduces that
stall with a fake radio; this does not identify every producer/transport gap.

`NpBlePreviewForm.cs` now consumes the bounded eight-packet display queue before
wire-busy gates. Only new preview requests/lease writes yield to controls.
The local timer polls at16ms instead of50ms for32ms packets; bounded decoding
no longer needs an empty/per-tick thread-pool round trip. Existing wire
serialization, epoch/session/selection guards, cancellation and failure behavior
remain. No firmware, SD, acquisition, recording, protocol or sample-rate change.

New **Window (s)** control:0.1–10s, default4s. History is capped at5000 display
rows at500Hz; changing the window retains samples/lease and sends no command.
Autoscale uses only visible samples. Missing/invalid samples remain real gaps;
no interpolation, fabricated continuity or anti-alias/LFP qualification claim.

Focused command:

```powershell
./test_ble_preview_display.ps1 -BaselineForm _artifacts/v2_ble_preview_display_20261017/NpBlePreviewForm.baseline.cs -OutputDirectory _artifacts/v2_ble_preview_display_20261017/checks
```

Baseline reproduction PASS; final51 actual-source checks PASS. These compile
the real form/stream, protocol, filters, theme, calibration and nominal sample
conversion, with only the radio doubled. They cover control/write-busy drain,
unchanged wire ownership/no extra writes, bounded drops and gaps, invalid rows,
stale epochs, P1/P2 at7.5/15/20/30k, window controls/history/wrap and actual plot
gap rendering. Initial offscreen fixture sizing/background assertions were
corrected; no production gap or transport guard was weakened. Scoped diff
whitespace check passes. No live GUI, discovery, device or recording test.

Delivery: source handoff to canonical console owner; no additional full GUI
build, version bump, installer, push or firmware pairing from this task yet.
Frozen1.0.0.38 candidate/cache and unrelated pending BLE/download work are
untouched. Do not present this component fixture as an installed-pair test.
Evidence: `_artifacts/v2_ble_preview_display_20261017/`, including baseline,
final checks log and compile-input hashes. One coordinated GUI build remains.

The console owner reviewed the production diff and independently reran the
baseline reproduction and all 51 final checks in `owner-review/`; both passed.
The focused runner is included in `test.ps1`. These changes remain source-ready
and are not present in the frozen 1.0.0.38 executable or an installer.

## Follow-up: one-channel subsampling, explicit preview Fs

The user approved reducing preview channels and explicitly requested simple
subsampling, no resampling, and correct viewer Fs. V2 firmware's existing
500Hz/16-row legacy packets overrun its~60ms UART+drain cadence; the GUI drain
fix does not hide that genuine timestamp loss. New compact rolling request5
retains64 single-channel samples/packet (128ms at500Hz) with raw serial-bit
packing only. Device ADC decoding/filtering/interpolation are not added.
Acquisition/SD clocks, stored formats and DMA ownership remain unchanged.

Wire remains224B body with existing header: kind5|0x40, rangehigh16=192 and
low16=64. Body32:uint32 Fs500;36:uint32 rows64;40:uint64 validity;48..175:
64 little-endian uint16 low12 raw bits (first serial tick=bit0, high4zero);
176..223 zero. Channel0..383 is exact, lane=(probe-1)*3+channel/128. Native
Hz/FirstTick are unchanged; source step=(nativeHz/previewFs)*1600.
This supports P1/P2 preview only; P3 recording is unaffected.

The canonical decoder validates compact metadata/words/padding/overflow and
reuses the PC bit decoder. The form consumes packet Fs/count/validity for
time, gap, HPF and window calculations, plots exactly the selected channel
in both modes, and keeps genuine gaps. UI labels aliasing explicitly; no
anti-alias or qualified-LFP claim. Legacy0/1/2 are retained. Explicit matched
unsupported reason1 allows one bounded550ms legacy fallback; no timeout or
generic transport-error fallback. A new form/epoch is needed to reconsider
the unsupported latch.

`NpBleConnection.TryWritePreview` now allows5 only for V2 (V1 max4 unchanged).
`NpBleWire` retains legacy six-bit truncation recovery and adds compact
high-byte/zero-tail recovery so a truncated preview does not hold controls.

Focused actual-source run:

```powershell
./test_ble_preview_display.ps1 -OutputDirectory _artifacts/v2_ble_single_preview_20261017/checks
```

PASS5434 protocol/display/control/window/gap/compatibility checks. Real form,
decoder/framer and extracted production admission method compile; radio only
is doubled. Checks cover source-rate/probe/channel boundaries,64-bit mask,
actual Fs/HPF/window, split/truncated compact frames/control recovery,
epoch/cancellation/CRC/control priority and explicit-only fallback. Firmware
native regression and one-unit-O3/memory evidence live in V2 shared
`Note/Tools/V2SdLogger/PROGRESS_PLAN.md` section736.

Delivery remains source/component checked: no full GUI build/version/installer,
frozen.38 mutation, flash, radio, real recording or publication in this change.
Canonical console owner owns the next coordinated full build. Existing
installed preview does not acquire this format until both components update.

## Channel switching keeps Preview enabled

Source-only follow-up to the directly requested console interaction: changing
channel or probe no longer unchecks Preview. It retires the old host stream,
clears its display and preserves the Window(s) setting and user's enabled/paused
choice. Repeated changes converge to the latest selection.

An active V2 preview lease lasts2s;550ms is only request admission, not lease
expiry. The form therefore leaves a2250ms host quiet window before calibration
or rearming. An already in-flight calibration/start/renewal is allowed to retire;
its completion restarts that quiet window. Selection generations guard every
async continuation and exception so stale callbacks cannot repopulate session,
renew an old stream or disable the newly selected preview. No recording Stop,
Start/reset, preview stop command, firmware change or protocol change is added.
This host-side margin is not a physical RF/MCU delivery-latency measurement.

One focused actual-source run PASS5493 checks:

```powershell
./test_ble_preview_display.ps1 -OutputDirectory _artifacts/v2_ble_selection_lease_20261017/checks
```

Retains prior compact/legacy/window/wire coverage and adds real checkbox/value
events, unchanged pause/window choice, rapid changes/latest selection, no early
rearm at550ms, automatic rearm after lease expiry, stale packets, and held old
calibration/start/renewal completion with both success and error/cancellation.
Only the radio is doubled. No discovery/device/recording, firmware/GUI build,
installer/version/package/flash or push. Scoped whitespace checks pass.
Changed production file: NpBlePreviewForm.cs; fixture:
Tests/NpBlePreviewDisplayTests.cs. Existing runner is unchanged.

## Cached recording-info header

Added internal `Func<string> RecordingInfo` and a compact24px top label above
the preview controls. Existing constructors are unchanged. Each Tick refreshes
it before recording-readiness/control/preview-busy gates; absent/empty/whitespace
text hides the strip. It reads only the owner's cached formatted text, with
no device commands. Provider exceptions show an unavailable message without
affecting preview or recording. The canonical owner injects common elapsed/
committed-size/available-space formatting in its separate main-form work.

One focused actual-source run PASS5505 checks:
`./test_ble_preview_display.ps1 -OutputDirectory _artifacts/v2_ble_recording_header_20261017/checks`.
Added top docking/visibility, busy and paused refresh, text/provider removal,
provider failure isolation, no radio calls and closed-form behavior; prior
selection-lease/window/compact compatibility checks remain. Only form/fixture
and this shared note changed; runner unchanged. No GUI build, firmware change,
device, installer/version, packaging or publication. Handoff for coordinated
console integration, not a claim that the currently installed UI has changed.
