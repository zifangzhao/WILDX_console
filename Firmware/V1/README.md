# V1 USB SD200mA fix — USBB5FC4876 / APP654824DE

Use the matching fused HEX for boot+USB+SD/BLE APP; retained configuration is
excluded. V1 NP only, not V1Camera or V2. APP-only flashing does not update USB.
USB explicitly selects200mA for SDR104/50/25/12; UHS mode receipts check the
actual power selection. The U5 CMD6 reader uses two positiveHALF bursts and
DATAEND/error completion, not an invalid idleRXFIFOE requirement. Cold DEFAULT
fallback remains read/reference validated without requiring optional CMD6.

Local WILDX51 production API: reset22846ms,84MiB exact reference,
1GiB known SHA256 at14.51MB/s,zero retries/errors,
31-record checkpoint unchanged. Faster UHS not accepted on this card; this is
not a43MB/s claim. No card writes. Boot/APP unchanged; other modes inherit prior
evidence, not freshly qualified. See download-init-manifest.json for scope.
