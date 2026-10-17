# WILDX 1.0.0.35 — explicit V1 firmware targets

**Advanced → Firmware Update → Firmware target** now offers:

| Choice | APP | USB | Selection |
| --- | --- | --- | --- |
| V1 NP (91k/91k) | 1.0.121-idle-sleep-test / BC6B506E | USB91 / 40D39075 | Default |
| V1 Camera40 — candidate | 1.0.121-camera40-default-candidate / 99BA4F27 | USB83 / 82C6E21A | Explicit user choice |

Use bundled APP resolves the selected target's checked APP at the time of the
click. It displays the target, APP version/CRC and limits, then retains the
existing exact-UID/physical-target confirmation and protected APP updater.
Changing targets does not flash. A target change during confirmation rejects the
pending selection; changing device UID resets the choice to NP. Shared version
1.0.121, ABI258 and radio identity never classify the target.

The camera candidate warning remains visible, including the prior strict USB
completeness failure with 11 camera drops. The existing target metadata and all
firmware images are unchanged. Only the GUI, version and camera instructions
changed in the runtime payload. Capture, SD storage, transport/readiness and
updater protocols were not modified.

One incremental GUI build and one focused actual-dialog/handler regression passed
34 checks: defaults/labels, exact target-to-APP routing, repeated switching,
cancel, stale-confirmation refusal, UID/busy/status gates, no auto-flash and
visible limits at minimum size. Confirmation and APP application were replaced
with spies; the device form's Shown/discovery handlers never ran. The actual
control [render](../docs/screenshots/v1-firmware-target.png) was inspected.
No broad suite, firmware build, hardware test, flash or card action was run.

GUI SHA256: 7CEBBDDDEEFDBDF0287E692657942DE3B73B3E7D6525C79E1D5EA48D89A0AD39.
Unchanged exporter SHA256: A04D630F1EA716FD3EC4D3DE54C3875479315B6647F4B4A3F4C60C26BB519070.
Installer SHA256: 3F92302140CAF243F21CBDEA0B37E6206F6A1B6683930E77F93C2C4529FA7DB8.
Independent extraction verified all 37 files. Thirty-five inherited files,
including both helpers, exporter and all ten firmware images, match 1.0.0.34
exactly. One final installer remains in LATEST_INSTALLERS.

The [1.0.0.34 evidence and limits](RELEASE_1.0.0.34_20261017.md) remain applicable;
this GUI change adds no whole-pair, endurance, implant or lossless qualification.
V1 camera40 is a separate board candidate, not a successor to the NP bundle.
The experimental download-readiness source remains excluded and local.
