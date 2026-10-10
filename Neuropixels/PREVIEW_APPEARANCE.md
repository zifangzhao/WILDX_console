# Preview canvas and waveform icon

The NP plot and electrode map share a muted slate canvas. Light channel labels
and captions remain readable, while baselines and inactive contacts are subdued.
Blue, Viridis, Turbo and Grayscale use brighter channel colours; every one of
their 64 rows has at least 4.5:1 contrast against the canvas. These display
palettes retain their hue families and do not encode waveform amplitude.

The same palette entries supply trace pens and map brushes. Physical geometry
ordering, 64-channel pages, AP/LFP gathering, fallback routing, trace geometry,
clipping, reduction, renderer timing, queue, decoder and recorded DAT paths are
unchanged. Waiting and resized canvases use the same background without a white
flash. Appearance resources are initialized once and add no per-frame work.

Resources/Waveform.ico is the native executable, main-window and NP-preview
icon. The installer and uninstaller use the same icon through the standard MUI
Icon/UninstallIcon directives. Resources/Waveform.svg retains the editable
waveform/W vector design; the supplied System.Drawing generator is retained
beside it. No PNG runtime dependency or new package is introduced.
The generator writes wildx-wave.ico into its OutputDirectory; copy that file
to Resources/Waveform.ico after regenerating. The ICO contains native DIB
variants at 16/24/32/48/64/128 pixels plus a standard PNG-backed 256-pixel entry.

Run test_np_preview_theme.ps1 for all-palette contrast and rendered pixels,
test_np_preview_mapping.ps1 for existing physical-order/render/input-invariance
checks, and Tests/test_np_system_info.ps1 against the actual Release executable.
These are offline software checks and do not access recording hardware.
