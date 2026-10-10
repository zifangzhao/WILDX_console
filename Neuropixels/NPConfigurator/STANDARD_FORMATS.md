# Neuropixels interchange formats

Use **Open…** for native `.npconfig.json` or SpikeGLX/Open Ephys `.imro` files.
Use **Export…** for IMRO, ProbeInterface JSON, or the channel-map CSV.
**Save configuration** always retains the complete editable native JSON document,
including enabled/disabled channels and identity fields used by the two console apps.
Importing IMRO creates an unsaved document; saving it does not overwrite the IMRO.
Import validates the complete file before replacing the current selection.

## Format coverage

| Format | Import | Export | Purpose |
| --- | --- | --- | --- |
| Native `.npconfig.json` | Yes | Yes | Complete sampling selection and settings for Wild console / USB_console |
| SpikeGLX/Open Ephys `.imro` | Yes | Yes | Standard Neuropixels readout routing, references, NP1 gains and filter |
| ProbeInterface `.json` | No | Yes | Enabled electrode geometry and channel indices for analysis tools |
| Channel-map `.csv` | No | Yes | Review all 384 channel assignments and sampling flags |

ProbeInterface JSON and native JSON are different schemas. To edit a routing
configuration, open native JSON or IMRO; a geometry file alone does not specify
all acquisition settings. Recording formats such as `.bin`, `.cbin`, NWB and
Open Ephys recordings are outside this configuration editor's file interface.

## IMRO models and references

Both numeric type headers and the part-number headers introduced in SpikeGLX
20260115 are accepted. All supported tables have 384 channel rows, numbered 0–383.

| Profile | Numeric types | Part-number headers | Reference interpretation |
| --- | --- | --- | --- |
| NP1 standard | `0` | `NP1000`, `NP1001` | External, tip, shared internal-reference bank |
| NP2 single, legacy | `21` | `NP2000` | External, tip |
| NP2 single, commercial | `2003`, `2004` | `NP2003`–`NP2006` | External, ground, tip |
| NP2 four-shank, legacy | `24` | `NP2010` | External, tip of shank 0–3 |
| NP2 four-shank, commercial | `2013`, `2014` | `NP2013`, `NP2014` | External, ground, tip of shank 0–3 |

For new configurations, export defaults to types 0, 2003 or 2013. An imported
identifier is retained in **Model / revision** through native JSON save/reopen,
so legacy and commercial reference codes remain distinct. An exact supported
part number entered there is also used when exporting. The legacy NP2 menu
item explicitly exports type 21 or 24; ground cannot be encoded in those types.
Older numeric aliases 2004/2014 import successfully and export as canonical
types 2003/2013, which current readers recognize. Part-number headers remain intact.
For four shanks, `Tip` follows the channel's selected shank; `Tip0`–`Tip3` select
one particular shank's tip reference.

NP2 gain is fixed by the model, not an IMRO field: legacy NP2000/NP2010 use 80,
and commercial NP2003–NP2006/NP2013–NP2014 use 100. The editor displays this
fixed gain and updates it when the exact model identifier changes. NP2 has one
full-band amplifier; the native `LfpGain` mirrors its gain for compatibility,
while ProbeInterface's unused NP2 `lf_gains` annotation is 1. Exporting an
explicit different IMRO type targets that model's fixed hardware properties.

IMRO does not contain a per-channel standby/enable mask. The editor therefore
refuses export when ordinary channels are disabled or unassigned. Use native
JSON for such selections, or ProbeInterface for their geometry. NP1's reserved
reference channel 191 remains a placeholder row in IMRO and is not treated as
an ordinary enabled electrode. Its placeholder routing bank is normalized to
the configured internal-reference bank on export.

NRIC has custom input wiring rather than a standard probe geometry; use native
JSON or CSV. UHD, NHP, opto, NP3 and quad-base probe geometries are not supported.
NP2 joined-bank masks and legacy on-shank references, and NP1 configurations
with several different internal-reference banks, are rejected explicitly.
NP2 electrode routing must agree with the installed Imec workbook catalog;
inconsistent tables are rejected without rewriting channel assignments.

The importer preserves per-channel NP1 gains and filters. Open Ephys documents
that its IMRO import uses the last channel's settings for probe-wide reference
and gain settings. Check the receiving application's capabilities when using
different values across channels. IMRO omits connector, serial, calibration,
and application-specific metadata; retain the native JSON alongside it.

## ProbeInterface export

Exports only electrodes with an assigned site and sampling enabled, sorted by
hardware channel. Contact IDs are `e<site>` for single shanks and
`s<shank>e<site>` for four shanks. Coordinates are in micrometres, relative to
the lowest electrode row: NP1 staggered 32 µm columns / 20 µm rows, NP2 aligned
32 µm columns / 15 µm rows, and 250 µm between NP2 shanks. Contact width is 12 µm.
The NP2006 part-number variant uses staggered columns with 15 µm rows.
These are probe-local coordinates, not anatomical or dissection coordinates.

- **384-channel stream** keeps hardware channel IDs in `device_channel_indices`.
  Disabled electrodes leave gaps, including NP1 channel 191. Use with a full
  384-channel AP or wideband stream in hardware-channel order.
- **Selected-channel stream** assigns dense indices 0…N−1. Use only when data
  contains exactly the enabled channels, in ascending hardware-channel order.

Neither mode assumes sync channels, combined AP/LFP columns, reordered columns,
or a different recording layout. Match the geometry to the actual data layout.
Annotations retain hardware IDs, requested gains/references, configuration ID
and `NotApplied` state; exporting does not assert that hardware used the settings.

## Command line

```powershell
NPConfigurator.exe --validate probe.imro
NPConfigurator.exe --import-imro probe.imro probe.npconfig.json
NPConfigurator.exe --export-imro probe.npconfig.json probe.imro
NPConfigurator.exe --export-imro probe.npconfig.json legacy.imro 21
NPConfigurator.exe --export-probeinterface probe.npconfig.json geometry.json
NPConfigurator.exe --export-probeinterface probe.npconfig.json compact.json --selected-channels
```

The reusable C# API is `StandardFormats.Load`, `FromImro`, `ToImro`, and
`ToProbeInterface`. The native schema and the host process bridge are unchanged.
NP2 four-shank readers must accept the added explicit `Tip0`–`Tip3` reference
strings if they consume configurations imported from external IMRO files.

## Verification and sources

The model tests exercise numeric/part-number headers, mixed banks/shanks,
references, NP1 gains/filters, disabled masks, malformed tables, source
immutability and both geometry channel layouts. Pass a fixture directory to
`NPConfigurator.Tests.dll` to write interoperability examples. The independent
reader check uses ProbeInterface 0.4.0 and NumPy:

```powershell
python Tools/NPConfigurator/Tests/check_probeinterface.py path/to/fixtures
```

The WinForms smoke check also tests successful IMRO import and failed import
without replacing the current selection. The shared editor builds in NPUsbWinApp.

- [SpikeGLX IMRO table specification](https://billkarsh.github.io/SpikeGLX/help/imroTables/)
- [Open Ephys Neuropixels PXI import/export](https://open-ephys.github.io/gui-docs/User-Manual/Plugins/Neuropixels-PXI.html)
- [ProbeInterface JSON specification](https://probeinterface.readthedocs.io/en/main/format_spec.html)
- [ProbeInterface Neuropixels reader](https://github.com/SpikeInterface/probeinterface/blob/main/src/probeinterface/neuropixels_tools.py)
- [ProbeInterface probe geometry catalog](https://github.com/SpikeInterface/probeinterface/blob/main/src/probeinterface/resources/neuropixels_probe_features.json)
