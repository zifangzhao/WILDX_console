# Current installer — WILDX 1.0.0.9, 2026-10-12

[Install WILDX 1.0.0.9](WILDX_console-Setup-1.0.0.9.exe).

NP2 output now undoes the ASIC's inverting amplifier: `2048 - raw12`.
USB recording and SD export save signed nominal counts (-2047..2048),
sample-major little-endian int16, in unchanged384-channel hardware order.
USB preview is centered at zero; BLE preview applies the same PC-only sign
conversion. RAW transport/capture bytes remain unchanged. This is not gain
calibration or a microvolt conversion. Existing NP2 DAT files from before9
remain unsigned raw codes; metadata identifies the new encoding.

The actual built GUI/exporter passed1,827,449 focused software checks:
all4096 ADC codes, all384 channels/all3 ports, packed3/6, PSSI8/split9,
15/20/30k offline replay, USB DAT/RAW/preview, SD export/metadata and BLE plot.
The old GUI fails the new gate. No hardware capture, installer execution,
independent extraction or new full firmware/GUI pair qualification this cycle.
Triple30k USB remains unsupported for live capture.

Firmware is unchanged: recovery6408DBB8 / USB A33B9912 / published APP ED881256.
ROM DFU controls and updater are retained unchanged. Earlier004B direct/combined
ROM enumeration/USB-return validation passed with private APP6A74E6AE preserved;
that is not qualification of the published fused APP. Eleven other runtime
files remain identical. The installer was built once and copied here.

See [manifest](release-manifest.json), [checksum](SHA256SUMS.txt),
[previous8](legacy/20261012_before_np2_polarity_1.0.0.9/README.md) and
[polarity evidence](https://github.com/zifangzhao/WILDX_console/blob/main/Note/NP2_POLARITY_20261012.md).
