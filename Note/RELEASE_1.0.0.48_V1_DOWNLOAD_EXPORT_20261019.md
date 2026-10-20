# WILDX 1.0.0.48 — V1 download/export decoder

This release reuses the exact software-checked 48 executable, SHA256
`CB392725EE11C8A607D6FECAA013ADC6383BA419C4FAA1046CBB838A366B5486`.
The other 47 runtime/firmware payload files are unchanged from release 47;
the exporter DLL remains `A396530AAD81A5C8BA6FDC8086FC1FB2642A2E4E34C52E50392AE8763FE32A2F`.
No application, dependency or firmware rebuild accompanies packaging.

V1 SD download/export now uses a bulk byte buffer and branchless NP1 ADC-word
unpacking. Startup, sync/fixed/fill/heartbeat/counter integrity and LFP-phase
checks remain unchanged. This does not alter USB streaming or the USB protocol.

Existing file-only checks: 3,101 differential cases passed against the frozen
decoder, including three layouts, feed boundaries and corrupt-input rejection.
Both saved probe streams retain exact sample hashes. The complete saved
P1+P2/20 kHz export produced 21 byte-identical files; recording settings differ
only in software version and export metadata creation time.

Median decoder time improved 1.4114 to 0.2697 seconds (5.23x). One bounded
full-export run improved 1.8732 to 0.6739 seconds (2.78x), using a memory-backed
saved card and ordinary output-file flush/close. These are not USB transfer
rates or durable disk-cache flush measurements.

The reported live 11 MB/s download remains unresolved, including a user report
on a 48 candidate. An earlier 43.18 MB/s raw V1 HS test is separate historical
evidence, not a new 48 end-to-end result. No new installed-pair, streaming,
SD recording, endurance or physical-device qualification is claimed.

V1 APP654824DE / HS USB0E542BBD and matching component/fused images, V1 camera,
V2 APPB60291EE / USBE323A06A / recovery6408DBB8, and their existing limits are
unchanged. Firmware selection/programming remains explicit. V1 USB catalog
repair firmware and new unbuilt Stream/Form timing work are not included.

Only the checked decoder/version/test delta is published from application
source. Pre-existing private GUI context in the checked binary is retained;
a complete old 47 compiler-input ledger was unavailable, so clean-public-HEAD
binary reconstruction is not claimed.

Packaging is one NSIS pass followed by exact independent extraction checks
against the frozen 48-file payload. The release manifest records identities,
scope and packaging results. The prior installer remains in
`LATEST_INSTALLERS/legacy/20261019_before_download_export_48/`.

The Finish → Run checkbox now uses Windows shell launch to honor the app's
administrator manifest. The console developer checked the actual preprocessed
MUI callback with three isolated dummy-payload cases; real WILD-X/UAC was not
executed. [Installer-launch scope](INSTALLER_FINISH_LAUNCH_FIX_20261019.md).

Installer SHA256: `33B335DB99E83A601FCAD2B97F2131DA7048BB9FE03D804C6732EE217704AD78`.
