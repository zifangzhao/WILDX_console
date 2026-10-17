# WILDX 1.0.0.36

[Download the installer](WILDX_console-Setup-1.0.0.36.exe).

BLE Recording settings now uses the USB-style probe checkboxes and AP sample
rate, with probe configuration inside the dialog. Start applies changed settings
and waits for their device confirmation, removing the separate Apply step.
Camera/microphone selection remains available where advertised by V1 firmware.

Stop can be requested during startup work. It cancels a pending host start,
waits for an existing device command, and confirms an inactive recorder using
fresh status. A finalized V1 state4/fault2 is displayed as stopped after a
capture-source failure, rather than waiting for Complete. Active/pending flags
must clear before stopping is confirmed. Lost mutation replies are not replayed.

These console changes require no firmware upgrade. All 36 inherited runtime,
helper and firmware files are unchanged; all 37 extracted payload files match
the checked build. Run WILD-X remains available after installation.

The underlying capture-source fault on the two reported V1 loggers has not been
reproduced or fixed by this release. See [validation and limits](../Note/RELEASE_1.0.0.36_20261017.md),
the [manifest](release-manifest.json) and [checksums](SHA256SUMS.txt).

This root contains one final installer. The preceding release and original
metadata are [archived here](legacy/20261017_before_1.0.0.36/README.md).
