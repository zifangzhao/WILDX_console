# Factory probe selection — WILD-X 1.0.0.84

Calibration searches the remembered factory folder recursively. Probe lists can
be filtered to V1/NP1 or V2/NP2; the model comes from the CSV format, and the SN
comes from the CSV contents. Connected logger workflows fix the filter to their
model. Offline workflows allow either filter when historical model information
is unavailable. The probe picker lists the matching ADC/gain file paths.

From recording and Calibrate folders prompt when a historical probe SN is
missing. Each choice belongs to one recording and physical port. AP/LFP files
sharing recording metadata reuse that choice; another recording prompts again.
Merged files retain their declared physical probe order. A known recorded SN
continues to match automatically and is not replaced by the picker. No first
probe is selected automatically; Skip leaves the recording unresolved.

NP1 acquisition gains come from recorded channel settings or an IMRO. If both
are unavailable, the picker accepts an IMRO instead of guessing gains. A supplied
IMRO must agree with recorded gains and the chosen probe model/SN. Missing ADC
files, conflicting duplicates, changed IMROs, and model conflicts prevent that
file from being calibrated. Existing manual setup remains available.

Batch correction records explicit missing-SN selections in the output receipt
as `user_selected_missing_serial`, with physical port, selected SN/model, and
`recorded_serial_available=false`. Original recording metadata stays unchanged.
Factory snapshots, IMROs, hashes, copy/overwrite transaction protections, and
double-correction checks are retained. The GUI needs no firmware update.

Validation on the exact 1.0.0.84 GUI:

- 174 calibration library, model filter, missing-SN scope, AP/LFP prompt
  deduplication, merged port, receipt, skip, GUI selection, and real-file checks.
- 15,060 batch calibration regression checks.
- 139 probe metadata, native SD export, old firmware fallback, and layout checks.
- 22 metadata persistence regression checks.
- 15,395 total. The real shared folder contains 12 NP1 ADC/gain pairs and 15
  NP2 gain files; all 39 CSV hashes remained unchanged. UI renders were inspected
  at the minimum window width and wider sizes.

GUI SHA256: `06452316D4D0325BF8DDFDCFAEE007EFA2A6F835E19AB8210C63DAA60199011C`.
SDK, firmware, helpers, and other payload files are byte-identical to release
1.0.0.82. Compilation 83 was an intermediate build; only 84 is published.
No user DAT was processed and no device, radio, SD card, or firmware was touched.
The installer is extracted and verified without executing it.
