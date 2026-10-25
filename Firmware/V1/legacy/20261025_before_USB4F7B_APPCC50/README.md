# V1 USB30k startup fix — USBF2D80C4F / APP654824DE

Use the matching fused HEX to install boot + USB service + SD/BLE APP.
Retained configuration is excluded. V1 NP only, not V1Camera or V2.
APP-only flashing does not install this USB30k fix.

The exact checked USB image adds30k-only provision-gated PA8 low-voltage
drive and moves USB30k run-register verification before capture DMA.
Two5s starts and60s P1/native30k, plus P12/native20k5s control pass with
zero frame/counter/phase/relock/USB-sequence errors and IMU gaps.

Boot, SD/BLE APP, UHS200mA allowance, read/fallback implementation and73
other linked objects are unchanged. Download performance/fallback evidence
is inherited from F8, not newly measured on this relinked image. The exact
public APP/USB pair was not newly installed together; private bench APP was
preserved. See download-init-manifest.json for complete identities/limits.
