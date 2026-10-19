# WILDX 1.0.0.44

[Download the installer](WILDX_console-Setup-1.0.0.44.exe).

Includes the checked V1 NP SD/BLE APP **FA2016E7**, USB **02FF93A9** and matching
complete fused firmware. V1Camera and V2 firmware remain unchanged.

**Large USB downloads remain unresolved:** large replies on the current hubbed
bench still fail Windows 31 after two bounded retries. Small CRC-bound reads,
checked cleanup and APP return pass. This is not full USB/all-mode qualification.

P1+P2/15 kHz completed 810.105 seconds/18.84 GiB with BLE preview/status/reconnect
and clean Stop/catalog; short P1+P2/20 kHz also passed. Two saved-data windows
passed, not full-extent readback. Physical Format/power-cut, other profiles,
camera and fused programming were not qualified in this cycle.

Exact checked owner EXE packaged without Compile. The owner reports 495 targeted
software checks; main GUI construction passed and all 40 installer files matched
after extraction. Default preview icons are an accepted cosmetic limitation.
Dirty-tree/selected-input provenance is documented; no exact all-source
reproduction claim is made.

See [release details](../Note/RELEASE_1.0.0.44_20261018.md),
[V1 firmware and limits](../Firmware/V1/README.md),
[manifest](release-manifest.json) and [checksums](SHA256SUMS.txt).

Setup includes **Run WILD-X**. This root contains one final installer.
The previous release and original metadata are
[archived here](legacy/20261018_before_1.0.0.44/README.md).
