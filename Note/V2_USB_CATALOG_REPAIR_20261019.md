# V2 USB SD catalog repair — source handoff

Superseded by `V1_V2_USB_CATALOG_REPAIR_20261019.md` for the shared host source,
renamed helper/regression commands, and V1 capability/implementation limits.

Status: implemented in canonical console source; no application compilation,
test compilation/execution, packaging, publication, or hardware action in this
handoff. The released console remains 1.0.0.45. Its next compilation increments
automatically through BuildVersion.targets; do not manually bump first.

Repair SD catalog is available in the existing USB information card and Data
Download card actions. Both enter the exact selected V2's idle download mode
through the existing EnsureDownload path and use one shared workflow.
Read-only Recover recordings remains separate and retains its V1/reader scope.

The workflow checks 0x6E identity/capability 0x10, queues one A1 inspection,
and confirms the UID, CID, capacity, and catalog state. After confirmation it
rechecks the exact inspection, queues A2 once, and polls cached A0 receipts for
up to 60 seconds. Nonce, USB epoch, raw CID/capacity, inspected nonce, terminal
state, and terminal CID CRC must match. Pending CID CRC may be zero. An initial
cached report's epoch is not an admission witness. Nonces never wrap.

Lost acknowledgement, cancellation, timeout, or lost/mismatched receipt reports
an unknown outcome without retrying A2 or aborting/resetting a firmware job.
The old listing is invalidated before the repair attempt. Only a matching
successful receipt triggers a fresh recording list. Success means metadata
repair; FULL flags remain visible and imply no free-space or recording-ready
claim. Payload integrity and lossless recovery are not claimed.

Old USB firmware receives a clear matching USB-service update message; APP-only
updating is insufficient. This feature adds no format, erase, rewind, recording
Start, bulk-stream cancellation, or automatic repair-on-list/download behavior.

Protocol owner: V2 firmware chat. Frozen header:
`D:/Code/XE_NP_V2/Core/Inc/xe_np_usb_catalog.h`.
Header SHA256 at handoff:
`8FD2BB3D4B8A9E1B7D0695BD6482CF2A9AF0CE5FB82968E35325ABC25477B4C2`.

Pending coordinated regression command (run after the matching app build):

```powershell
./test_v2_usb_catalog_repair.ps1 -OutputDirectory ./_artifacts/v2-usb-catalog-repair-tests -ConsoleAssembly <absolute-path-to-coordinated-WILDX_console.exe>
```

The runner compiles the actual shared workflow and native EP0 boundary against
throw-on-unexpected-command fakes. It covers eligibility/ownership, fresh
confirmation, one-shot A1/A2, nonce serialization, stale card/epoch/inspection,
CID CRC, future numeric errors, lost replies, cancellation, and FULL completion.
It also checks the actual compiled download form's button placement and gates
without enumeration or hardware. These checks have not yet been run.
