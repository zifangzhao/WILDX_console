param([string]$OutputDirectory="$PSScriptRoot/_artifacts/download-prefetch-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$test=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'NpDownloadPrefetchTests.exe'
& $compiler /nologo /optimize+ /warnaserror "/out:$test" "$PSScriptRoot/Tests/NpDownloadPrefetchTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpLogger/NpDownloadPrefetchStream.cs"
if($LASTEXITCODE){throw 'Prefetch checks did not compile'}
& $test
if($LASTEXITCODE){throw 'Prefetch checks failed'}
