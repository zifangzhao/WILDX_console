param([string]$OutputDirectory="$PSScriptRoot/_artifacts/sd_grid16_append_protocol_20261030/codec")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$taskOutput/Grid16AppendTests.exe" "$PSScriptRoot/Tests/NpFirstSyncSdGrid16AppendTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpFirstSyncSd.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpFirstSyncWire.cs"
if($LASTEXITCODE){throw 'Grid16 append managed protocol fixture compile failed'}
& "$taskOutput/Grid16AppendTests.exe" $taskOutput
if($LASTEXITCODE){throw 'Grid16 append managed protocol checks failed'}
