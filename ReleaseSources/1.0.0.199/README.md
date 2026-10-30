# WILD-X 1.0.0.199

Development acceptance passed; the installer was packaged once and its fresh extraction matched all56intended payload files.

| Device | USB recording | BLE-to-SD recording |
|---|---:|---:|
| V1, two probes at20kHz | 111 microseconds | 111 microseconds |
| V2, three probes at20kHz | 120 microseconds | 121 microseconds |

These are the full device-local RTC-to-first-valid-NP-STARTBYTE intervals. USB captures lasted60seconds; SD captures lasted30seconds with full bounded readback. NP continuity checks passed and IMU data was saved. PC synchronization uncertainty is recorded separately and is not a sub-millisecond absolute UTC claim.

Use recording_start_utc.json as the recording origin. Old firmware/file readers remain supported; missing fine timing stays unknown without adding a recording Start gate. See validation-summary.json and the pinned individual reviews for exact data, source binding, restoration and limits.

The experimental V2 hardware IMU grid is disabled. V1 has one initial unavailable grid slot, with no later observed slot gaps in these tests. Full IMU phase, final-tail and continuous-clock qualification remain open. The V2 catalog-inspection control failed; normal listing/download/export passed.

compiled-sources.zip contains197exact checked app/SDK inputs. The original laboratory build receipt is preserved unchanged as build provenance; release qualification is recorded separately. companion-sources.zip and its freeze contain15checked helper inputs. Exact189 companion runtimes are reused because code/resources/ABI match; the later build differs only in metadata. The firmware-source directory preserves the tested V2 header before a separate11-slot experiment.

