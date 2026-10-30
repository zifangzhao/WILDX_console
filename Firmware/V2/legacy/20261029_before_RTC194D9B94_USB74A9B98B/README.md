# V2 checked SD first-sample release — WILD-X189

APP **46DF5009**, USB **62910614**, retained recovery **6408DBB8**.
Use the fused HEX for a full image, or the matching component update files.
Retained device configuration is excluded. Exact checked seals/bodies are preserved; no rebuild, new hardware test or fused reflash occurred during packaging.

Validated one 30-second P1+P2+P3/20kHz SD run on Delkin V90: full 1092116480-byte readback, 23440 chunks, 600063 DAT rows/probe, all five reported loss counters zero. Saved IMU 5780 AG/2890 MAG observations, observed sequence gaps zero; not every physical conversion. Independent local RTC-to-first-NP full interval120us; pre-record PC calibration generation2 confirmed and bound. PC declared setup uncertainty20594us and separate stopped readback phase interval[-36669,+41237]us are not the120us local interval or a verified absolute PC-error guarantee. R6 USB acquisition/USB IMU physical recording remains untested. Normal SD-to-USB2/download3/full export passed. No all-mode/card/endurance/analog-quality or arbitrary ADC-bit-checksum guarantee. Whole fused image not newly flashed; exact checked components merged without rebuild. Lab originals restored after test.

See acceptance.json, package-receipt.json and source-manifest.json for exact evidence and bounds.
