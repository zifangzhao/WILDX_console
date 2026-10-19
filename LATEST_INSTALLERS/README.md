# WILDX — high-speed USB only

The forced full-speed **V1 store2 rc1 / USB FACEA0D2** package is withdrawn.
Full-speed USB is not an accepted workaround for the current hardware.

The [previous exact 1.0.0.44 installer](WILDX_console-Setup-1.0.0.44.exe) is restored
without rebuilding. It bundles APP **FA2016E7**, high-speed USB **02FF93A9** and
its original matching fused image. It is **not a high-speed replacement for the
new FS2/store2 recording pipeline**. Do not downgrade firmware or format existing
FS2 media to use this older package; preserve those recordings.

Large high-speed downloads still have documented Windows31 failures. The speed
policy change does not establish a fix or new qualification. A matching
high-speed store2 replacement is pending coordination with the V1 developer.
V1Camera and V2 remain unchanged. No device was flashed, reset or formatted.

See [USB policy and withdrawal](../Note/V1_HIGH_SPEED_USB_POLICY_20261019.md),
[manifest](release-manifest.json), [checksums](SHA256SUMS.txt), and the
[withdrawn package history](legacy/20261019_withdrawn_full_speed_store2_rc1/WITHDRAWN.md).
This root contains one installer. Setup retains **Run WILD-X**.
