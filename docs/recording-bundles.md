# Recording files stay together

Each USB recording, recovery segment and downloaded session saves its data and metadata in one recording folder. `recording_files.json` lists the files using relative paths. Keep the folder and its subfolders together.

The bundle includes recording settings, rates and channel layout, per-probe JSON, available IMRO/configuration files, exact requested/applied binary selections, device receipts, calibration manifest and factory CSVs, clock/timing records and completion/error reports. Metadata checkpoints run before capture and after completion. They do not change the neural RAW/DAT format or add work to sample callbacks.

Separate-probe DAT folders also receive the probe files, shared metadata and calibration CSVs. Their JSON/XML retains a single-probe layout. Shared bulk sensor/camera data stays at the recording root with explicit relative references. A recovery session's root index includes its journals and all separate segments; gaps remain marked.

SD downloads preserve exact descriptor, footer and parameters bytes. V2 downloads preserve `configuration.bin` and use the recorded probe mask, including probe 3. Dual-probe full binary requests are saved as `probe_N.sd-selection.n2b1`; triple-probe ID/CRC fingerprints are retained in the probe JSON. Stored requests/fingerprints are not treated as applied-device receipts. SD formats that omit original IMRO or full maps explicitly mark that information unavailable; current GUI settings are never substituted for historical settings.

Raw-download folders retain their archive, transport receipt, descriptor/footer/parameters and recording/probe JSON together. Corrected DAT files retain adjacent calibration provenance and an `.source-metadata` folder containing the original acquisition/probe context. That source context describes the original recording; the corrected file's `.calibration.json` describes its new sample encoding.

`test_recording_bundle.ps1` checks merged/separate V1/V2 bundles, exact binary and sidecar copies, calibration-file portability, disabled misc metadata, incomplete recordings, corrected-output source metadata and actual dual/triple SD export. Existing metadata, Neuroscope XML, merge and native SD sample/byte checks cover the same paths without hardware access.
