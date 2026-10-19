# V1/V2 USB catalog repair parity

The shared host source supersedes the V2-only handoff. It has not been compiled
or published in this turn. The product remains 1.0.0.45; the next console
compilation increments automatically, without a separate manual bump.

USB Mode and Data Download now expose the same Repair SD catalog action for
V1 and V2. Both use NpUsbCatalogRepair and the same inspect, confirmation,
one-shot repair, 60-second receipt polling, and result handling. The existing
EnsureDownload paths retain generation-specific mode entry. Neither repair
path falls back to Format or BLE tail reclaim.

Generation is read from the authoritative 0x6E service identity. Prefixed paths
and later service replies must agree; legacy bare UID paths can identify either
generation. Both parsers retain their existing ABI and UID checks. V1 must
advertise capability 0x20, while V2 uses 0x10. V1 bit0x10 is existing ROM DFU:
it must never authorize A0/A1/A2. Old V1 firmware receives an explicit matching
USB-service update message before any catalog command is sent. APP-only
updating cannot supply the new USB backend.

The shared NCR1 receipt keeps nonce, CID/capacity, epoch, inspected nonce and
terminal CRC checks. Unknown errors retain their numeric value. V1 catalog
flags remain raw because layout-specific meanings are not frozen. V2 FULL is
interpreted only on V2; successful repair implies neither free space nor
payload integrity. Unconfirmed outcomes leave the old listing invalidated and
never replay a mutation; only confirmed success refreshes the list.

## Actual support boundary

The V2 owner reports a frozen firmware adapter and passing native/EP0 tests;
the coordinated console build and its targeted regressions remain pending.

V1 USB0E542BBD/APP654824DE does not implement preservation repair. Its developer
has reserved the matching transport with capability0x20 and is implementing the
new backend separately. The capability must stay clear until real support is
verified. FS1 journal replay can encode a delete or shrink, so calling existing
mount blindly is unsafe. FS2 mount is read-only and has no existing copy-healing
operation. New repair must prove preserved allocation/count, bind the exact
authority, and refuse unknown/conflicting state without writes. Missing record
descriptors cannot be invented. This note does not claim V1 firmware parity is
already ready or published.

Existing USB Format remains V1-only, with its separate erase confirmation;
read-only Recover recordings retains its V1/reader scope. Those are distinct
capabilities, not substitute repair implementations. BLE storage maintenance
retains its existing generation-specific contracts.

## Source and pending checks

Shared helper: `USB512_interface/USB512_interface/NpLogger/NpUsbCatalogRepair.cs`.
Shared entry points: NpDownloadForm.cs and mainForm.WildX.cs.

```powershell
./test_usb_catalog_repair.ps1 -OutputDirectory ./_artifacts/usb-catalog-repair-tests -ConsoleAssembly <absolute-path-to-coordinated-WILDX_console.exe>
```

The actual-source fake EP0 checks cover both generation identities, legacy bare
UID paths, V1 ROM-DFU capability rejection without catalog traffic, shared
transactions, generation changes, unknown V1 flags, lost replies and existing
receipt/CRC/nonce/ownership cases. The optional actual compiled form check
covers both generations' button availability, ownership blocking and separation
from format/read-only recovery. Tests have not yet been compiled or executed.
