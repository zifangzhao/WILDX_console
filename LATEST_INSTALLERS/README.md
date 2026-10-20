# WILDX48 — V1 fast SD download firmware refresh

[Download installer](WILDX_console-Setup-1.0.0.48-V1-SD-download-USB6CD1917C.exe).
Same checked WILDX1.0.0.48 runtime; new V1 USB1.0.96 / CRC6CD1917C with matching
fused boot+USB+APP654824DE. This firmware update is needed to use the new SD
fast initialization/read-retirement path; an APP-only update does not change USB.

Connected V1:84MiB exact comparison42.35MB/s; two matching1GiB reads43.11/43.42MB/s;
full export33.69MB/s,21 identical data files,0errors/retries. All31 recordings
unchanged and SD/BLE restored. No SD writes, recording or streaming tests.
Local HIL preserved privateAPP60EC609E; unchanged releaseAPP654824DE pair was not
newly device-tested. Remote11.2MB/s/0xFFFFFFFE not proven resolved. Near prior
short-read44MB/s speed restored; no claim above44. Missing main-only USB DMA
build flag fixed; maintained APP SD initialization retained. V2/camera/runtime unchanged.

All40 independently extracted payload files match frozen inputs. Installer not
executed as a new installed-pair test. Unbuilt private host changes excluded.
[Full scope](../Note/V1_USB_DMA_SD_DOWNLOAD_20261019.md) · [Manifest](release-manifest.json)
· [Previous48](legacy/20261019_before_sd_dma/README.md).
