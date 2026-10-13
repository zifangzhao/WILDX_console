# WILDX 1.0.0.13 - 2026-10-13

[Download the installer](WILDX_console-Setup-1.0.0.13.exe).

Capture activity now wraps the recording status and output file link. Its rows
and card grow to display the full text, including long paths and multiline
messages, then shrink again for short status messages. Resizing and DPI/font
changes refresh the layout while retaining the live monitor area.

The exact app passed 60 focused offscreen layout checks at normal/minimum
window sizes and 100%, 150% and 200% scaling, plus the existing 294 standalone
checks. The final GUI build had zero warnings/errors. All 13 installer runtime
files were independently extracted and verified; the twelve non-GUI files
match the previous SR/IMU package.

The previously released V2 sensor behavior and firmware baseline are retained:
recovery 6408DBB8 / resident USB BFF0A8A2 / logger E07FB882. Prior short USB
evidence covers accelerometer/gyro only; magnetometer remained unavailable
(error 9). This layout release performs no device test or firmware change.
Endurance, damaged-shank, all-rate and clean installation qualification remain
pending. The prior SR/IMU installer is retained under `legacy`.

[Validation and layout preview](../Note/RECORDING_INFO_LAYOUT_20261013.md),
[release manifest](release-manifest.json) and [checksums](SHA256SUMS.txt).
