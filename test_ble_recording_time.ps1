param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-recording-time-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'NpBleRecordingTimeTests.exe'
& $compiler /nologo /warnaserror /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "$PSScriptRoot/Tests/NpBleRecordingTimeTests.cs"
if($LASTEXITCODE){throw 'BLE recording-time checks did not compile'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'BLE recording-time checks failed'}
