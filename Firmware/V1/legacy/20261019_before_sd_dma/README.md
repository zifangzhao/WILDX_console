# V1 fast SD download — USB037CF67C / APP654824DE

Use the fused HEX for the complete boot + USB + SD/BLE APP stack. Retained
configuration is excluded. Pure APP and USB/DFU components are also included.
V1 NP target only: not V1Camera or V2.

USB restores the exact previously checked fast USB initializer and keeps the
new CRC, exact-length/DATAEND, bounded CMD12/CMD13, DAT0 retirement, sticky-error
and no-replay checks. See download-init-manifest.json for measured raw/export
and two independent1GiB read results. All21 data files matched the reference;
no errors/retries;31 recordings/frontier unchanged; SD/BLE restored idle.

Local HIL preserved private APP60EC609E. Bundled APP654824DE and boot are
unchanged; this exact release pair was not newly device-tested. Remote11.2 MB/s
and0xFFFFFFFE failures are not proven resolved. The earlier44MB/s was a short
raw read, not decoded export throughput. This restores near that fast rate;
it does not establish a speed above44MB/s. Exact checked initializer objects
were reused: the mutable archived source rebuild was slower, so clean-source
equivalence is not claimed. No new capture, streaming, camera, endurance or
physical interruption qualification. V2/camera/host runtime unchanged.
