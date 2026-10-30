# V1 recording firmware

APP87109990 / USB9D595E4D. Bootloader bytes are unchanged (ABI258).

Checked with WILD-X1.0.0.199 at two probes/20kHz: USB60seconds and BLE-to-SD30seconds with960MiB readback. Both local RTC-to-first-NP intervals were111microseconds. NP continuity and saved IMU checks passed; one initial IMU slot is unavailable. PC uncertainty is separate.

See acceptance.json and source-manifest.json for exact identities and scoped limits. No full phase, final-tail, endurance or all-profile claim. The fused HEX merges exact checked components without rebuilding or including configuration pages. Earlier deliverables are retained under legacy.
