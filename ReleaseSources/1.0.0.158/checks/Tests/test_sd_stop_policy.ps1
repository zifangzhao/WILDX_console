param([string]$OutputDirectory="$(Split-Path $PSScriptRoot -Parent)/_artifacts/preparation_20261028/stop-policy")
$ErrorActionPreference='Stop'
$sourceRoot=Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$testExe=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'NpSdStopPolicyTests.exe'
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$appRoot=Join-Path $sourceRoot 'USB512_interface/USB512_interface'
& $compiler /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$testExe" `
    "$PSScriptRoot/NpSdStopPolicyTests.cs" "$PSScriptRoot/NpBleCatalogFileBoundary.cs" `
    "$appRoot/Ble/NpV2SdControl.cs" "$appRoot/Ble/NpV1BleProtocol.cs" "$appRoot/Ble/NpSdControl.cs" `
    "$appRoot/NpLogger/NpSdGeometry.cs" "$appRoot/NpLogger/NpV1StoreV2.cs"
if($LASTEXITCODE){throw 'SD Stop-policy checks failed to compile'}
& $testExe
if($LASTEXITCODE){throw 'SD Stop-policy checks failed'}
