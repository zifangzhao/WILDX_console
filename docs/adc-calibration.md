# Factory ADC/gain correction

The host pipeline applies factory-file corrections after native decoding and before DAT writing, resampling and display filtering. It preserves sample order, rates, timestamps and the existing nominal polarity. Offline source archives are kept. USB app recordings save decoded DAT without an additional native RAW file. It is opt-in; a recording without a loaded profile follows its existing decoding/output path.

## Use

1. Open **USB Mode → Calibration…**, select the physical connector and enter its probe serial number. Load its factory `*_gainCalValues.csv`; for NP1, optionally load the matching `*_ADCCalibration.csv` and select the AP/LFP gains actually used during acquisition. NP2 file type is detected from its gain CSV; it does not need NP1 ADC/gain controls.
2. The next USB recording snapshots the selected profiles. Corrected samples feed both DAT and preview, while native RAW stays unchanged. Profiles are bound to the selected USB peer and connector; selections last for the app session. Recorded copies remain available after the app closes.
3. **BLE Mode → Calibration…** uses a separate exact-peer selection. Reopen NP Preview after changing its calibration. This corrects the host display; it does not alter onboard SD data or issue a recorder command.
4. **Data Download → Calibration…** loads explicit offline profiles. Native NP1/NP2 SD exports then use those profiles. **Correct DAT…** processes an existing DAT into a fresh file. Specify AP/LFP, signed nominal or legacy unsigned input, and the comma-separated physical probe order. A per-probe DAT uses one probe; merged DAT uses its actual contiguous 384-channel probe order.

Probe serials and acquired NP1 gains are operator-supplied bindings. File serials must agree with those bindings and with each other. This does not attest physical probe identity or gain settings through firmware.

Only connectors with a loaded profile receive correction. Other active connectors keep nominal counts; the archived per-probe entries identify the calibrated connectors. An empty selection keeps the existing DAT and receipts unchanged.

## Supported factory data

- NP2 gain: serial on the first line and 384 ordered `channel,factor` rows.
- NP1 gain: serial, followed by 384 rows with eight AP and eight LFP factors. Gain-column order is 50, 125, 250, 500, 1000, 1500, 2000, 3000. Uniform 960-electrode tables are also supported, excluding factory reference factors of 1 when determining the shared factor. Nonuniform electrode tables require confirmed channel-to-site routing and are refused rather than guessed.
- NP1 ADC: serial and 32 ordered rows containing ADC index, CompP, CompN, Slope, Coarse, Fine, Cfix, Offset and Threshold. All fields are validated and the original CSV is retained. The host applies the digital operation `raw > threshold ? raw - offset : raw`, then the existing `512 - corrected_raw` polarity and the selected band gain. ADC assignment uses the hardware output-channel mapping.

NP2 gain multiplies the existing `2048 - raw12` nominal count. Corrections use precomputed shared lookup tables, nearest rounding with ties away from zero, and signed little-endian int16 output. No clipping or microvolt conversion is performed. A profile that would overflow int16 is rejected when loaded.

The six NP1 comparator/bias trim fields require probe register programming. This implementation imports, validates and archives them; it does not program those registers or repair their historical effects in an old recording. Its provenance explicitly records that hardware trims were not written by this host pipeline and that acquisition trim state is unverified.

Primary format/application references: [Open Ephys NP1 factory parser](https://github.com/open-ephys/bonsai-onix1/blob/main/OpenEphys.Onix1/NeuropixelsV1Helper.cs), [NP1 digital ADC correction](https://github.com/open-ephys/bonsai-onix1/blob/main/OpenEphys.Onix1/NeuropixelsV1eDataFrame.cs), [NP2 gain parser](https://github.com/open-ephys/bonsai-onix1/blob/main/OpenEphys.Onix1/NeuropixelsV2Helper.cs), [hardware ADC trim configuration](https://github.com/open-ephys/bonsai-onix1/blob/main/OpenEphys.Onix1/NeuropixelsV1.cs). These were inspected; no external SDK or source dependency was added.

## Output and safeguards

Recordings and native downloads contain `adc_calibration.json` plus byte-exact CSV copies under `calibration/`. The snapshot records per-probe serial, selected gain columns, file SHA-256, output encoding, rounding, processing stage and hardware-trim limits. Active processing uses immutable loaded profiles, not files that can change during a capture.

Offline DAT correction preserves its input and creates `<output>.partial` until processing succeeds. It saves `<output>.calibration.json` with the input SHA-256, encoding, band, probe order and factory hashes, plus a complete input snapshot under `<output>.calibration/`. Only a successful pass promotes the final DAT. Existing files are never overwritten.

Known calibration provenance prevents double correction. A copied DAT without its metadata cannot be identified as already corrected from sample values alone; input encoding/order remain explicit operator declarations. Resampled AP with known provenance is refused because post-resampling correction cannot reproduce the online operation; use its native RAW decoder instead. NP2 LFP correction, invalid serials, missing/duplicate rows, malformed/nonfinite coefficients, out-of-range trim parameters, truncation and changed snapshot hashes are refused.

## Verification

Run the retained focused check against a build containing this feature:

```powershell
.\test_adc_calibration.ps1 -ConsoleExecutable .\_artifacts\adc_calibration_20261012\bin\WILDX_console.exe
.\Neuropixels\test_np1_polarity.ps1 -OutputDirectory .\_artifacts\adc_calibration_20261012\np1-polarity
```

The focused check compiles only the existing native block/image fixtures and tests the actual component. It covers all NP2 codes, NP1 ADC routing and threshold boundaries, AP/LFP gain columns, serials beyond JSON's exact numeric range, malformed files, merged DAT order, legacy encoding, snapshot hashes, resampling refusal, cancellation and double-correction guards. Actual NP2 recorder DAT matches its preview and preserves RAW bytes; its post-session merge preserves corrected rows and provenance without applying correction again. Actual NP1 sample writing and BLE plotting use the same correction. Strict v12 SD output matches the online formula, while descriptor/footer/UTC bytes remain unchanged. The calibration dialog and existing workspace controls are rendered offscreen without device discovery. The NP1 polarity check also exercises native SD factory AP/LFP correction, preserved RAW and accurate live/export receipts.

Retained evidence: `_artifacts/adc_calibration_20261012/{build.log,test.log,np1-polarity.log,ui.log}`. The private component remains version 1.0.0.11; production binaries, published installer and firmware were not promoted or replaced. These synthetic software checks do not establish physical ADC calibration accuracy or transport/endurance qualification.
