# NP2 USB rate admission — WILDX 1.0.0.6, 2026-10-12

Three-probe 30 kHz split9 requires 54 MB/s before headers, exceeding the ideal
USB2 HS bulk ceiling. One shared admission check now rejects Apply, profile
configuration, START, recording preflight and mode-reset entry with an
actionable reason; no silent rate change. Old status/file parsing remains
compatible, and dual30 remains available. Prior confirmed IMRO preview and
normal BLE DFU visibility are retained.

One incremental Release GUI build; focused software regression passes 29
checks, including packed-dual decoding, old triple30 file decoding, all current
mask/rate combinations and pre-mutation guard ordering. No dependency rebuild.
All 12 prior runtime dependencies/updater files are byte-identical. One NSIS
installer generated; all 13 staged payload hashes checked. No installer
execution or independent extraction this cycle.

Exact GUI6/USB A33B9912 pair tested headlessly on MCU001A0004434250062039314E:
dual30 and triple20, each8192 blocks/10.487s, zero detected drops/errors and
every saved DAT byte matched RAW replay. The new USB also completed32768
dual30 blocks/41.944s with old GUI5; all1932733440 saved DAT bytes replay-match
GUI6. Default setup only, not custom IMRO, interactive preview, analog/LFP,
long endurance, SD/BLE APP or complete fused update qualification.

Installer:2247502B; SHA256
`4383A0457A0D91AFA88C2700B1FE5965A64ECC3F13E58D2C985A49B9D6B3611E`.
GUI SHA256:`99EBA1A8835AC8B60A564FF2D180F6527123DF05EE44BFD231804976B3E28977`.
Local delivery in canonical and V2 LATEST_INSTALLERS, previous GUI5 recoverable
under legacy/20261012_before_usb_rate_fix_1.0.0.6. No desktop install/Git push.

Full firmware/HIL evidence and next plan:
`D:/Code/XE_NP_V2/Note/Tools/V2Usb/USB_RATE_FIX_20261012.md`.
