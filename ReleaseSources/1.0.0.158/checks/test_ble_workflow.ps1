param(
    [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
    [string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-workflow-tests"
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskTests=Join-Path $OutputDirectory 'NpBleWorkflowTests.exe'
& $taskCompiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$taskTests" "$PSScriptRoot/Tests/NpBleWorkflowTests.cs"
if($LASTEXITCODE){throw 'BLE workflow checks did not compile'}
& $taskTests ([IO.Path]::GetFullPath($ConsoleExecutable)) (Join-Path $OutputDirectory 'ble-recordings.png')
if($LASTEXITCODE){throw 'BLE workflow checks failed'}
$taskTransport=Join-Path $OutputDirectory 'NpBleStorageTransportTests.exe'
& $taskCompiler /nologo /warnaserror "/out:$taskTransport" "$PSScriptRoot/Tests/NpBleStorageTransportTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/Ble/NpBleConnection.Storage.cs"
if($LASTEXITCODE){throw 'BLE native transport checks did not compile'}
& $taskTransport
if($LASTEXITCODE){throw 'BLE native transport checks failed'}
$taskV2=Join-Path $OutputDirectory 'NpV2BleCatalogTests.exe'
$taskSource=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
& $taskCompiler /nologo /warnaserror /nowarn:0649 "/out:$taskV2" "$PSScriptRoot/Tests/NpV2BleCatalogTests.cs" "$PSScriptRoot/Tests/NpBleCatalogFileBoundary.cs" "$taskSource/Ble/NpV2BleCatalog.cs" "$taskSource/Ble/NpBleStorage.cs" "$taskSource/Ble/NpV1BleProtocol.cs" "$taskSource/Ble/NpV2SdControl.cs" "$taskSource/Ble/NpSdControl.cs" "$taskSource/NpLogger/NpSdGeometry.cs" "$taskSource/NpLogger/NpV1StoreV2.cs"
if($LASTEXITCODE){throw 'V2 BLE catalog checks did not compile'}
& $taskV2
if($LASTEXITCODE){throw 'V2 BLE catalog checks failed'}
