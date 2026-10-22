# WILD-X 1.0.0.78 — probe metadata and layout

USB form fields now share column widths and vertical centering. Action rows
measure wrapped content, status text has its own measured height, and device
health buttons can wrap without clipping. GPIO indicators are drawn circles,
not oversized font glyphs. The actual form was rendered at three widths and
with enlarged fonts/controls; this is software layout verification, not a
physical monitor/DPI or installed-application qualification.

Every neural recording/export writes probe_info.json alongside
recording_settings.json and probe_N.json. Per-probe data subfolders receive
their own physical-port identity entry. Decimal serial, 64-bit hex serial,
original little-endian EEPROM serial bytes, model/part and evidence are saved
when available. Missing identities remain explicit nulls with a reason.
Requested selection/configuration digests are never labeled as physical SNs.

V1 live USB uses its existing NPM1 receipt after confirmed STOP. Each port is
independent; a failed/unsupported read cannot suppress another port or change
the recording/recovery outcome. An EEPROM-absent default or explicit model
is not promoted to a measured serial. No I2C is issued by the host in capture.

V2 live USB uses the first fully verified native header. NP2USB03 slots remain
at their physical indices (serial96+8*p, model120+8*p); synthetic/non-live,
zero or erased slots remain unknown. Legacy NP2USB01/02 use their single P2
identity at80/88. V2 SD export retains a512-byte copy of the already-verified
historical native header, available via the SDK result. It does not reread the
card or query the currently attached probes. DAT bytes, decoding, checksums,
source-gap handling and timestamps remain unchanged.

BLE START retains host recording settings and requested probe/IMRO snapshots
under %LOCALAPPDATA%/WILDX/RecordingMetadata/BLE-<address>/<unique-start>/.
Its path appears in the BLE report. Device-confirmed active/terminal states
update the receipt; disconnect/shutdown preserves an unconfirmed completion
without stopping the SD recording. Metadata errors are non-blocking. Existing
V2 CAPS supplies the MCU UID; it is kept as logger identity, never probe SN.
No additional BLE exchanges, speculative opcodes or new START gates were added.

Firmware limitation confirmed with both developers: bundled BLE firmware
exposes no per-port EEPROM reader, and historical V1 SD formats contain no
probe serial/model. These modes still save all available probe/request data,
but missing SNs cannot be recovered by this software update. BLE host journals
remain explicitly unbound; they are not silently attached to an old SD session
using only a reused LBA,32-bit session, transport address or card capacity.
Existing firmware remains usable and all bundled firmware bytes are unchanged.

Verification:139 actual-app identity/legacy/BLE lifecycle/layout/native SD
export checks,15,060 recursive calibration/serial/order/overwrite checks,
and22 metadata persistence/error checks passed (15,221 total). Native healthy,
source-gap and overflow SD fixture DATs are byte-identical to checked outputs.
Offscreen renders were inspected. No hardware, BLE radio, network service,
user data, flash, card mutation or installer execution was used for validation.
Compilation77 was an intermediate build; only78 is the final release.
