# V1 NP identity-parity candidate

APP122 `8FD89229` + unchanged USB91 `40D39075`, with the current recovery/bootloader.
For V1 Neuropixels only; keep the camera target and V2 separate.

The fused HEX is for SWD programming without mass erase. It excludes retained
configuration. The APP-only HEX is supplied for component programming; do not
use the fused HEX in an APP-only updater.

This corrects NP1 part-code admission parity with USB while retaining EEPROM-less
NRIC support. Capture clocks, DMA/rings, SD scheduling and UART are unchanged.

Canonical identity/AUTO software checks and independent installed APP readback
passed. Boot/configuration and USB service were byte-identical after APP flashing.
BLE logging, custom-map application on the failing recorder, saved-data integrity
and fused-image programming have **not** passed hardware validation. The local
BLE attempts stopped before connection; no START command or new recording occurred.
Published at the user's request for parallel validation, not as an end-to-end
qualified firmware/console pair.
