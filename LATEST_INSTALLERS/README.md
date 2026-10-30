# WILD-X1.0.0.221

USB Mode now includes **SD card test** without BLE. Select the connected probes and 10-120 seconds per rate (default 60). The test starts at the highest applicable rate and keeps the probe selection fixed, stopping at the first rate that passes full saved-data readback.

Results show SD write/service pauses and saved NP gap statistics when available: count, maximum, mean, total missing time, p95 and p99. V1 exposes loss-event/skipped-byte lower bounds and longest write time; exact NP gap durations remain unavailable. Statistics opens the full detail. PC card-reader testing remains available in its own tab.

Install the matching full bundled APP and USB firmware before using the USB logger test. Existing firmware remains supported for ordinary recording and download. Test recordings stay on SD; reports go to the selected PC folder. Cancel waits for an accepted finite recording to finish and prevents the next rate.

Development checks used V1 with two probes and V2 with three probes, 10 seconds per rate. Both verified loss at 30 kHz, automatically tried 20 kHz, and passed full readback with scheduled IMU data. Earlier recordings were preserved, and both devices were restored to their original firmware/options/profiles.

Qualification covers this short warm-handoff workflow, selected cards and probe combinations. V1 interrupted profile recovery was fixed and checked in native tests; physical cold interruption, full IMU source-window/final-tail coverage, physical sensor conversion phase and endurance remain unqualified. V1 camera firmware is unchanged.
