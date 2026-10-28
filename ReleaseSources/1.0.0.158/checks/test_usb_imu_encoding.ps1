param([string]$OutputDirectory="$PSScriptRoot/_artifacts/preparation_20261028/usb-imu-encoding")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskSources=@(
    "$PSScriptRoot/USB512_interface/USB512_interface/NpUsbSensors.cs",
    "$PSScriptRoot/USB512_interface/USB512_interface/NpImuTimeCorrection.cs",
    "$PSScriptRoot/USB512_interface/USB512_interface/NpHostClock.cs",
    "$PSScriptRoot/USB512_interface/USB512_interface/NpAcquisitionAnchor.cs",
    "$PSScriptRoot/USB512_interface/USB512_interface/NpRecordingUtc.cs",
    "$PSScriptRoot/USB512_interface/USB512_interface/NpUsbTimingWire.cs",
    "$PSScriptRoot/USB512_interface/USB512_interface/NpUsbTimingEvidence.cs",
    "$PSScriptRoot/Neuropixels/ClosedLoop/NpClosedLoopEvent.cs",
    "$PSScriptRoot/Neuropixels/ClosedLoop/NpClosedLoopControl.cs",
    "$PSScriptRoot/Neuropixels/ClosedLoop/NpClosedLoopCarrier.cs",
    "$PSScriptRoot/Tests/NpUsbImuEncodingTests.cs"
)
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /r:System.Web.Extensions.dll "/out:$taskOutput/UsbImuEncodingTests.exe" @taskSources
if($LASTEXITCODE){throw 'USB IMU encoding checks failed to compile'}
& "$taskOutput/UsbImuEncodingTests.exe" (Join-Path $taskOutput ([Guid]::NewGuid().ToString('N')))
if($LASTEXITCODE){throw 'USB IMU encoding checks failed'}
