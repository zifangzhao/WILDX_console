param([string]$OutputDirectory="$PSScriptRoot/_artifacts/firmware-notice-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$exe=Join-Path $OutputDirectory 'NpFirmwareNoticeTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /nowarn:0649 /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$exe" "$PSScriptRoot/Tests/NpFirmwareNoticeTests.cs" "$source/NpLogger/NpFirmwareCatalog.cs" "$source/Ble/NpSdControl.cs" "$source/NpLogger/NpSdGeometry.cs"
if($LASTEXITCODE){throw 'Firmware notice checks did not compile'}
& $exe "$PSScriptRoot/Firmware"
if($LASTEXITCODE){throw 'Firmware notice checks failed'}
