# V1 battery-only BLE startup fix — 2026-10-19

USB service **76E544AB** fixes a source-proven deadlock before the first host
configuration: USB bus cancel events changed virgin download state0 to state4,
which blocked automatic SD/BLE fallback on battery power. The strict virgin
idle case now remains state0 while the epoch still advances. Configured,
active or dirty sessions retain the existing checked cancellation behavior.

**SOFTWARE-VALIDATED, NOT DEVICE-TESTED.** The actual canonical include regression
passed the battery-only case and retirement/epoch safety cases. One incremental
USB-unit compile used cached dependencies; no hardware test was performed, as
requested by the human. B34D50's end-to-end symptom has not been verified fixed.

APP **654824DE**, recovery/boot and ABI258 are unchanged. The exact current fixed
GUI (1.0.0.44, SHA256
03228EC8E7D5B2C41847C86318DCCE9FC8CECE76ADC08A600B6921C54D60AB41)
and exporter DLL are reused without an application build. V1Camera/V2 are
unchanged. This bundle does not include the separate V2 BLE-listing candidate.

The owner-provided component/fused HEX files are copied byte-for-byte. Fused
SHA256 7C637268412F031E9DA6BA6A7306B7389A32593C0DAF68FF0EF2A11E2205D44D
covers firmware flash and excludes retained configuration0x0801C000..0x0801DFFF.
No mass erase is required. APP-only updates do not replace this USB service.
Existing FS2 recordings and configuration must be retained.

Earlier raw-transfer and SD/BLE receipts refer to USB02FF93A9 and their recorded
APP/host combinations. They remain historical evidence, not validation of this
new service. The full matched stack and physical interruption-tail bound remain
unverified. High-speed USB is retained; forced full-speed is excluded.
