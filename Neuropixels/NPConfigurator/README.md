# NP Channel Selector

Choose sampling channels and save their configuration for **Wild console** and **USB_console**. The editor can run on its own or be embedded in a Windows application.

Launch `NPConfigurator.exe` with its adjacent DLL/runtime files. Requires the .NET 8 Desktop Runtime.

## Use

1. Choose the probe type. Change **Bank** or **Shank** to navigate immediately.
2. Click, drag, or Shift-click on the map to **highlight electrodes only**. Mouse selection does not change sampling, site assignments, the enabled count, or saved configuration. Both range endpoints are included. Ctrl-click adds/removes separate highlights; Ctrl-drag adds a range. Esc cancels a drag.
3. Press **E / Sample on** to enable the highlighted electrodes, **D / Sample off** to disable them, or **Space** to toggle the group. Inactive and unassigned physical electrodes can be highlighted and then enabled.
4. Alternatively, enter **From site / To site**, choose an **Apply action** (Add, Remove, Replace all), and click **Apply range** to change sampling explicitly. Select table rows to edit their currently assigned electrodes, gains, or references; their **Sample** checkboxes also change sampling directly.
5. Click **Save configuration** to create one `.npconfig.json` file. **Open** loads it for further editing. Highlights are temporary UI state and are not saved as sampling selections.

**Open…** also imports SpikeGLX/Open Ephys IMRO tables for standard NP1 and NP2 probes. **Export…** offers IMRO, ProbeInterface geometry JSON, and channel-map CSV. Native JSON preserves disabled channels; IMRO requires every ordinary channel to be enabled. See [supported formats and limitations](STANDARD_FORMATS.md).

Enabled electrodes do **not** need to be continuous. Ctrl-select separate sites/ranges and enable them together, or enable successive selections, including sites from different banks and NP2 shanks. **Enabled electrodes: N / maximum** above the map shows the total across all banks and shanks, and remains visible during action and error messages. NP1 supports 383 ordinary electrodes because one channel is reserved for reference; the other profiles support 384.

Teal sites are enabled, gray sites are inactive, and amber sites are reserved references. Blue outlines show the physical mouse selection, or the assigned electrodes of selected table rows. IDs start at zero. Enabling an electrode automatically disables the previous electrode on the same channel, including across banks or shanks; highlighting it alone never replaces anything. The status message reports how many previous electrodes were disabled. Unrelated electrodes and channel gain/reference settings are retained. Disabling an unassigned highlighted site leaves any conflicting electrode alone. Reference sites are skipped and counted. A selection containing two requested sites on the same channel can be highlighted, but enabling it is rejected atomically since both cannot be sampled together.

**Select bank** applies that bank as the whole sampling selection. **96 sites / shank** applies a balanced NP2 four-shank preset. **Disable all** disables all channels. Changing the Bank/Shank numbers only navigates.

To highlight across banks, click the starting electrode, change **Bank**, then **Shift-click** the ending electrode on the same shank. Changing banks retains the starting point. Press E to enable the highlighted range. Dragging across a bank boundary also works. For example, after **Disable all**, highlight sites **380–390** and press E to enable 11 electrodes across banks 0 and 1.

## Keyboard shortcuts

Click the table or map first. After a map gesture, shortcuts and sampling buttons target the highlighted physical electrodes. Entering the table switches to its selected rows and their current assignments. Selecting a table row brings its assigned electrode into view.

| Key | Action |
| --- | --- |
| **E** | Enable highlighted physical electrodes, or selected table rows with assigned sites |
| **D** | Disable selected electrodes, retaining their site assignments |
| **Space** | Toggle the group: enable all if any eligible highlighted electrode is inactive; otherwise disable all |
| **Ctrl+A** | Select all table rows |
| **Ctrl / Shift + click** in the table | Select multiple rows |
| **Ctrl + click / drag** on the map | Add separate highlights; Ctrl-click a highlighted site to remove its highlight |
| **Esc** while dragging | Cancel the range |

When working from table selection, enabling skips unconnected rows and reports their count. To assign new physical sites, highlight them on the map and press E or Sample on. Typing in metadata, numeric inputs, dropdowns, or cell editors keeps its normal behavior. Sampling shortcuts are ignored during a map drag.

## Reuse in the two apps

The shared JSON format includes the probe profile, channel-to-site/shank mapping, enable/standby settings, gains, and references. Existing configuration files remain compatible. See [INTEGRATION.md](INTEGRATION.md) for the file contract and the reusable editor interface.

The editor's job is selection and configuration transfer. Device operations belong to the receiving application.

## Build and verify

```powershell
dotnet build Tools/NPConfigurator/NPConfigurator.csproj --configfile Tools/NuGet.offline.config -c Release
dotnet build Tools/NPUsbWinApp/NPUsbWinApp.csproj --configfile Tools/NuGet.offline.config -c Release
dotnet build Tools/NPConfigurator/Tests/NPConfigurator.Tests.csproj --configfile Tools/NuGet.offline.config -c Release
dotnet Tools/NPConfigurator/Tests/bin/Release/net8.0/NPConfigurator.Tests.dll
dotnet Tools/NPConfigurator/bin/Release/net8.0-windows/NPConfigurator.dll --ui-smoke tmp/npconfig-ui-smoke
```

Use `--no-restore` in place of `--configfile ...` when dependencies have already been restored. No NuGet packages are required.

The existing tests cover mapping, sampling, ranges, conflicts, configuration round trips, and immediate navigation. UI smoke checks exercise all four probe profiles and the embedded editor interface, including rendered multi-row highlights, keyboard enable/disable/toggle, editing focus, unconnected rows, and automatic map navigation. Cross-bank checks cover every bank boundary and shank, forward/reverse Shift-click after spinner or typed navigation, dragging, typed ranges, highlights, keyboard sampling, and reuse of disabled assignments. Live repaint checks cover repeated mouse-wheel and scrollbar scrolling so the fixed shank header cannot leave copied trails in the map.

Automatic replacement checks cover explicit enable/toggle commands after mouse selection, single-site and range conflicts across banks/shanks, retained unrelated sites and gains, map colors/highlights, and toggling replacements off again. Count checks cover clearing, mouse selection without mutation, sampling buttons and checkboxes, replacements, errors, and visibility at the minimum window size. Physical-selection tests cover Ctrl selection, unassigned highlights, unchanged dirty/JSON state, safe disable of conflicting unassigned sites, table focus changes, and atomic enabling of arbitrary site sets.

An optional native JSON or IMRO filename opens that file in the GUI. `--validate file` validates either format, and `--render directory` renders all four layouts. Developer-only `--export file directory` still generates the existing register/map review bundle. Standard file-conversion commands are documented in [STANDARD_FORMATS.md](STANDARD_FORMATS.md).

`catalog.json` is embedded in the build. Its mapping sources and register details are documented in [the document review](../../NP_CHANNEL_CONFIGURATION_REVIEW.md).
