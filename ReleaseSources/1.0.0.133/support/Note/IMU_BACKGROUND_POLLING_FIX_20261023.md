# IMU polling fixes — private133

A slow pending clock-read retirement could span multiple1s timer events before
bleImuTask was assigned. Each event could therefore enter and later start its
own IMU read. The previous132 callback reproduces two overlapping jobs in the
file-only regression.133 owns the complete background cycle with one guard,
released in finally; foreground control can still cancel/retire optional work.

The display also now clears its current IMU state after3s without a complete
usable observation, showing “IMU waiting for fresh data…” rather than leaving
old values presented as current. Fresh observations restore the normal display.
Unsupported firmware and closed-form states retain their existing handling.

The USB IMU lifecycle fixture now loads the actual compiled application instead
of trying to compile a stale subset of sensor/clock source dependencies. Its
monitor-only/no-save case verifies received display data without any device
handle or EP0 request and still checks the source acquisition exclusions.

Validation:5 checks reproduce the old overlap;22 actual-source scheduling,
freshness and recovery checks pass for the fix;5 actual-compiled USB lifecycle,
366 BLE IMU and43 clock-evidence checks pass against133. The new source runner
works without a baseline artifact; -BaselineForm optionally reproduces132.

Only NpBleForm.Imu.cs and the automatic app version differ from frozen132's
compiled inputs. Neural workers/decoders, sensor parser, clock-correction engines
and SDK bytes are unchanged. No new firmware, device access, installer or
publication. V2 saved-session292 full independent readback remains blocked by
physical device access; a low SWD VTref is not proof the board itself is off.
