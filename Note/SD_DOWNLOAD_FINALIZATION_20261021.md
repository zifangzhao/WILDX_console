# SD export appeared stuck at 100%

The reported session used console 1.0.0.51 and had remained at100% for about
two minutes. The old exporter reports the final USB block before flushing its
DATs, rereading every complete DAT for SHA256, and saving final receipts/XML.
This source path can explain a long finalization phase; a device/host hang in
that user's run was not reproduced or independently observed.

Version1.0.0.71 computes each DAT SHA256 from the bytes being written, in bounded
96KiB batches per probe. It avoids the second complete DAT read. The recorded
checksum describes emitted DAT bytes; final file length and flush checks remain.
It is not a separate disk-readback test. Decoder, source-gap, sample ordering,
recorded-state and firmware protocols are unchanged.

SDK100% is emitted once after its files, receipt and XML are finalized. A cancel
after the last native block is checked before final publication. The GUI retains
99% while recording metadata, optional merge/conversion and other completion
work remain, and displays “Finalizing recording files…” instead of a stationary
100% download status. ETA remains available during native export.

Verification:97 focused checks cover one/two/three probes, independent stored-DAT
hashes, byte equality against the pinnedBF32 exporter, saved healthy/source-gap/
overflow reports, incomplete prefixes, cancellation, terminal progress and the
actual71 ExportCalibrated metadata bridge. Another14 actual-app ETA checks pass.
No new hardware, cable-throughput, full-session timing or installed-UI check was
performed. Previous70 firmware/hardware evidence remains scoped to that pair.

GUI SHA256: `BE9F324CAD9B6795AC4E22291A1F8FBE551B33A59581761807130E0C7DC28A9D`.

SDK SHA256: `F485AFE114AA0E64AF8E24B4B91F809804752056BBC8EDF97977B23C68857AB4`.

The SDK's11 compile inputs and override targets are recorded separately. Shared
files reuse checked70 inputs where available; maintained standalone source is
snapshotted explicitly. No claim that the new build reproduces an unavailable
historical BF32 source closure is made. GUI71 retains exact70 source composition
except the progress display and normal revision; the runtime bundle changes only
the GUI EXE and SDK DLL. Firmware and helpers remain byte-identical to70.

Checks/build evidence: `_artifacts/sd_finalize_20261021`.

The new installer can be installed after an active older export finishes.
