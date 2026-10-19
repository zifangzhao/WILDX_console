# V1 USB policy — updated 2026-10-19

Use high-speed USB; do not restore forced full-speed FACEA0D2. The human has
requested publication of the latest **unverified** bundle: frozen r7 GUI,
APP654824DE, HSUSB02FF93A9, ABI258 recovery and existing matching fused98B08774….
This supersedes the interim legacy fallback as the current bundle. V1Camera
and V2 remain separate. APP60EC609E is not a replacement release component.

The authorized corrected direct-path HS test passed84MiB at41.17MiB/s with no
errors/retries, using the same HS service/r7 but preserving the private power
trial APP60EC609E. Meter-only causation is not established. Frozen APP654824DE
already has short matched HS-service SD/BLE acceptance; current exact-tuple
USB streaming and HS-download acceptance still have gaps. The <=30s tail-loss
tolerance is a target, not a verified physical power-removal guarantee.

See [current bundle and limits](RELEASE_V1_HS_FS2_R7_20261019.md).
No new hardware/build/qualification cycle is part of this publication.
