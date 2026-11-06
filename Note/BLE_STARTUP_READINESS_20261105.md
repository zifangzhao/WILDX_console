# BLE connection startup readiness

Current development host: 1.0.0.248. The fixed 247 settling wait passed on an
already initialized APP but did not cover a fresh USB-to-APP boot. Version 248
adds bounded initial read-only readiness; its actual cold hardware test passed.

Application 1.0.0.247 adds one cancellable 500 ms readiness wait after the
existing notification setup, 250 ms subscription settle and optional Windows
throughput preference. This matches the successful hardware probe using the
unchanged 246 connection followed by a 500 ms wait. It runs once per connection
in the shared V1/V2 class, while the existing command semaphore remains held.
Start, Stop, Delete and connected recording traffic gain no per-command delay
or command replay.

The connection epoch is captured before notification subscription. Before
publishing a ready connection, the code checks the same epoch, owned timeout,
cancellation and live connection. A disconnected/reconnected Windows object
cannot revive an old attempt. Explicit Disconnect cancels the readiness wait.

## Evidence

The immediate first discovery timed out without MCU UART receive bytes. On the
same radio/address, the delayed read-only probe returned two valid, correlated
82/B9 status replies. Radio: HJ-185HT_6BCC7C, F5D68A6BCC7C / Random; MCU UID
001A0004434250062039314E. This establishes the tested startup condition, not a
universal Bluetooth timing guarantee.

Probe: `C:/Frank/Code/XE_NP/_artifacts/v2_torn_journal_fix_20261105/hil/ble-delayed-read-only.json`
SHA256: `DB606BC3D6E8B26F7899DF410A872434E85CEDD6FE8BB3A1BBB582A431C97FE4`.

One incremental application compilation produced 247 in 7.5 s from the frozen
246 sources. Of 208 stable source inputs, only NpBleConnection.cs and version
metadata changed. The checked SDK 235 is unchanged. Unrelated dirty workspace
sources were excluded from this build.

Runtime: `C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_host/enabled247-blesettle`.
WILDX_console.exe SHA256:
`72FF2B27017B1B1786EB62A20D359A46B6A9D6AC2597C8D14E19C8B607EA23CA`.
SDK SHA256:
`EBC98EEB84FDE42DEE06B713F1F6246B30B3527732075D9F84C6DF57BCA69EFB`.
The supplemental receipt identifies the actual 246 baseline; the reused raw
builder's old 222 annotation is not source provenance:
`C:/Frank/Code/USB_PCconsole/_artifacts/v2_ble_startup_20261105/host247/build-qualified.json`
SHA256: `B95C5DFEB48F60275AB9A31403B7EF8EB1718BCBEB9851F61521CD9D676AB474`.

The focused actual-source WarmLink harness passed 134 checks: queued commands
remain blocked through readiness, explicit close cancels promptly, epoch
change rejects a revived connection, failed subscription never enters
readiness, and connected command probes do not repeat the delay. The harness
models GATT endpoints and does not operate a radio.

Receipt: `_artifacts/ble_startup247-tests/receipt.json`
SHA256: `0EB42347EB8344C5FFFD117DE45C5901F90B91DD5A8117C0FE4F163B61D08C88`.

Actual 247 startup passed on already initialized V2 without an extra harness delay after APP
automatic delete recovery. The trace enters bridge readiness at 5,071 ms and
publishes connected at 5,583 ms. The first discovery plus correlated B9 status
and a second status both passed, state 1/error 0, expected capacity/frontier.

Hardware receipt:
`C:/Frank/Code/XE_NP/_artifacts/v2_torn_journal_fix_20261105/hil/host247-after-app-auto.json`
SHA256: `74773A3073AF87D9755AC453EA7C5426C774B80F497F8881E37369695E45F00D`.

The V1/V2 shared connection path is exercised by the focused harness; a new
V1 radio test was not performed for this change. Firmware recovery
qualification is separate and retains its own exact candidate/test scope.
No installer or publication is claimed by this note.

## Initial discovery during cold boot: 248

The GUI's first device read now calls `ReadInitialDevice`. It permits only
canonical discovery 82 and B9 STATUS requests. A missing read-only receipt
may retry after 250 ms, under one 30-second deadline; each correlated STATUS
uses a fresh transaction ID. Valid device faults, malformed replies and other
I/O errors fail immediately. Start, Stop, Delete and ordinary connected status
handling are unchanged. The UI remains in its cancellable connecting state.

The owned startup timeout is canceled by Disconnect. Each exchange checks
the connection epoch, timeout ownership and live connection before and after
delivery. An old connection cannot send a later discovery on a new epoch.

One incremental 248 compilation reused the exact checked SDK and frozen 247
inputs. Only Connection, Form and version metadata changed. Runtime:
`C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_host/enabled248-bleready`.
WILDX_console.exe SHA256:
`364BE7FF77B45C4B959FB72FE9CB657E8F04CEDBCF4C21E3F8F2AC22890DAF30`.
Supplemental build receipt:
`C:/Frank/Code/USB_PCconsole/_artifacts/v2_ble_startup_20261105/host248/build-qualified.json`
SHA256: `A9EC7EAD49232AD45ABDD7C64540DBCFBEAABAB6EBA9908EC568DF98F8E18ADC`.

The actual-source initial-discovery fixture passed 31 checks for both
families, fresh STATUS IDs, read-only frames, modeled deadline, explicit
cancellation, epoch change and immediate valid-fault/parse/I/O rejection.
No hardware is operated by the fixture; the unchanged 500 ms phase reuses
the 134-check evidence above.

Receipt: `_artifacts/ble_startup248-tests/receipt.json`
SHA256: `39D2C9661F09C6CCCA31FA812EA19B90CCA51B2B14A9665006DC4B1E77AC78EF`.

Actual 248 cold USB-to-APP transition, immediate Connect and instance
ReadInitialDevice passed without an external wait or retry. Correct UID/caps
and idle/error-zero status preceded one ordinary three-probe, 20 kHz
continuous Start/Stop, which also passed. Control evidence is under
`C:/Frank/Code/XE_NP/_artifacts/v2_torn_journal_fix_20261105/hil/post-usb-auto-record248`.
Full saved-data and Delete acceptance remain in the firmware owner's final
qualification receipt; a Start acknowledgement alone does not prove integrity.
