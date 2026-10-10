# USB console system information

The main window shows firmware availability, the actual USB device release,
USB ID/serial, SD identity, total capacity and cached query-session bus fields.
Details organizes the same snapshot into Device, Firmware and software, SD card
and Status sections. Total capacity uses both decimal GB and binary GiB; free
recording space and measured write speed remain unavailable unless reported.

The current V1/V2 protocols have no semantic firmware release field. The GUI
therefore displays **Not reported by firmware** for resident firmware,
SD/BLE application and bootloader versions. NPS1/NPS2 ABI and USB bcdDevice
are labeled separately. Fixed USB release 1.03, ABI values, local firmware
packages and manifest build hashes are never converted into a firmware version.
The console title identifies its own executing assembly rather than uninstall
registry metadata. A semantic firmware self-report extension is a separate
firmware change; none was added for this GUI feature.

On selection or explicit Refresh info, an idle background worker opens a
dedicated EP0 handle. It sends standard GET_DESCRIPTOR, then (verified NP
devices only) passive 0x6E service status and at most one SD query: 0x4B cached
SD Info in streaming role, or 0x60 passive download-service capacity in role3.
Unavailable/stalled/truncated requests retain explicit unavailable fields and
stop the sequence. Other USB512/unidentified devices receive only the standard
descriptor request; no new vendor extension is attempted. MSC physical-drive
devices retain unavailable fields here because they have no WinUSB EP0 handle.

These one-shot synchronous control transfers run on a background thread using
[WinUSB's documented five-second control timeout](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-functions-for-pipe-policy-modification).
There is no polling timer, bulk read, OUT request, card reset/initialization,
mode switch or bulk-pipe configuration. Full SD Info remains an explicit idle
action; its existing identification operation is never invoked automatically.
Cached bus values describe that old query session, not recording speed or the
recording application's bus configuration.

While the worker owns EP0, Start, firmware actions, download, configuration,
impedance, enumeration and device selection are excluded. Recording and retry
states prevent a new query. Delayed results are discarded after a selection
generation/path change or a recording start. Details renders only cached data.
