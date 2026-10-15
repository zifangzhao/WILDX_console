# Local network time and preview windows — 1.0.0.25

The network clock displays Windows local time with an explicit UTC offset, including daylight-saving conversion. The target is ±10 ms. Both main panel and settings compare the current fresh estimate against that value. Internal UTC and existing recording clocks are unchanged.

Automatic selection compares Cloudflare, NIST and Microsoft with eight independent samples per source. Ranking uses minimum measured uncertainty plus observed offset range, with a 0.1 ms incumbent margin. Stale, failed and paused sources cannot win. Healthy endpoints are pinned; changing source/endpoint clears the frequency-fit history, preserves the QPC anchor and slews phase. Per-source 16-second polling, persistent RATE backoff, DENY/RSTR pause, generation cancellation and manual override are retained. [Behavior and primary references](../docs/network-time.md).

Preview forms were kept above the console by WinForms owner relationships; V1 media was modal. USB/BLE neural, camera and scope previews now open without owners. V1 media opens modeless while retaining its exclusive USB lease until FormClosed. Console close requests media Stop and waits for its task to drain. Closing a live neural/camera preview retains its capture resources; closing the console closes the independent previews. A prepared hidden camera can be reopened. Pending media implementation changes were excluded.

Checks passed against the final GUI: 20,083 deterministic NTP/clock/source-selection/UTC/DST/10 ms boundary assertions; 37 actual clock-UI/default/layout checks; 11 preview z-order/lifetime checks using a modeled media task; 134 USB UI stop/close/completion ownership assertions using a modeled capture worker. Preview checks cover reopening, a responsive main window, retained USB lease, deferred media drain and cleanup. Commands: `test_network_time.ps1` and `test_preview_windows.ps1` with the isolated GUI path.

A final 20-second public-NTP check received two replies from all three providers and chose Cloudflare. Its round-trip delays were 14.740 and 8.769 ms; displayed estimated errors were ±12.431 and ±15.394 ms, correctly above the 10 ms target. The last NIST/Microsoft RTTs were 60.81/53.58 ms. This validates network operation and selection on this PC; it is not an independent absolute-accuracy calibration. Earlier 40-second comparison also selected Cloudflare. The GUI renders contain synthetic sample values.

The isolated GUI starts from `2f2303ccde873d5a603e9a80e6c979a8b3ae55f1` with nine time/settings/preview/version source overlays. Build: zero warnings/errors. All twelve non-GUI runtime files remain identical to 1.0.0.24. All thirteen installer runtime files were independently extracted and hash-verified. No COM/USB/BLE recording hardware, firmware action, real recording or installer execution. Existing hardware qualification limits remain.

Private artifacts: `_artifacts/network_time_20261014`, including final-time-checks.log, live.log, preview-tests, build.log, source-inputs.json and package verification. [Manifest](../LATEST_INSTALLERS/release-manifest.json).

- GUI SHA-256: `A126E04143BD5FEE7F6FAAC31D60AAB1C733FB1640BC81ACBA37D293622D075D`
- Installer SHA-256: `C4C30788FAAC9A5358DF08E33C006C4071FE74672FB902AC67B66E95FCD9C23B` (2,336,367 bytes).
