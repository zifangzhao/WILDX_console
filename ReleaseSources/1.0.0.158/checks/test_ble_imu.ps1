param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/v2_ble_imu_20261023/tests129")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskTest=Join-Path $OutputDirectory 'NpBleImuTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /r:System.Core.dll "/out:$taskTest" "$PSScriptRoot/Tests/NpBleImuTests.cs"
if($LASTEXITCODE){throw 'BLE IMU tests did not compile'}
& $taskTest ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'recording')))
if($LASTEXITCODE){throw 'BLE IMU tests failed'}
