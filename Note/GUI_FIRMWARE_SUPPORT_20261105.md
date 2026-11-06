# GUI / firmware support review

This is the pre-implementation review of app239. Later implementation and
acceptance are recorded in [GUI support implementation](GUI_SUPPORT_IMPLEMENTATION_20261105.md)
and [V1 crash-safe Delete Last](V1_DELETE_LAST_20261105.md).

Reviewed current GUI239 command paths against the published238 firmware:
V1 APP5F90FD3B / USBC91871BD and V2 APP99CC44F9 / USBD43FE162.
GUI239's port-label change does not change protocol routing. Older installed
firmware can have fewer capabilities than these bundles.

This is a source and existing-evidence review. No device commands, recordings,
card operations, firmware changes, builds or publication were performed.
Implemented protocol support and hardware qualification are separate results.

## Findings to address

1. **Detailed SD Info is not functional across both families.**
   - V1 has no SDIF384/IN0x4B implementation. Its streaming OUT0x4C is camera
     exposure, so a shared refresh command must not be sent blindly.
   - V2 links a real SD reader and IN0x4B/384 cache, but its OUT0x4C refresh is
     rejected by the outer USB dispatcher before the refresh branch. There is
     no cold cache fill. Its valid EMPTY cache cannot provide real card details.
   - The active GUI only calls the passive `NpSystemInfo.Read` path. It never
     calls the older `ReadSdInfo` refresh helper, so fixing dispatch alone would
     not make the GUI refresh the cache.
   - Download-mode capacity is a separate working NPD1/IN0x60 path. V1 also
     queries optional IN0x79 CID information. The V2 basic-info GUI does not use
     the available format-info/CID primitive. Missing detailed information is
     not evidence of a missing or unhealthy card.

2. **USB quality-test availability is broader than its working modes.**
   The button uses generic NP availability. V2 testing requires an idle download
   service and readable card; streaming mode rejects its IN0x60 query. V1 can
   additionally fall back to its streaming synthetic diagnostic. The backend
   fails safely, but the GUI can offer an operation that cannot run in that mode.

3. **V1 BLE preview does not meet the requested 500 Hz minimum at all rates.**
   The compiled LFP preview produces approximately312.5Hz at7.5kHz AP,
   416.7Hz at15/20kHz and500Hz at30kHz. Preview and closed-loop intentionally
   share a4680-byte union; firmware rejects their simultaneous use with an
   explicit unavailable reply. The GUI's preview gate does not check CL
   ownership, so it can offer preview until the backend refuses it.

4. **V2 BLE preview plus closed-loop is reachable but unqualified.**
   The compiled preview supports1–2 channels at500Hz at7.5/15/20/30kHz.
   Separate storage allows coexistence by code, and neither admission path
   excludes it. Current APP99 closed-loop qualification ran with preview off.
   This combination needs a recording-priority guard or affected-mode tests.

5. **USB Delete Last is not implemented in the current GUI.**
   It is correctly disabled for USB sources in both families. BLE deletion and
   removable-reader deletion are separate supported workflows. USB Format is
   implemented and must not be confused with Delete Last or catalog repair.

## Current function matrix

"Supported" below means an implemented matching protocol. It is not a claim
that every rate, probe, card or simultaneous-feature combination is qualified.

| GUI function | V1 | V2 | Conditions / evidence |
|---|---|---|---|
| USB NP recording, Stop, restart | Supported | Supported |238 bounded NP/IMU checks; qualified CL profiles are2P15k and3P20k respectively. V2 USB restart remains reset-separated. |
| BLE-controlled SD recording/settings/Stop | Supported | Supported |APPs unchanged; earlier recording/full-readback evidence reused. Confirm device state rather than treating ACK as recording/Stop completion. |
| Probe configuration / IMRO / probe identity | Conditional | Conditional |Requires the corresponding configuration/receipt capability and actual application confirmation. Legacy unknown SN/receipt must remain unknown. USB and APP legal profiles can differ. |
| USB neural waveform preview | Supported from received NP data | Supported from received NP data |Host rendering/decoding; new firmware238 qualification had preview off, so simultaneous-load performance is not newly established. |
| BLE waveform preview | Conditional; rate/CL limitations above |1–2 channels500Hz; CL coexistence unqualified |Recording state, advertised capability and renewable lease required. Preview remains lower priority than recording; gaps are not silently a pass. |
| USB camera/audio preview |Camera-only path; no live USB audio |Not exposed by current host |V1 needs compatible camera hardware/firmware. V2 firmware has AUX camera/200kHz electrical ADC producers, but host media sinks are not wired. V2 camera conflicts with SPI-selected CL. |
| Detailed SD Info / card registers / bus/power details |Unsupported SDIF |Refresh unreachable / empty cache |See finding1. SDIF support is distinct from capacity/CID and does not imply NAND lifetime/SMART health. |
| Basic SD capacity / card identity |Download capacity; optional CID |Download capacity; incomplete GUI CID use |Idle mode3. A busy device or unavailable field must not be displayed as zero or a healthy-card verdict. |
| USB quality test, bounded/continuous |Download SD-backed; streaming synthetic fallback |Download SD-backed only |Checks payload/sequence/CRC and host-observed throughput. Requires correct mode/ownership; not an electrical cable bit-error-rate test. |
| SD recording-speed/profile test through logger |Implemented |Implemented |A3 IN256 / A4 OUT128, cap0x10000, exact installed APP/USB pairing, high-speed USB, idle state and fresh card/catalog/ticket. Creates a finite test recording and verifies saved data. |
| SD test max gaps / statistics |Partial device telemetry; readback observations |Richer gap/performance telemetry |Respect validity flags. V1 does not supply all exact duration/performance fields; unknown is not zero. Earlier221 sweeps tested10s30k→20k, not a fresh238 sweep. |
| PC-reader SD test |PC-only |PC-only |Temporary filesystem benchmark; does not establish the logger's supported recording profile. |
| List/download incomplete recordings, merged DAT, IMRO export |Supported |Supported |Matching decoder/record format and committed extent required; retain incomplete/gap status. Earlier export evidence reused; not rerun here. |
| USB Format |Conditional, implemented |Conditional, implemented |Idle download, actual format capability, fresh card/identity snapshot and explicit confirmation. Distinct from read-only download and not secure erase. |
| USB catalog inspect/repair |Conditional, implemented |Conditional, implemented |A0 report / A1 inspect / A2 repair, family-specific capability and same-card nonce. Repair is an explicit metadata mutation, not full payload validation. |
| Delete Last |BLE / removable reader; USB disabled |BLE / removable reader; USB disabled |Fresh stopped writable catalog and last-entry confirmation. |
| BLE mount/list/recovery/format |Conditional, implemented |Conditional, implemented |Firmware capabilities, stopped/quiet state, explicit keys and confirmation. This audit does not repeat power-cut/format/delete tests. |
| Diagnostics / self-test |Passive info; powered self-test disabled |Powered self-test implemented |V2 needs idle streaming ownership. IN0x79/256 self-test differs from IN0x79/64 format info. No new powered self-test was run. |
| Closed-loop control / events |Qualified1–2 at2P15k |Qualified1–2 at3P20k/P2ADC0/1 |Ripple6, source/session binding and camera/resource guards. Other supported firmware masks are not automatically qualified. Trigger output remains deferred. |
| USB modes, bundled/manual firmware updates |Conditional, implemented |Conditional, implemented |Exact family/UID, capability/idle state and supported recovery/DFU/programmer route. Unknown old firmware identity is not an affirmative match. |
| RTC/time synchronization / first-source receipts |Conditional, implemented |Conditional, implemented |Compatible wire version; device calibration only while idle. Local RTC-to-NP evidence is separate from PC/UTC accuracy. |
| Calibration CSV search/batch/backup, media export, network time |PC-side |PC-side |No new NP firmware capability for offline processing; source metadata and separate clock/network limits still apply. |
| GPIO/RTC logging and panel |Separate logger + host |Separate logger + host |Depends on the GPIO logger/connection, not an NP USB command. NP recording continues with warning if the configured GPIO logger cannot start. |
| USB exploratory scanning |Configuration protocol supported; full sweep not qualified |Configuration protocol supported; full sweep not qualified |USB-only sequencing, confirmed routing and fresh per-bank files; software fixtures do not prove physical bank coverage. |
| Pre-arm |Unsupported stub / host feature disabled |Not qualified/enabled in current release |Do not infer support from a status handler that returns an unsupported frame. Normal configuration staging and Start are separate. |

## Recommended order

1. Implement an advertised, family-safe SD Info protocol and an explicit idle
   refresh flow; use existing passive capacity/CID when rich details are absent.
2. Make USB quality and preview controls reflect actual mode/capability/resource
   restrictions, with clear reasons for unavailable operations.
3. Resolve V1 preview's500Hz target and qualify or disable V2 preview+CL
   coexistence while preserving NP/IMU priority.
4. Rerun only the affected SD Info/test/preview paths on the resulting exact
   firmware pairs. Do not repeat recording, formatting or whole-card tests just
   to review labels or package unchanged bytes.

## Evidence and source references

Host paths: `mainForm.cs:324`, `mainForm.UsbQuality.cs:14`,
`UsbQualityTest.cs:21`, `NpSystemInfo.cs:93`, `XeNpSdInfo.cs:419`,
`XeNpWinUsb.cs:594`, `NpLogger/NpDownloadForm.cs:109`,
`NpSdUsbTestController.cs:137`, `Ble/NpV1BlePreviewForm.cs:51`,
`Ble/NpBleForm.cs:302`, `NpDiagnosticsForm.cs:48`.

Exact D43 stream source SHA256
`D15D640AAA7AB41E32C64019036FDE72032305B8F1F3D5B37CCA2D68CEBB5258`:
outer OUT guard3044, SD cache GET3050, unreachable refresh3062, foreground fill3446.
The source is pinned by R3/R4 receipts, rather than inferred from today's generic
firmware tree. Frozen APP dispatchers and retained object identities were also
checked by the family owners.

Detailed source/command/evidence matrices:

- `C:/Frank/Code/XE_NP/_artifacts/gui_fw_support_20261105/v1/v1-support.json`
  SHA256 `07A882FC9C28636C86C1FFB1E514CEDDE79578810AC36010BD386A962B67166E`.
- `C:/Frank/Code/XE_NP/_artifacts/gui_fw_support_20261105/v2/evidence.json`
  SHA256 `DB39A6EF7EFC9622783D02E9D24EBAC97846AF007C89697E5D162BBE1248D854`.
- Existing qualification: [238 USB](../ReleaseSources/1.0.0.238/qualification.json),
  [SD test](SD_CARD_TEST_VALIDATION_20261030.md),
  [closed-loop](CLOSED_LOOP_QUALIFICATION_20261104.md),
  [exploratory scan](USB_EXPLORATORY_SCAN_20261026.md).
