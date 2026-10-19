# V1 matched high-speed / FS2 readiness — updated 2026-10-19

**HS service/host PASS on the corrected direct connection. Exact frozen-pair
binding remains pending.** No new development defect is established by this
result. This is an evidence update only: no build, tests, merge, hardware,
packaging or publication was performed here.

USB **02FF93A9** and the frozen **r7** console read all **88,080,384 reference
bytes** identically in **2040ms**, approximately **41.17MiB/s** including host
comparison. The production exact-UID WinUSB path negotiated HS512-byte endpoints
and used1020-sector requests: **170 requests/170 completions,0 firmware errors,
0 retries and0 recovered reads**. The same stream mounted FS2 and checked all31
paired descriptors; it did not read every payload in all31 records.

Independent full2MiB installation dumps matched the exact USB-only page-erase
model. Boot, APP and retained settings were preserved. BLE return confirmed
Idle0/fault0/P1+P2-15k/count31/frontier63471616/gen534, same epoch and unchanged
explicit extents. There was no new recording, format, delete or SD payload write.

The human changed to a direct USB connection and removed the inline power meter;
cable/reseat/direct-path/meter removal changed together. **A meter-only cause is
not proven.** Earlier HS failures remain old-path evidence, not a universal
statement that the current HS service fails.

The retest preserved **APP60EC609E**, the private power trial. It is not promoted
or substituted for frozen **APP654824DE**. The latter already passed short
P1+P2/20k SD/BLE capture with the same HS02FF93A9/r7:128chunks, full16MiB new-extent
SWD readback/strict8MiB payload export, zero known NP continuity errors,128 IMU
and magnetometer snapshots, and preserved prior extents. The earlier stopped
descriptor-read-unavailability/Mount acceptance remains scoped to that receipt;
append was after read-hook removal/cache recovery.

**Specific remaining gap:** no corrected-path production HS read/return receipt
is bound to **APP654824DE + USB02FF93A9 + r7**. The checked handoff also lacks a
USB probe-streaming mode receipt for that exact current tuple. The new result
does not call for a firmware rebuild or broad requalification. Main owns any
further exact-pair hardware decision. Historical P12/native20 USB streaming
passes belong to APPBC6B506E/USB40D39075/WILDX33 and retain that scope.

| Deliverable | Identity / status |
| --- | --- |
| Frozen NP/FS2 APP | `654824DE`, SHA256 `26C36C71A58CA7C05E7FBDDD68348C844C8603465D4123A77047FAB38A6B32AF` |
| Existing HS USB | `02FF93A9`, SHA256 `D35FB5091317F3AA085A3A75CDD3B8ABCD8918247519BBAC8A3EE4627A1DC58F` |
| Checked r7 console | `1.0.0.44`, SHA256 `0BF2DBDC3A9D2407620EF1DB1D7C7270A33793FCA67CF8C9655048358B126DAA` |
| Recovery | ABI258; boot anchor SHA256 `03CBCA29117EA3C54F797F5CD9698E786BC5D0FFA73853FEBAD3431B425E3A7F` |
| Matching frozen-APP fused HEX | Already published as **unverified**, commit `5d15df743e8c5b10ed476548d65d75fa8a088f47`; SHA256 `98B0877430452323D8A21F9A219A431A4992860F87D667D78EE3D0D00175ABC3` |
| Matching current r7 installer | [Latest r7 HS/FS2 bundle](../LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.44-V1-HS-FS2-r7.exe), published as **unverified**; exact-pair qualification remains pending |

The fused HEX and matching components already came from one direct verified-input
merge with configuration0x0801C000..0x0801DFFF excluded. Reuse their exact bytes
while inputs remain unchanged. Any changed service/APP needs a refreshed matching
fused deliverable. Do not restore FACEA0D2 full-speed, drop FS2 support, substitute
the power trial, or downgrade/format existing FS2 media to use the legacy fallback.

The **<=30s interrupted-tail** tolerance remains an acceptance target; physical
power-removal recovery within that bound is unverified. This HS result makes no
new streaming, SD capture, endurance, camera or power-removal claim. V1Camera
and V2 remain separate and unchanged.

Authoritative source:
`C:/Frank/Code/XE_NP/_artifacts/v1_hs_cable_retest_20261019_r1/acceptance.json`.
Readback receipt SHA256:
`F5D59648100860FABFEDB3E6FD1977CD1E7ECC5C9E31C780F79ECCE71883D4F2`.
Updated machine-readable readiness:
`D:/Code/WILDX_console/WILDX_console/_artifacts/v1_matched_hs_fs2_readiness_20261019/readiness.json`.

The human subsequently requested the latest bundle: frozen r7 GUI + APP654824DE + HSUSB02FF93A9 + existing fused98B08774… are packaged without a rebuild. This publication does not promote the private power trial or change the measured qualification scope.
