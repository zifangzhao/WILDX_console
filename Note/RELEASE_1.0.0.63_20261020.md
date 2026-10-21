# WILDX 1.0.0.63 — compact V2 BLE preview and camera recovery

This release pairs the exact checked GUI63 with V2 APP15FAE47B, USB E323A06A and
recovery6408DBB8, including matching fused/component/update files. V1 and camera
firmware retain their prior bytes. Installing the console does not flash a device.

BLE preview calibration permits healthy active recording while excluding pending
SD commands and control ownership. Explicit recording-pressure refusals receive
at most three fresh-request retries after 1/2/4 seconds; uncertain outcomes and
stale sessions are not retried. The actual form passed 6,003 source checks and
the V2 developer's bounded dual15k live preview/SD test, including P1 to P2/channel383.
All 10,140 produced/written blocks passed stopped readback. See
`Firmware/V2/source-validation.md` for invalid preview rows, selection downtime,
baseline provenance and the unqualified profiles/modes.

The shared camera decoder also includes the checked row-alignment recovery from
GUI61, retaining tone-v2 color/contrast. Recoverable row shifts are realigned;
missing or ambiguous pixels hold the preceding whole image and remain reported.
All 15 FrontCamera videos were regenerated separately: 494,807 frames, 11,575
realigned frames and 542 held frames, with backups and read-only native inputs.
Camera software/stored-footage evidence is reused; no new camera hardware test.

The GUI63 executable SHA256 is
`8653385637E334133CF9C4B0D218F76AF43DD006C55644CBFDB766CB5302AE2C`.
The exact 98 build/compiler inputs are preserved under ReleaseSources/1.0.0.63.
Packaging reuses this executable and existing dependencies; no product rebuild,
firmware rebuild or additional hardware action. GUI64 V1 stall recovery and the
new V2 IMU startup investigation are excluded and remain separate work.

One successful NSIS pass produced a 6,570,476-byte installer, SHA256
`2328BCF114272C6BDA2B6F797DA3A51AE8DC6E4F41F7CFC46875DD8C8E53A05C`.
The first restricted launch failed before NSIS started; the successful launch
used the same frozen inputs outside that process restriction. All 44 extracted
payload hashes match. The 29 files outside V2 and the firmware root index/README
match frozen GUI63 exactly, including V1/camera/dependencies. No installer was
executed. LATEST_INSTALLERS contains one final installer; its predecessor is
preserved in legacy.
