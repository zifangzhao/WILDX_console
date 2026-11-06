param(
    [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
    [string]$OutputDirectory="$PSScriptRoot/_artifacts/usb_device_labels",
    [switch]$ReadWindowsMetadata
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /warnaserror ("/out:"+(Join-Path $taskOutput 'DeviceLabelTests.exe')) (Join-Path $PSScriptRoot 'Tests/NpUsbDeviceLabelTests.cs')
if($LASTEXITCODE){throw 'Device-label test compilation failed'}
$taskArguments=@([IO.Path]::GetFullPath($ConsoleExecutable))
if($ReadWindowsMetadata){$taskArguments+='--windows-metadata'}
& (Join-Path $taskOutput 'DeviceLabelTests.exe') @taskArguments
if($LASTEXITCODE){throw 'Device-label regression failed'}
