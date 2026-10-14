# Folder naming and independent recording restarts — 2026-10-13

WILDX 1.0.0.17 adds a shared optional Session label to USB Mode and Data Download. Names follow `<label>_wild_yyyyMMdd_HHmmss.fff`. USB uses PC Start-attempt time; SD NP/media exports and NP1 fast archives use validated stored START clocks. Missing/invalid clocks produce an explicit `time_unknown` name instead of a download-time date. Blank labels retain default naming. Live camera saves inherit the USB label.

Per the owner's follow-up, every automatic restart now creates a separate timestamped recording directly under the output folder, instead of a segment subfolder. The data decoder, native admission, exact-UID selection, retry policy, limits, settings and probe maps remain. Each new folder contains its own recording/probe metadata; `recovery_link.json` links the preceding recording/error context and the first recording's journal. Gap duration and missing samples remain unknown. Files are never appended across restarts.

Fresh folder creation is exclusive. Exports claim a fresh path without precreating it, preserving strict exporter guards. Collisions receive numeric suffixes; all file-only checks preserve existing recording bytes. A concurrent Windows delete-pending claim race was found and fixed during verification.

Verification against the delivered GUI:

- 53 naming/time/UI checks: V1/V2 stored START brackets, tick wrap, media START and legacy clock, corrupt/missing/mismatched clock refusal, exact naming, path validation, simultaneous claims, 20 concurrent saves, no overwrite, synchronized label fields, and USB/download/archive renders.
- 125 targeted recovery checks: separate V1/V2 folders and prefixes, preserved data, linkage, exact UID/profile ownership, transport/overrun gates, unknown gaps, bounded retry and Stop.
- 134 USB UI lifecycle checks; 106 shared workflow checks; 294 standalone catalog/BLE/UI checks, including the minimum window layout.
- Final GUI build has zero warnings/errors. All 13 NSIS runtime files independently extracted and hash-verified; all 12 non-GUI files match 1.0.0.16. No helper/firmware build, hardware access, or installer execution.

Installer SHA256: `CC18A7781A95BCC6AECFA10B277EF4DBEE1A255A1E981925AF68400669573BFA`

GUI SHA256: `59A43F654202FB5E74033569F912CB0811DD11E779C13808A9A5B3F6DB41F8DD`

Evidence: `_artifacts/folder_naming_20261013`. Previous DAT-only/live-DAT and hardware qualification limits remain. Run `test_folder_naming.ps1` against the built GUI to reproduce the focused file-only checks.
