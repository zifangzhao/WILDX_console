# Recursive GUI post-calibration — 1.0.0.76

Open Calibration, load factory gain/ADC profiles for the probes, then choose
**Calibrate folders…**. Enter or browse a source folder and scan it. Subfolders
are included. The preview lists each neural DAT, AP/LFP band, physical probe
order, serial assignment, eligibility and reason for a skipped file.

Copy mode is the default and mirrors the directory tree in a separate output
folder. **Overwrite originals** is an explicit option and disables the separate
output selector. Scan again after changing paths, encoding or overwrite mode.
Choose **Calibrate ready files** to process all eligible files. Errors are
reported per file while other files continue. Cancel leaves completed outputs
and marks remaining work in the batch report.

Supported neural names include merged neuropixels_ap.dat/neuropixels_lfp.dat,
SD npN_ap.dat/npN_lfp.dat and neuropixels_probeN_ap.dat, live probeN subfolders,
and per-probe unpaired_tail DATs. Non-neural DATs, raw files, camera/audio data,
partial suffixes, linked folders and prior derived metadata/output folders are
not calibration candidates.

Merged order comes from dat_merge.json or declared recording metadata. It is
never inferred from file size or sorted filenames. Single-probe identity comes
from its file/subfolder and is checked against recorded masks. Factory files
match saved serials when available, including changed physical port assignment.
When recorded serials are unavailable, the explicit loaded per-port assignment
is shown as manual in the preview/report. Missing/mismatched profiles or known
gain/model conflicts are skipped.

Auto encoding uses recorded transform/encoding evidence. If absent, choose
signed nominal ADC counts or legacy unsigned ADC codes explicitly. A selection
that conflicts with recorded encoding is refused. Already-calibrated files are
skipped using file/folder receipts, acquisition metadata and status evidence.
Resampled AP and declared missing-sample placeholder rows are skipped because
the existing correction cannot reconstruct pre-resampling ADC values or mask
missing samples safely. V2 source-gap DATs containing real rows remain supported;
their source timing/gap/probe metadata is copied without resampling or reordering.

The existing immutable factory CSV parser and streamed NpCalibrationDat.Correct
pipeline perform the correction. The DAT's row/channel order and length are
preserved. Each result receives its calibration receipt, exact ADC/gain files
and hashes, original recording metadata/IMRO snapshot, and adjacent XML when
applicable. The report is post_calibration_report.json in copy mode, or a unique
post_calibration_report_<timestamp>_<id>.json in the selected source root for
overwrite mode. Batch metadata labels the actual post-calibration stage and
whether the original uncorrected DAT was retained.

Overwrite stages a full corrected DAT and its metadata on the source volume,
holds a read/share-delete guard against concurrent source writes, and publishes
metadata plus a durable pending marker before File.Replace atomically replaces
the DAT. Successful overwrite retains no backup DAT. A cancel before commit
leaves the original unchanged. A failed replacement leaves the original,
staged correction and pending marker for review; subsequent scans refuse that
file to prevent uncertain double correction. Temporary space for a full corrected
DAT is still necessary even in overwrite mode. No recording metadata is relabeled
as a new acquisition; per-file correction receipts describe the derived encoding.

Verification:15,060 actual-app synthetic checks cover recursive NP1/NP2,
merged order [3,1], per-probe AP/LFP and tails, ADC threshold/offset and gain
snapshots/hashes, serial remapping, manual legacy assignments, malformed rows,
calibration/resampling/gap/identity skips, per-file decode failures, unchanged
copy inputs, successful atomic overwrite, cancellation before commit, locked-file
commit failure, pending-journal protection and actual GUI scan/control states.
The offscreen GUI was rendered and inspected. No user data was calibrated;
no device, firmware, radio or installed-application qualification was performed.

Build76 uses101 recorded GUI compiler/build inputs based on published73,
changing only the calibration form, two new batch classes, their project entries
and normal revision. Preliminary74/75 builds were not published; revisions were
incremented for each compilation. The SDK, firmware, helpers and acquisition
paths are byte-identical to73.

Runnable check: `test_batch_calibration.ps1 -ConsoleExecutable <WILDX_console.exe>
-OutputDirectory <fresh folder>`. Evidence:
`_artifacts/batch_calibration_20261022/checks76-r2.log`.

GUI SHA256: `CABD9E27F1FBEBF3D3E5F726BE67BE1FBA86F15287A473E4B8EC50F9788E80D2`.
