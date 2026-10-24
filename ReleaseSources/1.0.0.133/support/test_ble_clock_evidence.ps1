param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/ble_clock_evidence_20261023/tests131")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskTest=Join-Path $OutputDirectory 'NpBleClockEvidenceTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /r:System.Core.dll /r:System.Web.Extensions.dll "/out:$taskTest" "$PSScriptRoot/Tests/NpBleClockEvidenceTests.cs"
if($LASTEXITCODE){throw 'Clock evidence checks did not compile'}
& $taskTest ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'evidence')))
if($LASTEXITCODE){throw 'Clock evidence checks failed'}
