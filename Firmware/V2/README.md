# V2 bounded SD download release - 2026-10-25

Recovery 6408DBB8 / USB A71BDECC / BLE-SD APP 8DE56637.

Actual WILD-X141 buffered export median: 43.3317 MB/s versus previous 31.4195 MB/s
(+37.91%). Six exports matched the reference DAT, descriptor and footer hashes;
all 296 catalog entries matched; firmware errors 0, CRC self-test 1. The selected
record contained no IMRO file. Legacy serial median was 31.1702 MB/s.

Short three-probe/20 kHz USB capture on the unchanged actual140 capture graph:
7280 received and verified blocks, zero raw/decoded drops, fresh AG and MAG in
all 186 retained IMU observations. This is bounded qualification, not all-mode,
all-card, endurance, analog-quality, physical-time or installed-UI qualification.

Fused HEX directly merges exact checked components and omits configuration.
APP/recovery stay byte-for-byte unchanged. New USB/fused HEX use LF; legacy
recovery HEX retains CRLF. The fused image was not separately flashed as a whole.

The source ZIP contains incremental source/object/build/evidence provenance,
not a standalone updater or full clean-tree reproduction. Historical main source
preimage is unavailable; exact retained main.o is pinned. Existing APP limits
and prior evidence remain in acceptance.json and package-receipt.json.
