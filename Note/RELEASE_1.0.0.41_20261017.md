# WILDX 1.0.0.41

V1/V2 BLE neural previews now use a fixed, zero-centered Y scale with a manual
**Gain (×)** control, matching the existing USB preview workflow. Gain ranges
from 0.1× to 100×, initially 1×. Increasing gain enlarges the trace; the displayed
count range updates accordingly. At 1× the half-range is 64 nominal counts for
NP1 and 256 for NP2. Factory-corrected data remains labeled in calibrated
counts, not microvolts.

Signal amplitude, DC offset and outliers no longer automatically zoom or
recenter the graph. Out-of-range traces are clipped inside the plot. Changing
gain retains display history and the BLE preview lease; channel/probe/window
selection retains the gain setting. This is display processing only: it does
not change hardware gain, recording data, calibration or firmware settings.
No firmware upgrade is required for the gain control.

Validation includes 36 actual-app gain/DC/clip/history/selection/layout checks,
5,507 actual-source preview protocol/display/lease checks and 4,397 actual-app
BLE workflow/framing/catalog/UI checks. GUI builds and independent
installer payload extraction passed. No hardware recording was performed.
All runtime, exporter and firmware payloads except the GUI are inherited
unchanged from 1.0.0.40. The experimental V1 catalog-recovery host slice and
unrelated USB readiness experiment remain excluded.
