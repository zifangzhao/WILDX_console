# V1 NP fused candidate — corrected packaging revision 2

Use the `_r2.hex` file. The earlier sparse fused APP122 file is rejected as
partial by WILDX and is superseded; it is not a supported WILDX update input.

This revision explicitly represents unused firmware flash as erased `FF`,
covering all 2 MiB except the protected 8 KiB configuration page. It preserves
the exact checked bootloader, APP122 `8FD89229` and USB91 `40D39075` bytes.
No firmware was rebuilt. Integrity guards were not relaxed.

V1 NP only, not camera or V2. Do not mass erase or change option bytes.
The earlier BLE logging/custom-map/data-integrity and fused-programming
hardware-validation limits still apply.
