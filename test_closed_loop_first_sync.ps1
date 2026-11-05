param([string]$OutputDirectory='C:/Frank/Code/USB_PCconsole/_artifacts/closed_loop_fix_20261102/host/source-binding')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOut=[IO.Path]::GetFullPath($OutputDirectory);$taskUi=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskSources=@('NpClosedLoopV2UsbLog','NpFirstSyncWire','NpFirstSyncUsb','NpFirstSyncSd','NpFirstSyncUsbEvidence','NpUsbTimingWire','NpUsbTimingEvidence','NpAcquisitionAnchor','NpRecordingUtc','NpHostClock') | ForEach-Object {Join-Path $taskUi ($_+'.cs')}
$taskSources+=@('NpClosedLoopEvent','NpClosedLoopControl','NpClosedLoopCarrier','NpClosedLoopLog') | ForEach-Object {Join-Path "$PSScriptRoot/Neuropixels/ClosedLoop" ($_+'.cs')}
$taskSources+=@('NpUsbTimingSession','NpUsbTimingOut','NpUsbSensors','NpImuTimeCorrection','NpFineClockWire','NpSensorFineWire','NpRtcStartWire','NpRtcUsbArchive','NpRtcSdArchive','NpRtcRecordingUtc','NpSensorFine64Wire','NpSensorFineTime','NpSdFineAnalog','NpUsbFineSensors','NpDerivedImuGridUtc','NpFirstSyncUtc','NpFirstSyncSdUtc','NpFirstSyncUsbUtc','NpFineSdArchive','NpFineClockFit','NpFineClockRecording','NpFineClockEvidenceImport') | ForEach-Object {Join-Path $taskUi ($_+'.cs')}
$taskSources+=@('NpMetadataFile','NpSdImuExport') | ForEach-Object {Join-Path "$PSScriptRoot/Neuropixels/SD" ($_+'.cs')}
$taskExe=Join-Path $taskOut 'ClFirstSyncBindingTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /nowarn:0649 /r:System.Core.dll /r:System.Web.Extensions.dll /main:NpClosedLoopFirstSyncBindingTests "/out:$taskExe" @taskSources "$PSScriptRoot/Tests/NpFirstSyncWireTests.cs" "$PSScriptRoot/Tests/NpUsbTimingEvidenceTests.cs" "$PSScriptRoot/Tests/NpClosedLoopFirstSyncBindingTests.cs"
if($LASTEXITCODE){throw 'Actual CL first-sync binding compile failed'}
& $taskExe $taskOut
if($LASTEXITCODE){throw 'Actual CL first-sync binding checks failed'}
