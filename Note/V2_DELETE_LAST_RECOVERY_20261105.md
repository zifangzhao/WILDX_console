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

Normal USB deletion passed with USB16F before the metadata-read-only DMA delta.
Final USB067 separately passed automatic target/clear writes and normal A7
inspection. That measured component reuse is retained rather than claiming a
new normal067 mutation or endurance test. Earlier USB finite-test-job source
window failure remains explicit; ordinary BLE recordings independently passed.

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
