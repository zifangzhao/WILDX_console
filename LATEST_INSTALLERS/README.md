# WILD-X 1.0.0.73

[Installer](WILDX_console-Setup-1.0.0.73.exe).

New V1 SD neural downloads write decoded AP/LFP DAT without ephys.bin or lane8
copies. Probe configuration, metadata, sensors, analog data and timestamps remain
with the recording. Optional merged DAT still works. Raw-only fast archive is
removed from the download page. Existing recordings and camera.raw are unchanged.
V1/V2 live recording and V2 SD export already use DAT-only neural output.

1,181 software checks passed, including byte comparisons against72 and interrupted
recording recovery/cancellation. No physical device or installed-UI qualification
was performed. SDK, firmware and helpers are unchanged from72.

[Exact manifest](release-manifest.json) / [change and validation](../Note/NEURAL_DAT_ONLY_20261022.md)
/ [compiled source inputs](../ReleaseSources/1.0.0.73/README.md).
The previous installer is retained under `legacy/20261022_before_1.0.0.73`.
