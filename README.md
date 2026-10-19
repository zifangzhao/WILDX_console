# WILD-X Neuropixels Console

Independent Windows console for Neuropixels devices. Current published version **1.0.0.37**. Complete hardware qualification is pending.

The application uses the Neuropixels acquisition and preview code from USB512_console / USB_PCconsole, the BLE control and catalog contracts from WILD_console, and the NP2 firmware updater from XE_NP. All required source and helper projects are stored here; building and running WILDX does not require those repositories.

BLE Control aligns device/address and recording-profile/storage-bound fields in shared columns, with connection actions below. Buttons have consistent height and spacing; narrower action rows and long status messages wrap. [BLE layout render](docs/screenshots/ble-mode.png) and [validation](Note/BLE_LAYOUT_ALIGNMENT_20261014.md).

Network time displays local time with the Windows UTC offset and selects among Cloudflare, NIST and Microsoft automatically, with a manual server override. The accuracy target is **±10 ms**; the UI reports the estimated error honestly. Preview windows use normal window ordering. [Clock settings](docs/network-time.md), [preview](docs/screenshots/network-time-settings.png) and [validation](Note/NETWORK_TIME_LOCAL_PREVIEW_20261014.md).

The compact USB page sizes the recording actions and recovery option to their visible content. Aligned inline field labels and tighter card spacing keep the complete normal recording view visible at checked client sizes from 1044×780. Long diagnostics remain expandable. [Updated render](docs/screenshots/usb-recording-gpio.png) and [validation](Note/COMPACT_USB_LAYOUT_20261014.md).

The original Neuropixels entry points in WILD_console remain available until WILDX passes hardware checks, as requested.

The modern workspace uses four separate tabs, with WILD's teal palette, white cards, Segoe UI typography and resizable layouts. **USB Mode** contains USB acquisition, live previews, device health and time status. **BLE Mode** embeds wireless discovery, connection, profiles and onboard SD recording controls. **Data Download** contains the recording browser, USB / card-reader downloads, NP1 raw-archive decoding and media export. **Advanced** places USB streaming, USB download and SD / BLE controls beside Firmware Update and Diagnostics & self-test. Recovery and flashing stay in the firmware dialogs. BLE Mode and Advanced share one connection; firmware, rescue reset and catalog recovery actions use that session without a second BLE window. Tabs remain accessible while BLE is connected, scanning or executing a command. Conflicting USB acquisition, firmware and logger access remain gated; local exports and independent card readers remain available. An active USB capture, download or firmware operation still blocks switching workspaces. Downloads can be canceled without losing the operation's controls. Bundled and installed firmware identities are displayed separately; unknown installed builds receive a package-review notice, not an inferred upgrade claim. Updating remains explicit.

Layout previews: [USB Mode](docs/screenshots/usb-mode.png), [BLE Mode](docs/screenshots/ble-mode.png), [Data Download](docs/screenshots/data-download.png), [Advanced](docs/screenshots/advanced.png). These are rendered from the built application with hardware discovery disabled.

**USB Mode → Media preview…** adds V1 camera-only live preview, FPS/exposure controls and optional raw camera saving. Its Audio pane displays and plays a bounded listening copy of exported microphone WAVs. The connected V1's camera startup timed out before any frames arrived; live USB microphone data is not advertised by its resident firmware. See [media preview and V1 test evidence](docs/usb-media-preview.md).

V1 and V2 now share **Recording settings** with **Probe configuration…**, misc-signal recording, exportable reports and **Diagnostics & self-test…**. Controls adapt to device capabilities; firmware identity, applied-map receipts and native USB decoder checks remain enforced. The shared workflow is included in the installer and is awaiting hardware checks. See [universal workflow](docs/universal-workflow.md).

V1 Recording settings can read the existing firmware's profile while the logger is in idle USB streaming or SD-download mode. Download mode uses its service status and capability replies; Start prepares USB streaming on the same MCU UID. Legacy selectable and fixed-profile firmware paths remain supported. This is an app-only compatibility fix and requires no firmware upgrade. See [V1 recording validation](Note/V1_RECORDING_MODE_20261014.md).

V1 probe boxes are directly editable and initially reflect the active firmware profile. Advertised connectors do not establish physical probe presence. Advertised 20 kHz V1 profiles no longer show the generic experimental confirmation; two-probe 30 kHz retains its qualification warning. Metadata saves retry transient file locks, retain completed failed replacements beside the data, and report destination paths and native errors. Stop reports metadata persistence separately from USB/decoder failures; the report UI no longer repeats final settings/probe/XML writes. See [metadata fix and validation](Note/V1_METADATA_FIX_20261014.md).

The probe configuration page shows three stacked cards with independent IMRO loading and physical shank/site maps. The USB workspace has one entry through **Recording settings**. Live USB and BLE previews have a **Display HPF** checkbox and editable cutoff; defaults are 300 Hz for USB and 100 Hz for the 500-sample/s BLE preview, initially disabled. Filtering affects the display only. [Probe page](docs/screenshots/probe-configuration.png), [filter controls](docs/screenshots/live-hpf.png), and [validation](Note/PROBE_CONFIGURATION_UI_20261013.md).

USB Capture activity displays elapsed recording time, actual recording-folder size and available destination disk space. The **GPIO / RTC** selector starts the selected WILD GPIO RTC board with each USB recording and saves its event, clock-query and session files beside the neural data. GPIO continues through interrupted NP recording and automatic recovery until explicit Stop or app closure; subsequent recording folders link to the same GPIO files. Its panel extends from Device health down to Network time, with a table of **ID / Status LED / Count / Freq (Hz)**. It starts with IDs 0–3 and adds rows for additional received IDs, scrolling as needed. Frequency uses the latest one-second device-RTC window. Each LED flashes for 350 ms on an event; final counts remain after Stop while Hz returns to zero. GPIO startup failure warns and allows neural recording to continue. Sensor errors warn once per category per recording. See [integration](Note/RECORDING_STATUS_GPIO_RTC_20261014.md), [continuity](Note/GPIO_CONTINUITY_20261016.md), and [channel checks](Note/GPIO_CHANNEL_TABLE_20261014.md).

New USB app recordings save decoded DAT without an extra neural RAW file. V1 saves AP/LFP; V2 saves AP. The combined channel layout and live writing remain unchanged. Recording metadata and probe files are saved alongside data, including per-probe sidecars, exact configuration binaries, calibration CSVs and timing/error reports. `recording_files.json` indexes each recording folder and its subfolders. SD downloads archive the recorded metadata bytes and preserve missing-map information explicitly. See [recording bundles](docs/recording-bundles.md).

The shared theme uses a neutral gray canvas, white cards, soft gray input/log surfaces and a single teal accent. Selected tabs and primary actions share that accent; secondary actions, borders, hover/pressed states and disabled controls use the same palette throughout the workspaces and management/settings dialogs. Destructive actions retain a muted red treatment, and live telemetry retains its distinct signal colors.

## Capabilities

| Workflow | NP1 / V1 | NP2 / V2 |
| --- | --- | --- |
| USB streaming | AP / LFP recording, probe configuration, preview, supported camera / IMU streams | Dual-probe AP recording and preview through the native V2 USB decoder |
| Firmware update | Existing validated APP updater and guarded HEX / ROM DFU manager | Bundled transactional `.xenp` APP updater, CRC commit and protected resident USB / loader regions |
| Data download | USB and read-only card-reader catalog export; raw archive download and offline decoding | USB and read-only card-reader catalog export to classic per-probe DAT files |
| BLE control | Device status, profiles, channel staging, SD start / stop, PC time, catalog mount / recovery and USB mode handoff | Device status, supported profiles, SD start / stop, automatic PC-time initialization, read-only clock checks, NP preview, catalog mount / recovery and USB service / loader handoff |

Profiles and mutations are checked against the connected device's firmware status and capabilities. BLE commands use the FFF0 service, FFF1 notifications and FFF2 writes, correlate CRC-valid receipts, and stop on uncertain outcomes without replaying commands. Full-rate recording uses USB or the onboard SD recorder. BLE provides controls and an expendable display preview.

On V2 connection, a stopped recorder receives 16 valid PC-time observations and one anchor commit. The shared calibration engine retains its default of 64 for other callers and all timing-quality, generation and commit-receipt guards. Attaching to an active recorder only reads and verifies its existing clock. The two-second status refresh also checks clock generation and offset without committing time. V2 Start requires confirmed PC time; failed initialization leaves status and Stop available. Use **Sync PC time** while stopped to recover an unconfirmed mapping. This source change was checked against one stopped-device synchronization; connection discovery and recording quality remain separate checks. See [16-observation sync evidence](docs/v2-ble-sync16.md).

**BLE Mode → NP Preview** displays three ADC lanes for a selected probe and base channel during an existing V2 SD recording. The host validates a complete SuperSync calibration, then decodes rolling packets at 500 samples/s using the native unsigned 12-bit ADC mapping. Session, rate, selection and connection must match. The source supports dual-probe 20/30 kHz and triple-probe 15/20 kHz recording profiles; the current BLE payload carries probes 1 and 2 only, so probe 3 preview remains unsupported. The six BLE payload bits are independent of the SD packed6/raw9 storage codec. The profile admission fix is included in the installer and has a fixture in `test_ble_preview_profiles.ps1`. The plot shows missing samples and gaps without interpolating them; display filtering is optional and there is no LFP claim. Preview traffic yields to controls and uses a bounded queue. Closing or pausing the preview releases its subscription and lets the device lease expire; it never sends SD Start or Stop.

Download validates the selected source, catalog, committed recording extent and generation-specific metadata. NP2 export supports v11 and v12 descriptors and footers, retains the native AP decoder and does not synthesize an LFP stream. v12 validates the card allocation unit separately from the exact recording extent; unused reservation padding cannot become recorded data. See [v11/v12 export checks](docs/v2-sd-v12.md). Existing native media export is included; MP4 conversion requires an external FFmpeg installation.

**Calibration…** loads probe-specific factory ADC/gain CSV files for USB recording, BLE preview and Data Download. Host corrections are opt-in and use the same engine online and offline; offline input archives are preserved. USB app recordings save decoded DAT without an extra neural RAW copy. NP1 digital ADC threshold/offset and the selected AP/LFP gain columns are supported; NP2 uses its gain file. Calibration inputs, serial bindings and hashes are archived with processed outputs. **Correct DAT…** creates a new corrected file, including an explicit legacy-unsigned input option. Factory hardware trim values are retained but are not written to the probe by this host pipeline. See [calibration workflow and checks](docs/adc-calibration.md). Calibration is included in the current installer.

V1 and V2 use the same outer CE64 catalog. Desktop listing, removable-card inspection and explicit Quick Format recognize `NP_CE64_1`, `NP_PSSI_6` and `NP_PSSI_7`; Quick Format retains the card's existing identity and settings. Recording headers still select the appropriate V1/V2 decoder, and legacy writer eligibility remains separate. See [catalog maintenance checks](docs/catalog-maintenance.md).

NP2 updates are APP-only. Factory / USB service replacement still uses separate firmware tooling. NP1 HEX / ROM DFU operations require STM32CubeProgrammer CLI and the reviewed firmware layout described in [the inherited firmware manager contract](Neuropixels/FIRMWARE_FLASH.md).

The three checked four-shank NP2/type2013 examples are in [ProbeConfig](ProbeConfig). Load them using **Probe configuration… → Load IMRO / JSON…**, and choose the physical connector explicitly; IMRO filenames do not specify a connector. Save the request and select **Apply before next recording** to stage it during preparation. Matching device receipts confirm application.

The source build also includes resident V2 USB self-test and manual rail-hold actions under **Advanced → Diagnostics & self-test…**. These require compatible resident firmware in idle USB-stream mode and attest the selected MCU UID. Rails stay held until explicit Release or MCU reset; the report shows requested GPIO levels, not measured voltages. Release is followed by an explicit normal-mode reboot before acquisition. V1 powered tests remain disabled because its firmware has no compatible command. The retained firmware reports contain failed and skipped checks, and live GUI USB invocation is unqualified. See [self-test integration evidence](docs/v2-usb-selftest-integration.md).

## Install and run

The current installer is [WILDX BLE connection fix](LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.44-V1-HS-FS2-BLE-fix.exe), embedded version 1.0.0.44. It fixes the V1 FS2 connection timeout after Ready; 28 focused checks and all 45 extracted payload files passed. The high-speed firmware bundle is unchanged and the complete matched stack remains unverified. See [fix and validation](Note/BLE_V1_FS2_CONNECTION_FIX_20261019.md) and [firmware scope](Firmware/V1/README.md). The latest root contains one final installer.

The installer installs to `%LOCALAPPDATA%\WILDX_console`, creates its own Start menu entry and uninstall registration, and offers **Run WILD-X** on completion. Its bundled firmware is recorded in the release manifest; [separate newer firmware candidates and limits](Firmware/README.md) are published independently. Firmware programming requires an explicit device and image selection.

[Release manifest](LATEST_INSTALLERS/release-manifest.json) records the installer, every extracted runtime/firmware file, firmware publication provenance and outstanding hardware qualification.

A separate [V1 NP identity-parity candidate](Firmware/V1/README.md), APP `8FD89229` / unchanged USB `40D39075`, is available for parallel user validation. It accepts both NP1 part variants and retains EEPROM-less NRIC support. Software/build checks and APP readback passed; BLE recording and fused programming remain unvalidated. This candidate is not in the 1.0.0.37 installer.

The bundled V2 development firmware is APP `136172A5`, resident USB `2E07799E`, loader `6408DBB8`; the earlier `F4057C04` SD pairing remains historical evidence in the archived 1.0.0.33 release. The 1.0.0.37 installer bundles V1 Neuropixels APP `BC6B506E` / USB `40D39075`; V1 camera firmware is a separate explicitly selected candidate. These firmware owners reconfirmed the artifacts on 2026-10-17. Device-applied NP2013 requests require advertised capability and matching receipts. Degraded selected-shank receipts retain the requested configuration with explicit unverified mapping metadata. V2 auto recovery requires a clean verified prefix and creates a new timestamped recording with an unknown gap. This does not establish uninterrupted capture or fix WinUSB error 31. New installer-pair, endurance and implant qualification remain outstanding.

Requirements:

- 64-bit Windows with .NET Framework 4.8.1.
- .NET 8 Desktop Runtime (x64) for the bundled channel selector; the updater also uses .NET 8.
- The existing device WinUSB driver / firmware interface and a Windows BLE adapter for BLE control.
- Administrator access when launching the console, retained for raw card-reader access.
- Optional STM32CubeProgrammer CLI for NP1 ROM DFU and optional FFmpeg for MP4 conversion (`FFMPEG_EXE` can point to its executable).

For USB recording, use **USB Mode**, select the device, choose an output folder and recording settings, then Start. In **BLE Mode**, scan or enter the exact address and connect. The app reads and confirms the device's control version before enabling matching controls and profiles. No version selection is required. Apply a supported profile before starting continuous SD recording, then Stop explicitly and wait for firmware-confirmed drain/completion. Disconnecting BLE does not stop onboard recording. **BLE Mode → Recordings** provides capability-gated listing and management controls with confirmed formatting; V2 file listing/deletion requires firmware capability22, absent from the current bundled firmware. Use **Data Download → Refresh sources** for USB or card-reader downloads. Switching tabs does not scan hardware or start an operation.

For NP2 USB download, use **BLE Mode → USB streaming** to enter USB service, then **Advanced → USB download** and refresh Data Download sources. **Advanced → USB streaming** returns to live acquisition mode. NP1 also has a direct BLE USB download handoff. A successful handoff disconnects BLE and releases the workspace. Firmware management retains the exact MCU UID across enumeration.

NP1 firmware operation records are saved under `%LOCALAPPDATA%\WILDX_console\FirmwareOperations`; NP2 helper logs are saved in `Documents\WILDX firmware logs`.

## Build and offline checks

Install Visual Studio / Build Tools with MSBuild and the .NET Framework 4.8.1 targeting pack, plus the .NET 8 SDK. NSIS is required only for installer generation. Restore requires the Windows SDK contracts / WinRT packages or their local NuGet cache; the existing OpenTK packages are retained locally.

```powershell
.\build.ps1
.\test.ps1
.\build.ps1 -Installer
```

The main project is `USB512_interface\USB512_interface\WILDX_console.csproj`; `WILDX_console.sln` opens it. Output is `USB512_interface\USB512_interface\bin\Release\WILDX_console.exe`, with adjacent `NPConfigurator` and `FirmwareUpdater` helper folders. Installer output is `WILDX_console-Setup-<application-version>.exe` in that Release directory.

`test.ps1` checks fragmented / corrupt / stale BLE receipts, automatic device-version detection and its fresh read-only confirmation, stopped-only clock commits, read-only clock checks, preview framing / ADC decoding / session admission / cancellation, exact-device selection, NP1 / NP2 busy and mode gates, native NP2 catalogs, failed and truncated extents, UI layout, the NP1 resident update flow, management forms, NP1 startup decoding and NP2 SD-to-DAT export. UI tests render controls in an offscreen host without invoking device discovery handlers. These checks do not scan BLE, connect devices, record, flash firmware or access a physical disk.

## Hardware qualification still required

- NP1 and NP2 sustained USB recordings: startup, sample / timestamp integrity, previews, Stop and reconnect.
- BLE cold connect, fragmented receipts, profile application, SD start / stop / drain, disconnect / reconnect, time sync and every mode handoff.
- NP1 and NP2 recording download: USB and card reader, canceled / interrupted transfers and exported channel / tick integrity compared with the recorded input.
- Exact-UID firmware update: preflight, CRC commit / readback, interruption / recovery, protected-region preservation and a fresh recording afterward.
- Installer launch, helper runtime discovery and clean install / uninstall on a separate Windows machine.

Keep the existing WILD_console paths until these checks pass. Software regression results do not establish physical transport reliability or firmware qualification.

## Source provenance

[Extraction provenance](Neuropixels/extraction-provenance.json) records the source roots and commits. Inputs were working source snapshots, including existing local modifications, followed by WILDX integration changes. The source repositories were not edited. Inherited documentation describes the originating implementations and release history; the release status and build commands in this README apply to WILDX.
