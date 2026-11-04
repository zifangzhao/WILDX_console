param([string]$OutputDirectory="$PSScriptRoot/_artifacts/closed_loop_20261027/workflow")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$out=[IO.Path]::GetFullPath($OutputDirectory)
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$ui="$PSScriptRoot/USB512_interface/USB512_interface"
$cl="$PSScriptRoot/Neuropixels/ClosedLoop"
& $compiler /nologo /warnaserror /nowarn:0649 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$out/WorkflowTests.exe" "$PSScriptRoot/Tests/NpClosedLoopWorkflowTests.cs" "$ui/NpClosedLoopSettings.cs" "$ui/NpClosedLoopV2UsbLog.cs" "$ui/NpFirstSyncWire.cs" "$ui/NpFirstSyncUsb.cs" "$ui/NpFirstSyncUsbEvidence.cs" "$ui/NpFirstSyncSd.cs" "$ui/WildXTheme.cs" "$ui/NpAcquisitionAnchor.cs" "$ui/NpRecordingUtc.cs" "$ui/NpHostClock.cs" "$ui/NpUsbTimingWire.cs" "$ui/NpUsbTimingEvidence.cs" "$cl/NpClosedLoopEvent.cs" "$cl/NpClosedLoopControl.cs" "$cl/NpClosedLoopCarrier.cs" "$cl/NpClosedLoopLog.cs"
if($LASTEXITCODE){throw 'Closed-loop workflow fixture failed to compile'}
& "$out/WorkflowTests.exe" $out "$PSScriptRoot/Tests/Fixtures/closed-loop/metadata.bin" "$PSScriptRoot/Tests/Fixtures/closed-loop/events.npcl"
if($LASTEXITCODE){throw 'Closed-loop workflow fixture failed'}
