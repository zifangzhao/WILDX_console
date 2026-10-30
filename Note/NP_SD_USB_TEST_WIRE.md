# Common USB SD-test codec draft

Command IDs A3 GET IN, A4 SUBMIT OUT and capability bit 0x00010000 are **proposed** until the root integrator freezes the common header. This is a USB-only control codec; it does not start a recording, access media, modify an NPSD request, or qualify data integrity. Device outcome `Candidate` requires host readback.

All integers are little-endian. CRC32 uses IEEE reflected polynomial `0xEDB88320`, initial/final XOR `0xFFFFFFFF`.

**SUBMIT** is exactly 128 bytes: u32 magic `0x3154514E` at 0, u32 ABI 1 at 4, u64 ticket at 8, UID words at 16–27, card capacity at 28, four raw SDInfo CID words at 32–47, expected frontier at 48, duration 1–120 seconds at 52, flags at 56, and CRC32 at 60 over bytes 0–59 followed by bytes 64–127. Flag bit 0 requests explicit reconciliation for a new manually initiated test; all other bits must be zero. Bytes 64–127 preserve the exact finite NPSD START request, including its own checksum. The outer codec treats these 64 bytes as opaque. Family firmware must independently validate NPSD checksum, UID, token, profile and finite geometry, and confirm the card and frontier before admission.

Manual first-use reconciliation requires current Identity, fresh Catalog, source Quiet, SD Quiet and Append Available proofs (validity mask 0x2F), catalog flags zero, and exact device, card and frontier; finite allocation and a fresh ticket must also be verified. Closed Terminal and Prior Profile Restored are not required for virgin, corrupt or interrupted retention because no old proof can be invented. A valid queued, accepted or running job must never be replaced; reconciliation is not an automatic fallback. An automatic next rate after a finished test still requires truthful prior-profile restoration.

**GET** is exactly 256 bytes, 64 u32 words; word 63 is CRC over bytes 0–251. Each field occupies its given word index:

| Words | Meaning |
|---|---|
| 0–7 | `NQR1` magic 0x3152514E, ABI 1, byte count 256, capabilities, phase, outcome class, validity flags, bridge error |
| 8–16 | ticket low/high, plan CRC, inner NPSD request ID, family, mask, Hz, seconds, target NN blocks |
| 17–29 | current UID[3], capacity, CID[4], current frontier, catalog flags, supported rates by physical mask (8 nibbles; bits 0–3 = 7.5/15/20/30 kHz), maximum seconds, expected frontier |
| 30–44 | prior runtime mask/Hz, session, reservation start/end-exclusive, payload start, durable end, stored N, admitted A, logical prefix P, high water H, APP state, writer state, writer error, command error |
| 45–53 | dropped NN, physical overflows, source gaps, missing ticks low/high, discarded raw ticks low/high, recording ms, drain ms |
| 54–62 | write count, total write microseconds low/high, maximum write microseconds, maximum service microseconds, SD Hz, CPU Hz, AUX queue drops, AUX scheduler misses |
| 63 | report CRC32 |

Phases 0–6: none, queued (not acknowledged), accepted (USB status acknowledged and expected handoff sealed), running (sealed before START), finished, rejected (known no START), interrupted/unknown. Outcomes 0–10: pending, device complete candidate, throughput loss (actual backpressure/source overflow), probe fault, unsupported, identity mismatch, catalog, storage I/O, cancelled, internal, interrupted.

Validity bits 0–15: current card identity, fresh catalog, source quiet, SD quiet, closed terminal, append available, capture counters (drop/overflow/discarded), admitted A, performance, AUX counters, prior profile restored, expected software handoff, source gap count, missing ticks, P/N/H available, rejected without media mutation. Bits 16–31 are reserved. Family and MCU UID words 17–19 always report the actual device even when a card is absent; bit 0 gates only capacity word 20 and CID words 21–24. Unknown numeric values are `UINT32_MAX`, or `UINT64_MAX` for two-word values, with the corresponding validity bit clear; in particular unavailable timings are not zero. Performance bit 8 gates words 54–60 only; recording and drain milliseconds (52–53) each independently use `UINT32_MAX` when unknown. The codec enforces explicit field groups for card identity, catalog, capture counters, admitted A, performance, AUX, gaps, missing ticks and P/N/H. Other sentinel and phase/outcome consistency obligations remain with the producer and consumer.

The report is not a request echo. `NpUsbSdTestReport` holds words 0–62; `np_usb_sd_report_init` initializes unknown values to all ones, and only the encoder writes CRC word 63. Inputs with wrong length, magic, ABI, unsupported plan flags, duration, CRC, phase, outcome or reserved validity bits are rejected before output is committed. The codec does not assign acquisition semantics to NPSD or assert a successful recording. The finished-candidate fixture values are illustrative, not hardware measurements.
