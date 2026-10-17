# Current installer — WILDX 1.0.0.11, 2026-10-12

[Install WILDX 1.0.0.11](WILDX_console-Setup-1.0.0.11.exe).

The NP1/NP2 USB preview now has a **Display HPF** selector: **Off** (default),
**1, 10, 100 or 300 Hz**. BLE preview offers **Off, 1, 10 or 100 Hz** for its
500-sample/s display. It is a first-order, causal PC display filter, not an
ASIC setting or a zero-phase analysis filter. For low-frequency viewing leave
it Off, or use a low cutoff; 300 Hz deliberately suppresses low-frequency content.

Filtering runs only on display-owned samples. USB processing is on the existing
below-normal renderer, not the capture/logger workers. AP and LFP use their
actual sample rates; changing page/probe/band/cutoff or encountering a gap resets
the filter. Saved DAT, RAW and BLE packets are not changed. No firmware, SD
exporter, DFU updater or configuration protocol changed.

The actual GUI passed 3,875 focused filter/ownership/control checks plus 10
offscreen UI checks. Cutoff response, DC rejection, signs, independent channel
state, mapped AP/LFP channels, byte-exact Off path, untouched recording inputs,
BLE invalid-row gaps and resets were checked. The rendered NP2 control was
visually inspected without a device connection. No new recording qualification,
installer execution or hardware test was performed.

NP2 merge and gap-aware USB recovery from 1.0.0.10 are retained unchanged:
merge runs after Stop, retains per-probe sources and never joins recovery gaps.
The signed NP2 convention from 1.0.0.9 (`2048 - raw12`) is also unchanged.
Triple 30 kHz USB remains unsupported.

Source/evidence: `Note/NP2_MERGE_RECOVERY_20261012.md`, follow-up Display HPF
section, in the canonical WILDX repository. This package is local, not yet pushed.
