# V1 SD download update — USB22D0792B / APP654824DE

Use the fused HEX for the complete boot + USB + SD/BLE APP stack. Retained
configuration is excluded. Pure APP and USB/DFU components are also included.
V1 NP target only: not V1Camera or V2.

USB reuses the APP SD power/120 MHz kernel/tuning sequence, guards virgin-handle
reset and retires reads with bounded CMD12/CMD13 + DAT0 checks. On the connected
V1, WILDX48 passed 84 MiB exact comparison at36.44 MB/s, two matching1 GiB reads
at36.78/36.77 MB/s, and full export at32.78 MB/s with21 identical data files.
No errors/retries;31 recordings/frontier unchanged; SD/BLE restored idle.

Local HIL preserved private APP60EC609E. Bundled APP654824DE and boot are
unchanged; this exact release pair was not newly device-tested. Remote11.2 MB/s
and0xFFFFFFFE failures are not proven resolved. Prior local USB achieved44 MB/s,
so this is not a demonstrated same-device speed improvement. No new capture,
streaming, camera, endurance or physical interruption qualification.
