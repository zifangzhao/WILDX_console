# V1 paired raw USB parser candidate — wire 0x77

The 48-byte mode descriptor accepts WireId `0x77` only for physical probe mask 3
(P1+P2) and native AP rate 20,000 Hz. Existing `0x76` and legacy `0x06` modes stay
supported. BatchPackets retains its existing 4..256, multiple-of-four validation;
each paired reconstruction group is always four packets. No control opcode or
driver/recovery code changes.

Each NP packet is exactly 512 bytes:

| Bytes | Value |
| --- | --- |
| 0..1 | `12 a5` |
| 2 | `77` |
| 3 | Time lane 0, 1, 2, 3 in exact order |
| 4..5 | Global NP packet sequence, uint16 little endian, beginning at zero |
| 6 | Physical probe mask `03` |
| 7 | Format version `01` |
| 8..511 | 252 GPIOE halfwords, little endian: low byte P1, high byte P2 |

For group position `i=0..251` and lane `l=0..3`, the chronological byte index for
each probe is `4*i+l`. A complete group yields 1,008 unchanged bytes per probe.
Those bytes feed the existing strict NP1 decoder in GPIO V1 quad layout. No
sample correction, ADC repair or resynchronization is added.

Every `0x77` NP packet advances the NP sequence, wrapping at 65,536. Existing
misc and camera packets retain their lengths, flags, payloads and independent
sequence semantics; they are accepted only between complete four-lane groups
for this paired transport. Legacy `0x76` behavior is unchanged. Wrong headers,
lane order, mask, version, sequence gaps, mixed NP wire IDs and incomplete packets
or groups stop parsing explicitly.

Startup admission can change an empty planned dual-20 `0x77` session to an
accepted single probe at 20 kHz using `0x76`, under the existing subset/rate/batch
checks. Other startup wire changes are rejected. The established per-probe and
counter-aligned merged DAT writers, previews, metadata, XML and post-drain LFP
`time.dat` are reused. Counter/framing corruption still fails even with zero USB
sequence gaps. Dual-20 remains experimental and hardware qualification is not
established by these software checks.
