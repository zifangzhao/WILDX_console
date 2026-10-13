param(
    [string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\universal-workflow-tests')
)
$ErrorActionPreference='Stop'
$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$testExecutable=Join-Path $OutputDirectory 'NpUniversalWorkflowTests.exe'
& $compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$testExecutable" (Join-Path $PSScriptRoot 'Tests\NpUniversalWorkflowTests.cs')
if($LASTEXITCODE){throw 'Universal workflow tests did not compile.'}
& $testExecutable $ConsoleExecutable $OutputDirectory $PSScriptRoot
if($LASTEXITCODE){throw 'Universal workflow regression failed.'}
