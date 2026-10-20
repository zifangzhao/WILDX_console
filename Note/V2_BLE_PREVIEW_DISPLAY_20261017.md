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

## 2026-10-20 — Fixed source-tick sweep and visible transport limitation

User reports the BLE waveform still looks bad. A screenshot/version and whether
the symptom is jumping, distortion or amplitude remain requested; no physical
waveform-quality diagnosis is inferred from synthetic display checks.

The previous plot anchored every X coordinate to the newest received tick,
moving the whole trace by16/64 samples at each BLE burst. The plot now uses a
fixed phase of the original source tick within the selected window, matching
the USB oscilloscope behavior. Existing samples stay at fixed X positions;
missing intervals and sweep wrap break the line. An isolated valid sample is
drawn rather than silently disappearing, and a cursor marks received progress.
Window/gain/channel controls and original sample values are unchanged. No
interpolation, resampling, new MCU work or radio/recording command was added.

Status now explicitly distinguishes compact64-row versus legacy16-row transport
and labels500Hz as the sample GRID, not a guarantee of delivered coverage.
Firmware source evidence: a legacy16-row packet represents32ms but requires
about19.8ms UART time plus40ms pacing, so its offered throughput is below500
samples/s. Compact64-row packets represent128ms and fit that budget. Actual
remote firmware/received mode remains unconfirmed; do not claim this GUI change
repairs RF gaps, legacy transport throughput or aliasing. Raw subsampling can
alias; it is not an anti-aliased LFP reconstruction.

Focused regression: `test_ble_preview_display.ps1 -OutputDirectory
_artifacts/ble_preview_scope_20261020` passed5516 actual-source checks, including
stable X across bursts, exact500Hz/window period, missing-row gaps, no false
wrap diagonal, mode labeling, compatibility and zero extra radio commands.
Radio only is doubled; no device/discovery/recording or app launch. Offscreen
`ble-preview-fixed-sweep.png` was visually inspected: trace is coherent, the
missing burst remains blank and no false wrap segment is present.

The initial fixture compile failed on a new local-name collision and the
unrelated new V1 filesystem dependency in NpSdGeometry. The fixture now extracts
the actual pure geometry/header/count helpers it needs, like its existing
production filter/admission extraction; it does not mock storage verification.
All source dependencies are hashed in source-inputs.json. No V1 storage source
was edited to make a preview test pass.

One cached incremental Release `Compile;CopyFilesToOutputDirectory` succeeded,
automatically versioned1.0.0.49. No restore/dependency/configurator/updater build
or installer generation. Executable is the canonical bin/Release/WILDX_console.exe,
SHA256 F686D6A70330AB134A246BB22A5A6699E8283279BBC23308F2B871ACA021F2C5.
Form SHA81940F6B2F21856022338F6C699638EB6BD6228429F36CC9B597E4FC31D60E1D;
fixture BB143B4B816C7C2FBE48B50ECF38B79701699C102489AA0E86E470538C9FD955;
runner656A16DD4027ABDE2334CB32547BF23571593656C85BF2D6FC076A89E8D972F2.

This is a GUI-only component check/development build, NOT a newly qualified
firmware/installer pair. Other pre-existing unbuilt catalog/download changes
were preserved; the cached exporter dependency was not rebuilt. No firmware
flash, hardware experiment, installer/package/publication or claim that the
user's installed app updated. Canonical console owner receives these exact
patch/identity/evidence limits for its coordinated integration.

## 2026-10-20 — Pace original samples between BLE bursts

The renewed waveform complaint exposed a remaining display issue independent
of the fixed-X sweep: compact packets contain64 samples/128ms, but Append
painted every sample immediately and the16ms timer did not repaint between
packets. Even a loss-free compact stream therefore advanced in roughly8Hz
chunks. Published installer48 also predates the private49 sweep fix; the user's
actual installed version/transport and waveform remain unconfirmed.

The PC now reveals already-buffered original samples using a monotonic host
clock at the packet-declared500Hz, with the first received burst providing the
initial display lead. The preview timer refreshes locally even while another
control owns the radio. Source ticks, values, filtering Fs, window/gain choice,
missing intervals and fixed sweep positions are retained. No interpolation,
resampling, extra BLE traffic, MCU work or recording change. This corrects
burst-wise visual stepping, not RF loss or subsampling aliasing.

One focused actual-source regression passed5916 checks: compact/legacy timing
at7.5/15/20/30kHz recording, no whole-burst reveal or clock restart, immutable
samples/ticks, sparse-row timing and control-busy redraw without radio work.
The first run stopped on an old bitmap assertion expecting immediate whole-burst
display; bitmap fixtures now explicitly advance the display clock. The corrected
run passed. Offscreen fixed-sweep image was inspected; a deliberately missing
packet stays blank. Evidence: `_artifacts/ble_preview_pacing_20261020/`.

One cached incremental Release Compile/CopyFilesToOutputDirectory succeeded
as1.0.0.50; dependencies, firmware, configurator, updater and installer were
not rebuilt. Canonical bin/Release/WILDX_console.exe SHA256:
E2F43F95258B76FED940D07FCDD3E835413B55272AC235F8B2E515208294223C.
Form SHA9B61A9E503023C72C6B90228DCF72039A6516B7B1E88D1CDA00BE0193CFED1D7;
fixture SHA00B52272B26E524787C73D6D883A364819E0641E3743403000F13395DD8D4BB0.
The compiler's normal hook alone updated AssemblyInfo to50. Scoped whitespace
checks pass. Other pre-existing private changes were preserved, not qualified.

Status/next: component-checked private development build only. No app window,
radio/device connection, recording, flash, installer, release pairing or push.
Console owner receives this handoff and owns coordinated delivery; published48
is unchanged. Obtain the current screenshot/version and samples/s/gaps/skipped
line before claiming the physical waveform is repaired.

## Delivery — 1.0.0.51

The existing checked preview changes are isolated over the recorded48 build
inputs, excluding both private download telemetry files. One cached compile and
one package produce51; all40 extracted files match, with39 non-GUI files
unchanged. Firmware is not rebuilt or requalified. See
[release identities and actual limits](V2_BLE_PREVIEW_RELEASE_20261020.md).
