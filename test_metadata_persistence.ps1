param([string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),[string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\metadata-persistence-tests'),[string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe',[switch]$LegacyReproducer)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$exe=Join-Path $OutputDirectory 'NpMetadataPersistenceTests.exe'
& $Compiler /nologo /warnaserror /target:exe /r:System.Core.dll /r:System.Web.Extensions.dll "/out:$exe" (Join-Path $PSScriptRoot 'Tests\NpMetadataPersistenceTests.cs')
if($LASTEXITCODE){throw 'Metadata persistence tests did not compile.'}
$arguments=@($ConsoleExecutable,$OutputDirectory)
if($LegacyReproducer){$arguments+='--legacy'}
& $exe @arguments
if($LASTEXITCODE){throw 'Metadata persistence regression failed.'}
