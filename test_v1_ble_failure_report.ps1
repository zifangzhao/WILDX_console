param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/v1-ble-failure-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'NpV1BleFailureReportTests.exe'
& $compiler /nologo /warnaserror /r:System.Core.dll /r:System.Windows.Forms.dll "/out:$exe" "$PSScriptRoot/Tests/NpV1BleFailureReportTests.cs"
if($LASTEXITCODE){throw 'V1 failure-report checks did not compile'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'V1 failure-report checks failed'}
