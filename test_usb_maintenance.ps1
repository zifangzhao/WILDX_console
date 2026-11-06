param(
    [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
    [string]$OutputDirectory="$PSScriptRoot/_artifacts/usb_maintenance"
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /warnaserror /langversion:latest ("/out:"+(Join-Path $taskOutput 'UsbMaintenanceTests.exe')) (Join-Path $PSScriptRoot 'Tests/NpUsbMaintenanceTests.cs')
if($LASTEXITCODE){throw 'USB maintenance test compilation failed'}
& (Join-Path $taskOutput 'UsbMaintenanceTests.exe') ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'USB maintenance regression failed'}
