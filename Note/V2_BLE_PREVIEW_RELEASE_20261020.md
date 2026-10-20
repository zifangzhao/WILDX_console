# BLE preview display release — WILDX 1.0.0.51

Published48 lacked the private49/50 preview improvements.51 delivers those
checked changes without unrelated private download telemetry.

The sweep uses fixed source-tick positions. Buffered original samples are
revealed at the declared500Hz instead of painting64 samples at each BLE
arrival. Local refresh does not wait for status/control writes. Window, gain,
channel, filtering Fs and original values/timestamps are retained. No
interpolation/resampling, additional BLE traffic or MCU processing is added.
Missing samples remain gaps; this does not repair RF loss or aliasing.

## Frozen identities

| Item | SHA256 |
|---|---|
| GUI51 | `4057008015E449A01A2EFE5A094A66D91B2B54106DC52E4880F1526AC4FA5C72` |
| Installer51,6509455bytes | `D4E124F6D55027726B71D3D613C3030356419CA19CD0B910824289FF7E5B4264` |
| Preview form | `9B61A9E503023C72C6B90228DCF72039A6516B7B1E88D1CDA00BE0193CFED1D7` |
| Display fixture | `00B52272B26E524787C73D6D883A364819E0641E3743403000F13395DD8D4BB0` |
| Unchanged exporter DLL | `A396530AAD81A5C8BA6FDC8086FC1FB2642A2E4E34C52E50392AE8763FE32A2F` |

Reused5916 passed actual-source checks for identical form/fixture: compact/
legacy transport,7.5/15/20/30k source rates,500Hz pacing, unchanged samples/
ticks, genuine gaps, sweep/window, channel/lease and control-busy behavior.
Radio alone was doubled. Retained offscreen rendering was visually inspected.

One cached incremental Compile/Copy produced51 via the normal version hook.
The98 recorded GUI48 source/project inputs were admitted, changing only preview
and version. Temporary Compile-item substitutions used exact48 download-source
artifacts; canonical private files were untouched. Form SHA
969AF39B67335C07DF1F0AD4748DEB1D4B0D967B5380C3758C9E6525D8F6E622;
Stream SHA02D62A9EC78DA8DF308082DECFCD141A0CE139C341808D0B6E940CBE79C62014.
Actual compile paths confirm both substitutions. Managed embedded resources
match48; current resource/reference inputs are recorded separately. The98-input
receipt alone is not a full compiler/resource ledger or clean-public-HEAD build.

One NSIS pass reused the checked48 binary payload, replacing only GUI EXE.
All40 independently extracted files match; the other39, including firmware,
configurator and updater, are unchanged. Private50 EXE is preserved. No
dependency/firmware build, device connection, recording, power experiment,
installer execution or new installed-pair qualification belongs to this release.

The user's installed version, received transport and physical waveform remain
unconfirmed.500Hz is the sample grid, not guaranteed delivered coverage or
qualified LFP. Firmware remains the previous checked payload; V2 recovery
6408DBB8/USBE323A06A/APPB60291EE is not newly recording-qualified here.

Package: `LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.51-BLE-preview-20261020.exe`.
Removable evidence: `_artifacts/ble_preview_release_20261020/`. Previous48 is
recoverable in `LATEST_INSTALLERS/legacy/20261020_before_ble_preview51`.
