param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/probe-info-layout-tests",[string]$SdFixtures)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$tests=Join-Path $OutputDirectory 'NpProbeInfoLayoutTests.exe'
& $compiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll "/out:$tests" "$PSScriptRoot/Tests/NpProbeInfoLayoutTests.cs"
if($LASTEXITCODE){throw 'Probe info/layout checks did not compile'}
$arguments=@([IO.Path]::GetFullPath($ConsoleExecutable),[IO.Path]::GetFullPath((Join-Path $OutputDirectory 'results')))
if($SdFixtures){$arguments+=[IO.Path]::GetFullPath($SdFixtures)}
& $tests @arguments
if($LASTEXITCODE){throw 'Probe info/layout checks failed'}
