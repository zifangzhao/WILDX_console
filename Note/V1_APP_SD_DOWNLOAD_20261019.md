# V1 USB SD initialization/read update — 2026-10-19

Checked USB service: `22D0792B`, version `1.0.94-app-sd-init`.
Frozen WILDX48 executable: `CB392725EE11C8A607D6FECAA013ADC6383BA419C4FAA1046CBB838A366B5486`.

The resident USB source now composes the maintained APP's 31 SD initialization
functions, clock-source constants, tuning tables and SD-only MSP. It uses the
APP's PLL2Q 120 MHz SD kernel and PF2 power/tri-state sequence, without changing
USB system/capture clocks. The reset helper admits an uninitialized handle:
the first candidate faulted in `SDMMC_PowerState_OFF(NULL)` before this guard.
That failed candidate is retained as evidence and was not published.

The fused FIFO/CRC reader now retires CMD18 with bounded CMD12, DAT0-idle and
CMD13 READY_FOR_DATA/TRANSFER checks before declaring READY. It preserves late
data faults, has a bounded timeout and leaves a sticky error until explicit
reinitialization. No silent read replay, format, reclocking or protocol change.
One native actual-reader regression passed, including delayed/stuck/wrapped
retirement, ambiguous CMD18 errors, CRC/timeout/overrun, exact bytes and DATAEND.

## Connected-device result

Exact ST-Link3439 / MCU `001D00065942500B20313354`, card
`1B534D4A453259373010B35487A1A300`, preserved private APP `60EC609E`:

| Actual WILDX48 production API operation | Result |
|---|---|
| 84 MiB raw download, all bytes compared to reference | 2.417 s; 36.44 MB/s |
| Independent 1 GiB read, pass1 | 29.190 s; 36.78 MB/s |
| Independent 1 GiB read, pass2 | 29.204 s; 36.77 MB/s; identical SHA256 |
| Complete P12/20 kHz export | 32.78 MB/s payload; 21 identical data files |
| Read recovery/error counters | 0 retries, 0 recovered reads, 0 errors |
| Return through USB to SD/BLE | Idle0/fault0; P12/15k;31 records; frontier63471616 |

Only metadata software version and creation time differ from the reference
export. Two complete flash readbacks match the USB-only page-erase model;
boot, APP and configuration were preserved. No SD writes or new recordings.
The final linker map fits the unchanged flash/RAM/DMA regions; stack top
0x200186D0 remains below the0x2001C000 USB-DMA boundary.

This does not prove a speedup over the old USB on this local device: the prior
local result was44 MB/s. It does demonstrate sustained36–37 MB/s with the new
initialization/read-retirement path. The other PC's11.2 MB/s and Yoshi's
0xFFFFFFFE errors were not reproduced remotely, so they remain unconfirmed.
No new streaming, recording, camera, interruption or endurance qualification.

## Delivery

One direct merge of the exact checked USB with unchanged current boot/recovery
and release APP `654824DE`. The local HIL used APP `60EC609E`; the exact bundled
APP654824DE pair was not newly device-tested. All current V2/camera/runtime
bytes are reused from frozen48; no console compile or private telemetry edits.
Fused complete-image loading and all catalog identities/hashes pass the actual
frozen48 loader. One NSIS pass; all40 extracted payload files match inputs.

- Fused SHA256: `384EB564A77A638ECF8F3C73997373EDD7DC27337B43C34015FF1CC13A7B2AC5`
- Installer SHA256: `63BA3EBA92AD14A2DA77A4B64F08974DA67977A65ACCB1E2DB7B091869BC9713`
- Evidence: `_artifacts/v1_usb_app_sd_init_20261019_r3/acceptance.json`,
  `install-verification.json`, `delivery/download-init-manifest.json`,
  `release48/package-verification.json`.

The catalog's source commit remains the existing base anchor, explicitly
labelled as such. Exact local composition inputs are hashed; a clean public
source-HEAD reconstruction is not claimed.
