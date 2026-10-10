param([switch]$Installer)
$ErrorActionPreference='Stop'
$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if(-not $installation){throw 'Visual Studio MSBuild is required.'}
$msbuild=Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
$project=Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\WILDX_console.csproj'
$generate=if($Installer){'true'}else{'false'}
& $msbuild $project /restore /nologo /v:minimal /p:Configuration=Release "/p:GenerateNsisInstaller=$generate"
if($LASTEXITCODE){throw 'WILDX build failed.'}
