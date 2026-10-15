# Network time

The USB workspace and Time settings display local time using the Windows time zone, including the UTC offset so repeated daylight-saving hours remain distinguishable. NTP calculations and internal timestamps remain UTC. This display does not alter Windows time or the existing recorder clock.

**Select best server automatically** is enabled by default. The app compares `time.cloudflare.com`, `time.nist.gov` and `time.windows.com`. It keeps eight samples per server, ranks measured uncertainty plus observed offset variation, ignores failed/paused/stale candidates, and avoids switching for differences below 0.1 ms. Healthy server endpoints stay pinned. Switching sources preserves the running QPC clock and slews phase; frequency estimation never mixes different peers.

Each server is polled no faster than every 16 seconds. RATE replies increase its retained minimum interval; DENY/RSTR pause that server, allowing other candidates to continue. Reapply settings to retry paused sources. To use a local laboratory time server, turn automatic selection off and enter its hostname or IP address.

The target is **±10 ms**. The GUI compares the fresh estimate against that threshold. Estimated error includes path delay, server root delay/dispersion, jitter, pending correction and holdover drift; server selection does not guarantee target accuracy. The final 20-second check on the development PC selected Cloudflare with approximately 9–15 ms round-trip delay and ±12.431–15.394 ms estimated error, so it reported above target. These are measurements of this network path, not calibration against an independent time reference.

[Microsoft's accuracy guidance](https://learn.microsoft.com/en-us/windows-server/networking/windows-time-service/accurate-time) explains the effect of remote paths and asymmetry. [Cloudflare's NTP documentation](https://developers.cloudflare.com/time-services/ntp/) describes its public service and non-smearing behavior. The polling interval respects [NIST's usage limit](https://tf.nist.gov/tf-cgi/servers.cgi).

Run `test_network_time.ps1` with the built GUI path for deterministic protocol/selection/DST checks and the actual GUI checks. `-LiveSeconds 20` additionally contacts the public NTP services. [Validation](../Note/NETWORK_TIME_LOCAL_PREVIEW_20261014.md).
