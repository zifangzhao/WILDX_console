# WILDX 1.0.0.41

[Download the installer](WILDX_console-Setup-1.0.0.41.exe).

V1/V2 BLE previews now use a fixed Y scale with a manual **Gain (×)** control,
from 0.1× to 100×. Signal amplitude and DC offsets no longer automatically zoom
or recenter the plot. Gain survives channel/probe/window changes and affects
the display only. USB retains its existing fixed gain control.

No firmware update is required. The previous download fixes are included.
The experimental V1 catalog-recovery work remains separate and unpublished.

The GUI build, targeted gain/preview/workflow checks, and all 40 extracted
payload files passed. Runtime helpers, exporter DLL and firmware are unchanged.
See [release details](../Note/RELEASE_1.0.0.41_20261017.md),
[manifest](release-manifest.json), and [checksums](SHA256SUMS.txt).

Setup includes **Run WILD-X**. This root contains one final installer; the
previous release and original metadata are
[archived here](legacy/20261017_before_1.0.0.41/README.md).
