# Camera tone contrast - local GUI60

User reported pale regenerated videos. In3real native frames, previous tone
lifted1st-percentile luma to about135/255; revised contrast gives25-27/255,
with shadow detail/highlights and the corrected rotatedRGGB phase retained.

Shared preview/playback/MP4 tone uses interior spatial luminance contrast,
excluding2pixels at the edges. Shadow correction requires at least64/1023
of1st-to99th-percentile luminance spread; black target capped256,8codes of
shadow margin, smoothed at most4codes/frame. Uniform tinted/gray/bright scenes
keep the zero-based tone. Packing, rawbytes, frames, exposure controls and
firmware are unchanged. This is limited display contrast, not calibrated
sensor black-level or white-balance correction.

NanEyeC DS000503p14 documents electrical black references, but V1 developer's
source review confirms thisCE64 layout does not establish their absolute
physical coordinates. No reference location/factory offset is invented.
Manufacturer reference:
https://look.ams-osram.com/m/19863b4335e67f1b/original/NanEyeC-Miniature-Camera-Module.pdf

Validation:1064actual-source checks including MP4 encode/decode, unchanged
nativebytes, oldhotpixel/nearwhite/saturation/gap/reset regressions and new
flatcolor/gray/shadow-detail tests. Normal hook advanced59to60. ActualGUI60
pixels equal checked source for3native frames. GUI SHA256:
4ED5B103154BFA26EFE9B0303D0DBE7C3AA400979874A20878183096E7492889.

Only shareddecoder and conversion metadata differ from exactGUI59 inputs.
9prior-source snapshots preserve unrelated current work. Local app-current
carries all41hash-verified files of publishedF2D80C4F release, then GUI60overlay.
The original comparison did not modify recordings. The user subsequently
approved packaging/publication and regeneration of15MP4s with backups; see
RELEASE_1.0.0.60_20261020.md. No hardware or firmware rebuild.

Evidence: _artifacts/camera_tone_contrast_20261020/build-receipt.json,
frame-metrics.json, tone-v2.patch, tests/result.txt and comparison.png.
