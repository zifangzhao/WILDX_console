# V1 BLE startup handoff fix — 2026-10-19

High-speed USB **0E542BBD** preserves the quiescent automatic SD/BLE handoff
across USB bus callbacks and reconciles the cancelled/expired handoff latch.
This supersedes USB76E544AB. The exact current fixed GUI (1.0.0.44), APP654824DE,
boot/recovery and V2/camera firmware are unchanged. No source was rebuilt by
the packager. The separate pending V2 catalog work is excluded.

The full-callback software regression passed. Local USB-only installation had
two matching full readbacks. Normal USB role3 idle, two injected no-host USB
connect suppressions entering APP, fresh BLE status and the 31-entry catalog
passed using preserved private APP60EC609E. **The release APP654824DE pair was
not newly device-tested.** This was not a physical battery-only power cycle;
remote B34D50 was unavailable. Windows discovery/endpoint lookup was intermittent:
one timeout is retained, followed by advertisement and successful reconnection.
No recording, streaming, throughput, endurance or in-animal readiness is proven.

The owner-provided fused/component HEX files are copied unchanged. Fused SHA256:
CB6F2989B94FE890A6096C8FA8B26C186C6609FE6BD9A3C45CFC4EA2604CA630.
Retained configuration is excluded. Preserve existing SD recordings and settings.
APP-only updates do not replace the USB service containing this fix.
