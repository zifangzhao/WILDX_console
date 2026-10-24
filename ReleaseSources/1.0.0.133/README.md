# WILD-X 1.0.0.133

Exact software-checked build133 and reused SDK115 inputs are retained in compiled-sources.zip and source-inputs.json. Packaging did not recompile the application. Unrelated working-tree changes are excluded.

Since128: coherent BLE IMU snapshots with compatible firmware; retries after temporary failures; IMU priority above optional clock observations and waveform requests; serialized background timer callbacks; stale IMU display after3s without a fresh observation; read-only periodic raw MCU/PC clock evidence for V1/V2. Clock evidence collection does not implement AP drift correction. Existing NP recording workers and decoders are unchanged.

Existing firmware remains supported. Bundled firmware, helpers, SDK and all payload files except the GUI are byte-identical to128. The optional high-resolution USB timing images and private V2 IMU APP CBAAA2B5 are not bundled. Older firmware without BLE IMU support will not gain it from a console update alone.

Validation:5 checks reproduce the prior overlap;22 actual-source background scheduling/freshness checks, 5 compiled USB IMU lifecycle checks,366 compiled BLE IMU checks and43 compiled clock-evidence checks pass. Earlier preview regression checks also passed; see retained receipts. All 43 installer payload files were extracted and hash-verified. The installer was not executed.

Earlier V2 firmware129 HIL received6 valid coherent IMU views while recording P123 at20kHz and finalized session292 with fault/error0. This is not lossless200Hz IMU transport. Full independent SD292 NP readback remains pending physical V2 access; simultaneous waveform/IMU operation and physical NP/GPIO alignment remain unqualified. The133 scheduling changes have software checks only. No hardware was accessed during packaging.
