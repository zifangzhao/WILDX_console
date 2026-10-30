param([string]$OutputDirectory="$PSScriptRoot/_artifacts/sd_grid16_protocol")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$taskOut=[IO.Path]::GetFullPath($OutputDirectory)
$taskCsc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCsc /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$taskOut/Grid16Tests.exe" "$PSScriptRoot/Tests/NpFirstSyncSdGrid16Tests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpFirstSyncSd.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpFirstSyncWire.cs"
if($LASTEXITCODE){throw 'Grid16 codec fixtures compile failed'}
& "$taskOut/Grid16Tests.exe" generate "$taskOut/vectors.bin"
if($LASTEXITCODE){throw 'Grid16 managed codec check failed'}
$taskVc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/VC/Auxiliary/Build/vcvars64.bat'
$taskNative='call "'+$taskVc+'" >nul && cl /nologo /std:c11 /W4 /WX /O2 /I"'+$PSScriptRoot+'/Neuropixels/Timing" "'+$PSScriptRoot+'/Tests/NpFirstSyncSdGrid16Golden.c" /Fo"'+$taskOut+'/golden.obj" /Fe"'+$taskOut+'/golden.exe"'
& $env:ComSpec /d /c $taskNative
if($LASTEXITCODE){throw 'Grid16 native header compile failed'}
& "$taskOut/golden.exe" "$taskOut/vectors.bin" "$taskOut/production-c-v4-golden.bin"
if($LASTEXITCODE){throw 'Grid16 C parity failed'}
& "$taskOut/Grid16Tests.exe" native "$taskOut/production-c-v4-golden.bin"
if($LASTEXITCODE){throw 'Grid16 native golden roundtrip failed'}
