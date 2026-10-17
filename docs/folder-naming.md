# Recording folder names

Enter an optional **Session label** in USB Mode or Data Download. The two fields share the same value while the app is open. For example, `RatID_sleep` produces `RatID_sleep_wild_20261013_143025.123`.

Live USB uses the PC's Start-attempt time. SD neural/media downloads, including NP1 fast archives, always use the stored recording START clock when valid, even with a blank session label. For example, a download without a label creates `wild_20261013_143025.123`; a label adds the prefix shown above. Stored UTC is converted to the PC's local time zone. This folder date does not certify the first ADC conversion time. Recordings without valid stored START evidence use `[<label>_]wild_time_unknown_<session-and-LBA>`; the download date is never substituted. A blank label retains default naming for live USB recordings. Labels accept up to 80 characters and exclude Windows filename separators/invalid characters and trailing dots.

Automatic USB recovery now creates a new independent recording directly under the selected output folder, with a fresh timestamp. It does not create `segment_0001` folders. Each recording has its own DAT, probe/configuration/calibration files, timing records and completion/error reports. A restarted recording includes `recovery_link.json`, linking to its previous recording and the first recording's restart journal. Gaps and missing sample counts remain unknown; recordings are never appended or joined across a restart.

If a destination already exists or another export has claimed it, `_2`, `_3`, etc. are added. Existing recordings and partial downloads are preserved.

The neural DAT encoding, live probe-block order and DAT-only USB policy are unchanged. Firmware and device clocks are not altered by folder naming.
