param([string]$OutputDirectory="$PSScriptRoot/_artifacts/imu_sample_time_20261023/tests",[string]$ConsoleExecutable)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /r:System.Web.Extensions.dll "/out:$OutputDirectory/NpImuTimeCorrectionTests.exe" "$PSScriptRoot/USB512_interface/USB512_interface/NpImuTimeCorrection.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpUsbSensors.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpHostClock.cs" "$PSScriptRoot/Tests/NpImuTimeCorrectionTests.cs"
if($LASTEXITCODE){throw 'IMU correction regression compile failed'}
$taskArgs=@($OutputDirectory,"$PSScriptRoot/_artifacts/command_paths_20261023/hardware/v2-record10","$PSScriptRoot/_artifacts/first_sample_precision_20261023/v2-usb70")
if($ConsoleExecutable){$taskArgs+=$ConsoleExecutable}
& "$OutputDirectory/NpImuTimeCorrectionTests.exe" @taskArgs
if($LASTEXITCODE){throw 'IMU correction regression failed'}
