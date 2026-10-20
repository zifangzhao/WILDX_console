# WILDX1.0.0.60 - camera contrast and console updates

Camera videos previously looked pale because zero-anchored tone mapping lifted the recorded scene's dark levels. The shared USB preview, recorded playback and MP4 decoder now uses limited spatial-luminance shadow contrast. Corrected BGGR/rotatedRGGB mapping, bright flat scenes, highlights and native camera bytes are retained. This is a display adjustment, not calibrated black-level/white-balance correction.

This release also carries the checked console updates accumulated since GUI51: packed-pixel exposure metering, preview work limits and hidden/minimized metering, editable output paths, USB download ETA, validated camera START timestamp folder naming with explicit unknown-time fallback, best-effort V1 clock synchronization and shorter firmware status text.

Validation:1,064 actual-source camera checks including MP4 encode/decode, packing, nearwhite/magenta, highlights, saturation, gaps, flat colors and shadow detail.3 real native frames through actualGUI60 match checked pixels exactly.57 preview checks pass against the extracted installer. All41 extracted payload hashes match. ActualGUI60 firmware catalog and full fused-image loader accept the bundle. No installer execution, hardware access, new recording/firmware qualification or product rebuild during packaging.

GUI60 SHA256:4ED5B103154BFA26EFE9B0303D0DBE7C3AA400979874A20878183096E7492889.
Installer SHA256:8017AE527C316150C234525C1106F1A779F2A7AFA0C1CD61CC51D06BB63A2865.

The40 non-GUI payload files match releaseV1_USB30K_USBF2D80C4F_APP654824DE_20261020 exactly. Boot, publicAPP654824DE, USBF2D80C4F, V2 and camera firmware retain their existing validation and limits; none was rebuilt/resealed. No fresh download/full-installed-pair/laptop claim. The held directory-reconnect candidate and private V2 recording/SDK changes are excluded.

Exact98 compiler/build inputs are archived in ReleaseSources/1.0.0.60, with hashes and original line endings. Matching source snapshots are committed at their normal paths without overwriting unrelated current working changes. The build reused recorded resources/dependencies/exporter, not a new clean-HEAD build. LATEST_INSTALLERS contains one final installer; its predecessor is preserved in legacy.

The user separately authorized regeneration of15 FrontCamera camera.mp4 files using this exactGUI60. Each current MP4 is backed up, raw inputs are read-only, and frames/fps/duration are checked before replacement. Audio, timestamps, raw data and existing earlier backups are retained. Batch completion is reported separately; packaging does not imply the batch is finished.
