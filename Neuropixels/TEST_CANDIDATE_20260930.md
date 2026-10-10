# USB console NP test candidate — 1.0.3.3

The current repository-owned NP sources include the shared NP2 SD DAT exporter,
NP presence preflight and explicit optional-sensor validity. Missing/failed
IMU and magnetometer channels use unavailable sentinels in the fixed 16-word
CE64-compatible 1,250 Hz analogin file, not stale plausible values. Download
pipe reset is restricted to a new idle session, never active recording.

Release build and software tests passed: 402,295 NP1 parser/profile checks,
1,032,004 NP2 wire/status checks, 470,064 exact SD DAT checks against the built
console, 22,957 configurator assertions, and actual-assembly mixed packet,
analogin layout/clock-wrap, disabled-sensor and corruption tests. These tests
do not qualify continuous acquisition on real probes.

The installer now includes NpV2SdDatExport.dll as well as the configurator.
NSIS accepts APP_PAYLOAD_DIR and OUTPUT_FILE for isolated builds, and an
optional NP_FIRMWARE_DIR for a matched V1 test firmware bundle. Installing the
console does not flash hardware. The bundle's .xenp package updates only the
app; its full factory BIN is for blank boards, never provisioned loggers.
