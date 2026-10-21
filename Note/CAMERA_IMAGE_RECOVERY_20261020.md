# Camera image alignment recovery — GUI 1.0.0.61

The native camera decoder now checks the twelve training bytes and both serial
start/stop guards of every pixel in every row before demosaicing. This shared
path covers full-raster live USB preview, saved playback, and MP4 conversion.
It does not apply this geometry to downsampled BLE preview images.

An invalid row can be realigned only when exactly one candidate within ±256
bits has a complete training prefix and all 320 valid pixel words. The window
is much shorter than a row, so it cannot borrow an adjacent sensor row. A
clipped first training prefix is accepted only when all other 319 rows agree
on the same early offset, every first-row pixel remains present, and all its
guards validate. Captured bounds exclude regenerated wire padding.

If any row remains missing, malformed, or ambiguous, hold the previous complete
display image for that frame slot; use black before the first valid image.
Do not combine rows from different capture times. Such held frames are reported
as unavailable measurements, not as repaired sensor frames. Native raw bytes,
frame order/count, nominal playback rate, timestamp CSVs, and original capture
qualification remain unchanged. Serial guards are not a pixel-value checksum.

Live preview reports when it holds a damaged image, with recovery totals in its
status tooltip. Saved playback shows recovery totals. Conversion writes affected
zero-based frame indices to `camera.image-recovery.csv` and aggregate counts to
`conversion.json`. A partial conversion retains its partial audit. Metering
rejects invalid training or either pixel guard at the metering sites, including
when preview rendering is hidden or throttled.

## Verification

- GUI61 SHA256: `5D6B129C7654BDCC91290E234661F5930DB3186360D5E3E64F75A55144974BFE`.
- 1,163 source camera checks: all 1,024 pixel codes; original color/tone;
  bit offsets from −96 to +96; a phase change halfway through a frame; flat
  and saturated scenes; ambiguous patterns; damaged guards/tails; held-image
  reset; native byte preservation; and encoded frame indices/counts.
- 18 delivered export checks: actual GUI61 conversion and audit, unchanged
  native data/timestamps/qualification, frame slots, decoded MP4 holds, and
  refusal to overwrite an existing video.
- 58 delivered preview checks: cadence/duty limits, hidden/minimized metering,
  buffer ownership, and rendering resumption. No hardware access.
- Actual GUI61 decoded 525 sampled frames (35 per recording) from all 15
  FrontCamera recordings: 487 already valid, 27 realigned, 11 held because a
  complete unambiguous raster was unavailable. This is a sample, not a full
  recording integrity scan. Existing MP4 files were not modified by these tests.

Example: `SF09_20261018_outdoor/front/142983_20000000_000000.999`, frame 5370.
The previous fixed-offset decode produces large false magenta/green regions.
Marker-based recovery reconstructs a complete normally colored raster without
changing source pixels. Comparison and receipts are in
`_artifacts/camera_alignment_20261020/`.

V1/V2 developers reviewed the native format and existing capture/exposure paths.
They identified known capture alignment sensitivities and confirmed that missing
prefix pixels or damaged tails cannot be recreated by shifting. Bright-light
correlation does not establish causation: available recording metadata lacks
per-frame exposure settings. No firmware changes or hardware qualification are
claimed by this host decoder change.

## Full FrontCamera regeneration completed

The separately authorized batch completed all 15 `camera.mp4` videos under
`W:\data\FieldRat\behavior\FrontCamera`: 494,807 frames (2.962 hours),
11,575 realigned frames and 542 damaged/ambiguous frame slots held. Every video
passed full frame-count/rate/duration decoding and SHA verification; CSV recovery
indices/counts matched. All 165 native raw/audio/timestamp/recording metadata
files retained their lengths and modification times, and inputs were opened
read-only. Previous videos remain beside each output as
`camera.before-alignment-fix-20261020-202540.mp4`. Per-video recovery CSV/JSON
and regeneration receipts use that same run ID. No native samples or timestamps
were rewritten. Final receipt:
`_artifacts/frontcamera_alignment_regenerate_20261020/verification.json`.
