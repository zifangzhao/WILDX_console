param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$SourceFile="$PSScriptRoot/USB512_interface/USB512_interface/NpLogger/NpDownloadStream.cs",[string]$OutputDirectory="$PSScriptRoot/_artifacts/download-speed-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
& "$PSScriptRoot/test_download_burst.ps1" -SourceFile $SourceFile -OutputDirectory (Join-Path $OutputDirectory 'transport')
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$test=Join-Path $OutputDirectory 'NpDownloadSpeedTests.exe'
& $compiler /nologo /warnaserror "/out:$test" "$PSScriptRoot/Tests/NpDownloadSpeedTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpProgressEstimate.cs"
if($LASTEXITCODE){throw 'Rate checks failed to compile'}
& $test ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'Built-app byte rate checks failed'}
$eta=Join-Path $OutputDirectory 'NpDownloadEtaTests.exe'
& $compiler /nologo /warnaserror "/out:$eta" "$PSScriptRoot/Tests/NpDownloadEtaTests.cs"
if($LASTEXITCODE){throw 'ETA checks failed to compile'}
& $eta ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'Unchanged ETA regression checks failed'}
