# Unverified V1 high-speed / FS2 fused image

Published at the user's explicit request, **unverified**. Contains frozen NP/FS2
APP **654824DE**, existing high-speed USB **02FF93A9** and ABI258 recovery.
Forced full-speed FACEA0D2 and the APP60EC609E power trial are excluded.

[Download the fused HEX](V1_fused_BOOT_USB02FF93A9_APP654824DE_HS_UNVERIFIED_20261019.hex).
The matching APP-only, USB-only and boot+USB HEX files are alongside it.
See [exact identities and limits](manifest.json) and [checksums](SHA256SUMS.txt).

One direct merge preserved the frozen component bytes, checked input ownership,
overlap, manifests, vectors and bounds, and excluded retained configuration at
0x0801C000..0x0801DFFF. The fused HEX uses LF line endings. **Do not mass erase.**
No firmware rebuild, manifest reseal, hardware test or flashing was performed.

This is not a stable matched-stack release. High-speed large-download Windows31
failures remain unresolved. USB probe-streaming qualification for this exact
tuple, physical power-removal recovery and the <=30s lost-tail target remain
unverified. Existing short P12/20k SD/BLE and strict SWD-readback evidence does
not establish sustained high-speed USB downloading or all-mode/camera readiness.

The intended existing console is r7/1.0.0.44, EXE SHA256
0BF2DBDC3A9D2407620EF1DB1D7C7270A33793FCA67CF8C9655048358B126DAA.
No matching installer is created by this publication. The current installer
and stable firmware catalog are unchanged. Fused/service HEX files are not
APP-only updater inputs.
