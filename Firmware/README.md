# Bundled firmware

The current V1 and V2 recording images and matching catalog are included in the installer. V1 uses APP87109990/USB9D595E4D; V2 uses APP194D9B94/USB74A9B98B. Unchanged V1Camera assets are retained.

USB60-second and BLE-to-SD30-second checks at20kHz verified local RTC-to-first-NP intervals below1ms, saved IMU data and NP continuity. These are scoped tests, not all-profile/endurance qualification. See each family's acceptance.json and the release validation summary for exact limits. Experimental V2 hardware-grid firmware is excluded. Previous deliverables are recoverable under legacy.
