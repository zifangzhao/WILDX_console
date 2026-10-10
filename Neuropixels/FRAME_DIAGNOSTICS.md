# NP1 frame failure details

The universal live decoder reports the zero-based hardware-frame index, raw
probe byte offset, 20-bit counter, expected/received sync and fixed words,
nonzero fill tick count/mask and heartbeat transition count on a frame failure.
The recording stops and is marked incomplete; it never resynchronizes or repairs
samples. Counters retain their separate strict discontinuity check.

All additional field reads and message formatting run only after the existing
frame validator rejects a frame. Startup qualification, accepted models, lane
layouts, frame/counter checks, sample extraction, writer queues and recording
control remain unchanged. The valid-frame path adds no field reads or allocations.

The October 4 bench wire prefix reproduced the original 1.0.3.31 failure offline:
P1 frame2034, counter12005, raw offset122509 had sync0x08F instead of0x0CF.
Its fixed word0x011, fill and heartbeat remained valid; counter continuity was
unbroken. P2 had no failing frame among6392 available frames. The requested
75ms reader pause had not been injected. This is captured sync-bit corruption,
not evidence that acceptance should be relaxed. Sampling/staging origin remains
a separate firmware investigation. The host exception now exposes those fields.

No hardware, firmware or on-disk recordings are modified by the diagnostic update.
Software tests cover the same rejection in GPIO, PSSI and V1 quad layouts, each
checked field and the unchanged counter/closed-session behavior. Offline replay
uses the original captured bytes without altering them.
