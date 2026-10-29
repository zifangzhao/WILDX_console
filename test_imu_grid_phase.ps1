param([string]$OutputDirectory="$PSScriptRoot/_artifacts/imu_grid_phase_20261029")
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /warnaserror /r:System.Core.dll /main:NpImuGridPhaseTests "/out:$taskOutput/ImuGridPhaseTests.exe" "$PSScriptRoot/USB512_interface/USB512_interface/NpImuGridPhase.cs" "$PSScriptRoot/Tests/NpImuGridPhaseTests.cs"
if($LASTEXITCODE){throw 'NSGP managed fixture compile failed'}
$taskVc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/VC/Auxiliary/Build/vcvars64.bat'
$taskNative='call "'+$taskVc+'" >nul && cl /nologo /std:c11 /W4 /WX /O2 /I"'+$PSScriptRoot+'/Neuropixels/Timing" "'+$PSScriptRoot+'/Tests/NpImuGridPhaseGolden.c" /Fo"'+$taskOutput+'/golden.obj" /Fe"'+$taskOutput+'/golden.exe"'
& $env:ComSpec /d /c $taskNative
if($LASTEXITCODE){throw 'NSGP native fixture compile failed'}
& "$taskOutput/ImuGridPhaseTests.exe" generate "$taskOutput/vectors.bin" "$taskOutput/expected.bin"
if($LASTEXITCODE){throw 'NSGP managed checks failed'}
& "$taskOutput/golden.exe" "$taskOutput/vectors.bin" "$taskOutput/native.bin"
if($LASTEXITCODE){throw 'NSGP native validation parity failed'}
& "$taskOutput/ImuGridPhaseTests.exe" verify "$taskOutput/expected.bin" "$taskOutput/native.bin"
if($LASTEXITCODE){throw 'NSGP exact packed bytes differ'}
