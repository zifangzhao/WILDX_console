# WILDX V1 store2 rc1

[Download the installer](WILDX_console-Setup-1.0.0.44-V1-store2-rc1.exe).

Exact checked console **1.0.0.44**, APP **654824DE**, USB **FACEA0D2**, ABI **258**.
The executable's embedded version is preserved; **V1 store2 rc1** identifies this
distinct package. No application/firmware rebuild or source integration.
V1Camera and V2 firmware/targets remain unchanged.

FS2 needs explicit fresh format/epoch; no silent legacy migration. Older writers
must refuse FS2. Download uses checked role3 full-speed compatibility at about
**0.83 MiB/s**; capture roles retain high speed. Normal high-speed download is
unqualified. Five-minute P1+P2/20 kHz with reconnect and sampled72MiB passed,
along with shorter full strict NP/IMU exports, transient descriptor unavailability
through Mount and START-remount/safe append, and production USB download/export.
This is not full10GB, physical-removal, camera, implant or all-mode endurance
qualification. P1+P2/30 kHz sustained remains failed on the tested card.

One NSIS pass; all **44 extracted payload files** matched. Setup includes
**Run WILD-X**. This root contains one installer. Previous package and original
metadata are [archived](legacy/20261019_before_V1_store2_rc1/README.md).

See [release details](../Note/RELEASE_V1_STORE2_RC1_20261019.md),
[V1 files/evidence](../Firmware/V1/README.md), [manifest](release-manifest.json)
and [checksums](SHA256SUMS.txt).
