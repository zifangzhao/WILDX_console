# WILD-X90: V2 BLE Stop finalization

The user's screenshot already shows Complete, but its log retains the older
30-second Stop confirmation timeout. Published APP660A6DA4 drains the source,
rereads the saved native recording, then finalizes the catalog before Complete.
The fixed 30-second GUI deadline misreported a legitimate long finalization;
later status polling did not resolve that warning. This does not prove the
installed APP or the integrity of the pictured recording.

## Fix

V2 Stop is sent once. After acknowledgment the foreground UI operation ends;
ordinary two-second status reads continue, including when optional periodic
refresh is unchecked. The status shows elapsed finalization time, without an
invented progress percentage or a 30-second failure. A fresh terminal status
from the same connection epoch and peer resolves the observation and reports
completion or a stopped fault once. A lost acknowledgment remains explicitly
unconfirmed; it is never replayed. A changed/disconnected connection retires
the observation as unknown. New Start and repeated Stop are blocked while
that observation remains pending. An already-finalizing device is observed
without another Stop command. V1's existing Stop behavior is unchanged.

## Evidence and limits

- 102 actual-source Apply/Start/Stop checks passed using a fake transport and
  deterministic elapsed time, including >30-second finalization followed by
  Complete, lost Stop ACK, status-read failure/recovery, terminal faults,
  same-epoch/peer guarding, duplicate Stop suppression, immediate completion
  and preservation of V1 behavior. Production status/timer/reset hooks are
  checked by the runner. Run `test_ble_recording_settings.ps1 -FlowOnly` with
  its required ConsoleExecutable argument (unused in FlowOnly mode).
- One incremental GUI90 Release build, from exact published88 compiler inputs
  plus NpBleForm.cs and the automatic compilation version only. Its frozen
  source matches the source exercised by the handler regression.
- One installer packaging pass; all40 extracted payload files match their
  checked inputs. No firmware, SDK, helper, catalog or format UI changes.
- The legacy settings/layout test run against the default old bin/Release
  executable rejected its obsolete unqualified-checkbox UI. That unrelated
  assembly is not the release input; the focused source flow regression was
  run in isolation. No claim of a fresh installed-application visual check.
- No hardware access, BLE command, recording, reset, flash or new end-to-end
  hardware/pair qualification. Existing firmware evidence is reused unchanged.

GUI90 SHA256: `8EDDFAD97BA550C74E42D36D16CCEF39294D49CDE0D79D2C9C8483C2AAE1BEED`.

Installer `WILDX_console-Setup-1.0.0.90.exe`,7050767bytes, SHA256:
`1201CAC554E058F0E448FD57B3BB845775B9BBFB0364296D6A13930AC554D8F1`.

Exact inputs and payload identities are retained in
`ReleaseSources/1.0.0.90` and `LATEST_INSTALLERS/release-manifest.json`.
Firmware remains recovery6408DBB8 / USBE323A06A / APP660A6DA4.

Keep device power connected until a fresh Complete/stopped terminal status.
The APP's full-recording verification time is deliberately not shortened by
this console fix. Transport failures and fault terminals remain visible;
neither a Stop ACK nor this UI patch establishes lossless recording.
