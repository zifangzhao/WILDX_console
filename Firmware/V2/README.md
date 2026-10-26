# V2 two-channel500Hz BLE preview â€” checked release

APP C250DF48 / USB A71BDECC / recovery6408DBB8; matching console WILDX143.

Two selected P1/P2 channels at500Hz, fixed-point anti-alias filtering before
rate reduction,32packet/2.56s bounded replay. Preview band0â€“200Hz;200â€“300Hz
transition. P3 preview is not added. Raw NP recording data is unchanged.

GUI143 hardware check: two continuous subscriptions, selection change and forced
packet-drop recovery; zero CRC/sequence/source-tick gaps or preview terminalerrors.
P123/20k SD recording session407110382/28956blocks passed full export/readback:
threeDATs,LosslessTrue,zero sourcegaps/rawdiscards/overflow. Fifteen freshAG+MAG
views had no errors/skips. Catalog prior entries preserved; originalP12/20k
profile restored, finalUSBidle2. Stopped verifier/drain took48.373s; no faster
Stop claim. Native gather/filter cost9.2566% of a fully-active5.032s DWT window;
this excludes TX/CRC and is not whole-system CPU use.

Fused HEX directly merges exact components and excludes retained configuration;
USB/recovery and all nonAPP addresses stay exact. APPHEX retains its checkedCRLF
bytes; newfused usesLF. No separate whole-fused flash was performed. No all-mode,
all-card,endurance,analog-quality or arbitraryRF-outage continuity guarantee.

Source/evidence snapshots are under ReleaseSources/V2_TWO500_C250DF48_A71BDECC_20261026.
They establish scoped incremental provenance with preserved historical objects,
not a full clean-tree rebuild. No ZIP is generated. See acceptance.json,
package-receipt.json and source-manifest.json for exact scope and identities.
