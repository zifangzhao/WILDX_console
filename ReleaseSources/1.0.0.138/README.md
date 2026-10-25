# WILD-X 1.0.0.138

Adds up to four selected V1 BLE preview channels using compact raw ADC samples
within the existing packet size. AP/LFP selection, display gain and window
controls apply to four stacked traces. Channel timestamps retain their actual
ADC offsets on a shared time axis. Missing samples remain visible gaps.

Four-channel preview requires the bundled V1 APP 82E51DEA. Its capability flag
selects the new format; older firmware and clients retain single-channel
preview. USB 1.0.108 / 4F7B521A, SD recording formats, buffered USB downloads
and V2 firmware are unchanged. UART remains at 115,200 baud. The radio did not
answer the documented configuration queries, so no higher baud was written
or qualified.

One connected V1 ran a bounded two-probe 20 kHz SD recording with four channels
on probe 1: 0, 24, 25 and 383. Preview was off, then AP, then LFP, then off.
The full 1,107,296,256-byte recording was read back and checked:

- 27,163 valid preview samples matched the SD source bits exactly across all
  eight AP/LFP channel comparisons; 4,581 missing preview samples stayed explicit.
- All 507 framed compact packets passed payload CRC; no sample or phase mismatches.
- Both probes had zero reported frame, counter, relock or LFP-phase errors,
  with exact DAT lengths. All 16,768 saved IMU snapshots were valid.
- Status replies took at most 233 ms during the measured phases; normal Stop
  drained in 1,122 ms without a recording fault. All 35 prior catalog entries
  were preserved and the new recording was added.

The first observer attempt failed before Start because it did not wait for
profile readiness. Corrected observer r3 completed capture, preview, Stop and
catalog checks but exited with a timeout while confirming restoration across
the profile's clock-reset handoff. A separate fresh production BLE connection
confirmed the exact device UID, restored P12/15 kHz profile and idle state,
then returned it to USB download mode. Both failures and the separate success
are retained; this is not presented as an uninterrupted observer pass.

Software checks covered 13,336 isolated production-source assertions and the
actual 138 assembly: 139 compact fixture/CRC/lease assertions, 4,251 legacy
wire/phase/queue assertions and 18 V1 gain/layout assertions. Firmware-generated
AP/LFP packets were decoded independently by the host. One GUI compilation
changed only the four preview sources and assembly version from 137. The SDK
was reused unchanged. Exact inputs and firmware provenance are included here.

This is bounded acceptance on one configuration, not all-port/rate endurance
qualification, full-rate spike monitoring or physical ADC/GPIO synchronization.
Preview remains lower priority than recording and controls. USB streaming and
download-speed evidence is inherited from unchanged 137 components.

The installer retains its Run WILD-X finish option. Its extracted payload is
hash-verified; see `package-verification.json` and `firmware/acceptance.json`.
