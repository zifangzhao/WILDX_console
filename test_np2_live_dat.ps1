param([string]$ConsoleExecutable="$PSScriptRoot\USB512_interface\USB512_interface\bin\Release\WILDX_console.exe",[string]$OutputDirectory="$PSScriptRoot\_artifacts\np2-live-dat-tests",[string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath;$Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'}
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$fixture=Join-Path $OutputDirectory 'wire-fixture'
& (Join-Path $PSScriptRoot 'Neuropixels\test_sd_dat.ps1') -Compiler $Compiler -OutputDirectory $fixture -CompileOnly
$exe=Join-Path $OutputDirectory 'Np2LiveDatTests.exe'
& $Compiler /nologo /optimize+ /r:System.Web.Extensions.dll "/out:$exe" (Join-Path $PSScriptRoot 'Tests\Np2LiveDatTests.cs')
if($LASTEXITCODE){throw 'Live DAT fixture did not compile'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) (Join-Path $OutputDirectory ('run-'+[Guid]::NewGuid().ToString('N'))) (Join-Path $fixture 'NpV2SdDatExportTests.exe')
if($LASTEXITCODE){throw 'Live DAT checks failed'}
