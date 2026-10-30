# V1 release204 firmware handoff

Exact qualified components: APP `FFF283D9`, USB `1E73A743`, and the unchanged checked DFU bootloader. The fused HEX is their direct partition-checked merge and excludes retained configuration.

USB passed ordinary host204 P1+P2/20 kHz/60 s capture with 75,057 observed IMU slots and one marked initial missing slot. APP passed the 30 s SD recording and exact-session 960 MiB host204 readback with 37,799 observed slots and one marked initial missing slot. The enforced cached-copy relative bound is 42 µs; local RTC-to-first-NP intervals are 112 µs for USB and 111 µs for SD. PC epoch calibration uncertainty is separate.

The 1,250 Hz grid preserves missing slots and repeated native sensor conversions. Physical sensor/ADC conversion phase, terminal tail and whole-clock lifetime are unqualified. SD source overlap remains unknown. Neural best-effort policy and disclosed boundary trims remain; no post-Stop full-payload scan is added.

Original firmware, options and the prior USB profile were restored and verified. Acceptance and source identities are in the JSON sidecars. This directory is private staging for the root release owner; it does not modify public firmware, installers or Git.
