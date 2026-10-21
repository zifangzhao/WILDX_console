# WILDX installer — 1.0.0.63

Download `WILDX_console-Setup-1.0.0.63.exe`.

Includes compact V2 BLE waveform preview, healthy-recording admission and bounded
calibration-pressure retries, plus camera row-alignment recovery with the adjusted
color and contrast. Bundles V2 APP15FAE47B, USBE323A06A and recovery6408DBB8 with
matching fused/component/update files. V1 and camera firmware retain their prior
bytes. Installing the console does not flash a logger.

The exact app/firmware pair passed bounded dual15k BLE preview and stopped SD
readback. Dual20k failed source overwrite; triple/P3 preview and other untested
modes remain unqualified. See Note/RELEASE_1.0.0.63_20261020.md and
`release-manifest.json` for checks, hashes and limits. All 44 extracted payload
hashes match. Earlier installers are in `legacy`.

GUI64 V1 recovery changes and the V2 IMU startup investigation are separate and
are not included in this tested pair.
