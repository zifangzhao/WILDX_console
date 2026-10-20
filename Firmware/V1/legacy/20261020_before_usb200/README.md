# V1 SD read tiers candidate — USBD75CA6F9 / APP654824DE

User-approved early publication. SOFTWARE CHECKED; DEVICE VALIDATION PENDING.
Use the matching fused HEX for boot + USB + SD/BLE APP. Retained configuration
is excluded. Separate USB, boot+USB and unchanged APP components are included.
V1 NP target only, not V1Camera or V2. APP-only flashing does not update USB.

USB tries120/60/30/20MHz clock tiers with SDR104/SDR50/SDR25 alternatives,
fresh per-candidate reset, strict selected-mode query and repeated reference
reads. Fixes post-validation VSEL mutation. DDR remains disabled. Internal
USB DMA, guarded read retirement, boot and APP are unchanged.

196 actual-policy assertions and mode-check fault injection passed; one main
compile passed. No live throughput, streaming, recording or installed-pair test.
The9.5MB/s report is not proven resolved; signaling clocks are not MB/s claims.
See download-init-manifest.json for component hashes and validation limits.
