# WILDX 1.0.0.44 — V1 SD/BLE storage release

Packages the exact checked WILDX44 EXE and old-V1-NP firmware APP **FA2016E7**,
USB **02FF93A9**, and matching complete fused recovery/USB/APP image.
V1Camera and V2 firmware are unchanged. Previous V1 files and installer are archived.

**Large USB downloads remain unresolved.** On the current hubbed bench, a
522,272-byte reply still fails Windows 31 after two bounded retries. Small
CRC-bound reads, checked cancellation/idle and APP return pass. Failed/partial
bytes are rejected. Do not claim complete USB, all-profile, camera, custom-map,
physical-power-cut or implant qualification.

Owner evidence covers P1+P2/15 kHz for 810.105 seconds/18.84 GiB with BLE
status/preview/reconnect, clean Stop/catalog and earlier recordings preserved;
short P1+P2/20 kHz also passed. Two independent saved-data windows of 128 chunks
each passed 65,535 frames/probe/window without known protocol-field/counter/cadence
errors. Full-extent readback was not performed. Installed APP/USB were independently
verified, boot/configuration unchanged. Fused loader acceptance passed offline;
fused programming and physical Format were not hardware-qualified.

The owner reports 495 download-handler/assembly checks. Bounded read recovery
requires the new explicitly negotiated checked-retirement barrier; old firmware,
live streams and quality-test streams do not gain that authority. Read-only
recording recovery and uncertain-extent guards remain.

No application Compile, firmware rebuild/reseal or hardware action was performed
for packaging. Exact EXE SHA-256:
`1F987A79F441D95E99DEB4E815F0C8768A2D4AE67FC342D2E90A7F83892E0F17`.
Runtime dependencies are byte-identical to 1.0.0.43. Main GUI construction passed;
the owner accepted optional embedded-resource/custom-icon loss. No startup failure
was observed. Original compilation used a dirty tree and two recorded frozen
overrides; no complete precompile source snapshot exists. This artifact release
does not claim maintained-source parity/reproducibility. Private V2 overwrite work
is excluded. See [firmware details](../Firmware/V1/README.md).
