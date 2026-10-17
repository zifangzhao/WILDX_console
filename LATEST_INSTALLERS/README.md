# WILDX 1.0.0.37

[Download the installer](WILDX_console-Setup-1.0.0.37.exe).

BLE Mode now shows a bold **Recording time HH:MM:SS** line for V1/V2 neural
recording and supported V1 camera/audio. It uses device recording progress,
works after reconnecting and retains the final duration after Stop. USB keeps
its existing elapsed-time, recorded-size and free-space display.

The [1.0.0.36 BLE settings and Stop fixes](../Note/RELEASE_1.0.0.36_20261017.md)
are included. No firmware upgrade is required. The underlying V1 capture-source
fault on the two reported loggers remains unresolved; this display change does
not establish hardware recording integrity.

All 37 extracted files match the checked payload; the 36 inherited runtime,
helper and firmware files are unchanged. The installer retains Run WILD-X.
See [validation and timing semantics](../Note/RELEASE_1.0.0.37_20261017.md),
the [manifest](release-manifest.json) and [checksums](SHA256SUMS.txt).

This root contains one final installer. The preceding release and original
metadata are [archived here](legacy/20261017_before_1.0.0.37/README.md).
