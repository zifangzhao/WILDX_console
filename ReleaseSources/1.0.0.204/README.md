# WILD-X 1.0.0.204 source records

This update contains the checked application and V1 deterministic IMU firmware. The V2 firmware bundle is retained from1.0.0.199 while its new SD scheduler remains in development.

The host archive contains the199 exact source inputs recorded by the204 build receipt and the matching SDK qualification. The V1 archive contains the new production unit sources, resolved dependencies, compiler arguments, link inputs and their identities. Retained historical objects are identified separately; a complete clean historical firmware rebuild is not claimed.

V1 USB1E73A743 and APPFFF283D9 passed two-probe20kHz captures of60s USB and30s BLE-to-SD. The local RTC-to-first-NP interval widths were112us/111us. The enforced1250Hz cached-copy grid had a42us relative-copy bound and one initial unavailable slot in each mode. Physical sensor conversions remain asynchronous; SD full saved-NP overlap and terminal coverage remain unqualified. PC clock calibration uncertainty is separate from these local intervals.

The catalog sourceCommit identifies the commit publishing these exact incremental source archives. Owner component manifests and acceptance receipts remain byte-for-byte unchanged. See the final validation summary and firmware catalog for the installed release identities and scope.