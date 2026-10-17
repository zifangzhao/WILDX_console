# WILDX 1.0.0.42

SD downloads now name each recording folder from its stored recording START
timestamp, including when Session label is blank. This applies to V1/V2 neural
and camera/media downloads through USB or a card reader, and NP1 fast archives.

An unlabeled folder is `wild_yyyyMMdd_HHmmss.fff`; a session label adds
`<label>_` before it. The stored UTC timestamp is converted to the PC's local
time zone. Missing, invalid or unsynchronized timestamps use
`[<label>_]wild_time_unknown_<session-and-LBA>` instead of the download time.
Existing destinations are preserved with numeric suffixes for repeated names.
Live USB folder naming and saved recording/probe metadata are unchanged.

Validation: 62 actual-app naming/time/UI checks cover stored V1/V2/media clocks,
missing/invalid timestamps, labels and collision/concurrency/no-overwrite.
Another 139 actual-app recovery checks passed. The recovery fixture now
initializes the current USB mode controls when bypassing the main constructor.
GUI compilation and all 40 independently extracted installer payload files
passed. No device recording or download was performed.

Only the GUI payload changed from 1.0.0.41. Exporter, runtime helpers and bundled
firmware are identical. The pending V1 catalog-recovery host experiment and
unrelated USB readiness experiment are excluded. No firmware update is required.
