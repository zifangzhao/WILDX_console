# GPIO panel and port LEDs — 1.0.0.21

The right column now stacks Device health, a taller GPIO / RTC panel and Network time / UTC. GPIO extends alongside Capture activity to Network time. Capture activity occupies the left column; its metrics, status and path continue to wrap and grow as needed. The right column follows that height change without reducing the live monitor's minimum height.

Four native WinForms indicators show Port 1–4, mapped explicitly to wire channels CH0–CH3, with event counts. A new counter observation lights that port in the shared teal accent for 350 ms; the UI checks every 100 ms. Continuous event activity keeps the LED lit. The protocol reports trigger events, so these are activity indicators rather than sustained input-level readings. Stop or logger failure dims the LEDs; final counts remain. Starting a fresh recording or changing/refreshing the selected device resets them. UI snapshots cannot mutate the logger's counters.

Software checks passed: 26 simulated GPIO protocol/clock/file checks; 29 delivered-GUI warning, panel and LED checks; 16 recording-metrics checks; 294 standalone protocol/catalog/layout checks; and 134 capture Stop/close ownership checks. The LED fixture checks channel mapping, short-event visibility, expiration, burst counts, Stop retention and reset. Layout is checked at 1044, 1100 and 1450 pixels, including adjacency to Network time and unclipped controls. [The render](../docs/screenshots/usb-recording-gpio.png) uses illustrative values with ports 1 and 3 active.

The isolated GUI starts from `f29100c918bd02766a6a80f8b800f0b90b3b2e3b` with only the GPIO panel, counter snapshot, layout and version overlays. The GUI build has zero warnings/errors. All twelve non-GUI runtime files remain byte-for-byte identical to 1.0.0.20; all thirteen extracted runtime files match the checked payload. The installer is not executed. Pending BLE/media work and hardware tests are excluded. No physical COM/USB/BLE or recording operations occur in these checks. Hardware qualification remains pending, and existing V1 firmware support is unchanged.

Installer, payload and source hashes are in [the manifest](../LATEST_INSTALLERS/release-manifest.json). Private build, test and rendering artifacts are retained in `_artifacts/gpio_panel_leds_20261014`.

- GUI SHA256: `62CDC6AA978C7458939B1F1F9985CD68B20942DD91E042B3E62FA99E2C1BA2D4`
- Installer SHA256: `2E173744E9ED2930C84DE3503715F2967F47123D3F954C6A02D3EDF4D32A4FF0`
- Installer bytes: 2,330,640
