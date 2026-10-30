# V2 recording firmware

APP 194D9B94 / USB 74A9B98B / recovery 6408DBB8.

Checked with WILD-X 1.0.0.199 at three probes, 20 kHz: USB 60 seconds and BLE-to-SD 30 seconds with complete bounded readback. Device-local RTC-to-first-NP intervals were 120 and 121 microseconds. NP loss counters were zero; IMU observations were saved. PC synchronization uncertainty is separate.

The ordinary IMU path is enabled. The experimental hardware grid is disabled and is not qualified. This is a scoped update of the published R6 baseline, with no all-mode, endurance, or EC5 equivalence claim. Optional catalog inspection failed; normal listing/download/export passed. See acceptance.json for exact results and limits.

The fused HEX merges the exact checked components without rebuilding or including device configuration. Earlier deliverables are retained under legacy.
