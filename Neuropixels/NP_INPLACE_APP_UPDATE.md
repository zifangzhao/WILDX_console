# V1 same-interface APP update integration

USB_PCconsole 1.0.3.27 is a source/build candidate for the V1 firmware contract
in XE_NP/Tools/V1Capture/UnifiedUsb/INPLACE_UPDATE.md. No hardware qualification
or installer promotion is implied. The 1.0.3.26 release delivery remains the
qualified dual20 recording host.

Firmware Update enables Update SD app directly for stopped B386 firmware
advertising NPS1 capability bit 64. The selected full 24-hex UID must enumerate
uniquely. NPS1 identity/ABI/UID, busy=0 and pending-mode=0 are checked before
vendor 0x6f target 4. Role 4, idle and no pending transition must then be verified
on the existing handle/path/PID; no B387 enumeration or replacement device is
used. Firmware filenames never select this path.

The existing strict APP-only HEX/XENP validator and protocol-v2 APP writer are
reused: INFO checks ABI/capacity before BEGIN, DATA is sequential with CRCs,
COMMIT validates the published APP, and commands use 30-second timeouts on
bulk 0x01/0x81. Successful updates initiated in streaming/download role return
to that original role and verify it on the same interface. An update begun in
already-selected role 4 retains it because the host cannot infer its original
personality. Changing personality through a manual mode button still uses the
existing exact-UID verification path and may reset/re-enumerate USB.

Flash errors stop without automatic retry, COMMIT, ABORT or mode exit. Read
status before the explicit Abort APP update action. ABORT clears a volatile
transaction; an uncommitted APP remains invalid. The form retains the original
role for the selected UID after a failed update and verifies its return after
an acknowledged ABORT. Selecting a different UID clears that return state.
If no original role is known, update mode remains idle for manual selection.
An uncertain or corrupt reply fails closed; no pipe reset or silent replay is
introduced. Physical disconnect/interruption handling remains a bench gate.

Older B386 firmware does not gain APP-update permission without bit 64. Its
existing Recovery button and B387 updater remain available; legacy recovery
updates stay in recovery. Guarded ROM DFU remains unchanged. This APP updater
cannot replace the running USB service, recovery loader, configuration or
option bytes. USB-service replacement retains the separate ROM DFU/SWD path.

Offline checks compile the maintained manager against an in-memory WinUSB
model (only its private WritePipe P/Invoke declaration is redirected), and
exercise the actual Release form and APP validator. No discovery/native USB,
flash or installer execution occurs. A physical same-path update, protected
region readback, interruption/recovery and fresh recording gate are required
before qualifying this integration with the new firmware candidate.
