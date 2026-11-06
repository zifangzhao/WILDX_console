param(
 [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
 [Parameter(Mandatory=$true)][string]$FirmwareVectors,
 [string]$StaleGrantVectors,
 [string]$OutputDirectory="$PSScriptRoot/_artifacts/v1_delete_last_host"
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOut=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /warnaserror /langversion:latest ("/out:"+(Join-Path $taskOut 'V1DeleteLastTests.exe')) (Join-Path $PSScriptRoot 'Tests/NpV1DeleteLastTests.cs')
if($LASTEXITCODE){throw 'V1 delete fixture compilation failed'}
$taskArgs=@([IO.Path]::GetFullPath($ConsoleExecutable),[IO.Path]::GetFullPath($FirmwareVectors))
if($StaleGrantVectors){$taskArgs+=[IO.Path]::GetFullPath($StaleGrantVectors)}
& (Join-Path $taskOut 'V1DeleteLastTests.exe') @taskArgs
if($LASTEXITCODE){throw 'V1 delete host regression failed'}
