# WILD-X 1.0.0.139

The installer includes the latest accepted V2 Recovery 6408DBB8 / USB 1D3262FB /
APP 8DE56637 and the matching firmware catalog. No separate update ZIP or
catalog-install script is needed. Install this console update, then use its
Firmware Update controls to update a device when required.

The application was compiled once from the exact tested 1.0.0.138 compiler
inputs. Only AssemblyInfo.cs changed to version 1.0.0.139; all other GUI inputs
and the SDK hash are unchanged. V1 firmware remains APP 82E51DEA / USB 4F7B521A.
The installer retains the Run WILD-X finish option.

The actual 139 catalog loader verified every referenced firmware hash and
recognized matching latest V1 and V2 pairs as current. All prior runtime and
V1 payload files were preserved. The complete installer payload was extracted
and hash-verified; see package-verification.json. Packaging did not flash a
device or execute the installer.

V2 device evidence was gathered with the identical application logic in 138:
bounded USB three-probe 20 kHz and restart/rate-change one-probe 30 kHz runs,
fresh IMU data, BLE/SD Start/Stop/catalog checks, and a complete 8,468-block
export with 216,780 AP rows per probe. Four recorded B9 AG/MAG snapshots passed
the actual 138 display/journal replay. The exact firmware bytes are unchanged
from the accepted standalone publication; they were not rebuilt for 139.

Legacy UTC1 lacks first-sample PC timing. The optional startup-anchor query
can time out without rejecting recording. One earlier USB return required a
cable reconnect; a later final-image return passed. The fused image was not
separately flashed whole, and short selected-mode checks do not establish
all-card/all-mode endurance or physical timing. Detailed upstream evidence is
retained in ../V2_IMU_1D3262FB_8DE56637_20261025/.
