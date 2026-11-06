# GUI and firmware implementation

This records the app241 work package. The subsequent V1 crash-safe USB/BLE
Delete Last implementation and its acceptance supersede the V1 deletion limits
below; see [V1 Delete Last](V1_DELETE_LAST_20261105.md).

Development app1.0.0.241 follows the source review in
[GUI_FIRMWARE_SUPPORT_20261105.md](GUI_FIRMWARE_SUPPORT_20261105.md).
No installer or publication has been performed for this work package.

| Item | Implementation |
|---|---|
| SD Info | Shared advertised A5/A6 protocol, explicit idle refresh, sequence/UID checks and partial/error validity. V1 enters download mode using the existing exact-UID transition; V2 can query either idle USB service. Older firmware retains passive capacity/CID/cache support. |
| USB quality test | V2 enters idle download mode automatically instead of attempting the unsupported streaming query. V1 retains its acquisition diagnostic fallback. Storage-backed throughput remains distinct from electrical USB quality. |
| BLE preview | V1 LFP preview uses native500–625Hz ticks. Both old and new strides are accepted and the actual packet rate is displayed. Device-confirmed CL ownership guards preview on both families. Recording with CL off gets no additional Start gate. |
| SD recording-profile test | Existing A3/A4 job and checked APP pairing retained. V1's changed APP must have the matching updated USB job gate. Card identity, reservation and full readback guards remain. |
| USB Delete Last | V2 explicit fresh inspection, confirmation, matching card/catalog/last-entry snapshot and one-shot commit; catalog metadata only. The button is enabled after List confirms the advertised capability. |

V1 FS2 has no compatible deletion primitive: its monotonic count/frontier
invariants must remain intact. USB Delete Last therefore remains unavailable
there. V2 deletion is limited to a complete, metadata-valid last entry; an
incomplete allocation is not silently discarded. No operation falls back to
Format. The shared wire is documented in
[USB_MAINTENANCE_WIRE_20261105.md](USB_MAINTENANCE_WIRE_20261105.md).

The V1 preview change avoids a new resampler: selecting an integer native LFP
stride gives625Hz at7.5/15kHz AP,555.56Hz at20kHz and500Hz at30kHz. This is a
sample-rate change, not a claim that BLE transport can never lose preview
packets. NP/IMU recording remains higher priority; CL and preview coexistence
is deliberately unavailable until qualified.

## Host acceptance

Functional app compilation240;206 input identities stable. EXE SHA256:
`BD0416D30E53946B65380D8F925AE002C277D9C835D22F6A59598D1675F26ECD`.
The checked SDK is unchanged, SHA256:
`EBC98EEB84FDE42DEE06B713F1F6246B30B3527732075D9F84C6DF57BCA69EFB`.

Compilation241 corrects two SD Info report/tree strings after the V1 firmware
owner confirmed that normal download initialization can negotiate UHS. It
does not change protocol, preview, recording or SDK source. Two actual241
label assertions and the206-input source-delta comparison pass;240's relevant
functional/device evidence is reused. The final runtime is
`C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_host/enabled241-sdinfo`;
identity and delta receipt: `_artifacts/gui_support241/labels-and-delta.json`.

| Focused checks | Result |
|---|---:|
| Actual-assembly maintenance transactions, identity/nonce/CRC, busy/legacy guards, no replay; positive active-query path |220 passed on241|
| Legacy passive system info |437 passed|
| Device port labels and stable identity |26 passed|
| Preview admission/receipts, actual240 parser and121 firmware-generated packet vectors |708 passed|

## Device acceptance

Both lab devices were returned to their exact original firmware/options,
settings and idle modes; hardware leases were closed. Existing user recordings
were preserved. These are bounded development checks, not endurance proof.

| Family / checked pair | Physical results |
|---|---|
| V1 APPD05473D6 / USB71AE291B | SD Info passed cold and after completed bulk reads. Post-query30s USB at2P15k had no observed midstream NP errors; native AG199.40/MAG99.98Hz.30.27s BLE-to-SD preview delivered18,816 samples/channel with625Hz source cadence, no source gaps/invalid samples/device drops, and96/96 sampled raw ADC values matching recorded data. Full stored SD extent/NP continuity and IMU checks passed. |
| V2 APP99CC44F9 / USBEDEB6C3A | SD Info passed in both idle USB modes; subsequent USB capture retained8,088/8,088 NP blocks and12,940 scheduled IMU rows with no reported errors. Quality test completed cleanly.10s3P20k SD test fully read back365,163,520 bytes:7,816 NP blocks,12,506 IMU rows, no reported gaps/drops/overflows or IMU queue/scheduler errors. Read-only catalog inspection worked after completed downloads. Delete Last removed only that verified test session; all22 previous catalog identities were preserved. |

V1 retains its existing startup qualification, one unavailable initial IMU
grid slot and unknown SD terminal-tail completeness. V2 also retains unknown
terminal/source-window coverage where the metadata cannot prove it. Native
sensor conversion phase is separate from the1250Hz cached-copy grid. Prior
closed-loop qualification is reused for unchanged processing with preview off;
there is no new CL/preview coexistence claim. The V1 completed test session
remains on its card because FS2 cannot safely delete it. Large local generated
neural/timestamp payloads were removed after compact evidence was secured.

The V2 physical checks caught two admission defects before acceptance: an
inherited legacy runtime accessor intentionally returns null, and a completed
read cache is not an active SD transfer. The final implementation checks the
actual NP2 owners and peripherals, then reconciles only completed cached reads
in the foreground for A1/A7. It never cancels an active transfer or resets its
epoch/pipes. Native checks cover2,192 cases and671 immutable CRC snapshots;
V1 preview/SD Info checks cover12,025/8,488 cases.

Exact component, source, acceptance and restoration receipts are retained in
`ReleaseSources/1.0.0.241`. The app catalog selects the checked pairs above;
the published installer remains1.0.0.238 until explicitly packaged/published.

App240's runtime and source snapshot are under
`C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_host/enabled240-guiops`
and `source240-guiops`; root host receipt is
`_artifacts/gui_support240/host-verification.json`.
