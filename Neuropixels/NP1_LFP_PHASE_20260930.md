# NP1 LFP reset-phase correction — 2026-09-30

The V1 quad decoder previously assigned slot 0 to the first received LFP
superframe. Framing lock does not imply LFP slot 0: the real two-probe SD
recording started at slots 8 and 7. AP values were correct, but LFP channels
were assigned to the wrong multiplexing positions.

The decoder now initializes the LFP slot once from the first complete
superframe's reset-epoch counter, then advances continuously across natural
20-bit counter wrap. It emits only complete 12-slot LFP cycles; the incomplete
initial cycle is omitted and its offset is reported. AP samples are unchanged.

A V1 quad decoder requires an explicit recording-reset declaration. The SD
exporter uses V1's reset-before-START contract. Live USB declares the epoch only
after firmware acknowledges a fresh acquisition session. Legacy keep-alive
firmware without this receipt is rejected rather than silently producing
shifted LFP. Arbitrary midstream/wrapped counter epochs must not be guessed.
Legacy separately qualified GPIO/PSSI layouts are unchanged.

Validation:

- 3,296,338 checks per actual live/SD source pair: all 12 initial slots, NP1
  fixed word 0x11 and NRIC 0x22, byte fragmentation, partial startup, natural
  counter wrap, unknown/reset-inconsistent epochs and counter corruption.
- 402,295 legacy USB parser/profile checks and 1,032,004 NP2 wire/status checks.
- 470,064 NP2 SD/DAT checks through each actual Release executable, plus WILD
  control/preview tests. These are regression checks, not NP1 hardware proof.
- Actual USB 1.0.3.4 and WILD 3.4.2.172 executables exported the same existing
  five-second real two-NP1 recording, without card or firmware modification.
- Independent Table-15/16 bytewise reference comparison: 58,074,240 AP values
  and 4,839,168 LFP values, zero mismatches in either console. Both consoles'
  DAT files are byte-identical. The old slot-zero output is a failing control.
- The actual Release live decoder also matches every AP/LFP DAT byte from these
  saved real streams. This is offline decoder evidence, not a new live USB
  transport endurance qualification.

Raw framing had zero sync, heartbeat, fill, fixed-word, counter or cadence
failures. First complete superframe counters were 2600 and 2587. First complete
LFP cycle starts at AP-row offsets 4 and 5, respectively. No interpolation,
resynchronization, signed conversion or payload repair was applied. Source ADC
samples have no payload checksum; correct transport decoding is not independent
analog ground truth.

This is a targeted candidate fix, not an all-functions release qualification.
Real long DAT readback across counter wraps, live USB restart/endurance, BLE
lifecycle reliability and interrupted field-update safety remain separate gates.
The legacy NP1 SD export still writes raw diagnostic sidecars; final DAT-only
output-policy work remains open. No firmware runtime change is in this update.

Programming distinction: the NP USB personality currently advertises the
protected APP updater, not STM32 ROM DFU. The ordinary full APP+USB HEX needs
STM32CubeProgrammer after independently verified ROM-DFU entry. The SD/BLE APP
has a guarded ROM-DFU entry path; the NP USB GUI button does not yet expose it.
Do not send the unrelated USB512 DFU opcode to an NP device.
