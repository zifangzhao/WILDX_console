# V1 connector and metadata persistence fixes — 1.0.0.19

This app-only release addresses Yoshi's [1.0.0.18 connector/warning post](https://ayalab.slack.com/archives/C0C8LFU0D0W/p1791995159532469) and [Stop metadata error](https://ayalab.slack.com/archives/C0C8LFU0D0W/p1791995367086299). His screenshots show active P1+P2 at 20 kHz, firmware advertising P1+P2+P3, and a Stop-time failure replacing metadata. The exact remote destination and Windows cause remain unconfirmed; the screenshot alone does not prove neural corruption or a valid complete recording.

V1 settings now initializes from the active profile and exposes direct probe checkboxes. P3 can be deselected without disabling a separate automatic-selection control. An advertised port is not proof of physical presence. Fixed legacy firmware remains supported, as do streaming/download settings paths added in 1.0.0.18. Advertised 20 kHz V1 modes no longer trigger the generic host experimental popup; two-probe 30 kHz retains its qualification warning. Firmware rate/mask admission and acquisition checks remain enforced.

## Persistence and Stop behavior

All three prior `File.Replace` paths—XML, settings/probe JSON, and recording bundle index—use one shared boundary-only writer. It creates a unique staged file beside the destination, flushes the completed bytes and replaces the destination with a Windows same-directory rename. It retries sharing/access/removal failures for at most one second per file. No delete-then-move or direct overwrite fallback is used. Persistent failures retain the old file and completed candidate, with exact destination and Win32 error in the exception. Failed partial serialization is cleaned up. These choices use the documented [MoveFileEx replacement flags](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw), replacing the separate [ReplaceFile transaction](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew).

Other sidecars are still attempted when one fails, including LFP XML when AP XML is locked. Failed candidates end in `.tmp` and remain excluded from the canonical bundle index. Keep the entire folder to preserve them. Neural DAT files are never passed to this writer.

V1 Stop drains processing, flushes/closes data and then saves metadata. A metadata-only failure now reports `Stopped; metadata warning: ...` rather than falsely declaring an acquisition-continuity failure. Real USB, decoder and flush errors retain `Capture error:`. The completion UI no longer repeats settings/probe/XML writes. V1/V2 reports include `metadata_save_error.txt` when it exists; failed index persistence also remains visible. A warning does not qualify the complete dataset: neural completion and metadata persistence must be assessed separately.

## Validation

| Check | Assertions |
| --- | ---: |
| Actual V1 settings, direct P1/P2 selection, no forced P3, 20 kHz warning policy and legacy profiles | 103 |
| Actual assemblies with real transient/persistent local file locks, old-file/candidate retention, serialization cleanup, concurrent saves, independent XML/JSON saves, Stop/UI ownership and unchanged DAT bytes | 22 |
| Physical geometry, merged/per-probe AP/LFP XML and unchanged samples | 107 |
| Actual recording bundles and metadata indexes | 203 |
| Actual V1 paired/legacy decoder, AP/LFP/DAT/timing/metadata | 74,176 |
| Actual V2 v11/v12 SD metadata/bridge, allocation bounds and DAT byte equivalence against the frozen 1.0.0.18 companion | 196 |
| Universal workflow and rendered V1/V2 dialogs | 106 |
| USB Stop/completion ownership | 134 |
| V1/V2 independent recording recovery | 125 |
| Folder labels, timestamps and no-overwrite behavior | 53 |
| Standalone protocol/catalog/UI checks | 294 |

The published 1.0.0.18 XML replacement path was reproduced with a local file-sharing lock (`HRESULT 0x80070020`) and a read-only destination (`0x80070005`). These validate the failing path, **not Yoshi's exact reported removal error or its remote cause**. Fixed-build tests cover transient lock recovery and persistent failure without modifying old metadata or neural data.

GUI and companion builds had zero warnings/errors. The direct-source SD fixture retains its existing unassigned-cancellation-field warning. All 13 installer runtime files were independently extracted and hash-verified; 11 match 1.0.0.18 byte for byte. Only GUI and shared metadata companion changed. The configurator and firmware updater are frozen. No hardware, firmware operation, discovery, physical disk access or installer execution occurred. Yoshi's hardware retest remains pending.

```powershell
.\test_metadata_persistence.ps1 -ConsoleExecutable <checked-GUI.exe>
.\test_v1_recording_profile.ps1 -ConsoleExecutable <checked-GUI.exe>
```

The metadata runner also supports `-LegacyReproducer` with the retained 1.0.0.18 GUI. Both new checks are in `test.ps1`.

## Release identity

The isolated build starts at `5b163e795e029dd5e4f1cf0c6c36112d3d24c3b6` with only the scoped metadata, connector/warning, report and version changes. Pending BLE cache, media-export and hardware-test changes are excluded. Source inputs, logs and package verification are retained in `_artifacts/v1_metadata_fix_20261014`.

- GUI SHA256: `34867999308EFCFCDD4CBA93CBB6100CB8F923F7A019E9F026EC638EE55E71EE`
- Shared companion SHA256: `15A59CC99E4F90973BE11D4E55D109242AEC8133746478345F7B78AF4EEBF72D`
- Installer SHA256: `BC2E48A5F7059DEDBBAA8812F029D7F2B19B42264E877BC8C92761A2BE36E619`
- Installer bytes: 2,316,527

No V1 firmware upgrade is required. No firmware image or new physical-throughput qualification is promoted by this release.
