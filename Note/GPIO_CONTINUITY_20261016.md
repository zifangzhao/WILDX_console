# GPIO/RTC continuity through NP interruptions

GPIO/RTC logging now stays open when a V1 or V2 neural capture completes because
of an interruption, including recovery waits and exhausted recovery. A later NP
recording reuses the same COM port, clock observations, event sequence, counters
and GPIO files. Explicit Stop ends both streams and cancels recovery, including
when only GPIO remains active. Closing the app also closes the GPIO logger.
Independent GPIO transport/file failures retain their existing error handling.

GPIO files remain in their original recording folder. Each subsequent NP folder
records their paths in `gpio_rtc_link.json` and its recording-settings context;
per-probe settings resolve those paths from the child folder. Keep linked folders
together. Final GPIO metadata retains the earliest observed neural receive time,
and the original recording index is refreshed after the GPIO log closes.

Private build and receipt: `_artifacts/gpio_continuity_20261016` (version
1.0.0.33). No installer, publication, firmware changes or hardware actions.
The separate USB-readiness change remains excluded by the checked HEAD source
override. Existing V2 test-only edits remain unchanged.

Validation: 42 serial protocol/clock/file lifecycle checks, 38 delivered-GUI
warning/panel checks and 54 delivered-GUI V1/V2 continuity checks passed using
in-memory serial input. The continuity check fails on the previous FCEC build
with `XeNp failure closed GPIO`. It checks events across the interruption and
new folder, relative and absolute metadata references, Stop during reconnect and
active capture, app close, one port-open/close and the original neural timestamp.
Run with `test_gpio_rtc.ps1 -ConsoleExecutable <checked GUI>`.
