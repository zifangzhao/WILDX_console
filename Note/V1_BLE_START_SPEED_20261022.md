# V1 BLE setup time — console 1.0.0.95

The user confirmed recording eventually starts after the qualification warning.
The PC path performed 16 clock observations and an unconditional 53-exchange
map upload per retained selected probe on every Start. This can leave a long
delay after OK even though recording has not failed.

Two PC-only changes:

- Automatic V1 Start requests eight clock observations, the existing engine's
  supported minimum, instead of 16. The existing uncertainty, generation,
  CRC, anchor receipt and offset checks remain. Explicit Sync PC time still
  defaults to 16; V2 initialization is unchanged.
- BLE staging uses one fresh B8 STATUS per selected port to reuse an identical
  resident map. Require normal NPR1 parser validation, Error0, TransferState
  NONE0/STAGED2 with Received0, PortState STAGED2/APPLIED3, exact payload CRC
  and all 32 configuration-ID bytes. Any mismatch/reset/failed port uses the
  existing upload path. Invalid/error receipts still fail normally. No host
  cache or unsupported protocol assumption replaces the status exchange.

The existing two-argument StageAsync and three-argument StageForCaptureAsync
APIs remain with full-upload defaults. Reuse is explicitly enabled only by
the BLE recording handler. USB/manual behavior remains unchanged.

A resident map is not hardware readiness or fresh application proof. Firmware
START still performs quiesce/bring-up/Prepare/Applied and controls acceptance.
Old APPLIED receipts can outlive a probe failure, so the GUI does not present
them as verification for the new recording. A FAILED port always reuploads
to clear the firmware latch. A rate-family reboot clears RAM maps and thus
requires the normal upload on the next fresh status.

The existing V1 developer confirmed these semantics from np_config.c,
np_config_port.c, np_sd_control.inc and np_sd_firmware.inc, including the
engine used by bundled APP654824DE. No firmware update is required.

## Evidence and limits

Local build: `_artifacts/v1_ble_start_speed_20261022/gui95/compile-bin/WILDX_console.exe`.
Only NpBleForm.cs and NpConfigClient.cs replace the frozen GUI94 compiler
graph. All prior calibration/diagnostic changes remain included. SDK unchanged:
`86EF0F858AE6CDB188C9C525ED50894EA204D6DB15C1C38BC1EB472CDBC7F546`.
Exact source/binary hashes are in `gui95/build-receipt.json`.

- PASS29 actual-client reuse checks: unchanged two-probe configuration drops
  from 106 exchanges to two fresh STATUS exchanges; full upload on changed
  ID/CRC, reset, failed port, undocumented/in-progress transfer or partial
  receipt; invalid status refusal; selected-port scope; old API defaults.
- PASS146 actual clock-engine checks, including eight-observation quick sync,
  single COMMIT, normal 16-observation initialization, active Observe and
  quality/CRC/generation/receipt rejection.
- PASS105 actual-source Apply/Start/Stop flow checks; includes automatic
  eight-observation selection, opt-in reuse, old firmware warning acceptance,
  and no new V2 host admission gate or mutation replay.

These are software checks. Device startup latency has not been measured and
first setup, changed IMRO, lost RAM maps, rate-family reboot and physical probe
initialization still take time. No hardware, firmware, SD data, packaging or
publication was touched. GUI95 is built locally, not shipped.

These changes are included in the packaged GUI1.0.0.96 installer. GUI95 was
an intermediate checked build and was not published as a separate installer.
