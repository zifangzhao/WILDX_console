param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/available-catalog-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'NpAvailableCatalogTests.exe'
& $compiler /nologo /warnaserror /r:System.Core.dll "/out:$exe" "$PSScriptRoot/Tests/NpAvailableCatalogTests.cs"
if($LASTEXITCODE){throw 'Catalog checks did not compile'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'Catalog checks failed'}
