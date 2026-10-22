param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v1-ble-start-speed-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskSource=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskReuse=Join-Path $OutputDirectory 'NpV1ConfigurationReuseTests.exe'
& $taskCompiler /nologo /warnaserror /r:System.Core.dll "/out:$taskReuse" "$PSScriptRoot/Tests/NpV1ConfigurationReuseTests.cs" "$PSScriptRoot/Neuropixels/V1Capture/Configuration/NpConfigClient.cs"
if($LASTEXITCODE){throw 'V1 map reuse checks did not compile'}
& $taskReuse
if($LASTEXITCODE){throw 'V1 map reuse checks failed'}
$taskBinding=Join-Path $OutputDirectory 'UnusedSdBinding.cs'
[IO.File]::WriteAllText($taskBinding,'namespace CE32_console {static class NpV1StoreBinding {internal static void Validate(byte[] h,byte[] f,int offset=0){throw new System.InvalidOperationException("Unexpected SD export in clock test");}}}')
$taskClock=Join-Path $OutputDirectory 'NpBleClockTests.exe'
& $taskCompiler /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$taskClock" "$PSScriptRoot/Tests/NpBleClockTests.cs" "$taskSource/Ble/NpBleClock.cs" "$taskSource/Ble/NpV2OnlineClock.cs" "$taskSource/Ble/NpSdControl.cs" "$taskSource/NpLogger/NpSdGeometry.cs" $taskBinding
if($LASTEXITCODE){throw 'BLE clock checks did not compile'}
& $taskClock
if($LASTEXITCODE){throw 'BLE clock checks failed'}
