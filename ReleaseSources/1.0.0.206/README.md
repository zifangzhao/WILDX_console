# WILD-X 1.0.0.206

Fixes the V2 Full bundle parser failure before programming at `0x080074C0`. The parser validates the recovery image's actual dense prefix and retains every omitted flash address from the exact backup. V1 recovery admission and the existing flash-page preservation policy are unchanged.

The executable is `084A328A72180F8297810EF85E3D8F8078D4C8DF5E93261D74F4B0789CF292CA`. The SDK remains `B01C4B3FF45F9875ED8542F1DCF2EF0F3BD7ECB12A6FAD3CDA8FC9690C01D46A`. All 56 other payload files, including firmware and catalog, retain exact published204 bytes. New V2 deterministic-SD experiments are excluded.

Actual206 checks passed: 592 firmware bundle/identity checks, 24 shared manager checks, 37,456 V2 parser/guarded-programmer-model checks, and independent backup/page byte comparisons. Frozen source tests reject missing recovery/vector/reset bytes, malformed APP/USB manifests, incorrect CRC/SHA and configuration writes. Byte assertion totals describe bulk byte comparisons, not distinct scenarios. Legacy notice fixture failures are retained as non-gating evidence because their stale expectations differ from unchanged current behavior; no notice fix was applied.

The installer is 7,780,450 bytes, SHA-256 `8891B8EC45FE8BE5AD2594F8D717BB96AB6F345867ABF4DBF16108C477ED9966`. One native NSIS invocation and one fresh extraction verified all 57 payload files. No installer execution, physical firmware update or new recording was performed for this host-only fix.

`compiled-sources.zip` contains 199 exact compiled inputs. Source provenance is actual Git baseline `16262132449d10ec45075105de66c4d4c2254d15` plus the archived compiled-source overlay; it is not a clean-HEAD or version-only IL claim. Firmware catalog sourceCommit values remain unchanged. The original build receipt is preserved, with its stale published199 recipe label corrected in the provenance supplement to the actual published204 baseline.

Use the shared Advanced firmware update window, select the exact device and bundled family, then choose the full APP + USB + boot bundle. Existing recording qualification and its limits remain those of release204 and its retained V2 components.
