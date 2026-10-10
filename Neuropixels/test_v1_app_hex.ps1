param(
 [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
 [Parameter(Mandatory=$true)][string]$FirmwareDirectory,
 [Parameter(Mandatory=$true)][string]$LegacyTestPackage,
 [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$m=Get-Content -LiteralPath (Join-Path $FirmwareDirectory 'manifest.json') -Raw | ConvertFrom-Json
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'TestNpV1AppHex.exe'
& $compiler /nologo /warnaserror /optimize+ "/out:$exe" "$PSScriptRoot/Tests/TestNpV1AppHex.cs"
if($LASTEXITCODE){throw 'APP HEX reflection test compilation failed'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) `
 (Join-Path $FirmwareDirectory $m.hex_images.sd_ble_app_only.file) `
 (Join-Path $FirmwareDirectory $m.hex_images.fused_bootloader_usb_app.file) `
 ([IO.Path]::GetFullPath($LegacyTestPackage)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Actual Release APP-only HEX gates failed'}
