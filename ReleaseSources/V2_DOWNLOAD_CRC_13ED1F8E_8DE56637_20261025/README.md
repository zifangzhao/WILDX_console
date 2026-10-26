# V2 CRC-read improvement - 2026-10-25

Recovery6408DBB8 / USB13ED1F8E / BLE-SD APP8DE56637.

Fused HEX merges the exact checked USB with unchanged APP/recovery and omits
retained configuration. Components retain their existing bytes/seals. New USB/fused HEX use LF; the
unchanged legacy recovery HEX retains its existing CRLF formatting.
No separate whole-fused flash test was run.

WILD-X139 download comparison: serial export median30.41 to31.82MB/s; all six
exports matched baseline hashes, firmware errors0 and CRC self-test1. SD time
also improved, so the entire gain cannot be attributed solely to the CRC loop.
No new neural capture/endurance test was added. Prior APP evidence is reused.

These exact assets are intended for the single WILD-X140 installer. Its owner
records final installer identity. No standalone update ZIP is required. The
compiled-sources ZIP is source/build/evidence only, not an updater.

See package-receipt.json, acceptance.json, source-manifest.json and
../../Note/V2_USB_TIMING_SIDEBAND_20261024.md for scope and provenance.
