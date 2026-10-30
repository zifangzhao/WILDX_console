param([string]$OutputDirectory="$PSScriptRoot/_artifacts/first_sync_sd_client_20261028")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'NpFirstSyncSdClientTests.exe'
# The complete frozen core also declares binding fields unused by this client-
# only runner. Keep all other compiler warnings as errors.
& $compiler /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$exe" "$PSScriptRoot/Tests/NpFirstSyncSdClientTests.cs" "$source/NpFirstSyncWire.cs" "$source/NpFirstSyncSd.cs" "$source/NpFirstSyncSdClient.cs"
if($LASTEXITCODE){throw 'Isolated SD client checks did not compile'}
& $exe
if($LASTEXITCODE){throw 'Isolated SD client checks failed'}
