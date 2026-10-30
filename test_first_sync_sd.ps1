param([string]$OutputDirectory="$PSScriptRoot/_artifacts/first_sync_sd_20261028")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /warnaserror /r:System.Core.dll /main:NpFirstSyncSdTests "/out:$taskOutput/FirstSyncSdTests.exe" "$PSScriptRoot/Tests/NpFirstSyncSdTests.cs" "$PSScriptRoot/Tests/NpFirstSyncWireTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpFirstSyncSd.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpFirstSyncWire.cs"
if($LASTEXITCODE){throw 'First-sync SD C# checks failed to compile'}
& "$taskOutput/FirstSyncSdTests.exe" generate "$taskOutput/requests.bin"
if($LASTEXITCODE){throw 'First-sync SD C# checks failed'}
$taskVc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/VC/Auxiliary/Build/vcvars64.bat'
$taskNative='call "'+$taskVc+'" >nul && cl /nologo /std:c11 /W4 /WX /O2 /I"'+$PSScriptRoot+'/Neuropixels/Timing" "'+$PSScriptRoot+'/Tests/NpFirstSyncSdGolden.c" /Fo"'+$taskOutput+'/golden.obj" /Fe"'+$taskOutput+'/golden.exe"'
& $env:ComSpec /d /c $taskNative
if($LASTEXITCODE){throw 'First-sync SD C fixture failed to compile'}
& "$taskOutput/golden.exe" "$taskOutput/requests.bin" "$taskOutput/requests.bin.sources" "$taskOutput/replies.bin"
if($LASTEXITCODE){throw 'First-sync SD C parity checks failed'}
& "$taskOutput/FirstSyncSdTests.exe" reply "$taskOutput/replies.bin"
if($LASTEXITCODE){throw 'First-sync SD C to C# exact roundtrips failed'}
