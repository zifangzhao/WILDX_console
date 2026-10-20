# V1 USB SD download: read-validated fallback tiers

Status: source and one incremental USB-main build checked; candidate package
prepared for user-approved publication before device validation. Not flashed.
No new sustained-throughput or installed-pair claim.

## Finding

The user's WILDX1.0.0.51/latest-fused report is9.5MB/s raw download.
The published USB6CD1917C previously measured43.1–43.4MB/s on this bench.
The fixed120MHz SD kernel with default divider3 produces20MHz four-bit SDR,
a10MB/s wire ceiling. That is consistent with the remote9.5MB/s report, but
the affected unit's live clock/mode has not been observed; it is a hypothesis,
not a reproduced cause.

The fallback source does contain a concrete bug: after successfully validating
HIGH/DEFAULT reads it changes VSEL to3.3V. Cold HAL initialization can already
have negotiated1.8V with CMD11. CMD6 SDR25/SDR12 does not renegotiate voltage;
changing VSEL after validation invalidates the accepted session.

The actual linked frozen HAL also checks a generic high-speed support bit,
not the selected CMD6 function. Its DDR path has weak timeout/selection checks.
Do not qualify DDR merely because that HAL returned success.

## Scoped correction

Enabled only by main-only `XE_NP_SD_USB_READ_TIERS=1U` in the resident USB
builder. The SD/BLE APP's original policy remains disabled from these changes.

| Order | Mode | Divider | Actual bus clock | Four-bit wire ceiling |
| --- | --- | --- | --- | --- |
|1|SDR104|0|120MHz|60MB/s|
|2|SDR104|1|60MHz|30MB/s|
|3|SDR104|2|30MHz|15MB/s|
|4|SDR50|2|30MHz|15MB/s|
|5|SDR25/high-speed|2|30MHz|15MB/s|
|6|Default/SDR12|3|20MHz|10MB/s|

These are signaling ceilings, not measured download rates. The existing
120MHz PLL2Q kernel stays fixed:40/50MHz clocks are not available by integer
SDMMC division. No shared PLL, capture clock, read engine, USB DMA, card supply
current class or driver-strength change. DDR is deliberately excluded until its
switching and receive path can be independently qualified.

For SDR104/SDR50, retain both existing edge and receive-path candidates. Higher
clock tiers are exhausted before lower ones. The second identical SDR104 sweep
is removed from the USB policy. Every attempt starts with the existing cold
power/reset/init sequence, including HIGH and DEFAULT. No failing card session
is reused. A failed reset skips that candidate; the next candidate resets again.

Acceptance requires the existing voltage, DLYB, CRC/read checks, then a bounded
read-only CMD6 query(`0x00FFFFFF`) proving support AND actual selected function,
exact64 bytes, DATAEND and no CRC/timeout/overrun. UHS requires actual negotiated
1.8V and an ultra-high-speed card classification. The voltage negotiated by
initialization is preserved for the lifetime of that card session.

Two further512-byte reads must match the conservative pre-sweep reference.
The card CID must remain identical across resets. Normal-speed cards may use
the fresh-init DEFAULT tier without unsupported CMD6 commands. If no tier passes,
initialization fails and the existing backend exposes zero usable capacity,
not an apparently ready unchecked card. Status16 is reference mismatch,17 CID
replacement,18 mode-query rejection; status5 remains a read-validation failure.

## Actual evidence

-196 assertions over the actual PostInit function: every acceptance position,
  exact tier/divider/edge/RX ordering, clean resets, baseline failure, candidate
  read failure/mismatch, replacement CID, reset failure and all-tiers failure.
-Actual mode-check header tested with support/selected-function matrices,
  FIFO short/extra/early-DATAEND/tails, CRC/timeout/overrun, missing DATAEND,
  tick wrap, command/configuration/block-length failures, normal-card DEFAULT
  and DDR/invalid-mode rejection.
-Both modified APP functions preprocess identically to the checked r17 versions
  with the USB-only option disabled. No APP image rebuild or recording changes.
-One incremental main compile/link passes. Internal USB DMA remains explicitly
  enabled. MSP and stream/reader objects are reused from checked r17. The
  generated stream differs only in its generated include directory; backend
  implementation is unchanged. No console build or V2 change.
-Linked flash code region0x36608 bytes fits its0x80000-byte allocation. Linker
  reports2464 bytes maximum known stack depth against a4096-byte stack, with
  its existing cycles/function-pointer uncertainty; no runtime high-water claim.

Build candidate: `_artifacts/v1_sd_read_tiers_20261020/checked/build/USB_app_sd.hex`
SHA256 `59E8BAE60067DE03E5AF1FE8A9F3B4AAB7FF76F0CE6CF47A3D745B94F85205A5`.
The frozen prepared source contains the earlier `3.3V` label on tier5; the final
maintained-source comment corrects that label to `SDR25/HS`. Executable tokens
are unchanged by that comment correction. Test/build/source-identity receipts
are under the same artifact directory.

## User-approved candidate package

The user explicitly approved packaging and pushing this candidate now, then
performing on-device validation afterward. This overrides the earlier hold on
candidate publication, not the requirement to report the untested scope.

USB package version1.0.97-sd-tiers-candidate, build`D75CA6F9`, loaderABI258.
Exact compiled code is preserved; only its standard USB manifest is sealed.
Current boot and APP654824DE are unchanged. The matching fused HEX excludes
retained configuration and has SHA256:
`AF0E83618771FD20F3BF484F8BDA9A6DA17D580D102B95E13D315D34D52E0AAE`.

WILDX1.0.0.51 runtime is unchanged; its firmware-only refreshed installer is
`WILDX_console-Setup-1.0.0.51-V1-SD-tiers-USBD75CA6F9-candidate.exe`, SHA256:
`4E020E54667602AF20EAB27A5FB4B8ECBE999E3B72FF98D57C7CF61856BEBE0A`.
One NSIS package pass; all40 extracted payload files match frozen inputs.
32 runtime/other-target files are unchanged; no private GUI52–54, recording-loss
or V2 changes are included. Installer was extracted, not executed.

## Remaining on-device gate

Passive Windows enumeration sees ST-Link interfaces but no V1USB1209:B386.
No flash/reset/reconnect, SD write, format, capture or benchmark was performed.
Actual throughput improvement and card-specific fallback remain unvalidated.
After this explicitly approved candidate publication, exercise it through the actual
USB download API on a mapped V1, check negotiated mode/clock, compare repeated
large raw-read hashes and error counters, and verify return to SD/BLE. Publication
must retain the SOFTWARE CHECKED / DEVICE VALIDATION PENDING label and preserve
the previous validated package in legacy; it does not establish hardware stability.
