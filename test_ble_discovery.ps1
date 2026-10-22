param(
    [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
    [string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-discovery-tests"
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$tests=Join-Path $OutputDirectory 'NpBleDiscoveryTests.exe'
& $compiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$tests" "$PSScriptRoot/Tests/NpBleDiscoveryTests.cs"
if($LASTEXITCODE){throw 'BLE discovery checks did not compile'}
& $tests ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'BLE discovery checks failed'}
