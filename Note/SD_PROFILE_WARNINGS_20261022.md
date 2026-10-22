# SD recording profiles and warning policy — 1.0.0.96

The bundled V1 APP654824DE's np_sd_qualification.h returns zero for every
CID/mask/rate. It does not define a card allowlist. This informational flag
therefore cannot establish that a supported combination is prohibited or
that no successful recording tests exist. Real firmware admission still
checks the profile/topology, storage ownership, mount, capacity and geometry.

## Firmware-supported combinations

These are supported selections, not card-specific sustained qualification.
The existing capability-driven menus remain authoritative for older firmware.

| Version / connectors | AP rates (kHz) |
|---|---|
| V1: any single P1, P2 or P3 | 7.5, 15, 20, 30 |
| V1: P1+P2 | 7.5, 15, 20, 30 |
| V1: P1+P3 or P2+P3 | 7.5, 15 |
| V1: P1+P2+P3 | 7.5, 15 |
| V2: P1, P1+P2, or P1+P2+P3 where available | 15, 20, 30; 7.5 only when advertised |

Modern V1 advertises the rate-profile capability for all 22 NP combinations.
Older V1 versions do not gain unadvertised 20-kHz modes; scaled20-only firmware
retains P1+P2-only 20-kHz support. V2 connector availability and live7.5 flags
likewise remain in use. No new START gate or fabricated capability was added.

V1 P1+P2/20-kHz has a bounded five-minute pass on one recorded card, while
P1+P2/30-kHz has a known sustained backlog failure. The V1 developer confirmed
these facts and the matching APP source dependencies from existing evidence.
No old bench result was promoted to a blanket card qualification.

## Fewer repeat popups

- A V1 unreported-validation warning is acknowledged once per selected
  mask/rate/capacity in the current BLE connection. The legacy acceptance flag
  is still sent on every applicable START; the acknowledgement never sets
  the firmware's qualification bit.
- Known-risk V1 dual30 and experimental V2 multi-probe30 warnings use the
  same acknowledgement scope. Settings-save acknowledgement prevents a
  duplicate Start warning for that profile. Cancel does not acknowledge it.
- Reset on reconnect, an explicit Format attempt, changed reported capacity,
  or observed loss of the mounted catalog. A new profile warns separately.
  BLE status has no CID here, so this is not a persistent same-card identity
  or qualification cache.
- Actual recording/storage/transport errors remain visible. Only repeated
  recording-profile notices are suppressed.
- Reports now say "Card/profile validation not reported" rather than
  asserting "HARDWARE UNQUALIFIED" from the absent informational flag.

## Evidence

Local build: `_artifacts/sd_profile_warnings_20261022/gui96/compile-bin/WILDX_console.exe`.
Build/source hashes are in `gui96/build-receipt.json`. Four warning-related
sources replace GUI95's frozen compiler graph; the faster V1 setup and all
prior checked console changes remain included. The SDK is unchanged.

- PASS92 actual-app settings/capability/fault checks: V1 22-mode matrix,
  older V1 capability limits, V2 three-port/7.5 capability limits, warning
  acknowledgements and common recording settings.
- PASS111 actual-source recording flow checks: warning once, repeated START
  acceptance flag, changed capacity/reconnect/mount-loss scope, known-risk
  dual30 with a true qualification bit, Stop/cancellation and existing flows.

No hardware or SD contents were accessed, no firmware was changed, and no
new performance qualification is claimed. GUI96 is packaged in
`LATEST_INSTALLERS/WILDX_console-Setup-1.0.0.96.exe`; all 40 extracted payload
files match the checked release. The installer was not executed.
