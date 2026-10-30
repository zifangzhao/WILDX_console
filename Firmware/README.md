# Bundled firmware

Release1.0.0.204 updates V1 to APPFFF283D9/USB1E73A743. V2 retains the checked APP194D9B94/USB74A9B98B/recovery6408DBB8 from1.0.0.199. V1Camera assets are unchanged. The unqualified V2 scheduler candidates are excluded.

V1 two-probe20kHz USB60 and BLE-to-SD30 checks verified NP continuity and an enforced1250Hz cached IMU grid. Local RTC-to-first-NP interval widths were112us/111us; PC clock uncertainty is separate. One initial missing IMU slot remains explicit. Physical sensor conversion phase and SD terminal coverage are not qualified. See each family's acceptance.json for exact scope. Previous deliverables remain under legacy.