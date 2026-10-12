# NP2 nominal polarity correction — WILDX1.0.0.9, 2026-10-12

Confirmed gap: NP1 already used512-raw10; NP2 USB/SD decoded raw0..4095 and
its preview used2048 as the pedestal. Open Ephys's NP2 documentation explicitly
states that the amplifiers invert electrode signals and that software applies
-1 to undo this; their default polarity correction is enabled:
https://open-ephys.github.io/bonsai-onix1-docs/api/OpenEphys.Onix1.NeuropixelsV2QuadShankProbeConfiguration.html
https://open-ephys.github.io/bonsai-onix1-docs/articles/hardware/np2e/gui.html#inverting-the-polarity-of-ephys-data

## Implementation

Shared NP2 decoded-sample boundary now emits2048-raw12 exactly once, range
-2047..2048. USB recorder and SD exporter consume those signed384-channel rows
unchanged. USB preview reference is0, without another inversion. BLE transport
stays raw; its disposable PC plot calls the same nominal conversion. NP1
polarity, channel order, framing/counters, source timing, transport queues,
completion guards, firmware, IMRO and original RAW bytes are unchanged.

USB/SD reports declare `np2_signed_nominal_v1`, transform, range and polarity.
No gain/comparator calibration or microvolt scaling is claimed. Existing NP2
DAT files from before1.0.0.9 remain unsigned raw12 and are not silently rewritten.
Only if such a file is confirmed to be old NP2 raw-code DAT, its corresponding
nominal signed count is2048-old_code; do not apply this again to corrected DAT.

## Actual evidence

One scoped SD DLL build and one Release GUI build, preserving published frozen
BLE/configuration/export inputs and excluding private SD v12/triple development.
Focused actual-GUI/actual-DLL fixture PASS1,827,449 checks: all4096 values,
all384 channels/all3 ports, packed3/6, PSSI8 and split9,15/20/30k offline replay,
complete row counts/channel order, signed little-endian USB and SD DAT, unchanged
original/saved RAW, preview row delivery, zero pedestal/trace direction, BLE
plot sign and SD metadata/legacy markers. OldGUI8 negative control refuses at
its missing polarity conversion. The test harness's initial reflection ctor
ambiguity was fixed; no second production build was needed.

Runnable fixture: Neuropixels/Tests/TestNp2Polarity.cs. Compile with Roslyn csc
`/optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll`; run with four
arguments: checkedGUI.exe, checkedNpV2SdDatExport.dll, fresh output directory,
and maintainedNeuropixels/USBConsoleV2/NpV2UsbConsole.cs. It never enumerates a
device, opens a hardware connection or sends a command. Existing test oracles
were adjusted to signed values; broader suites were not rerun.

One NSIS package; all13 staged runtime identities checked,11 unchanged.
GUI SHA256EA3163288A8A6B49C36DE7B62AD043EAB36ECF2D682A68AC703A2DF375AD2355.
SD DLL SHA2569864B4997DD241E9B90023D088558483F908D7DC2C4410BDBC09D3BAAD7134E5.
Installer2250015B/SHA25654118FC3E7E8E989D71059B7CB40F7CF97994C597C0352C3F09D3A0BDCE8FCFD.
Copied identically to canonical and V2 LATEST_INSTALLERS; superseded8 release
files are recoverable in legacy. Firmware delivery identities remain unchanged.

No hardware access, recording/readback, installer execution/extraction or new
full fused-pair qualification. Earlier USB capture and004B DFU evidence are
retained separately; no new analog calibration/performance/lossless claim.
Removable build/regression/package evidence: `_artifacts/np2_polarity_20261012`.
