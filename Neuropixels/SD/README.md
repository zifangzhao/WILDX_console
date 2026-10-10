# NP2 SD exporter ownership

`NpV2SdDatExport.csproj` is the shared, console-owned strict NP2 SD-to-DAT
exporter. USB_console and WILD_console use normal Copy Local project references
to this project; no firmware checkout, candidate source overlay, or manually
copied DLL is needed. Existing CE64/NP1 decoders remain in their own projects.

The NP2 USB decoder and NP configurator are maintained in this USB_PCconsole
repository. WILD's BLE/control/preview sources belong to CE32_console. Changes
to host applications should be made and reviewed in those repositories.

Build the normal USB512_interface or WILD_console project in Release. Both
bundle this library and build/restore the console-owned NP configurator.
WILD currently expects USB_PCconsole as a sibling checkout (as for the
configurator); preserve that repository layout when building WILD.

Run `../test_wire.ps1` for NP1 compatibility and NP2 wire tests. Run
`../test_sd_dat.ps1` for exact channel/value/byte-order fixtures at 20/30 kHz,
including corrupt/truncated metadata, extent bounds, and cancellation. Its
optional `-ConsoleExecutable` tests the actual built USB or WILD export entry
point and resolves its normal bundled DLL. These fixtures do not establish
hardware transport reliability or qualify additional recording profiles.
