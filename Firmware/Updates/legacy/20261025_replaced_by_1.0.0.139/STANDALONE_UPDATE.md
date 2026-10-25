# V2 firmware catalog update for WILD-X 1.0.0.138

1. Close WILD-X and extract this ZIP.
2. Run `Install-V2Firmware.cmd`. It finds the existing per-user installation,
   verifies the exact published 1.0.0.138 application and firmware hashes,
   preserves its V1 catalog entry, and backs up the previous catalog.
3. Reopen WILD-X. The bundled firmware controls now use V2 USB 1D3262FB /
   APP 8DE56637. Use Firmware Update to install firmware on the device.

This update installs local firmware files and their matching catalog. It does
not flash a device or replace the application/SDK. The original 1.0.0.138
installer is unchanged and contains its older V2 bundle; reapply this update
after reinstalling that installer. A custom installation can run
`Install-V2Firmware.ps1 -InstallDirectory "C:\path\to\WILDX_console"`
from Windows PowerShell.

Bounded USB/SD/BLE/IMU checks passed on the connected V2. The fused image was
assembled from the checked components and was not separately flashed whole.
First-sample PC time is unavailable for legacy UTC1 recordings. One earlier
USB return required reconnecting the data cable; a later final-image return
passed. These results do not establish all-card/all-mode endurance.
