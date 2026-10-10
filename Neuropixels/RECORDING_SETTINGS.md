# Recording setup snapshots

USB_PCconsole 1.0.3.21 saves `recording_settings.json` in each NP recording segment and each DAT output folder. Nonmerged multi-probe live recordings also copy each physical probe's snapshot into its `probeN` child folder. A merged DAT remains at the recording root, with 384 contiguous channels per physical probe in ascending port order.

Each active probe gets `probe_N.json`. It records identity, channel mapping, configuration state and relative references to available native configuration, selection and firmware receipt files. An absent receipt or default `NPM1` with `HasSelection=false` leaves exact routing/gains unavailable. GUI defaults are never substituted for missing device or historical evidence.

`probe_N.imro` is saved when a known request or device selection can be represented by the existing IMRO serializer. Read its `imro_state`: a requested table is initially `requested_unverified`. Only an existing applied receipt or firmware-held snapshot can confirm it. Imported IMRO bytes, including BOM and line endings, are preserved when their acquisition settings are unchanged and their compiled selection matches the held receipt. Otherwise the held snapshot is serialized, or IMRO remains unavailable. Disabled ordinary channels and NRIC settings may require native JSON instead of IMRO. The separately named `.requested.imro` always retains the request.

The shared native configurator JSON has an optional `OriginalImro` base64 property for imported bytes. Older configurations without that property serialize as before. The file-only helper commands `--recording-imro input.json output.imro` and `--snapshot-v1 input.npb1 output.json` reuse the existing standard-format and channel-selection serializers. These commands do not access hardware.

## Shared fields

| Field | Meaning |
| --- | --- |
| `schema_version`, `format` | Version 1, `neuropixels.recording_settings` |
| `source` | `live_usb`, `sd_download` or `sd_export` |
| `device` | USB path, available UID, session and status/recording format evidence; unavailable firmware semantic version is explicitly null |
| `requested.ap_sample_rate_hz`, `requested.probe_mask` | User or automatic request, before profile fallback/admission |
| `actual.ap_sample_rate_hz`, `actual.probe_mask`, `actual.evidence` | Validated READY/wire profile or saved recording descriptor |
| `decoded`, `output` | Native decoded rate and rate written to DAT, separately |
| `*.lfp_sample_rate` | `present`, `rate_numerator`, `rate_denominator`, and numeric `rate_hz` |
| `output.dat_layout`, `output.channel_count_per_dat` | Merged or per-probe layout; child folders have 384 channels |
| `misc_output` | File, output grid, record layout and held-value semantics; historical collection rate stays unknown |
| `streams`, `camera_output` | Misc/IMU/camera availability and files; sensor or camera acquisition rate is never inferred from an output grid |
| `probes` | Per-probe snapshots and relative file references |
| `recording_start_time_utc_host` | Host recording-start request time, including setup before acquisition; not an ADC synchronization claim |
| `state`, `recording_complete`, `failure` | Final capture/export outcome |

NP1 LFP always retains the exact native AP/12 fraction. Native 20 kHz therefore records numerator 20000 and denominator 12, rather than a truncated 1666 Hz. A 30 kHz request admitted at 15 kHz and written as 20 kHz AP records requested=30000, actual/decoded=15000, output=20000 and LFP=1250. NP2 records LFP as absent, with null rate fields.

Live NP1 `analogin.dat` has headerless 32-byte records of 16 little-endian int16 words on a 1250 Hz output grid. Its 800 us device-time grid holds the latest sensor values; this is not 1250 Hz sensor acquisition. Child setup files reference this shared root file using `../analogin.dat`.

Historical SD exports use only validated saved descriptors and recorded evidence. Missing identity, channel settings and original UTC remain unavailable. NP2's validated `utc.bin`, when present, supplies a capture-start bracket rather than an exact ADC start time. Raw-only downloads do not claim decoded DAT rates or output files. Each download now has its own recording folder, with an optional `decoded` child.

Snapshot serialization occurs before START and after STOP, outside read/decode/sample callbacks. The existing startup confirmation and post-stop manifest requests are reused; metadata adds no device requests or controls. Each recovered segment creates a fresh snapshot through the common recording-start path.

## Offline regression

Build the console and bundled configurator in Release without installer packaging, then run:

```powershell
Neuropixels\test_np_recording_settings.ps1 -ConfiguratorExecutable <Release\NPConfigurator\NPConfigurator.exe>
```

The fixture covers exact IMRO preservation, edited imports, mismatched receipts plus serializer failure, native/fallback/upsampled rates, probe child folders, merged output, distinct segments, historical unknown state, raw-only downloads and NP2 LFP absence. No hardware is accessed.
