param(
 [string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),
 [string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\recording-bundle-tests')
)
$ErrorActionPreference='Stop'
$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$fixtureDirectory=Join-Path $OutputDirectory 'sd-fixture'
& (Join-Path $PSScriptRoot 'Neuropixels\test_sd_dat.ps1') -Compiler $compiler -OutputDirectory $fixtureDirectory -CompileOnly
$executable=Join-Path $OutputDirectory 'NpRecordingBundleTests.exe'
& $compiler /nologo /optimize+ /r:System.Web.Extensions.dll "/out:$executable" (Join-Path $PSScriptRoot 'Tests\NpRecordingBundleTests.cs')
if($LASTEXITCODE){throw 'Recording bundle tests did not compile.'}
& $executable $ConsoleExecutable (Join-Path $OutputDirectory ('run-'+[Guid]::NewGuid().ToString('N'))) (Join-Path $fixtureDirectory 'NpV2SdDatExportTests.exe')
if($LASTEXITCODE){throw 'Recording bundle checks failed.'}
