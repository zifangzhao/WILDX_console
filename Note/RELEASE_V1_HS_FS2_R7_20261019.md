# WILDX high-speed FS2 r7 bundle — 2026-10-19

Includes the exact checked **r7 / 1.0.0.44 GUI**, NP/FS2 APP **654824DE**,
high-speed USB **02FF93A9**, ABI258 recovery and matching fused/component HEX.
The embedded app version is preserved. No application/firmware rebuild or new
fused merge. Forced full-speed FACEA0D2 and private APP60EC609E are not bundled.
V1Camera and V2 firmware/targets remain unchanged.

**The matched stack remains unverified.** APP654824DE + HS02FF93A9 + r7 passed
short P1+P2/20k SD/BLE capture, missing-descriptor/Mount handling and a strict
full new-extent SWD export. No current exact-tuple USB probe-streaming receipt
is recorded. The matching r7 GUI is now included rather than the older GUI.

The same HS service/r7 passed all88,080,384 reference bytes in2040ms
(~41.17MiB/s including host comparison),170requests/170completions,zero errors,
retries or recovered reads, with all31 FS2 paired descriptors preserved and
clean BLE APP return. That authorized retest preserved **private APP60EC609E**;
it does not establish the exact bundled APP654824DE pair's full acceptance.
Cable/reseat/direct connection/meter removal changed together; a meter-only
cause is not proven. Earlier failures belong to the previous connection path.

The <=30s interrupted-tail target and physical power-removal recovery remain
unverified. No all-mode, camera or implant qualification is claimed. Initial
FS2 formatting creates a fresh epoch and erases data; there is no automatic
legacy migration. Do not downgrade or routinely format existing FS2 recordings
for recovery. APP-only updates preserve USB/recovery and do not install them.

The fused HEX is unchanged from commit5d15df7, SHA256
98B0877430452323D8A21F9A219A431A4992860F87D667D78EE3D0D00175ABC3.
It excludes configuration0x0801C000..0x0801DFFF. Do not mass erase. Component
files, original build/fused receipts and scoped HS/SD evidence are retained.
No hardware test, flash, reset or format is part of this publication.
