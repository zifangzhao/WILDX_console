# V2 BLE calibration pressure — GUI 1.0.0.63

## Recording-active admission correction

The first GUI62 hardware adapter run found that calibration never reached the
radio. Production `NpSdStatus.Busy` includes both active-recording bit4 and
pending-command bit8. The new admission predicate incorrectly rejected both.
GUI63 allows bit4 during the required healthy state3 recording and rejects
bit8. The independent control-owner guard remains unchanged. Production flags
`0030FF91`, `0030FF95`, and `0030FF9D` now exercise initial admission, active
recording, and pending-command exclusion in the fixture.

One targeted run passed 6,003 checks. One isolated normal-hook build produced
GUI63 SHA256 `8653385637E334133CF9C4B0D218F76AF43DD006C55644CBFDB766CB5302AE2C`;
form SHA256 `8D64FFE9B832008501FD1EC81987450E1A6F53A43903017D16A396B0F76016D5`.
All 220 original cache hashes and frozen GUI62 remain unchanged. Artifacts:
`_artifacts/v2_preview_active_admission_20261020/`. The V2 developer subsequently
passed the actual-form dual15k BLE preview/SD readback gate; see
`Firmware/V2/acceptance.json` and `source-validation.md`. This build contains no new
USB recovery or IMU changes and has no installer/publication claim.

During a healthy V2 SD recording, an explicit calibration refusal with reason4
previously unchecked Preview. The actual preview form now retains the selection
and schedules at most three retries after 1, 2, and 4 seconds using the existing
`NpV2PreviewRetry` helper. The next timer turn checks the deadline; no retry loop
or busy wait is added. Each calibration uses a fresh preview request ID.

This applies only while awaiting `CapturePreview`, before a wire session has
been established. Recording/control ownership always takes priority. Retry
requires the same connection epoch, selection generation, probe, channel, rate,
allocation start LBA, SD command generation, and probe mask, with an active
fault-free recording with no pending SD command. `LastId` is an SD command generation, not the
preview wire-session ID; the successful calibration reply establishes that ID.
Status is checked again after asynchronous calibration/phase work and before
requesting the rolling lease.

The fourth consecutive pressure refusal shows a terminal error and pauses
Preview. Success or an explicit selection/pause change clears the retry state.
Other typed refusals, timeouts, cancellation, unknown transport outcomes, and
rolling-preview errors retain their previous terminal behavior. No SD Start,
Stop, IMRO, configuration, or clock command is replayed. No acquisition or SD
writer behavior changes.

## Checked identities and scope

- GUI62 SHA256: `1BA81B9BAB654B7C5F7ED48F9CC08EBFB8C63520E79A1351B6E01F84558A31D0`.
- Actual form SHA256: `189F406E75D1CA351A3CF4681231576B8C9113BD63A9C39ABE03F1D6DDB7AA18`.
- Fixture SHA256: `A42F4B4CBBB4FAA7C85E8A561254C00326D74985690E9BE11F3F2848F118223B`.
- One `test_ble_preview_display.ps1` run passed 5,996 actual-source checks,
  including 80 added handler checks for deadlines/budget, fresh IDs, priority,
  successful kind5 display after pressure relief, terminal uncertainty, and
  stale selection/epoch/recording conditions. Only the radio is doubled.
- One incremental GUI compile used the normal version hook and isolated copies
  of the prior cache. The 98-input source ledger admits only this form and the
  version change from GUI61. Embedded resources match GUI61. All 220 original
  GUI61 cache files and the frozen GUI61 executable retain their hashes.

Artifacts and receipts: `_artifacts/v2_calibration_pressure_20261020/`.
The actual private `Task Tick()` is the handler for the main-only hardware
adapter; invoke it on an STA with a message pump and real timer/deadline behavior.
The fixture's accelerated admission/deadline fields are test-only and must not
be used in live validation.

No device access, firmware build, installer, or publication belongs to this
host change. The copied runtime payload contains the previous firmware for host
execution only. The final installer must replace it with the exact verified V2
artifacts after the developer's paired live BLE/SD gate; it is not a newly
qualified firmware/console pair yet.
