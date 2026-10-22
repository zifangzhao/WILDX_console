param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/v2-ble-list-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$exe=Join-Path $OutputDirectory 'NpV2BleRecordingListTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$exe" "$PSScriptRoot/Tests/NpV2BleRecordingListTests.cs"
if($LASTEXITCODE){throw 'BLE list test compilation failed'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'BLE list checks failed'}
