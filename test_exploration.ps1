param([string]$OutputDirectory="$PSScriptRoot/_artifacts/exploration_20261026/tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'NpExplorationTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /target:exe /optimize+ /r:System.Web.Extensions.dll "/out:$exe" "$PSScriptRoot/Tests/NpExplorationTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpExploration.cs"
if($LASTEXITCODE){throw 'Exploration fixture failed to compile'}
& $exe
if($LASTEXITCODE){throw 'Exploration orchestration tests failed'}
dotnet run --project "$PSScriptRoot/Neuropixels/NPConfigurator/Tests/ProbeBankScanTests.csproj" -c Release --no-launch-profile -- (Join-Path $OutputDirectory 'templates')
if($LASTEXITCODE){throw 'Probe bank routing tests failed'}

$probePlan=Join-Path $OutputDirectory 'helper-plan'
if(Test-Path $probePlan){$probePlan=Join-Path $OutputDirectory ('helper-plan-'+[guid]::NewGuid().ToString('N'))}
& "$PSScriptRoot/_artifacts/exploration_20261026/helper/NPConfigurator.exe" --scan-plan (Join-Path $OutputDirectory 'templates/Np2Multi.json') $probePlan 2
if($LASTEXITCODE){throw 'Actual helper scan-plan command failed'}
$shared=[IO.File]::ReadAllText("$PSScriptRoot/USB512_interface/USB512_interface/NeuropixelsPreviewForm.cs")
$a=$shared.IndexOf('    internal static class NpPreviewTheme');$b=$shared.IndexOf('    internal sealed class NpPreviewPalette',$a)
$form=[IO.File]::ReadAllText("$PSScriptRoot/USB512_interface/USB512_interface/mainForm.Exploration.cs")
$c=$form.IndexOf('    internal sealed class NpExplorationForm:Form')
$deps=Join-Path $OutputDirectory 'ui-dependencies.cs'
[IO.File]::WriteAllText($deps,'using System;using System.Drawing;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;namespace USB512_interface{'+$shared.Substring($a,$b-$a)+$form.Substring($c))
$ui=Join-Path $OutputDirectory 'NpExplorationUiTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /target:exe /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$ui" "$PSScriptRoot/Tests/NpExplorationUiTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpExploration.cs" "$PSScriptRoot/USB512_interface/USB512_interface/WildXTheme.cs" $deps
if($LASTEXITCODE){throw 'Exploration UI fixture failed to compile'}
& $ui $probePlan (Join-Path $OutputDirectory 'exploratory-scan-ui.png')
if($LASTEXITCODE){throw 'Exploration UI fixture failed'}
