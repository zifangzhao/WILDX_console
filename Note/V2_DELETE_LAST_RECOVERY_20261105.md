# V2 Delete Last and automatic recovery

WILDX 1.0.0.248 bundles V2 APP7E463C83 / USB067D89A2 with unchanged recovery
6408DBB8. V1 APP4A677F83 / USB11646986 and V1Camera remain unchanged.

The [earlier V2 test](V2_DELETE_LAST_VALIDATION_20261105.md) found torn-journal
mount failures and valid deletes that required explicit repair. The new
transaction verifies exact Before/After pages and mirrored card-bound intents
before changing the directory. Stopped APP initialization/Start and USB
download initialization reconcile an authenticated pending deletion before
another allocation. Read-only inspection stays read-only; ambiguous authority
fails closed. Cold USB metadata reads use the checked DMA/cache adapter after
the polling adapter produced a receive FIFO overrun under USB service.

## Recorded acceptance

- Native actual-code tests: 18,468 interrupted deletion cuts and 6,156 recovery
  cuts; 44,556,020 checks. Earlier metadata/payload sentinels stayed unchanged
  and subsequent append passed in the defined sector-prefix model.
- Actual BLE Delete and USB A7 each delivered one owned delete before their
  independent VEE-low/five-second MCU-reset interruption. APP recovery was
  observed before entering USB download; USB067 recovery completed the same
  persisted intent without another A7. Both returned success, cleared pending
  authority and matched physical After/journal checks.
- All 22 earlier recording identities, headers, footers and bounded payload
  fingerprints matched. This was not a full reread of every older data byte.
- Ordinary three-probe 20 kHz, closed-loop-off recordings after recovery passed
  full production export with zero reported NP loss and IMU queue/scheduler
  loss. Final app248 run: 8,164 NP blocks, 13,057 valid scheduled IMU rows,
  2,049 native observations, retained source-window and phase checks passed.
- Actual248 cold BLE initialization, recording Start/Stop and guarded normal
  BLE Delete passed. The [host startup change](BLE_STARTUP_READINESS_20261105.md)
  retries only initial read-only discovery/status, with cancellation and a
  30-second bound; mutations are not replayed.

The original release reused the USB16F normal mutation before the metadata-read-only
DMA delta; final USB067 separately passed automatic recovery and A7 inspection.
The supplemental test below now directly verifies normal deletion on final067.
The immutable original acceptance and its measured component scope are retained.
Earlier USB finite-test-job source window limits remain explicit.

VEE enable plus reset did not measure zero volts or remove every power source.
The native persistence model and physical quiet-boundary checks are separate;
arbitrary SD corruption/controller atomicity and endurance are not established.
Scheduled 1,250 Hz IMU rows are cached observations, not sensor conversions at
that rate. Terminal physical conversion/sample completeness limits remain.

Original device flash/options, preferences, debugger state and idle service
were restored exactly; GLOBAL/UID leases closed. The new firmware is qualified
as an incremental build with pinned retained objects and 19 checked role
source selections; historical primary-source gaps prevent a clean-full-source
rebuild claim. The source manifest preserves that limitation.

Immutable acceptance SHA256:
`95D6FC028F7FB66DF46A575861D4158EBF70AEA0543F9A9038C9B926FC26201F`.
Checked source/closure evidence accompanies the release under
`ReleaseSources/1.0.0.248/firmware/V2`. No private device flash backups or full
generated neural test payloads are included there.

## Final067 direct deletion and recording follow-up

The exact published APP7E463C83/USB067D89A2 and app248 passed two direct normal
USB A7 deletions, each targeting one new, fully exported recording. Both returned
23 to 22 entries, restored frontier 36,462,592 and cleared journals 112/115/116.
All 22 original catalog/header/footer/bounded payload fingerprints matched.
An ordinary BLE-to-SD restart reused the reclaimed LBA with a new session.

Both BLE/SD exports passed neural framing and loss checks: 8,116 and 8,516 NP
blocks, with 12,980 and 13,625 scheduled IMU rows and no reported queue/scheduler
or accepted-unsaved loss. Two ordinary USB captures, separated by normal Stop
and restart, each verified 8,088 NP blocks over 10.35264 source seconds, all
three DAT extents, 12,941 scheduled IMU rows, zero NP errors and zero host drops.
The profile was three probes, 20 kHz, closed-loop off; this is not all-rate,
camera/audio, closed-loop-on or endurance qualification.

Timing/native limits remain: SD restart's final IMU-row bound straddles saved
NP end by -302 to +16 microseconds; both USB source-window flags remain false
and final ordinals unknown. Each USB native sidecar has 206 sparse observations,
205 accepted, one invalid startup observation and 1,830 unobserved native
sequence positions. Primary cached-grid drops were zero; this does not prove
retention of every native sensor conversion or exact terminal completeness.

Original firmware/settings, idle/quiet/debug state were restored and leases
closed. About 1.94 GB of generated neural test payloads were removed after
compact evidence was secured. Product, installer, fused image and the original
acceptance remain unchanged.

Supplement: [receipt](../ReleaseSources/1.0.0.248/firmware/V2/final-usb-delete/accepted.json),
[detailed report](../ReleaseSources/1.0.0.248/firmware/V2/final-usb-delete/report.md).
Receipt SHA256: `83DB8FB6650D05729962122CF9DB901F04C2DCDD31E7A695395C145D87957840`.
