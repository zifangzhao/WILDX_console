# Bundled firmware

Installer 1.0.0.246 selects V1 APP4A677F83/USB11646986 and V2 APP99CC44F9/USBEDEB6C3A with the matching catalog. Bootloaders and V1Camera assets are unchanged. No separate firmware update ZIP is required.

V1 FS2 Delete Last now works over USB and BLE with fresh same-card inspection and explicit confirmation. A stopped incomplete/failed last recording can be removed when its allocation and descriptors are valid. The bundled V1 APP and USB firmware are needed for this capability; older firmware retains its advertised recording and storage fallbacks.

The final V1 development package passed normal USB/BLE deletion, a five-second VEE hold between durable delete checkpoints followed by MCU reset and automatic recovery, and post-recovery two probes at 15 kHz NP/IMU readback. All 74 protected earlier recording fingerprints matched. Final USB cold readiness was checked separately. [Exact results and limits](../Note/V1_DELETE_LAST_20261105.md).

V2 retains the checked SD Info, completed-read maintenance, USB quality mode and complete-entry USB Delete Last implementation. Its earlier three probes at 20 kHz NP/IMU checks and restoration are reused. No V2 firmware bytes changed in this work package.

Existing closed-loop profiles are retained for unchanged processing with preview off; no new coexistence mode is enabled. Startup-slot and unknown terminal/source-window limits remain explicit. Fused images compose checked components and were not newly flashed as whole images. The VEE test does not establish measured rail voltage, whole-device power removal or arbitrary SD-controller power-loss behavior.

[Release qualification](../ReleaseSources/1.0.0.246/qualification.json) and each family's acceptance.json record identities and evidence. V1 exact source provenance uses the explicit frozen base and final overlay described by its source-manifest.json.
