# Camera40 packaged candidate — 2026-10-14 EDT

Recorded package: V1_APP121_USB83_WILD204_WILDX27_CAMERA40_20261014.

APP121/USB83, WILD3.4.2.204 and WILDX1.0.0.27 default to40fps.
Explicit and confirmed device rates remain preserved; select/apply40 if a logger
still reports50. WILD offers16/25/40/50; WILDX keeps its explicit rate control.

On tested APP120/USB83, the60-second SD camera40+ADC200k+IMU session had
2208/2208 valid images after the first5s, with25ms scheduled slots, continuous
ADC and no new IMU errors/timeouts/missed estimates. All142 invalid slots were
in the startup0.100..3.625s burst accepted by the user. Physical camera completion
times have millisecond jitter; slots do not establish exact exposure timing.
IMU uses latest-value snapshots rather than a lossless native FIFO.
40fps is the highest tested steady rate here;41..49 were not measured.

APP121's268560-byte payload differs from tested APP120 only at three compiled
default bytes (50->40), offsets109734/109812/109884. Native fallback/retention
regression passed. APP121 CRC99BA4F27. USB83 CRC82C6E21A is unchanged; the fused
factory package uses the existing recovery-default role flag1. APP-only leaves
the USB service and retained configuration untouched.

WILD passed183 protocol/runtime gates and2823 actual-form/wire checks.
WILDX passed8 focused actual-form rate/default/no-auto-start checks.
Independent installer extraction matched22 WILD runtime files and13 WILDX
runtime files. Existing dependencies were reused. A retained NPConfigurator PDB
is unchanged from the prior WILD installer.

The existing60s USB40+IMU app evidence and SD measurements are recorded in
LATEST_FIRMWARE/manifest.json. The new APP121/UI pair was not hardware retested;
this is a component-checked candidate, with no new endurance or whole-recording
lossless qualification. Installers were not executed. No hardware access, build,
test or repackaging was added for publication.

Publication commits contain the recorded packages, identities and delivery notes.
Pending source edits stay in their working checkouts; this push-only publication
does not integrate those edits or touch V2.
