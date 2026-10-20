# V1 USB UHS200 recovery — USBF8E721C2 / APP654824DE

Use the matching fused HEX for boot+USB+SD/BLE APP; retained configuration is
excluded. V1 NP only, not V1Camera or V2. APP-only flashing does not update USB.
USB explicitly selects200mA for SDR104/50/25/12 and verifies actual selection.
Fresh CMD6 UHS bits identify retained1.8V even when ACMD41S18A=0. Recovery checks
200mA before aligning VSEL and compares a sector before/after. No S18A or CMD11
success is fabricated. First tuning retains the validated initial session.
Native CRC/status/read checks and the bounded fallback tiers remain mandatory.

Local WILDX51 production API: reset1298ms,84MiB exact reference,
1GiB known SHA256 at42.57MB/s,zero retries/errors,
31-record checkpoint unchanged. A second reset/read also passed. Forced SDR104
rejection tested SDR50 fallback and its84MiB exact reference; normal reset then
restored fast mode. No card writes. Boot/APP unchanged; other modes inherit prior
evidence, not freshly qualified. See download-init-manifest.json for scope.
