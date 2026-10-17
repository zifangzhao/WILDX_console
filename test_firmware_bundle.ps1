param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/firmware-bundle-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskExe=Join-Path $OutputDirectory 'NpFirmwareBundleTests.exe'
& $taskCompiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$taskExe" "$PSScriptRoot/Tests/NpFirmwareBundleTests.cs"
if($LASTEXITCODE){throw 'Firmware bundle tests did not compile'}
& $taskExe ([IO.Path]::GetFullPath($ConsoleExecutable)) "$PSScriptRoot/Firmware" ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Firmware bundle tests failed'}
