# WILD-X Console 1.0.0.189

This release includes the checked V2 RTC completion update. First-sync timing
evidence is enabled in the host; pre-arm is disabled.

The V2 SD test recorded three probes at 20 kHz for 30 seconds. Session 3268315904
contained 600,063 neural rows per probe, with all five reported loss counters
zero. Saved IMU observations included 5,780 AG and 2,890 MAG records.

The saved first-NP-start position was mapped to the device hardware RTC with a
120 µs full interval (±60 µs). The separately declared PC calibration uncertainty
was 20,594 µs; it is not part of that local RTC-to-NP interval. The saved 1250 Hz
IMU grid holds the latest sensor values and does not establish physical sensor
conversion phase. Stop does not perform a full payload scan.

Bundled V2 firmware: APP 46DF5009, USB 62910614, recovery 6408DBB8. The tested V2
device was restored to its original firmware after validation. V1 remains the
unchanged build 165 bundle: APP B1758369 and USB 4F7B521A; this release makes no
new V1 RTC validation claim.

Software checks passed: 363 clock, 281 setup and 509 native-order checks. The
hardware result covers the V2 SD test above. V2 R6 USB acquisition, a full fused
flash and alignment of every stream have not been qualified.
