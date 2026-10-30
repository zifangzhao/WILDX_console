param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v2-ble-catalog-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$exe=Join-Path $OutputDirectory 'NpV2BleCatalogTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /langversion:7.3 /warnaserror /nowarn:0649 "/out:$exe" "$PSScriptRoot/Tests/NpV2BleCatalogTests.cs" "$PSScriptRoot/Tests/NpBleCatalogFileBoundary.cs" "$source/Ble/NpV2BleCatalog.cs" "$source/Ble/NpBleStorage.cs" "$source/Ble/NpV1BleProtocol.cs" "$source/Ble/NpV2SdControl.cs" "$source/Ble/NpSdControl.cs" "$source/NpLogger/NpSdGeometry.cs" "$source/NpLogger/NpV1StoreV2.cs" "$source/NpImuGridWire.cs" "$source/NpImuGridCarrier.cs"
if($LASTEXITCODE){throw 'V2 BLE catalog fixture did not compile'}
& $exe
if($LASTEXITCODE){throw 'V2 BLE catalog checks failed'}
