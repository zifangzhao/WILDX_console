param(
 [Parameter(Mandatory=$true)][string]$BaselineConsole,
 [Parameter(Mandatory=$true)][string]$SavedLaneDirectory,
 [string]$OutputDirectory="$PSScriptRoot/_artifacts/v1-download-performance"
)
$ErrorActionPreference='Stop'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$compiler=Join-Path $vs 'MSBuild/Current/Bin/Roslyn/csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpV1DownloadPerformanceTests.exe'
& $compiler /nologo /optimize+ /r:System.Web.Extensions.dll "/out:$exe" "$PSScriptRoot/Tests/NpV1DownloadPerformanceTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpLogger/NpCe64Decoder.cs"
if($LASTEXITCODE){throw 'Decoder differential fixture did not compile'}
& $exe ([IO.Path]::GetFullPath($BaselineConsole)) ([IO.Path]::GetFullPath($SavedLaneDirectory)) (Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'result.json')
if($LASTEXITCODE){throw 'Decoder differential fixture failed'}
