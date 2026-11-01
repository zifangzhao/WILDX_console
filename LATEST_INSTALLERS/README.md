# WILDX 1.0.0.222

V2 SD16 downloads read the committed payload once and reorder on the PC. A bounded same-device comparison improved native export from 25.9 to 11.8 seconds (2.19x), with identical neural/IMU output. Other PCs/cards may differ.

No firmware upgrade is required. Existing checked V1/V2/camera firmware and catalog are unchanged. Temporary reorder overflow is removed after use; neural raw and lane8 files are not retained. Download progress now uses the physical stored size, and burst timing counters include actual transfers.

Validation:279 single-pass,37 legacy parity,800 transport,254 rate and14 ETA checks. The device stayed idle and all20 existing recordings were preserved. See [development details](../Note/SINGLE_PASS_DOWNLOAD_20261031.md) and [qualification](../ReleaseSources/1.0.0.222/qualification.json).

Exact compiled source overlay and artifact identities are included here. This release does not claim every mode/card or an endurance qualification.
