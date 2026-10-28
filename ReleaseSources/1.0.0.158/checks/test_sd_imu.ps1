param([string]$OutputDirectory="$PSScriptRoot/_artifacts/preparation_20261028/sd-imu-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$out=[IO.Path]::GetFullPath($OutputDirectory)
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $compiler /nologo /warnaserror /r:System.Core.dll "/out:$out/SdImuTests.exe" "$PSScriptRoot/Tests/NpSdImuTests.cs" "$PSScriptRoot/Neuropixels/SD/NpSdImuExport.cs"
if($LASTEXITCODE){throw 'SD IMU checks failed to compile'}
$recording=Join-Path $out ([Guid]::NewGuid().ToString('N'))
& "$out/SdImuTests.exe" $recording
if($LASTEXITCODE){throw 'SD IMU checks failed'}
