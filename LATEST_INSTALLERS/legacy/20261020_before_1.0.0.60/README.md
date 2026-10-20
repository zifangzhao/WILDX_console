# Latest WILDX installer — V1 USB30k firmware update

Use `WILDX_console-Setup-1.0.0.51-V1-USB30k-USBF2D80C4F.exe`.
Console remains1.0.0.51; this package updates V1 NP firmware only.
In Advanced firmware update select V1 and the matching full fused image:
`V1_fused_BOOT_USBF2D80C4F_APP654824DE_usb30_startup_20261020.hex`.
APP-only flashing does not install the USB30k fix. Not for V1Camera or V2.

USB F2D80C4F: two short P1/30k starts and60s capture passed, plus P12/20k
control, zero frame/sequence errors and IMU gaps. Boot and APP654824DE stay
unchanged. Download/200mA/fallback implementation and evidence are inherited
from F8; no new download-speed/full-bundled-pair qualification is claimed.
No console rebuild, GUI59 preview changes or directory-retry candidate.

Matching fused, DFU/USB, USB-only and APP-only HEX files are in `Firmware/V1`.
See `release-manifest.json` and `Note/V1_USB30K_RELEASE_20261020.md` for exact
hashes, measured scope and limits. Superseded package is retained in `legacy`.
