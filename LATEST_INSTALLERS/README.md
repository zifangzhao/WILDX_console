# WILDX 1.0.0.40

[Download the installer](WILDX_console-Setup-1.0.0.40.exe).

Data Download now prepares the selected V1/V2 logger's USB download mode
automatically when you click **List recordings** or download. Stop recording
first; the app verifies the same device before opening the data stream.

Fixes the V2 final-recording case where a valid committed footer contains data
but the unfinished catalog still points to payload-start. It reads the committed
prefix without modifying the card and retains original recording status and
metadata. Listing also shows recording size in GiB.

Build, targeted software checks and all 40 extracted payload files passed.
The over-100-GB check used synthetic metadata; the reported physical session
has not been downloaded here. Firmware and helper payloads are unchanged.

See [release details](../Note/RELEASE_1.0.0.40_20261017.md),
[manifest](release-manifest.json), and [checksums](SHA256SUMS.txt).
Setup includes **Run WILD-X** after installation. This root contains one final
installer; the previous installer and original metadata are
[archived here](legacy/20261017_before_1.0.0.40/README.md).
