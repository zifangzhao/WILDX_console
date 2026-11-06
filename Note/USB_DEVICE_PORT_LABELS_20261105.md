# USB device port labels

Build1.0.0.239 adds Windows hub/port information before the UID in acquisition,
download, SD-test readback and firmware device selectors. For example:
`WILD-X V2 USB — Hub 4 / Port 3 — 001B0019434250062039314E`.

The shared presentation helper reads the matching USB instance's read-only
Windows `LocationInformation`. Missing, malformed or inaccessible metadata falls
back to name plus UID. Interface paths and UIDs still determine device selection;
the label does not choose a decoder, substitute a device or change recording.
These are Windows hub/port numbers, which can change when a cable is moved.

One app build reused the checked SDK and firmware. Twenty-six focused checks
passed against the actual239 assembly, including distinguishable ports/hubs,
UID fallback, firmware/recovery targets and acquisition binding. A separate
present-interface metadata enumeration showed V2 at Hub1/Port3 and Hub4/Port3,
and V1 at Hub1/Port6, without opening a device control handle.

Run `test_usb_device_labels.ps1 -ConsoleExecutable <exe> -ReadWindowsMetadata`.
Build/source evidence is in `_artifacts/usb_device_labels_20261105`; the compiled
runtime is `C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_host/enabled239-usbports`.
This change has not been packaged or pushed; published installer238 is unchanged.
