# Latest WILDX51 — V1 UHS200 firmware refresh

Installer:`WILDX_console-Setup-1.0.0.51-V1-UHS200-USBF8E721C2.exe`.
GUI51 unchanged. V1 NP USB serviceF8E721C2 +unchanged APP654824DE/boot.
Matching fused and component HEX files are in`Firmware/V1`; V1Camera/V2 unchanged.

Connected V1 actual production-API download:two normal resets (~1.3s),each84MiB
exact reference and1GiB known hash at42.57MB/s,zero errors/retries. Forced all
SDR104 attempts to fail; SDR50 fallback read84MiB exactly, then normal reset
restored full speed.31-record catalog unchanged; no card writes.

Fresh CMD6 evidence now recovers retained1.8V sessions even when ACMD41S18A=0.
Actual200mA function0, sector equality and native status/CRC remain required.
200mA is a card setting,not a measured current clamp. Other cards/modes and exact
bundled APP/USB pair are not newly device-qualified; existing evidence retained.
See`Note/V1_USB_UHS200_RESTORE_20261020.md` and the firmware manifests for limits.
