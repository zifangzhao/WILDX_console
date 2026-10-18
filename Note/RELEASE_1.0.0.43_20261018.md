# WILDX 1.0.0.43

Automatic firmware update prompts now require a verified installed build that
matches a known predecessor of the bundled firmware. Previously, the shared
USB/BLE notice also prompted for missing or unrecognized firmware identities,
even though a newer firmware version could not be confirmed. This caused
unnecessary prompts on devices already updated to current or private builds.

Current, missing, partially reported, unknown and private builds no longer
trigger automatic notices. Recognized older APP or USB builds still prompt
once per device/bundle in the running app, with No selected by default.
Manual firmware management and version displays remain available.

Validation: 70 checks compile the complete production notice/catalog source
with simulated dialog and hardware boundaries. They reproduce the previous
false prompt and cover both families, current/missing/partial/private builds,
verified predecessors, unverified CRC-only matches, board mismatches and
session deduplication. Another 590 actual-app firmware bundle, identity and
offline UI checks passed. All 40 independently extracted installer payload
files matched the checked build. No hardware or firmware writes occurred.

Only the GUI payload changed from 1.0.0.42. Runtime helpers, exporter and all
bundled firmware are byte-identical. Compilation uses frozen 1.0.0.42 inputs
except the firmware notice and assembly version; pending catalog/recording
recovery and USB transport experiments remain excluded.
