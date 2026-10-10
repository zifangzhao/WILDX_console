# WILD-X Neuropixels Console

Independent Windows console for Neuropixels devices. Version **1.0.0.0**, engineering release of 2026-10-10. Installer payload and offline V2 compatibility are verified; complete hardware qualification is pending.

The application uses the Neuropixels acquisition and preview code from USB512_console / USB_PCconsole, the BLE control and catalog contracts from WILD_console, and the NP2 firmware updater from XE_NP. All required source and helper projects are stored here; building and running WILDX does not require those repositories.

The original Neuropixels entry points in WILD_console remain available until WILDX passes hardware checks, as requested.

The modern workspace uses four separate tabs, with WILD's teal palette, white cards, Segoe UI typography and resizable layouts. **USB Mode** contains USB acquisition, live previews, device health and time status. **BLE Mode** embeds wireless discovery, connection, profiles and onboard SD recording controls. **Data Download** contains the recording browser, USB / card-reader downloads, NP1 raw-archive decoding and media export. **Advanced** contains firmware / USB mode management, BLE firmware-loader / ROM DFU handoffs and guarded SD catalog formatting. An active capture, download or firmware operation blocks switching workspaces. Finish pending BLE commands, stop scanning and disconnect BLE before changing tabs. Downloads can be canceled without losing the operation's controls. Automatic firmware update checks and prompts are deferred for later implementation.

Layout previews: [USB Mode](docs/screenshots/usb-mode.png), [BLE Mode](docs/screenshots/ble-mode.png), [Data Download](docs/screenshots/data-download.png), [Advanced](docs/screenshots/advanced.png). These are rendered from the built application with hardware discovery disabled.

The shared theme uses a neutral gray canvas, white cards, soft gray input/log surfaces and a single teal accent. Selected tabs and primary actions share that accent; secondary actions, borders, hover/pressed states and disabled controls use the same palette throughout the workspaces and management/settings dialogs. Destructive actions retain a muted red treatment, and live telemetry retains its distinct signal colors.

## Capabilities

| Workflow | NP1 / V1 | NP2 / V2 |
| --- | --- | --- |
| USB streaming | AP / LFP recording, probe configuration, preview, supported camera / IMU streams | Dual-probe AP recording and preview through the native V2 USB decoder |
| Firmware update | Existing validated APP updater and guarded HEX / ROM DFU manager | Bundled transactional `.xenp` APP updater, CRC commit and protected resident USB / loader regions |
| Data download | USB and read-only card-reader catalog export; raw archive download and offline decoding | USB and read-only card-reader catalog export to classic per-probe DAT files |
| BLE control | Device status, profiles, channel staging, SD start / stop, PC time, catalog mount / recovery and USB mode handoff | Device status, supported profiles, SD start / stop, automatic PC-time initialization, read-only clock checks, NP preview, catalog mount / recovery and USB service / loader handoff |

Profiles and mutations are checked against the connected device's firmware status and capabilities. BLE commands use the FFF0 service, FFF1 notifications and FFF2 writes, correlate CRC-valid receipts, and stop on uncertain outcomes without replaying commands. Full-rate recording uses USB or the onboard SD recorder. BLE provides controls and an expendable display preview.

On V2 connection, a stopped recorder receives 64 valid PC-time observations and one anchor commit. Attaching to an active recorder only reads and verifies its existing clock. The two-second status refresh also checks clock generation and offset without committing time. V2 Start requires confirmed PC time; failed initialization leaves status and Stop available. Use **Sync PC time** while stopped to recover an unconfirmed mapping.

**BLE Mode → NP Preview** displays three ADC lanes for a selected probe and base channel during an existing V2 SD recording. The host validates a complete SuperSync calibration, then decodes rolling packets at 500 samples/s using the native unsigned 12-bit ADC mapping. Session, rate, selection and connection must match. The plot shows missing samples and gaps without interpolating them; it is a display preview, with no filtering or LFP claim. Preview traffic yields to controls and uses a bounded queue. Closing or pausing the preview releases its subscription and lets the device lease expire; it never sends SD Start or Stop.

Download validates the selected source, catalog, committed recording extent and generation-specific metadata. NP2 export validates the dual-probe descriptors and footer and does not synthesize an LFP stream. Existing native media export is included; MP4 conversion requires an external FFmpeg installation.

V1 and V2 use the same outer CE64 catalog. Desktop listing, removable-card inspection and explicit Quick Format recognize `NP_CE64_1`, `NP_PSSI_6` and `NP_PSSI_7`; Quick Format retains the card's existing identity and settings. Recording headers still select the appropriate V1/V2 decoder, and legacy writer eligibility remains separate. See [catalog maintenance checks](docs/catalog-maintenance.md).

NP2 updates are APP-only. Factory / USB service replacement still uses separate firmware tooling. NP1 HEX / ROM DFU operations require STM32CubeProgrammer CLI and the reviewed firmware layout described in [the inherited firmware manager contract](Neuropixels/FIRMWARE_FLASH.md).

The three checked four-shank NP2/type2013 examples are in [ProbeConfig](ProbeConfig). Load them using **NP2 IMRO / channel maps… → Load IMRO / JSON…**, and choose the physical connector explicitly; IMRO filenames do not specify a connector. Current V2 imports remain host-only, not hardware-applied configurations.

The source build also includes resident V2 USB self-test and manual rail-hold actions under **Advanced → Firmware Update**. These require compatible resident firmware in idle USB-stream mode and attest the selected MCU UID. Rails stay held until explicit Release or MCU reset; the report shows requested GPIO levels, not measured voltages. Release is followed by an explicit normal-mode reboot before acquisition. The retained firmware reports contain failed and skipped checks, and live GUI USB invocation is unqualified. See [self-test integration evidence](docs/v2-usb-selftest-integration.md).

## Install and run

Use [the current engineering installer](LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.1.exe). It installs to `%LOCALAPPDATA%\WILDX_console` and creates its own Start menu entry and uninstall registration. It does not replace WILD_console or USB_PCconsole and does not bundle or select a firmware image.

[Release manifest](LATEST_INSTALLERS/release-manifest.json) records the installer, all 13 extracted payload checksums, compatible V2 firmware identities and outstanding hardware qualification. The older pre-integration installer remains under `LATEST_INSTALLERS/legacy`.

The installer includes V2 clock initialization/checks, BLE preview, catalog maintenance and corrected USB self-test reports. Version 1.0.0.1 adds **NP2 IMRO / channel maps…** in USB Recording settings and BLE Mode, with **Load IMRO / JSON…** and **Edit NP2 channel map…**. Requests are separate per connector and host only: current V2 firmware cannot apply these channel maps. USB recording can retain requested/unverified IMRO metadata; BLE imports do not alter onboard SD recording. Save the map in the configurator to reuse it between BLE and USB. Standard NP2 types 21/2003 and 24/2013 are supported; UHD/type 1110 is not NP1/NP2. See [IMRO release checks](Note/NP2_IMRO_RELEASE_20261010.md) and [previous release checks and limits](Note/RELEASE_20261010.md). The paired engineering logger remains APP `ED881256`, resident USB `DFB8F25E`, loader `97C3716A`. Self-test actions separately require newer resident firmware such as `C3C76C43`; this installer does not automatically update firmware. Offline compatibility is not live end-to-end recording/update qualification.

Requirements:

- 64-bit Windows with .NET Framework 4.8.1.
- .NET 8 Desktop Runtime (x64) for the bundled channel selector; the updater also uses .NET 8.
- The existing device WinUSB driver / firmware interface and a Windows BLE adapter for BLE control.
- Administrator access when launching the console, retained for raw card-reader access.
- Optional STM32CubeProgrammer CLI for NP1 ROM DFU and optional FFmpeg for MP4 conversion (`FFMPEG_EXE` can point to its executable).

For USB recording, use **USB Mode**, select the device, choose an output folder and recording settings, then Start. In **BLE Mode**, scan or enter the exact address and connect. The app reads the device's control version and confirms it with a fresh status transaction before enabling matching controls and profiles. No version selection is required. Apply a supported profile before starting SD recording. A duration reserves storage; it is not a precise recording timer. Wait for firmware-confirmed completion after Stop and drain. Disconnecting BLE does not stop an onboard recording. Use **Data Download → Refresh sources** to choose a USB device or card reader, then list and download saved sessions. Switching tabs does not scan hardware or start an operation.

For NP2 USB download, use **BLE Mode → USB streaming** to enter USB service, then use **Advanced → Firmware Update → USB download** and refresh Data Download sources. NP1 also has a direct BLE USB download handoff. A successful handoff disconnects BLE and releases the workspace. Firmware management selects an exact MCU UID and retains it across enumeration; a disappeared target is not silently replaced.

NP1 firmware operation records are saved under `%LOCALAPPDATA%\WILDX_console\FirmwareOperations`; NP2 helper logs are saved in `Documents\WILDX firmware logs`.

## Build and offline checks

Install Visual Studio / Build Tools with MSBuild and the .NET Framework 4.8.1 targeting pack, plus the .NET 8 SDK. NSIS is required only for installer generation. Restore requires the Windows SDK contracts / WinRT packages or their local NuGet cache; the existing OpenTK packages are retained locally.

```powershell
.\build.ps1
.\test.ps1
.\build.ps1 -Installer
```

The main project is `USB512_interface\USB512_interface\WILDX_console.csproj`; `WILDX_console.sln` opens it. Output is `USB512_interface\USB512_interface\bin\Release\WILDX_console.exe`, with adjacent `NPConfigurator` and `FirmwareUpdater` helper folders. Installer output is `WILDX_console-Setup-1.0.0.0.exe` in that Release directory.

`test.ps1` checks fragmented / corrupt / stale BLE receipts, automatic device-version detection and its fresh read-only confirmation, stopped-only clock commits, read-only clock checks, preview framing / ADC decoding / session admission / cancellation, exact-device selection, NP1 / NP2 busy and mode gates, native NP2 catalogs, failed and truncated extents, UI layout, the NP1 resident update flow, management forms, NP1 startup decoding and NP2 SD-to-DAT export. UI tests render controls in an offscreen host without invoking device discovery handlers. These checks do not scan BLE, connect devices, record, flash firmware or access a physical disk.

## Hardware qualification still required

- NP1 and NP2 sustained USB recordings: startup, sample / timestamp integrity, previews, Stop and reconnect.
- BLE cold connect, fragmented receipts, profile application, SD start / stop / drain, disconnect / reconnect, time sync and every mode handoff.
- NP1 and NP2 recording download: USB and card reader, canceled / interrupted transfers and exported channel / tick integrity compared with the recorded input.
- Exact-UID firmware update: preflight, CRC commit / readback, interruption / recovery, protected-region preservation and a fresh recording afterward.
- Installer launch, helper runtime discovery and clean install / uninstall on a separate Windows machine.

Keep the existing WILD_console paths until these checks pass. Software regression results do not establish physical transport reliability or firmware qualification.

## Source provenance

[Extraction provenance](Neuropixels/extraction-provenance.json) records the source roots and commits. Inputs were working source snapshots, including existing local modifications, followed by WILDX integration changes. The source repositories were not edited. Inherited documentation describes the originating implementations and release history; the candidate status and build commands in this README apply to WILDX.
