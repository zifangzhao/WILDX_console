# WILD-X 1.0.0.78

[Installer](WILDX_console-Setup-1.0.0.78.exe).

Aligned USB device/output fields, measured wrapped action/status rows, and
drawn GPIO indicators prevent the reported alignment and clipping problems.
Probe information is saved in probe_info.json beside recording metadata.
V1 USB uses optional post-Stop EEPROM receipts; V2 USB and SD use verified
per-port native header identities. BLE retains host settings/request/IMRO
receipts under LocalAppData/WILDX/RecordingMetadata; the BLE report shows its path.

Older firmware stays usable. Existing BLE firmware and historical V1 SD
formats do not expose per-probe serials; those fields remain explicitly
unavailable. A host journal is not silently bound to an unrelated old recording.
Firmware/helpers and data decoding are unchanged from76.

15,221 software checks passed, including native SD DAT byte comparisons,
legacy fallbacks, BLE receipt lifecycle, calibration serial remapping, metadata
failure handling and offscreen layout rendering. No hardware or installed-pair
qualification; the installer was extracted/hash-verified, not executed.

[Exact manifest](release-manifest.json) / [behavior and limits](../Note/PROBE_INFO_LAYOUT_20261022.md)
/ [exact compiler inputs](../ReleaseSources/1.0.0.78/README.md).
Previous76 is retained under legacy/20261022_before_1.0.0.78.
