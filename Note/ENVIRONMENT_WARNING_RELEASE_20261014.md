# Environmental sensor popup — WILDX 1.0.0.28

Environmental sensor errors no longer open a recording warning popup, including
when another sensor fault is reported. The device health display and recorded
environment error values remain available. Existing IMU, queue overflow, sensor
monitor, GPIO and capture-failure warnings retain their behavior.

The application was compiled in an isolated source snapshot using the release 27
runtime dependencies. The USB media default remains 40 fps, with explicit rates
and camera/IMU sidecars retained. This commit integrates only the environmental
popup change, version number and its existing GUI regression check; other pending
source work remains in the working checkout. Source input hashes are included
in the release manifest.

Validation:

- 38 actual-GUI warning, GPIO panel and LED checks, including environmental-only errors.
- 42 GPIO protocol/clock/file lifecycle checks using simulated serial input.
- 8 actual-form camera default/rate/no-auto-start checks.
- 12 focused checks of the actual warning observer with simulated sensor values.
- GUI build completed without compiler warnings or errors.
- Independent installer extraction verified all 13 payload files. The 12 files
  other than the GUI executable match release 27 byte for byte.

No installer execution, firmware update or hardware access was performed.
The new UI/firmware pair has not been hardware retested.

Native WILDX BLE media control remains incomplete. The GUI exposes neural
profiles; the unused media helpers retain 50 fps assumptions, MEDIA1 requires
its own receipt matching, and offline full-capture qualification still assumes
50 Hz. Those pre-existing gaps are outside this warning change. USB media
capture support is separate from native BLE media control.

Private verification: `_artifacts/environment_warning_release_20261014/`.
Re-run the GUI warning regression with:

```powershell
./test_gpio_rtc.ps1 -ConsoleExecutable <checked-WILDX_console.exe> -OutputDirectory <fresh-test-directory>
```
