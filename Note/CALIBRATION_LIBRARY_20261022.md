# WILD-X1.0.0.82 — calibration folder and SN/IMRO lookup

The default Calibration page now asks for one factory folder. Browse searches
it immediately; later opens search the remembered folder. Both flat folders
and nested serial-number folders are supported. CSV contents supply the serial
and format, so renaming a file does not break matching. Identical duplicates
are accepted; conflicting files for the same SN are rejected. Factory files
are not moved or modified. Unrelated CSVs are reported/ignored.

The three optional recording/legacy rows show SN, IMRO and a read-only match
summary. SN can be selected from the indexed factory probes. Current USB/BLE
recording configuration is reused for gains; From recording imports saved SNs
and IMRO paths. The default workflow has no ADC/gain CSV selectors or gain
entry boxes. Manual setup retains the previous explicit fallback UI.
Use for recording sets immutable calibration profiles for future capture or
preview, without staging or changing the hardware probe configuration. Selected
IMRO bytes are retained beside live calibration files.

Calibrate folders works directly from the factory index; per-port profiles
do not need to be loaded first. Each recording resolves its own physical probe
SN and NP1 gain pair from saved channel settings or a declared recording IMRO.
The index is not limited to three serials across a recursive batch. A standalone
probe_info.json plus IMRO is also usable for per-probe DATs. NP2 gain lookup
does not require NP1 ADC/gain settings. Matched IMROs and their hashes are added
to correction provenance alongside exact factory CSV snapshots. Different
recording gains produce different immutable profiles even for the same probe.

Existing merged order/encoding, resampling, missing-row, already-corrected and
atomic overwrite protections remain. A changed factory folder invalidates old
matches. The scan never invents a missing historical SN or merged channel order.
Legacy recordings without SN need one explicit per-port assignment. IMRO does
not replace the factory calibration CSVs. Known unverified requested IMROs are
not silently promoted to an applied device receipt; explicit manual profiles
remain available when historical acquisition details are unavailable. Existing
mixed NP1 channel gains and nonuniform960-site calibration limitations remain
explicit skips, not guessed corrections.

Read-only validation of the provided factory directory found27 probe serials:
12 NP1 ADC/gain pairs and15 NP2 gain files (39 CSVs). All profiles loaded and
all39 file hashes remained unchanged. No private calibration files are shipped.

Validation on the final82 binary:145 folder/SN/IMRO/recursive/UI checks,
15,060 recursive calibration/overwrite regressions,139 recording probe info,
layout/native SD checks and22 metadata persistence checks (15,366 total).
The actual calibration page was rendered/inspected at850,1000 and1250 widths.
Only synthetic DATs were calibrated. No hardware, BLE radio, firmware/card
mutation, user recording conversion or installer execution was performed.
SDK, firmware and helpers are byte-identical to release78. Intermediate
compilations79–81 are not published.
