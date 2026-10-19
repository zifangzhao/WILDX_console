# V1 FS2 BLE connection fix — 2026-10-19

V1 FS2 uses bit 0x02000000 for storage support. The generic firmware identity
capability uses the same bit for V2. The console incorrectly sent V1 an optional
0xbc identity query after its mandatory handshake had already returned Ready.
The unsupported query timed out and the transport closed GATT, leaving the
connection workflow waiting before its eventual reset.

The query now requires detected V2 as well as the capability bit. No firmware,
mandatory handshake, timeout policy, recording controls or V2 identity behavior
was changed. This fixes that connection failure; it is not evidence that a
separate probe capture fault is resolved.

The isolated regression passed 28 checks using actual Connect, ReadStatus,
Run, ShowStatus, ResetDevice, Send, ReadDevice/ParseStatus and RefreshV1Media
handler bodies. It reproduced r7's V1 Ready/identity-timeout disconnect, then
checked ordinary/FS2/selected V1 flags, reconnect replacing stale Recording,
optional media timeout, V2 identity success/failure and mandatory handshake
failure. Real WinForms controls and protocol parsers were used. GATT, watcher,
configuration/clock, profile and enabling services were simulated. Identity
parsing was simulated; the identity-timeout fixture models the source-confirmed
transport disconnect catch. Fixture visibility/dispatch adaptations were used.
No physical logger was accessed or modified.

One cached console Compile passed with embedded version 1.0.0.44. All 95 source
inputs were compared against the frozen r7 ledger: only NpBleForm.cs changed,
with the V2 guard and its explanatory comment. The existing exporter DLL is
unchanged. Other private working-tree edits are excluded from the source commit;
the complete compiled input ledger is retained, without claiming public source
equivalence. One NSIS pass and independent extraction verified all 45 payload
files. Only the GUI EXE differs from the previous installer payload.

Firmware remains byte-for-byte unchanged: APP654824DE, high-speed USB02FF93A9,
ABI258 recovery and fused SHA256
98B0877430452323D8A21F9A219A431A4992860F87D667D78EE3D0D00175ABC3.
The user's requested unverified matched-stack publication status remains.
Historical hardware receipts used r7; they are not new hardware validation of
this fixed GUI. No firmware build, flash, reset, format or recording occurred.

[Build/input ledger](evidence/ble_v1_fs2_fix_20261019/build-receipt.json) ·
[Regression receipt](evidence/ble_v1_fs2_fix_20261019/regression.json) ·
[Regression output](evidence/ble_v1_fs2_fix_20261019/regression.log).
